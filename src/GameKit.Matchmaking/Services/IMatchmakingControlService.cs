// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Admin control surface for matchmaking — sets the per-ladder pause / drain flags in
/// Redis and writes the corresponding audit row in one transaction-shaped operation.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>MatchmakingAdminEndpoints</c> in Phase 5 UAT-2 D1 so the
/// Phase 03.1 admin chrome (PauseQueueDialog / DrainQueueDialog) can invoke the
/// behaviour via DI rather than via HTTP loopback. The minimal-API endpoints continue
/// to exist for programmatic SPA / CLI clients and now delegate to this service.
/// </para>
/// <para>
/// Both methods take an explicit <c>actorId</c> rather than reaching into
/// <c>HttpContext</c> so the same service is callable from a Blazor Server interactive
/// circuit (which has no live HttpContext) and from a minimal-API endpoint handler.
/// </para>
/// </remarks>
public interface IMatchmakingControlService
{
    /// <summary>Sets the per-ladder pause flag and writes <c>admin.matchmaking.pause_queue</c> audit.</summary>
    /// <param name="ladderId">Target ladder.</param>
    /// <param name="reason">Operator-supplied rationale (stored as the Redis value and the audit reason).</param>
    /// <param name="actorId">Admin id that triggered the action (audit actor).</param>
    /// <param name="ct">Cancellation token.</param>
    Task PauseAsync(Guid ladderId, string reason, Guid actorId, CancellationToken ct);

    /// <summary>Sets the per-ladder drain flag and writes <c>admin.matchmaking.drain_queue</c> audit.</summary>
    /// <param name="ladderId">Target ladder.</param>
    /// <param name="reason">Operator-supplied rationale.</param>
    /// <param name="actorId">Admin id that triggered the action.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DrainAsync(Guid ladderId, string reason, Guid actorId, CancellationToken ct);
}
