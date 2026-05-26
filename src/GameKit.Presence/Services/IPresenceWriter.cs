// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Presence.Services;

/// <summary>
/// Write-side port for the Presence subsystem. Implemented by <c>RedisPresenceProvider</c>
/// in this package; consumed by the heartbeat HTTP endpoint and by
/// <c>PresenceSessionObserver</c> (the <see cref="GameKit.Core.Services.ISessionLifecycleObserver"/>
/// adapter that fans session-lifecycle events out to presence writes).
/// </summary>
/// <remarks>
/// <para>
/// Kept Presence-internal — <c>GameKit.Core</c> deliberately does NOT take a dep on the
/// write surface. Core defines only the read-side <see cref="GameKit.Core.Services.IPresenceProvider"/>
/// port so other packages can render presence panels / queries without coupling to the
/// Redis-backed write path. Plan 06-04 introduces this port.
/// </para>
/// <para>
/// All four methods are idempotent. <see cref="WriteHeartbeatAsync"/> additionally enforces
/// the in-match precedence rule (CONTEXT D-03 + PATTERNS warning #6) via an atomic Lua
/// script — see <c>RedisPresenceProvider</c> for the verbatim script body.
/// </para>
/// </remarks>
public interface IPresenceWriter
{
    /// <summary>
    /// Idempotent heartbeat write — refreshes the player's presence key with the configured
    /// TTL. MUST NOT downgrade an <c>in_match</c> value to <c>online</c>; if the existing
    /// value is <c>in_match</c>, only the TTL is refreshed (CONTEXT D-03 / PATTERNS warning #6).
    /// </summary>
    /// <param name="playerId">The player whose presence is being heart-beaten.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Implementation MUST use an atomic Redis primitive (Lua script or pipelined
    /// MULTI/EXEC) so the GET → conditional SET pair cannot race against a concurrent
    /// <see cref="WriteInMatchAsync"/> call from the session-lifecycle observer.
    /// </remarks>
    ValueTask WriteHeartbeatAsync(Guid playerId, CancellationToken ct);

    /// <summary>
    /// Sets the in-match marker on the player's presence key with TTL refresh. Called by
    /// <c>PresenceSessionObserver.OnSessionStartedAsync</c> when a game-server-authoritative
    /// <c>POST /api/sessions/{id}/start</c> fires (Phase 6 / D-03).
    /// </summary>
    /// <param name="playerId">The player who is now in a session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The game-server is authoritative here — no precedence check is needed because the
    /// caller (session-lifecycle observer) is trusted. A plain Redis <c>SET PX</c> with the
    /// configured TTL is sufficient.
    /// </remarks>
    ValueTask WriteInMatchAsync(Guid playerId, CancellationToken ct);

    /// <summary>
    /// Refreshes the player's presence key with the <c>online</c> value and configured TTL.
    /// Called by <c>PresenceSessionObserver.OnSessionCompletedAsync</c> so the player drops
    /// back from in-match to online without waiting for the next heartbeat.
    /// </summary>
    /// <param name="playerId">The player whose session just completed.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask WriteOnlineAsync(Guid playerId, CancellationToken ct);

    /// <summary>
    /// Clears the in-match marker by writing the <c>online</c> value and refreshing the TTL.
    /// Called by <c>PresenceSessionObserver.OnSessionAbandonedAsync</c> to transition the
    /// player back to online (heartbeat fresh) or implicitly offline (heartbeat already
    /// expired) after a game-server-driven abandon.
    /// </summary>
    /// <param name="playerId">The player whose session was abandoned.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ClearInMatchAsync(Guid playerId, CancellationToken ct);
}
