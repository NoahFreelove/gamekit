// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

// Anchor each assembly via a known migration class from that package.
// These typeof() references guarantee the test fails at compile time if a
// migration class is renamed or removed, rather than silently scanning the
// wrong assembly at runtime.
using CoreInitial = GameKit.Core.Migrations.CoreInitial;
using AuthInitial = GameKit.Auth.Migrations.AuthInitial;
using AdminInitial = GameKit.Admin.UI.Migrations.AdminInitial;
using RankingsInitial = GameKit.Rankings.Migrations.RankingsInitial;
using MatchmakingInitial = GameKit.Matchmaking.Migrations.MatchmakingInitial;
using LobbyInitial = GameKit.Lobby.Data.Migrations.LobbyInitial;

namespace GameKit.Core.Tests;

/// <summary>
/// DR-05 / DR-07c — asserts that each package's latest migration timestamp is
/// lexicographically greater than the previous package's latest migration timestamp,
/// in the canonical application order: Core → Auth → Admin → Rankings → Matchmaking → Lobby.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test exists:</b> EF Core applies migrations in the order registered per package
/// (by the timestamp prefix recorded in <c>[Migration("timestamp_Name")]</c>). Cross-package
/// ordering is not enforced by EF Core itself — it is a convention that must be maintained
/// manually. This test acts as a regression gate so that any new migration that breaks the
/// ordering fails CI immediately.
/// </para>
/// <para>
/// <b>Ordering technique:</b> reflection scans each package assembly for all types that inherit
/// (directly or transitively) from <c>Microsoft.EntityFrameworkCore.Migrations.Migration</c>,
/// reads the <c>[Migration("timestamp_Name")]</c> attribute value from each type (which contains
/// the canonical EF Core migration identifier e.g. <c>"20260622000000_AddGameSessionIdempotencyKey"</c>),
/// orders by that identifier string, and takes the lexicographically last as the "latest" migration.
/// </para>
/// <para>
/// <b>Assembly anchoring:</b> each package's assembly is obtained via
/// <c>typeof(SomeKnownMigration).Assembly</c> rather than <c>Assembly.Load</c> or
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c>, ensuring the test fails at compile time
/// (not silently at runtime) if a known migration class disappears.
/// </para>
/// </remarks>
public class MigrationTimestampTests
{
    /// <summary>
    /// Asserts that for every consecutive pair of packages in the canonical application order,
    /// the later package's latest migration timestamp is lexicographically (Ordinal) greater
    /// than the earlier package's latest migration timestamp.
    /// </summary>
    [Fact]
    public void PackageMigrations_LatestTimestamp_AreInCorrectOrder()
    {
        // Canonical application order per docs/ops/migrations-runbook.md and DR-07.
        // Each entry is anchored to a known migration class so the correct assembly is loaded.
        var packages = new[]
        {
            (Name: "Core",        Assembly: typeof(CoreInitial).Assembly),
            (Name: "Auth",        Assembly: typeof(AuthInitial).Assembly),
            (Name: "Admin",       Assembly: typeof(AdminInitial).Assembly),
            (Name: "Rankings",    Assembly: typeof(RankingsInitial).Assembly),
            (Name: "Matchmaking", Assembly: typeof(MatchmakingInitial).Assembly),
            (Name: "Lobby",       Assembly: typeof(LobbyInitial).Assembly),
        };

        string? previousLatest = null;
        string? previousName = null;

        foreach (var (name, assembly) in packages)
        {
            // Collect all Migration subclasses, excluding EF-generated ModelSnapshot types.
            // Use the [Migration("timestamp_Name")] attribute to get the canonical EF Core
            // migration identifier (which is timestamp-prefixed regardless of the class name).
            var migrationEntries = assembly.GetTypes()
                .Where(t =>
                    t.IsSubclassOf(typeof(Migration)) &&
                    !t.Name.EndsWith("ModelSnapshot", StringComparison.Ordinal))
                .Select(t => new
                {
                    Type = t,
                    Id = t.GetCustomAttributes(typeof(MigrationAttribute), inherit: false)
                           .Cast<MigrationAttribute>()
                           .Select(a => a.Id)
                           .FirstOrDefault(),
                })
                .Where(entry => entry.Id != null)
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .ToList();

            // A missing migration assembly is a packaging mistake — fail loudly.
            Assert.NotEmpty(migrationEntries);

            var latest = migrationEntries.Last().Id!;
            // e.g. "20260622000000_AddGameSessionIdempotencyKey"

            if (previousLatest is not null)
            {
                Assert.True(
                    string.Compare(latest, previousLatest, StringComparison.Ordinal) > 0,
                    $"{name} latest migration ({latest}) must be lexicographically AFTER " +
                    $"{previousName} latest migration ({previousLatest}). " +
                    $"Add a no-op ordering-marker migration to {name} with a timestamp after {previousLatest}.");
            }

            previousLatest = latest;
            previousName = name;
        }
    }

    /// <summary>
    /// Asserts that every package in the canonical application order has at least one migration,
    /// so that a future packaging mistake that drops a migration assembly fails loudly rather
    /// than silently passing the ordering test.
    /// </summary>
    [Fact]
    public void AllPackages_HaveAtLeastOneMigration()
    {
        var packages = new[]
        {
            (Name: "Core",        Assembly: typeof(CoreInitial).Assembly),
            (Name: "Auth",        Assembly: typeof(AuthInitial).Assembly),
            (Name: "Admin",       Assembly: typeof(AdminInitial).Assembly),
            (Name: "Rankings",    Assembly: typeof(RankingsInitial).Assembly),
            (Name: "Matchmaking", Assembly: typeof(MatchmakingInitial).Assembly),
            (Name: "Lobby",       Assembly: typeof(LobbyInitial).Assembly),
        };

        foreach (var (name, assembly) in packages)
        {
            var migrationTypes = assembly.GetTypes()
                .Where(t =>
                    t.IsSubclassOf(typeof(Migration)) &&
                    !t.Name.EndsWith("ModelSnapshot", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                migrationTypes.Count > 0,
                $"Package '{name}' has no Migration subclasses in assembly '{assembly.GetName().Name}'. " +
                $"The assembly reference may be wrong or all migrations were accidentally removed.");
        }
    }
}
