// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Asserts the pinned Matchmaking advisory-lock key matches live Postgres 17.9
/// <c>hashtext(...)</c> output and is distinct from Core, Auth, Admin, and Rankings
/// keys (PITFALLS §11 — pairwise-distinct invariant prevents cross-package migration
/// deadlock at app startup).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>tests/GameKit.Rankings.Integration.Tests/RankingsAdvisoryLockKeyTests.cs</c>
/// — same structure, swapped strings. The four known prior-package keys are duplicated
/// as integer literals inside the distinct-check (defense-in-depth: even if a sibling
/// package renames its constant, the literal value collision is still caught here).
/// </para>
/// <para>
/// Wave 0 expected state (Plan 05-01):
/// <list type="bullet">
///   <item><see cref="PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation"/> — RED
///   because <c>MatchmakingMigrationConstants.AdvisoryLockKey = 0L</c> (placeholder
///   that Plan 05-02 lands) does not match the live
///   <c>SELECT hashtext('gamekit.matchmaking.migrations')::bigint</c> output. The RED
///   state is the deterministic gate for Plan 05-02 — that plan's verification is
///   exactly: replace the placeholder with the live-verified value and flip this test
///   green.</item>
///   <item><see cref="MatchmakingKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Keys"/>
///   — GREEN because <c>0L</c> is distinct from each of <c>1800940027</c> /
///   <c>-298890956</c> / <c>-2101739634</c> / <c>-156812172</c>.</item>
/// </list>
/// </para>
/// </remarks>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class MatchmakingAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public MatchmakingAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    /// <summary>
    /// T-05-15-AA: live Postgres must return the same <c>bigint</c> for
    /// <c>hashtext('gamekit.matchmaking.migrations')::bigint</c> as the value pinned in
    /// <see cref="MatchmakingMigrationConstants.AdvisoryLockKey"/>. Phase 1/2/3/4 used the
    /// same placeholder-then-live-verify pattern (CLAUDE.md "Advisory lock key corrected"
    /// row); Plan 05-02 closes this gate.
    /// </summary>
    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.matchmaking.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(MatchmakingMigrationConstants.AdvisoryLockKey, computed);
    }

    /// <summary>
    /// Pairwise non-equality with Core / Auth / Admin / Rankings advisory keys (PITFALLS §11).
    /// The four known prior-package values are duplicated as integer literals so a future
    /// constant rename inside a sibling package does not silently mask a collision.
    /// </summary>
    [Fact]
    public void MatchmakingKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Keys()
    {
        // Symbolic non-equality — primary assertion.
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey, MatchmakingMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AuthMigrationConstants.AdvisoryLockKey, MatchmakingMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AdminMigrationConstants.AdvisoryLockKey, MatchmakingMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(RankingsMigrationConstants.AdvisoryLockKey, MatchmakingMigrationConstants.AdvisoryLockKey);

        // Defense-in-depth: integer-literal non-equality. Catches the case where a sibling
        // package renames its constant but accidentally collides with the new Matchmaking value.
        Assert.NotEqual(1800940027L, MatchmakingMigrationConstants.AdvisoryLockKey);   // Core
        Assert.NotEqual(-298890956L, MatchmakingMigrationConstants.AdvisoryLockKey);   // Auth
        Assert.NotEqual(-2101739634L, MatchmakingMigrationConstants.AdvisoryLockKey);  // Admin
        Assert.NotEqual(-156812172L, MatchmakingMigrationConstants.AdvisoryLockKey);   // Rankings
    }
}
