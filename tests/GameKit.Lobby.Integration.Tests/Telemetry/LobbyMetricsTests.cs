// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using GameKit.Lobby.Telemetry;
using Xunit;

namespace GameKit.Lobby.Integration.Tests.Telemetry;

/// <summary>
/// OBS-05 metrics-behavior assertions for <c>GameKit.Lobby</c> instruments.
/// Verifies that <see cref="LobbyMeter"/> instruments emit measurements with the correct values
/// and tag keys at their respective call sites.
/// </summary>
/// <remarks>
/// These tests drive the static instruments directly — no SignalR host, no DB, no Redis needed.
/// All tests carry <c>[Trait("Category","Unit")]</c>.
/// </remarks>
[Trait("Category", "Unit")]
[Collection("LobbyMeterTests")]
public sealed class LobbyMetricsTests
{
    /// <summary>
    /// The <c>lobby.connected_clients</c> ObservableGauge reflects <see cref="LobbyConnectionTracker"/>
    /// Increment and Decrement operations.
    /// </summary>
    [Fact]
    public void ConnectedClients_Gauge_ReflectsTrackerIncrementDecrement()
    {
        int? captured = null;

        var tracker = new LobbyConnectionTracker();
        LobbyMeter.Init(tracker);

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == LobbyMeter.MeterName
                    && instr.Name == "lobby.connected_clients")
                {
                    l.EnableMeasurementEvents(instr);
                }
            },
        };
        listener.SetMeasurementEventCallback<int>((_, value, _, _) => captured = value);
        listener.Start();

        // Increment twice → gauge should read 2.
        tracker.Increment();
        tracker.Increment();
        listener.RecordObservableInstruments();

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Value);

        // Decrement once → gauge should read 1.
        tracker.Decrement();
        captured = null;
        listener.RecordObservableInstruments();

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Value);
    }

    /// <summary>
    /// <see cref="LobbyMeter.MessagesSent"/> emits a measurement of value 1 on
    /// <c>Add(1)</c> — no tag keys.
    /// </summary>
    [Fact]
    public void MessagesSent_Add_EmitsMeasurement()
    {
        long? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == LobbyMeter.MeterName && instr.Name == "lobby.messages.sent")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => captured = value);
        listener.Start();

        LobbyMeter.MessagesSent.Add(1);

        Assert.NotNull(captured);
        Assert.Equal(1L, captured!.Value);
    }

    /// <summary>
    /// <see cref="LobbyMeter.ReadyCheckStarted"/> emits a measurement of value 1 on
    /// <c>Add(1)</c> — no tag keys.
    /// </summary>
    [Fact]
    public void ReadyCheckStarted_Add_EmitsMeasurement()
    {
        long? captured = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == LobbyMeter.MeterName && instr.Name == "lobby.ready_check.started")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => captured = value);
        listener.Start();

        LobbyMeter.ReadyCheckStarted.Add(1);

        Assert.NotNull(captured);
        Assert.Equal(1L, captured!.Value);
    }

    /// <summary>
    /// <see cref="LobbyMeter.ReadyCheckCompleted"/> emits a measurement of value 1 and carries
    /// the <c>check.result</c> tag (== <see cref="GameKitTelemetry.AttrCheckResult"/>).
    /// No PII tag keys are present.
    /// </summary>
    [Fact]
    public void ReadyCheckCompleted_Add_EmitsWithCheckResultTag()
    {
        long? capturedValue = null;
        var capturedTags = new List<KeyValuePair<string, object?>>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == LobbyMeter.MeterName && instr.Name == "lobby.ready_check.completed")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            capturedValue = value;
            foreach (var tag in tags)
                capturedTags.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
        });
        listener.Start();

        LobbyMeter.ReadyCheckCompleted.Add(1,
            new KeyValuePair<string, object?>(GameKitTelemetry.AttrCheckResult, "all_ready"));

        Assert.NotNull(capturedValue);
        Assert.Equal(1L, capturedValue!.Value);
        Assert.Single(capturedTags);
        Assert.Equal(GameKitTelemetry.AttrCheckResult, capturedTags[0].Key);
        Assert.Equal("all_ready", capturedTags[0].Value);
    }
}
