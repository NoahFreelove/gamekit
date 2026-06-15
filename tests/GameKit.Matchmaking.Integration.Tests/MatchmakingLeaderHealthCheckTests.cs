// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Health;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="MatchmakingLeaderHealthCheck"/> against a live Testcontainer
/// Redis. Verifies the three-state health probe contract:
/// <list type="bullet">
///   <item><description><c>Healthy</c> when this replica holds the lock (HLTH-04: holder InstanceId + TTL surfaced).</description></item>
///   <item><description><c>Degraded</c> (never <c>Unhealthy</c>) when another replica holds the lock (HLTH-03).</description></item>
///   <item><description><c>Degraded</c> when the lock is unheld (HLTH-03).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Mirrors <see cref="MatchmakerLeaseHelperTests"/> for the fixture lifecycle pattern:
/// <c>[Collection("Redis")]</c>, <c>IAsyncLifetime</c>, <c>ConnectionMultiplexer.ConnectAsync</c>
/// with <c>AllowAdmin=true</c>, and <c>FlushDatabaseAsync</c> for clean-slate isolation.
/// </remarks>
[Collection("Redis")]
[Trait("Category", "Integration")]
public sealed class MatchmakingLeaderHealthCheckTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer? _mux;

    /// <summary>Constructs the test class with the shared Redis fixture.</summary>
    /// <param name="redis">Redis container fixture.</param>
    public MatchmakingLeaderHealthCheckTests(RedisFixture redis) => _redis = redis;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var opts = ConfigurationOptions.Parse(_redis.ConnectionString);
        opts.AllowAdmin = true;
        _mux = await ConnectionMultiplexer.ConnectAsync(opts);

        // Clean slate: flush the database so no stale lock keys from sibling tests interfere.
        await _mux.GetServer(_mux.GetEndPoints().First()).FlushDatabaseAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_mux is not null)
            await _mux.DisposeAsync();
    }

    // ── HLTH-04: Healthy when holding lock ────────────────────────────────────────────────

    /// <summary>
    /// HLTH-04: When this replica holds the lock, <see cref="MatchmakingLeaderHealthCheck"/>
    /// returns <see cref="HealthStatus.Healthy"/> and the description contains the replica's
    /// <c>InstanceId</c> and "ttl" (the holder identity + remaining lease duration are surfaced
    /// for operator visibility per D-13).
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_Returns_Healthy_When_This_Replica_Holds_Lock()
    {
        var lease = BuildLease(lockTtlSeconds: 10);
        var check = new MatchmakingLeaderHealthCheck(lease);

        // Acquire the lock so this replica becomes the leader.
        Assert.True(await lease.TryAcquireLeaseAsync(CancellationToken.None));

        var ctx = MakeContext();
        var result = await check.CheckHealthAsync(ctx, CancellationToken.None);

        // HLTH-04: Healthy + description contains InstanceId + "ttl"
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains(lease.InstanceId, result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ttl", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── HLTH-03: Degraded (not Unhealthy) when another replica holds the lock ─────────────

    /// <summary>
    /// HLTH-03: When another replica holds the lock, <see cref="MatchmakingLeaderHealthCheck"/>
    /// returns <see cref="HealthStatus.Degraded"/> (the follower stays in the load-balancer
    /// rotation per D-10 — a follower replica must never be drained due to leadership state).
    /// The description names the actual lock holder's InstanceId (HLTH-04).
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_Returns_Degraded_When_Another_Replica_Holds_Lock()
    {
        // lease1 acquires the lock (becomes leader).
        var lease1 = BuildLease(lockTtlSeconds: 10);
        Assert.True(await lease1.TryAcquireLeaseAsync(CancellationToken.None));

        // lease2 is a follower: its check must report Degraded, not Unhealthy.
        var lease2 = BuildLease(lockTtlSeconds: 10);
        var check = new MatchmakingLeaderHealthCheck(lease2);

        var ctx = MakeContext();
        var result = await check.CheckHealthAsync(ctx, CancellationToken.None);

        // HLTH-03: Degraded (not Unhealthy) — follower stays in rotation (D-10).
        Assert.Equal(HealthStatus.Degraded, result.Status);

        // HLTH-04: Description surfaces the holder's InstanceId (the leader, not the follower).
        Assert.Contains(lease1.InstanceId, result.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── HLTH-03: Degraded when lock is unheld ────────────────────────────────────────────

    /// <summary>
    /// HLTH-03: When no replica holds the lock (no leader elected yet or lock expired),
    /// <see cref="MatchmakingLeaderHealthCheck"/> returns <see cref="HealthStatus.Degraded"/>
    /// with "unheld" in the description (D-10 — no lock = transient state, not an error).
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_Returns_Degraded_When_Lock_Unheld()
    {
        // No acquire: the lock key does not exist.
        var lease = BuildLease(lockTtlSeconds: 10);
        var check = new MatchmakingLeaderHealthCheck(lease);

        var ctx = MakeContext();
        var result = await check.CheckHealthAsync(ctx, CancellationToken.None);

        // HLTH-03: Degraded (not Unhealthy) when no lock holder exists.
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("unheld", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a <see cref="RedisMatchmakerLease"/> against the live Redis container.
    /// Each call produces a distinct <c>InstanceId</c> (new Guid suffix) simulating a
    /// different replica.
    /// </summary>
    private RedisMatchmakerLease BuildLease(int lockTtlSeconds = 5)
    {
        var opts = new GameKitMatchmakingOptions();
        opts.Ticker.LockKey = MatchmakingRedisKeys.MatcherLock;
        opts.Ticker.LockTtlSeconds = lockTtlSeconds;
        return new RedisMatchmakerLease(
            _mux!,
            Options.Create(opts),
            NullLogger<RedisMatchmakerLease>.Instance);
    }

    /// <summary>
    /// Creates a minimal <see cref="HealthCheckContext"/> for <c>CheckHealthAsync</c> calls.
    /// The <see cref="HealthCheckRegistration"/> factory is a no-op placeholder; the real
    /// check instance is constructed directly in tests.
    /// </summary>
    private static HealthCheckContext MakeContext() =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                name: "matchmaking-leader",
                instance: new NopHealthCheck(),
                // failureStatus is only used by DefaultHealthCheckService when an exception is thrown;
                // MatchmakingLeaderHealthCheck always returns a result, so this is a placeholder.
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready" })
        };

    /// <summary>Placeholder implementation satisfying <see cref="HealthCheckRegistration"/> ctor.</summary>
    private sealed class NopHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
            => Task.FromResult(HealthCheckResult.Healthy());
    }
}
