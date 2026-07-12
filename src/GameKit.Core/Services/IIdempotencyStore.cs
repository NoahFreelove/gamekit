// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// Port interface for persisting and looking up idempotency state for
/// <c>POST /api/sessions/{id}/complete</c> (D-08).
/// </summary>
/// <remarks>
/// <para>
/// This port is OPTIONAL. If no implementation is registered, <see cref="ISessionCompleteService"/>
/// operates in degraded mode: duplicate requests re-run the full completion flow without dedup
/// protection. <c>GameKit.Rankings</c> registers <c>RankingsIdempotencyStore</c> as the concrete
/// implementation when <c>AddRankings()</c> is called, writing dedup rows to the
/// <c>session_complete_idempotency</c> table.
/// </para>
/// <para>
/// IMPORTANT: implementations MUST run inside the caller's ambient transaction. Do NOT open a
/// new transaction inside these methods. The store shares the caller's <c>GameKitDbContext</c>
/// and its dedup INSERT must commit atomically with the state-conditional UPDATE on
/// <c>game_sessions</c> (D-08 / T-04-05-RP).
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to look up a previously stored idempotency entry for the given
    /// <paramref name="sessionId"/> / <paramref name="idempotencyKey"/> pair.
    /// </summary>
    /// <param name="sessionId">The session being completed.</param>
    /// <param name="idempotencyKey">The client-supplied <c>Idempotency-Key</c> header value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IdempotencyLookup"/> describing whether a prior entry was found and,
    /// if so, the stored request body hash and cached response bytes.
    /// </returns>
    Task<IdempotencyLookup> TryGetAsync(
        Guid sessionId,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>
    /// Persists the idempotency entry for the given <paramref name="sessionId"/> /
    /// <paramref name="idempotencyKey"/> pair. Called after the session-complete work completes
    /// but before <c>CommitAsync</c>, so it participates in the same transaction.
    /// </summary>
    /// <param name="sessionId">The completed session's id.</param>
    /// <param name="idempotencyKey">The client-supplied <c>Idempotency-Key</c> header value.</param>
    /// <param name="requestBodyHash">
    /// SHA-256 hex (64 lower-case chars) of the canonical request body computed by
    /// <see cref="ICanonicalRequestHasher"/>.
    /// </param>
    /// <param name="responseBody">
    /// Serialized response bytes to return on a subsequent cache hit (exact byte-for-byte replica).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreAsync(
        Guid sessionId,
        string idempotencyKey,
        string requestBodyHash,
        byte[] responseBody,
        CancellationToken ct);
}

/// <summary>
/// Result of a <see cref="IIdempotencyStore.TryGetAsync"/> lookup.
/// </summary>
/// <param name="Found">
/// <see langword="true"/> if a prior idempotency entry exists for the given
/// session / idempotency-key pair.
/// </param>
/// <param name="ExistingRequestHash">
/// The SHA-256 hex hash that was stored with the prior request.
/// <see langword="null"/> when <paramref name="Found"/> is <see langword="false"/>.
/// </param>
/// <param name="CachedResponseBody">
/// The serialized response bytes from the prior successful call.
/// <see langword="null"/> when <paramref name="Found"/> is <see langword="false"/>.
/// </param>
public sealed record IdempotencyLookup(
    bool Found,
    string? ExistingRequestHash,
    byte[]? CachedResponseBody);
