// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// OPS-04 (Plan 06-08 Task 3, D-22): empirically proves that every GameKit src
/// package's <c>Internal.GameKitMarker.GameKitVersion</c> constant is stamped to the
/// SAME MinVer-derived version. Validates Plan 06-01's source-generator wire-up
/// across all 7 shipped packages (Pitfall 1 + Pitfall 2 defenders).
/// </summary>
/// <remarks>
/// <para>
/// D-22 corrected the earlier ROADMAP wording that said "6 packages" — Phase 6
/// introduced <c>GameKit.OpenApi</c> as the 7th. This test is the in-CI gate
/// that fails loudly if the source generator regresses + a future package is
/// added but not wired into the analyzer chain.
/// </para>
/// <para>
/// The generated <c>GameKitMarker</c> class is <c>internal static partial</c>, the
/// <c>GameKitVersion</c> field is a <c>public const string</c>. Reflection requires
/// <c>BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static</c>.
/// </para>
/// </remarks>
public sealed class OPS04_VersionStampedAcrossPackagesTests
{
    /// <summary>
    /// The full coordinated release-train set — every shipped GameKit src package
    /// (D-22). <c>GameKit.Build</c> is NOT in the list because it is the source-generator
    /// analyzer (build-only, never a runtime assembly) and is explicitly excluded by
    /// <see cref="Core.Hosting.GameKitVersionAssertionHostedService"/>.
    /// </summary>
    private static readonly string[] AllSevenGameKitPackages =
    {
        "GameKit.Core",
        "GameKit.Auth",
        "GameKit.Rankings",
        "GameKit.Matchmaking",
        "GameKit.Admin.UI",
        "GameKit.Presence",
        "GameKit.OpenApi",
    };

    /// <summary>
    /// Step 1: confirm every package has the source-generator-emitted marker. A missing
    /// marker indicates the <c>OutputItemType="Analyzer"</c> wire-up is dropped from
    /// the package's csproj.
    /// </summary>
    [Fact]
    public void All_Seven_GameKit_Packages_Have_GameKitMarker()
    {
        var missing = new List<string>();
        foreach (var packageName in AllSevenGameKitPackages)
        {
            var asm = Assembly.Load(packageName);
            var markerType = asm.GetType($"{packageName}.Internal.GameKitMarker", throwOnError: false);
            if (markerType is null)
            {
                missing.Add(packageName);
            }
        }

        Assert.True(
            missing.Count == 0,
            $"GameKitMarker type missing from: {string.Join(", ", missing)}. " +
            "Verify <ProjectReference Include=\"..\\GameKit.Build\\GameKit.Build.csproj\" " +
            "OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" /> in each csproj.");
    }

    /// <summary>
    /// Step 2: D-22 release-train assertion — every marker MUST report the SAME
    /// MinVer-derived version string. A divergent stamp indicates either a partial
    /// rebuild from a stale binary cache, or — more importantly — a future release
    /// where one package was rebuilt without the rest (which is exactly what the
    /// coordinated train forbids).
    /// </summary>
    [Fact]
    public void All_Seven_GameKit_Packages_Stamp_Same_MinVer_Version()
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var packageName in AllSevenGameKitPackages)
        {
            var asm = Assembly.Load(packageName);
            var markerType = asm.GetType($"{packageName}.Internal.GameKitMarker", throwOnError: true)!;
            var field = markerType.GetField(
                "GameKitVersion",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

            var value = field.GetValue(null) as string;
            Assert.False(
                string.IsNullOrWhiteSpace(value),
                $"{packageName}.Internal.GameKitMarker.GameKitVersion is null or whitespace.");
            Assert.NotEqual("0.0.0", value);

            versions[packageName] = value!;
        }

        var distinct = versions.Values.Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(
            distinct.Length == 1,
            $"GameKitVersion mismatch across packages: " +
            $"{string.Join(", ", versions.Select(kv => $"{kv.Key}={kv.Value}"))}.");
    }
}
