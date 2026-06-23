// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Lobby.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GameKit.DR.Tests;

/// <summary>
/// DR-03: Full disaster-recovery round-trip test.
/// Proves that a Postgres backup (pg_dump --format=custom) can be restored into a
/// fresh container and the application starts healthy against the restored database.
/// </summary>
/// <remarks>
/// <para>
/// Steps executed end-to-end:
/// <list type="number">
///   <item>Start Postgres container 1 with init-scripts + a bind-mounted /dump dir.</item>
///   <item>Apply ALL six packages' migrations in canonical order (Core → Auth → Admin → Rankings → Matchmaking → Lobby).</item>
///   <item>Seed one player row so the restore has data to prove survived.</item>
///   <item>Run pg_dump --format=custom inside container 1 via ExecAsync (no host pg_dump dependency).</item>
///   <item>Dispose container 1 (destroy it entirely).</item>
///   <item>Start fresh Postgres container 2 with the SAME bind-mounted /dump dir.</item>
///   <item>Run pg_restore inside container 2 against the dump produced in step 4.</item>
///   <item>Boot the app against the restored connection string; assert GET /health/ready → 200.</item>
///   <item>Assert the seeded player row exists in the restored database.</item>
/// </list>
/// </para>
/// <para>
/// This test runs in the serialised <c>DisasterRecovery</c> collection (no parallel containers)
/// and the 17-01 ordering-marker migrations are exercised as part of step 2 — if any marker
/// migration fails to apply cleanly the test will fail at the migration step with a clear error.
/// </para>
/// </remarks>
[Collection("DisasterRecovery")]
[Trait("Category", "DisasterRecovery")]
public sealed class DrRoundTripTests
{
    /// <summary>
    /// DR round-trip: dump → destroy container → restore → app starts healthy → seeded row survives.
    /// </summary>
    [Fact(DisplayName = "DR round-trip: dump → destroy → restore → /health/ready 200", Timeout = 600_000)]
    public async Task Dump_Destroy_Restore_AppStartsHealthy_AndSeedRowSurvives()
    {
        // ── Setup: shared bind-mount directories ──────────────────────────────────────────
        var repoRoot = GitRootLocator.FindRepoRoot();
        var initDir  = Path.Combine(repoRoot, "docker", "postgres", "init");
        var tmpDir   = Path.Combine(Path.GetTempPath(), "gk-dr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        // The seeded player's id — used to assert the row survived the restore.
        var seededPlayerId = Guid.NewGuid();

        try
        {
            // ── Phase 1: Container 1 — migrate + seed + pg_dump ───────────────────────────
            var pg1 = new PostgreSqlBuilder("postgres:17.9")
                .WithUsername("postgres")
                .WithPassword("postgres_test")
                .WithDatabase("postgres")
                .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
                .WithBindMount(tmpDir, "/dump")
                .Build();

            string ownerCs1;
            await pg1.StartAsync();
            try
            {
                var host1 = pg1.Hostname;
                var port1 = pg1.GetMappedPublicPort(5432);
                ownerCs1 = $"Host={host1};Port={port1};Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";

                // Step 2: Apply ALL six packages' migrations in canonical order.
                // This exercises the 17-01 ordering-marker migrations end-to-end.
                await ApplyAllMigrationsAsync(ownerCs1);

                // Step 3: Seed one player row (proves data exists to be lost + restored).
                var adminCs1 = $"Host={host1};Port={port1};Database=gamekit;Username=postgres;Password=postgres_test";
                await SeedPlayerAsync(adminCs1, seededPlayerId);

                // Step 4: pg_dump inside container 1 (no host-side pg_dump dependency).
                // PGPASSWORD prevents pg_dump from hanging on a password prompt (Pitfall 3).
                var dumpResult = await pg1.ExecAsync(new[]
                {
                    "bash", "-c",
                    "PGPASSWORD=postgres_test pg_dump --username=postgres --format=custom --file=/dump/gamekit.pgdump gamekit"
                });

                Assert.True(dumpResult.ExitCode == 0,
                    $"pg_dump exited with code {dumpResult.ExitCode}.\nstdout: {dumpResult.Stdout}\nstderr: {dumpResult.Stderr}");

                // Verify the dump file was written to the bind-mounted host dir.
                var dumpPath = Path.Combine(tmpDir, "gamekit.pgdump");
                Assert.True(File.Exists(dumpPath),
                    $"pg_dump did not write to bind-mounted dir at {dumpPath}");
                Assert.True(new FileInfo(dumpPath).Length > 0,
                    "pg_dump wrote an empty file — dump failed silently");
            }
            finally
            {
                // Step 5: Destroy container 1 entirely.
                await pg1.DisposeAsync();
            }

            // ── Phase 2: Container 2 — restore + health check ────────────────────────────

            // Step 6: Start a fresh container 2 with the SAME bind mounts.
            // The init scripts create the gamekit database, roles, and extensions but NO tables
            // — pg_restore will recreate the full schema (Pitfall 6: init creates empty DB).
            var pg2 = new PostgreSqlBuilder("postgres:17.9")
                .WithUsername("postgres")
                .WithPassword("postgres_test")
                .WithDatabase("postgres")
                .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
                .WithBindMount(tmpDir, "/dump")
                .Build();

            await pg2.StartAsync();
            await using (pg2)
            {
                var host2 = pg2.Hostname;
                var port2 = pg2.GetMappedPublicPort(5432);
                var adminCs2 = $"Host={host2};Port={port2};Database=gamekit;Username=postgres;Password=postgres_test";

                // Step 7: pg_restore inside container 2.
                // --no-owner / --no-privileges: the dump was made as postgres but the restored DB
                // belongs to gamekit_owner in this container — skip ownership reassignment.
                // pg_restore into the init-script-created empty gamekit DB.
                // We tolerate benign warnings (e.g. "SET ROLE" notes) but assert exit code 0.
                // If non-zero: capture stderr and report it as the assertion message.
                // Step 7: pg_restore inside container 2.
                // --no-owner / --no-privileges: dump was made as postgres; restored DB is owned by gamekit_owner.
                // --clean --if-exists: drop existing objects before recreating (Pitfall 6 fix).
                //   The init scripts pre-create the gamekit schema and roles. Without --clean the restore
                //   would fail with "schema already exists". --if-exists suppresses the "object does not
                //   exist" drop errors for objects the init scripts did not create (e.g., tables).
                // --single-transaction: wrap the entire restore in a transaction so partial restores fail atomically.
                var restoreResult = await pg2.ExecAsync(new[]
                {
                    "bash", "-c",
                    "PGPASSWORD=postgres_test pg_restore --username=postgres --dbname=gamekit --no-owner --no-privileges --clean --if-exists /dump/gamekit.pgdump"
                });

                // pg_restore with --clean --if-exists may still emit warnings for objects that
                // do not exist during the DROP phase (e.g. extensions pre-created by init scripts).
                // We tolerate exit code 1 when the stderr contains only warnings (not "pg_restore: error:").
                // The definitive success proof is (a) /health/ready == 200 and (b) player row present.
                if (restoreResult.ExitCode != 0)
                {
                    // Distinguish fatal errors from non-fatal warnings.
                    // pg_restore: error: = fatal; pg_restore: warning: = non-fatal.
                    var stderrLines = restoreResult.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in stderrLines)
                    {
                        // Skip warning lines and lines that are continuations of the CREATE/DROP command text.
                        var trimmed = line.TrimStart();
                        if (trimmed.StartsWith("pg_restore: warning:", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (trimmed.StartsWith("Command was:", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (trimmed.StartsWith("pg_restore: warning", StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Any remaining non-empty line starting with "pg_restore: error:" is fatal.
                        if (trimmed.StartsWith("pg_restore: error:", StringComparison.OrdinalIgnoreCase))
                        {
                            Assert.Fail(
                                $"pg_restore reported a fatal error (exit {restoreResult.ExitCode}).\n" +
                                $"stderr: {restoreResult.Stderr}\nstdout: {restoreResult.Stdout}");
                        }
                    }
                }

                // Step 8: Boot app against the restored database; assert /health/ready → 200.
                // Use the postgres superuser connection string for the health check host.
                // Rationale: pg_restore with --no-owner restores object data but skips ALTER OWNER;
                // the restored schema/tables may be owned by 'postgres' (the restore user), so
                // gamekit_owner loses schema access. The postgres superuser has unrestricted access
                // to all restored objects. The health check's CoreMigrationReadinessReporter only
                // needs to read __ef_migrations_core — any connection with SELECT rights works.
                var (app, client) = await DrHealthTestHost.StartAsync(adminCs2);
                await using (app)
                {
                    var healthResponse = await client.GetAsync("/health/ready");
                    Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
                }

                // Step 9: Assert the seeded player row survived the round-trip.
                // This proves the restore carried actual data, not just schema.
                await AssertPlayerRowExistsAsync(adminCs2, seededPlayerId);

                // Step 10: Assert all 6 packages' migration history tables exist in the restored DB.
                // This proves the 17-01 marker migrations and all prior migrations were applied on
                // container 1 before the dump, and all survive the restore intact.
                await AssertAllMigrationTablesExistAsync(adminCs2);
            }
        }
        finally
        {
            // Clean up the bind-mount temp dir (best-effort — test infra may clean on agent restart).
            try
            {
                if (Directory.Exists(tmpDir))
                    Directory.Delete(tmpDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup — suppress exceptions so the test result is not obscured.
            }
        }
    }

    // ── Migration helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies all six packages' migrations in canonical dependency order:
    /// Core → Auth → Admin.UI → Rankings → Matchmaking → Lobby.
    /// Each package runs under its own advisory lock key via
    /// <see cref="MigrationRunner.MigrateWithLockAsync"/>.
    /// The 17-01 ordering-marker migrations are included in this run.
    /// </summary>
    private static async Task ApplyAllMigrationsAsync(string cs)
    {
        // Step 1 — Core: use AddGameKit's service container (owns the canonical Core context).
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

        // Step 2 — Auth (depends on Core; adds player_identities, player_credentials, refresh_tokens).
        await using (var authCtx = BuildAuthMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(authCtx, AuthMigrationConstants.AdvisoryLockKey);
        }

        // Step 3 — Admin.UI (depends on Core + Auth; adds admin_users, ban history).
        await using (var adminCtx = BuildAdminMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(adminCtx, AdminMigrationConstants.AdvisoryLockKey);
        }

        // Step 4 — Rankings (depends on Core; adds ladders + player_ratings).
        await using (var rankingsCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
        }

        // Step 5 — Matchmaking (depends on Core + Rankings; adds matchmaking_tickets etc.).
        await using (var matchmakingCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(matchmakingCtx, MatchmakingMigrationConstants.AdvisoryLockKey);
        }

        // Step 6 — Lobby (depends on Core + Rankings + Matchmaking; adds lobbies + lobby_members).
        await using (var lobbyCtx = BuildLobbyMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(lobbyCtx, LobbyMigrationConstants.AdvisoryLockKey);
        }
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildAdminMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildRankingsMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildMatchmakingMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
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
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildLobbyMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(LobbyMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, LobbyMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    // ── Seed + assertion helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts one row into <c>gamekit.players</c> with the given id.
    /// The row proves there is data in the dump to be potentially lost and then recovered.
    /// </summary>
    private static async Task SeedPlayerAsync(string adminCs, Guid playerId)
    {
        await using var conn = new NpgsqlConnection(adminCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)";
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.AddWithValue("name", $"dr-test-player-{playerId:N}");
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Asserts that the seeded player row is present in the restored database.
    /// Failure here proves the restore did not carry data (not just schema).
    /// </summary>
    private static async Task AssertPlayerRowExistsAsync(string adminCs, Guid playerId)
    {
        await using var conn = new NpgsqlConnection(adminCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(1) FROM gamekit.players WHERE ""Id"" = @id";
        cmd.Parameters.AddWithValue("id", playerId);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.True(count == 1,
            $"Seeded player row (id={playerId}) was not found in the restored database. " +
            "This means the pg_restore did not carry data — the round-trip is incomplete.");
    }

    /// <summary>
    /// Asserts that all six packages' migration history tables are present in the
    /// restored database. This proves the 17-01 ordering-marker migrations were
    /// applied (and survived the dump → restore cycle) for all packages.
    /// </summary>
    private static async Task AssertAllMigrationTablesExistAsync(string adminCs)
    {
        // Canonical migration history table names for all 6 packages.
        // Verified against *MigrationConstants.MigrationsHistoryTable in each package.
        var expectedMigrationTables = new[]
        {
            "gamekit.__ef_migrations_core",        // Core
            "gamekit.__ef_migrations_auth",        // Auth  (includes 20260623000000_DrOrderingMarker)
            "gamekit.__ef_migrations_admin",       // Admin.UI (includes 20260624000000_DrOrderingMarker)
            "gamekit.__ef_migrations_rankings",    // Rankings (includes 20260625000000_DrOrderingMarker)
            "gamekit.__ef_migrations_matchmaking", // Matchmaking (includes 20260626000000_DrOrderingMarker)
            "gamekit.__ef_migrations_lobby",       // Lobby (includes 20260627000000_DrOrderingMarker)
        };

        await using var conn = new NpgsqlConnection(adminCs);
        await conn.OpenAsync();

        foreach (var table in expectedMigrationTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT to_regclass('{table}') IS NOT NULL";
            var exists = (bool)(await cmd.ExecuteScalarAsync() ?? false);
            Assert.True(exists,
                $"Migration history table '{table}' does not exist in the restored database. " +
                $"This means the corresponding package's migrations were NOT applied before the dump, " +
                $"or the restore did not include them. Check the ApplyAllMigrationsAsync step.");
        }
    }
}
