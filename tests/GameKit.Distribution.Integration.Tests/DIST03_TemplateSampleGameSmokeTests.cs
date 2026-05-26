// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// DIST-03 (Plan 06-09 Task 2): structural smoke test for the
/// <c>dotnet new gamekit</c> template — packs <c>templates/GameKit.Templates/</c>,
/// installs the produced .nupkg via <c>dotnet new install</c>, instantiates the
/// template into a temp directory with two flag combinations (full + minimal),
/// then asserts the generated tree has the correct file shape AND the generated
/// <c>Program.cs</c> + <c>.csproj</c> reflect the requested opt-out flags.
/// </summary>
/// <remarks>
/// <para>
/// Per CONTEXT.md D-12, the four opt-out flags are
/// <c>--skip-auth</c> / <c>--skip-rankings</c> / <c>--skip-matchmaking</c> /
/// <c>--skip-presence</c>. This test exercises a "full" generation (no flags)
/// and a "minimal" generation (rankings + matchmaking + presence skipped) and
/// asserts the conditional <c>//#if (!skipX)</c> + <c>&lt;!--#if (!skipX)--&gt;</c>
/// blocks in <c>Program.cs</c> + <c>.csproj</c> were honoured by the template
/// engine.
/// </para>
/// <para>
/// Plan 06-09 explicitly scopes DIST-03 to the structural-smoke surface — the
/// "boot the generated app + assert guest auth / session-complete / leaderboard
/// queries succeed" UAT lives in Plan 06-10's human-verify checkpoint (which
/// has a real Postgres + Redis stack available + the GameKit.* packages
/// published to a local NuGet feed). DIST-03 in 06-09 is the contract test that
/// the template renders correctly; DIST-03 UAT in 06-10 is the contract test
/// that the rendered output ACTUALLY BOOTS. Both gates must pass for the
/// `dotnet new gamekit` story to ship.
/// </para>
/// <para>
/// Test isolation: every test installs + uninstalls its own template package to
/// avoid bleeding template registrations across test runs in the user's
/// .templateengine state. The package id <c>GameKit.Templates</c> is uninstalled
/// in <c>DisposeAsync</c>-equivalent cleanup blocks.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DIST03_TemplateSampleGameSmokeTests
{
    /// <summary>
    /// Full generation smoke: no opt-out flags. Verifies the default
    /// `dotnet new gamekit -n FullSmoke` produces a complete tree with all four
    /// player-facing packages wired and the post-action script in place.
    /// </summary>
    [Fact]
    public async Task TemplateInstall_AndFullGenerate_ProducesAllExpectedFiles()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var nupkg = await PackTemplateAsync(repoRoot);
        try
        {
            await InstallTemplateAsync(nupkg);

            var workDir = Path.Combine(Path.GetTempPath(),
                $"gamekit-dist03-full-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);
            try
            {
                var (exit, stdout, stderr) = await RunProcessAsync(
                    "dotnet",
                    "new gamekit -n FullSmoke --allow-scripts No",
                    workDir);

                // Exit 0 = full success; exit 105 = template rendered but the
                // post-action was declined via --allow-scripts No. Both are acceptable
                // for the structural-smoke test because the file tree is rendered
                // identically in both cases — only the dev RSA keypair generation
                // is gated by --allow-scripts. We pick `No` to keep CI runs hermetic
                // (no openssl dep, no spurious file-system pollution).
                Assert.True(exit == 0 || exit == 105,
                    $"dotnet new gamekit (full) FAILED with unexpected exit code:\nexit={exit}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

                var generated = Path.Combine(workDir, "FullSmoke");

                // Top-level fixtures.
                Assert.True(File.Exists(Path.Combine(generated, "FullSmoke.sln")));
                Assert.True(File.Exists(Path.Combine(generated, "README.md")));
                Assert.True(File.Exists(Path.Combine(generated, "docker-compose.yml")));
                Assert.True(File.Exists(Path.Combine(generated, "scripts", "gen-test-rsa-pem.sh")));
                Assert.True(File.Exists(Path.Combine(generated, "docker", "postgres", "init", "01-roles.sql")));

                // Web tier.
                var web = Path.Combine(generated, "src", "FullSmoke");
                Assert.True(File.Exists(Path.Combine(web, "FullSmoke.csproj")));
                Assert.True(File.Exists(Path.Combine(web, "Program.cs")));
                Assert.True(File.Exists(Path.Combine(web, "Game", "TicTacToeBoard.cs")));
                Assert.True(File.Exists(Path.Combine(web, "Http", "DemoEndpoints.cs")));
                Assert.True(File.Exists(Path.Combine(web, "wwwroot", "index.html")));

                // Game-server tier (D-13).
                var gs = Path.Combine(generated, "src", "FullSmoke.GameServer");
                Assert.True(File.Exists(Path.Combine(gs, "FullSmoke.GameServer.csproj")));
                Assert.True(File.Exists(Path.Combine(gs, "Program.cs")));

                // Default (no opt-outs): every conditional GameKit.* PackageRef present in csproj.
                var webCsproj = File.ReadAllText(Path.Combine(web, "FullSmoke.csproj"));
                Assert.Contains("GameKit.Core", webCsproj);
                Assert.Contains("GameKit.Auth", webCsproj);
                Assert.Contains("GameKit.Rankings", webCsproj);
                Assert.Contains("GameKit.Matchmaking", webCsproj);
                Assert.Contains("GameKit.Presence", webCsproj);
                Assert.Contains("GameKit.OpenApi", webCsproj);
                Assert.Contains("GameKit.Admin.UI", webCsproj);

                // Default: every conditional Add*/Map* present in Program.cs. Receiver-qualified
                // to avoid false positives against comment prose that lists the methods.
                var programCs = File.ReadAllText(Path.Combine(web, "Program.cs"));
                Assert.Contains("gameKitBuilder.AddAuth(", programCs);
                Assert.Contains("gameKitBuilder.AddRankings(", programCs);
                Assert.Contains("gameKitBuilder.AddMatchmaking(", programCs);
                Assert.Contains("gameKitBuilder.AddPresence(", programCs);
                Assert.Contains("app.MapPresence(", programCs);
                Assert.Contains("app.MapRankings(", programCs);
                Assert.Contains("app.MapMatchmaking(", programCs);

                // sourceName substitution worked: namespace + RootNamespace + AssemblyName.
                Assert.Contains("namespace FullSmoke.Http;", File.ReadAllText(Path.Combine(web, "Http", "DemoEndpoints.cs")));
                Assert.Contains("<RootNamespace>FullSmoke</RootNamespace>", webCsproj);
                Assert.Contains("<AssemblyName>FullSmoke</AssemblyName>", webCsproj);

                // No stray "GameKit.SampleGame" literals remain in the rendered output
                // (the sourceName field would substitute, but any literal that escaped
                // the substitution rules signals a bug in the template).
                var allCsContents = string.Join("\n",
                    Directory.EnumerateFiles(generated, "*.cs", SearchOption.AllDirectories)
                        .Select(File.ReadAllText));
                Assert.DoesNotContain("GameKit.SampleGame", allCsContents);
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { }
            }
        }
        finally
        {
            await UninstallTemplateAsync();
        }
    }

    /// <summary>
    /// Minimal generation smoke (D-12): asserts <c>--skip-rankings
    /// --skip-matchmaking --skip-presence</c> omits both the conditional
    /// <c>&lt;PackageReference&gt;</c>s AND the matching <c>Add*</c> /
    /// <c>Map*</c> calls in <c>Program.cs</c>.
    /// </summary>
    [Fact]
    public async Task TemplateInstall_AndMinimalGenerate_OmitsSkippedPackagesAndCalls()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var nupkg = await PackTemplateAsync(repoRoot);
        try
        {
            await InstallTemplateAsync(nupkg);

            var workDir = Path.Combine(Path.GetTempPath(),
                $"gamekit-dist03-minimal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);
            try
            {
                var (exit, stdout, stderr) = await RunProcessAsync(
                    "dotnet",
                    "new gamekit -n MiniSmoke --skip-rankings --skip-matchmaking --skip-presence --allow-scripts No",
                    workDir);

                // Exit 0 = success; exit 105 = template rendered, post-action declined.
                Assert.True(exit == 0 || exit == 105,
                    $"dotnet new gamekit (minimal) FAILED with unexpected exit code:\nexit={exit}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

                var web = Path.Combine(workDir, "MiniSmoke", "src", "MiniSmoke");
                Assert.True(File.Exists(Path.Combine(web, "MiniSmoke.csproj")));

                var csproj = File.ReadAllText(Path.Combine(web, "MiniSmoke.csproj"));
                // Always-included packages (Core, Auth, OpenApi, Admin.UI) remain.
                Assert.Contains("GameKit.Core", csproj);
                Assert.Contains("GameKit.Auth", csproj);
                Assert.Contains("GameKit.OpenApi", csproj);
                Assert.Contains("GameKit.Admin.UI", csproj);
                // Skipped packages MUST be absent (conditional content blocks excised by engine).
                Assert.DoesNotContain("GameKit.Rankings", csproj);
                Assert.DoesNotContain("GameKit.Matchmaking", csproj);
                Assert.DoesNotContain("GameKit.Presence", csproj);

                var programCs = File.ReadAllText(Path.Combine(web, "Program.cs"));
                // Always-on Add*/Map* present (qualified to avoid matching prose in comments
                // that documents the receiver pattern — e.g. "we can call .AddAuth()…").
                Assert.Contains("gameKitBuilder.AddAuth(", programCs);
                Assert.Contains("builder.Services.AddGameKitOpenApi(", programCs);
                Assert.Contains("gameKitBuilder.AddGameKitAdmin(", programCs);
                Assert.Contains("app.MapAuth(", programCs);
                Assert.Contains("app.MapGameKit(", programCs);
                Assert.Contains("app.MapGameKitOpenApi(", programCs);
                // Skipped Add*/Map* MUST be absent at the call site. The receiver-qualified
                // form (`gameKitBuilder.AddX(` / `app.MapX(`) excludes false-positive matches
                // against comment prose that lists the methods (e.g. ".AddAuth() / .AddRankings()").
                Assert.DoesNotContain("gameKitBuilder.AddRankings(", programCs);
                Assert.DoesNotContain("gameKitBuilder.AddMatchmaking(", programCs);
                Assert.DoesNotContain("gameKitBuilder.AddPresence(", programCs);
                Assert.DoesNotContain("app.MapRankings(", programCs);
                Assert.DoesNotContain("app.MapMatchmaking(", programCs);
                Assert.DoesNotContain("app.MapPresence(", programCs);
                // The /demo/ladder-id/{name} helper depends on BOTH rankings + matchmaking,
                // so it must be excised when either is skipped.
                Assert.DoesNotContain("/demo/ladder-id/{name}", programCs);
                // The 'using' directives for skipped packages must also be excised.
                Assert.DoesNotContain("using GameKit.Rankings.Builder;", programCs);
                Assert.DoesNotContain("using GameKit.Matchmaking.Builder;", programCs);
                Assert.DoesNotContain("using GameKit.Presence.Builder;", programCs);
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { }
            }
        }
        finally
        {
            await UninstallTemplateAsync();
        }
    }

    // ----------------------------------------------------------------------------
    // Shared helpers — pack / install / uninstall the template package.
    // ----------------------------------------------------------------------------

    /// <summary>
    /// Packs <c>templates/GameKit.Templates/GameKit.Templates.csproj</c> into a
    /// unique temp directory and returns the produced .nupkg path. Pack may emit
    /// a non-fatal NU5017 diagnostic (no deps nor content — spurious for template
    /// packages); we treat the EXISTENCE of the .nupkg as the success signal.
    /// </summary>
    private static async Task<string> PackTemplateAsync(string repoRoot)
    {
        var templateCsproj = Path.Combine(repoRoot, "templates", "GameKit.Templates", "GameKit.Templates.csproj");
        Assert.True(File.Exists(templateCsproj),
            $"GameKit.Templates.csproj not found at {templateCsproj} — Plan 06-09 Task 1 not applied?");

        var artifactsDir = Path.Combine(Path.GetTempPath(),
            $"gamekit-dist03-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactsDir);

        var (exit, stdout, stderr) = await RunProcessAsync(
            "dotnet",
            $"pack \"{templateCsproj}\" -c Debug -o \"{artifactsDir}\" --nologo --verbosity quiet -p:UseSharedCompilation=false -p:BuildInParallel=false",
            repoRoot);

        var nupkg = Directory.EnumerateFiles(artifactsDir, "GameKit.Templates.*.nupkg")
            .FirstOrDefault(p => !p.EndsWith(".snupkg", StringComparison.Ordinal)
                              && !p.EndsWith(".symbols.nupkg", StringComparison.Ordinal));
        Assert.True(nupkg is not null,
            $"PackTemplateAsync did not produce a .nupkg in {artifactsDir}.\n" +
            $"pack exit={exit}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        return nupkg!;
    }

    /// <summary>Installs the produced template package via <c>dotnet new install</c>.</summary>
    private static async Task InstallTemplateAsync(string nupkg)
    {
        var (exit, stdout, stderr) = await RunProcessAsync(
            "dotnet",
            $"new install \"{nupkg}\"",
            Path.GetTempPath());
        Assert.True(exit == 0,
            $"dotnet new install FAILED:\nexit={exit}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    /// <summary>
    /// Uninstalls the template by package id. Tolerant of the "not installed"
    /// case — used in finally blocks so a failed install or interrupted prior
    /// run doesn't leak the registration across test invocations.
    /// </summary>
    private static async Task UninstallTemplateAsync()
    {
        var (_, _, _) = await RunProcessAsync(
            "dotnet",
            "new uninstall GameKit.Templates",
            Path.GetTempPath());
        // Intentionally ignore exit code — uninstall returns non-zero if the
        // template isn't currently installed, which is fine for cleanup.
    }

    /// <summary>
    /// Process spawn helper — mirrors <see cref="D26_NuspecExactPinGuardTests"/>'s
    /// helper. Disables MSBuild node reuse + the .NET CLI's persistent build
    /// server to prevent deadlock against the parent test host's MSBuild nodes.
    /// </summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
