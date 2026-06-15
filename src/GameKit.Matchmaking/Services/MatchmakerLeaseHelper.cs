// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Encapsulates <c>IDatabase.LockTakeAsync / LockExtendAsync / LockReleaseAsync</c> with a
/// Polly v8 resilience pipeline that retries transient Redis failures with decorrelated
/// jitter. The matchmaker analog of
/// <c>GameKit.Rankings.Services.RankingsTickerLeaseHelper</c> (Phase 4 / D-03 / Pattern 5).
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>IDatabase.LockTakeAsync / LockExtendAsync / LockReleaseAsync</c> — the built-in
/// StackExchange.Redis wrapper that executes a Lua-script-verified release. Do NOT replace
/// with raw <c>StringSetAsync(k, v, ttl, When.NotExists)</c>: the Lua-script release path is
/// the fencing-token guard that prevents this instance from ever deleting another instance's
/// lock after a temporary disconnect (see <see cref="MatchmakingRedisKeys.MatcherLock"/>).
/// </para>
/// <para>
/// <b>InstanceId:</b> unique per process (<c>MachineName:Guid</c>). Used as the Redis lock
/// value; the Lua release script compares it before deleting. The Plan 05-04
/// <c>AtomicClaimScript</c> ALSO compares this value as its first fencing-token step — so the
/// ticker MUST pass <see cref="InstanceId"/> as the <c>leaseValue</c> when invoking the
/// atomic-claim script (Pitfall §2).
/// </para>
/// <para>
/// <b>Pitfall §6 (renew-or-bail):</b> <see cref="RenewLeaseAsync"/> returns <c>false</c>
/// when the lock has already expired. The caller (<c>MatchmakerTickerService</c>)
/// MUST check the return value and bail out of the current iteration when it is <c>false</c>.
/// Otherwise the ticker would continue processing pools after another replica has taken
/// leadership, producing double-match races (T-05-05-01).
/// </para>
/// <para>
/// <b>IMatchmakerLease implementation:</b> this class implements
/// <see cref="IMatchmakerLease"/> so that the unified Plan 05-07 reconciler / retention
/// builder registration (<c>MatchmakingBuilderExtensions.Background.AddBackgroundServices</c>)
/// can replace its minimal <c>RedisMatchmakerLease</c> default with this richer Polly-wrapped
/// helper. The ticker (Plan 05-05) and sweeps (Plan 05-07) then share a single fencing-token
/// instance keyed on the same Redis lock — preventing the orchestrator-merge ambiguity flagged
/// in Plan 05-07 SUMMARY §Wave-3 Parallel-Plan Coordination Notes.
/// </para>
/// </remarks>
public sealed class MatchmakerLeaseHelper : IMatchmakerLease
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MatchmakerLeaseHelper> _logger;
    private readonly GameKitMatchmakingOptions _opts;
    private readonly ResiliencePipeline _polly;

    /// <summary>
    /// Unique fencing token for this process instance. Format: <c>MachineName:Guid</c>.
    /// Passed as <c>leaseValue</c> to <c>AtomicClaimScript.ExecuteAsync</c> so the Lua script
    /// can verify the leader before any write. Exposed for diagnostics + test assertions
    /// (the SC#4 phase-gate test reads <c>StringGetAsync(MatcherLock)</c> after failover
    /// and asserts the value equals the new leader's <see cref="InstanceId"/>).
    /// </summary>
    public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

    /// <summary>Constructs the lease helper and builds the Polly resilience pipeline.</summary>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="logger">Logger for Polly retry diagnostics.</param>
    /// <param name="options">Matchmaking options snapshot providing <c>Ticker.LockKey</c> and <c>Ticker.LockTtlSeconds</c>.</param>
    public MatchmakerLeaseHelper(
        IConnectionMultiplexer redis,
        ILogger<MatchmakerLeaseHelper> logger,
        IOptions<GameKitMatchmakingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _redis = redis;
        _logger = logger;
        _opts = options.Value;

        // Polly v8 pipeline: 3 retries, exponential backoff with decorrelated jitter,
        // only on transient Redis connection / timeout exceptions. Mirrors Phase 4 D-03
        // and the Plan 05-05 must_haves ("Polly v8 retry pipeline").
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
                        "MatchmakerLeaseHelper: Redis retry {Attempt} after {Delay}ms.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// Attempts to acquire the matchmaker leader-election lock at
    /// <see cref="MatchmakingRedisKeys.MatcherLock"/> (default — overridable via
    /// <c>GameKitMatchmakingTickerOptions.LockKey</c>).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the lock was acquired; <c>false</c> if another replica holds it or all
    /// Polly retries were exhausted.
    /// </returns>
    public async Task<bool> TryAcquireLeaseAsync(CancellationToken ct)
    {
        try
        {
            return await _polly.ExecuteAsync(
                async _ =>
                {
                    var db = _redis.GetDatabase();
                    return await db.LockTakeAsync(
                        _opts.Ticker.LockKey,
                        InstanceId,
                        TimeSpan.FromSeconds(_opts.Ticker.LockTtlSeconds))
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MatchmakerLeaseHelper: failed to acquire lease after retries — treating as LockNotAcquired.");
            return false;
        }
    }

    /// <summary>
    /// Attempts to extend (renew) the distributed lock TTL mid-tick.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the lock was successfully extended; <c>false</c> if the lock expired
    /// before renewal. <b>Pitfall §6:</b> callers MUST check this return value — a <c>false</c>
    /// result means this instance no longer holds the lock and MUST stop processing.
    /// </returns>
    public async Task<bool> RenewLeaseAsync(CancellationToken ct)
    {
        try
        {
            return await _polly.ExecuteAsync(
                async _ =>
                {
                    var db = _redis.GetDatabase();
                    return await db.LockExtendAsync(
                        _opts.Ticker.LockKey,
                        InstanceId,
                        TimeSpan.FromSeconds(_opts.Ticker.LockTtlSeconds))
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MatchmakerLeaseHelper: failed to extend lease — treating as lease lost.");
            return false;
        }
    }

    /// <summary>
    /// Releases the distributed lock (Lua-script-verified — safe even if the lock has
    /// expired or has been taken over by another instance).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ReleaseLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.LockReleaseAsync(_opts.Ticker.LockKey, InstanceId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Release failures are non-fatal: the TTL will expire naturally. Log as warning.
            _logger.LogWarning(ex,
                "MatchmakerLeaseHelper: failed to release lease — lock will expire via TTL.");
        }
    }

    /// <inheritdoc />
    public async Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var holder = await db.LockQueryAsync(_opts.Ticker.LockKey).ConfigureAwait(false);
            var ttl    = await db.KeyTimeToLiveAsync(_opts.Ticker.LockKey).ConfigureAwait(false);
            return new LeaseStatus(
                holder.HasValue ? (string?)holder : null,
                ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MatchmakerLeaseHelper: QueryLeaseAsync — Redis unavailable.");
            return new LeaseStatus(null, null);
        }
    }
}
