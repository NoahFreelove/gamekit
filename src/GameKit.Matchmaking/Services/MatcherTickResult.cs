// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Outcome of a single <see cref="IMatchmakerTicker.RunOnceAsync"/> iteration. Distinguishes
/// the five terminal states the matchmaker ticker can reach in one tick. Test code (especially
/// the SC#4 phase-gate <c>MatchmakingLeaderElectionTests</c>) asserts on the precise value to
/// confirm exactly-one-leader semantics across two racing replicas.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the shape of <c>GameKit.Rankings.Services.TickResult</c> but adds
/// <see cref="LeaseLost"/> — a state Rankings never reaches because Rankings does not use
/// a fencing token. Matchmaking uses the Lua atomic-claim script (Plan 05-04) whose first
/// step is a fencing-token check; if the leader's lease expired between RenewLease and EVAL,
/// the script returns <c>LEASE_LOST</c> and the ticker propagates the loss up via this enum.
/// </para>
/// <para>
/// Integer values are pinned so binary serialization (e.g. OTel tags, audit rows) is stable.
/// </para>
/// </remarks>
public enum MatcherTickResult
{
    /// <summary>
    /// The ticker scanned every pool and no match could be formed in this tick. Normal idle
    /// state — the matcher will retry on the next <c>PeriodicTimer</c> interval.
    /// </summary>
    NoMatch = 0,

    /// <summary>
    /// At least one match proposal was formed and written to Redis via the atomic-claim
    /// script. The accept-step lifecycle continues asynchronously from this point.
    /// </summary>
    Matched = 1,

    /// <summary>
    /// The Redis distributed lock was not acquired (another replica holds it). This instance
    /// skips the current tick and waits for the next interval. Counterpart to
    /// <c>TickResult.LockNotAcquired</c> in the Rankings ticker.
    /// </summary>
    LockNotAcquired = 2,

    /// <summary>
    /// The leader's lease was lost mid-tick — either the Redis lock expired before
    /// <see cref="MatchmakerLeaseHelper.RenewLeaseAsync"/> could renew it, OR the Lua
    /// atomic-claim script returned <c>LEASE_LOST</c> due to a fencing-token mismatch.
    /// The ticker bails out of the current iteration; the new leader picks up on the next
    /// tick (Pitfall §2 + Pitfall §6).
    /// </summary>
    LeaseLost = 3,

    /// <summary>
    /// A non-transient Redis error occurred (e.g. a connection drop that the Polly retry
    /// pipeline exhausted). Logged as a warning; the ticker continues on the next interval.
    /// </summary>
    RedisUnavailable = 4,
}
