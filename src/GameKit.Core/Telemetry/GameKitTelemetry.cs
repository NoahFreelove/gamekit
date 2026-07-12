// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.Telemetry;

/// <summary>
/// Single source of truth for all GameKit OpenTelemetry <c>ActivitySource</c> and <c>Meter</c>
/// names, the shared <see cref="Version"/> string, and the D-04 low-cardinality span attribute
/// key constants.
/// </summary>
/// <remarks>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> spans and metrics emitted by GameKit packages
/// are no-ops unless the host registers the sources and meters via the OpenTelemetry SDK. The
/// recommended integration point is <c>AddGameKitObservability()</c> on <c>IGameKitBuilder</c>
/// (see <c>GameKitObservabilityBuilderExtensions</c>), which calls
/// <c>AddSource(GameKitTelemetry.MatchmakingTickerSourceName)</c> and
/// <c>AddMeter(GameKitTelemetry.MatchmakingMeterName)</c> for you.
/// </para>
/// <para>
/// Operators who manage their own <c>TracerProvider</c> or <c>MeterProvider</c> can reference
/// these constants directly:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t =&gt; t
///         .AddSource(GameKitTelemetry.MatchmakingTickerSourceName)
///         .AddSource(GameKitTelemetry.RankingsTickerSourceName))
///     .WithMetrics(m =&gt; m
///         .AddMeter(GameKitTelemetry.MatchmakingMeterName));
/// </code>
/// </para>
/// <para>
/// <b>PII guardrail (OBS-07):</b> D-04 attribute key constants are restricted to
/// <b>low-cardinality, non-PII</b> identifiers. High-cardinality identifiers such as
/// <c>player.id</c> or <c>ticket.id</c> are deliberately NOT defined here; the
/// <c>PiiAttributeAnalyzer</c> (GK0001) enforces this at build time.
/// </para>
/// </remarks>
public static class GameKitTelemetry
{
    // ── Shared version ────────────────────────────────────────────────────────

    /// <summary>
    /// Version string used by every GameKit <c>ActivitySource</c> and <c>Meter</c> instance,
    /// pinned to <c>"1.0.0"</c> for v1 wire compatibility across the coordinated release train.
    /// </summary>
    public const string Version = "1.0.0";

    // ── Source prefix ─────────────────────────────────────────────────────────

    /// <summary>
    /// Common prefix for all GameKit ActivitySource names (D-01).
    /// Per-package names extend this prefix: <c>"GameKit.Matchmaking.Ticker"</c>,
    /// <c>"GameKit.Rankings.Ticker"</c>, etc.
    /// </summary>
    public const string SourcePrefix = "GameKit";

    // ── ActivitySource names ──────────────────────────────────────────────────

    /// <summary>
    /// <c>ActivitySource</c> name for the matchmaker ticker (Plan 05-05).
    /// Operators MUST call <c>AddSource("GameKit.Matchmaking.Ticker")</c> to subscribe.
    /// </summary>
    /// <remarks>
    /// Equals <c>MatchmakingActivitySource.SourceName</c> in <c>GameKit.Matchmaking</c>.
    /// The reflection enforcement test in <c>GameKitTelemetryConstantsTests</c> asserts
    /// value-equality between these two constants at runtime, catching drift.
    /// </remarks>
    public const string MatchmakingTickerSourceName = "GameKit.Matchmaking.Ticker";

    /// <summary>
    /// <c>ActivitySource</c> name for the rankings ticker (Plan 04-07, extracted in Plan 13-02).
    /// Operators MUST call <c>AddSource("GameKit.Rankings.Ticker")</c> to subscribe.
    /// </summary>
    /// <remarks>
    /// Equals <c>RankingsActivitySource.SourceName</c> in <c>GameKit.Rankings</c>.
    /// The reflection enforcement test in <c>GameKitTelemetryConstantsTests</c> asserts
    /// value-equality between these two constants at runtime, catching drift.
    /// </remarks>
    public const string RankingsTickerSourceName = "GameKit.Rankings.Ticker";

    // ── Meter names ───────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Meter</c> name for <c>GameKit.Matchmaking</c> diagnostics (analytics dropped-events
    /// counter). Operators MUST call <c>AddMeter("GameKit.Matchmaking")</c> to subscribe.
    /// </summary>
    /// <remarks>
    /// Equals <c>MatchmakingMeter.MeterName</c> in <c>GameKit.Matchmaking</c>.
    /// The reflection enforcement test in <c>GameKitTelemetryConstantsTests</c> asserts
    /// value-equality at runtime, catching drift.
    /// </remarks>
    public const string MatchmakingMeterName = "GameKit.Matchmaking";

    // ── D-04 low-cardinality span attribute key constants ─────────────────────
    //
    // These keys are safe to log as span attributes because they identify low-cardinality
    // dimensions (ladder configuration, pool names, regions, status codes) rather than
    // personally-identifiable data. High-cardinality identifiers (player.id, ticket.id)
    // are deliberately NOT defined here — see PiiAttributeAnalyzer (GK0001).

    /// <summary>Span attribute key for the ladder identifier (e.g., <c>"main"</c>, <c>"ranked"</c>).</summary>
    public const string AttrLadderId = "ladder.id";

    /// <summary>Span attribute key for the matchmaking pool name (e.g., <c>"main.default"</c>, <c>"main.us-east"</c>).</summary>
    public const string AttrPoolName = "pool.name";

    /// <summary>Span attribute key for the human-readable ladder name.</summary>
    public const string AttrLadderName = "ladder.name";

    /// <summary>Span attribute key for the geographic region (e.g., <c>"us-east"</c>, <c>"eu-west"</c>).</summary>
    public const string AttrRegion = "region";

    /// <summary>Span attribute key for the status of an operation (e.g., <c>"ok"</c>, <c>"degraded"</c>, <c>"down"</c>).</summary>
    public const string AttrStatus = "status";

    /// <summary>Span attribute key for the result of an operation (e.g., <c>"match_formed"</c>, <c>"timeout"</c>).</summary>
    public const string AttrResult = "result";

    /// <summary>
    /// Span attribute key for the error type, following OpenTelemetry semantic convention
    /// <c>"error.type"</c>. Use for exception type names or short error codes.
    /// </summary>
    public const string AttrErrorType = "error.type";

    // ── Phase 15 additions ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>ActivitySource</c> name for GameKit.Lobby SignalR hub instrumentation (OBS-05).
    /// Operators MUST call <c>AddSource("GameKit.Lobby")</c> to subscribe.
    /// </summary>
    /// <remarks>
    /// Equals <c>LobbyActivitySource.SourceName</c> in <c>GameKit.Lobby</c>.
    /// The reflection enforcement test in <c>GameKitTelemetryConstantsTests</c> asserts
    /// value-equality between these two constants at runtime, catching drift.
    /// </remarks>
    public const string LobbySourceName = "GameKit.Lobby";

    /// <summary>
    /// <c>Meter</c> name for <c>GameKit.Rankings</c> diagnostics (decay duration, rows updated).
    /// Operators MUST call <c>AddMeter("GameKit.Rankings")</c> to subscribe.
    /// </summary>
    /// <remarks>
    /// Equals <c>RankingsMeter.MeterName</c> in <c>GameKit.Rankings</c>.
    /// The reflection enforcement test in <c>GameKitTelemetryConstantsTests</c> asserts
    /// value-equality at runtime, catching drift.
    /// </remarks>
    public const string RankingsMeterName = "GameKit.Rankings";

    /// <summary>
    /// <c>Meter</c> name for <c>GameKit.Lobby</c> diagnostics (connected clients, messages,
    /// ready-checks). Operators MUST call <c>AddMeter("GameKit.Lobby")</c> to subscribe.
    /// </summary>
    /// <remarks>
    /// Equals <c>LobbyMeter.MeterName</c> in <c>GameKit.Lobby</c>.
    /// The reflection enforcement test in <c>GameKitTelemetryConstantsTests</c> asserts
    /// value-equality at runtime, catching drift.
    /// </remarks>
    public const string LobbyMeterName = "GameKit.Lobby";

    /// <summary>
    /// Span/metric attribute key for the result of a ready-check operation.
    /// Low-cardinality values: <c>"all_ready"</c>, <c>"timeout"</c>, <c>"cancelled"</c>.
    /// </summary>
    /// <remarks>
    /// Operator action required: this attribute is emitted by <c>LobbyHub.MarkReadyAsync</c>
    /// on the <c>lobby.ready_check.completed</c> counter (OBS-05). The GK0001 PII analyzer
    /// confirms this key carries only low-cardinality enum results, not player identifiers.
    /// </remarks>
    public const string AttrCheckResult = "check.result";
}
