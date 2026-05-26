// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Presence.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Presence.Services;

/// <summary>
/// Redis-backed implementation of BOTH the read-side
/// <see cref="IPresenceProvider"/> port (from <c>GameKit.Core</c>) and the write-side
/// <see cref="IPresenceWriter"/> port (Presence-internal). Single class, two interfaces —
/// the DI container registers one Singleton instance against both service types so the
/// heartbeat endpoint, the session-lifecycle observer, and any read-side panel all share
/// the same Redis connection-multiplexer-backed instance.
/// </summary>
/// <remarks>
/// <para>
/// CONTEXT D-04 — a single Redis key per player (<c>presence:{playerId}</c>) with
/// last-write-wins semantics across multiple devices. PrefixOnline / PrefixInMatch are
/// the values stored at that key (not key prefixes); see <see cref="PresenceValues"/> +
/// <see cref="PresenceRedisKeys"/>.
/// </para>
/// <para>
/// PATTERNS warning #6 — <see cref="WriteHeartbeatAsync"/> MUST NOT downgrade an
/// existing <c>in_match</c> value to <c>online</c>. The atomic Lua script below performs
/// GET → conditional SET in a single Redis round-trip so a concurrent
/// <see cref="WriteInMatchAsync"/> from the session-lifecycle observer cannot race the
/// heartbeat into corrupting the in-match marker (CONTEXT D-03).
/// </para>
/// <para>
/// <see cref="GetOnlinePlayerIdsAsync"/> uses <see cref="IServer.KeysAsync"/> (SCAN-based,
/// async, paged) — NEVER the synchronous <c>Keys()</c> primitive (RESEARCH anti-pattern
/// line 872). SCAN cursors are streamed lazily so the cap respected by the <c>take</c>
/// parameter terminates the enumeration without exhausting the cursor.
/// </para>
/// </remarks>
internal sealed class RedisPresenceProvider : IPresenceProvider, IPresenceWriter
{
    // Verbatim Lua script per PATTERNS Block 2 lines 236-244 / RESEARCH Pattern 1
    // §CRITICAL precedence rule. The script:
    //   1. GETs the current value of the player presence key.
    //   2. If the value is 'in_match', refresh the TTL only (PEXPIRE — do NOT overwrite).
    //   3. Otherwise SET the value to 'online' with the supplied TTL (PX milliseconds).
    // The script body is asserted character-for-character in the unit tests so any
    // accidental edit that breaks the precedence rule fails CI.
    internal const string HeartbeatLuaScript =
        "local v = redis.call('GET', KEYS[1])\n" +
        "if v == 'in_match' then\n" +
        "  redis.call('PEXPIRE', KEYS[1], ARGV[1])\n" +
        "else\n" +
        "  redis.call('SET', KEYS[1], 'online', 'PX', ARGV[1])\n" +
        "end\n" +
        "return 1";

    private readonly IConnectionMultiplexer _redis;
    private readonly GameKitPresenceOptions _options;

    /// <summary>
    /// Constructs the Redis-backed presence provider.
    /// </summary>
    /// <param name="redis">The shared Redis connection multiplexer (Singleton).</param>
    /// <param name="options">The Presence options (TTL + heartbeat cadence).</param>
    public RedisPresenceProvider(IConnectionMultiplexer redis, IOptions<GameKitPresenceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(options);
        _redis = redis;
        _options = options.Value;
    }

    private TimeSpan Ttl => TimeSpan.FromSeconds(_options.TtlSeconds);

    // ---- Read path (IPresenceProvider) ----

    /// <inheritdoc />
    public async ValueTask<PresenceStatus> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(PresenceRedisKeys.Player(playerId)).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
        {
            return PresenceStatus.Offline;
        }

        var str = (string?)value;
        return str switch
        {
            PresenceValues.InMatch => PresenceStatus.InMatch,
            PresenceValues.Online => PresenceStatus.Online,
            // Defensive: an unexpected value (key shape drift, manual operator probe) is
            // treated as Offline — the safer default for the admin UI's read path.
            _ => PresenceStatus.Offline,
        };
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Guid>> GetOnlinePlayerIdsAsync(int take, CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return Array.Empty<Guid>();
        }

        // SCAN-based async enumeration — never the synchronous Keys() primitive
        // (RESEARCH §Pitfall anti-pattern line 872).
        var endPoints = _redis.GetEndPoints();
        if (endPoints.Length == 0)
        {
            return Array.Empty<Guid>();
        }

        var server = _redis.GetServer(endPoints[0]);
        var results = new List<Guid>(capacity: Math.Min(take, 256));

        await foreach (var key in server.KeysAsync(pattern: PresenceRedisKeys.ScanPattern, pageSize: 250)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var suffix = ExtractPlayerSuffix(key);
            if (suffix is not null && Guid.TryParse(suffix, out var playerId))
            {
                results.Add(playerId);
                if (results.Count >= take)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static string? ExtractPlayerSuffix(RedisKey key)
    {
        var str = (string?)key;
        if (str is null)
        {
            return null;
        }
        // PresenceRedisKeys.Player => "presence:{guid}"; bare ASCII colon split is sufficient.
        var idx = str.IndexOf(':');
        if (idx < 0 || idx == str.Length - 1)
        {
            return null;
        }
        return str.Substring(idx + 1);
    }

    // ---- Write path (IPresenceWriter) ----

    /// <inheritdoc />
    public async ValueTask WriteHeartbeatAsync(Guid playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var key = (RedisKey)PresenceRedisKeys.Player(playerId);
        var ttlMs = (long)Ttl.TotalMilliseconds;
        // Atomic Lua: PATTERNS warning #6 — never use plain StringSetAsync here.
        await db.ScriptEvaluateAsync(
            HeartbeatLuaScript,
            new[] { key },
            new RedisValue[] { ttlMs }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WriteInMatchAsync(Guid playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        // Game-server authoritative — plain SET PX is correct here (no precedence check
        // needed because the caller is trusted).
        await db.StringSetAsync(
            PresenceRedisKeys.Player(playerId),
            PresenceValues.InMatch,
            expiry: Ttl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WriteOnlineAsync(Guid playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.StringSetAsync(
            PresenceRedisKeys.Player(playerId),
            PresenceValues.Online,
            expiry: Ttl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask ClearInMatchAsync(Guid playerId, CancellationToken ct)
    {
        // ClearInMatchAsync semantically transitions in_match → online with TTL refresh.
        // Implementation is identical to WriteOnlineAsync; kept as a distinct method
        // so the call-site at PresenceSessionObserver.OnSessionAbandonedAsync remains
        // self-documenting.
        return WriteOnlineAsync(playerId, ct);
    }
}
