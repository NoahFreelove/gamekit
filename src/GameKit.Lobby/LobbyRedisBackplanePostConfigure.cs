// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Lobby;

/// <summary>
/// Defers <see cref="IConnectionMultiplexer"/> resolution into the SignalR Redis
/// backplane <see cref="RedisOptions.ConnectionFactory"/> so that <c>AddLobby()</c> does
/// NOT call <c>BuildServiceProvider()</c> at registration time and does NOT register a
/// second multiplexer.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a Singleton <see cref="IPostConfigureOptions{T}"/> in <c>AddLobby()</c>.
/// At startup the options system calls <see cref="PostConfigure"/> after the DI container
/// is fully built, at which point <see cref="IConnectionMultiplexer"/> is resolvable from
/// the consumer's registrations.
/// </para>
/// <para>
/// <c>IConnectionMultiplexer</c> is a consumer-provided Singleton; <c>AddLobby()</c> MUST
/// NOT register its own (RESEARCH §Anti-Patterns). If no <c>IConnectionMultiplexer</c> has
/// been registered, <see cref="PostConfigure"/> throws a clear <see cref="InvalidOperationException"/>
/// at startup with an actionable message naming the missing service and how to fix it.
/// </para>
/// </remarks>
internal sealed class LobbyRedisBackplanePostConfigure : IPostConfigureOptions<RedisOptions>
{
    private readonly IServiceProvider _sp;

    /// <summary>Constructs the post-configurator.</summary>
    public LobbyRedisBackplanePostConfigure(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        _sp = sp;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, RedisOptions options)
    {
        var mux = _sp.GetService<IConnectionMultiplexer>()
            ?? throw new InvalidOperationException(
                "GameKit.Lobby requires a registered IConnectionMultiplexer because AddLobby() " +
                "mandates a SignalR Redis backplane (LOBBY-06; Azure SignalR is not supported). " +
                "Register a multiplexer BEFORE calling AddLobby(), for example: " +
                "services.AddSingleton<IConnectionMultiplexer>(" +
                "ConnectionMultiplexer.Connect(\"<your-redis-connection-string>\"))");

        options.ConnectionFactory = _ => Task.FromResult(mux);
    }
}
