// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// Assumption A2 validation: the pinned <see cref="GameKitMigrationConstants.AdvisoryLockKey"/>
/// matches <c>SELECT hashtext('gamekit.migrations')::bigint</c> on a live Postgres 17.9 instance.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class AdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;

    public AdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.migrations')::bigint";

        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(GameKitMigrationConstants.AdvisoryLockKey, computed);
    }
}
