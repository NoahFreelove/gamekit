// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameKit.Core.Services;
using Xunit;

namespace GameKit.Core.Tests.Services;

/// <summary>
/// Contract tests for <see cref="ILeaderLease"/> (SCALE-01).
/// Verifies that the interface exposes exactly the expected five members and that
/// <c>IMatchmakerLease</c> in <c>GameKit.Matchmaking</c> is an assignable alias-forward
/// of <see cref="ILeaderLease"/>. Uses pure reflection — no Testcontainers, no I/O.
/// </summary>
public sealed class LeaderLeaseContractTests
{
    /// <summary>
    /// <see cref="ILeaderLease"/> must declare exactly five members:
    /// <c>InstanceId</c>, <c>TryAcquireLeaseAsync</c>, <c>RenewLeaseAsync</c>,
    /// <c>ReleaseLeaseAsync</c>, and <c>QueryLeaseAsync</c>.
    /// Checks logical members (properties + methods) by name, not the raw reflection
    /// token list that includes accessor methods (e.g. <c>get_InstanceId</c>).
    /// </summary>
    [Fact]
    public void ILeaderLease_ExposesExactlyFiveExpectedMembers()
    {
        var iface = typeof(ILeaderLease);
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // Collect property names and non-accessor method names separately so that
        // compiler-generated accessor names (get_InstanceId) are not counted twice.
        var propertyNames = iface.GetProperties(flags).Select(p => p.Name);
        var methodNames = iface.GetMethods(flags)
            .Where(m => !m.IsSpecialName) // exclude get_/set_/add_/remove_ accessors
            .Select(m => m.Name);

        var declaredNames = propertyNames.Concat(methodNames).ToHashSet();

        var expected = new HashSet<string>
        {
            nameof(ILeaderLease.InstanceId),
            nameof(ILeaderLease.TryAcquireLeaseAsync),
            nameof(ILeaderLease.RenewLeaseAsync),
            nameof(ILeaderLease.ReleaseLeaseAsync),
            nameof(ILeaderLease.QueryLeaseAsync),
        };

        Assert.Equal(expected, declaredNames);
    }

    /// <summary>
    /// <c>IMatchmakerLease</c> (from <c>GameKit.Matchmaking.Services</c>) must be assignable
    /// to <see cref="ILeaderLease"/> — confirms the alias-forward relationship (SCALE-01).
    /// Loaded via reflection so this test project does not need a direct project reference to
    /// <c>GameKit.Matchmaking</c>. The assembly is resolved by locating
    /// <c>GameKit.Matchmaking.dll</c> alongside the test assembly (via
    /// <see cref="Assembly.Location"/>-relative path) or from the build output tree.
    /// </summary>
    [Fact]
    public void IMatchmakerLease_IsAssignableToILeaderLease()
    {
        // First try: the assembly may already be in the AppDomain (loaded via
        // Type.GetType will find it if the assembly was transitively loaded).
        Type? matchmakerLeaseType = Type.GetType(
            "GameKit.Matchmaking.Services.IMatchmakerLease, GameKit.Matchmaking");

        if (matchmakerLeaseType is null)
        {
            // Second try: load the assembly from a location adjacent to or near the
            // Core test assembly's output directory. GameKit.Matchmaking is built into
            // a sibling bin folder under the same Debug/net10.0 tree.
            var testAssemblyDir = Path.GetDirectoryName(
                typeof(LeaderLeaseContractTests).Assembly.Location)!;

            // Walk up to the repo root (tests/GameKit.Core.Tests/bin/Debug/net10.0 → repo root)
            // then down to the Matchmaking build output.
            var repoRoot = FindRepoRoot(testAssemblyDir);
            var matchmakingDll = repoRoot is not null
                ? Path.Combine(repoRoot, "src", "GameKit.Matchmaking", "bin", "Debug", "net10.0",
                    "GameKit.Matchmaking.dll")
                : null;

            if (matchmakingDll is not null && File.Exists(matchmakingDll))
            {
                var asm = Assembly.LoadFrom(matchmakingDll);
                matchmakerLeaseType = asm.GetType(
                    "GameKit.Matchmaking.Services.IMatchmakerLease");
            }
        }

        Assert.NotNull(matchmakerLeaseType);
        Assert.True(
            typeof(ILeaderLease).IsAssignableFrom(matchmakerLeaseType),
            $"{matchmakerLeaseType!.FullName} must extend ILeaderLease (SCALE-01 alias-forward).");
    }

    /// <summary>
    /// Walks parent directories from <paramref name="start"/> until it finds a directory
    /// containing a <c>.git</c> entry, indicating the repo root. Returns <c>null</c> if
    /// not found within 10 levels.
    /// </summary>
    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        }
        return null;
    }
}
