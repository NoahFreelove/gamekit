// SPDX-License-Identifier: Apache-2.0
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
/// Default implementation of <see cref="ISessionStartService"/> (D-20, PRES-05). Transitions
/// <c>game_sessions.state</c> from <see cref="GameSessionState.Pending"/> to
/// <see cref="GameSessionState.Active"/> inside a <c>ReadCommitted</c> transaction, then fires
/// every registered <see cref="ISessionLifecycleObserver"/> so sibling packages (e.g. Presence)
/// can react to the transition under the same transactional envelope.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the <see cref="SessionCompleteService"/> wiring shape (D-22). Observers are injected
/// as an <see cref="IEnumerable{T}"/> and run synchronously inside the transaction — a throwing
/// observer rolls back the state transition. Per the <see cref="ISessionLifecycleObserver"/>
/// contract, implementations MUST be idempotent and MUST NOT throw under non-fatal conditions
/// (transient downstream errors, optional-side-effect failures, etc.).
/// </para>
/// <para>
/// Idempotency: re-invocations on a session already in <see cref="GameSessionState.Active"/>
/// return <see cref="SessionStartResult.InvalidState"/> rather than a fresh <see cref="SessionStartResult.Started"/>.
/// The endpoint maps this to <c>409 Conflict</c>. Phase 6 deliberately does NOT layer
/// <c>Idempotency-Key</c> retry semantics on top of <c>/start</c> (only <c>/complete</c> needs
/// the cached-response replay protection because /complete carries result payload — /start
/// is body-less and the state machine is naturally idempotent — D-20).
/// </para>
/// </remarks>
public sealed class SessionStartService : ISessionStartService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly ILogger<SessionStartService> _logger;
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
    public SessionStartService(
        GameKitDbContext ctx,
        IClock clock,
        ILogger<SessionStartService> logger,
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
    public async Task<SessionStartResult> StartAsync(
        Guid sessionId,
        SessionStartRequest req,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        try
        {
            var now = _clock.UtcNow;

            // State-conditional UPDATE — WHERE state = Pending. Mirrors the D-07 pattern from
            // SessionCompleteService: a single SQL UPDATE that's a no-op if the row is missing
            // or in the wrong state.
            var affected = await _ctx.GameSessions
                .Where(s => s.Id == sessionId && s.State == GameSessionState.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.State, GameSessionState.Active)
                    .SetProperty(s => s.StartedAt, now),
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
                    return new SessionStartResult.SessionNotFound();
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new SessionStartResult.InvalidState(existing.State);
            }

            // Collect participant ids for observer fan-out. PlayerId is nullable per the
            // entity (GDPR erasure tombstone — SessionParticipant XML doc) so we filter nulls.
            var participantIds = await _ctx.SessionParticipants
                .AsNoTracking()
                .Where(p => p.SessionId == sessionId && p.PlayerId.HasValue)
                .Select(p => p.PlayerId!.Value)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Fan out to observers INSIDE the transaction (D-21). A throwing observer rolls
            // back the state transition. PresenceSessionObserver (Plan 06-04) uses this hook
            // to write the in-match marker for every participant.
            foreach (var observer in _observers)
            {
                await observer
                    .OnSessionStartedAsync(sessionId, participantIds, ct)
                    .ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Session {SessionId} transitioned to Active with {ParticipantCount} participant(s) and {ObserverCount} observer(s) fired.",
                sessionId, participantIds.Count, _observers.Count());

            return new SessionStartResult.Started(GameSessionState.Active);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* ignore rollback failure — original exception is rethrown */ }
            throw;
        }
    }
}
