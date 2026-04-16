// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Cli.Tests;

/// <summary>
/// Functional test for <c>gamekit migrate</c>: invokes the CLI binary against a fresh
/// Testcontainers Postgres, asserts exit code 0 and stdout contains "OK".
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class MigrateCommandTests
{
    private readonly PostgresFixture _pg;

    public MigrateCommandTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Migrate_Command_Applies_Schema()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var cliProj = Path.Combine(repoRoot, "src", "GameKit.Cli", "GameKit.Cli.csproj");

        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProj}\" -- migrate --connection-string \"{_pg.OwnerConnectionString}\"")
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
        Assert.Contains("OK", stdout);
    }
}
