// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/login</c>. The endpoint is anonymous + rate-limited
/// (<c>gamekit:admin:login</c>, 5/min/IP) and validates presence via
/// <see cref="Validators.LoginRequestValidator"/>.
/// </summary>
/// <param name="Username">Admin username (case-insensitive via the citext column).</param>
/// <param name="Password">Plaintext password submitted for <c>BCrypt.Verify</c>.</param>
/// <param name="RememberMe">When <c>true</c>, the issued cookie is persistent (30-day window per
/// <see cref="AdminCookieOptions.RememberMeDuration"/>); otherwise session-scoped (8-hour sliding).</param>
public sealed record LoginRequest(string Username, string Password, bool RememberMe);
