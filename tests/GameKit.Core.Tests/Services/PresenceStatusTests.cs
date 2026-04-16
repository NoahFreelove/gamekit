// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Services;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class PresenceStatusTests
{
    [Fact]
    public void Offline_IsZero()
    {
        Assert.Equal(0, (int)PresenceStatus.Offline);
    }

    [Fact]
    public void Online_IsOne()
    {
        Assert.Equal(1, (int)PresenceStatus.Online);
    }

    [Fact]
    public void InMatch_IsTwo()
    {
        Assert.Equal(2, (int)PresenceStatus.InMatch);
    }
}
