// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Matchmaking.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IMatchmakerLease"/> — wraps <c>IDatabase.LockTakeAsync</c> +
/// <c>IDatabase.LockReleaseAsync</c> for the matchmaker leader-election lock at
/// <see cref="MatchmakingRedisKeys.MatcherLock"/>.
/// </summary>
/// <remarks>
/// <para>
/// Plan 05-07 ships this minimal helper so the
/// <see cref="MatchmakingReconcilerService"/> + <see cref="MatchmakingRetentionCleanupService"/>
/// can be leader-gated without a ProjectReference to a not-yet-existing Plan 05-05 type. If
/// 05-05's richer <c>MatchmakerLeaseHelper</c> (Polly v8 retry, lease renewal) lands, the
/// builder can swap it in via <c>services.Replace(...)</c> — both implement
/// <see cref="IMatchmakerLease"/>.
/// </para>
/// <para>
/// <b>Fencing-token:</b> <see cref="InstanceId"/> is computed once per process
/// (<c>MachineName:Guid</c>) and used as the Redis lock value. The
/// <c>IDatabase.LockReleaseAsync</c> Lua script verifies the value before deleting — this
/// instance can never release another instance's lock even after a temporary disconnect.
/// </para>
/// </remarks>
public sealed class RedisMatchmakerLease : IMatchmakerLease, ILeaderLease
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisMatchmakerLease> _logger;
    private readonly string _lockKey;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Fencing-token-grade unique id for this process instance (<c>MachineName:Guid</c>).
    /// Exposed for diagnostics and integration-test assertions.
    /// </summary>
    public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

    /// <summary>Constructs the helper.</summary>
    /// <param name="redis">Connection multiplexer.</param>
    /// <param name="options">Matchmaking options snapshot (ticker.LockKey + ticker.LockTtlSeconds).</param>
    /// <param name="logger">Logger.</param>
    public RedisMatchmakerLease(
        IConnectionMultiplexer redis,
        IOptions<GameKitMatchmakingOptions> options,
        ILogger<RedisMatchmakerLease> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _logger = logger;
        _lockKey = options.Value.Ticker.LockKey;
        _ttl = TimeSpan.FromSeconds(options.Value.Ticker.LockTtlSeconds);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.LockTakeAsync(_lockKey, InstanceId, _ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RedisMatchmakerLease: failed to acquire lease — treating as not-leader.");
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// This minimal implementation does not support lease renewal. Returns <c>false</c>
    /// unconditionally — callers must treat this as lease lost and stop processing.
    /// Use <c>MatchmakerLeaseHelper</c> (Polly v8) when renewal is required.
    /// </remarks>
    public Task<bool> RenewLeaseAsync(CancellationToken ct) => Task.FromResult(false);

    /// <inheritdoc />
    public async Task ReleaseLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.LockReleaseAsync(_lockKey, InstanceId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RedisMatchmakerLease: failed to release lease — lock will expire via TTL.");
        }
    }

    /// <summary>
    /// Single non-acquiring atomic read of the lock holder + remaining TTL. Returns the
    /// holder value (element 0) and PTTL in milliseconds (element 1) from the same point in
    /// time so the snapshot is never torn (WR-02). Does NOT take or modify the lock.
    /// </summary>
    private const string QueryLeaseScript =
        "return { redis.call('GET', KEYS[1]), redis.call('PTTL', KEYS[1]) }";

    /// <inheritdoc />
    public async Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            // Single atomic eval: holder + PTTL come from the same point in time, so a torn
            // holder/TTL snapshot (WR-02) is impossible. Non-acquiring (no LockTake).
            var result = (RedisResult[]?)await db
                .ScriptEvaluateAsync(QueryLeaseScript, new RedisKey[] { _lockKey })
                .ConfigureAwait(false);
            return ParseLeaseStatus(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RedisMatchmakerLease: QueryLeaseAsync — Redis unavailable.");
            return new LeaseStatus(null, null);
        }
    }

    /// <summary>
    /// Parses the <c>{ GET, PTTL }</c> Lua result into a <see cref="LeaseStatus"/>: element 0
    /// is the holder (null/empty =&gt; no holder), element 1 is PTTL in milliseconds
    /// (&lt;= 0 =&gt; no TTL, covering both the -1 "no expiry" and -2 "missing key" replies).
    /// </summary>
    private static LeaseStatus ParseLeaseStatus(RedisResult[]? result)
    {
        if (result is null || result.Length < 2)
            return new LeaseStatus(null, null);

        var holderRaw = (RedisValue)result[0];
        string? holder = holderRaw.HasValue && holderRaw.Length() > 0
            ? (string?)holderRaw
            : null;

        var pttlMs = (long)result[1];
        TimeSpan? ttl = pttlMs > 0 ? TimeSpan.FromMilliseconds(pttlMs) : null;

        return new LeaseStatus(holder, ttl);
    }
}
