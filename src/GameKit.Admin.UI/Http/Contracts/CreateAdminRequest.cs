// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/admins</c> (superadmin-only). The username regex,
/// role enum, and password min-length are enforced by
/// <see cref="Validators.CreateAdminRequestValidator"/> BEFORE the service pays the BCrypt
/// hash cost (T-02-27-style mitigation).
/// </summary>
/// <param name="Username">New admin username. Must match <c>^[a-z0-9_-]{3,32}$</c> per D-06.</param>
/// <param name="Password">Plaintext password (min 8 chars; hashed server-side via <c>BCryptPasswordHasher</c>).</param>
/// <param name="Role">Either <c>"admin"</c> or <c>"superadmin"</c> (ck_admin_users_role CHECK constraint).</param>
public sealed record CreateAdminRequest(string Username, string Password, string Role);
