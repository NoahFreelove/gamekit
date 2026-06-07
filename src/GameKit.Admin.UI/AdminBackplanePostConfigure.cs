// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Admin.UI;

/// <summary>
/// Defers <see cref="IConnectionMultiplexer"/> resolution into the SignalR Redis
/// backplane <see cref="RedisOptions.ConnectionFactory"/> so that <c>AddGameKitAdmin()</c>
/// does NOT call <c>BuildServiceProvider()</c> at registration time and does NOT register
/// a second multiplexer.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a Singleton <see cref="IPostConfigureOptions{T}"/> in <c>AddGameKitAdmin()</c>
/// via <c>TryAddEnumerable</c> so that a second registration from <c>AddLobby()</c> stacks
/// idempotently — both set <c>ConnectionFactory</c> to the same <see cref="IConnectionMultiplexer"/>
/// instance (ADMIN-13 Pitfall 1 / T-12-04-SC mitigation).
/// </para>
/// <para>
/// At startup the options system calls <see cref="PostConfigure"/> after the DI container
/// is fully built, at which point <see cref="IConnectionMultiplexer"/> is resolvable from
/// the consumer's registrations. When no <see cref="IConnectionMultiplexer"/> is registered
/// (single-instance install without Redis), <see cref="PostConfigure"/> returns without setting
/// <see cref="RedisOptions.ConnectionFactory"/>, leaving SignalR's default in-process backplane
/// intact (CR-01 fix).
/// </para>
/// <para>
/// <c>IConnectionMultiplexer</c> is a consumer-provided Singleton; <c>AddGameKitAdmin()</c>
/// MUST NOT register its own (RESEARCH §Anti-Patterns).
/// </para>
/// </remarks>
internal sealed class AdminBackplanePostConfigure : IPostConfigureOptions<RedisOptions>
{
    private readonly IServiceProvider _sp;

    /// <summary>Constructs the post-configurator.</summary>
    /// <param name="sp">The application service provider used to resolve <see cref="IConnectionMultiplexer"/> at startup.</param>
    public AdminBackplanePostConfigure(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        _sp = sp;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, RedisOptions options)
    {
        var mux = _sp.GetService<IConnectionMultiplexer>();
        if (mux is null) return;  // single-instance install — in-process backplane only
        options.ConnectionFactory = _ => Task.FromResult(mux);
    }
}
