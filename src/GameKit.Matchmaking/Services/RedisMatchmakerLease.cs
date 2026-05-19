// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
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
public sealed class RedisMatchmakerLease : IMatchmakerLease
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
}
