// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Error response envelope used by the 400 / 401 / 409 responses from the /auth/* endpoints.
/// On <c>identity_already_linked</c> (409), <see cref="ExternalIdHash"/> carries the SHA-256
/// hash of the (provider, external_id) tuple — never the raw external id (CONTEXT D-11 / T-02-10).
/// </summary>
/// <param name="Error">Stable machine-readable error discriminator (e.g. <c>invalid_credentials</c>, <c>identity_already_linked</c>).</param>
/// <param name="Provider">Optional provider discriminator that produced the error.</param>
/// <param name="ExternalIdHash">Optional SHA-256 hash of a colliding external id (for link collisions only).</param>
public sealed record AuthErrorResponse(string Error, string? Provider = null, string? ExternalIdHash = null);
