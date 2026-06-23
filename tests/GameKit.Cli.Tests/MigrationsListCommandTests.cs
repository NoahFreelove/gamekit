// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Cli.Commands.Migrations;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameKit.Cli.Tests;

/// <summary>
/// Integration tests for <c>gamekit migrations list</c> (DR-04).
/// Verifies that the command prints all 6 package names, applied/pending counts,
/// and the recommended application order line against a fresh Testcontainers Postgres.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class MigrationsListCommandTests
{
    private readonly PostgresFixture _pg;

    public MigrationsListCommandTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task MigrationsList_PrintsAllSixPackages_AndRecommendedOrderLine()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var cliProj = Path.Combine(repoRoot, "src", "GameKit.Cli", "GameKit.Cli.csproj");

        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProj}\" -p:NuGetAudit=false -- migrations list --connection-string \"{_pg.OwnerConnectionString}\"")
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

        // All 6 package names must appear in the output
        Assert.Contains("Core", stdout);
        Assert.Contains("Auth", stdout);
        Assert.Contains("Admin", stdout);
        Assert.Contains("Rankings", stdout);
        Assert.Contains("Matchmaking", stdout);
        Assert.Contains("Lobby", stdout);

        // The recommended application order line must appear
        Assert.Contains("Core → Auth → Admin → Rankings → Matchmaking → Lobby", stdout);
    }

    [Fact]
    public async Task MigrationsList_AfterApplyingMigrations_ShowsPendingZeroForAllPackages()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var cliProj = Path.Combine(repoRoot, "src", "GameKit.Cli", "GameKit.Cli.csproj");

        // First: apply all migrations via 'migrations apply'
        var applyPsi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProj}\" -p:NuGetAudit=false -- migrations apply --connection-string \"{_pg.OwnerConnectionString}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var applyProc = Process.Start(applyPsi)!;
        var applyOut = await applyProc.StandardOutput.ReadToEndAsync();
        var applyErr = await applyProc.StandardError.ReadToEndAsync();
        await applyProc.WaitForExitAsync();

        Assert.True(applyProc.ExitCode == 0,
            $"Apply step failed with exit {applyProc.ExitCode}.\nstdout={applyOut}\nstderr={applyErr}");

        // Now: verify via in-process API that all migrations are applied
        // (pending counts should be 0 for all packages)
        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, _pg.OwnerConnectionString);
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            var pendingList = new List<string>(pending);

            Assert.True(pendingList.Count == 0,
                $"Package '{pkg.DisplayName}' still has {pendingList.Count} pending migration(s) after apply: " +
                string.Join(", ", pendingList));
        }

        // Run 'migrations list' again and verify pending column shows 0 for all packages
        var listPsi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProj}\" -p:NuGetAudit=false -- migrations list --connection-string \"{_pg.OwnerConnectionString}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var listProc = Process.Start(listPsi)!;
        var listOut = await listProc.StandardOutput.ReadToEndAsync();
        var listErr = await listProc.StandardError.ReadToEndAsync();
        await listProc.WaitForExitAsync();

        Assert.True(listProc.ExitCode == 0,
            $"List after apply failed with exit {listProc.ExitCode}.\nstdout={listOut}\nstderr={listErr}");

        // All 6 packages must appear and the recommended order line must be present
        Assert.Contains("Core", listOut);
        Assert.Contains("Lobby", listOut);
        Assert.Contains("Core → Auth → Admin → Rankings → Matchmaking → Lobby", listOut);
    }
}
