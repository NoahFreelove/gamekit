// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.IO;
using System.Reflection;
using GameKit.Core.Telemetry;
using GameKit.Matchmaking.Telemetry;
using Xunit;

namespace GameKit.Matchmaking.Tests.Telemetry;

/// <summary>
/// Criterion #4 enforcement tests: all camelCase span tag keys in
/// <c>MatchmakingActivitySource.cs</c> and <c>MatchmakerTickerService.cs</c> have been
/// normalized to OTel-compliant lowercase-dotted; cross-cutting keys reference
/// <see cref="GameKitTelemetry"/> constants (Plan 13-03 / D-03).
/// </summary>
public sealed class MatchmakingTagNamingTests
{
    private static string ReadSourceFile(string relativePath)
    {
        // The test assembly lives at:
        //   <worktree>/tests/GameKit.Matchmaking.Tests/bin/Debug/net10.0/
        // Go up 5 levels to reach the worktree root.
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var dir = new DirectoryInfo(assemblyDir);
        for (var i = 0; i < 5; i++)
            dir = dir.Parent!;

        var path = Path.Combine(dir.FullName, relativePath);
        Assert.True(File.Exists(path), $"Source file not found: {path}");
        return File.ReadAllText(path);
    }

    private static string ActivitySourceFile =>
        ReadSourceFile(Path.Combine("src", "GameKit.Matchmaking", "Telemetry", "MatchmakingActivitySource.cs"));

    private static string TickerServiceFile =>
        ReadSourceFile(Path.Combine("src", "GameKit.Matchmaking", "Services", "MatchmakerTickerService.cs"));

    // ── Source-assert: no old camelCase tag keys remain as SetTag arguments ──
    //
    // The check looks for the SetTag call form — "SetTag(\"<key>\"" — to assert
    // that the old camelCase string is not passed as a span tag key argument.
    // This avoids false positives from Redis hash field reads (which use the same
    // identifiers as keys in the wire format and must NOT be renamed), comments,
    // XML doc attributes, or local variable names.

    /// <summary>
    /// Source-assert: neither <c>MatchmakingActivitySource.cs</c> nor
    /// <c>MatchmakerTickerService.cs</c> may pass an old camelCase string as a
    /// <c>SetTag</c> key argument. Fails until the D-03 rename is applied (RED gate).
    /// </summary>
    [Theory]
    [InlineData("SetTag(\"ladderId\"")]
    [InlineData("SetTag(\"poolName\"")]
    [InlineData("SetTag(\"candidatesEvaluated\"")]
    [InlineData("SetTag(\"matchesFormed\"")]
    [InlineData("SetTag(\"budgetBail\"")]
    [InlineData("SetTag(\"matchCapBail\"")]
    [InlineData("SetTag(\"phase.hashFanoutMs\"")]
    [InlineData("SetTag(\"phase.matchLoopMs\"")]
    [InlineData("SetTag(\"phase.totalMs\"")]
    public void MatchmakingSource_DoesNotContain_OldCamelCaseTagKey(string oldSetTagPattern)
    {
        var activitySource = ActivitySourceFile;
        var tickerService = TickerServiceFile;

        Assert.DoesNotContain(oldSetTagPattern, activitySource);
        Assert.DoesNotContain(oldSetTagPattern, tickerService);
    }

    // ── Source-assert: cross-cutting keys reference GameKitTelemetry constants ─

    /// <summary>
    /// Source-assert: <c>MatchmakingActivitySource.cs</c> references
    /// <see cref="GameKitTelemetry.AttrLadderId"/> (not the inline string <c>"ladderId"</c>).
    /// </summary>
    [Fact]
    public void MatchmakingActivitySource_References_GameKitTelemetry_AttrLadderId()
    {
        Assert.Contains("GameKitTelemetry.AttrLadderId", ActivitySourceFile);
    }

    /// <summary>
    /// Source-assert: <c>MatchmakingActivitySource.cs</c> references
    /// <see cref="GameKitTelemetry.AttrPoolName"/> (not the inline string <c>"poolName"</c>).
    /// </summary>
    [Fact]
    public void MatchmakingActivitySource_References_GameKitTelemetry_AttrPoolName()
    {
        Assert.Contains("GameKitTelemetry.AttrPoolName", ActivitySourceFile);
    }

    // ── Reflection test: Source version equals GameKitTelemetry.Version ──────

    /// <summary>
    /// <see cref="MatchmakingActivitySource.Source"/> version must equal
    /// <see cref="GameKitTelemetry.Version"/> (<c>"1.0.0"</c>). The version was previously
    /// hardcoded as a literal — this test enforces single-source-of-truth via the constant.
    /// </summary>
    [Fact]
    public void MatchmakingActivitySource_SourceVersion_EqualsGameKitTelemetry_Version()
    {
        Assert.Equal(GameKitTelemetry.Version, MatchmakingActivitySource.Source.Version);
    }
}
