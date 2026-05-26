// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Builder;
using GameKit.Core.Services;
using GameKit.Presence.Configuration;
using GameKit.Presence.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GameKit.Presence.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Presence</c> onto an existing
/// <see cref="IGameKitBuilder"/>. Declared <see langword="partial"/> so future plan files
/// (e.g. a hypothetical multi-device aggregator in v2) can add their own extension methods
/// without modifying this file — mirrors the Matchmaking partial-split convention
/// (PATTERNS Block 5).
/// </summary>
public static partial class PresenceBuilderExtensions
{
    /// <summary>
    /// Registers <c>GameKit.Presence</c> services:
    /// <list type="bullet">
    ///   <item>Options + validator — <see cref="GameKitPresenceOptions"/> validated by
    ///         <see cref="PresenceOptionsValidator"/> (fail-fast at host startup).</item>
    ///   <item><c>RedisPresenceProvider</c> as a Singleton registered against BOTH
    ///         <see cref="IPresenceProvider"/> (Core read port) and
    ///         <see cref="IPresenceWriter"/> (Presence-internal write port) — single
    ///         instance, two interfaces.</item>
    ///   <item><see cref="PresenceSessionObserver"/> via
    ///         <c>TryAddEnumerable&lt;ISessionLifecycleObserver&gt;</c> (Scoped) so it
    ///         coexists with any sibling observers a future package may add (CONTEXT D-21).</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The existing <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Optional callback to populate <see cref="GameKitPresenceOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>LIFETIME NOTE (intentional):</b> the Scoped <see cref="PresenceSessionObserver"/>
    /// consuming the Singleton <see cref="IPresenceWriter"/> is the SAFE direction of a
    /// lifetime mix — captive-dependency hazards arise only when a Singleton captures a
    /// Scoped (longer-lived holding shorter-lived), not the reverse. A Scoped service
    /// holding a Singleton reference is the canonical ASP.NET Core DI pattern (e.g. HTTP
    /// request services holding <c>ILogger</c>).
    /// </para>
    /// <para>
    /// The Redis <c>IConnectionMultiplexer</c> Singleton is the consumer's responsibility
    /// — mirror the Phase-5 Matchmaking convention. The sample <c>TicTacToeDuel/Program.cs</c>
    /// shows the recommended registration (operators tune ConfigurationOptions for TLS,
    /// AllowAdmin, AbortOnConnectFail).
    /// </para>
    /// </remarks>
    public static IGameKitBuilder AddPresence(
        this IGameKitBuilder builder,
        Action<GameKitPresenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Bind + validate options. ValidateOnStart guarantees the IValidateOptions runs
        //    at host startup — a misconfigured 3× safety factor fails fast before Kestrel
        //    accepts traffic.
        var optsBuilder = builder.Services.AddOptions<GameKitPresenceOptions>();
        if (configure is not null)
        {
            optsBuilder.Configure(configure);
        }
        optsBuilder.ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GameKitPresenceOptions>, PresenceOptionsValidator>());

        // 2. RedisPresenceProvider — register the concrete Singleton once, then route both
        //    Core's IPresenceProvider and Presence's IPresenceWriter to the same instance via
        //    factory registrations. This gives consumers a single Redis-multiplexer-backed
        //    read+write surface (PATTERNS Block 2).
        builder.Services.TryAddSingleton<RedisPresenceProvider>();
        builder.Services.TryAddSingleton<IPresenceProvider>(sp => sp.GetRequiredService<RedisPresenceProvider>());
        builder.Services.TryAddSingleton<IPresenceWriter>(sp => sp.GetRequiredService<RedisPresenceProvider>());

        // 3. PresenceSessionObserver — Scoped per CONTEXT D-21. TryAddEnumerable so multiple
        //    ISessionLifecycleObserver implementations coexist when sibling packages add
        //    their own observers in a future phase.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ISessionLifecycleObserver, PresenceSessionObserver>());

        return builder;
    }
}
