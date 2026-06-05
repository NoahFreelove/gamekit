// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Epic.Configuration;

/// <summary>
/// Configuration options for the GameKit Epic Games OAuth provider.
/// Pass to <c>AddEpic(o => { ... })</c> on your <c>IGameKitBuilder</c>.
/// </summary>
public sealed class GameKitEpicOptions
{
    /// <summary>
    /// Epic Online Services (EOS) client ID obtained from the Epic Games Dev Portal.
    /// When <see langword="null"/> or empty, the Epic authentication scheme is NOT registered
    /// but the <c>IOAuthProvider</c> is still resolvable from DI (test-harness safety).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Epic Online Services (EOS) client secret obtained from the Epic Games Dev Portal.
    /// Must be supplied together with <see cref="ClientId"/> for the scheme to activate.
    /// This value is sent as HTTP Basic auth credentials to the token endpoint — it is never
    /// sent as a form field (T-07-05-01 mitigation).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// OAuth2 callback path registered in the Epic Games Dev Portal as the redirect URI.
    /// Defaults to <c>/signin-epic</c>.
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-epic";
}
