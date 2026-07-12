// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using FluentValidation;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Rankings;
using GameKit.Rankings.Data;
using GameKit.Rankings.Data.Configurations;
using GameKit.Rankings.Http.Contracts;
using GameKit.Rankings.Http.Validators;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// SC#3 integration test: verifies that <see cref="IRankAdjustService.AdjustAsync"/> writes
/// an <c>admin.player.rank_adjust</c> row to <c>admin_audit_log</c> against a real
/// Testcontainers Postgres database with Core + Auth + Admin + Rankings migrations applied.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class RankAdjustServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private AdminTestHost _host = default!;

    public RankAdjustServiceTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        // Truncate tables before each test so prior runs don't interfere.
        ResetTables(_pg.OwnerConnectionString);

        // Apply the Rankings migration (Core+Auth+Admin already applied by AdminTestHost.StartAsync).
        await ApplyRankingsMigrationAsync(_pg.OwnerConnectionString);

        _host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("superadmin", "P@ss1234567!", AdminRoles.Superadmin),
            configureExtraServices: services =>
            {
                // Register only the Rankings services needed for SC#3 — avoids calling AddRankings()
                // which would register hosted services (migration runner, StartupLadderUpserter)
                // that require ladder registrations and fight the already-running admin host.
                // IRankAdjustService depends on: GameKitDbContext (already registered), IClock,
                // IIdGenerator, IOptions<GameKitRankingsOptions> — all provided by the base
                // AddGameKit chain or the options default.
                services.AddScoped<IRankAdjustService, RankAdjustService>();
                services.AddScoped<IValidator<RankAdjustRequest>, RankAdjustRequestValidator>();
                services.Configure<GameKitRankingsOptions>(_ => { });

                // Override the DbContext to use a customizer that includes Auth+Admin+Rankings
                // entity configurations so GameKitDbContext.Set<Ladder>() and
                // GameKitDbContext.Set<PlayerRank>() resolve correctly inside RankAdjustService.
                // Without this override, EF Core's cached model (from the first AddGameKit call
                // in AdminTestHost) would not include Rankings entities (Pitfall 3).
                services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                    dbOpts.UseNpgsql(_pg.OwnerConnectionString, npg =>
                    {
                        npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                        npg.MigrationsHistoryTable(
                            GameKitMigrationConstants.MigrationsHistoryTable,
                            GameKitMigrationConstants.SchemaName);
                    }).ReplaceService<IModelCustomizer, RankAdjustRuntimeQueryCustomizer>());
            });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact(DisplayName = "SC#3: IRankAdjustService.AdjustAsync → admin_audit_log row written with action 'admin.player.rank_adjust'")]
    public async Task AdjustAsync_Writes_AdminAuditLog_Row()
    {
        // Seed a player and a ladder via raw Npgsql (avoids the FOLLOW-UP-02-03-01 two-
        // service-provider quirk used by other service tests in this test suite).
        var playerId = await SeedPlayerAsync("test-rank-player");
        var ladderId = await SeedLadderAsync("sc3-test-ladder");
        var actorId = await GetSeededAdminIdAsync();

        // Resolve IRankAdjustService from the running host scope.
        var (scope, svc) = _host.Resolve<IRankAdjustService>();
        RankAdjustResult result;
        using (scope)
        {
            result = await svc.AdjustAsync(
                playerId,
                ladderId,
                newRating: 1800.0,
                reason: "SC3 integration test adjustment",
                actorId: actorId,
                ct: default);
        }

        // Assert the service returned a success result.
        Assert.Equal(1800.0, result.After);

        // Assert the admin_audit_log row was written inside the SERIALIZABLE transaction.
        var (dbScope, ctx) = _host.CreateDbScope();
        using (dbScope)
        {
            var auditRow = await ctx.Set<AdminAuditLog>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Action == "admin.player.rank_adjust" &&
                    r.TargetId == playerId);

            Assert.NotNull(auditRow);
            Assert.Equal(actorId, auditRow.ActorId);
            Assert.Equal("player", auditRow.TargetType);
            Assert.Equal("SC3 integration test adjustment", auditRow.Reason);
        }
    }

    // ---- helpers ----

    private async Task<Guid> SeedPlayerAsync(string displayName)
    {
        var id = Guid.CreateVersion7();
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.players " +
            "(\"Id\", \"DisplayName\", \"CreatedAt\", \"IsBanned\") " +
            "VALUES ($1, $2, $3, $4)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = displayName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = false });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedLadderAsync(string name)
    {
        var id = Guid.CreateVersion7();
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Use name::citext cast to satisfy the citext column type on gamekit.ladders.
        cmd.CommandText =
            "INSERT INTO gamekit.ladders " +
            "(\"Id\", \"Name\", \"Algorithm\", \"IsActive\", \"CreatedAt\") " +
            "VALUES ($1, $2::citext, $3, $4, $5)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = name });
        cmd.Parameters.Add(new NpgsqlParameter { Value = "glicko2" });
        cmd.Parameters.Add(new NpgsqlParameter { Value = true });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> GetSeededAdminIdAsync()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\" FROM gamekit.admin_users LIMIT 1";
        var id = (Guid)(await cmd.ExecuteScalarAsync() ?? Guid.Empty);
        if (id == Guid.Empty)
            throw new InvalidOperationException(
                "No admin seeded — SeedAdminAsync should have run before this call.");
        return id;
    }

    private static async Task ApplyRankingsMigrationAsync(string ownerConnectionString)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(ownerConnectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .Options;

        await using var ctx = new GameKitDbContext(opts);
        await MigrationRunner
            .MigrateWithLockAsync(ctx, RankingsMigrationConstants.AdvisoryLockKey)
            .ConfigureAwait(false);
    }

    private static void ResetTables(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Reset audit log, admin users, player ranks, and players before each test.
            // ladders are not reset here — the citext unique index is fine if the test
            // is the only writer (and we use a unique name per run via Guid).
            cmd.CommandText =
                "TRUNCATE TABLE gamekit.admin_audit_log; " +
                "TRUNCATE TABLE gamekit.admin_users; " +
                "DELETE FROM gamekit.player_ranks; " +
                "DELETE FROM gamekit.players";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // First run — tables don't exist yet; migrations run during InitializeAsync.
        }
    }

    /// <summary>
    /// Runtime <see cref="IModelCustomizer"/> for the SC#3 integration-test DbContext.
    /// Extends <see cref="AdminTestHost.AdminRuntimeQueryCustomizer"/> behavior by additionally
    /// applying all seven Rankings entity configurations so <c>GameKitDbContext.Set&lt;Ladder&gt;()</c>
    /// and <c>GameKitDbContext.Set&lt;PlayerRank&gt;()</c> resolve correctly in
    /// <see cref="RankAdjustService"/>.
    /// </summary>
    internal sealed class RankAdjustRuntimeQueryCustomizer : RelationalModelCustomizer
    {
        /// <summary>Constructs the customizer.</summary>
        public RankAdjustRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        /// <inheritdoc />
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            // Auth entity configurations (same as AdminRuntimeQueryCustomizer).
            modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerIdentityConfiguration());
            modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerCredentialConfiguration());
            modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.RefreshTokenConfiguration());
            // Admin entity configurations.
            modelBuilder.ApplyConfiguration(new GameKit.Admin.UI.Data.Configurations.AdminUserConfiguration());
            // Rankings entity configurations — required for RankAdjustService.AdjustAsync.
            modelBuilder.ApplyConfiguration(new LadderConfiguration());
            modelBuilder.ApplyConfiguration(new PlayerRankConfiguration());
            modelBuilder.ApplyConfiguration(new LadderSeasonConfiguration());
            modelBuilder.ApplyConfiguration(new SeasonRankArchiveConfiguration());
            modelBuilder.ApplyConfiguration(new ServiceTokenConfiguration());
            modelBuilder.ApplyConfiguration(new PendingRatingUpdateConfiguration());
            modelBuilder.ApplyConfiguration(new SessionCompleteIdempotencyConfiguration());
        }
    }
}
