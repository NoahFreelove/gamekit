// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Integration.Tests;

/// <summary>
/// D-06 Phase 1 harness: pack GameKit.Core, install the resulting .nupkg into a scratch
/// ASP.NET Core 10 web app via a local NuGet feed, then run Database.Migrate() against a
/// fresh Testcontainers Postgres. Asserts all Core tables + __ef_migrations_core exist.
/// Phase 2+ extends this test with additional packages; the harness shape stays the same.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class PackAndInstallMigrationTests
{
    private readonly PostgresFixture _pg;

    public PackAndInstallMigrationTests(PostgresFixture pg) => _pg = pg;

    [Fact(Skip = "D-06 Phase 1 harness -- requires dotnet SDK in PATH and sufficient disk space for tempdir. Enable in CI once ubuntu-24.04 matrix settles.")]
    public async Task Pack_Install_Migrate_Roundtrip_Creates_Schema()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var feedDir = Path.Combine(repoRoot, "artifacts", "nupkg-local");
        var scratchDir = Path.Combine(Path.GetTempPath(), $"gamekit-scratch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(feedDir);
        Directory.CreateDirectory(scratchDir);

        try
        {
            // 1. dotnet pack src/GameKit.Core -> ./artifacts/nupkg-local/
            await RunAsync("dotnet",
                $"pack src/GameKit.Core/GameKit.Core.csproj -c Release -o \"{feedDir}\"",
                repoRoot);

            // 2. dotnet new web -o <scratchDir>/Scratch
            var scratchProj = Path.Combine(scratchDir, "Scratch");
            await RunAsync("dotnet",
                $"new web -o \"{scratchProj}\" --framework net10.0",
                scratchDir);

            // 3. dotnet add package GameKit.Core --source <feedDir>
            await RunAsync("dotnet",
                $"add \"{scratchProj}\" package GameKit.Core --source \"{feedDir}\" --prerelease",
                scratchDir);

            // 4. Patch the scratch Program.cs to AddGameKit + UseGameKit
            var programPath = Path.Combine(scratchProj, "Program.cs");
            var connStr = _pg.OwnerConnectionString;
            var program =
                "using GameKit.Core.Builder;\n" +
                "var builder = WebApplication.CreateBuilder(args);\n" +
                "builder.Services.AddGameKit(opts =>\n" +
                "{\n" +
                $"    opts.ConnectionString = \"{connStr}\";\n" +
                $"    opts.MigrationsConnectionString = \"{connStr}\";\n" +
                "    opts.AutoMigrate = true;\n" +
                "});\n" +
                "var app = builder.Build();\n" +
                "app.UseGameKit();\n" +
                "await app.StartAsync();\n" +
                "await app.StopAsync();\n";
            await File.WriteAllTextAsync(programPath, program, Encoding.UTF8);

            // 5. dotnet run -- boots app, migrates, shuts down.
            await RunAsync("dotnet",
                $"run --project \"{scratchProj}\" -c Release",
                scratchDir,
                timeoutSec: 120);

            // 6. Verify schema exists against Postgres.
            await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
            await conn.OpenAsync();
            foreach (var table in new[]
                     {
                         "players", "game_sessions", "session_participants",
                         "admin_audit_log", "__ef_migrations_core"
                     })
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT to_regclass('gamekit.{table}') IS NOT NULL";
                var exists = (bool)(await cmd.ExecuteScalarAsync() ?? false);
                Assert.True(exists,
                    $"gamekit.{table} must exist after pack-and-install roundtrip");
            }
        }
        finally
        {
            try { Directory.Delete(scratchDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static async Task RunAsync(
        string exe, string args, string cwd, int timeoutSec = 60)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = cwd,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        await p.WaitForExitAsync(cts.Token);
        if (p.ExitCode != 0)
            throw new Xunit.Sdk.XunitException(
                $"`{exe} {args}` (cwd={cwd}) exited {p.ExitCode}\nstdout:\n{await stdout}\nstderr:\n{await stderr}");
    }
}
