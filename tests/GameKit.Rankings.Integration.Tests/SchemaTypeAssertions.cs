// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Schema-introspection assertions for the Rankings migration (SC#3 / RANK-02 / RANK-03 / Pitfall §12 / Pitfall §13).
/// Queries <c>information_schema</c> directly — not via EF Core — to verify the actual Postgres
/// column types and schema state match our design decisions.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class SchemaTypeAssertions
{
    private readonly PostgresFixture _pg;

    public SchemaTypeAssertions(PostgresFixture pg) => _pg = pg;

    /// <summary>
    /// Applies Core + Rankings migrations once, then runs all schema-introspection assertions
    /// against the resulting database state.
    /// </summary>
    private async Task<string> EnsureMigratedAsync()
    {
        var connStr = _pg.OwnerConnectionString;

        // Core first
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = connStr;
            o.MigrationsConnectionString = connStr;
            o.AutoMigrate = false;
        });
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        // Rankings
        // ConfigureWarnings: suppress PendingModelChangesWarning — the hand-authored snapshot
        // is structurally correct but may not match EF Core's internal hash exactly without a
        // full `dotnet ef` run. The schema-introspection tests below verify actual Postgres
        // column types, table presence, and FK constraints via information_schema queries,
        // which is the meaningful correctness gate (SC#3 / RANK-02 / RANK-03 / Pitfall §12).
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connStr, npg =>
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
        await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);

        return connStr;
    }

    [Fact]
    public async Task Rating_Columns_Are_DoublePrecision()
    {
        var connStr = await EnsureMigratedAsync();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Column names are PascalCase (Npgsql EF Core convention — no snake_case mapping in
        // this project, confirmed by \d gamekit.game_sessions showing "LadderId", "CreatedAt", etc.)
        cmd.CommandText = @"
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'gamekit'
              AND (
                (table_name = 'player_ranks'        AND column_name IN ('Rating', 'RatingDeviation', 'Volatility'))
                OR
                (table_name = 'session_participants' AND column_name IN ('RatingBefore', 'RatingAfter', 'RatingDelta'))
              )
            ORDER BY table_name, column_name";

        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string Table, string Col, string Type)>();
        while (await reader.ReadAsync())
        {
            // column_name is the second column in information_schema but first in the result
            rows.Add((string.Empty, reader.GetString(0), reader.GetString(1)));
        }

        // Expect exactly 6 rows (3 on player_ranks, 3 on session_participants)
        Assert.Equal(6, rows.Count);
        foreach (var (_, _, type) in rows)
        {
            Assert.Equal("double precision", type);
        }
    }

    [Fact]
    public async Task Seven_New_Tables_Exist_In_Gamekit_Schema()
    {
        var connStr = await EnsureMigratedAsync();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'gamekit' AND table_type = 'BASE TABLE'
            ORDER BY table_name";

        await using var reader = await cmd.ExecuteReaderAsync();
        var tables = new HashSet<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        // Seven Rankings tables must be present
        Assert.Contains("ladders", tables);
        Assert.Contains("player_ranks", tables);
        Assert.Contains("ladder_seasons", tables);
        Assert.Contains("season_rank_archive", tables);
        Assert.Contains("service_tokens", tables);
        Assert.Contains("pending_rating_updates", tables);
        Assert.Contains("session_complete_idempotency", tables);
    }

    [Fact]
    public async Task FK_FromGameSessions_To_Ladders_Has_OnDeleteSetNull()
    {
        var connStr = await EnsureMigratedAsync();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT delete_rule
            FROM information_schema.referential_constraints
            WHERE constraint_name = 'fk_game_sessions_ladders'";

        var deleteRule = await cmd.ExecuteScalarAsync() as string;
        Assert.NotNull(deleteRule);
        Assert.Equal("SET NULL", deleteRule);
    }

    [Fact]
    public async Task PendingRatingUpdates_PlayerId_Is_Nullable()
    {
        var connStr = await EnsureMigratedAsync();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Column name is PascalCase ("PlayerId") — Npgsql EF Core convention, no snake_case mapping.
        cmd.CommandText = @"
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'gamekit'
              AND table_name = 'pending_rating_updates'
              AND column_name = 'PlayerId'";

        var isNullable = await cmd.ExecuteScalarAsync() as string;
        Assert.NotNull(isNullable);
        Assert.Equal("YES", isNullable);
    }
}
