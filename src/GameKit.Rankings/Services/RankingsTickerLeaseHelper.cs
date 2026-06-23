// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace GameKit.Rankings.Services;

/// <summary>
/// Encapsulates <c>IDatabase.LockTake / LockExtend / LockRelease</c> with a Polly v8
/// resilience pipeline that retries transient Redis failures with decorrelated jitter
/// (D-03 / Pattern 5 / T-04-06-RD).
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>IDatabase.LockTakeAsync / LockExtendAsync / LockReleaseAsync</c> — the built-in
/// StackExchange.Redis wrapper that executes a Lua-script-verified release. Do NOT replace
/// with raw <c>StringSetAsync(k, v, ttl, When.NotExists)</c> (see "Don't Hand-Roll" §Redis).
/// </para>
/// <para>
/// The instance ID is unique per process (<c>MachineName:Guid</c>). This value is used as
/// the lock value; the Lua release script compares it before deleting, ensuring we never
/// release another instance's lock even after a temporary disconnection (T-04-06-DD).
/// </para>
/// <para>
/// <b>Pitfall 6:</b> <see cref="RenewLeaseAsync"/> returns <c>false</c> when the lock has
/// already expired. The caller (<see cref="RankingsTickerService"/>) MUST check the return
/// value and bail out of the current iteration when it is <c>false</c>.
/// </para>
/// </remarks>
public sealed class RankingsTickerLeaseHelper : ILeaderLease
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RankingsTickerLeaseHelper> _logger;
    private readonly GameKitRankingsOptions _opts;
    private readonly ResiliencePipeline _polly;

    /// <summary>
    /// Unique fencing token for this process instance. Format: <c>MachineName:Guid</c>.
    /// Exposed for diagnostics and test assertions.
    /// </summary>
    public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

    /// <summary>
    /// Constructs the lease helper and builds the Polly resilience pipeline.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="logger">Logger for Polly retry diagnostics.</param>
    /// <param name="opts">Rankings options snapshot providing <c>Ticker.LockKey</c> and <c>Ticker.LockTtlSeconds</c>.</param>
    public RankingsTickerLeaseHelper(
        IConnectionMultiplexer redis,
        ILogger<RankingsTickerLeaseHelper> logger,
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
                        "RankingsTickerLeaseHelper: Redis retry {Attempt} after {Delay}ms.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// Attempts to acquire the distributed leader-election lock.
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
                "RankingsTickerLeaseHelper: failed to acquire lease after retries — treating as LockNotAcquired.");
            return false;
        }
    }

    /// <summary>
    /// Attempts to extend (renew) the distributed lock TTL mid-tick.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the lock was successfully extended; <c>false</c> if the lock expired
    /// before renewal. <b>Pitfall 6:</b> callers MUST check this return value — a <c>false</c>
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
                "RankingsTickerLeaseHelper: failed to extend lease — treating as lease lost.");
            return false;
        }
    }

    /// <summary>
    /// Releases the distributed lock (Lua-script-verified — safe even if the lock has expired).
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
                "RankingsTickerLeaseHelper: failed to release lease — lock will expire via TTL.");
        }
    }

    /// <summary>
    /// Single non-acquiring atomic read of the ticker lock holder + remaining TTL. Returns the
    /// holder value (element 0) and PTTL in milliseconds (element 1) from the same point in
    /// time so the snapshot is never torn. Does NOT take or modify the lock.
    /// </summary>
    private const string QueryLeaseScript =
        "return { redis.call('GET', KEYS[1]), redis.call('PTTL', KEYS[1]) }";

    /// <inheritdoc />
    public async Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var result = (RedisResult[]?)await db
                .ScriptEvaluateAsync(QueryLeaseScript, new RedisKey[] { _opts.Ticker.LockKey })
                .ConfigureAwait(false);
            return ParseLeaseStatus(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RankingsTickerLeaseHelper: QueryLeaseAsync — Redis unavailable.");
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
