// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics.Metrics;

namespace GameKit.Matchmaking.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="Meter"/> for <c>GameKit.Matchmaking</c> diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Exposes the <c>matchmaking.analytics.dropped_events</c> counter (D-16) incremented by
/// <see cref="GameKit.Matchmaking.Services.MatchmakingAnalyticsDrainService"/> when a
/// <see cref="GameKit.Matchmaking.Entities.TicketEvent"/> batch is dropped — either because
/// the bounded <see cref="System.Threading.Channels.Channel{T}"/> is full
/// (<c>reason=channel_full</c>) or because the Polly retry pipeline exhausted on a
/// sustained Postgres outage (<c>reason=polly_exhausted</c>).
/// </para>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> OpenTelemetry instruments are no-ops unless the
/// host application registers <c>AddMeter("GameKit.Matchmaking")</c> in its OpenTelemetry SDK
/// configuration. Without this registration, increments to
/// <see cref="DroppedEvents"/> are discarded silently — operators will not see the alerting
/// signal during a Postgres outage. The XML doc on
/// <c>MatchmakingBuilderExtensions.AddMatchmaking</c> repeats this guidance.
/// </para>
/// <para>
/// Declared <see langword="internal"/> so external code cannot mutate the static instance;
/// <c>InternalsVisibleTo</c> grants in <c>AssemblyInfo.cs</c> let the Matchmaking test
/// assemblies subscribe a <see cref="MeterListener"/> for verification.
/// </para>
/// </remarks>
internal static class MatchmakingMeter
{
    /// <summary>The Matchmaking meter name. Operators must register <c>AddMeter</c> with this exact value.</summary>
    public const string MeterName = "GameKit.Matchmaking";

    /// <summary>The meter version, pinned to <c>1.0.0</c> for v1 wire compatibility.</summary>
    public const string MeterVersion = "1.0.0";

    /// <summary>The <see cref="Meter"/> instance backing every Matchmaking counter / histogram.</summary>
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    /// <summary>
    /// Counter tracking the number of <see cref="GameKit.Matchmaking.Entities.TicketEvent"/>
    /// instances dropped without being persisted to Postgres.
    /// </summary>
    /// <remarks>
    /// <para>Tags:</para>
    /// <list type="bullet">
    ///   <item><c>reason=channel_full</c> — the bounded <see cref="System.Threading.Channels.Channel{T}"/>
    ///         rejected the write because the producer was faster than the drain (D-15).</item>
    ///   <item><c>reason=polly_exhausted</c> — the drain service's Polly retry pipeline gave up after
    ///         the configured maximum attempts on a sustained Postgres outage (D-16).</item>
    /// </list>
    /// </remarks>
    public static readonly Counter<long> DroppedEvents = Meter.CreateCounter<long>(
        name: "matchmaking.analytics.dropped_events",
        unit: "events",
        description: "Count of TicketEvents dropped due to bounded-channel-full or Polly retry exhaustion");
}
