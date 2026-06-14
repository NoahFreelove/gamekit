// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Reflection;
using GameKit.Core.Builder;
using GameKit.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Core.Tests.Telemetry;

/// <summary>
/// Validates that <see cref="GameKitTelemetry"/> is the single source of truth for all
/// GameKit ActivitySource/Meter names and D-04 attribute key constants (criterion #4), and
/// verifies <see cref="GameKitObservabilityBuilderExtensions.AddGameKitObservability"/> can be
/// called without throwing (smoke test for Task 2).
/// </summary>
public class GameKitTelemetryConstantsTests
{
    // ── Version ──────────────────────────────────────────────────────────────

    [Fact]
    public void Version_Is_1_0_0()
    {
        Assert.Equal("1.0.0", GameKitTelemetry.Version);
    }

    // ── Source names ──────────────────────────────────────────────────────────

    [Fact]
    public void MatchmakingTickerSourceName_Equals_GameKit_Matchmaking_Ticker()
    {
        Assert.Equal("GameKit.Matchmaking.Ticker", GameKitTelemetry.MatchmakingTickerSourceName);
    }

    [Fact]
    public void RankingsTickerSourceName_Equals_GameKit_Rankings_Ticker()
    {
        Assert.Equal("GameKit.Rankings.Ticker", GameKitTelemetry.RankingsTickerSourceName);
    }

    // ── Meter names ───────────────────────────────────────────────────────────

    [Fact]
    public void MatchmakingMeterName_Equals_GameKit_Matchmaking()
    {
        Assert.Equal("GameKit.Matchmaking", GameKitTelemetry.MatchmakingMeterName);
    }

    // ── D-04 attribute key constants ──────────────────────────────────────────

    [Fact]
    public void AttrLadderId_Equals_ladder_id()
    {
        Assert.Equal("ladder.id", GameKitTelemetry.AttrLadderId);
    }

    [Fact]
    public void AttrPoolName_Equals_pool_name()
    {
        Assert.Equal("pool.name", GameKitTelemetry.AttrPoolName);
    }

    [Fact]
    public void AttrLadderName_Equals_ladder_name()
    {
        Assert.Equal("ladder.name", GameKitTelemetry.AttrLadderName);
    }

    [Fact]
    public void AttrRegion_Equals_region()
    {
        Assert.Equal("region", GameKitTelemetry.AttrRegion);
    }

    [Fact]
    public void AttrStatus_Equals_status()
    {
        Assert.Equal("status", GameKitTelemetry.AttrStatus);
    }

    [Fact]
    public void AttrResult_Equals_result()
    {
        Assert.Equal("result", GameKitTelemetry.AttrResult);
    }

    [Fact]
    public void AttrErrorType_Equals_error_type()
    {
        Assert.Equal("error.type", GameKitTelemetry.AttrErrorType);
    }

    // ── Single-source-of-truth reflection enforcement ─────────────────────────
    //
    // These tests verify that per-package Telemetry class constants have the SAME
    // VALUES as GameKitTelemetry (criterion #4, D-02). They use Assembly.LoadFrom
    // to avoid adding a compile-time ProjectReference to GameKit.Matchmaking, which
    // carries a pre-existing NU1903 (MessagePack 2.5.187) build failure.
    //
    // The assembly is loaded from the standard GameKit.Matchmaking build output path.
    // If the assembly is not found, a descriptive message explains how to pre-build it.

    private static Assembly LoadMatchmakingAssembly()
    {
        var testAsmLocation = typeof(GameKitTelemetryConstantsTests).Assembly.Location;
        var testAsmDir = Path.GetDirectoryName(testAsmLocation)!;

        // Probe 1: same output directory
        var p1 = Path.Combine(testAsmDir, "GameKit.Matchmaking.dll");

        // Probe 2: sibling project output — navigate from tests/…/bin/Debug/net10.0
        var netDir = testAsmDir;
        var configDir = Path.GetDirectoryName(netDir)!;
        var binDir = Path.GetDirectoryName(configDir)!;
        var projDir = Path.GetDirectoryName(binDir)!;
        var testsDir = Path.GetDirectoryName(projDir)!;
        var repoRoot = Path.GetDirectoryName(testsDir)!;

        // Walk up from worktree root to find directory containing src/GameKit.Matchmaking
        var root = repoRoot;
        for (var i = 0; i < 5; i++)
        {
            if (Directory.Exists(Path.Combine(root, "src", "GameKit.Matchmaking")))
                break;
            var parent = Path.GetDirectoryName(root);
            if (parent is null) break;
            root = parent;
        }

        var config = Path.GetFileName(configDir);
        var p2 = Path.Combine(root, "src", "GameKit.Matchmaking", "bin", config, "net10.0", "GameKit.Matchmaking.dll");
        var p3 = Path.Combine(root, "src", "GameKit.Matchmaking", "bin", "Debug", "net10.0", "GameKit.Matchmaking.dll");

        foreach (var candidate in new[] { p1, p2, p3 })
        {
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }

        throw new FileNotFoundException(
            $"GameKit.Matchmaking.dll not found. Probed: '{p1}', '{p2}', '{p3}'. " +
            $"Run 'dotnet build {Path.Combine(root, "src", "GameKit.Matchmaking")} /p:TreatWarningsAsErrors=false' " +
            "before running this test.");
    }

    [Fact]
    public void MatchmakingActivitySource_SourceName_Equals_GameKitTelemetry_MatchmakingTickerSourceName()
    {
        // Reflection-based single-source-of-truth check (criterion #4, D-02).
        // Verifies at runtime that MatchmakingActivitySource.SourceName VALUE equals
        // the corresponding GameKitTelemetry constant. Catches drift even if the per-package
        // const is not initialized via GameKitTelemetry.
        var asm = LoadMatchmakingAssembly();
        var type = asm.GetType("GameKit.Matchmaking.Telemetry.MatchmakingActivitySource");
        Assert.NotNull(type);

        var sourceNameField = type!.GetField("SourceName", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sourceNameField);

        var actualValue = (string?)sourceNameField!.GetValue(null);
        Assert.Equal(GameKitTelemetry.MatchmakingTickerSourceName, actualValue);
    }

    [Fact]
    public void MatchmakingMeter_MeterName_Equals_GameKitTelemetry_MatchmakingMeterName()
    {
        // Reflection-based single-source-of-truth check (criterion #4, D-02).
        var asm = LoadMatchmakingAssembly();
        var type = asm.GetType("GameKit.Matchmaking.Telemetry.MatchmakingMeter");
        Assert.NotNull(type);

        var meterNameField = type!.GetField("MeterName", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(meterNameField);

        var actualValue = (string?)meterNameField!.GetValue(null);
        Assert.Equal(GameKitTelemetry.MatchmakingMeterName, actualValue);
    }

    // ── AddGameKitObservability smoke test ────────────────────────────────────

    [Fact]
    public void AddGameKitObservability_DoesNotThrow_WithDefaultOptions()
    {
        // Smoke test: AddGameKit(...).AddGameKitObservability() must not throw.
        // Verifies the method is callable on IGameKitBuilder and registers OTel
        // sources/meters without error (criterion #2 + OBS-01).
        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Record.Exception(() =>
        {
            services.AddGameKit(opts =>
                opts.ConnectionString = "Host=localhost;Database=test;Username=u;Password=p")
                .AddGameKitObservability();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AddGameKitObservability_WithOtlpEndpoint_DoesNotThrow()
    {
        // Smoke test: AddGameKitObservability with OtlpEndpoint configured must not throw.
        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Record.Exception(() =>
        {
            services.AddGameKit(opts =>
                opts.ConnectionString = "Host=localhost;Database=test;Username=u;Password=p")
                .AddGameKitObservability(otel =>
                {
                    otel.OtlpEndpoint = "http://localhost:4317";
                });
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AddGameKitObservability_ReturnsIGameKitBuilder()
    {
        // Verifies the method returns IGameKitBuilder (fluent chaining — acceptance criterion #1).
        var services = new ServiceCollection();
        services.AddLogging();

        IGameKitBuilder? result = null;
        var exception = Record.Exception(() =>
        {
            var builder = services.AddGameKit(opts =>
                opts.ConnectionString = "Host=localhost;Database=test;Username=u;Password=p");
            result = builder.AddGameKitObservability();
        });

        Assert.Null(exception);
        Assert.NotNull(result);
    }
}
