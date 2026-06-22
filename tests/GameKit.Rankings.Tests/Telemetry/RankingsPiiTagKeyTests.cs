// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using Xunit;

namespace GameKit.Rankings.Tests.Telemetry;

/// <summary>
/// OBS-04 criterion #1 enforcement: no instrument emitted by <c>GameKit.Rankings</c>
/// carries a PII-bearing tag key.
/// </summary>
/// <remarks>
/// Wave-0 stub: <c>RankingsMeter</c> does not yet exist (ships in Plan 04).
/// The MeterListener is filtered on the meter name string literal
/// <c>"GameKit.Rankings"</c> (== <see cref="GameKitTelemetry.RankingsMeterName"/>) and
/// exercises no instruments — the empty-set assertion passes trivially.
/// <para>
/// TODO(15-04): reference <c>RankingsMeter</c> and add
/// <c>DecayDuration.Record(...)</c> + <c>DecayRowsUpdated.Add(...)</c> calls once
/// Plan 04 ships the <c>GameKit.Rankings.Telemetry.RankingsMeter</c> class.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RankingsPiiTagKeyTests
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
                // Filter on string literal — RankingsMeter does not yet exist (Plan 04)
                if (instr.Meter.Name == GameKitTelemetry.RankingsMeterName)
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

        // TODO(15-04): add RankingsMeter.DecayDuration.Record(...) + RankingsMeter.DecayRowsUpdated.Add(...)
        // once Plan 04 ships GameKit.Rankings.Telemetry.RankingsMeter.
        listener.RecordObservableInstruments();

        Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k));
    }
}
