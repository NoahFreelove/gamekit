// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Services;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentTime()
    {
        var clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var result = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(result, before, after);
    }

    [Fact]
    public void UtcNow_IsUtcKind()
    {
        var clock = new SystemClock();
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }
}
