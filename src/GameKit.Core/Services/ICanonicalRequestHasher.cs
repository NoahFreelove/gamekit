// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Http.Contracts;

namespace GameKit.Core.Services;

/// <summary>
/// Port interface for computing a canonical SHA-256 hash of a
/// <see cref="SessionCompleteRequest"/> body (Open Q5).
/// </summary>
/// <remarks>
/// <para>
/// This port is OPTIONAL. If no implementation is registered, <see cref="ISessionCompleteService"/>
/// cannot compute a body hash and idempotency dedup falls back to key-only matching (degraded).
/// <c>GameKit.Rankings</c> registers <c>CanonicalJsonHasher</c> as a singleton implementation
/// when <c>AddRankings()</c> is called.
/// </para>
/// <para>
/// The canonicalization contract (Open Q5 Assumption A5):
/// <list type="number">
///   <item>Sort participants by <c>PlayerId.ToString()</c> ordinal ascending — deterministic across
///         .NET versions (Guid's default <c>ToString()</c> is round-trip stable).</item>
///   <item>Re-serialize via <c>System.Text.Json</c> with
///         <c>PropertyNamingPolicy = JsonNamingPolicy.CamelCase</c> and
///         <c>WriteIndented = false</c> — eliminates JSON whitespace variation.</item>
///   <item>SHA-256 the resulting UTF-8 bytes; return 64 lower-case hex chars.</item>
/// </list>
/// Maintainers MUST NOT change these rules without a migration strategy for existing
/// <c>session_complete_idempotency.request_body_hash</c> rows.
/// </para>
/// </remarks>
public interface ICanonicalRequestHasher
{
    /// <summary>
    /// Computes the canonical SHA-256 hex hash of <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The session-complete request to hash.</param>
    /// <returns>A 64-character lower-case hexadecimal SHA-256 hash string.</returns>
    string ComputeSha256(SessionCompleteRequest request);
}
