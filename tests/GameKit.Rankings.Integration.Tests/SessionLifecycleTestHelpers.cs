// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Rankings.Authentication;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Shared seed / DB / token helpers for the session-lifecycle integration tests
/// (<see cref="SessionsStartEndpointTests"/>, <see cref="SessionsAbandonEndpointTests"/>,
/// and the cross-package observer test in Presence). Extracts the common Postgres seed
/// logic from <see cref="SessionCompleteIdempotencyTests"/> without duplicating it.
/// </summary>
internal static class SessionLifecycleTestHelpers
{
    /// <summary>Creates a fresh per-test database with the gamekit schema + citext extension.</summary>
    public static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_sl_" + Guid.NewGuid().ToString("N")[..12];

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

    /// <summary>Applies Core + Rankings migrations against the per-test database.</summary>
    public static async Task ApplyMigrationsAsync(string cs)
    {
        // Core migrations.
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Rankings migrations.
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
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
        await using var rankingsCtx = new GameKitDbContext(rankingsOpts);
        await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
    }

    /// <summary>
    /// Seeds a Pending session with 2 participants. Returns the session id.
    /// Mirrors <c>SeedActivatedSessionAsync</c> from <see cref="SessionCompleteIdempotencyTests"/>
    /// but leaves the state at <see cref="GameSessionState.Pending"/> so /start can transition it.
    /// </summary>
    public static async Task<Guid> SeedPendingSessionAsync(string cs, string ladderName)
    {
        var sessionId = Guid.NewGuid();
        await SeedSessionWithStateAsync(cs, ladderName, sessionId, GameSessionState.Pending);
        return sessionId;
    }

    /// <summary>
    /// Seeds a session in the requested state with 2 participants. Inserts the ladder if not present.
    /// </summary>
    public static async Task SeedSessionWithStateAsync(
        string cs, string ladderName, Guid sessionId, GameSessionState state)
    {
        var now = DateTimeOffset.UtcNow;
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Players.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{p1Id}', 'P1', '{now:O}'), ('{p2Id}', 'P2', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Ladder (insert if missing).
        object? ladderId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            ladderId = await cmd.ExecuteScalarAsync();
        }
        if (ladderId is null)
        {
            var newLadderId = Guid.NewGuid();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                    VALUES ('{newLadderId}', '{ladderName}', 'glicko2', true, '{now:O}')";
                await cmd.ExecuteNonQueryAsync();
            }
            ladderId = newLadderId;
        }

        // Session — State stored as text via HasConversion<string>().
        // StartedAt is set when state is Active/Completed/Abandoned, null when Pending/Cancelled.
        var startedAtClause = state == GameSessionState.Pending || state == GameSessionState.Cancelled
            ? "NULL"
            : $"'{now:O}'";
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"")
                VALUES ('{sessionId}', '{state}', '{ladderId}', '{now:O}', {startedAtClause})";
            await cmd.ExecuteNonQueryAsync();
        }

        // Participants.
        var sp1Id = Guid.NewGuid();
        var sp2Id = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{sp1Id}', '{sessionId}', '{p1Id}', 0),
                       ('{sp2Id}', '{sessionId}', '{p2Id}', 1)";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Seeds a Pending session and returns the participant ids alongside the session id.
    /// Used by the Presence end-to-end observer test (Plan 06-05 Task 3).
    /// </summary>
    public static async Task<(Guid SessionId, Guid P1Id, Guid P2Id)> SeedPendingSessionWithIdsAsync(
        string cs, string ladderName)
    {
        var sessionId = Guid.NewGuid();
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{p1Id}', 'P1', '{now:O}'), ('{p2Id}', 'P2', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        object? ladderId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            ladderId = await cmd.ExecuteScalarAsync();
        }
        if (ladderId is null)
        {
            var newLadderId = Guid.NewGuid();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                    VALUES ('{newLadderId}', '{ladderName}', 'glicko2', true, '{now:O}')";
                await cmd.ExecuteNonQueryAsync();
            }
            ladderId = newLadderId;
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"")
                VALUES ('{sessionId}', '{nameof(GameSessionState.Pending)}', '{ladderId}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        var sp1Id = Guid.NewGuid();
        var sp2Id = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{sp1Id}', '{sessionId}', '{p1Id}', 0),
                       ('{sp2Id}', '{sessionId}', '{p2Id}', 1)";
            await cmd.ExecuteNonQueryAsync();
        }

        return (sessionId, p1Id, p2Id);
    }

    /// <summary>Issues a fresh service-token JWT against the test server's IServiceTokenService.</summary>
    public static async Task<(string Raw, ServiceToken Row)> IssueTokenAsync(
        SessionLifecycleTestServer server, string name)
    {
        using var scope = server.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();
        return await svc.IssueAsync(name, expiresAt: null, default);
    }

    /// <summary>Runs a scalar SELECT against the per-test database.</summary>
    public static async Task<string?> QueryScalarStringAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }
}
