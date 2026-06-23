// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// Common abstraction for distributed leader-election leases backed by Redis
/// SET NX PX. Implemented by per-package lease helpers; registered in DI as the
/// concrete type (not this interface) per-package — consumers resolve the concrete
/// helper, which also satisfies this interface for health checks and auditing.
/// </summary>
/// <remarks>
/// All four lease helpers (<c>MatchmakerLeaseHelper</c>, <c>RedisMatchmakerLease</c>,
/// <c>RankDecayLeaseHelper</c>, <c>RankingsTickerLeaseHelper</c>) implement this interface.
/// The SCALE-01 invariant: every <c>LockTakeAsync</c> call in <c>src/</c> is inside a class
/// that implements <see cref="ILeaderLease"/>. Health checks and auditing tools can
/// enumerate <see cref="ILeaderLease"/> instances via DI or reflection without taking
/// per-package references.
/// </remarks>
public interface ILeaderLease
{
    /// <summary>
    /// Fencing-token-grade unique id for this process instance (<c>MachineName:Guid</c>).
    /// Passed as the lock value so that Lua-script-verified release can guarantee
    /// this instance never deletes another instance's lock after a temporary disconnect.
    /// </summary>
    string InstanceId { get; }

    /// <summary>
    /// Attempts to acquire the leader lock. Returns <c>true</c> if this caller is now the
    /// leader for at least the configured lock TTL.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> TryAcquireLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Extends the lock TTL mid-run. Returns <c>false</c> when the lease has expired before
    /// renewal — the caller MUST stop processing when this returns <c>false</c>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> RenewLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Releases the lock. Lua-script-verified — safe even if the lock has expired or
    /// was taken over by another instance. On shutdown paths the caller must invoke this
    /// with a non-cancelling token so the release survives after the host stopping signal
    /// has already fired.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ReleaseLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Non-acquiring read of the current lock holder and remaining TTL. Used by health
    /// checks to report leader identity without contending for the lock.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct);
}
