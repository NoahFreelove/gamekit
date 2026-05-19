// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Matchmaking.Strategy;

/// <summary>
/// A single party currently sitting in the matchmaking queue, as seen by an
/// <see cref="IMatchmakingStrategy"/>. Plain DTO record — the strategy is pure and reads
/// these values without touching Redis or Postgres directly.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AggregateRating"/> is computed once at enqueue time by
/// <see cref="PartyRatingAggregatorService"/> and cached on the Redis ticket hash
/// (RESEARCH §Decision 5 — "Cache aggregate rating at enqueue time"). The matcher
/// reads this cached value rather than recomputing each tick. The cache may be stale by
/// up to one ratings period for long-waiting tickets; this is documented and accepted.
/// </para>
/// <para>
/// <see cref="QueuedAt"/> is the original enqueue timestamp; re-queued tickets after a
/// decline (CONTEXT D-09) preserve their original <see cref="QueuedAt"/> so the bracket
/// flex accumulator is not lost. Stored as Unix milliseconds in the Redis sorted-set
/// score (Pitfall §6 — never seconds).
/// </para>
/// </remarks>
/// <param name="TicketId">Ticket identifier — same as the Postgres <c>matchmaking_tickets.Id</c> + Redis member id.</param>
/// <param name="PartyId">Party identifier, or <see langword="null"/> for solo tickets (defensive — v1 ships party-only).</param>
/// <param name="LadderId">Ladder identifier — selects the per-ladder config the strategy uses.</param>
/// <param name="PoolName">Pool name (e.g. <c>"ranked"</c>, <c>"casual"</c>) — operator-defined free-form.</param>
/// <param name="Members">Per-member rating snapshots — required for <see cref="PartyRatingAggregator.GlickoWeighted"/> + spread-cap checks.</param>
/// <param name="AggregateRating">Cached per-party rating (computed at enqueue via the ladder's aggregator).</param>
/// <param name="QueuedAt">Original enqueue UTC timestamp — preserved across re-queues (CONTEXT D-09).</param>
public sealed record QueuedParty(
    Guid TicketId,
    Guid? PartyId,
    Guid LadderId,
    string PoolName,
    IReadOnlyList<QueuedPartyMember> Members,
    double AggregateRating,
    DateTimeOffset QueuedAt);

/// <summary>
/// Per-member rating snapshot carried inside <see cref="QueuedParty"/>. Mirrors
/// <c>PlayerRank</c> from <c>GameKit.Rankings</c> without taking a hard runtime dependency
/// on the Rankings entity type (the strategy is pure).
/// </summary>
/// <param name="PlayerId">Canonical player identifier.</param>
/// <param name="Rating">Current Glicko-2 rating.</param>
/// <param name="RatingDeviation">Current Glicko-2 RD — used by <see cref="PartyRatingAggregator.GlickoWeighted"/> (weight = 1/RD²).</param>
/// <param name="Volatility">Current Glicko-2 volatility — not used by v1 aggregators, carried for forward-compat.</param>
public sealed record QueuedPartyMember(
    Guid PlayerId,
    double Rating,
    double RatingDeviation,
    double Volatility);
