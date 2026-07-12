// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Snapshot of live matchmaking-queue telemetry used by the admin queue-depth panel (MATCH-14).
/// Sourced directly from Redis (ZCARD per pool + GET on the leader-lock key) — NEVER from the
/// Postgres reconciliation mirrors. Phase 5 Plan 05-08 RESEARCH §Decision 11.
/// </summary>
/// <param name="Pools">Per-pool depth rows. Order is not guaranteed (Redis SCAN order is implementation-defined).</param>
/// <param name="ActiveLeaseCount">
/// 0 or 1. 1 indicates the matcher lock key is currently held; 0 indicates no replica owns the lock
/// (between TTL expirations or during planned drain). Higher numbers are impossible by construction —
/// the lock is a single Redis key.
/// </param>
/// <param name="LeaderInstanceId">
/// The current lease holder's instance id (the fencing-token value inside the lock key), or
/// <see langword="null"/> when no replica currently owns the lock.
/// </param>
/// <param name="AsOf">UTC timestamp at which the snapshot was captured.</param>
public sealed record MatchmakingQueueStats(
    IReadOnlyList<PoolDepth> Pools,
    int ActiveLeaseCount,
    string? LeaderInstanceId,
    DateTimeOffset AsOf);

/// <summary>
/// Per-pool depth row inside a <see cref="MatchmakingQueueStats"/> snapshot.
/// </summary>
/// <param name="LadderId">Ladder identifier parsed from the Redis queue key.</param>
/// <param name="PoolName">Pool name parsed from the Redis queue key.</param>
/// <param name="Depth">Number of tickets currently in this pool (ZCARD).</param>
public sealed record PoolDepth(Guid LadderId, string PoolName, long Depth);
