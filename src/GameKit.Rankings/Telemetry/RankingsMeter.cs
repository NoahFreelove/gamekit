// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics.Metrics;

namespace GameKit.Rankings.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="Meter"/> for <c>GameKit.Rankings</c> diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Exposes the <c>rankings.decay.duration</c> histogram (OBS-04) recording the wall-clock
/// duration of a single <c>RankDecayBackgroundService.RunOnceAsync</c> decay run, measured
/// after lease acquisition and before lease release (Pitfall 5 — lock-wait time excluded).
/// Also exposes the <c>rankings.decay.rows_updated</c> counter tracking how many
/// <c>player_ranks</c> rows were updated per run.
/// </para>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> OpenTelemetry instruments are no-ops unless
/// the host application registers <c>AddMeter("GameKit.Rankings")</c> in its OpenTelemetry SDK
/// configuration. Without this registration, histogram recordings and counter increments are
/// discarded silently. The XML doc on <c>RankingsBuilderExtensions.AddRankings</c> repeats
/// this guidance.
/// </para>
/// <para>
/// Declared <see langword="internal"/> so external code cannot mutate the static instance;
/// <c>InternalsVisibleTo</c> grants in <c>AssemblyInfo.cs</c> let the Rankings test assemblies
/// subscribe a <see cref="MeterListener"/> for verification.
/// </para>
/// </remarks>
internal static class RankingsMeter
{
    /// <summary>
    /// The Rankings meter name. Operators must register <c>AddMeter</c> with this exact value.
    /// </summary>
    /// <remarks>
    /// Value equals <c>GameKitTelemetry.RankingsMeterName</c> — the reflection Fact in
    /// <c>GameKit.Core.Tests.Telemetry.GameKitTelemetryConstantsTests</c> asserts this at
    /// test time to catch drift between the constant and the meter name.
    /// </remarks>
    public const string MeterName = "GameKit.Rankings"; // must equal GameKitTelemetry.RankingsMeterName

    /// <summary>The meter version, pinned to <c>1.0.0</c> for v1 wire compatibility.</summary>
    public const string MeterVersion = "1.0.0";

    /// <summary>The <see cref="Meter"/> instance backing every Rankings histogram / counter.</summary>
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    /// <summary>
    /// Histogram recording the wall-clock duration of a single
    /// <c>RankDecayBackgroundService.RunOnceAsync</c> decay run, measured AFTER lease
    /// acquisition and BEFORE lease release (ms).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pitfall 5 compliance: the <c>Stopwatch</c> is started immediately after
    /// <c>TryAcquireLeaseAsync</c> returns <see langword="true"/>, so the recorded value
    /// reflects actual decay work time — not Redis lock-wait contention.
    /// </para>
    /// <para>
    /// <b>Operator action required:</b> operators MUST call <c>AddMeter("GameKit.Rankings")</c>
    /// in their OpenTelemetry SDK setup to receive this histogram (Pitfall §7).
    /// </para>
    /// </remarks>
    public static readonly Histogram<double> DecayDuration = Meter.CreateHistogram<double>(
        name: "rankings.decay.duration",
        unit: "ms",
        description: "Wall-clock duration of one RankDecayBackgroundService.RunOnceAsync decay run (post-lease, pre-release)");

    /// <summary>
    /// Counter tracking the number of <c>player_ranks</c> rows updated in a single decay run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Incremented once per <c>DecayLadderAsync</c> call that processed at least one candidate,
    /// with the value equal to the number of rows saved (<c>candidates.Count</c> at
    /// <c>SaveChangesAsync</c>).
    /// </para>
    /// <para>
    /// <b>Operator action required:</b> operators MUST call <c>AddMeter("GameKit.Rankings")</c>
    /// in their OpenTelemetry SDK setup to receive this counter (Pitfall §7).
    /// </para>
    /// </remarks>
    public static readonly Counter<long> DecayRowsUpdated = Meter.CreateCounter<long>(
        name: "rankings.decay.rows_updated",
        unit: "rows",
        description: "Count of player_ranks rows updated per RankDecayBackgroundService decay run");
}
