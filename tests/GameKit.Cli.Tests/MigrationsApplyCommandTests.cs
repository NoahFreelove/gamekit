// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Cli.Commands.Migrations;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameKit.Cli.Tests;

/// <summary>
/// Integration tests for <c>gamekit migrations apply --dry-run</c> (DR-05).
/// Critical assertion: dry-run executes ZERO DDL — the schema is unchanged after the
/// command runs. Satisfies T-17-03-01 (zero-DDL is verified, not assumed).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class MigrationsApplyCommandTests
{
    private readonly PostgresFixture _pg;

    public MigrationsApplyCommandTests(PostgresFixture pg) => _pg = pg;

    /// <summary>
    /// DR-05 core assertion: after dry-run, schema is still empty (all migrations still pending).
    /// </summary>
    [Fact]
    public async Task DryRun_PrintsIdempotentSql_AndExecutesZeroDDL()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var cliProj = Path.Combine(repoRoot, "src", "GameKit.Cli", "GameKit.Cli.csproj");

        // Baseline: verify all migrations are pending before the dry-run
        var pendingBefore = new Dictionary<string, int>();
        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, _pg.OwnerConnectionString);
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            pendingBefore[pkg.DisplayName] = new List<string>(pending).Count;
        }

        // Run: gamekit migrations apply --dry-run
        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProj}\" -p:NuGetAudit=false -- migrations apply --dry-run --connection-string \"{_pg.OwnerConnectionString}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        Assert.True(p.ExitCode == 0,
            $"Expected exit 0; got {p.ExitCode}.\nstdout={stdout}\nstderr={stderr}");

        // (a) Verify idempotent SQL section headers per package
        Assert.Contains("-- Package: Core", stdout);
        Assert.Contains("-- Package: Auth", stdout);
        Assert.Contains("-- Package: Admin", stdout);
        Assert.Contains("-- Package: Rankings", stdout);
        Assert.Contains("-- Package: Matchmaking", stdout);
        Assert.Contains("-- Package: Lobby", stdout);

        // (b) Verify idempotent guards are present (EF Core wraps each migration in an
        //     IF NOT EXISTS check against the history table). The generated SQL contains
        //     IF NOT EXISTS queries on the history table rows.
        Assert.Contains("IF NOT EXISTS", stdout,
            StringComparison.OrdinalIgnoreCase);

        // (c) CRITICAL T-17-03-01: Verify ZERO DDL was executed — schema still empty.
        //     All migrations must still be pending after the dry-run.
        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, _pg.OwnerConnectionString);
            var pendingAfter = await ctx.Database.GetPendingMigrationsAsync();
            var pendingAfterList = new List<string>(pendingAfter);

            Assert.True(
                pendingAfterList.Count == pendingBefore[pkg.DisplayName],
                $"Package '{pkg.DisplayName}': dry-run changed pending count from " +
                $"{pendingBefore[pkg.DisplayName]} to {pendingAfterList.Count}. " +
                $"DDL must not have been executed during dry-run (T-17-03-01).");
        }

        // (d) Verify no GameKit tables were created (information_schema check)
        // Use the owner connection string to query information_schema
        var npgsqlConn = new Npgsql.NpgsqlConnection(_pg.OwnerConnectionString);
        await npgsqlConn.OpenAsync();
        await using (npgsqlConn)
        {
            await using var cmd = npgsqlConn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)::int
                FROM information_schema.tables
                WHERE table_schema = 'gamekit'
                  AND table_type = 'BASE TABLE'";
            var tableCount = (int)(await cmd.ExecuteScalarAsync())!;

            Assert.Equal(0, tableCount);
        }
    }

    /// <summary>
    /// Verifies that non-dry-run apply actually creates schema, then pending drops to 0.
    /// </summary>
    [Fact]
    public async Task Apply_WithoutDryRun_MigratesAllPackages()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var cliProj = Path.Combine(repoRoot, "src", "GameKit.Cli", "GameKit.Cli.csproj");

        // Apply all packages
        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProj}\" -p:NuGetAudit=false -- migrations apply --connection-string \"{_pg.OwnerConnectionString}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        Assert.True(p.ExitCode == 0,
            $"Expected exit 0; got {p.ExitCode}.\nstdout={stdout}\nstderr={stderr}");

        // All packages should show 0 pending after apply
        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, _pg.OwnerConnectionString);
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            var pendingList = new List<string>(pending);

            Assert.True(pendingList.Count == 0,
                $"Package '{pkg.DisplayName}' still has {pendingList.Count} pending migration(s) after apply: " +
                string.Join(", ", pendingList));
        }
    }
}
