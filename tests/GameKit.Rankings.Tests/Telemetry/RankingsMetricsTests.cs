// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Rankings.Telemetry;
using Xunit;

namespace GameKit.Rankings.Tests.Telemetry;

/// <summary>
/// OBS-04 contract tests: <c>RankingsMeter.DecayDuration</c> and
/// <c>RankingsMeter.DecayRowsUpdated</c> produce measurements that are captured by a
/// <see cref="MeterListener"/> filtered on <c>GameKit.Rankings</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tests exercise the static instruments directly — no DB, no Redis, no service resolution.
/// The unit assertion is that the meter-level contract is satisfied: a caller invoking
/// <c>DecayDuration.Record(x)</c> causes a <c>double</c> measurement, and a caller invoking
/// <c>DecayRowsUpdated.Add(n)</c> causes a <c>long</c> measurement, on the
/// <c>GameKit.Rankings</c> meter.
/// </para>
/// <para>
/// End-to-end timing assertions (Stopwatch placed after lease acquisition) live in
/// <c>GameKit.Rankings.Integration.Tests</c> where a DbContext and Redis fixture are available.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
[Collection("RankingsMetrics")]
public sealed class RankingsMetricsTests
{
    [Fact]
    public void DecayDuration_Record_ProducesMeasurement_OnRankingsMeter()
    {
        // Arrange — use a distinctive sentinel value unlikely to collide with other test recordings.
        // xUnit runs test classes in parallel; static instruments are shared, so multiple
        // listeners may be active concurrently. Collect all captured values and verify the
        // sentinel appears (rather than asserting on last/only value).
        const double sentinel = 999_001.0;
        var capturedValues = new List<double>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.decay.duration")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) =>
        {
            capturedValues.Add(value);
        });

        // Start BEFORE exercising the instrument.
        listener.Start();

        // Act
        RankingsMeter.DecayDuration.Record(sentinel);

        // Assert — the sentinel must appear among captured measurements.
        Assert.Contains(capturedValues, v => Math.Abs(v - sentinel) < 1e-6);
    }

    [Fact]
    public void DecayRowsUpdated_Add_ProducesMeasurement_OnRankingsMeter()
    {
        // Arrange — use a distinctive sentinel value unlikely to collide with other test recordings.
        const long sentinel = 999_002L;
        var capturedValues = new List<long>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.decay.rows_updated")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            capturedValues.Add(value);
        });

        // Start BEFORE exercising the instrument.
        listener.Start();

        // Act
        RankingsMeter.DecayRowsUpdated.Add(sentinel);

        // Assert — the sentinel must appear among captured measurements.
        Assert.Contains(sentinel, capturedValues);
    }

    [Fact]
    public void DecayDuration_HasExpectedInstrumentMetadata()
    {
        // Verify instrument name, unit, and description (contract assertion).
        Histogram<double>? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, _) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.decay.duration" &&
                    instr is Histogram<double> h)
                    captured = h;
            },
        };
        listener.Start();

        // Force instrument discovery by recording a measurement.
        RankingsMeter.DecayDuration.Record(0);

        Assert.NotNull(captured);
        Assert.Equal("ms", captured.Unit);
    }

    [Fact]
    public void DecayRowsUpdated_HasExpectedInstrumentMetadata()
    {
        // Verify instrument name and unit.
        Counter<long>? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, _) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.decay.rows_updated" &&
                    instr is Counter<long> c)
                    captured = c;
            },
        };
        listener.Start();

        // Force instrument discovery by adding a value.
        RankingsMeter.DecayRowsUpdated.Add(0);

        Assert.NotNull(captured);
        Assert.Equal("rows", captured.Unit);
    }

    [Fact]
    public void LockAcquisitionFailures_Add_ProducesMeasurement_OnRankingsMeter()
    {
        // Arrange — use a distinctive sentinel value unlikely to collide with other test recordings.
        const long sentinel = 999_003L;
        var capturedValues = new List<long>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.leader_lock.acquisition_failures")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            capturedValues.Add(value);
        });

        // Start BEFORE exercising the instrument.
        listener.Start();

        // Act
        RankingsMeter.LockAcquisitionFailures.Add(sentinel);

        // Assert — the sentinel must appear among captured measurements.
        Assert.Contains(sentinel, capturedValues);
    }

    [Fact]
    public void LockAcquisitionFailures_HasExpectedInstrumentMetadata()
    {
        // Verify instrument name and unit (contract assertion — mirrors the
        // matchmaking.leader_lock.acquisition_failures naming convention).
        Counter<long>? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, _) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.leader_lock.acquisition_failures" &&
                    instr is Counter<long> c)
                    captured = c;
            },
        };
        listener.Start();

        // Force instrument discovery by adding a value.
        RankingsMeter.LockAcquisitionFailures.Add(0);

        Assert.NotNull(captured);
        Assert.Equal("failures", captured.Unit);
    }

    [Fact]
    public void NoForbiddenPiiTagKey_EmittedByAnyRankingsInstrument()
    {
        // OBS-04 criterion #1: no rankings instrument emits a PII tag key.
        // This companion test exercises both instruments with their natural (no-tag) signatures
        // and confirms the forbidden-key set remains empty — complementary to
        // RankingsPiiTagKeyTests which exercises the same property via the shared pattern.
        var emittedTagKeys = new List<string>();
        var forbiddenKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
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
        listener.Start();

        // Exercise all rankings instruments (no PII tags on any).
        RankingsMeter.DecayDuration.Record(1.0);
        RankingsMeter.DecayRowsUpdated.Add(1);
        RankingsMeter.LockAcquisitionFailures.Add(1);
        listener.RecordObservableInstruments();

        Assert.DoesNotContain(emittedTagKeys, k => forbiddenKeys.Contains(k));
    }
}
