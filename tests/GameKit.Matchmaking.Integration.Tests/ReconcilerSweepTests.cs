// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Services;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// MATCH-06 integration tests for <see cref="MatchmakingReconcilerService"/>. Asserts:
/// <list type="bullet">
///   <item>Stale Queued ticket not in Redis is marked <c>Expired</c>.</item>
///   <item>Queued ticket present in Redis is left untouched.</item>
///   <item>Orphan Active <c>game_session</c> is marked <c>Cancelled</c> + audit row emitted.</item>
///   <item>Reconciler performs zero Redis writes (verified by Redis INFO commandstats delta).</item>
///   <item>Reconciler skips when not leader (lease helper returns false).</item>
/// </list>
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class ReconcilerSweepTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;
    private string _redisCs = string.Empty;

    public ReconcilerSweepTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(_cs);
        _redisCs = _redis.ConnectionString;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task StaleQueuedTicket_NotInRedis_MarkedExpired()
    {
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "reconciler-stale");
        var ticketId = await IntegrationTestHelpers.SeedTicketAsync(
            _cs, ladderId, status: TicketStatus.Queued,
            queuedAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        // Crucially: do NOT add to Redis. The reconciler should treat this as expired.
        await using var sp = BuildReconcilerServiceProvider(_cs, _redisCs, isLeader: true);
        var reconciler = sp.GetRequiredService<IMatchmakingReconciler>();

        var result = await reconciler.RunSweepOnceAsync(CancellationToken.None);

        Assert.False(result.SkippedBecauseNotLeader);
        Assert.Equal(1, result.TicketsExpired);

        await using var verifyCtx = IntegrationTestHelpers.BuildMatchmakingContext(_cs);
        var refreshed = await verifyCtx.Set<MatchmakingTicket>().FirstAsync(t => t.Id == ticketId);
        Assert.Equal(TicketStatus.Expired, refreshed.Status);
        Assert.NotNull(refreshed.TerminalAt);
    }

    [Fact]
    public async Task QueuedTicket_InRedis_NotTouched()
    {
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "reconciler-live");
        var queuedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);
        var ticketId = await IntegrationTestHelpers.SeedTicketAsync(
            _cs, ladderId, status: TicketStatus.Queued, queuedAt: queuedAt);

        // ZADD the ticket into the live queue — reconciler must leave it alone.
        await using (var mux = await ConnectionMultiplexer.ConnectAsync(_redisCs))
        {
            var db = mux.GetDatabase();
            var queueKey = MatchmakingRedisKeys.Queue(ladderId, "default");
            await db.SortedSetAddAsync(queueKey, ticketId.ToString(), queuedAt.ToUnixTimeMilliseconds());
        }

        await using var sp = BuildReconcilerServiceProvider(_cs, _redisCs, isLeader: true);
        var reconciler = sp.GetRequiredService<IMatchmakingReconciler>();

        var result = await reconciler.RunSweepOnceAsync(CancellationToken.None);

        Assert.Equal(0, result.TicketsExpired);

        await using var verifyCtx = IntegrationTestHelpers.BuildMatchmakingContext(_cs);
        var refreshed = await verifyCtx.Set<MatchmakingTicket>().FirstAsync(t => t.Id == ticketId);
        Assert.Equal(TicketStatus.Queued, refreshed.Status);
        Assert.Null(refreshed.TerminalAt);
    }

    [Fact]
    public async Task OrphanActiveSession_MarkedCancelled_WithAudit()
    {
        var sessionId = await IntegrationTestHelpers.SeedActiveGameSessionAsync(
            _cs, createdAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(20));

        await using var sp = BuildReconcilerServiceProvider(_cs, _redisCs, isLeader: true);
        var reconciler = sp.GetRequiredService<IMatchmakingReconciler>();

        var result = await reconciler.RunSweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.SessionsCancelled);

        // Verify session state + audit row.
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""State"" FROM gamekit.game_sessions WHERE ""Id"" = @id";
            cmd.Parameters.AddWithValue("id", sessionId);
            var state = (string)(await cmd.ExecuteScalarAsync())!;
            // GameSession.State is HasConversion<string>() — store as enum name.
            Assert.Equal(GameSessionState.Cancelled.ToString(), state);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.admin_audit_log
                                WHERE ""Action"" = 'admin.matchmaking.session_orphan_cancelled'
                                AND ""TargetId"" = @id";
            cmd.Parameters.AddWithValue("id", sessionId);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(1L, count);
        }
    }

    [Fact]
    public async Task Reconciler_DoesNotCallRedisWrites()
    {
        // Seed a stale ticket so the reconciler does have work to do — proves write absence
        // even on the happy-path expire branch (Pitfall §1).
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "reconciler-noredis");
        await IntegrationTestHelpers.SeedTicketAsync(
            _cs, ladderId, status: TicketStatus.Queued,
            queuedAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        // Capture Redis write-command counts BEFORE the sweep.
        var statsBefore = await ReadRedisWriteCommandStatsAsync(_redisCs);

        await using var sp = BuildReconcilerServiceProvider(_cs, _redisCs, isLeader: true);
        var reconciler = sp.GetRequiredService<IMatchmakingReconciler>();
        var result = await reconciler.RunSweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.TicketsExpired);

        var statsAfter = await ReadRedisWriteCommandStatsAsync(_redisCs);

        // The reconciler must not have executed any of the matchmaker write verbs.
        Assert.Equal(statsBefore.Zadd, statsAfter.Zadd);
        Assert.Equal(statsBefore.Hset, statsAfter.Hset);
        Assert.Equal(statsBefore.Sadd, statsAfter.Sadd);
        Assert.Equal(statsBefore.Publish, statsAfter.Publish);
    }

    [Fact]
    public async Task Reconciler_SkipsWhenNotLeader()
    {
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "reconciler-noleader");
        await IntegrationTestHelpers.SeedTicketAsync(
            _cs, ladderId, status: TicketStatus.Queued,
            queuedAt: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

        await using var sp = BuildReconcilerServiceProvider(_cs, _redisCs, isLeader: false);
        var reconciler = sp.GetRequiredService<IMatchmakingReconciler>();

        var result = await reconciler.RunSweepOnceAsync(CancellationToken.None);

        Assert.True(result.SkippedBecauseNotLeader);
        Assert.Equal(0, result.TicketsExpired);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ServiceProvider BuildReconcilerServiceProvider(
        string cs, string redisCs, bool isLeader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<GameKitMatchmakingOptions>().Configure(o =>
        {
            o.Reconciler.StaleTicketThresholdMinutes = 5;
            o.Reconciler.OrphanSessionThresholdMinutes = 10;
        });

        services.AddDbContext<GameKitDbContext>(opts =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisCs));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAdminAuditWriter, AdminAuditWriter>();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.AddSingleton<IMatchmakerLease>(new StubMatchmakerLease(isLeader));
        services.AddSingleton<MatchmakingReconcilerService>();
        services.AddSingleton<IMatchmakingReconciler>(sp => sp.GetRequiredService<MatchmakingReconcilerService>());

        return services.BuildServiceProvider();
    }

    private static async Task<(long Zadd, long Hset, long Sadd, long Publish)> ReadRedisWriteCommandStatsAsync(string redisCs)
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(redisCs);
        var db = mux.GetDatabase();
        var result = (string?)(await db.ExecuteAsync("INFO", "commandstats"));
        return (
            ParseCalls(result, "cmdstat_zadd"),
            ParseCalls(result, "cmdstat_hset"),
            ParseCalls(result, "cmdstat_sadd"),
            ParseCalls(result, "cmdstat_publish"));
    }

    private static long ParseCalls(string? info, string key)
    {
        if (string.IsNullOrEmpty(info)) return 0;
        foreach (var line in info.Split('\n'))
        {
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                // cmdstat_zadd:calls=12,usec=...
                var idx = line.IndexOf("calls=", StringComparison.Ordinal);
                if (idx < 0) return 0;
                var rest = line[(idx + "calls=".Length)..];
                var comma = rest.IndexOf(',');
                if (comma >= 0) rest = rest[..comma];
                return long.TryParse(rest, out var n) ? n : 0;
            }
        }
        return 0;
    }
}

/// <summary>Hand-rolled <see cref="IMatchmakerLease"/> stub for deterministic leader-gate tests.</summary>
internal sealed class StubMatchmakerLease : IMatchmakerLease
{
    private readonly bool _isLeader;
    public StubMatchmakerLease(bool isLeader) => _isLeader = isLeader;

    /// <inheritdoc />
    public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

    /// <inheritdoc />
    public Task<bool> TryAcquireLeaseAsync(CancellationToken ct) => Task.FromResult(_isLeader);

    /// <inheritdoc />
    /// <remarks>This stub does not support renewal; returns <c>false</c> unconditionally.</remarks>
    public Task<bool> RenewLeaseAsync(CancellationToken ct) => Task.FromResult(false);

    /// <inheritdoc />
    public Task ReleaseLeaseAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct) =>
        Task.FromResult(_isLeader
            ? new LeaseStatus(InstanceId, TimeSpan.FromSeconds(90))
            : new LeaseStatus(null, null));
}
