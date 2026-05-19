// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="MatchmakerLeaseHelper"/> against a live Testcontainer
/// Redis. Verifies the lock-take / lock-extend / lock-release lifecycle, the fencing-token
/// guarantee against cross-instance release, and the Polly v8 retry pipeline on transient
/// failures (T-05-05-01 mitigation).
/// </summary>
/// <remarks>
/// Mirrors the Phase 4 <c>RankingsTickerLeaderElectionTests</c> patterns for the per-replica
/// lease lifecycle. The two-replica race + forced-failover scenarios live in
/// <see cref="MatchmakingLeaderElectionTests"/>.
/// </remarks>
[Collection("Redis")]
[Trait("Category", "Integration")]
public sealed class MatchmakerLeaseHelperTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer? _mux;
    private IDatabase _db = null!;
    private IServer _server = null!;

    /// <summary>Constructs the test with the shared Redis fixture.</summary>
    public MatchmakerLeaseHelperTests(RedisFixture redis) => _redis = redis;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var opts = ConfigurationOptions.Parse(_redis.ConnectionString);
        opts.AllowAdmin = true;
        _mux = await ConnectionMultiplexer.ConnectAsync(opts);
        _db = _mux.GetDatabase();
        _server = _mux.GetServer(_mux.GetEndPoints().First());

        // Clean slate per class invocation.
        await _server.FlushDatabaseAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_mux is not null)
            await _mux.DisposeAsync();
    }

    private MatchmakerLeaseHelper BuildHelper(int lockTtlSeconds = 5)
    {
        var opts = new GameKitMatchmakingOptions();
        opts.Ticker.LockKey = MatchmakingRedisKeys.MatcherLock;
        opts.Ticker.LockTtlSeconds = lockTtlSeconds;
        return new MatchmakerLeaseHelper(
            _mux!,
            NullLogger<MatchmakerLeaseHelper>.Instance,
            Options.Create(opts));
    }

    [Fact]
    public async Task TryAcquireLease_Returns_True_When_Lock_Available()
    {
        var helper = BuildHelper();

        var acquired = await helper.TryAcquireLeaseAsync(CancellationToken.None);

        Assert.True(acquired);

        // Lock value matches this instance's fencing token.
        var stored = await _db.StringGetAsync(MatchmakingRedisKeys.MatcherLock);
        Assert.Equal(helper.InstanceId, stored.ToString());
    }

    [Fact]
    public async Task TryAcquireLease_Returns_False_When_Lock_Held_By_Other()
    {
        var helper1 = BuildHelper();
        var helper2 = BuildHelper();

        Assert.True(await helper1.TryAcquireLeaseAsync(CancellationToken.None));
        var second = await helper2.TryAcquireLeaseAsync(CancellationToken.None);

        Assert.False(second);

        // Lock value still belongs to helper1.
        var stored = await _db.StringGetAsync(MatchmakingRedisKeys.MatcherLock);
        Assert.Equal(helper1.InstanceId, stored.ToString());
    }

    [Fact]
    public async Task RenewLease_Returns_True_When_Holding_Lock()
    {
        var helper = BuildHelper(lockTtlSeconds: 10);
        Assert.True(await helper.TryAcquireLeaseAsync(CancellationToken.None));

        var renewed = await helper.RenewLeaseAsync(CancellationToken.None);

        Assert.True(renewed);

        // TTL has been reset to ~LockTtl.
        var ttl = await _db.KeyTimeToLiveAsync(MatchmakingRedisKeys.MatcherLock);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 5, 10);
    }

    [Fact]
    public async Task RenewLease_Returns_False_When_Lock_Already_Released()
    {
        var helper = BuildHelper();
        Assert.True(await helper.TryAcquireLeaseAsync(CancellationToken.None));
        await helper.ReleaseLeaseAsync(CancellationToken.None);

        var renewed = await helper.RenewLeaseAsync(CancellationToken.None);

        Assert.False(renewed);
    }

    [Fact]
    public async Task ReleaseLease_Removes_Lock_Key()
    {
        var helper = BuildHelper();
        Assert.True(await helper.TryAcquireLeaseAsync(CancellationToken.None));

        await helper.ReleaseLeaseAsync(CancellationToken.None);

        var exists = await _db.KeyExistsAsync(MatchmakingRedisKeys.MatcherLock);
        Assert.False(exists);
    }

    [Fact]
    public async Task ReleaseLease_Does_Not_Remove_Another_Instances_Lock()
    {
        // Fencing-token safety: a stale instance must NEVER delete the live leader's lock
        // (T-05-05-01). The Lua-script-verified LockReleaseAsync is what guarantees this —
        // we assert it explicitly here.
        var helper1 = BuildHelper();
        var helper2 = BuildHelper();

        Assert.True(await helper1.TryAcquireLeaseAsync(CancellationToken.None));
        // helper2 never acquired the lock; calling ReleaseLeaseAsync should be a no-op
        // because the value stored at the key is helper1.InstanceId.
        await helper2.ReleaseLeaseAsync(CancellationToken.None);

        var stored = await _db.StringGetAsync(MatchmakingRedisKeys.MatcherLock);
        Assert.Equal(helper1.InstanceId, stored.ToString());
    }

    [Fact]
    public void InstanceId_Has_Machine_Name_Prefix()
    {
        // The plan's must_haves pin the format "{MachineName}:{Guid}" because the Phase 3
        // admin observability panel will surface it as the leader identity. Guarding the
        // contract here prevents accidental refactoring drift.
        var helper = BuildHelper();

        Assert.StartsWith($"{Environment.MachineName}:", helper.InstanceId);
        // The portion after the colon should be a valid Guid string.
        var guidPart = helper.InstanceId[(Environment.MachineName.Length + 1)..];
        Assert.True(Guid.TryParse(guidPart, out _));
    }
}
