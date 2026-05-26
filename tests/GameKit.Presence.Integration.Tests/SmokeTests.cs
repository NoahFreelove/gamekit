// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using Xunit;

namespace GameKit.Presence.Integration.Tests;

/// <summary>
/// Wave 0 smoke test (Phase 6, Plan 06-03 Task 2): proves the integration-test
/// project loads and that the <c>GameKit.Presence</c> + <c>GameKit.Auth</c>
/// assemblies are resolvable. Plan 06-04 fills this assembly with the
/// heartbeat TTL / online-status integration tests; Plan 06-05 fills it
/// with the in-match precedence transition tests.
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// Asserts the integration-test project loads AND its primary
    /// ProjectReferences resolve at runtime.
    /// </summary>
    [Fact]
    public void TestProject_Loads()
    {
        Assert.NotNull(Assembly.Load("GameKit.Presence"));
        Assert.NotNull(Assembly.Load("GameKit.Auth"));
        Assert.NotNull(Assembly.Load("GameKit.Core"));
    }
}
