// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Lobby.Data;
using GameKit.Lobby.Health;
using GameKit.Lobby.Http.Contracts;
using GameKit.Lobby.Services;
using GameKit.Lobby.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
    ///         <c>AddLobby()</c> REQUIRES a consumer-registered <see cref="IConnectionMultiplexer"/>
    ///         (LOBBY-06 mandates the Redis backplane; Azure SignalR is not supported). A missing
    ///         registration fails fast at startup with a clear, actionable
    ///         <see cref="InvalidOperationException"/> naming the missing service and the registration
    ///         pattern (<c>services.AddSingleton&lt;IConnectionMultiplexer&gt;(ConnectionMultiplexer.Connect(...))</c>
    ///         before <c>AddLobby()</c>). The backplane multiplexer is wired via
    ///         <see cref="LobbyRedisBackplanePostConfigure"/> at startup time so no second
    ///         <see cref="IConnectionMultiplexer"/> is registered.</item>
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
        // 3a. Lobby migration readiness reporter — reports whether __ef_migrations_lobby
        //     migrations are all applied. Registered as an enumerable singleton so the Core
        //     aggregate "migrations" health check discovers all six IMigrationReadinessReporter
        //     implementations.
        builder.Services.AddSingleton<IMigrationReadinessReporter, LobbyMigrationReadinessReporter>();

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

        // 9. OBS-05: register the LobbyConnectionTracker singleton + wire the ConnectedClients
        //    ObservableGauge. The tracker is injected into LobbyHub; a startup IHostedService
        //    resolves it from DI and calls LobbyMeter.Init(tracker) once at StartAsync.
        //    This mirrors the MatchmakingMeterInitService pattern (Plan 15-02).
        builder.Services.AddSingleton<LobbyConnectionTracker>();
        builder.Services.AddHostedService<LobbyMeterInitService>();

        return builder;
    }
}

/// <summary>
/// Minimal <see cref="IHostedService"/> that calls <see cref="LobbyMeter.Init"/> once at host
/// startup so the <c>lobby.connected_clients</c>
/// <see cref="System.Diagnostics.Metrics.ObservableGauge{T}"/> callback has a reference to the
/// singleton <see cref="LobbyConnectionTracker"/> before the first OTel scrape (OBS-05).
/// </summary>
/// <remarks>
/// Registered by <see cref="LobbyBuilderExtensions.AddLobby"/> as a hosted service. The service
/// resolves <see cref="LobbyConnectionTracker"/> lazily from DI (avoids eagerly constructing the
/// singleton during <c>ConfigureServices</c>) and calls <c>LobbyMeter.Init</c> once at
/// <see cref="StartAsync"/>. <see cref="StopAsync"/> is a no-op.
/// </remarks>
internal sealed class LobbyMeterInitService : IHostedService
{
    private readonly LobbyConnectionTracker _tracker;

    /// <summary>Constructs the init service.</summary>
    /// <param name="tracker">The singleton connection tracker.</param>
    public LobbyMeterInitService(LobbyConnectionTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // OBS-05: wires the ConnectedClients ObservableGauge to the singleton tracker.
        LobbyMeter.Init(_tracker);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
