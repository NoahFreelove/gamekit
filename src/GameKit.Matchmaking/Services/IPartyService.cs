// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Entities;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Application service for the durable party CRUD surface (CONTEXT D-01..D-05).
/// Wires HTTP endpoints (Plan 05-08) and the matchmaker enqueue path against the
/// <see cref="Party"/> + <see cref="PartyMember"/> entities (Plan 05-02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Single-active-party enforcement</b> — every mutating operation runs under a
/// Postgres SERIALIZABLE transaction (RESEARCH §OQ-2-RESOLVED). The composite UNIQUE
/// constraint on <c>(PartyId, PlayerId)</c> from Plan 05-02 prevents duplicate-row inserts,
/// but the cross-party invariant (a player cannot belong to two open parties at once) is
/// enforced at the SERIALIZABLE level. Retries on 40001 serialization-failure follow the
/// Polly pattern from Phase 4.
/// </para>
/// <para>
/// <b>Case-insensitive code lookup</b> — Plan 05-02 declared the <c>party_code</c> column
/// as Postgres <c>citext</c>, so the <c>WHERE party_code = @code</c> SQL is automatically
/// case-insensitive. The service does NOT call <c>ToUpperInvariant()</c> on the supplied
/// code before lookup (Pitfall §9 — the citext does the work).
/// </para>
/// </remarks>
public interface IPartyService
{
    /// <summary>
    /// Create a new party owned by <paramref name="ownerPlayerId"/>. Generates a unique
    /// party code, retries up to 5 times on UNIQUE violation. Throws
    /// <see cref="PartyConflictException"/> if the owner is already in an active party
    /// (state ∈ <c>{ Open, Queueing, InMatch }</c>).
    /// </summary>
    /// <param name="ownerPlayerId">Canonical player id of the creator. Becomes the only member.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="Party"/>.</returns>
    /// <exception cref="PartyConflictException">The owner is already in an active party, or the generator failed to mint a unique code in 5 attempts.</exception>
    Task<Party> CreateAsync(Guid ownerPlayerId, CancellationToken ct = default);

    /// <summary>
    /// Add <paramref name="playerId"/> to the party identified by <paramref name="code"/>.
    /// Case-insensitive lookup (citext). Throws if the player is already in an active
    /// party, or the party is not in <see cref="PartyState.Open"/>, or the code is unknown.
    /// </summary>
    /// <param name="code">Party code (case-insensitive — citext does the work).</param>
    /// <param name="playerId">Canonical player id of the joining player.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="Party"/>.</returns>
    /// <exception cref="PartyConflictException">The player is already in an active party.</exception>
    /// <exception cref="PartyInvalidStateException">The party is not in state <see cref="PartyState.Open"/>, or the code is unknown.</exception>
    Task<Party> JoinAsync(string code, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Dissolve the party. Only the owner may dissolve. <see cref="PartyMember"/> rows are
    /// retained (audit). The party state transitions to <see cref="PartyState.Dissolved"/>,
    /// freeing the player slot for a new party.
    /// </summary>
    /// <param name="partyId">Party identifier.</param>
    /// <param name="actorPlayerId">Canonical player id of the dissolving actor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Awaitable.</returns>
    /// <exception cref="PartyInvalidStateException">The party is unknown or already dissolved.</exception>
    /// <exception cref="PartyAuthorizationException">The actor is not the party owner.</exception>
    Task DissolveAsync(Guid partyId, Guid actorPlayerId, CancellationToken ct = default);

    /// <summary>
    /// Get a party by its (case-insensitive) code. Returns <see langword="null"/> when
    /// no party with that code exists. Does not filter by state — callers wishing to
    /// reject dissolved parties must check <see cref="Party.State"/> themselves.
    /// </summary>
    /// <param name="code">Party code (case-insensitive — citext does the work).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The party, or <see langword="null"/> if not found.</returns>
    Task<Party?> GetByCodeAsync(string code, CancellationToken ct = default);
}
