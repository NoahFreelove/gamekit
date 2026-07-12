// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Successful login / refresh / register response. <see cref="RefreshToken"/> is null only in the
/// idempotent-replay case within the refresh grace window — the client already holds the token.
/// </summary>
/// <param name="AccessToken">The freshly-signed JWT access token.</param>
/// <param name="RefreshToken">The raw refresh token, or null on idempotent refresh replay.</param>
/// <param name="TokenType">Token type header hint; always <c>"Bearer"</c>.</param>
public sealed record TokenResponse(string AccessToken, string? RefreshToken, string TokenType = "Bearer");
