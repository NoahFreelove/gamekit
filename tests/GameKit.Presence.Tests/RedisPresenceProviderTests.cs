// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Presence;
using GameKit.Presence.Configuration;
using GameKit.Presence.Services;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Presence.Tests;

/// <summary>
/// Unit tests for <see cref="RedisPresenceProvider"/> — covers the read path
/// (Offline/Online/InMatch detection), the SCAN-based online enumeration (defensive
/// Guid parse + take cap), and the write path (atomic Lua heartbeat, plain SET for
/// in-match / online / clear-in-match).
/// </summary>
/// <remarks>
/// Uses Moq to fake <see cref="IConnectionMultiplexer"/> + <see cref="IDatabase"/> +
/// <see cref="IServer"/> so the tests run without a Redis container. Integration tests
/// in <c>GameKit.Presence.Integration.Tests</c> exercise the same provider against a
/// real Testcontainers Redis (Plan 06-04 Task 3).
/// </remarks>
public sealed class RedisPresenceProviderTests
{
    private static readonly Guid PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (Mock<IConnectionMultiplexer> mux, Mock<IDatabase> db) NewMux()
    {
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        return (mux, db);
    }

    private static RedisPresenceProvider NewSut(Mock<IConnectionMultiplexer> mux) =>
        new(mux.Object, Options.Create(new GameKitPresenceOptions()));

    // ---- Read path ----

    [Fact]
    public async Task GetStatusAsync_ReturnsOffline_WhenKeyAbsent()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisValue.Null);

        var status = await NewSut(mux).GetStatusAsync(PlayerId);

        Assert.Equal(PresenceStatus.Offline, status);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsOnline_WhenValueIsOnline()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(PresenceValues.Online);

        var status = await NewSut(mux).GetStatusAsync(PlayerId);

        Assert.Equal(PresenceStatus.Online, status);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsInMatch_WhenValueIsInMatch()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(PresenceValues.InMatch);

        var status = await NewSut(mux).GetStatusAsync(PlayerId);

        Assert.Equal(PresenceStatus.InMatch, status);
    }

    // ---- SCAN path ----

    [Fact]
    public async Task GetOnlinePlayerIdsAsync_SkipsNonGuidSuffixes()
    {
        var (mux, db) = NewMux();
        var server = new Mock<IServer>(MockBehavior.Loose);
        mux.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
           .Returns(new System.Net.EndPoint[] { new DnsEndPoint("localhost", 6379) });
        mux.Setup(m => m.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object?>()))
           .Returns(server.Object);

        var validGuid = Guid.NewGuid();
        var keys = new RedisKey[]
        {
            (RedisKey)$"presence:{validGuid}",
            (RedisKey)"presence:not-a-guid",
            (RedisKey)"presence:",
        };
        server.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(ToAsyncEnumerable(keys));

        var result = await NewSut(mux).GetOnlinePlayerIdsAsync(take: 10);

        Assert.Single(result);
        Assert.Equal(validGuid, result[0]);
    }

    [Fact]
    public async Task GetOnlinePlayerIdsAsync_RespectsTakeCap()
    {
        var (mux, db) = NewMux();
        var server = new Mock<IServer>(MockBehavior.Loose);
        mux.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
           .Returns(new System.Net.EndPoint[] { new DnsEndPoint("localhost", 6379) });
        mux.Setup(m => m.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object?>()))
           .Returns(server.Object);

        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var keys = ids.Select(g => (RedisKey)$"presence:{g}").ToArray();
        server.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(ToAsyncEnumerable(keys));

        var result = await NewSut(mux).GetOnlinePlayerIdsAsync(take: 3);

        Assert.Equal(3, result.Count);
    }

    // ---- Write path ----

    [Fact]
    public async Task WriteHeartbeatAsync_InvokesScriptEvaluateAsync_WithKeyAndTtl()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisResult.Create(1));

        await NewSut(mux).WriteHeartbeatAsync(PlayerId, default);

        db.Verify(d => d.ScriptEvaluateAsync(
            It.Is<string>(s => s.Contains("in_match") && s.Contains("PEXPIRE") && s.Contains("'online'")),
            It.Is<RedisKey[]>(k => k.Length == 1 && k[0] == (RedisKey)PresenceRedisKeys.Player(PlayerId)),
            It.Is<RedisValue[]>(v => v.Length == 1 && (long)v[0] == 30L * 1000L),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteInMatchAsync_InvokesStringSetAsync_WithInMatchValueAndTtl()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await NewSut(mux).WriteInMatchAsync(PlayerId, default);

        db.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == (RedisKey)PresenceRedisKeys.Player(PlayerId)),
            It.Is<RedisValue>(v => v == (RedisValue)PresenceValues.InMatch),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value == TimeSpan.FromSeconds(30)),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteOnlineAsync_InvokesStringSetAsync_WithOnlineValueAndTtl()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await NewSut(mux).WriteOnlineAsync(PlayerId, default);

        db.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == (RedisKey)PresenceRedisKeys.Player(PlayerId)),
            It.Is<RedisValue>(v => v == (RedisValue)PresenceValues.Online),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value == TimeSpan.FromSeconds(30)),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearInMatchAsync_InvokesStringSetAsync_WithOnlineValueAndTtl()
    {
        var (mux, db) = NewMux();
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await NewSut(mux).ClearInMatchAsync(PlayerId, default);

        // ClearInMatchAsync overwrites in_match with online (refreshing TTL) — equivalent to
        // WriteOnlineAsync but semantically distinct at the observer-call site.
        db.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == (RedisKey)PresenceRedisKeys.Player(PlayerId)),
            It.Is<RedisValue>(v => v == (RedisValue)PresenceValues.Online),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value == TimeSpan.FromSeconds(30)),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    private static async System.Collections.Generic.IAsyncEnumerable<RedisKey> ToAsyncEnumerable(RedisKey[] keys)
    {
        foreach (var k in keys)
        {
            yield return k;
        }
        await Task.CompletedTask;
    }
}
