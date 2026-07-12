// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.Lobby.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// Asserts the pinned Lobby advisory-lock key matches live Postgres 17.9
/// <c>hashtext(...)</c> output and is distinct from Core, Auth, Admin, Rankings, and
/// Matchmaking keys (SC#1 / OPS-11 — pairwise-distinct invariant prevents cross-package
/// migration deadlock at app startup).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs</c>
/// — same structure, swapped strings and constants. The five known prior-package keys are
/// duplicated as integer literals inside the distinct-check (defense-in-depth: even if a
/// sibling package renames its constant, the literal value collision is still caught here).
/// </para>
/// <para>
/// Wave 0 expected state (Plan 11-01):
/// <list type="bullet">
///   <item><see cref="PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation"/> — starts
///   RED because <c>LobbyMigrationConstants.AdvisoryLockKey = 0L</c> (placeholder) does
///   not match the live <c>SELECT hashtext('gamekit.lobby.migrations')::bigint</c> output.
///   The constant is updated to the live-verified value within this plan (Wave 0 gate)
///   to flip this test GREEN.</item>
///   <item><see cref="LobbyKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Matchmaking_Keys"/>
///   — also verifies the live key is pairwise-distinct from all five existing package keys
///   by both symbolic constant and integer literal.</item>
/// </list>
/// </para>
/// </remarks>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class LobbyAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public LobbyAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    /// <summary>
    /// SC#1 / OPS-11 Wave 0 gate: live Postgres must return the same <c>bigint</c> for
    /// <c>hashtext('gamekit.lobby.migrations')::bigint</c> as the value pinned in
    /// <see cref="LobbyMigrationConstants.AdvisoryLockKey"/>. The value is live-verified
    /// via Testcontainers and the constant updated within Plan 11-01 (same
    /// placeholder-then-live-verify pattern used for all five prior packages).
    /// </summary>
    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.lobby.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(LobbyMigrationConstants.AdvisoryLockKey, computed);
    }

    /// <summary>
    /// Pairwise non-equality with Core / Auth / Admin / Rankings / Matchmaking advisory
    /// keys (PITFALLS §11). The five known prior-package values are duplicated as integer
    /// literals so a future constant rename inside a sibling package does not silently mask
    /// a collision.
    /// </summary>
    [Fact]
    public void LobbyKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Matchmaking_Keys()
    {
        // Symbolic non-equality — primary assertion.
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey,     LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AuthMigrationConstants.AdvisoryLockKey,        LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AdminMigrationConstants.AdvisoryLockKey,       LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(RankingsMigrationConstants.AdvisoryLockKey,    LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(MatchmakingMigrationConstants.AdvisoryLockKey, LobbyMigrationConstants.AdvisoryLockKey);

        // Defense-in-depth: integer-literal non-equality. Catches the case where a sibling
        // package renames its constant but accidentally collides with the Lobby value.
        Assert.NotEqual(1800940027L,  LobbyMigrationConstants.AdvisoryLockKey);  // Core
        Assert.NotEqual(-298890956L,  LobbyMigrationConstants.AdvisoryLockKey);  // Auth
        Assert.NotEqual(-2101739634L, LobbyMigrationConstants.AdvisoryLockKey);  // Admin
        Assert.NotEqual(-156812172L,  LobbyMigrationConstants.AdvisoryLockKey);  // Rankings
        Assert.NotEqual(388956820L,   LobbyMigrationConstants.AdvisoryLockKey);  // Matchmaking
    }
}
