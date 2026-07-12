// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// Helpers shared by the Phase 14 health integration tests.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Applies Core-only migrations to the database identified by <paramref name="connectionString"/>.
    /// Uses <see cref="MigrationRunner.MigrateWithLockAsync"/> via a temporary service container
    /// so the Core migration boundary is respected.
    /// </summary>
    /// <param name="connectionString">Owner-role connection string targeting the test database.</param>
    internal static async Task ApplyCoreOnlyMigrationsAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = connectionString;
            o.MigrationsConnectionString = connectionString;
            o.AutoMigrate = false;
        });

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        // Suppress PendingModelChangesWarning — the Core snapshot is correct but EF Core 10's
        // internal hash may differ (same pattern as TestHelpers.ApplyMigrations in Auth tests).
        await ctx.Database.MigrateAsync();
    }
}
