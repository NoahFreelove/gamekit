// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// DIST-04 (Plan 06-09 Task 2): pure file-IO contract test that asserts the
/// produced <c>GameKit.Templates</c> NuGet package contains the expected
/// template-engine layout — <c>content/GameKit.SampleGame/.template.config/template.json</c>
/// + the two <c>Program.cs</c> files (web tier + game-server tier) — and that
/// <c>template.json</c> declares the four <c>--skip-*</c> opt-out symbols
/// plus a populated <c>postActions</c> array (D-12 + D-13).
/// </summary>
/// <remarks>
/// <para>
/// This is the EMPIRICAL contract for the template-engine layout. If a future
/// edit to <see cref="GameKit.Distribution.Integration.Tests.DIST04_TemplatePackageShapeTests"/>'s
/// sibling <c>GameKit.Templates.csproj</c> drops <c>NoDefaultExcludes=true</c>
/// or moves the content path, <c>dotnet new install</c> on the resulting
/// package would silently fail to discover the inner template — this test
/// fires loudly at pack time so the regression never reaches a consumer.
/// </para>
/// <para>
/// Sister test <see cref="DIST03_TemplateSampleGameSmokeTests"/> goes
/// one step further: installs the package, instantiates the template, and
/// asserts the generated tree structure (substitution + conditional content)
/// behaves as designed.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DIST04_TemplatePackageShapeTests
{
    /// <summary>
    /// The literal set of in-package paths that MUST exist after <c>dotnet pack</c>.
    /// Loss of any one of these breaks <c>dotnet new install</c> for end users.
    /// </summary>
    private static readonly string[] RequiredPackageEntries =
    {
        // The template manifest itself — without this, the template engine refuses
        // to register the package as a template source (NoDefaultExcludes=true is
        // what keeps the dot-prefixed .template.config dir inside the pack).
        "content/GameKit.SampleGame/.template.config/template.json",

        // The dotnetcli.host.json sibling that maps camelCase symbol names to
        // kebab-case CLI aliases (--skip-auth / --skip-rankings / etc.).
        "content/GameKit.SampleGame/.template.config/dotnetcli.host.json",

        // Web-tier Program.cs (carries the //#if (!skipX) conditional blocks).
        "content/GameKit.SampleGame/src/GameKit.SampleGame/Program.cs",

        // Web-tier csproj (carries the <!--#if (!skipX)--> conditional PackageRefs).
        "content/GameKit.SampleGame/src/GameKit.SampleGame/GameKit.SampleGame.csproj",

        // Game-server-tier Program.cs (no conditionals — GameServer is package-
        // independent per D-13).
        "content/GameKit.SampleGame/src/GameKit.SampleGame.GameServer/Program.cs",

        // Game-server-tier csproj.
        "content/GameKit.SampleGame/src/GameKit.SampleGame.GameServer/GameKit.SampleGame.GameServer.csproj",

        // Top-level generated README + the solution that binds both projects.
        "content/GameKit.SampleGame/README.md",
        "content/GameKit.SampleGame/GameKit.SampleGame.sln",

        // docker-compose for local dev Postgres + Redis.
        "content/GameKit.SampleGame/docker-compose.yml",

        // The post-action script (D-13). MUST live at the template root so its
        // path is invariant after sourceName substitution.
        "content/GameKit.SampleGame/scripts/gen-test-rsa-pem.sh",
    };

    /// <summary>
    /// The four <c>--skip-*</c> symbols that <c>template.json</c> MUST declare
    /// per D-12. Missing any of these breaks the documented opt-out UX.
    /// </summary>
    private static readonly string[] RequiredOptOutSymbols =
    {
        "skipAuth",
        "skipRankings",
        "skipMatchmaking",
        "skipPresence",
    };

    /// <summary>
    /// DIST-04 primary assertion: pack <c>templates/GameKit.Templates/</c> and
    /// inspect the produced .nupkg's zip listing for every required entry.
    /// </summary>
    [Fact]
    public async Task PackedTemplate_ContainsAllRequiredEntries()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var templateCsproj = Path.Combine(repoRoot, "templates", "GameKit.Templates", "GameKit.Templates.csproj");
        Assert.True(File.Exists(templateCsproj),
            $"GameKit.Templates.csproj not found at {templateCsproj} — Plan 06-09 Task 1 not applied?");

        var artifactsDir = Path.Combine(Path.GetTempPath(),
            $"gamekit-dist04-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactsDir);

        try
        {
            var (exitCode, stdout, stderr) = await RunProcessAsync(
                "dotnet",
                $"pack \"{templateCsproj}\" -c Debug -o \"{artifactsDir}\" --nologo --verbosity quiet -p:UseSharedCompilation=false -p:BuildInParallel=false",
                repoRoot);

            // Pack should produce a .nupkg even though NU5017 (no deps nor content) may
            // fire as a non-fatal diagnostic — the produced package still has correct
            // content per RequiredPackageEntries. Treat the EXISTENCE of the .nupkg as
            // the success signal, not the exit code.
            var producedNupkgs = Directory.EnumerateFiles(artifactsDir, "GameKit.Templates.*.nupkg")
                .Where(p => !p.EndsWith(".snupkg", StringComparison.Ordinal))
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.Ordinal))
                .ToList();

            Assert.True(producedNupkgs.Count == 1,
                $"Expected exactly 1 GameKit.Templates.*.nupkg in {artifactsDir}, got {producedNupkgs.Count}.\n" +
                $"pack exit={exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            var nupkg = producedNupkgs[0];

            using var zip = ZipFile.OpenRead(nupkg);
            // Normalize zip entry separators to '/' for cross-platform consistency.
            var entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);

            var missing = RequiredPackageEntries.Where(req => !entries.Contains(req)).ToList();
            Assert.True(missing.Count == 0,
                "DIST-04 violated — produced .nupkg is missing required entries:\n" +
                string.Join("\n", missing) +
                $"\n\nActual entries:\n{string.Join("\n", entries.OrderBy(e => e, StringComparer.Ordinal))}");
        }
        finally
        {
            try { Directory.Delete(artifactsDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// DIST-04 secondary assertion: crack open the packed template.json and
    /// assert it declares (a) <c>sourceName == "GameKit.SampleGame"</c> +
    /// (b) every required <c>--skip-*</c> opt-out symbol per D-12 + (c) a
    /// non-empty <c>postActions</c> array per D-13.
    /// </summary>
    [Fact]
    public async Task PackedTemplate_TemplateJson_DeclaresRequiredSymbolsAndPostActions()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var templateCsproj = Path.Combine(repoRoot, "templates", "GameKit.Templates", "GameKit.Templates.csproj");
        Assert.True(File.Exists(templateCsproj),
            $"GameKit.Templates.csproj not found at {templateCsproj}.");

        var artifactsDir = Path.Combine(Path.GetTempPath(),
            $"gamekit-dist04-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactsDir);

        try
        {
            var (exitCode, stdout, stderr) = await RunProcessAsync(
                "dotnet",
                $"pack \"{templateCsproj}\" -c Debug -o \"{artifactsDir}\" --nologo --verbosity quiet -p:UseSharedCompilation=false -p:BuildInParallel=false",
                repoRoot);

            var nupkg = Directory.EnumerateFiles(artifactsDir, "GameKit.Templates.*.nupkg")
                .FirstOrDefault(p => !p.EndsWith(".snupkg", StringComparison.Ordinal)
                                  && !p.EndsWith(".symbols.nupkg", StringComparison.Ordinal));
            Assert.True(nupkg is not null,
                $"No GameKit.Templates.*.nupkg found in {artifactsDir}.\n" +
                $"pack exit={exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            using var zip = ZipFile.OpenRead(nupkg!);
            var entry = zip.GetEntry("content/GameKit.SampleGame/.template.config/template.json");
            Assert.NotNull(entry);

            using var stream = entry!.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // (a) sourceName must be "GameKit.SampleGame" — the literal token the
            // template engine replaces with the consumer's -n value across file
            // contents AND file/directory names.
            Assert.True(root.TryGetProperty("sourceName", out var sourceName),
                "template.json missing 'sourceName' field.");
            Assert.Equal("GameKit.SampleGame", sourceName.GetString());

            // (b) every required opt-out symbol must be declared with type=parameter
            // datatype=bool defaultValue=false.
            Assert.True(root.TryGetProperty("symbols", out var symbols),
                "template.json missing 'symbols' object.");

            foreach (var requiredSymbol in RequiredOptOutSymbols)
            {
                Assert.True(symbols.TryGetProperty(requiredSymbol, out var sym),
                    $"template.json symbols missing '{requiredSymbol}' (D-12).");
                Assert.Equal("parameter", sym.GetProperty("type").GetString());
                Assert.Equal("bool", sym.GetProperty("datatype").GetString());
                Assert.Equal("false", sym.GetProperty("defaultValue").GetString());
            }

            // (c) postActions must be a non-empty array that invokes
            // ./scripts/gen-test-rsa-pem.sh with continueOnError=true (D-13 + Pitfall 5).
            Assert.True(root.TryGetProperty("postActions", out var postActions),
                "template.json missing 'postActions' array.");
            Assert.True(postActions.ValueKind == JsonValueKind.Array,
                "postActions must be an array.");
            Assert.True(postActions.GetArrayLength() >= 1,
                "postActions must contain at least one entry (D-13).");

            var firstAction = postActions[0];
            // Use the documented run-script GUID (RESEARCH Pattern 8 line 818).
            Assert.Equal("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2",
                firstAction.GetProperty("actionId").GetString());
            // continueOnError=true keeps Windows-without-WSL instantiation working
            // (Pitfall 5 mitigation — falls back to manualInstructions).
            Assert.True(firstAction.GetProperty("continueOnError").GetBoolean(),
                "continueOnError MUST be true (D-13 Pitfall 5 Windows fallback).");
        }
        finally
        {
            try { Directory.Delete(artifactsDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Process spawn helper — same shape as <see cref="D26_NuspecExactPinGuardTests"/>'s
    /// helper to keep the test-suite spawn behaviour consistent (MSBuild node reuse
    /// disabled, .NET CLI build server disabled to prevent deadlock with the parent
    /// test host's MSBuild nodes).
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
