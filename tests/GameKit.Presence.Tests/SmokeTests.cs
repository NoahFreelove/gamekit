// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using Xunit;

namespace GameKit.Presence.Tests;

/// <summary>
/// Wave 0 smoke test (Phase 6, Plan 06-03 Task 1): proves the unit-test project
/// loads and that the <c>GameKit.Presence</c> assembly can be discovered by name.
/// Plan 06-04 fills this assembly with <c>RedisPresenceProviderTests</c> +
/// <c>PresenceOptionsValidatorTests</c>; until then, this lightweight probe is
/// what CI runs to confirm the build wiring is sound.
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// Asserts the unit-test project loads AND the GameKit.Presence assembly is
    /// resolvable at runtime (catches a broken ProjectReference early).
    /// </summary>
    [Fact]
    public void TestProject_Loads()
    {
        var presenceAssembly = Assembly.Load("GameKit.Presence");
        Assert.NotNull(presenceAssembly);
        Assert.Equal("GameKit.Presence", presenceAssembly.GetName().Name);
    }
}
