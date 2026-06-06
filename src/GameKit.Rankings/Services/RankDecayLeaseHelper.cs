// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace GameKit.Rankings.Services;

/// <summary>
/// Encapsulates <c>IDatabase.LockTake / LockExtend / LockRelease</c> with a Polly v8
/// resilience pipeline that retries transient Redis failures with decorrelated jitter
/// for the rank-decay background service (RANK-15).
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>IDatabase.LockTakeAsync / LockExtendAsync / LockReleaseAsync</c> — the built-in
/// StackExchange.Redis wrapper that executes a Lua-script-verified release. Do NOT replace
/// with raw <c>StringSetAsync(k, v, ttl, When.NotExists)</c>.
/// </para>
/// <para>
/// The instance ID is unique per process (<c>MachineName:Guid</c>). This value is used as
/// the lock value; the Lua release script compares it before deleting, ensuring we never
/// release another instance's lock even after a temporary disconnection.
/// </para>
/// <para>
/// The lock key is <c>gamekit:rankings:decay:lease</c> (configurable via
/// <see cref="GameKitRankingsDecayOptions.LockKey"/>), which is DISTINCT from the ticker
/// lease key (<c>gamekit:rankings:ticker:lease</c>) so decay and ticker never mutually
/// exclude each other (RANK-15 / Pitfall 4 from RESEARCH.md).
/// </para>
/// </remarks>
public sealed class RankDecayLeaseHelper
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RankDecayLeaseHelper> _logger;
    private readonly GameKitRankingsOptions _opts;
    private readonly ResiliencePipeline _polly;

    /// <summary>
    /// Unique fencing token for this process instance. Format: <c>MachineName:Guid</c>.
    /// Exposed for diagnostics and test assertions.
    /// </summary>
    public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

    /// <summary>
    /// Constructs the decay lease helper and builds the Polly resilience pipeline.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="logger">Logger for Polly retry diagnostics.</param>
    /// <param name="opts">Rankings options snapshot providing <c>Decay.LockKey</c> and <c>Decay.LockTtlSeconds</c>.</param>
    public RankDecayLeaseHelper(
        IConnectionMultiplexer redis,
        ILogger<RankDecayLeaseHelper> logger,
        IOptions<GameKitRankingsOptions> opts)
    {
        _redis = redis;
        _logger = logger;
        _opts = opts.Value;

        // Polly v8 pipeline: 3 retries, exponential backoff, decorrelated jitter,
        // only on transient Redis connection / timeout exceptions (D-03).
        _polly = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisConnectionException>()
                    .Handle<RedisTimeoutException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "RankDecayLeaseHelper: Redis retry {Attempt} after {Delay}ms.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// Attempts to acquire the distributed leader-election lock for the decay runner.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the lock was acquired; <c>false</c> if another instance holds it or
    /// all Polly retries were exhausted.
    /// </returns>
    public async Task<bool> TryAcquireLeaseAsync(CancellationToken ct)
    {
        try
        {
            return await _polly.ExecuteAsync(
                async token =>
                {
                    var db = _redis.GetDatabase();
                    return await db.LockTakeAsync(
                        _opts.Decay.LockKey,
                        InstanceId,
                        TimeSpan.FromSeconds(_opts.Decay.LockTtlSeconds))
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RankDecayLeaseHelper: failed to acquire lease after retries — treating as LockNotAcquired.");
            return false;
        }
    }

    /// <summary>
    /// Attempts to extend (renew) the distributed decay lock TTL mid-run.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the lock was successfully extended; <c>false</c> if the lock expired
    /// before renewal. Callers MUST check this return value — a <c>false</c>
    /// result means this instance no longer holds the lock and MUST stop processing.
    /// </returns>
    public async Task<bool> RenewLeaseAsync(CancellationToken ct)
    {
        try
        {
            return await _polly.ExecuteAsync(
                async token =>
                {
                    var db = _redis.GetDatabase();
                    return await db.LockExtendAsync(
                        _opts.Decay.LockKey,
                        InstanceId,
                        TimeSpan.FromSeconds(_opts.Decay.LockTtlSeconds))
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RankDecayLeaseHelper: failed to extend lease — treating as lease lost.");
            return false;
        }
    }

    /// <summary>
    /// Releases the distributed decay lock (Lua-script-verified — safe even if the lock has expired).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ReleaseLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.LockReleaseAsync(_opts.Decay.LockKey, InstanceId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Release failures are non-fatal: the TTL will expire naturally. Log as warning.
            _logger.LogWarning(ex,
                "RankDecayLeaseHelper: failed to release lease — lock will expire via TTL.");
        }
    }
}
