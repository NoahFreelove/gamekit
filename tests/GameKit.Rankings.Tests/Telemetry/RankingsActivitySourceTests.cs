// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.IO;
using System.Reflection;
using GameKit.Core.Telemetry;
using GameKit.Rankings.Telemetry;
using Xunit;

namespace GameKit.Rankings.Tests.Telemetry;

/// <summary>
/// Criterion #5 enforcement tests: <see cref="RankingsActivitySource"/> is extracted into a
/// canonical <c>Telemetry/</c> class, its <see cref="RankingsActivitySource.SourceName"/>
/// matches the Core constant, and <c>RankingsTickerService.cs</c> no longer declares an inline
/// <see cref="System.Diagnostics.ActivitySource"/> (Plan 13-03).
/// </summary>
public sealed class RankingsActivitySourceTests
{
    /// <summary>
    /// <see cref="RankingsActivitySource.SourceName"/> must equal
    /// <see cref="GameKitTelemetry.RankingsTickerSourceName"/> — criterion #5 reflection test.
    /// Drift here would silently break operator <c>AddSource(...)</c> calls and cause
    /// all Rankings spans to be discarded.
    /// </summary>
    [Fact]
    public void SourceName_EqualsGameKitTelemetry_RankingsTickerSourceName()
    {
        Assert.Equal(
            GameKitTelemetry.RankingsTickerSourceName,
            RankingsActivitySource.SourceName);
    }

    /// <summary>
    /// <see cref="RankingsActivitySource.Source"/> must be non-null and its
    /// <see cref="System.Diagnostics.ActivitySource.Name"/> must equal
    /// <c>"GameKit.Rankings.Ticker"</c>.
    /// </summary>
    [Fact]
    public void Source_IsNonNull_AndNameMatchesExpected()
    {
        var source = RankingsActivitySource.Source;
        Assert.NotNull(source);
        Assert.Equal("GameKit.Rankings.Ticker", source.Name);
    }

    /// <summary>
    /// Source-assert: <c>RankingsTickerService.cs</c> must NOT contain
    /// <c>new ActivitySource(</c> — the inline field has been extracted to
    /// <see cref="RankingsActivitySource"/>.
    /// Fails until the extraction refactor is applied (RED gate).
    /// </summary>
    [Fact]
    public void RankingsTickerService_DoesNotContain_InlineActivitySourceDeclaration()
    {
        // Walk from the assembly location to the solution root, then to the source file.
        // The test assembly lives at:
        //   <worktree>/tests/GameKit.Rankings.Tests/bin/Debug/net10.0/
        // We need to go up 5 levels to reach the worktree root.
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var dir = new DirectoryInfo(assemblyDir);
        for (var i = 0; i < 5; i++)
            dir = dir.Parent!;

        var serviceFile = Path.Combine(
            dir.FullName,
            "src", "GameKit.Rankings", "Services", "RankingsTickerService.cs");

        Assert.True(File.Exists(serviceFile),
            $"RankingsTickerService.cs not found at expected path: {serviceFile}");

        var content = File.ReadAllText(serviceFile);
        Assert.DoesNotContain("new ActivitySource(", content);
    }
}
