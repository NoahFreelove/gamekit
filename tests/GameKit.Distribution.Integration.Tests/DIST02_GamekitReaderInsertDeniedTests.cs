// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Distribution.Integration.Tests.Fixtures;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// DIST-02 (Plan 06-08 Task 2): empirically proves the 3-role Postgres bootstrap
/// in <c>docker/postgres/init/01-roles.sql</c> denies <c>INSERT</c> on
/// <c>gamekit.game_sessions</c> when authenticated as <c>gamekit_reader</c>.
/// </summary>
/// <remarks>
/// <para>
/// The DIST-02 contract: a leaked <c>gamekit_reader</c> credential gives an attacker
/// SELECT-only access to the schema. The Postgres role layer enforces this — not
/// convention. PostgreSQL signals the privilege violation by raising error code
/// SQLSTATE <c>42501</c> ("insufficient_privilege" / "permission denied for table").
/// </para>
/// <para>
/// PATTERNS warning #11: <see cref="PostgresFixture"/> already bind-mounts
/// <c>docker/postgres/init/</c> on first start (the canonical 3-role init script).
/// This test just consumes <see cref="DistributionIntegrationFixture.ReaderConnectionString"/>
/// + <see cref="DistributionIntegrationFixture.OwnerConnectionString"/> — no custom
/// container plumbing.
/// </para>
/// </remarks>
[Collection("Distribution")]
[Trait("Category", "Integration")]
public sealed class DIST02_GamekitReaderInsertDeniedTests
{
    private readonly DistributionIntegrationFixture _fixture;

    /// <summary>Constructs the test with the shared Postgres + Redis collection fixtures.</summary>
    public DIST02_GamekitReaderInsertDeniedTests(PostgresFixture postgres, RedisFixture redis)
    {
        _fixture = new DistributionIntegrationFixture(postgres, redis);
    }

    /// <summary>
    /// Pre-condition gate: a connection as <c>gamekit_reader</c> can issue a SELECT
    /// against <c>gamekit.players</c>. The table need not contain rows; the query must
    /// simply not raise a permission-denied error.
    /// </summary>
    /// <remarks>
    /// Requires the Core migrations to have been applied so <c>gamekit.players</c>
    /// exists. We bootstrap a minimal owner-side schema dance to guarantee this without
    /// pulling the full Core migration runner — see <c>EnsureGameSessionsTableExistsAsync</c>.
    /// </remarks>
    [Fact]
    public async Task Reader_CanSelect_FromPlayersTable()
    {
        await EnsureCoreTablesExistAsync();

        await using var conn = new NpgsqlConnection(_fixture.ReaderConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM gamekit.players LIMIT 1;";

        // Materialize the result; we don't care if it returns 0 rows — only that
        // the SELECT itself is authorized.
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // no-op
        }
    }

    /// <summary>
    /// DIST-02 core assertion: opens a connection as <c>gamekit_reader</c> and attempts
    /// <c>INSERT INTO gamekit.game_sessions</c>. The attempt MUST raise a
    /// <see cref="PostgresException"/> with <c>SqlState == "42501"</c> ("permission denied
    /// for table game_sessions"). The seed row inserted via <c>gamekit_owner</c> beforehand
    /// is incidental — it exists only to guarantee the table is populated for downstream
    /// reads; the assertion would still fire on an empty table.
    /// </summary>
    [Fact]
    public async Task Reader_InsertOnGameSessions_IsDeniedWith42501()
    {
        await EnsureCoreTablesExistAsync();

        // Step A — owner seed: prove the owner role CAN insert (so we know the table
        // exists, the grant is in force, and the failure in Step B is the role barrier,
        // not a missing-table or column-mismatch error.).
        var seedSessionId = Guid.NewGuid();
        await using (var ownerConn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await ownerConn.OpenAsync();
            await using var seedCmd = ownerConn.CreateCommand();
            seedCmd.CommandText =
                "INSERT INTO gamekit.game_sessions (\"Id\", \"State\", \"CreatedAt\") " +
                "VALUES (@id, @state, @createdAt);";
            seedCmd.Parameters.AddWithValue("id", seedSessionId);
            seedCmd.Parameters.AddWithValue("state", "Pending");
            seedCmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
            await seedCmd.ExecuteNonQueryAsync();
        }

        // Step B — reader attempt: this MUST throw with SQLSTATE 42501.
        await using var readerConn = new NpgsqlConnection(_fixture.ReaderConnectionString);
        await readerConn.OpenAsync();
        await using var insertCmd = readerConn.CreateCommand();
        insertCmd.CommandText =
            "INSERT INTO gamekit.game_sessions (\"Id\", \"State\", \"CreatedAt\") " +
            "VALUES (@id, @state, @createdAt);";
        insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("state", "Pending");
        insertCmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => insertCmd.ExecuteNonQueryAsync());

        // 42501 is the standard PostgreSQL SQLSTATE for "insufficient_privilege".
        // https://www.postgresql.org/docs/current/errcodes-appendix.html
        Assert.Equal("42501", ex.SqlState);
    }

    /// <summary>
    /// Ensures the minimum Core tables (<c>gamekit.players</c>, <c>gamekit.game_sessions</c>)
    /// exist by applying the bare-minimum DDL via the owner role with
    /// <c>CREATE TABLE IF NOT EXISTS</c> (idempotent across test re-runs sharing the
    /// same container).
    /// </summary>
    /// <remarks>
    /// Deliberately avoids the full <see cref="GameKit.Core.Data.GameKitDbContext"/> + EF migration
    /// path: invoking <c>AddGameKit</c> here would build + cache an EF model in the
    /// process-wide model cache (no <c>IModelCacheKeyFactory</c> override in the
    /// codebase), poisoning later tests in the same xUnit assembly that expect the
    /// composite (Core + Auth + Rankings + ...) model on the same context type. DIST-02
    /// is about Postgres role privileges, not EF — raw DDL keeps the boundary clean.
    /// OPS-06 (Task 3) exercises the full migration chain in its own isolated container.
    /// </remarks>
    private async Task EnsureCoreTablesExistAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        // Schema is created by docker/postgres/init/01-roles.sql; ensure tables match the
        // Core migration shape (see src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS gamekit.players (
                "Id" uuid PRIMARY KEY,
                "DisplayName" varchar(64) NOT NULL DEFAULT 'player',
                "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                "LastSeenAt" timestamptz,
                "IsBanned" boolean NOT NULL DEFAULT false,
                "BannedAt" timestamptz,
                "BanReason" varchar(500),
                "Metadata" jsonb
            );

            CREATE TABLE IF NOT EXISTS gamekit.game_sessions (
                "Id" uuid PRIMARY KEY,
                "State" varchar(16) NOT NULL,
                "LadderId" uuid,
                "CreatedAt" timestamptz NOT NULL,
                "StartedAt" timestamptz,
                "CompletedAt" timestamptz,
                "Metadata" jsonb
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
