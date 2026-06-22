// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using GameKit.Matchmaking.Telemetry;
using Xunit;

namespace GameKit.Matchmaking.Tests.Telemetry;

/// <summary>
/// OBS-04 metrics-behavior assertions for <c>GameKit.Matchmaking</c> instruments.
/// Verifies that <see cref="MatchmakingMeter"/> instruments emit measurements with the
/// correct values and tag keys at their respective call sites.
/// </summary>
[Trait("Category", "Unit")]
[Xunit.Collection("MatchmakingMeterTests")]
public sealed class MatchmakingMetricsTests
{
    /// <summary>
    /// <see cref="MatchmakingMeter.TickerLag"/> records the measurement value supplied
    /// via <c>Record(double)</c>.
    /// </summary>
    [Fact]
    public void TickerLag_Record_EmitsMeasurement()
    {
        double? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName && instr.Name == "matchmaking.ticker.lag")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => captured = value);
        listener.Start();

        MatchmakingMeter.TickerLag.Record(99.5);

        Assert.NotNull(captured);
        Assert.Equal(99.5, captured!.Value, precision: 3);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.LockAcquisitionFailures"/> increments by 1 on each
    /// <c>Add(1)</c> call; the MeterListener sees the delta value 1.
    /// </summary>
    [Fact]
    public void LockAcquisitionFailures_Add_EmitsMeasurement()
    {
        long? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.leader_lock.acquisition_failures")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => captured = value);
        listener.Start();

        MatchmakingMeter.LockAcquisitionFailures.Add(1);

        Assert.NotNull(captured);
        Assert.Equal(1L, captured!.Value);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.MatchesFormed"/> emits the <c>ladder.id</c> tag key
    /// with each measurement.
    /// </summary>
    [Fact]
    public void MatchesFormed_Add_EmitsLadderIdTag()
    {
        var capturedTags = new List<string>();
        long? capturedValue = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.matches.formed")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            capturedValue = value;
            foreach (var tag in tags)
                capturedTags.Add(tag.Key);
        });
        listener.Start();

        var ladderId = Guid.NewGuid().ToString();
        MatchmakingMeter.MatchesFormed.Add(1,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, ladderId));

        Assert.NotNull(capturedValue);
        Assert.Equal(1L, capturedValue!.Value);
        Assert.Contains(GameKitTelemetry.AttrLadderId, capturedTags);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.BudgetBail"/> emits the <c>ladder.id</c> tag key
    /// with each measurement.
    /// </summary>
    [Fact]
    public void BudgetBail_Add_EmitsLadderIdTag()
    {
        var capturedTags = new List<string>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.budget_bail")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                capturedTags.Add(tag.Key);
        });
        listener.Start();

        MatchmakingMeter.BudgetBail.Add(1,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, "test-ladder"));

        Assert.Contains(GameKitTelemetry.AttrLadderId, capturedTags);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.PoolSweepDuration"/> emits the <c>ladder.id</c> tag key
    /// with each histogram recording.
    /// </summary>
    [Fact]
    public void PoolSweepDuration_Record_EmitsLadderIdTag()
    {
        var capturedTags = new List<string>();
        double? capturedValue = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.pool_sweep.duration")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            capturedValue = value;
            foreach (var tag in tags)
                capturedTags.Add(tag.Key);
        });
        listener.Start();

        MatchmakingMeter.PoolSweepDuration.Record(7.8,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, "test-ladder"));

        Assert.NotNull(capturedValue);
        Assert.Equal(7.8, capturedValue!.Value, precision: 3);
        Assert.Contains(GameKitTelemetry.AttrLadderId, capturedTags);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.QueueDepth"/> ObservableGauge callback fires without
    /// throwing when <c>_multiplexer</c> is null (Init not called at unit-test time). The
    /// callback yields no measurements — it must not propagate a NullReferenceException
    /// out of <see cref="MeterListener.RecordObservableInstruments"/>.
    /// </summary>
    [Fact]
    public void QueueDepth_ObservableGauge_WhenInitNotCalled_YieldsNoMeasurements()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.queue.depth")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        // Must not throw — the callback guards on _multiplexer == null.
        var ex = Record.Exception(() => listener.RecordObservableInstruments());

        Assert.Null(ex);
        // No measurements because Init was never called (null _multiplexer guard).
        Assert.Empty(measurements);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.LeaseAcquired"/> increments by 1 on each <c>Add(1)</c> call.
    /// </summary>
    [Fact]
    public void LeaseAcquired_Add_EmitsMeasurement()
    {
        long? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.lease.acquired")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => captured = value);
        listener.Start();

        MatchmakingMeter.LeaseAcquired.Add(1);

        Assert.NotNull(captured);
        Assert.Equal(1L, captured!.Value);
    }

    /// <summary>
    /// <see cref="MatchmakingMeter.LeaseLost"/> increments by 1 on each <c>Add(1)</c> call.
    /// </summary>
    [Fact]
    public void LeaseLost_Add_EmitsMeasurement()
    {
        long? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName
                    && instr.Name == "matchmaking.lease.lost")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => captured = value);
        listener.Start();

        MatchmakingMeter.LeaseLost.Add(1);

        Assert.NotNull(captured);
        Assert.Equal(1L, captured!.Value);
    }
}
