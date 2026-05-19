// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Application service that closes the D-06 proposal accept / decline flow. Wired by the
/// HTTP endpoints <c>POST /api/mm/proposal/{id}/accept</c> and
/// <c>POST /api/mm/proposal/{id}/decline</c> (Plan 05-08) and by the ticker's proposal
/// sweeper (Plan 05-05) for TTL-expired proposals.
/// </summary>
/// <remarks>
/// <para>
/// <b>Accept flow (RESEARCH §Architecture lines 145-152):</b>
/// <list type="number">
///   <item>HGETALL the proposal hash; if missing → <see cref="AcceptResult.ProposalNotFound"/>.</item>
///   <item>Verify the supplied ticket id is in <c>proposal.Tickets</c> (T-05-06-01 — Spoofing guard).</item>
///   <item>Run the atomic Lua complete-script: <c>SADD</c> ticket id to the acceptors set + <c>SCARD</c>;
///         if count == expected ticket count and state is pending, <c>HSET state=complete</c> and return <c>COMPLETE</c>.</item>
///   <item>On <c>COMPLETE</c>: INSERT <c>GameSession</c> + <c>SessionParticipant</c> rows; PUBLISH "matched"; emit <c>Matched</c> ticket events.</item>
///   <item>On <c>PENDING</c>: emit <c>Accepted</c> ticket event for this ticket; return <see cref="AcceptResult.Accepted"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Decline flow (CONTEXT D-08 + D-09):</b>
/// <list type="number">
///   <item>HGETALL proposal hash; if missing → <see cref="DeclineResult.ProposalNotFound"/>.</item>
///   <item>Verify the supplied ticket id is in <c>proposal.Tickets</c> (T-05-06-01).</item>
///   <item>Write <c>DeclineHistory</c> row first (durable) — guarantees the cooldown effect even if Redis fails.</item>
///   <item>Run the atomic Lua decline-and-reap script: for each accepting ticket (in the <c>acceptors</c> set, not the declining one), <c>ZADD</c> back into the original pool with the <em>original</em> <c>QueuedAtUnixMs</c> score (D-09 preservation); DEL acceptors + proposal hashes.</item>
///   <item>PUBLISH "cancelled" to the declining ticket; PUBLISH "requeued" to each accepting ticket.</item>
///   <item>Emit <c>Declined</c> event for declining ticket + <c>Cancelled</c> events for the other tickets.</item>
/// </list>
/// </para>
/// <para>
/// <b>Idempotency on late accept (T-05-06-04):</b> after the Lua complete script flips state
/// to <c>complete</c>, subsequent accept calls observe <c>state=complete</c> in HGETALL and
/// return <see cref="AcceptResult.AlreadyAccepted"/> instead of touching the
/// <c>GameSession</c>. This makes the accept path safe to retry from the client.
/// </para>
/// </remarks>
public interface IProposalService
{
    /// <summary>
    /// Accept the proposal on behalf of the ticket holder.
    /// </summary>
    /// <param name="proposalId">The proposal id.</param>
    /// <param name="ticketId">The ticket id that is accepting (must belong to the proposal).</param>
    /// <param name="playerId">The canonical player id submitting the accept.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="AcceptResult"/> indicating the proposal's resulting state.</returns>
    Task<AcceptResult> AcceptAsync(Guid proposalId, Guid ticketId, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Decline the proposal on behalf of the ticket holder. Writes a <c>decline_history</c>
    /// row (CONTEXT D-08 cooldown bookkeeping), re-ZADDs accepting partner tickets with their
    /// original <c>queuedAt</c> score (CONTEXT D-09), and tears down the proposal hash.
    /// </summary>
    /// <param name="proposalId">The proposal id.</param>
    /// <param name="ticketId">The ticket id that is declining (must belong to the proposal).</param>
    /// <param name="playerId">The canonical player id submitting the decline.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeclineResult"/> indicating the proposal's resulting state.</returns>
    Task<DeclineResult> DeclineAsync(Guid proposalId, Guid ticketId, Guid playerId, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IProposalService.AcceptAsync"/>.</summary>
public enum AcceptResult
{
    /// <summary>The accept was recorded and the proposal is still pending other acceptors.</summary>
    Accepted = 0,

    /// <summary>This ticket had already accepted; the call was idempotent (no state changed).</summary>
    AlreadyAccepted = 1,

    /// <summary>
    /// The accept was recorded and was the FINAL acceptor — the <c>GameSession</c> was created
    /// and "matched" was published to every member's status channel.
    /// </summary>
    AllAccepted = 2,

    /// <summary>The proposal does not exist (TTL expired, or was never created).</summary>
    ProposalNotFound = 3,

    /// <summary>The ticket id is not a member of the proposal (T-05-06-01 spoofing guard).</summary>
    NotInProposal = 4,
}

/// <summary>Outcome of <see cref="IProposalService.DeclineAsync"/>.</summary>
public enum DeclineResult
{
    /// <summary>The decline was recorded; the proposal was torn down + accepting partners re-queued.</summary>
    Declined = 0,

    /// <summary>The proposal does not exist (TTL expired, or was never created).</summary>
    ProposalNotFound = 1,

    /// <summary>The ticket id is not a member of the proposal (T-05-06-01 spoofing guard).</summary>
    NotInProposal = 2,
}
