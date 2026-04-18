// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net.Http;
using AspNet.Security.OAuth.Discord;
using GameKit.Auth.Egress;
using Microsoft.Extensions.Options;

namespace GameKit.Auth.Providers.Discord;

/// <summary>
/// Routes the aspnet-contrib Discord handler's <c>Options.Backchannel</c> through our named
/// HttpClient <c>gamekit.auth.provider.discord</c> so BOTH <c>ExchangeCodeAsync</c> and
/// <c>CreateTicketAsync</c> go through the <see cref="EgressAllowListHandler"/> + resilience
/// pipeline.
/// </summary>
/// <remarks>
/// Implemented as <see cref="IPostConfigureOptions{T}"/> because DI mutations of authentication
/// options must run AFTER the <c>.AddDiscord(...)</c> <c>Configure</c> callback (the callback
/// captures <c>Backchannel</c> by value at options-creation time and is not DI-aware). Scoping
/// the post-configuration to <see cref="DiscordAuthenticationOptions"/> (and NOT a global
/// <c>IPostConfigureOptions&lt;AuthenticationSchemeOptions&gt;</c>) prevents accidental
/// Backchannel overrides on sibling schemes.
/// </remarks>
internal sealed class DiscordBackchannelPostConfigure : IPostConfigureOptions<DiscordAuthenticationOptions>
{
    private readonly IHttpClientFactory _factory;

    /// <summary>Constructs the post-configurator.</summary>
    public DiscordBackchannelPostConfigure(IHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, DiscordAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Backchannel = _factory.CreateClient("gamekit.auth.provider.discord");
    }
}
