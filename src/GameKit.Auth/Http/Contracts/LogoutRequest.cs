// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Request body for <c>POST /auth/logout</c>. Revokes the family of the presented refresh token
/// (RESEARCH §15 open question #4 resolved — family revoke, not single-token revoke).
/// </summary>
/// <param name="RefreshToken">The raw refresh token whose family should be revoked.</param>
public sealed record LogoutRequest(string RefreshToken);
