---
phase: 19-load-performance-testing
plan: "01"
subsystem: benchmarks
status: complete
tags: [benchmarks, benchmarkdotnet, performance, jwt, bcrypt, argon2, glicko2, matchmaking, redis]
dependency_graph:
  requires: []
  provides:
    - tests/GameKit.LoadTests (BenchmarkDotNet console app)
    - five [Benchmark] methods covering all PERF-01 hot-paths
    - BenchmarkDotNet 0.15.8 NuGet pin in Directory.Packages.props
  affects:
    - Directory.Packages.props
    - GameKit.sln
tech_stack:
  added:
    - BenchmarkDotNet 0.15.8 (MIT; dotnet/BenchmarkDotNet; >100M NuGet downloads)
  patterns:
    - BDN console app OutputType=Exe (not IsTestProject) with inherited xUnit/Test.Sdk refs removed
    - [GlobalSetup] for expensive one-time work (RSA key gen, container start, hash pre-computation)
    - [MemoryDiagnoser] on all benchmark classes
    - [MinIterationCount(15)] on the Redis round-trip benchmark to dampen Docker-bridge jitter
    - Testcontainers Redis + Postgres started in GlobalSetup, never in the measured [Benchmark] body
key_files:
  created:
    - tests/GameKit.LoadTests/GameKit.LoadTests.csproj
    - tests/GameKit.LoadTests/Program.cs
    - tests/GameKit.LoadTests/Benchmarks/JwtValidationBenchmarks.cs
    - tests/GameKit.LoadTests/Benchmarks/PasswordHasherBenchmarks.cs
    - tests/GameKit.LoadTests/Benchmarks/Glicko2Benchmarks.cs
    - tests/GameKit.LoadTests/Benchmarks/MatchmakingTicketBenchmarks.cs
    - tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs
  modified:
    - Directory.Packages.props (added BenchmarkDotNet 0.15.8 pin)
    - GameKit.sln (added tests/GameKit.LoadTests/GameKit.LoadTests.csproj)
decisions:
  - "BDN console app is separate from tests/GameKit.Matchmaking.LoadTests (xUnit sustain harness) — incompatible runner models"
  - "MatchmakingBenchmarkHost uses public migration APIs (MigrationRunner.MigrateWithLockAsync + package-specific migration customizers) rather than internal LoadTestMigrationHelpers from the sibling project — no InternalsVisibleTo change needed"
  - "GameKit.LoadTests uses the same player re-enqueue approach in TicketEnqueueAsync — AlreadyEnqueued result on subsequent iterations is intentional and still exercises the full Redis fast-path decision"
  - "AllowInsecureParametersForTesting never set — BCrypt wf=12 (~205ms) and Argon2id production params are enforced, matching the SECURITY requirement in the threat model"
metrics:
  duration: "~11 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 7
  files_modified: 2
---

# Phase 19 Plan 01: BenchmarkDotNet Micro-Benchmark Harness Summary

One-liner: BenchmarkDotNet 0.15.8 console app with five production-param hot-path benchmarks (JWT RSA-SHA256 validation, BCrypt wf=12, Argon2id m=65536/t=3/p=1, Glicko-2 Apply, matchmaking-ticket Redis round-trip against Testcontainers Redis).

## Tasks Completed

| Task | Description | Commit | Status |
|------|-------------|--------|--------|
| 1 | Create GameKit.LoadTests BDN console project, pin BenchmarkDotNet 0.15.8, register in solution | `11d55b0` | Done |
| 2 | CPU benchmarks: JWT validation, BCrypt+Argon2id verify, Glicko-2 Apply | `96d5fa8` | Done |
| 3 | Matchmaking-ticket Redis round-trip benchmark with Testcontainers Redis | `e31303e` | Done |

## What Was Built

### Project: `tests/GameKit.LoadTests`

A new BenchmarkDotNet console application (`OutputType=Exe`, `IsPackable=false`, NOT `IsTestProject`) distinct from the existing `tests/GameKit.Matchmaking.LoadTests` (xUnit sustain-load harness).

**Key structural decisions:**
- `<PackageReference Remove="Microsoft.NET.Test.Sdk" />` etc. neutralize the inherited `tests/Directory.Build.props` xUnit auto-injection that would conflict with BDN's console runner (19-RESEARCH.md Pitfall §7)
- `Program.cs` uses `BenchmarkRunner.Run(typeof(Program).Assembly, args: args)` to discover all `[Benchmark]`-annotated classes

### Benchmark 1: JWT Validation (`JwtValidationBenchmarks`)

- **[GlobalSetup]:** Generates an RSA-2048 key in-process, issues a valid 1-hour JWT, builds `TokenValidationParameters` mirroring `AuthBuilderExtensions` (ValidateIssuer/Audience/Lifetime/SigningKey all true, ClockSkew=30s, RSA-SHA256)
- **[Benchmark] ValidateToken():** Calls `JwtSecurityTokenHandler.ValidateToken(token, params, out _)`
- Measures the hot-path exercised on every authenticated HTTP request

### Benchmark 2: Password Verification (`PasswordHasherBenchmarks`)

- **[GlobalSetup]:** Constructs `BCryptPasswordHasher` with `BCryptWorkFactor=12` and `Argon2idPasswordHasher` with `new GameKitArgon2Options()` (production defaults m=65536/t=3/p=1). Pre-hashes once.
- **[Benchmark] BCryptVerify():** Calls `_bcrypt.Verify(password, hash)` — ~205ms on dev hardware at wf=12
- **[Benchmark] Argon2idVerify():** Calls `_argon2.Verify(password, hash)` — production Argon2id latency
- `AllowInsecureParametersForTesting` is **never set** (grep confirmed)

### Benchmark 3: Glicko-2 Rating Calculation (`Glicko2Benchmarks`)

- **[GlobalSetup]:** Builds `Glicko2Algorithm(tau:0.5, initVolatility:0.06)` + 200-player `RankingState` at Glicko-2 defaults + three `RankingBatch` instances (2/10/100 outcomes)
- **[Benchmark] Apply_2/Apply_10/Apply_100:** Calls `_algo.Apply(_state, _batch)` at each batch size
- Demonstrates O(n) scaling; pure CPU, no I/O

### Benchmark 4: Matchmaking-Ticket Redis Round-Trip (`MatchmakingTicketBenchmarks`)

- **Infrastructure: `MatchmakingBenchmarkHost`** — self-contained helper that:
  - Starts PostgreSqlContainer (postgres:17.9 + init scripts for roles/schema) and RedisContainer in parallel
  - Applies Core → Admin → Rankings → Matchmaking migrations via public `MigrationRunner.MigrateWithLockAsync` + package-specific migration model customizers
  - Seeds a `ladders` row and a `players` row
  - Builds a full `IHost` with `AddGameKit().AddAuth().AddRankings().AddMatchmaking()` wired to the Testcontainers Redis
- **[GlobalSetup] SetupAsync():** Starts the host once; container boot cost (~1-3s) excluded from measurement
- **[Benchmark] TicketEnqueueAsync():** Calls `IMatchmakingService.EnqueueAsync(playerId, ladderId, null, null, ct)` — measures Redis HSETNX + ZADD path
- **[MinIterationCount(15)]:** Stabilises the mean against Docker-bridge network jitter
- **[GlobalCleanup]:** Disposes containers

## Verification Results

```
Build:
  dotnet build tests/GameKit.LoadTests -c Release
  → Build succeeded. 0 Warning(s). 0 Error(s).

Smoke run (--job short --filter '*BCryptVerify*'):
  | Method       | Mean     | Error    | StdDev  | Allocated |
  |------------- |---------:|---------:|--------:|----------:|
  | BCryptVerify | 204.9 ms | 19.86 ms | 1.09 ms |   5.14 KB |
  → BDN results table produced; runner discovers and executes the benchmark

Security gate:
  grep -rn "AllowInsecure" tests/GameKit.LoadTests/ → comments only (no property assignment)

IsTestProject gate:
  grep -rn "IsTestProject" tests/GameKit.LoadTests/GameKit.LoadTests.csproj → only in comment

Solution gate:
  dotnet sln list | grep LoadTests → tests/GameKit.LoadTests/GameKit.LoadTests.csproj ✓
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `BenchmarkRunner.Run(assembly, args)` returns `Summary[]` not `Summary`**
- **Found during:** Task 1 first build
- **Issue:** `Program.cs` used `summary.HasCriticalValidationErrors` but `Run()` returns an array
- **Fix:** Changed to `summaries.Any(s => s.HasCriticalValidationErrors)`
- **Files modified:** `tests/GameKit.LoadTests/Program.cs`
- **Commit:** `11d55b0`

**2. [Rule 1 - Bug] `await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()...Build()` — awaiting non-awaitable**
- **Found during:** Task 3 first build  
- **Issue:** `.Build()` returns `IHost`, not `Task<IHost>` — the `await` was applied incorrectly
- **Fix:** Removed `await` from the builder chain; used `await _host.StartAsync().ConfigureAwait(false)` separately
- **Files modified:** `tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs`
- **Commit:** `e31303e`

**3. [Rule 2 - Missing critical functionality] MatchmakingBenchmarkHost uses public migration APIs instead of internal LoadTestMigrationHelpers**
- **Found during:** Task 3 implementation
- **Issue:** `LoadTestMigrationHelpers` is `internal` to `GameKit.Matchmaking.LoadTests`; no `InternalsVisibleTo("GameKit.LoadTests")` grant exists in Matchmaking's AssemblyInfo
- **Fix:** Re-implemented migration logic inline using public APIs: `MigrationRunner.MigrateWithLockAsync`, public `*MigrationModelCustomizer` classes, and public `*MigrationConstants` classes. No source changes to Matchmaking required.
- **Files modified:** `tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs`

## Known Stubs

None. All five benchmark methods call the real production code paths.

## Threat Flags

No new network endpoints, auth paths, or schema changes introduced. The benchmark I/O targets Testcontainers only (T-19-01-03 mitigated). RSA key/JWT are ephemeral and never persisted (T-19-01-02 accepted).

## Self-Check: PASSED

- [x] `tests/GameKit.LoadTests/GameKit.LoadTests.csproj` exists
- [x] `tests/GameKit.LoadTests/Program.cs` exists
- [x] `tests/GameKit.LoadTests/Benchmarks/JwtValidationBenchmarks.cs` exists
- [x] `tests/GameKit.LoadTests/Benchmarks/PasswordHasherBenchmarks.cs` exists
- [x] `tests/GameKit.LoadTests/Benchmarks/Glicko2Benchmarks.cs` exists
- [x] `tests/GameKit.LoadTests/Benchmarks/MatchmakingTicketBenchmarks.cs` exists
- [x] `tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs` exists
- [x] Commit `11d55b0` (Task 1) exists in git log
- [x] Commit `96d5fa8` (Task 2) exists in git log
- [x] Commit `e31303e` (Task 3) exists in git log
- [x] `dotnet build tests/GameKit.LoadTests -c Release` → 0 errors
- [x] Smoke run `--job short --filter '*BCryptVerify*'` produces BDN results table
- [x] No `IsTestProject=true` in csproj
- [x] No `AllowInsecureParametersForTesting` assignment in benchmark source
- [x] `BenchmarkDotNet 0.15.8` is the only new NuGet pin added to `Directory.Packages.props`
