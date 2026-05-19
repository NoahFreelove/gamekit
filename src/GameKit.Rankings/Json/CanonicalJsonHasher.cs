// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GameKit.Core.Http.Contracts;
using GameKit.Core.Services;

namespace GameKit.Rankings.Json;

/// <summary>
/// Default implementation of <see cref="ICanonicalRequestHasher"/> for
/// <see cref="SessionCompleteRequest"/> bodies (Open Q5).
/// </summary>
/// <remarks>
/// <para>
/// Canonicalization rules (Open Q5 Assumption A5 — MUST NOT change without a migration strategy
/// for existing <c>session_complete_idempotency.request_body_hash</c> rows):
/// <list type="number">
///   <item>
///     Sort participants by <c>PlayerId.ToString()</c> ordinal ascending. This produces a
///     deterministic order across .NET versions because <c>Guid.ToString()</c> (default "D"
///     format) is round-trip stable and ordinal string comparison is locale-independent.
///   </item>
///   <item>
///     Serialize the reordered request via <c>System.Text.Json</c> with
///     <c>PropertyNamingPolicy = JsonNamingPolicy.CamelCase</c> and <c>WriteIndented = false</c>.
///     This eliminates whitespace variation from different client JSON serializers.
///   </item>
///   <item>
///     SHA-256 the resulting UTF-8 byte array; return a 64-character lower-case hex string.
///   </item>
/// </list>
/// </para>
/// <para>
/// This class is registered as a singleton by <c>RankingsBuilderExtensions.AddRankings()</c> via
/// the <see cref="ICanonicalRequestHasher"/> port. Core's <c>SessionCompleteService</c> consumes
/// the port and therefore has zero compile-time reference to <c>CanonicalJsonHasher</c> (D-22
/// invariant).
/// </para>
/// </remarks>
public sealed class CanonicalJsonHasher : ICanonicalRequestHasher
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <inheritdoc />
    public string ComputeSha256(SessionCompleteRequest request)
        => Sha256OfCanonicalJson(request);

    /// <summary>
    /// Computes the canonical SHA-256 hex hash of <paramref name="req"/>.
    /// </summary>
    /// <remarks>
    /// Exposed as a public static method so unit tests can call it without constructing a full DI
    /// container. The instance <see cref="ComputeSha256"/> method delegates here.
    /// </remarks>
    /// <param name="req">The session-complete request to hash.</param>
    /// <returns>A 64-character lower-case hexadecimal SHA-256 hash string.</returns>
    public static string Sha256OfCanonicalJson(SessionCompleteRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        // Step 1: Sort participants by PlayerId.ToString() ordinal ascending.
        // Using StringComparison.Ordinal for locale-independent, .NET-version-stable ordering
        // (Open Q5 Assumption A5).
        var sorted = req.Participants
            .OrderBy(p => p.PlayerId.ToString(), StringComparer.Ordinal)
            .ToList();

        var canonical = new SessionCompleteRequest(sorted);

        // Step 2: Serialize with camelCase + no whitespace.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, _opts);

        // Step 3: SHA-256, returned as 64 lower-case hex chars.
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
