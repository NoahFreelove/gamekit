// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameKit.Core.Services;

/// <summary>
/// Default implementation of <see cref="ISessionAbandonService"/> (D-20, PRES-05). Transitions
/// <c>game_sessions.state</c> from <see cref="GameSessionState.Active"/> to
/// <see cref="GameSessionState.Abandoned"/> inside a <c>ReadCommitted</c> transaction, then fires
/// every registered <see cref="ISessionLifecycleObserver"/> so sibling packages (e.g. Presence)
/// can clear the in-match marker under the same transactional envelope.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="SessionStartService"/>. The chosen terminal state is
/// <see cref="GameSessionState.Abandoned"/> (not <see cref="GameSessionState.Cancelled"/>) because
/// the dedicated <c>/abandon</c> endpoint is the game-server's mechanism for declaring that a
/// session ended mid-play (e.g. disconnect, rage-quit, server crash). The
/// <see cref="GameSessionStateTransitions"/> table permits only <c>Active → Abandoned</c>;
/// transitions from <see cref="GameSessionState.Pending"/> would use <see cref="GameSessionState.Cancelled"/>
/// and would be triggered by a different operation (matchmaking timeout — out of scope for Phase 6).
/// </para>
/// <para>
/// Observers run synchronously inside the transaction — a throwing observer rolls back the state
/// transition. Per the <see cref="ISessionLifecycleObserver"/> contract, implementations MUST be
/// idempotent and MUST NOT throw under non-fatal conditions.
/// </para>
/// </remarks>
public sealed class SessionAbandonService : ISessionAbandonService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly ILogger<SessionAbandonService> _logger;
    private readonly IEnumerable<ISessionLifecycleObserver> _observers;

    /// <summary>
    /// Constructs the service.
    /// </summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="observers">
    /// Cross-package lifecycle observers (D-21). Pass <see cref="Enumerable.Empty{TResult}"/>
    /// in Core-only installs where no observer is registered.
    /// </param>
    public SessionAbandonService(
        GameKitDbContext ctx,
        IClock clock,
        ILogger<SessionAbandonService> logger,
        IEnumerable<ISessionLifecycleObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(observers);

        _ctx = ctx;
        _clock = clock;
        _logger = logger;
        _observers = observers;
    }

    /// <inheritdoc />
    public async Task<SessionAbandonResult> AbandonAsync(
        Guid sessionId,
        SessionAbandonRequest req,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        try
        {
            var now = _clock.UtcNow;

            // State-conditional UPDATE — WHERE state = Active. Mirrors the D-07 pattern.
            var affected = await _ctx.GameSessions
                .Where(s => s.Id == sessionId && s.State == GameSessionState.Active)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.State, GameSessionState.Abandoned)
                    .SetProperty(s => s.CompletedAt, now),
                    ct)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                var existing = await _ctx.GameSessions
                    .AsNoTracking()
                    .Where(s => s.Id == sessionId)
                    .Select(s => new { s.State })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    return new SessionAbandonResult.SessionNotFound();
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new SessionAbandonResult.InvalidState(existing.State);
            }

            var participantIds = await _ctx.SessionParticipants
                .AsNoTracking()
                .Where(p => p.SessionId == sessionId && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Fan out to observers INSIDE the transaction (D-21). PresenceSessionObserver
            // (Plan 06-04) uses this hook to clear the in-match marker — players fall back
            // to Online (heartbeat fresh) or Offline (heartbeat expired).
            foreach (var observer in _observers)
            {
                await observer
                    .OnSessionAbandonedAsync(sessionId, participantIds, ct)
                    .ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Session {SessionId} transitioned to Abandoned with {ParticipantCount} participant(s) and {ObserverCount} observer(s) fired.",
                sessionId, participantIds.Count, _observers.Count());

            return new SessionAbandonResult.Abandoned(GameSessionState.Abandoned);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* ignore rollback failure — original exception is rethrown */ }
            throw;
        }
    }
}
