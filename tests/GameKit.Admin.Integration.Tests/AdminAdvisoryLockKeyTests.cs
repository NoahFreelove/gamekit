// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Asserts the pinned Admin advisory-lock key matches live Postgres 17.9 <c>hashtext(...)</c>
/// output and is distinct from both Core's and Auth's keys (per SP-13). Mirrors
/// <c>AuthAdvisoryLockKeyTests</c>.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AdminAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;

    public AdminAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.admin.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(AdminMigrationConstants.AdvisoryLockKey, computed);
    }

    [Fact]
    public void AdminKey_Is_Distinct_From_Core_And_Auth_Keys()
    {
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey, AdminMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AuthMigrationConstants.AdvisoryLockKey, AdminMigrationConstants.AdvisoryLockKey);
    }
}
