// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Redis-backed error-rate counter for multi-replica deployments (ADMIN-14).
/// Uses INCRBY on per-second time-bucketed keys with a sliding-window MGET read so
/// all replicas write to and read from a shared counter — the health panel tile
/// reflects the true cross-fleet error rate rather than only the local replica's count.
/// </summary>
/// <remarks>
/// Key schema: <c>gamekit:admin:errors:{epoch_bucket}</c> where
/// <c>epoch_bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / bucketWidthSeconds</c>.
/// TTL = <c>HealthErrorRateWindow + HealthErrorRateBucketSize</c> (defensive expiry).
/// Writes are fire-and-forget (never propagate exceptions). Reads return <c>-1</c> when
/// Redis is unavailable so callers can fall back to <see cref="ErrorRateRingBuffer"/>.
/// </remarks>
internal sealed class RedisErrorRateCounter : IRedisErrorRateCounter
{
    private readonly IConnectionMultiplexer _mux;
    private readonly long _bucketWidthSeconds;
    private readonly int _bucketCount;
    private readonly TimeSpan _keyTtl;

    /// <summary>
    /// Constructs the counter, deriving bucket geometry from the same
    /// <see cref="AdminPanelOptions"/> that <see cref="ErrorRateRingBuffer"/> uses.
    /// </summary>
    /// <param name="mux">Redis connection multiplexer.</param>
    /// <param name="opts">Admin options supplying the window + bucket size.</param>
    public RedisErrorRateCounter(IConnectionMultiplexer mux, GameKitAdminOptions opts)
    {
        ArgumentNullException.ThrowIfNull(mux);
        ArgumentNullException.ThrowIfNull(opts);
        _bucketWidthSeconds = (long)Math.Max(1, opts.Panel.HealthErrorRateBucketSize.TotalSeconds);
        _bucketCount = (int)Math.Ceiling(
            opts.Panel.HealthErrorRateWindow.TotalSeconds / _bucketWidthSeconds);
        _keyTtl = opts.Panel.HealthErrorRateWindow + opts.Panel.HealthErrorRateBucketSize;
        _mux = mux;
    }

    /// <inheritdoc />
    public void IncrementError()
    {
        _ = IncrementInternalAsync();  // discard Task — fire-and-forget
    }

    /// <inheritdoc />
    public async Task<long> RecentErrorCountAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _mux.GetDatabase();
            var nowBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _bucketWidthSeconds;
            var keys = new RedisKey[_bucketCount];
            for (var i = 0; i < _bucketCount; i++)
                keys[i] = $"gamekit:admin:errors:{nowBucket - (_bucketCount - 1 - i)}";
            var values = await db.StringGetAsync(keys).ConfigureAwait(false);
            var sum = 0L;
            foreach (var v in values)
                if (v.TryParse(out long n)) sum += n;
            return sum;
        }
        catch
        {
            return -1;  // sentinel: Redis unavailable — caller falls back to in-memory counter
        }
    }

    private async Task IncrementInternalAsync()
    {
        try
        {
            var db = _mux.GetDatabase();
            var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _bucketWidthSeconds;
            var key = (RedisKey)$"gamekit:admin:errors:{bucket}";
            await db.StringIncrementAsync(key).ConfigureAwait(false);
            // Always set TTL on each increment — safe fallback that avoids any SE.Redis 2.8.41
            // ExpireWhen API uncertainty (RESEARCH Pitfall 3 / Open Q 1). Minor overhead, correct.
            await db.KeyExpireAsync(key, _keyTtl).ConfigureAwait(false);
        }
        catch { /* swallow — Redis unavailable degrades to in-memory counter only */ }
    }
}
