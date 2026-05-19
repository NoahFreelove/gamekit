// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Entities;

namespace GameKit.Rankings.Entities;

/// <summary>
/// Idempotency cache row for <c>POST /api/sessions/{id}/complete</c> (D-08).
/// Deduplicates retried requests within a 24-hour TTL window.
/// </summary>
/// <remarks>
/// <para>
/// The composite primary key <c>(session_id, idempotency_key)</c> enforces uniqueness.
/// Lookup: if a row exists with the same <c>(session_id, idempotency_key)</c> pair,
/// the handler compares <c>RequestBodyHash</c> to the inbound body hash:
/// <list type="bullet">
///   <item>Same hash → return <c>CachedResponse</c> with <c>200 OK</c>.</item>
///   <item>Different hash → return <c>409 idempotency_key_reused</c>.</item>
/// </list>
/// </para>
/// <para>
/// <c>RequestBodyHash</c> is the SHA-256 hex of the canonicalized request body
/// (sorted by <c>player_id</c>, re-serialized via <c>System.Text.Json</c>) per
/// <c>CanonicalJsonHasher</c> (Open Q5 recommendation).
/// </para>
/// <para>
/// <c>CachedResponse</c> is the full serialized response body (<c>bytea</c>), stored so
/// the handler can return an exact byte-for-byte replica on cache hit without re-querying
/// the database. Both columns are required by <c>IIdempotencyStore</c>'s
/// <c>TryGetCachedResponseAsync</c> and <c>StoreResponseAsync</c> signatures (plan 04-05).
/// </para>
/// <para>
/// Rows older than the TTL (default 24h) are purged by <c>IdempotencyCleanupService</c>.
/// </para>
/// </remarks>
public sealed class SessionCompleteIdempotency
{
    /// <summary>FK → <see cref="GameSession"/> (ON DELETE CASCADE) — part of composite PK.</summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Client-supplied idempotency key (the <c>Idempotency-Key</c> header value) — part of composite PK.
    /// </summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>
    /// SHA-256 hex (64 chars, lower-case) of the canonicalized request body.
    /// Used to detect same-key-different-body conflicts (409 path).
    /// </summary>
    public required string RequestBodyHash { get; set; }

    /// <summary>
    /// Serialized response body returned on cache hit. Stored as <c>bytea</c> so the handler
    /// returns an exact replica without re-computing the rating deltas.
    /// </summary>
    public required byte[] CachedResponse { get; set; }

    /// <summary>UTC timestamp at which this row was stored. Used by the cleanup service for TTL eviction.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
