// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// D-26 primary defense (Plan 06-08 Task 4, WARNING #4 fix): empirically asserts
/// the coordinated release-train exact-pin enforcement at the produced-nuspec
/// layer (the surface NuGet consumers actually see) AND at the source-csproj
/// layer (defense-in-depth against a developer typo).
/// </summary>
/// <remarks>
/// <para>
/// Plan 06-01 wired <c>GameKit.targets</c> at the repo root with an
/// <c>&lt;ItemDefinitionGroup&gt;</c> that stamps
/// <c>PackageVersion="[$(Version)]"</c> onto every <c>ProjectReference</c>. At
/// pack time, <c>GenerateNuspec</c> reads that metadata when converting
/// sibling-package <c>ProjectReference</c>s into <c>PackageReference</c>s in
/// the produced .nuspec. The square-bracket <c>[X.Y.Z]</c> syntax is NuGet's
/// exact-pin operator (blocks restore from accepting any other version —
/// NU1605 fires on mismatch).
/// </para>
/// <para>
/// Test 1 (the primary defense per D-26 + CONTEXT.md) cracks open every
/// produced <c>.nupkg</c> and asserts every <c>&lt;dependency id="GameKit.*"&gt;</c>
/// entry matches the literal square-bracket exact-pin pattern. Test 2 is the
/// source-side guard: greps every <c>src/GameKit.*/*.csproj</c> for any
/// <c>Version="*"</c> / <c>Version="^…"</c> wildcard / caret. Both must pass
/// on current main; both fire loudly on any future regression.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class D26_NuspecExactPinGuardTests
{
    /// <summary>
    /// Regex matches NuGet exact-pin square-bracket version syntax:
    /// <c>[X.Y.Z]</c> or <c>[X.Y.Z.W]</c> or <c>[X.Y.Z-pre.1]</c> etc.
    /// The opening + closing literal brackets are the exact-pin operator; the
    /// trailing capture allows the standard SemVer pre-release / metadata
    /// alphabet (digits, dots, dashes, alphanumerics).
    /// </summary>
    private static readonly Regex ExactPinRegex = new(
        @"^\[\d+\.\d+\.\d+(\.\d+)?(-[A-Za-z0-9.\-]+)?(\+[A-Za-z0-9.\-]+)?\]$",
        RegexOptions.Compiled);

    /// <summary>
    /// Regex matches a forbidden wildcard <c>Version="*"</c> or caret-prefix
    /// <c>Version="^…"</c> attribute in any csproj XML. Either pattern would
    /// produce a floating-version sibling dep at restore time, defeating the
    /// coordinated release train.
    /// </summary>
    private static readonly Regex WildcardOrCaretRegex = new(
        "Version=\"(\\*|\\^[^\"]*)\"",
        RegexOptions.Compiled);

    /// <summary>
    /// D-26 primary defense: pack every src/GameKit.* package, crack the produced
    /// .nuspec, and assert every <c>&lt;dependency id="GameKit.*"&gt;</c> entry uses
    /// literal square-bracket exact-pin syntax.
    /// </summary>
    [Fact]
    public async Task Produced_Nuspec_For_Every_GameKit_Package_Pins_Sibling_GameKit_Deps_With_Exact_Square_Brackets()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var artifactsDir = Path.Combine(Path.GetTempPath(),
            $"gamekit-d26-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactsDir);

        try
        {
            // dotnet pack every shipped src/GameKit.* package into the temp artifacts dir.
            // -c Debug to match the test build configuration (no need for Release here —
            // produced .nuspec content is independent of compilation config).
            // /p:IsPackable=true forces packing for projects with IsPackable=false defaults
            // (most of our src/GameKit.* are NuGet-packable already; samples + GameKit.Build
            // are excluded via Directory filter below).
            var srcDir = Path.Combine(repoRoot, "src");
            var packTargets = Directory.EnumerateDirectories(srcDir, "GameKit.*")
                .Select(dir => new
                {
                    Name = Path.GetFileName(dir)!,
                    Csproj = Path.Combine(dir, Path.GetFileName(dir) + ".csproj"),
                })
                // GameKit.Build is the source generator (IsPackable=false, build-only). The
                // CLI is intentionally not in the coordinated release train (it's a tool).
                .Where(t => t.Name != "GameKit.Build" && t.Name != "GameKit.Cli")
                .Where(t => File.Exists(t.Csproj))
                .ToList();

            Assert.NotEmpty(packTargets);

            var packErrors = new List<string>();
            foreach (var target in packTargets)
            {
                var (exitCode, stdout, stderr) = await RunProcessAsync(
                    "dotnet",
                    $"pack \"{target.Csproj}\" -c Debug -o \"{artifactsDir}\" --nologo --verbosity quiet -p:UseSharedCompilation=false -p:BuildInParallel=false",
                    repoRoot);

                if (exitCode != 0)
                {
                    packErrors.Add($"{target.Name}: exit={exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
                }
            }

            Assert.True(packErrors.Count == 0,
                "dotnet pack failed for: " + string.Join("\n---\n", packErrors));

            // For each produced .nupkg, extract the .nuspec entry, parse it, and assert
            // every <dependency id="GameKit.*"> uses the literal [X.Y.Z] exact-pin syntax.
            var nupkgs = Directory.EnumerateFiles(artifactsDir, "GameKit.*.nupkg")
                // Exclude .symbols.nupkg (snupkgs end .snupkg; symbols pre-Source-Link era
                // used .symbols.nupkg — defensive filter).
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(nupkgs);

            var nuspecViolations = new List<string>();
            var emittedDepSamples = new List<string>();

            foreach (var nupkg in nupkgs)
            {
                using var zip = ZipFile.OpenRead(nupkg);
                var nuspecEntry = zip.Entries
                    .FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(nuspecEntry);

                using var stream = nuspecEntry!.Open();
                var doc = await XDocument.LoadAsync(stream, LoadOptions.None, default);

                // .nuspec uses a default namespace; XPath would need a namespace manager.
                // Simpler: iterate all <dependency> elements regardless of namespace.
                var depElements = doc.Descendants()
                    .Where(e => e.Name.LocalName == "dependency")
                    .ToList();

                foreach (var dep in depElements)
                {
                    var id = (string?)dep.Attribute("id");
                    var version = (string?)dep.Attribute("version");

                    if (id is null || !id.StartsWith("GameKit.", StringComparison.Ordinal))
                        continue;

                    // Sibling GameKit.* dep — must use exact-pin square-bracket syntax.
                    if (version is null || !ExactPinRegex.IsMatch(version))
                    {
                        nuspecViolations.Add(
                            $"{Path.GetFileName(nupkg)}: <dependency id=\"{id}\" version=\"{version}\" /> " +
                            $"does NOT match [X.Y.Z] exact-pin pattern.");
                    }
                    else if (emittedDepSamples.Count < 3)
                    {
                        // Capture a couple of valid samples for the SUMMARY's empirical proof.
                        emittedDepSamples.Add(
                            $"{Path.GetFileName(nupkg)}: <dependency id=\"{id}\" version=\"{version}\" />");
                    }
                }
            }

            Assert.True(
                nuspecViolations.Count == 0,
                "D-26 primary defense violated — produced .nuspec contains non-exact-pin sibling dep(s):\n" +
                string.Join("\n", nuspecViolations));

            // Defensive sanity: we should have observed at least ONE sibling dep across the
            // packed set; if every package's nuspec has zero sibling refs, something's wrong
            // with the test discovery and the assertion above is vacuously true.
            Assert.NotEmpty(emittedDepSamples);
        }
        finally
        {
            try { Directory.Delete(artifactsDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// D-26 source-side defense-in-depth: enumerate every <c>src/GameKit.*/*.csproj</c>
    /// and assert no <c>Version="*"</c> wildcard or <c>Version="^…"</c> caret attribute
    /// appears. Catches a developer typing a floating-version pin BEFORE pack-time
    /// (the .nuspec defense kicks in at pack; this catches at file-save).
    /// </summary>
    [Fact]
    public void Source_Csprojs_For_GameKit_Packages_Have_No_Wildcard_Or_Caret_Version_Attributes()
    {
        var repoRoot = GitRootLocator.FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");

        // Walk every src/GameKit.* directory and inspect all .csproj files (top-level
        // only — sub-folders never carry csprojs in this repo's layout).
        var violations = new List<string>();
        foreach (var packageDir in Directory.EnumerateDirectories(srcDir, "GameKit.*"))
        {
            foreach (var csproj in Directory.EnumerateFiles(packageDir, "*.csproj"))
            {
                var content = File.ReadAllText(csproj);
                foreach (Match match in WildcardOrCaretRegex.Matches(content))
                {
                    violations.Add($"{csproj}: {match.Value}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "D-26 source-side defense violated — wildcard / caret Version attribute(s) found in src/GameKit.*/*.csproj:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// Process spawn helper. Captures stdout + stderr separately. Default working
    /// directory is the repo root so relative paths in arguments resolve correctly.
    /// Disables MSBuild node reuse + the .NET CLI's persistent build server so the
    /// spawned <c>dotnet pack</c> does not deadlock against the parent
    /// <c>dotnet test</c> host's own MSBuild nodes (both share the global node
    /// pool by default — see https://github.com/dotnet/sdk/issues/14922).
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

        // Isolate from the parent test host's MSBuild nodes — without these the spawned
        // pack invocation can hang trying to attach to the parent's node pool.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
