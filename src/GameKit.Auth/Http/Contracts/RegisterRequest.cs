// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Request body for <c>POST /auth/register</c>. When the caller is authenticated as a guest,
/// D-12 upgrade-in-place semantics apply and the endpoint delegates to
/// <see cref="Services.IGuestUpgradeService.UpgradeToPasswordAsync"/>.
/// </summary>
/// <param name="Username">Desired username (subject to <see cref="PasswordOptions.UsernameRegex"/>).</param>
/// <param name="Password">Plaintext password (hashed before persistence).</param>
/// <param name="DisplayName">Optional display name; falls back to <paramref name="Username"/> when absent.</param>
public sealed record RegisterRequest(string Username, string Password, string? DisplayName);
