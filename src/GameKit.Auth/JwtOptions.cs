// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth;

/// <summary>JWT issuance + validation options. See RESEARCH §8.9 for PEM permission guidance.</summary>
public sealed class JwtOptions
{
    /// <summary>JWT <c>iss</c> claim. Required. Typically the deployment's public URL or app identifier.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>JWT <c>aud</c> claim. Required. Typically matches the API host.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to an RSA private key PEM file (server-side signing key).
    /// MUST be mode 0600 / readable only by the process owner. Never embed in appsettings.json.
    /// </summary>
    public string PrivateKeyPemPath { get; set; } = string.Empty;

    /// <summary>Absolute path to the matching RSA public key PEM file (token validation).</summary>
    public string PublicKeyPemPath { get; set; } = string.Empty;

    /// <summary><c>kid</c> header claim — stable identifier for the active signing key (enables future key rotation).</summary>
    public string Kid { get; set; } = "gamekit-jwt-kid-1";

    /// <summary>Access-token lifetime. Default 15 minutes (CONTEXT D-01).</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Refresh-token lifetime. Default 30 days (CONTEXT D-02).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Reuse-interval grace window — a refresh attempt arriving within this duration of the parent's
    /// <c>UsedAt</c> timestamp, WITH matching client fingerprint, receives the already-issued child
    /// instead of triggering family revocation (CONTEXT D-05, D-06). Default 45 s per RESEARCH §6.4.
    /// </summary>
    public TimeSpan RefreshReuseInterval { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Clock-skew tolerance on the validator. Default 30 seconds (OWASP).</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
