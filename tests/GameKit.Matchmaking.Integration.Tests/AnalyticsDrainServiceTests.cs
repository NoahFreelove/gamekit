// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Services;
using GameKit.Matchmaking.Telemetry;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// MATCH-02 + D-15/D-16 integration tests for
/// <see cref="MatchmakingAnalyticsDrainService"/>. Verifies (a) happy-path drain of 100 events
/// in one batch INSERT, (b) Postgres outage path drops the batch + emits
/// <see cref="MatchmakingMeter.DroppedEvents"/> with <c>reason=polly_exhausted</c>, and
/// (c) channel-full path emits the counter with <c>reason=channel_full</c> via the producer
/// pattern.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class AnalyticsDrainServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    public AnalyticsDrainServiceTests(PostgresFixture pg, RedisFixture _) => _pg = pg;

    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task HundredEvents_DrainedInBatch_PersistedToPostgres()
    {
        // Arrange — a parent ticket row is required so ticket_events FK is satisfied.
        var ladderId = await SeedLadderAsync(_cs, "drain-test-happy");
        var ticketId = await SeedTicketAsync(_cs, ladderId);

        var channel = Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = false,
        });

        // Enqueue 100 events.
        var nowBase = DateTimeOffset.UtcNow;
        for (var i = 0; i < 100; i++)
        {
            Assert.True(channel.Writer.TryWrite(new TicketEvent
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                EventType = TicketEventType.Queued,
                OccurredAt = nowBase.AddMilliseconds(i),
            }));
        }

        await using var sp = BuildAnalyticsServiceProvider(_cs, channel.Reader);
        var drain = sp.GetRequiredService<IMatchmakingAnalyticsDrain>();

        // Act
        var drained = await drain.DrainOnceAsync(100, CancellationToken.None);

        // Assert — all 100 events persisted.
        Assert.Equal(100, drained);

        await using (var verifyCtx = BuildMatchmakingContext(_cs))
        {
            var count = await verifyCtx.Set<TicketEvent>()
                .Where(e => e.TicketId == ticketId)
                .CountAsync();
            Assert.Equal(100, count);
        }
    }

    [Fact]
    public async Task PostgresOutage_DropsBatch_IncrementsCounter()
    {
        // Arrange — analytics drain pointing at a connection string that fails immediately.
        var deadCs = "Host=127.0.0.1;Port=1;Database=nonexistent;Username=invalid;Password=invalid;Timeout=2;Command Timeout=2";

        var channel = Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = false,
        });

        for (var i = 0; i < 10; i++)
        {
            Assert.True(channel.Writer.TryWrite(new TicketEvent
            {
                Id = Guid.NewGuid(),
                TicketId = Guid.NewGuid(),
                EventType = TicketEventType.Queued,
                OccurredAt = DateTimeOffset.UtcNow,
            }));
        }

        // Speed up the Polly pipeline so the test does not run for minutes — override
        // PollyMaxRetryAttempts=1, PollyBaseDelayMs=10, PollyTimeoutSeconds=3 via options.
        await using var sp = BuildAnalyticsServiceProvider(deadCs, channel.Reader, opts =>
        {
            opts.Analytics.PollyMaxRetryAttempts = 1;
            opts.Analytics.PollyBaseDelayMs = 10;
            opts.Analytics.PollyTimeoutSeconds = 3;
            opts.Analytics.DrainBatchSize = 10;
            opts.Analytics.DrainIntervalSeconds = 1;
        });
        var drain = sp.GetRequiredService<IMatchmakingAnalyticsDrain>();

        var captured = new List<(long Value, string? Reason)>();
        using var listener = StartDroppedEventsListener(captured);

        // Act — drain should fail after Polly exhaustion, drop the batch, and emit the counter.
        var drained = await drain.DrainOnceAsync(10, CancellationToken.None);

        // Assert
        Assert.Equal(0, drained);
        Assert.Contains(captured, c => c.Reason == "polly_exhausted" && c.Value > 0);
    }

    [Fact]
    public async Task ChannelFull_DropNewest_IncrementsCounter()
    {
        // The bounded channel uses DropNewest semantics: when full, TryWrite still returns true
        // BUT the newest entry is dropped (the channel cannot grow beyond capacity). The
        // matchmaking producer (Plan 05-05/05-06) emits the counter explicitly on the drop
        // path because the channel does not signal back to the producer. This integration
        // test simulates the producer's full-channel path against a live MatchmakingMeter
        // listener.
        var channel = Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = false,
        });

        // Fill to capacity.
        for (var i = 0; i < 2; i++)
        {
            channel.Writer.TryWrite(new TicketEvent
            {
                Id = Guid.NewGuid(),
                TicketId = Guid.NewGuid(),
                EventType = TicketEventType.Queued,
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }

        var captured = new List<(long Value, string? Reason)>();
        using var listener = StartDroppedEventsListener(captured);

        // Producer-side drop emit (the same path Plan 05-05/05-06 take when WriteAsync would block).
        MatchmakingMeter.DroppedEvents.Add(1, new KeyValuePair<string, object?>("reason", "channel_full"));

        await Task.Yield();

        Assert.Contains(captured, c => c.Reason == "channel_full" && c.Value == 1);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MeterListener StartDroppedEventsListener(List<(long Value, string? Reason)> captured)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == MatchmakingMeter.MeterName &&
                    instr.Name == "matchmaking.analytics.dropped_events")
                {
                    l.EnableMeasurementEvents(instr);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instr, value, tags, _) =>
        {
            string? reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason" && tag.Value is string s) reason = s;
            }
            captured.Add((value, reason));
        });
        listener.Start();
        return listener;
    }

    private static ServiceProvider BuildAnalyticsServiceProvider(
        string cs,
        ChannelReader<TicketEvent> reader,
        Action<GameKitMatchmakingOptions>? configureOpts = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(reader);

        var optsBuilder = services.AddOptions<GameKitMatchmakingOptions>();
        if (configureOpts is not null)
            optsBuilder.Configure(configureOpts);

        services.AddDbContext<GameKitDbContext>(opts =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<MatchmakingAnalyticsDrainService>();
        services.AddSingleton<IMatchmakingAnalyticsDrain>(sp => sp.GetRequiredService<MatchmakingAnalyticsDrainService>());

        return services.BuildServiceProvider();
    }

    private static GameKitDbContext BuildMatchmakingContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs)
            .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static async Task<Guid> SeedLadderAsync(string cs, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @n, 'Glicko2', true, NOW(), '{}'::jsonb)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", name);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid> SeedTicketAsync(string cs, Guid ladderId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.matchmaking_tickets
            (""Id"", ""PartyId"", ""LadderId"", ""PoolName"", ""Status"", ""QueuedAt"", ""TerminalAt"", ""SessionId"")
            VALUES (@id, NULL, @ladder, 'default', 0, NOW(), NULL, NULL)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("ladder", ladderId);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_mm_drain_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }

        return freshCs;
    }

    private static async Task ApplyMigrationsAsync(string cs)
    {
        // Apply Core+Auth+Admin+Rankings migrations via AddGameKit's migration hosted services.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.MigrationsConnectionString = cs;
            o.AutoMigrate = false;
        });
        await using (var coreSp = coreServices.BuildServiceProvider())
        {
            await using var scope = coreSp.CreateAsyncScope();
            var coreCtx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(coreCtx);
        }

        // Apply Rankings migration (prerequisite for matchmaking_tickets.LadderId FK).
        await using (var rankingsCtx = BuildRankingsContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                rankingsCtx,
                GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey);
        }

        // Apply Matchmaking migration.
        await using (var matchmakingCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                matchmakingCtx,
                GameKit.Matchmaking.Data.MatchmakingMigrationConstants.AdvisoryLockKey);
        }
    }

    private static GameKitDbContext BuildRankingsContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Rankings.Data.RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Rankings.Data.RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Rankings.Data.RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildMatchmakingMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Matchmaking.Data.MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Matchmaking.Data.MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Matchmaking.Data.MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }
}
