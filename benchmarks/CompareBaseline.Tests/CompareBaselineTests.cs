// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// Proving self-test for the PERF-06 regression gate (CompareBaseline).
//
// These tests PROVE the gate fires correctly — T-19-03-03 (repudiation threat):
//   a gate that is present but unproven provides false confidence.
//
// Test strategy:
//   * Call Comparator.CompareReports() directly (no process spawn) for result assertions.
//   * Call Program.Main(string[]) with fixture file paths for exit-code assertions.

using System.Reflection;
using CompareBaseline;
using Xunit;

namespace CompareBaseline.Tests;

public sealed class CompareBaselineTests
{
    // ---------------------------------------------------------------------------
    // Fixture paths — resolved relative to the test output directory so that
    // the <Content CopyToOutputDirectory="PreserveNewest"> items are always found.
    // ---------------------------------------------------------------------------
    private static string FixtureDir =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "fixtures");

    private static string BaselinePath            => Path.Combine(FixtureDir, "baseline.json");
    private static string WithinThresholdPath     => Path.Combine(FixtureDir, "within-threshold-report.json");
    private static string RegressedPath           => Path.Combine(FixtureDir, "regressed-report.json");

    // ---------------------------------------------------------------------------
    // Helper — read fixture file text
    // ---------------------------------------------------------------------------
    private static string ReadFixture(string path) => File.ReadAllText(path);

    // ---------------------------------------------------------------------------
    // Test 1: >20 % regression detected — HasRegression=true, exit code 1
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When BCryptVerify's mean increases from 100 ms to 130 ms (+30 %),
    /// <see cref="Comparator.CompareReports"/> must return HasRegression=true.
    /// </summary>
    [Fact]
    public void CompareReports_RegressedFixture_HasRegressionTrue()
    {
        var result = Comparator.CompareReports(
            ReadFixture(RegressedPath),
            ReadFixture(BaselinePath));

        Assert.True(result.HasRegression,
            "Expected HasRegression=true because BCryptVerify regressed by +30 %.");

        // Exactly one method should be flagged as a regression (BCryptVerify).
        var regressions = result.Results.Where(r => r.IsRegression).ToList();
        Assert.Single(regressions);
        Assert.Equal("BCryptVerify", regressions[0].Method);

        // Delta should be +0.30 within floating-point tolerance.
        Assert.True(regressions[0].Delta >= 0.29 && regressions[0].Delta <= 0.31,
            $"Expected delta ~0.30 but got {regressions[0].Delta:F4}");
    }

    /// <summary>
    /// <see cref="Program.Main"/> must return exit code 1 when any benchmark regresses.
    /// This is the gate proof: exit 1 on a >20 % injected regression.
    /// </summary>
    [Fact]
    public void Main_RegressedFixture_Returns1()
    {
        int exitCode = Program.Main(new[] { RegressedPath, BaselinePath });
        Assert.Equal(1, exitCode);
    }

    // ---------------------------------------------------------------------------
    // Test 2: within-threshold — HasRegression=false, exit code 0
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When all benchmark means are within +/-10 % of baseline,
    /// <see cref="Comparator.CompareReports"/> must return HasRegression=false.
    /// </summary>
    [Fact]
    public void CompareReports_WithinThreshold_HasRegressionFalse()
    {
        var result = Comparator.CompareReports(
            ReadFixture(WithinThresholdPath),
            ReadFixture(BaselinePath));

        Assert.False(result.HasRegression,
            "Expected HasRegression=false because all methods are within 10 % of baseline.");

        // No method should be flagged as a regression.
        Assert.DoesNotContain(result.Results, r => r.IsRegression);
    }

    /// <summary>
    /// <see cref="Program.Main"/> must return exit code 0 when all benchmarks are within threshold.
    /// </summary>
    [Fact]
    public void Main_WithinThreshold_Returns0()
    {
        int exitCode = Program.Main(new[] { WithinThresholdPath, BaselinePath });
        Assert.Equal(0, exitCode);
    }

    // ---------------------------------------------------------------------------
    // Test 3: missing/added methods produce warnings, not failures
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A baseline method absent from the new report must produce a warning entry
    /// (IsWarning=true) but must NOT cause HasRegression=true.
    /// </summary>
    [Fact]
    public void CompareReports_BaselineMethodMissingFromNew_WarnsDoesNotFail()
    {
        // New report with only ValidateToken — BCryptVerify and Glicko2Apply are gone.
        const string newReportMissingMethod = """
            {
              "Benchmarks": [
                {
                  "Method": "ValidateToken",
                  "Statistics": { "Mean": 5000.0 }
                }
              ]
            }
            """;

        var result = Comparator.CompareReports(newReportMissingMethod, ReadFixture(BaselinePath));

        Assert.False(result.HasRegression,
            "A missing baseline method must warn, not fail the gate.");

        var warnings = result.Results.Where(r => r.IsWarning).ToList();
        Assert.True(warnings.Count >= 1,
            "Expected at least one WARNING for each baseline method absent from the new report.");

        // Every warning message must contain the word "WARNING"
        Assert.All(warnings, w => Assert.Contains("WARNING", w.Message, StringComparison.Ordinal));
    }

    /// <summary>
    /// A new method absent from the baseline must produce a warning entry
    /// (IsWarning=true) but must NOT cause HasRegression=true.
    /// </summary>
    [Fact]
    public void CompareReports_NewMethodAbsentFromBaseline_WarnsDoesNotFail()
    {
        // New report adds "NewBenchmark" that has no baseline entry.
        const string newReportWithExtra = """
            {
              "Benchmarks": [
                {
                  "Method": "BCryptVerify",
                  "Statistics": { "Mean": 100000000.0 }
                },
                {
                  "Method": "ValidateToken",
                  "Statistics": { "Mean": 5000.0 }
                },
                {
                  "Method": "Glicko2Apply",
                  "Statistics": { "Mean": 250000.0 }
                },
                {
                  "Method": "NewBenchmark",
                  "Statistics": { "Mean": 999999.0 }
                }
              ]
            }
            """;

        var result = Comparator.CompareReports(newReportWithExtra, ReadFixture(BaselinePath));

        Assert.False(result.HasRegression,
            "A new method with no baseline entry must warn, not fail the gate.");

        var warnings = result.Results.Where(r => r.IsWarning).ToList();
        Assert.True(warnings.Count >= 1,
            "Expected at least one WARNING for the new method absent from the baseline.");

        var newMethodWarning = warnings.FirstOrDefault(w => w.Method == "NewBenchmark");
        Assert.NotNull(newMethodWarning);
        Assert.Contains("WARNING", newMethodWarning!.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // Test 4: Threshold constant is 0.20
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Confirms the regression threshold is exactly 0.20 (20 %) as required by PERF-06.
    /// </summary>
    [Fact]
    public void Threshold_RegressionConstant_Is0Point20()
    {
        Assert.Equal(0.20, Threshold.Regression, precision: 10);
    }
}
