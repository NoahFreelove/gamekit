// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace GameKit.Core.Builder;

/// <summary>
/// Options governing the <c>AddGameKitObservability()</c> builder extension.
/// </summary>
/// <remarks>
/// All fields are optional. Call <c>AddGameKitObservability()</c> with no callback to register
/// GameKit sources and meters without an OTLP exporter — useful when the host manages its own
/// <c>TracerProvider</c> configuration.
/// </remarks>
public sealed class GameKitObservabilityOptions
{
    /// <summary>
    /// OTLP exporter endpoint URI (e.g., <c>"http://localhost:4317"</c>).
    /// When <see langword="null"/> (the default), no OTLP exporter is registered; the host
    /// application is responsible for wiring an exporter to its <c>TracerProvider</c> and
    /// <c>MeterProvider</c>.
    /// </summary>
    public string? OtlpEndpoint { get; set; }
}

/// <summary>
/// Builder extension that registers all known GameKit <c>ActivitySource</c> and <c>Meter</c>
/// names with the host's OpenTelemetry SDK. Implements OBS-01 (opt-in SDK dependency) and
/// OBS-02 (centralized source/meter registration via <see cref="GameKitTelemetry"/> constants).
/// </summary>
/// <remarks>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> GameKit packages emit spans and metrics via
/// the in-box <c>System.Diagnostics.ActivitySource</c> and <c>System.Diagnostics.Metrics.Meter</c>
/// primitives, which are no-ops unless a listener is registered. Call
/// <c>AddGameKitObservability()</c> to register all known GameKit sources and meters with the
/// host's OpenTelemetry SDK in a single call.
/// </para>
/// <para>
/// <b>OBS-01 — no forced SDK:</b> the <c>OpenTelemetry.Extensions.Hosting</c> and
/// <c>OpenTelemetry.Exporter.OpenTelemetryProtocol</c> packages are declared with
/// <c>PrivateAssets="all"</c> in <c>GameKit.Core.csproj</c>. A consumer who does NOT call
/// <c>AddGameKitObservability()</c> will NOT pull the OTel SDK into their build graph.
/// </para>
/// <para>
/// Consumers who manage their own <c>TracerProvider</c> can reference the constants directly
/// instead of calling this method:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t =&gt; t
///         .AddSource(GameKitTelemetry.MatchmakingTickerSourceName)
///         .AddSource(GameKitTelemetry.RankingsTickerSourceName)
///         .AddSource(GameKitTelemetry.LobbySourceName))
///     .WithMetrics(m =&gt; m
///         .AddMeter(GameKitTelemetry.MatchmakingMeterName)
///         .AddMeter(GameKitTelemetry.RankingsMeterName)
///         .AddMeter(GameKitTelemetry.LobbyMeterName));
/// </code>
/// </para>
/// </remarks>
public static class GameKitObservabilityBuilderExtensions
{
    /// <summary>
    /// Registers all known GameKit <c>ActivitySource</c> and <c>Meter</c> names with the
    /// host's OpenTelemetry SDK, and optionally wires an OTLP exporter.
    /// </summary>
    /// <param name="builder">The existing <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">
    /// Optional callback to populate <see cref="GameKitObservabilityOptions"/>.
    /// Pass <see langword="null"/> (or omit) to register sources/meters only, without an exporter.
    /// </param>
    /// <returns>The same <see cref="IGameKitBuilder"/> for continued chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Sources registered: <c>"GameKit.Matchmaking.Ticker"</c>, <c>"GameKit.Rankings.Ticker"</c>,
    /// and <c>"GameKit.Lobby"</c> (added in Phase 15 — OBS-05).
    /// </para>
    /// <para>
    /// Meters registered: <c>"GameKit.Matchmaking"</c>, <c>"GameKit.Rankings"</c> (Phase 15 — OBS-04),
    /// and <c>"GameKit.Lobby"</c> (Phase 15 — OBS-05).
    /// </para>
    /// </remarks>
    public static IGameKitBuilder AddGameKitObservability(
        this IGameKitBuilder builder,
        Action<GameKitObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var opts = new GameKitObservabilityOptions();
        configure?.Invoke(opts);

        var otlpEndpoint = opts.OtlpEndpoint;

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(GameKitTelemetry.MatchmakingTickerSourceName)
                    .AddSource(GameKitTelemetry.RankingsTickerSourceName)
                    .AddSource(GameKitTelemetry.LobbySourceName);           // Phase 15 — OBS-05

                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(GameKitTelemetry.MatchmakingMeterName)
                    .AddMeter(GameKitTelemetry.RankingsMeterName)           // Phase 15 — OBS-04
                    .AddMeter(GameKitTelemetry.LobbyMeterName);             // Phase 15 — OBS-05

                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            });

        return builder;
    }
}
