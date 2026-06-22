// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Matchmaking.Telemetry;
using Xunit;

namespace GameKit.Matchmaking.Tests.Telemetry;

/// <summary>
/// OBS-04 criterion #1 enforcement: no instrument emitted by <c>GameKit.Matchmaking</c>
/// carries a PII-bearing tag key.
/// </summary>
/// <remarks>
/// Wave-0 stub: currently exercises the already-existing
/// <see cref="MatchmakingMeter.DroppedEvents"/> counter so the test compiles and passes today.
/// <para>
/// TODO(15-02): add new instrument Add/Record calls once Plan 02 ships the new
/// MatchmakingMeter instruments (TickerLag, PoolSweepDuration, QueueDepth,
/// LockAcquisitionFailures, MatchesFormed, BudgetBail, LeaseAcquired, LeaseLost).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
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

        // Exercise existing instrument with allowed tag key (reason=channel_full)
        MatchmakingMeter.DroppedEvents.Add(1,
            new System.Collections.Generic.KeyValuePair<string, object?>("reason", "channel_full"));

        listener.RecordObservableInstruments();

        Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k));
    }
}
