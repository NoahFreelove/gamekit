// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>
/// Deterministic, non-reversible hash of (provider, external_id) used in 409
/// <c>identity_already_linked</c> response bodies so the raw external id is not disclosed
/// to the requesting guest (CONTEXT D-11).
/// </summary>
public interface IExternalIdHasher
{
    /// <summary>Returns hex-encoded SHA-256 of <c>"{provider}:{externalId}"</c>.</summary>
    /// <param name="provider">Provider discriminator (e.g. <c>steam</c>, <c>discord</c>).</param>
    /// <param name="externalId">Provider-specific external id string.</param>
    /// <returns>64-character lowercase hex SHA-256 digest.</returns>
    string Hash(string provider, string externalId);
}
