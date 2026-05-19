// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Asserts the pinned Rankings advisory-lock key matches live Postgres 17.9
/// <c>hashtext(...)</c> output and is distinct from Core, Auth, and Admin keys (Pitfall §11).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class RankingsAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;

    public RankingsAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.rankings.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(RankingsMigrationConstants.AdvisoryLockKey, computed);
    }

    [Fact]
    public void RankingsKey_Is_Distinct_From_Core_Auth_Admin_Keys()
    {
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey, RankingsMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AuthMigrationConstants.AdvisoryLockKey, RankingsMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AdminMigrationConstants.AdvisoryLockKey, RankingsMigrationConstants.AdvisoryLockKey);
    }
}
