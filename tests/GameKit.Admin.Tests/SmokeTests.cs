// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.Admin.Tests;

/// <summary>
/// Wave 0 baseline smoke test — proves the Admin unit test project compiles and loads under
/// <c>dotnet test</c>. Later plans (03-03, 03-05, 03-07, 03-11) append real assertions; this
/// placeholder keeps the project green between waves.
/// </summary>
public class SmokeTests
{
    /// <summary>Baseline xUnit discovery + assembly-load check.</summary>
    [Fact]
    public void TestProject_Loads()
    {
        // Prove type loading works (InternalsVisibleTo permits us to touch AdminUiMarker from GameKit.Admin.Tests)
        Assert.NotNull(typeof(GameKit.Admin.UI.AdminUiMarker));
    }
}
