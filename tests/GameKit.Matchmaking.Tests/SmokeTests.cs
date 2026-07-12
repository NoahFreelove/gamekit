// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.Matchmaking.Tests;

/// <summary>
/// Smoke test that simply proves the unit test project loads and references resolve
/// (GameKit.Matchmaking + GameKit.Core + xUnit + Moq). Phase 5 Plan 05-01 Wave 0 artifact.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void TestProject_Loads() => Assert.True(true);
}
