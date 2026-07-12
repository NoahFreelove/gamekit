// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Lobby;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// W-1 guard tests: verifies that <see cref="LobbyRedisBackplanePostConfigure"/> fails fast
/// with a clear, actionable <see cref="InvalidOperationException"/> when no
/// <c>IConnectionMultiplexer</c> has been registered in the consumer's DI container.
/// These tests are service-collection / unit-shaped — no Testcontainers, no Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RedisRequirementTests
{
    [Fact(DisplayName = "W-1: PostConfigure throws InvalidOperationException when IConnectionMultiplexer is unregistered")]
    public void PostConfigure_ThrowsInvalidOperationException_WhenNoMultiplexerRegistered()
    {
        // Build a minimal ServiceProvider with NO IConnectionMultiplexer registered.
        var sp = new ServiceCollection().BuildServiceProvider();
        var sut = new LobbyRedisBackplanePostConfigure(sp);

        var ex = Assert.Throws<InvalidOperationException>(
            () => sut.PostConfigure(null, new RedisOptions()));

        Assert.NotNull(ex);
    }

    [Fact(DisplayName = "W-1: InvalidOperationException message names IConnectionMultiplexer")]
    public void PostConfigure_ErrorMessage_NamesIConnectionMultiplexer()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var sut = new LobbyRedisBackplanePostConfigure(sp);

        var ex = Assert.Throws<InvalidOperationException>(
            () => sut.PostConfigure(null, new RedisOptions()));

        Assert.Contains("IConnectionMultiplexer", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "W-1: InvalidOperationException message names AddLobby and explains registration pattern")]
    public void PostConfigure_ErrorMessage_NamesAddLobby_AndExplainsRegistration()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var sut = new LobbyRedisBackplanePostConfigure(sp);

        var ex = Assert.Throws<InvalidOperationException>(
            () => sut.PostConfigure(null, new RedisOptions()));

        Assert.Contains("AddLobby", ex.Message, StringComparison.Ordinal);
        // The message must tell the consumer HOW to fix it (the ConnectionMultiplexer.Connect pattern).
        Assert.Contains("ConnectionMultiplexer.Connect", ex.Message, StringComparison.Ordinal);
    }
}
