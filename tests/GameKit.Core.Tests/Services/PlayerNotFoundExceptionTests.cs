// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Services;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class PlayerNotFoundExceptionTests
{
    [Fact]
    public void Constructor_SetsPlayerId()
    {
        var id = Guid.NewGuid();
        var ex = new PlayerNotFoundException(id);
        Assert.Equal(id, ex.PlayerId);
    }

    [Fact]
    public void Constructor_SetsMessage()
    {
        var id = Guid.NewGuid();
        var ex = new PlayerNotFoundException(id);
        Assert.Contains(id.ToString(), ex.Message);
    }
}
