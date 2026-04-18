// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;

namespace GameKit.Auth.Egress;

/// <summary>
/// Literal default host allow-list for the two named Auth HttpClients. Shipped as a public constant
/// (not a config default) so that a misconfigured <c>appsettings.json</c> cannot silently clear the list.
/// CONTEXT <c>&lt;specifics&gt;</c>: "The allow-list default must be a literal list in code..."
/// </summary>
public static class DefaultAllowedHosts
{
    /// <summary>The four hosts the default Steam + Discord providers must reach.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "steamcommunity.com",
        "api.steampowered.com",
        "discord.com",
        "discordapp.com",
    };
}
