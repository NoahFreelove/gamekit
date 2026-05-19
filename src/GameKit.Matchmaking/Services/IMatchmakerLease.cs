// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Leader-gate contract for matchmaker background services. Tries to acquire / release a
/// single shared distributed lock (the matchmaker key from
/// <see cref="GameKit.Matchmaking.Redis.MatchmakingRedisKeys.MatcherLock"/>) so that
/// reconciler and retention sweeps run on only one replica at a time
/// (RESEARCH §Decision 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Wave-3 ordering note:</b> the concrete <c>MatchmakerLeaseHelper</c> (Polly v8 +
/// <c>IDatabase.LockTake / LockExtend / LockRelease</c>) ships with Plan 05-05. Plan 05-07
/// (this file) introduces the interface so the reconciler + retention services can depend
/// on it without depending on the concrete type from 05-05; the integration tests stub
/// this interface directly (always-leader or never-leader). After 05-05 lands, its
/// <c>MatchmakerLeaseHelper</c> implements this interface and the same instance backs
/// both the ticker (05-05) and these sweeps (05-07).
/// </para>
/// <para>
/// <b>Why a separate instance per service?</b> Each background service tracks its own
/// fencing-token <c>InstanceId</c>. The ticker, reconciler, and retention services therefore
/// hold different leases against the same lock key — they are sequenced through the same
/// Redis lock but distinguished by instance id in the audit trail.
/// </para>
/// </remarks>
public interface IMatchmakerLease
{
    /// <summary>
    /// Attempts to acquire the shared matchmaker leader-election lock. Returns <c>true</c>
    /// when this caller is now the leader for at least <c>LockTtlSeconds</c>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> TryAcquireLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Releases the lock. Lua-script-verified — safe to call even if the lock already expired
    /// or was taken over by another instance.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ReleaseLeaseAsync(CancellationToken ct);
}
