// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>Asserts the pinned Auth advisory-lock key matches live Postgres 17.9 <c>hashtext(...)</c> output and is distinct from Core's.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class AuthAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;

    public AuthAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.auth.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(AuthMigrationConstants.AdvisoryLockKey, computed);
    }

    [Fact]
    public void AuthKey_Is_Distinct_From_Core_Key()
    {
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey, AuthMigrationConstants.AdvisoryLockKey);
    }
}
