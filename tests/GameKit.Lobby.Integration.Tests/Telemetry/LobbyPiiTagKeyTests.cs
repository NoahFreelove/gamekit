// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using Xunit;

namespace GameKit.Lobby.Integration.Tests.Telemetry;

/// <summary>
/// OBS-05 criterion #1 enforcement: no instrument emitted by <c>GameKit.Lobby</c>
/// carries a PII-bearing tag key.
/// </summary>
/// <remarks>
/// Wave-0 stub: <c>LobbyMeter</c> does not yet exist (ships in Plan 05).
/// The MeterListener is filtered on the meter name string literal
/// <c>"GameKit.Lobby"</c> (== <see cref="GameKitTelemetry.LobbyMeterName"/>) and
/// exercises no instruments — the empty-set assertion passes trivially.
/// <para>
/// Placed in <c>GameKit.Lobby.Integration.Tests</c> (NOT a new GameKit.Lobby.Tests project)
/// because the Integration.Tests project already holds the
/// <c>[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]</c> grant in
/// <c>GameKit.Lobby/AssemblyInfo.cs</c>, giving test access to internal Lobby types
/// including the future <c>LobbyMeter</c> class.
/// </para>
/// <para>
/// TODO(15-05): reference <c>LobbyMeter</c> and add
/// <c>MessagesSent.Add(...)</c> + <c>ReadyCheckStarted.Add(...)</c> +
/// <c>ReadyCheckCompleted.Add(...)</c> calls once Plan 05 ships
/// <c>GameKit.Lobby.Telemetry.LobbyMeter</c>.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
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

    [Fact]
    public void NoInstrument_EmitsTagKey_MatchingForbiddenSet()
    {
        var emittedTagKeys = new List<string>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                // Filter on string literal — LobbyMeter does not yet exist (Plan 05)
                if (instr.Meter.Name == GameKitTelemetry.LobbyMeterName)
                    l.EnableMeasurementEvents(instr);
            },
        };

        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
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

        // TODO(15-05): add LobbyMeter.MessagesSent.Add(...) + LobbyMeter.ReadyCheckStarted.Add(...)
        // + LobbyMeter.ReadyCheckCompleted.Add(...) + LobbyMeter.ConnectedClients gauge
        // once Plan 05 ships GameKit.Lobby.Telemetry.LobbyMeter.
        listener.RecordObservableInstruments();

        Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k));
    }
}
