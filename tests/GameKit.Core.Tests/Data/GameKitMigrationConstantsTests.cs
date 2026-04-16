// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class GameKitMigrationConstantsTests
{
    [Fact]
    public void SchemaName_IsGamekit()
    {
        Assert.Equal("gamekit", GameKitMigrationConstants.SchemaName);
    }

    [Fact]
    public void MigrationsHistoryTable_IsEfMigrationsCore()
    {
        Assert.Equal("__ef_migrations_core", GameKitMigrationConstants.MigrationsHistoryTable);
    }

    [Fact]
    public void AdvisoryLockKey_IsPinnedBigint()
    {
        // Value is SELECT hashtext('gamekit.migrations')::bigint on Postgres 17.9.
        // Plan 07 AdvisoryLockKeyTests verified this against a live Testcontainers Postgres.
        Assert.Equal(1800940027L, GameKitMigrationConstants.AdvisoryLockKey);
    }
}
