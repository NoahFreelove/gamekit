// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using FluentValidation;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Lobby.Data;
using GameKit.Lobby.Http.Contracts;
using GameKit.Lobby.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Lobby.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Lobby</c> onto an existing
/// <see cref="IGameKitBuilder"/>.
/// </summary>
public static class LobbyBuilderExtensions
{
    /// <summary>
    /// Registers <c>GameKit.Lobby</c> services:
    /// <list type="bullet">
    ///   <item>Options + validator: <see cref="GameKitLobbyOptions"/> fail-fast at host startup.</item>
    ///   <item><c>LobbyModelBuilderExtension</c> via <c>TryAddEnumerable</c> so Lobby entities
    ///         land in <c>GameKitDbContext</c> at runtime.</item>
    ///   <item><c>LobbyMigrationHostedService</c> — applies <c>__ef_migrations_lobby</c> at
    ///         startup under the per-package advisory-lock key (<c>12178347L</c>, Plan 11-01).</item>
    ///   <item>SignalR with StackExchange Redis backplane (ChannelPrefix <c>"GameKit"</c>).
    ///         The backplane multiplexer is wired via
    ///         <see cref="LobbyRedisBackplanePostConfigure"/> at startup time so no second
    ///         <see cref="IConnectionMultiplexer"/> is registered (LOBBY-06).</item>
    ///   <item>JWT Bearer WebSocket query-string token extraction scoped to <c>/hubs/lobby</c>
    ///         via <see cref="LobbyJwtBearerPostConfigure"/> (SC#2 / T-11-03-01).</item>
    ///   <item><see cref="ILobbyService"/> → <c>LobbyService</c> (scoped).</item>
    ///   <item><see cref="ILobbyMessageHandler"/> → <c>NullLobbyMessageHandler</c> (singleton
    ///         default, replaceable by consumers).</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The existing <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Optional callback to override <see cref="GameKitLobbyOptions"/> defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IGameKitBuilder AddLobby(
        this IGameKitBuilder builder,
        Action<GameKitLobbyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Options + validation — fail-fast at host startup.
        var optsBuilder = builder.Services.AddOptions<GameKitLobbyOptions>();
        if (configure is not null)
            optsBuilder.Configure(configure);
        optsBuilder.ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GameKitLobbyOptions>, LobbyOptionsValidator>());

        // 2. Lobby model extension — contributes lobbies + lobby_members to GameKitDbContext.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, LobbyModelBuilderExtension>());

        // 3. Migration runner — applies __ef_migrations_lobby under the Lobby advisory-lock key.
        builder.Services.AddHostedService<LobbyMigrationHostedService>();

        // 4. SignalR + Redis backplane (ChannelPrefix pinned in code — LOBBY-06 / Pitfall §3).
        //    AddSignalR().AddStackExchangeRedis is chained — AddStackExchangeRedis extends
        //    ISignalRServerBuilder (RESEARCH Pitfall §3).
        builder.Services.AddSignalR()
            .AddStackExchangeRedis(options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
            });

        // IPostConfigureOptions<RedisOptions> defers IConnectionMultiplexer resolution to
        // after the DI container is fully built — avoids BuildServiceProvider() at registration.
        // TryAddEnumerable is idempotent under double AddLobby() — consistent with all other
        // IPostConfigureOptions registrations in this file (WR-03).
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<RedisOptions>, LobbyRedisBackplanePostConfigure>());

        // 5. JWT Bearer WebSocket query-string token extraction (SC#2 / T-11-03-01).
        //    TryAddEnumerable allows future packages to chain additional handlers without collision.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, LobbyJwtBearerPostConfigure>());

        // 6. Lobby service (scoped — accesses the scoped GameKitDbContext).
        builder.Services.AddScoped<ILobbyService, LobbyService>();

        // 7. Optional relay seam — no-op default; consumers replace via TryAddSingleton BEFORE AddLobby.
        builder.Services.TryAddSingleton<ILobbyMessageHandler, NullLobbyMessageHandler>();

        // 8. FluentValidation validators for REST request DTOs.
        builder.Services.AddScoped<IValidator<CreateLobbyRequest>, CreateLobbyRequestValidator>();
        builder.Services.AddScoped<IValidator<JoinLobbyRequest>, JoinLobbyRequestValidator>();

        return builder;
    }
}
