// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Google.Configuration;

/// <summary>
/// Configuration options for the GameKit Google OAuth provider.
/// Pass to <c>AddGoogle(o => { ... })</c> on your <c>IGameKitBuilder</c>.
/// </summary>
public sealed class GameKitGoogleOptions
{
    /// <summary>
    /// Google OAuth2 client ID from the Google Cloud Console.
    /// When <see langword="null"/> or empty, the Google authentication scheme is NOT registered
    /// but the <c>IOAuthProvider</c> is still resolvable from DI (test-harness safety).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Google OAuth2 client secret from the Google Cloud Console.
    /// Must be supplied together with <see cref="ClientId"/> for the scheme to activate.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// OAuth2 callback path registered in the Google Cloud Console.
    /// Defaults to <c>/signin-google</c>.
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-google";
}
