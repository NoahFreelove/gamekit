// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// JSON-serialised payload written into the <c>fields</c> hash field of the Redis proposal
/// hash by the ticker's <see cref="GameKit.Matchmaking.Redis.AtomicClaimScript"/>; read by
/// <see cref="ProposalService"/> on accept / decline.
/// </summary>
/// <remarks>
/// <para>
/// Plan 05-04's <c>AtomicClaimScript</c> writes a single field <c>fields</c> on the proposal
/// hash containing a JSON blob of this shape. The ticker (Plan 05-05) is responsible for
/// constructing the JSON; this plan reads it back. The schema is stable across producers
/// and consumers via the public properties below.
/// </para>
/// <para>
/// <b>Why a single JSON blob (not per-field HSET):</b> the ticker writes the proposal atomically
/// inside the Lua claim script; bundling fields as JSON keeps the script's <c>HSET</c> count
/// at 1 (under the 30-line script cap) and lets us evolve the schema without changing the
/// Lua source.
/// </para>
/// </remarks>
public sealed class ProposalFields
{
    /// <summary>
    /// Member tickets in the proposal. Each entry carries the ticket id (also the queue
    /// member id) and the original <c>queuedAt</c> Unix-ms timestamp used as the sorted-set
    /// score (Pitfall §6 — millisecond precision).
    /// </summary>
    /// <remarks>
    /// On decline, <see cref="ProposalService.DeclineAsync"/> re-ZADDs each accepting
    /// ticket to <see cref="QueueKey"/> with its <see cref="ProposalTicket.QueuedAtUnixMs"/>
    /// score — preserving the original bracket-flex accumulator (CONTEXT D-09).
    /// </remarks>
    public List<ProposalTicket> Tickets { get; set; } = new();

    /// <summary>
    /// Ladder identifier — propagated into the <see cref="GameKit.Core.Entities.GameSession"/>
    /// row on all-accept (Plan 05-06 Task 2 step 5).
    /// </summary>
    public Guid LadderId { get; set; }

    /// <summary>
    /// Pool sorted-set key (e.g. <c>"mm:queue:{ladderId}:default"</c>) the ticker pulled the
    /// tickets from. Used by <see cref="ProposalService.DeclineAsync"/> to ZADD accepting
    /// tickets back into the original pool (CONTEXT D-09).
    /// </summary>
    public string QueueKey { get; set; } = string.Empty;

    /// <summary>
    /// UTC deadline at which the proposal expires (ISO-8601 string). Informational —
    /// the canonical TTL is on the Redis hash via <c>EXPIRE</c>. Carried for analytics + future
    /// "deadline" exposure to clients via the long-poll status endpoint.
    /// </summary>
    public string Deadline { get; set; } = string.Empty;
}

/// <summary>
/// Per-ticket entry inside <see cref="ProposalFields"/>. Carries the queue score so the
/// decline-and-reap re-ZADD preserves <see cref="QueuedAtUnixMs"/> verbatim.
/// </summary>
public sealed class ProposalTicket
{
    /// <summary>Ticket identifier (queue sorted-set member id).</summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// Original <c>queuedAt</c> Unix-milliseconds score. Re-used verbatim on decline re-ZADD
    /// (CONTEXT D-09 — preserves bracket-flex accumulator).
    /// </summary>
    public long QueuedAtUnixMs { get; set; }

    /// <summary>
    /// Canonical player ids tied to this ticket. For solo tickets, a single entry; for party
    /// tickets, all party member ids. Consumed by <c>ProposalService.AcceptAsync</c> when
    /// constructing <see cref="GameKit.Core.Entities.SessionParticipant"/> rows.
    /// </summary>
    public List<Guid> PlayerIds { get; set; } = new();
}
