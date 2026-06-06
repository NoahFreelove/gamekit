// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Auth.AccountMerge.Integration.Tests;

/// <summary>
/// Shared Plan 10-02 test scaffolding: applies Core + Auth + Rankings + Matchmaking migrations
/// in the correct dependency order so the full account-merge service code path (FK surgery +
/// Redis cleanup) can execute against a real Postgres + Redis instance.
/// </summary>
/// <remarks>
/// <para>
/// Migration order:
/// <list type="number">
///   <item>Core — creates <c>players</c>, <c>game_sessions</c>, <c>session_participants</c>, <c>admin_audit_log</c></item>
///   <item>Auth — creates <c>player_identities</c>, <c>player_credentials</c>, <c>refresh_tokens</c>, <c>account_merges</c></item>
///   <item>Rankings — creates <c>ladders</c>, <c>player_ranks</c>, and related tables</item>
///   <item>Matchmaking — creates <c>matchmaking_tickets</c>, <c>parties</c>, and related tables</item>
/// </list>
/// Matchmaking FKs reference both Ladders (Rankings) and Players (Core), so all four packages
/// must be applied in order.
/// </para>
/// <para>
/// Auth is intentionally applied before Rankings + Matchmaking (unlike the load-test helper
/// which skips Auth) because the merge service tests require <c>player_credentials</c> /
/// <c>player_identities</c> / <c>account_merges</c> tables to seed test data.
/// </para>
/// </remarks>
internal static class TestHelpers
{
    /// <summary>
    /// Applies Core, Auth, Rankings, and Matchmaking migrations in dependency order against
    /// <paramref name="connectionString"/>. Each migration train runs under its own Postgres
    /// advisory lock so concurrent test-host invocations (e.g. parallelised class-level
    /// fixtures) do not deadlock.
    /// </summary>
    /// <param name="connectionString">Owner-role Postgres connection string (typically <c>PostgresFixture.OwnerConnectionString</c>).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static async Task ApplyMigrations(string connectionString, CancellationToken cancellationToken = default)
    {
        // ── Step 1: Core migrations ──────────────────────────────────────────────────────────
        // Rule-1 fix (mirrors TestHelpers.cs in GameKit.Auth.Integration.Tests): EF Core 10
        // promotes PendingModelChangesWarning from informational to error when Auth entities are
        // registered in the runtime model but the Core snapshot only knows Core entities. This
        // is the expected per-package migration boundary (PITFALLS #3) — suppress intentionally.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = connectionString; o.AutoMigrate = false; });
        coreServices.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
        coreServices.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
            dbOpts.UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .UseApplicationServiceProvider(sp)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // ── Step 2: Auth migrations ──────────────────────────────────────────────────────────
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .UseApplicationServiceProvider(coreSp)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var authCtx = new GameKitDbContext(authOpts);
        await authCtx.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        // ── Step 3: Rankings migrations ──────────────────────────────────────────────────────
        // RankingsMigrationModelCustomizer applies Rankings entity configurations and excludes
        // all Core entities (per-package migration boundary). PendingModelChangesWarning
        // suppressed because the Auth entities (registered in the runtime model above) are not
        // in the Rankings snapshot — intentional per PITFALLS #3.
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var rankingsCtx = new GameKitDbContext(rankingsOpts);
        await MigrationRunner.MigrateWithLockAsync(
            rankingsCtx,
            RankingsMigrationConstants.AdvisoryLockKey,
            cancellationToken).ConfigureAwait(false);

        // ── Step 4: Matchmaking migrations ───────────────────────────────────────────────────
        // MatchmakingMigrationModelCustomizer has the widest exclusion list (Core + Auth + Admin
        // + Rankings). Admin entities are excluded by its own exclusion list even though we have
        // not applied Admin migrations — Matchmaking's customizer calls ExcludeFromMigrations
        // on Admin entities to avoid emitting Admin tables in the Matchmaking migration diff.
        // This is the same approach used by the load-test helper (LoadTestMigrationHelpers).
        var matchmakingOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var matchmakingCtx = new GameKitDbContext(matchmakingOpts);
        await MigrationRunner.MigrateWithLockAsync(
            matchmakingCtx,
            MatchmakingMigrationConstants.AdvisoryLockKey,
            cancellationToken).ConfigureAwait(false);
    }
}
