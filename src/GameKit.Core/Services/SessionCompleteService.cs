// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Http.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameKit.Core.Services;

/// <summary>
/// Default implementation of <see cref="ISessionCompleteService"/>.
/// Orchestrates the state-conditional UPDATE + participant result write + idempotency dedup +
/// post-completion handler dispatch — all inside a single <c>ReadCommitted</c> transaction (D-07,
/// D-08, D-22).
/// </summary>
/// <remarks>
/// <para>
/// This service is registered as <c>Scoped</c> in <c>AddGameKit()</c>. The optional ports
/// (<see cref="IPostSessionCompleteHandler"/>, <see cref="IIdempotencyStore"/>,
/// <see cref="ICanonicalRequestHasher"/>) are injected as nullable via a factory registration
/// that calls <c>IServiceProvider.GetService&lt;T&gt;()</c>. When absent, the service
/// operates in degraded mode (session is still completed, but without rating-update enqueuing or
/// idempotency dedup — appropriate for Core-only installs per Open Q6).
/// </para>
/// <para>
/// Canonical flow (RESEARCH Pattern 4):
/// <list type="number">
///   <item>Compute request hash via <see cref="ICanonicalRequestHasher"/> (if registered).</item>
///   <item>Open a <c>ReadCommitted</c> transaction.</item>
///   <item>Lookup prior idempotency entry via <see cref="IIdempotencyStore"/> (if registered).</item>
///   <item>State-conditional UPDATE: <c>WHERE state = 'active'</c> (D-07).</item>
///   <item>Write participant results to <c>session_participants</c>.</item>
///   <item>Call <see cref="IPostSessionCompleteHandler.OnCompletedAsync"/> (if registered).</item>
///   <item>Store idempotency entry via <see cref="IIdempotencyStore"/> (if registered).</item>
///   <item>SaveChanges + Commit.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SessionCompleteService : ISessionCompleteService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly ILogger<SessionCompleteService> _logger;
    private readonly IEnumerable<ISessionLifecycleObserver> _lifecycleObservers;
    private readonly IPostSessionCompleteHandler? _postCompleteHandler;
    private readonly IIdempotencyStore? _idempotencyStore;
    private readonly ICanonicalRequestHasher? _hasher;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Constructs the service. The optional port dependencies are nullable — when absent, the
    /// service operates in degraded mode (Open Q6). Use the factory registration in
    /// <c>AddGameKit()</c> which calls <c>GetService&lt;T&gt;()</c> for optional ports.
    /// </summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="lifecycleObservers">
    /// Cross-package lifecycle observers (D-21 — Phase 6 PRES-05). Kept for backwards-compat
    /// alongside <see cref="IPostSessionCompleteHandler"/>: both interfaces coexist and may be
    /// registered independently in the same container. Pass <see cref="Enumerable.Empty{TResult}"/>
    /// in Core-only installs.
    /// </param>
    /// <param name="postCompleteHandler">
    /// Optional post-completion handler (e.g. Rankings enqueue adapter). Absent in Core-only installs.
    /// </param>
    /// <param name="idempotencyStore">
    /// Optional idempotency store. Absent in Core-only installs (no dedup).
    /// </param>
    /// <param name="hasher">
    /// Optional canonical request hasher. Absent in Core-only installs (no body hash).
    /// </param>
    public SessionCompleteService(
        GameKitDbContext ctx,
        IClock clock,
        ILogger<SessionCompleteService> logger,
        IEnumerable<ISessionLifecycleObserver> lifecycleObservers,
        IPostSessionCompleteHandler? postCompleteHandler = null,
        IIdempotencyStore? idempotencyStore = null,
        ICanonicalRequestHasher? hasher = null)
    {
        ArgumentNullException.ThrowIfNull(lifecycleObservers);
        _ctx = ctx;
        _clock = clock;
        _logger = logger;
        _lifecycleObservers = lifecycleObservers;
        _postCompleteHandler = postCompleteHandler;
        _idempotencyStore = idempotencyStore;
        _hasher = hasher;
    }

    /// <inheritdoc />
    public async Task<SessionCompleteResult> CompleteAsync(
        Guid sessionId,
        string idempotencyKey,
        SessionCompleteRequest req,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // Step 1: Compute the canonical request hash (if hasher is registered).
        var requestHash = _hasher?.ComputeSha256(req);

        // Step 2: Open a ReadCommitted transaction.
        await using var tx = await _ctx.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            // Step 3: Idempotency lookup (if store is registered).
            if (_idempotencyStore is not null && requestHash is not null)
            {
                var lookup = await _idempotencyStore.TryGetAsync(sessionId, idempotencyKey, ct);

                if (lookup.Found)
                {
                    if (lookup.ExistingRequestHash != requestHash)
                    {
                        // Same key, different body → 409.
                        await tx.CommitAsync(ct);
                        return new SessionCompleteResult.IdempotencyKeyReused();
                    }

                    // Same key, same body → return cached response (D-08).
                    await tx.CommitAsync(ct);
                    if (lookup.CachedResponseBody is { Length: > 0 })
                    {
                        var cached = JsonSerializer.Deserialize<SessionCompleteResponse>(
                            lookup.CachedResponseBody, _jsonOpts);
                        if (cached is not null)
                            return new SessionCompleteResult.AlreadyCompletedCached(cached);
                    }

                    // Cached bytes missing/corrupt — treat as not-found (should not happen in practice).
                    _logger.LogWarning(
                        "Idempotency row found for session {SessionId} key {Key} but CachedResponse is empty or undeserializable.",
                        sessionId, idempotencyKey);
                    return new SessionCompleteResult.SessionNotFound();
                }
            }

            return await RunCompletionAsync(sessionId, idempotencyKey, req, requestHash, tx, ct);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { /* ignore rollback error */ }
            throw;
        }
    }

    private async Task<SessionCompleteResult> RunCompletionAsync(
        Guid sessionId,
        string idempotencyKey,
        SessionCompleteRequest req,
        string? requestHash,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // Step 4: State-conditional UPDATE — WHERE state = Active (D-07).
        var affected = await _ctx.GameSessions
            .Where(s => s.Id == sessionId && s.State == GameSessionState.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.State, GameSessionState.Completed)
                .SetProperty(s => s.CompletedAt, now),
                ct);

        if (affected == 0)
        {
            // Session was not in Active state — determine why.
            var session = await _ctx.GameSessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.State })
                .FirstOrDefaultAsync(ct);

            if (session is null)
            {
                await tx.CommitAsync(ct);
                return new SessionCompleteResult.SessionNotFound();
            }

            // If already Completed and we have a store, check for cached response with this key.
            // WR-10: when the state UPDATE loses a race with a concurrent A-then-B call, B falls
            // through here and looks up A's idempotency row. We MUST compare hashes before
            // returning A's cached response — otherwise B receives 200 OK carrying A's body
            // even though B's request body differed from A's. Mirrors the pre-update check
            // at line 121.
            if (session.State == GameSessionState.Completed && _idempotencyStore is not null)
            {
                var lookup = await _idempotencyStore.TryGetAsync(sessionId, idempotencyKey, ct);
                if (lookup.Found)
                {
                    if (requestHash is not null
                        && lookup.ExistingRequestHash is not null
                        && lookup.ExistingRequestHash != requestHash)
                    {
                        // Same key, different body → 409 (same semantics as the pre-update check).
                        await tx.CommitAsync(ct);
                        return new SessionCompleteResult.IdempotencyKeyReused();
                    }

                    if (lookup.CachedResponseBody is { Length: > 0 })
                    {
                        await tx.CommitAsync(ct);
                        var cached = JsonSerializer.Deserialize<SessionCompleteResponse>(
                            lookup.CachedResponseBody, _jsonOpts);
                        if (cached is not null)
                            return new SessionCompleteResult.AlreadyCompletedCached(cached);
                    }
                }
            }

            await tx.CommitAsync(ct);
            return new SessionCompleteResult.InvalidState(session.State);
        }

        // Step 5: Validate participants and write results.
        var existingParticipants = await _ctx.SessionParticipants
            .Where(p => p.SessionId == sessionId)
            .ToListAsync(ct);

        // Check for extra participants in the request (not on the session).
        foreach (var reqParticipant in req.Participants)
        {
            var existing = existingParticipants
                .FirstOrDefault(p => p.PlayerId == reqParticipant.PlayerId);
            if (existing is null)
            {
                await tx.RollbackAsync(ct);
                return new SessionCompleteResult.UnknownParticipant(reqParticipant.PlayerId);
            }
        }

        // Check for session participants missing from the request.
        foreach (var sp in existingParticipants)
        {
            if (sp.PlayerId.HasValue)
            {
                var inRequest = req.Participants.Any(p => p.PlayerId == sp.PlayerId.Value);
                if (!inRequest)
                {
                    await tx.RollbackAsync(ct);
                    return new SessionCompleteResult.MissingParticipant(sp.PlayerId.Value);
                }
            }
        }

        // Write participant results (Result + Score fields).
        foreach (var reqParticipant in req.Participants)
        {
            await _ctx.SessionParticipants
                .Where(p => p.SessionId == sessionId && p.PlayerId == reqParticipant.PlayerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.Result, reqParticipant.Result)
                    .SetProperty(p => p.Score, reqParticipant.Score),
                    ct);
        }

        // Retrieve the session to get LadderId for the post-handler snapshots.
        var completedSession = await _ctx.GameSessions
            .AsNoTracking()
            .Select(s => new { s.Id, s.LadderId })
            .FirstAsync(s => s.Id == sessionId, ct);

        var ladderId = completedSession.LadderId;

        // Build participant snapshots for the post-handler.
        var participantSnapshots = req.Participants
            .Select(p => new SessionParticipantSnapshot(p.PlayerId, ladderId, p.Result, p.Score))
            .ToList();

        // Step 6: Call post-completion handler (if registered — optional per Open Q6).
        if (_postCompleteHandler is not null)
        {
            await _postCompleteHandler.OnCompletedAsync(sessionId, participantSnapshots, ct);
        }

        // Step 6b (Phase 6 — D-21, PRES-05): fan out to cross-package lifecycle observers
        // AFTER the post-complete handler ran. Both ports coexist (IPostSessionCompleteHandler
        // continues to fire for Rankings' rating-update enqueue; ISessionLifecycleObserver fires
        // for Presence's in-match clearance). Observers run inside the same transaction — a
        // throwing observer rolls back the whole completion (including the rating-update
        // enqueue). Per the ISessionLifecycleObserver contract, implementations MUST be
        // idempotent and MUST NOT throw under non-fatal conditions.
        if (participantSnapshots.Count > 0)
        {
            var participantIds = participantSnapshots
                .Select(p => p.PlayerId)
                .ToList();
            foreach (var observer in _lifecycleObservers)
            {
                await observer
                    .OnSessionCompletedAsync(sessionId, participantIds, ct)
                    .ConfigureAwait(false);
            }
        }

        // Reload participants to get rating_before values set by the post-handler.
        var updatedParticipants = await _ctx.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .ToListAsync(ct);

        var participantResults = req.Participants
            .Select(reqP =>
            {
                var sp = updatedParticipants.FirstOrDefault(p => p.PlayerId == reqP.PlayerId);
                return new SessionCompleteParticipantResult(
                    reqP.PlayerId,
                    reqP.Result,
                    sp?.RatingBefore,
                    sp?.RatingAfter,
                    sp?.RatingDelta);
            })
            .ToList();

        var response = new SessionCompleteResponse(
            sessionId,
            GameSessionState.Completed,
            participantResults,
            now);

        // Step 7: Store idempotency entry (if store + hasher are registered).
        if (_idempotencyStore is not null && requestHash is not null)
        {
            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOpts);
            await _idempotencyStore.StoreAsync(sessionId, idempotencyKey, requestHash, responseBytes, ct);
        }

        // Step 8: SaveChanges + Commit (idempotency store already called SaveChanges within the tx).
        await _ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SessionCompleteResult.Completed(response);
    }
}
