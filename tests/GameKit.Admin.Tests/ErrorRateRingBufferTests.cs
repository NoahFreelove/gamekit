// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Admin.UI;
using GameKit.Admin.UI.Services;
using Xunit;

namespace GameKit.Admin.Tests;

/// <summary>
/// Unit tests for <see cref="ErrorRateRingBuffer"/> using a controllable
/// <c>FakeClock</c> so decay behavior is deterministic.
/// </summary>
public class ErrorRateRingBufferTests
{
    private sealed class FakeClock : GameKit.Core.Services.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow = UtcNow.Add(d);
    }

    [Fact]
    public void IncrementError_Accumulates_WithinWindow()
    {
        var opts = new GameKitAdminOptions();
        var clock = new FakeClock();
        var buf = new ErrorRateRingBuffer(opts, clock);
        for (var i = 0; i < 7; i++)
        {
            buf.IncrementError();
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        Assert.Equal(7, buf.RecentErrorCount());
    }

    [Fact]
    public void ErrorsOlderThan_Window_Decay_ToZero()
    {
        var opts = new GameKitAdminOptions();  // 5-minute window, 1-second buckets
        var clock = new FakeClock();
        var buf = new ErrorRateRingBuffer(opts, clock);
        buf.IncrementError();
        buf.IncrementError();
        Assert.Equal(2, buf.RecentErrorCount());

        // Advance past the full window; all buckets should have been zeroed by rotation.
        clock.Advance(opts.Panel.HealthErrorRateWindow + TimeSpan.FromSeconds(1));
        Assert.Equal(0, buf.RecentErrorCount());
    }

    [Fact]
    public void PartialDecay_Keeps_Recent_Errors()
    {
        var opts = new GameKitAdminOptions();
        var clock = new FakeClock();
        var buf = new ErrorRateRingBuffer(opts, clock);
        // Old error
        buf.IncrementError();
        // Jump almost to the end of the window
        clock.Advance(opts.Panel.HealthErrorRateWindow - TimeSpan.FromSeconds(2));
        buf.IncrementError();
        Assert.Equal(2, buf.RecentErrorCount());

        // Slide forward so the first error falls out but the second remains
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(1, buf.RecentErrorCount());
    }
}
