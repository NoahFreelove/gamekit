// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using GameKit.Lobby.Telemetry;
using Xunit;

namespace GameKit.Lobby.Integration.Tests.Telemetry;

/// <summary>
/// OBS-05 criterion #1 enforcement: no instrument emitted by <c>GameKit.Lobby</c>
/// carries a PII-bearing tag key.
/// </summary>
/// <remarks>
/// <para>
/// Exercises every <see cref="LobbyMeter"/> instrument — <c>MessagesSent</c>,
/// <c>ReadyCheckStarted</c>, <c>ReadyCheckCompleted</c> (with <c>check.result</c> tag), and
/// the <c>ConnectedClients</c> ObservableGauge via <c>LobbyMeter.Init</c> +
/// <c>RecordObservableInstruments()</c> — and asserts that no emitted tag key is in the
/// forbidden PII set. The only expected tag key is <c>check.result</c>.
/// </para>
/// <para>
/// Placed in <c>GameKit.Lobby.Integration.Tests</c> (NOT a new GameKit.Lobby.Tests project)
/// because the Integration.Tests project already holds the
/// <c>[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]</c> grant in
/// <c>GameKit.Lobby/AssemblyInfo.cs</c>, giving test access to the internal
/// <see cref="LobbyMeter"/> class.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
[Collection("LobbyMeterTests")]
public sealed class LobbyPiiTagKeyTests
{
    private static readonly HashSet<string> ForbiddenKeys = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "ticketId", "ticket_id",
        "playerId", "player_id",
        "sessionId", "session_id",
        "matchId", "match_id",
        "userId", "user_id",
        "email",
        "token",
        "fingerprint",
    };

    /// <summary>
    /// Asserts that every lobby instrument emits only permitted tag keys.
    /// The only allowed tag key is <c>check.result</c> (==
    /// <see cref="GameKitTelemetry.AttrCheckResult"/>).
    /// </summary>
    [Fact]
    public void NoInstrument_EmitsTagKey_MatchingForbiddenSet()
    {
        var emittedTagKeys = new List<string>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == LobbyMeter.MeterName)
                    l.EnableMeasurementEvents(instr);
            },
        };

        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                emittedTagKeys.Add(tag.Key);
        });
        listener.SetMeasurementEventCallback<int>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                emittedTagKeys.Add(tag.Key);
        });
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                emittedTagKeys.Add(tag.Key);
        });

        // MUST call Start() BEFORE exercising instruments
        listener.Start();

        // Exercise all lobby counters with their allowed tag keys (OBS-05 criterion #1):
        LobbyMeter.MessagesSent.Add(1);
        LobbyMeter.ReadyCheckStarted.Add(1);
        LobbyMeter.ReadyCheckCompleted.Add(1,
            new System.Collections.Generic.KeyValuePair<string, object?>(
                GameKitTelemetry.AttrCheckResult, "all_ready"));

        // Wire the tracker and trigger the ConnectedClients ObservableGauge callback.
        LobbyMeter.Init(new LobbyConnectionTracker());
        listener.RecordObservableInstruments();

        // Only check.result is a permitted tag key; PII keys must never appear.
        Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k));
    }
}
