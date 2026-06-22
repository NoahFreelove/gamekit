// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using GameKit.Rankings.Telemetry;
using Xunit;

namespace GameKit.Rankings.Tests.Telemetry;

/// <summary>
/// OBS-04 criterion #1 enforcement: no instrument emitted by <c>GameKit.Rankings</c>
/// carries a PII-bearing tag key.
/// </summary>
/// <remarks>
/// <para>
/// Wires a <see cref="MeterListener"/> filtered on <see cref="RankingsMeter.MeterName"/>
/// (<c>"GameKit.Rankings"</c> == <see cref="GameKitTelemetry.RankingsMeterName"/>),
/// exercises every rankings instrument (<c>DecayDuration.Record</c> and
/// <c>DecayRowsUpdated.Add</c>), and asserts that no emitted tag key appears in the
/// GK0001 forbidden set.
/// </para>
/// <para>
/// Plan 04: stub promoted to full test — <see cref="RankingsMeter"/> now exists and
/// both instruments are exercised. No forbidden PII tags are expected on either instrument
/// (T-15-04-PII mitigation).
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
                if (instr.Meter.Name == RankingsMeter.MeterName)
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

        // MUST call Start() BEFORE exercising instruments.
        listener.Start();

        // Exercise all rankings instruments (DecayDuration + DecayRowsUpdated).
        // Neither instrument emits PII tag keys — T-15-04-PII mitigation.
        RankingsMeter.DecayDuration.Record(1.0);
        RankingsMeter.DecayRowsUpdated.Add(1);
        listener.RecordObservableInstruments();

        Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k));
    }
}
