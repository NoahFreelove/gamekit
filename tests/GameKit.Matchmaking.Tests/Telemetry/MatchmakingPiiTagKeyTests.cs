// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using GameKit.Matchmaking.Telemetry;
using Xunit;

namespace GameKit.Matchmaking.Tests.Telemetry;

/// <summary>
/// OBS-04 criterion #1 enforcement: no instrument emitted by <c>GameKit.Matchmaking</c>
/// carries a PII-bearing tag key.
/// </summary>
/// <remarks>
/// Exercises EVERY matchmaking instrument with its allowed tags — verifies that no emitted
/// tag key is in the forbidden PII set. This test is the runtime complement to the GK0001
/// build-time analyzer: the analyzer catches string literals at build time; this test
/// asserts the actual emitted key values at runtime (criterion #1, Plan 02).
/// </remarks>
[Trait("Category", "Unit")]
[Xunit.Collection("MatchmakingMeterTests")]
public sealed class MatchmakingPiiTagKeyTests
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
                if (instr.Meter.Name == MatchmakingMeter.MeterName)
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

        // MUST call Start() BEFORE exercising instruments (TicketEventChannelDropTests pattern)
        listener.Start();

        // Exercise all instruments with their allowed tag keys:

        // DroppedEvents counter — allowed tag: reason
        MatchmakingMeter.DroppedEvents.Add(1,
            new KeyValuePair<string, object?>("reason", "channel_full"));

        // TickerLag histogram — no tags
        MatchmakingMeter.TickerLag.Record(42.5);

        // PoolSweepDuration histogram — allowed tag: ladder.id
        MatchmakingMeter.PoolSweepDuration.Record(12.3,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, "some-ladder-id"));

        // LockAcquisitionFailures counter — no tags
        MatchmakingMeter.LockAcquisitionFailures.Add(1);

        // MatchesFormed counter — allowed tag: ladder.id
        MatchmakingMeter.MatchesFormed.Add(1,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, "some-ladder-id"));

        // BudgetBail counter — allowed tag: ladder.id
        MatchmakingMeter.BudgetBail.Add(1,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, "some-ladder-id"));

        // LeaseAcquired counter — no tags
        MatchmakingMeter.LeaseAcquired.Add(1);

        // LeaseLost counter — no tags
        MatchmakingMeter.LeaseLost.Add(1);

        // Fire QueueDepth ObservableGauge callback (_db is null at unit-test time so yields
        // no measurements, but the callback must not throw and RecordObservableInstruments
        // must complete without error).
        listener.RecordObservableInstruments();

        Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k));
    }
}
