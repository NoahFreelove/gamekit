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
    /// per-process GUID token + "ttl" (the holder identity + remaining lease duration are
    /// surfaced for operator visibility per D-13). HLTH-05: only the GUID token — not the
    /// machine name portion of <c>InstanceId</c> — appears in the anonymous payload.
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

        // HLTH-04: Healthy + description contains the GUID token + "ttl".
        // (Dedicated HLTH-05 leak guard with a sentinel hostname lives in the
        // CheckHealthAsync_Description_Does_Not_Leak_Hostname_* tests below.)
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains(GuidToken(lease.InstanceId), result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ttl", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── HLTH-03: Degraded (not Unhealthy) when another replica holds the lock ─────────────

    /// <summary>
    /// HLTH-03: When another replica holds the lock, <see cref="MatchmakingLeaderHealthCheck"/>
    /// returns <see cref="HealthStatus.Degraded"/> (the follower stays in the load-balancer
    /// rotation per D-10 — a follower replica must never be drained due to leadership state).
    /// The description names the actual lock holder's per-process GUID token (HLTH-04), not its
    /// machine name (HLTH-05).
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

        // HLTH-04: Description surfaces the holder's GUID token (the leader, not the follower).
        Assert.Contains(GuidToken(lease1.InstanceId), result.Description, StringComparison.OrdinalIgnoreCase);
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

    // ── HLTH-05 / CR-01: hostname must never leak into the anonymous description ───────────

    private const string SentinelHost = "SENTINEL-HOST-9xyz";
    private const string SentinelGuid = "11111111-1111-1111-1111-111111111111";
    private const string SentinelInstanceId = SentinelHost + ":" + SentinelGuid;

    /// <summary>
    /// HLTH-05 / CR-01 regression guard (leader branch): when this replica holds the lock, the
    /// description must surface only the per-process GUID token and must NOT contain the machine
    /// name. Uses a stub lease whose <c>InstanceId</c> carries a sentinel hostname so the
    /// assertion is hostname-deterministic (unlike the live-lease tests, which see the real host
    /// name). This is the regression guard for the leak fixed in CR-01.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_Description_Does_Not_Leak_Hostname_On_Leader_Branch()
    {
        // Stub lease where this instance IS the holder (leader branch).
        var lease = new StubLease(SentinelInstanceId, new LeaseStatus(SentinelInstanceId, TimeSpan.FromSeconds(7)));
        var check = new MatchmakingLeaderHealthCheck(lease);

        var result = await check.CheckHealthAsync(MakeContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        // Hostname must NOT appear; GUID token MUST appear.
        Assert.DoesNotContain(SentinelHost, result.Description, StringComparison.Ordinal);
        Assert.Contains(SentinelGuid, result.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// HLTH-05 / CR-01 regression guard (follower branch): when another replica holds the lock,
    /// the description must surface only the holder's per-process GUID token and must NOT contain
    /// the holder's machine name. Uses a stub lease where the holder carries a sentinel hostname.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_Description_Does_Not_Leak_Hostname_On_Follower_Branch()
    {
        const string holderHost = "SENTINEL-HOST-9xyz";
        const string holderGuid = "22222222-2222-2222-2222-222222222222";
        var holderInstanceId = holderHost + ":" + holderGuid;

        // This replica's own InstanceId differs from the holder => follower branch.
        var lease = new StubLease(
            "FOLLOWER-HOST-abc:33333333-3333-3333-3333-333333333333",
            new LeaseStatus(holderInstanceId, TimeSpan.FromSeconds(5)));
        var check = new MatchmakingLeaderHealthCheck(lease);

        var result = await check.CheckHealthAsync(MakeContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        // Neither the holder's nor this replica's hostname may leak; holder GUID token MUST appear.
        Assert.DoesNotContain(holderHost, result.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("FOLLOWER-HOST-abc", result.Description, StringComparison.Ordinal);
        Assert.Contains(holderGuid, result.Description, StringComparison.Ordinal);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>Returns the GUID token (portion after the first ':') of a <c>MachineName:Guid</c> id.</summary>
    private static string GuidToken(string instanceId) => instanceId[(instanceId.IndexOf(':') + 1)..];

    /// <summary>
    /// In-memory <see cref="IMatchmakerLease"/> stub returning a fixed <see cref="LeaseStatus"/>
    /// so the leak-regression tests can inject a deterministic sentinel hostname without a live
    /// Redis lock. Only <see cref="InstanceId"/> and <see cref="QueryLeaseAsync"/> are exercised
    /// by <see cref="MatchmakingLeaderHealthCheck"/>; the acquire/release members throw.
    /// </summary>
    private sealed class StubLease : IMatchmakerLease
    {
        private readonly LeaseStatus _status;

        public StubLease(string instanceId, LeaseStatus status)
        {
            InstanceId = instanceId;
            _status = status;
        }

        public string InstanceId { get; }

        public Task<bool> TryAcquireLeaseAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task ReleaseLeaseAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct) => Task.FromResult(_status);
    }

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
