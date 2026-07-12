// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using BenchmarkDotNet.Running;

// Run all [Benchmark]-annotated methods in this assembly.
// BenchmarkDotNet discovers classes annotated with [MemoryDiagnoser] or containing [Benchmark]
// methods via reflection over typeof(Program).Assembly.
//
// Usage (always Release):
//   dotnet run --project tests/GameKit.LoadTests -c Release -- [BDN args]
//
// Quick validation:
//   dotnet run --project tests/GameKit.LoadTests -c Release -- --job short --filter '*BCryptVerify*'
//
// Full baseline capture (slow — ~10 min, see 19-RESEARCH.md §Baseline Capture):
//   dotnet run --project tests/GameKit.LoadTests -c Release -- --filter '*' --exporters json --artifacts BenchmarkRun

var summaries = BenchmarkRunner.Run(typeof(Program).Assembly, args: args);
return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
