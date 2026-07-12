// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Matchmaking.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IMatchmakingObservability"/> adapter — sources every field of
/// <see cref="MatchmakingQueueStats"/> directly from Redis. Uses
/// <c>IServer.Keys</c> (SCAN under the hood, NOT raw <c>KEYS</c> — Pitfall §1) to enumerate
/// populated queue keys, ZCARD per match in parallel via <c>Task.WhenAll</c>, and a single
/// GET on the matcher lock key to recover the current leader's fencing-token instance id.
/// </summary>
public sealed class RedisMatchmakingObservability : IMatchmakingObservability
{
    /// <summary>Glob pattern matching every populated queue key.</summary>
    public const string QueueKeyPattern = "mm:queue:*";

    private readonly IConnectionMultiplexer _redis;
    private readonly IClock _clock;
    private readonly ILogger<RedisMatchmakingObservability>? _logger;

    /// <summary>Constructs the adapter.</summary>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="clock">Authoritative UTC clock (for the snapshot's <c>AsOf</c> field).</param>
    /// <param name="logger">Optional logger.</param>
    public RedisMatchmakingObservability(
        IConnectionMultiplexer redis,
        IClock clock,
        ILogger<RedisMatchmakingObservability>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(clock);
        _redis = redis;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MatchmakingQueueStats> GetQueueStatsAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // 1. Resolve the lease holder via the matcher heartbeat key. The lock itself
        //    (MatchmakingRedisKeys.MatcherLock) is acquired-and-released per tick to
        //    coordinate with the reconciler + retention sweep, so a single point-read
        //    catches it ~0.4% of the time and would falsely report the matcher dead.
        //    The heartbeat is written each successful matcher tick with TTL=5× tick
        //    interval — present iff a matcher has ticked in the recent past.
        var leaseValue = await db.StringGetAsync(MatchmakingRedisKeys.MatcherHeartbeat).ConfigureAwait(false);
        var leaderInstanceId = leaseValue.IsNullOrEmpty ? null : (string?)leaseValue;
        var activeLease = leaseValue.IsNullOrEmpty ? 0 : 1;

        // 2. Enumerate queue keys via SCAN (StackExchange.Redis abstracts SCAN behind
        //    IServer.Keys; we explicitly avoid the raw KEYS command — Pitfall §1).
        var endpoints = _redis.GetEndPoints();
        if (endpoints.Length == 0)
        {
            return new MatchmakingQueueStats(
                Pools: Array.Empty<PoolDepth>(),
                ActiveLeaseCount: activeLease,
                LeaderInstanceId: leaderInstanceId,
                AsOf: _clock.UtcNow);
        }

        var server = _redis.GetServer(endpoints[0]);
        var queueKeys = new List<RedisKey>();
        foreach (var key in server.Keys(pattern: QueueKeyPattern, pageSize: 100))
        {
            ct.ThrowIfCancellationRequested();
            queueKeys.Add(key);
        }

        if (queueKeys.Count == 0)
        {
            return new MatchmakingQueueStats(
                Pools: Array.Empty<PoolDepth>(),
                ActiveLeaseCount: activeLease,
                LeaderInstanceId: leaderInstanceId,
                AsOf: _clock.UtcNow);
        }

        // 3. ZCARD per key in parallel — the multiplexer pipelines the commands automatically.
        var cardTasks = queueKeys.Select(k => db.SortedSetLengthAsync(k)).ToArray();
        var cards = await Task.WhenAll(cardTasks).ConfigureAwait(false);

        var pools = new List<PoolDepth>(queueKeys.Count);
        for (var i = 0; i < queueKeys.Count; i++)
        {
            if (TryParseQueueKey(queueKeys[i].ToString(), out var ladderId, out var poolName))
            {
                pools.Add(new PoolDepth(ladderId, poolName, cards[i]));
            }
            else
            {
                _logger?.LogWarning(
                    "RedisMatchmakingObservability: skipping malformed queue key '{Key}'.",
                    queueKeys[i].ToString());
            }
        }

        return new MatchmakingQueueStats(
            Pools: pools,
            ActiveLeaseCount: activeLease,
            LeaderInstanceId: leaderInstanceId,
            AsOf: _clock.UtcNow);
    }

    /// <summary>
    /// Parses a <c>mm:queue:{ladderId}:{poolName}</c> key into its components. The pool name
    /// MAY contain additional colons; everything after the third segment is treated as the
    /// pool name. Returns <see langword="false"/> on shape mismatch.
    /// </summary>
    internal static bool TryParseQueueKey(string key, out Guid ladderId, out string poolName)
    {
        ladderId = default;
        poolName = string.Empty;

        // Layout: mm:queue:{guid}:{pool}
        const string prefix = "mm:queue:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var rest = key.AsSpan(prefix.Length);
        var sep = rest.IndexOf(':');
        if (sep < 0)
            return false;

        var ladderSpan = rest[..sep];
        var poolSpan = rest[(sep + 1)..];
        if (!Guid.TryParse(ladderSpan, out ladderId))
            return false;

        poolName = poolSpan.ToString();
        return poolName.Length > 0;
    }
}
