// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Services;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Leader-gate contract for matchmaker background services — alias-forward of
/// <see cref="ILeaderLease"/> scoped to the matchmaker subsystem. All existing DI
/// registrations, reconciler, and retention-sweep references continue to compile
/// unchanged — they resolve <c>IMatchmakerLease</c> and receive a concrete helper that
/// satisfies both this interface and <see cref="ILeaderLease"/> (SCALE-01).
/// </summary>
/// <remarks>
/// <para>
/// <b>Wave-3 ordering note:</b> the concrete <c>MatchmakerLeaseHelper</c> (Polly v8 +
/// <c>IDatabase.LockTake / LockExtend / LockRelease</c>) ships with Plan 05-05. Plan 05-07
/// introduced the original interface; Phase 16 (SCALE-01) extended it to also satisfy
/// <see cref="ILeaderLease"/> from <c>GameKit.Core.Services</c>, providing a single
/// auditable lease surface across all packages.
/// </para>
/// <para>
/// <b>Why a separate instance per service?</b> Each background service tracks its own
/// fencing-token <c>InstanceId</c>. The ticker, reconciler, and retention services therefore
/// hold different leases against the same lock key — they are sequenced through the same
/// Redis lock but distinguished by instance id in the audit trail.
/// </para>
/// </remarks>
public interface IMatchmakerLease : ILeaderLease { }
