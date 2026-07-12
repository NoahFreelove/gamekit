// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// Schema-level integration tests for the <c>20260522000000_LobbyInitial</c> migration:
/// <list type="bullet">
/// <item><c>lobbies</c> and <c>lobby_members</c> tables exist in the <c>gamekit</c> schema (LOBBY-01, LOBBY-02).</item>
/// <item><c>__ef_migrations_lobby</c> history table records the <c>LobbyInitial</c> migration row (LOBBY-01).</item>
/// <item>Zero tables matching <c>lobby_message%</c> exist — the LOBBY-04 anti-feature is enforced at the schema level (T-11-02-02).</item>
/// <item>Duplicate <c>(LobbyId, PlayerId)</c> insert raises a 23505 unique violation (LOBBY-02, T-11-02-03).</item>
/// </list>
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class LobbySchemaTests
{
    private readonly PostgresFixture _pg;

    /// <summary>Initialises the test class with the shared Postgres fixture.</summary>
    public LobbySchemaTests(PostgresFixture pg) => _pg = pg;

    /// <summary>
    /// After applying all migrations, <c>gamekit.lobbies</c> and <c>gamekit.lobby_members</c>
    /// must both exist in <c>information_schema.tables</c> (LOBBY-01, LOBBY-02).
    /// </summary>
    [Fact]
    public async Task Migration_Creates_Lobbies_And_LobbyMembers()
    {
        var cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyLobbyMigrationsAsync(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'gamekit'
              AND table_name IN ('lobbies', 'lobby_members')";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(2L, count);
    }

    /// <summary>
    /// After applying migrations, <c>__ef_migrations_lobby</c> must exist and contain the
    /// <c>20260522000000_LobbyInitial</c> row (LOBBY-01 — per-package history table).
    /// </summary>
    [Fact]
    public async Task Migration_Records_LobbyHistory()
    {
        var cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyLobbyMigrationsAsync(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM gamekit.__ef_migrations_lobby
            WHERE ""MigrationId"" = '20260522000000_LobbyInitial'";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(1L, count);
    }

    /// <summary>
    /// No table whose name matches <c>lobby_message%</c> must exist in the <c>gamekit</c> schema —
    /// LOBBY-04 anti-feature enforcement at the schema level (T-11-02-02). Chat messages are
    /// ephemeral and MUST NOT be persisted.
    /// </summary>
    [Fact]
    public async Task No_Chat_Message_Table_Exists()
    {
        var cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyLobbyMigrationsAsync(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'gamekit'
              AND table_name LIKE 'lobby_message%'";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(0L, count);
    }

    /// <summary>
    /// Inserting two <c>lobby_members</c> rows with the same <c>(LobbyId, PlayerId)</c> must raise
    /// Postgres error code <c>23505</c> (unique_violation) — composite unique constraint
    /// enforced at the database level (LOBBY-02, T-11-02-03).
    /// </summary>
    [Fact]
    public async Task LobbyMembers_Unique_Constraint_Enforced()
    {
        var cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyLobbyMigrationsAsync(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Insert a player row (players is the FK principal for lobby_members.PlayerId)
        var playerId = Guid.NewGuid();
        await using (var insertPlayerCmd = conn.CreateCommand())
        {
            insertPlayerCmd.CommandText = @"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
                VALUES (@id, @name, NOW(), false)";
            insertPlayerCmd.Parameters.AddWithValue("id", playerId);
            insertPlayerCmd.Parameters.AddWithValue("name", "TestPlayer_" + playerId.ToString("N")[..8]);
            await insertPlayerCmd.ExecuteNonQueryAsync();
        }

        // Insert a lobby row (lobbies is the FK principal for lobby_members.LobbyId)
        var lobbyId = Guid.NewGuid();
        await using (var insertLobbyCmd = conn.CreateCommand())
        {
            insertLobbyCmd.CommandText = @"
                INSERT INTO gamekit.lobbies (""Id"", ""State"", ""MaxMembers"", ""CreatedAt"", ""UpdatedAt"")
                VALUES (@id, 0, 8, NOW(), NOW())";
            insertLobbyCmd.Parameters.AddWithValue("id", lobbyId);
            await insertLobbyCmd.ExecuteNonQueryAsync();
        }

        // First member insert — must succeed
        await using (var firstInsertCmd = conn.CreateCommand())
        {
            firstInsertCmd.CommandText = @"
                INSERT INTO gamekit.lobby_members (""Id"", ""LobbyId"", ""PlayerId"", ""Ready"", ""JoinedAt"")
                VALUES (@id, @lobbyId, @playerId, false, NOW())";
            firstInsertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            firstInsertCmd.Parameters.AddWithValue("lobbyId", lobbyId);
            firstInsertCmd.Parameters.AddWithValue("playerId", playerId);
            await firstInsertCmd.ExecuteNonQueryAsync();
        }

        // Second insert with the same (LobbyId, PlayerId) — must raise 23505
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var duplicateCmd = conn.CreateCommand();
            duplicateCmd.CommandText = @"
                INSERT INTO gamekit.lobby_members (""Id"", ""LobbyId"", ""PlayerId"", ""Ready"", ""JoinedAt"")
                VALUES (@id, @lobbyId, @playerId, false, NOW())";
            duplicateCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicateCmd.Parameters.AddWithValue("lobbyId", lobbyId);
            duplicateCmd.Parameters.AddWithValue("playerId", playerId);
            await duplicateCmd.ExecuteNonQueryAsync();
        });

        Assert.Equal("23505", ex.SqlState);
    }
}
