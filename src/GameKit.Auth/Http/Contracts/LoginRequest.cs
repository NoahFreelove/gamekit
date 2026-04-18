// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Request body for <c>POST /auth/login/{provider}</c>. <see cref="Username"/> / <see cref="Password"/>
/// are only required for the password provider; other providers (guest, steam, discord) ignore them.
/// </summary>
/// <param name="Username">Username — required for password provider; null otherwise.</param>
/// <param name="Password">Plaintext password — required for password provider; null otherwise.</param>
public sealed record LoginRequest(string? Username, string? Password);
