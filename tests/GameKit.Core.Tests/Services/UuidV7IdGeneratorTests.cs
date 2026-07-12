// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Services;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class UuidV7IdGeneratorTests
{
    [Fact]
    public void NewId_ReturnsNonEmptyGuid()
    {
        var gen = new UuidV7IdGenerator();
        var id = gen.NewId();
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void NewId_ReturnsDifferentIds()
    {
        var gen = new UuidV7IdGenerator();
        var id1 = gen.NewId();
        var id2 = gen.NewId();
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void NewId_ReturnsVersion7Guid()
    {
        var gen = new UuidV7IdGenerator();
        var id = gen.NewId();
        Assert.Equal(7, id.Version);
    }
}
