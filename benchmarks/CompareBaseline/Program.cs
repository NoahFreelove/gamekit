// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// BenchmarkDotNet regression gate (PERF-06).
// Reads a new BDN -report-full.json and a committed baseline JSON, computes
// (newMean - baseMean) / baseMean per matched benchmark method, and exits 1
// if any method regresses beyond the threshold.
//
// Usage:
//   dotnet run --project benchmarks/CompareBaseline -c Release -- <new-report.json> <baseline.json>
//
// Exit codes: 0 = no regression; 1 = regression detected.

using System.Text.Json;

namespace CompareBaseline
{
    /// <summary>Regression threshold: flag any benchmark that slows by more than 20 %.</summary>
    public static class Threshold
    {
        /// <summary>20 % — intentionally generous to absorb CI runner-to-runner noise (±5-15 %).</summary>
        public const double Regression = 0.20;
    }

    /// <summary>Per-method comparison result.</summary>
    public sealed record MethodResult(
        string Method,
        double BaselineMeanNs,
        double NewMeanNs,
        double Delta,          // (new - base) / base
        bool IsRegression,
        bool IsWarning,        // missing from one of the reports
        string Message);

    /// <summary>Aggregate comparison result returned by <see cref="Comparator.CompareReports"/>.</summary>
    public sealed record CompareResult(
        bool HasRegression,
        IReadOnlyList<MethodResult> Results);

    /// <summary>
    /// Core comparison logic — extracted as a <c>static</c> method so the
    /// <c>CompareBaseline.Tests</c> project can call it directly without spawning a process.
    /// </summary>
    public static class Comparator
    {
        /// <summary>
        /// Parses <paramref name="newReportJson"/> and <paramref name="baselineJson"/> and
        /// computes per-method regression deltas.  Methods present in one report but absent
        /// from the other produce warning entries (not failures).
        /// </summary>
        /// <param name="newReportJson">Full text of the new BDN <c>-report-full.json</c>.</param>
        /// <param name="baselineJson">Full text of the committed baseline JSON.</param>
        /// <returns>A <see cref="CompareResult"/> with <c>HasRegression=true</c> when any method
        /// exceeds <see cref="Threshold.Regression"/>.</returns>
        public static CompareResult CompareReports(string newReportJson, string baselineJson)
        {
            var newDoc      = JsonDocument.Parse(newReportJson);
            var baselineDoc = JsonDocument.Parse(baselineJson);

            // Build baseline lookup: method name -> mean (nanoseconds)
            var baselineMap = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var bm in baselineDoc.RootElement.GetProperty("Benchmarks").EnumerateArray())
            {
                var method = bm.GetProperty("Method").GetString()!;
                var mean   = bm.GetProperty("Statistics").GetProperty("Mean").GetDouble();
                baselineMap[method] = mean;
            }

            // Build new-report lookup
            var newMap = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var bm in newDoc.RootElement.GetProperty("Benchmarks").EnumerateArray())
            {
                var method = bm.GetProperty("Method").GetString()!;
                var mean   = bm.GetProperty("Statistics").GetProperty("Mean").GetDouble();
                newMap[method] = mean;
            }

            var results     = new List<MethodResult>();
            bool hasRegression = false;

            // Compare methods that exist in both reports
            foreach (var (method, baseMean) in baselineMap)
            {
                if (!newMap.TryGetValue(method, out var newMean))
                {
                    // Baseline method missing from new report — warn, do not fail
                    results.Add(new MethodResult(
                        method,
                        baseMean,
                        double.NaN,
                        double.NaN,
                        IsRegression: false,
                        IsWarning:    true,
                        $"WARNING: baseline method '{method}' is missing from the new report"));
                    continue;
                }

                double delta = (newMean - baseMean) / baseMean;
                bool regressed = delta > Threshold.Regression;
                if (regressed) hasRegression = true;

                string sign = delta >= 0 ? "+" : string.Empty;
                results.Add(new MethodResult(
                    method,
                    baseMean,
                    newMean,
                    delta,
                    IsRegression: regressed,
                    IsWarning:    false,
                    regressed
                        ? $"REGRESSION: {method}: {newMean / 1e6:F3} ms vs baseline {baseMean / 1e6:F3} ms" +
                          $" ({sign}{delta:P1}, threshold {Threshold.Regression:P0})"
                        : $"  OK: {method}: {newMean / 1e6:F3} ms vs baseline {baseMean / 1e6:F3} ms ({sign}{delta:P1})"));
            }

            // Warn about new methods absent from the baseline
            foreach (var method in newMap.Keys)
            {
                if (!baselineMap.ContainsKey(method))
                {
                    results.Add(new MethodResult(
                        method,
                        double.NaN,
                        newMap[method],
                        double.NaN,
                        IsRegression: false,
                        IsWarning:    true,
                        $"WARNING: new method '{method}' has no baseline entry — add it to the baseline after review"));
                }
            }

            return new CompareResult(hasRegression, results);
        }
    }

    /// <summary>Entry point.</summary>
    public static class Program
    {
        /// <summary>
        /// Reads two file-path arguments, compares them, prints results, and returns
        /// 0 (no regression) or 1 (regression detected).
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: CompareBaseline <new-report.json> <baseline.json>");
                return 2;
            }

            string newPath      = args[0];
            string baselinePath = args[1];

            if (!File.Exists(newPath))
            {
                Console.Error.WriteLine($"ERROR: new report not found: {newPath}");
                return 2;
            }

            if (!File.Exists(baselinePath))
            {
                Console.Error.WriteLine($"ERROR: baseline not found: {baselinePath}");
                return 2;
            }

            var result = Comparator.CompareReports(
                File.ReadAllText(newPath),
                File.ReadAllText(baselinePath));

            foreach (var r in result.Results)
            {
                if (r.IsRegression)
                    Console.Error.WriteLine(r.Message);
                else
                    Console.WriteLine(r.Message);
            }

            if (result.HasRegression)
            {
                Console.Error.WriteLine(
                    $"\nBenchmark regression gate FAILED — one or more benchmarks exceeded the {Threshold.Regression:P0} threshold.");
                return 1;
            }

            Console.WriteLine("\nBenchmark regression gate PASSED — all benchmarks within threshold.");
            return 0;
        }
    }
}
