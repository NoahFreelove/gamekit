// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="PartyService"/> against a live Postgres
/// (Testcontainers). Closes MATCH-03 at the application-service level.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class PartyServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    public PartyServiceTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAsync_Generates_Code_And_Persists_Party_And_Member()
    {
        var ownerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "Owner");

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();

        var party = await svc.CreateAsync(ownerId);

        Assert.NotEqual(Guid.Empty, party.Id);
        Assert.Equal(6, party.PartyCode.Length);
        Assert.Equal(PartyState.Open, party.State);
        Assert.Equal(ownerId, party.OwnerPlayerId);

        // Verify both rows present in Postgres.
        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var persisted = await ctx.Set<Party>().FirstAsync(p => p.Id == party.Id);
        Assert.Equal(party.PartyCode, persisted.PartyCode);

        var member = await ctx.Set<PartyMember>().FirstAsync(m => m.PartyId == party.Id);
        Assert.Equal(ownerId, member.PlayerId);
    }

    [Fact]
    public async Task JoinAsync_Is_Case_Insensitive_Via_Citext()
    {
        var ownerId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "OwnerCI");
        await SeedPlayerAsync(_cs, joinerId, "JoinerCI");

        await using var sp = BuildServiceProvider(_cs);

        Party party;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            party = await svc.CreateAsync(ownerId);
        }

        // Join using the lowercased code — citext must make the lookup succeed.
        var lowered = party.PartyCode.ToLowerInvariant();
        Assert.NotEqual(party.PartyCode, lowered); // sanity: code is uppercase, lowered differs

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var joined = await svc.JoinAsync(lowered, joinerId);
            Assert.Equal(party.Id, joined.Id);
        }

        // Verify the member is recorded.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var memberCount = await ctx.Set<PartyMember>().CountAsync(m => m.PartyId == party.Id);
            Assert.Equal(2, memberCount);
        }
    }

    [Fact]
    public async Task JoinAsync_Rejects_When_Player_Already_In_Active_Party()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerA, "OwnerA");
        await SeedPlayerAsync(_cs, ownerB, "OwnerB");

        await using var sp = BuildServiceProvider(_cs);

        Party partyA;
        Party partyB;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            partyA = await svc.CreateAsync(ownerA);
            partyB = await svc.CreateAsync(ownerB);
        }

        // ownerA is already in partyA. Joining partyB must fail with conflict.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var ex = await Assert.ThrowsAsync<PartyConflictException>(() =>
                svc.JoinAsync(partyB.PartyCode, ownerA));
            Assert.Equal("player_already_in_party", ex.Code);
        }
    }

    [Fact]
    public async Task CreateAsync_Rejects_Second_Party_For_Same_Owner()
    {
        var ownerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "DoubleCreator");

        await using var sp = BuildServiceProvider(_cs);

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            await svc.CreateAsync(ownerId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var ex = await Assert.ThrowsAsync<PartyConflictException>(() =>
                svc.CreateAsync(ownerId));
            Assert.Equal("player_already_in_party", ex.Code);
        }
    }

    [Fact]
    public async Task DissolveAsync_Requires_Owner()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "DissolveOwner");
        await SeedPlayerAsync(_cs, strangerId, "Stranger");

        await using var sp = BuildServiceProvider(_cs);

        Party party;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            party = await svc.CreateAsync(ownerId);
        }

        // Non-owner dissolve attempt → PartyAuthorizationException.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var ex = await Assert.ThrowsAsync<PartyAuthorizationException>(() =>
                svc.DissolveAsync(party.Id, strangerId));
            Assert.Equal("not_party_owner", ex.Code);
        }

        // Owner dissolve succeeds.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            await svc.DissolveAsync(party.Id, ownerId);
        }

        // State is now Dissolved; the owner is free to create a new party (active-state guard).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var reloaded = await ctx.Set<Party>().FirstAsync(p => p.Id == party.Id);
            Assert.Equal(PartyState.Dissolved, reloaded.State);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var second = await svc.CreateAsync(ownerId);
            Assert.NotEqual(party.Id, second.Id);
        }
    }

    [Fact]
    public async Task GetByCodeAsync_Returns_Null_When_Code_Not_Found()
    {
        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();

        var result = await svc.GetByCodeAsync("ZZZZZZ");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByCodeAsync_Returns_Party_Case_Insensitively()
    {
        var ownerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "Getter");

        await using var sp = BuildServiceProvider(_cs);

        Party party;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            party = await svc.CreateAsync(ownerId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var lookup = await svc.GetByCodeAsync(party.PartyCode.ToLowerInvariant());
            Assert.NotNull(lookup);
            Assert.Equal(party.Id, lookup!.Id);
        }
    }

    [Fact]
    public async Task JoinAsync_Throws_When_Party_Is_Dissolved()
    {
        var ownerId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "DissolvedOwner");
        await SeedPlayerAsync(_cs, joinerId, "LateJoiner");

        await using var sp = BuildServiceProvider(_cs);

        Party party;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            party = await svc.CreateAsync(ownerId);
            await svc.DissolveAsync(party.Id, ownerId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            var ex = await Assert.ThrowsAsync<PartyInvalidStateException>(() =>
                svc.JoinAsync(party.PartyCode, joinerId));
            Assert.Equal("party_not_open", ex.Code);
        }
    }

    [Fact]
    public async Task JoinAsync_Concurrent_Joins_Both_Succeed_When_Different_Players()
    {
        var ownerId = Guid.NewGuid();
        var joinerA = Guid.NewGuid();
        var joinerB = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "CcOwner");
        await SeedPlayerAsync(_cs, joinerA, "CcA");
        await SeedPlayerAsync(_cs, joinerB, "CcB");

        await using var sp = BuildServiceProvider(_cs);

        Party party;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            party = await svc.CreateAsync(ownerId);
        }

        // Two parallel joins by two different players targeting the same code.
        // Both should succeed (under SERIALIZABLE, one may retry; final state has 3 members).
        var taskA = Task.Run(async () =>
        {
            await using var scope = sp.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            return await svc.JoinAsync(party.PartyCode, joinerA);
        });
        var taskB = Task.Run(async () =>
        {
            await using var scope = sp.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            return await svc.JoinAsync(party.PartyCode, joinerB);
        });

        await Task.WhenAll(taskA, taskB);

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var count = await ctx.Set<PartyMember>().CountAsync(m => m.PartyId == party.Id);
            Assert.Equal(3, count); // owner + joinerA + joinerB
        }
    }

    [Fact]
    public async Task CreateAsync_Concurrent_Calls_For_Same_Owner_Exactly_One_Succeeds()
    {
        var ownerId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, ownerId, "RaceCreator");

        await using var sp = BuildServiceProvider(_cs);

        // Two parallel CreateAsync calls for the same player → exactly one succeeds, the other
        // throws PartyConflictException (player_already_in_party). The SERIALIZABLE +
        // active-membership guard + Polly retry pipeline guarantee this.
        var taskA = Task.Run(async () =>
        {
            await using var scope = sp.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            try { return await svc.CreateAsync(ownerId); }
            catch (PartyConflictException) { return null; }
        });
        var taskB = Task.Run(async () =>
        {
            await using var scope = sp.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPartyService>();
            try { return await svc.CreateAsync(ownerId); }
            catch (PartyConflictException) { return null; }
        });

        var results = await Task.WhenAll(taskA, taskB);
        var successes = results.Count(r => r is not null);
        Assert.Equal(1, successes);
    }

    // ---- Helpers (mirrors LeaderboardServiceTests pattern) ----

    private static ServiceProvider BuildServiceProvider(string cs)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        services.AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; });
        // Wire the IPartyCodeGenerator + PartyService — Plan 05-04's MatchmakingBuilderExtensions
        // (Task 3) registers these inside AddMatchmaking; this test stops short of bringing the
        // entire matchmaking surface up (no Redis) and registers just what the service needs.
        services.AddSingleton<IPartyCodeGenerator, PartyCodeGenerator>();
        services.AddScoped<IPartyService, PartyService>();

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_partysvc_" + Guid.NewGuid().ToString("N")[..12];
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
        // Core migration first.
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Rankings migration (matchmaking_tickets has FK → ladders).
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
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
        await using (var rankingsCtx = new GameKitDbContext(rankingsOpts))
        {
            await MigrationRunner.MigrateWithLockAsync(rankingsCtx, GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey);
        }

        // Matchmaking migration.
        var mmOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var mmCtx = new GameKitDbContext(mmOpts);
        await MigrationRunner.MigrateWithLockAsync(mmCtx, MatchmakingMigrationConstants.AdvisoryLockKey);
    }

    private static async Task SeedPlayerAsync(string cs, Guid playerId, string displayName)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
            VALUES (@id, @name, @now)
            ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("@id", playerId);
        cmd.Parameters.AddWithValue("@name", displayName);
        cmd.Parameters.AddWithValue("@now", now);
        await cmd.ExecuteNonQueryAsync();
    }
}
