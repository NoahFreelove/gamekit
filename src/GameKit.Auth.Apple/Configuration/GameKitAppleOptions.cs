// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Apple.Configuration;

/// <summary>
/// Configuration options for the GameKit Apple Sign-In provider.
/// Pass to <c>AddApple(o => { ... })</c> on your <c>IGameKitBuilder</c>.
/// </summary>
/// <remarks>
/// <b>Security:</b> Never bake <see cref="PrivateKeyBase64"/> into a container image or source
/// repository. Load it from an environment variable, a secrets manager, or a mounted secret.
/// The .p8 key grants the ability to generate valid Apple client secrets; its exposure is equivalent
/// to a long-lived bearer credential.
/// </remarks>
public sealed class GameKitAppleOptions
{
    /// <summary>
    /// The Apple Services ID (used as <c>ClientId</c> in the Apple OAuth2 handshake).
    /// This is the reverse-domain identifier created in the Apple Developer Portal under
    /// <em>Identifiers → Service IDs</em> (e.g. <c>com.example.gamekit</c>).
    /// When <see langword="null"/> or empty, the Apple authentication scheme is NOT registered
    /// but the <c>IOAuthProvider</c> is still resolvable from DI (test-harness safety,
    /// T-07-04-05 mitigation).
    /// </summary>
    public string? ServiceId { get; set; }

    /// <summary>
    /// The Apple Developer Team ID (10-character alphanumeric, visible in the Apple Developer
    /// Portal top-right corner). Required for ES256 client-secret generation.
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// The Key ID of the Sign-In-with-Apple key (.p8) created in the Apple Developer Portal
    /// under <em>Keys</em>. Required for ES256 client-secret generation.
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Base64-encoded content of the Apple .p8 private key file downloaded from the Apple
    /// Developer Portal when creating a Sign-In-with-Apple key.
    /// <para>
    /// <b>SECURITY: Never bake this value into source code or container images.</b>
    /// Load from <c>GAMEKIT_APPLE_PRIVATEKEY_BASE64</c> environment variable (or equivalent
    /// secrets-manager injection) at startup.
    /// </para>
    /// Must be supplied together with <see cref="ServiceId"/>, <see cref="TeamId"/>, and
    /// <see cref="KeyId"/> for the Apple authentication scheme to activate.
    /// </summary>
    public string? PrivateKeyBase64 { get; set; }

    /// <summary>
    /// OAuth2 callback path registered as the Return URL in the Apple Developer Portal
    /// Services ID configuration. Defaults to <c>/signin-apple</c>.
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-apple";

    /// <summary>
    /// Lifetime of the ES256 client secret JWT generated per token exchange.
    /// Apple caps client secrets at 6 months (180 days); this defaults to
    /// <c>170 days</c> to stay safely below the cap with an operational margin.
    /// <para>
    /// <b>Do NOT set this to 180 days or more.</b> Apple rejects client secrets at or
    /// beyond the 180-day boundary, producing an <c>invalid_client</c> error at the start
    /// of each token exchange (T-07-04-01 mitigation).
    /// </para>
    /// </summary>
    public TimeSpan ClientSecretExpiresAfter { get; set; } = TimeSpan.FromDays(170);
}
