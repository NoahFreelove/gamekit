// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// OPS-04 (Plan 06-08 Task 3, D-22 + Plan 12-01 DIST-07): empirically proves that every
/// GameKit src package's <c>Internal.GameKitMarker.GameKitVersion</c> constant is stamped
/// to the SAME MinVer-derived version. Validates Plan 06-01's source-generator wire-up
/// across all 12 shipped packages (Pitfall 1 + Pitfall 2 defenders).
/// </summary>
/// <remarks>
/// <para>
/// D-22 corrected the earlier ROADMAP wording that said "6 packages" — Phase 6
/// introduced <c>GameKit.OpenApi</c> as the 7th. DIST-07 (Phase 12) extends the train
/// to 12 packages adding the five Phase-12 additions:
/// <c>GameKit.Auth.Argon2</c>, <c>GameKit.Auth.Google</c>, <c>GameKit.Auth.Apple</c>,
/// <c>GameKit.Auth.Epic</c>, and <c>GameKit.Lobby</c>.
/// This test is the in-CI gate that fails loudly if the source generator regresses +
/// a future package is added but not wired into the analyzer chain.
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
    /// (D-22 extended by DIST-07). <c>GameKit.Build</c> is NOT in the list because it
    /// is the source-generator analyzer (build-only, never a runtime assembly) and is
    /// explicitly excluded by
    /// <see cref="Core.Hosting.GameKitVersionAssertionHostedService"/>.
    /// </summary>
    private static readonly string[] AllTwelveGameKitPackages =
    {
        // Original 7 packages (D-22 / Plan 06-08)
        "GameKit.Core",
        "GameKit.Auth",
        "GameKit.Rankings",
        "GameKit.Matchmaking",
        "GameKit.Admin.UI",
        "GameKit.Presence",
        "GameKit.OpenApi",
        // Phase-12 additions (DIST-07 / Plan 12-01)
        "GameKit.Auth.Argon2",
        "GameKit.Auth.Google",
        "GameKit.Auth.Apple",
        "GameKit.Auth.Epic",
        "GameKit.Lobby",
    };

    /// <summary>
    /// Step 1: confirm every package has the source-generator-emitted marker. A missing
    /// marker indicates the <c>OutputItemType="Analyzer"</c> wire-up is dropped from
    /// the package's csproj.
    /// </summary>
    [Fact]
    public void All_Twelve_GameKit_Packages_Have_GameKitMarker()
    {
        var missing = new List<string>();
        foreach (var packageName in AllTwelveGameKitPackages)
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
            $"GameKitMarker type missing from {missing.Count} of {AllTwelveGameKitPackages.Length} packages: " +
            $"{string.Join(", ", missing)}. " +
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
    public void All_Twelve_GameKit_Packages_Stamp_Same_MinVer_Version()
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var packageName in AllTwelveGameKitPackages)
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

    /// <summary>
    /// SC#4 (DIST-07 / Plan 12-01): the five Phase-12 additions are proven on the
    /// MinVer coordinated release train — each carries a non-"0.0.0" stamped version,
    /// and all 12 packages share exactly one version string. Complements Step 2 by
    /// explicitly naming the five new packages in the failure message, making CI
    /// failures immediately actionable.
    /// </summary>
    [Fact(DisplayName = "SC#4: All 12 packages incl. the 5 Phase-12 additions share one non-0.0.0 version")]
    public void SC4_All_Twelve_Packages_Including_Phase12_Are_On_Release_Train()
    {
        var phase12Packages = new[]
        {
            "GameKit.Auth.Argon2",
            "GameKit.Auth.Google",
            "GameKit.Auth.Apple",
            "GameKit.Auth.Epic",
            "GameKit.Lobby",
        };

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var packageName in AllTwelveGameKitPackages)
        {
            var asm = Assembly.Load(packageName);
            var markerType = asm.GetType($"{packageName}.Internal.GameKitMarker", throwOnError: true)!;
            var field = markerType.GetField(
                "GameKitVersion",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

            var value = field.GetValue(null) as string;

            Assert.False(
                string.IsNullOrWhiteSpace(value),
                $"[SC#4] {packageName}.Internal.GameKitMarker.GameKitVersion is null or whitespace.");

            Assert.True(
                !string.Equals(value, "0.0.0", StringComparison.Ordinal),
                $"[SC#4] {packageName} reports un-stamped version \"0.0.0\" — " +
                $"verify the GameKit.Build analyzer OutputItemType=\"Analyzer\" wire-up in {packageName}.csproj.");

            versions[packageName] = value!;
        }

        // Explicitly assert all 5 Phase-12 packages are present and stamped.
        foreach (var p12pkg in phase12Packages)
        {
            Assert.True(
                versions.ContainsKey(p12pkg),
                $"[SC#4] Phase-12 package {p12pkg} was not found in versions map — " +
                "ensure the ProjectReference is wired into Distribution.Integration.Tests.csproj.");
        }

        var distinct = versions.Values.Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(
            distinct.Length == 1,
            $"[SC#4] GameKitVersion mismatch — {AllTwelveGameKitPackages.Length} packages must share " +
            $"exactly 1 version string but found {distinct.Length}: " +
            $"{string.Join(", ", versions.Select(kv => $"{kv.Key}={kv.Value}"))}.");
    }
}
