// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>
/// Paired access + refresh tokens returned from a successful login or rotation.
/// <c>RawRefresh</c> may be null when the server returned the already-issued child
/// (idempotent replay within the grace window — the client already has it).
/// </summary>
/// <param name="AccessJwt">The freshly-signed JWT access token.</param>
/// <param name="RawRefresh">The raw (one-time emission) refresh token, or null for an idempotent replay.</param>
public sealed record TokenPair(string AccessJwt, string? RawRefresh);
