// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class GameKitOptionsTests
{
    [Fact]
    public void Defaults_ConnectionString_IsEmpty()
    {
        var opts = new GameKitOptions();
        Assert.Equal(string.Empty, opts.ConnectionString);
    }

    [Fact]
    public void Defaults_MigrationsConnectionString_IsNull()
    {
        var opts = new GameKitOptions();
        Assert.Null(opts.MigrationsConnectionString);
    }

    [Fact]
    public void Defaults_RedisConnectionString_IsNull()
    {
        var opts = new GameKitOptions();
        Assert.Null(opts.RedisConnectionString);
    }

    [Fact]
    public void Defaults_AutoMigrate_IsTrue()
    {
        var opts = new GameKitOptions();
        Assert.True(opts.AutoMigrate);
    }

    [Fact]
    public void Defaults_DeletedPlayerDisplayName_IsDeletedPlayer()
    {
        var opts = new GameKitOptions();
        Assert.Equal("Deleted Player", opts.DeletedPlayerDisplayName);
    }

    [Fact]
    public void ConnectionString_CanBeSet()
    {
        var opts = new GameKitOptions { ConnectionString = "Host=localhost" };
        Assert.Equal("Host=localhost", opts.ConnectionString);
    }

    [Fact]
    public void DeletedPlayerDisplayName_CanBeOverridden()
    {
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Anonymous" };
        Assert.Equal("Anonymous", opts.DeletedPlayerDisplayName);
    }
}
