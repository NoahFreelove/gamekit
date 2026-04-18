// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth;

/// <summary>Username/password policy (AUTH-09). Default BCrypt work factor = 12.</summary>
public sealed class PasswordOptions
{
    /// <summary>BCrypt work factor. Default 12 (CONTEXT discretion). Higher = slower + more secure.</summary>
    public int BCryptWorkFactor { get; set; } = 12;

    /// <summary>Username regex (RFC-lite default: 3-32 chars, alphanumeric + underscore + hyphen).</summary>
    public string UsernameRegex { get; set; } = "^[a-zA-Z0-9_-]{3,32}$";

    /// <summary>Minimum password length. Default 12.</summary>
    public int MinPasswordLength { get; set; } = 12;
}
