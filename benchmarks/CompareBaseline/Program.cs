// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// BenchmarkDotNet regression gate (PERF-06).
// Reads a new BDN -report-full.json (or a merged combined report) and a committed baseline JSON,
// computes (newMean - baseMean) / baseMean per matched benchmark method, and exits 1 if any
// method regresses beyond the threshold.
//
// Usage:
//   dotnet run --project benchmarks/CompareBaseline -c Release -- <new-report.json> <baseline.json>
//
// Exit codes: 0 = no regression; 1 = regression detected (fail-closed).

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
        bool IsWarning,        // new method has no baseline entry (added, not yet baselined)
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
        /// computes per-method regression deltas.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Fail-closed policy (CR-01 / CR-02):
        /// <list type="bullet">
        ///   <item>If the new report contains zero benchmarks while the baseline has some, the
        ///     gate FAILS (<c>HasRegression=true</c>) — this indicates a crashed or empty BDN
        ///     run, not a legitimate benchmark removal.</item>
        ///   <item>If a baseline method is absent from the new report (but the report has other
        ///     methods), the gate FAILS — a missing benchmark means it crashed or was removed
        ///     without updating the baseline.  To legitimately remove a benchmark, regenerate
        ///     the baseline (which also drops the method from it) so the gate will not see it
        ///     as missing on the next run.</item>
        ///   <item>New methods absent from the baseline produce a WARNING (not a failure) so
        ///     newly-added benchmarks do not block the gate until they are baselined.</item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="newReportJson">Full text of the new BDN <c>-report-full.json</c>
        /// (may be a merged combined report produced by the CI step).</param>
        /// <param name="baselineJson">Full text of the committed baseline JSON.</param>
        /// <returns>A <see cref="CompareResult"/> with <c>HasRegression=true</c> when any method
        /// exceeds <see cref="Threshold.Regression"/>, when a baseline method is absent from the
        /// new report, or when the new report contains zero benchmarks.</returns>
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

            // CR-02: an empty new report while the baseline has methods is a hard failure.
            // A crashed BDN run emits {"Benchmarks":[]} — silently treating that as "pass"
            // defeats the entire gate.  The operator must investigate and re-run.
            if (newMap.Count == 0 && baselineMap.Count > 0)
            {
                return new CompareResult(
                    HasRegression: true,
                    Results: [new MethodResult(
                        "<all-benchmarks>",
                        double.NaN, double.NaN, double.NaN,
                        IsRegression: true,
                        IsWarning:    false,
                        $"ERROR: new report contains 0 benchmarks but baseline has " +
                        $"{baselineMap.Count}. Benchmark run likely crashed or all methods " +
                        "were removed. Re-run benchmarks or regenerate the baseline.")]);
            }

            var results     = new List<MethodResult>();
            bool hasRegression = false;

            // Compare methods that exist in both reports
            foreach (var (method, baseMean) in baselineMap)
            {
                if (!newMap.TryGetValue(method, out var newMean))
                {
                    // CR-01 (fail-closed): a baseline method absent from the new report is a
                    // FAILURE, not a warning.  A missing method means the benchmark crashed or
                    // was removed without updating the baseline.  To legitimately remove a
                    // benchmark, regenerate the baseline (which also drops the method from it).
                    hasRegression = true;
                    results.Add(new MethodResult(
                        method,
                        baseMean,
                        double.NaN,
                        double.NaN,
                        IsRegression: true,
                        IsWarning:    false,
                        $"ERROR: baseline method '{method}' is missing from the new report. " +
                        "Benchmark may have crashed or been removed without updating the baseline. " +
                        "Re-run benchmarks or regenerate the baseline."));
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

            // Warn about new methods absent from the baseline — these are not failures because
            // newly-added benchmarks should not block the gate until they are baselined.
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
                    $"\nBenchmark regression gate FAILED — one or more benchmarks exceeded the {Threshold.Regression:P0} threshold or had missing methods.");
                return 1;
            }

            Console.WriteLine("\nBenchmark regression gate PASSED — all benchmarks within threshold.");
            return 0;
        }
    }
}
