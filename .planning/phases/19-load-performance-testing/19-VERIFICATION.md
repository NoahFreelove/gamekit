---
phase: 19-load-performance-testing
verified: 2026-06-23T00:00:00Z
status: passed
score: 5/5 must-haves verified
behavior_unverified: 0
overrides_applied: 0
---

# Phase 19: Load / Performance Testing — Verification Report

**Phase Goal:** Hardware-annotated benchmarks + a CI regression gate (>20% fails) + k6 load scenarios reproducible offline.
**Verified:** 2026-06-23
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | `tests/GameKit.LoadTests` BDN micro-benchmarks cover JWT validation, BCrypt+Argon2id verify, Glicko-2 Apply, and matchmaking-ticket Redis round-trip; results in `benchmarks/BASELINES.md` with machine spec + .NET version | VERIFIED | Build succeeds (0 errors); 7 `[Benchmark]` methods confirmed across 4 classes; `benchmarks/baselines/report-baseline.json` has all 7 methods with real `Statistics.Mean` values |
| 2 | CI benchmark regression gate fails if any hot-path benchmark regresses >20% from committed baseline | VERIFIED | `dotnet test benchmarks/CompareBaseline.Tests` passes 7/7 tests; gate proved to exit 1 on +30% injected regression (manual confirm: exit code 1); gate wired in `.github/workflows/ci.yml` push-to-main-only job |
| 3 | k6 matchmaking burst (500 players queue vs local Testcontainers stack) produces measured p99; committed + reproducible without external services | VERIFIED | `tests/k6/matchmaking-burst.js` exists with 500-VU burst scenario, p(99) thresholds, match-formation polling via GET /api/mm/queue/{ticketId}/status; documented run showed p(99)=36.71ms; k6 is Docker-CLI external only (no PackageReference) |
| 4 | k6 Lobby SignalR fan-out (N clients, one broadcast) produces delivery-time distribution; spike confirmed stock k6 WebSocket sufficiency before committing | VERIFIED | Plan 02 spike confirmed GO (all 3 k6 checks passed against live Lobby hub); `tests/k6/lobby-signalr-fanout.js` uses stable `k6/websockets` via `helpers/signalr.js`; Trend metric `signalr_delivery_time_ms` documented; run showed p(95)=15.39ms with 50/50 iterations complete |
| 5 | `docs/performance-tuning.md` covers BCrypt/Argon2 cost vs latency, Npgsql pool sizing, top-5 hot-query notes | VERIFIED | File is 347 lines; BCrypt cost-factor table + Argon2id table both present, cross-referencing BASELINES.md; MaxPoolSize=25 recommendation + sizing formula + `npgsql.pool.available_idle_connections` monitoring note; 5 hot queries with index recommendations; AGPLv3/MIT tool-licensing section included |

**Score:** 5/5 truths verified (0 present, behavior-unverified)

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `tests/GameKit.LoadTests/GameKit.LoadTests.csproj` | BDN console app (OutputType=Exe, not test project) | VERIFIED | `OutputType=Exe` confirmed; `IsTestProject` not set; xUnit/Test.Sdk references removed |
| `tests/GameKit.LoadTests/Program.cs` | `BenchmarkRunner.Run(assembly, args)` entry point | VERIFIED | Uses `BenchmarkRunner.Run(typeof(Program).Assembly, args: args)` |
| `tests/GameKit.LoadTests/Benchmarks/JwtValidationBenchmarks.cs` | RSA-SHA256 JWT validation benchmark | VERIFIED | `ValidateToken()` benchmark; RSA-2048 key+JWT generated in `[GlobalSetup]` |
| `tests/GameKit.LoadTests/Benchmarks/PasswordHasherBenchmarks.cs` | BCrypt wf=12 + Argon2id production params | VERIFIED | `BCryptVerify()` and `Argon2idVerify()`; `BCryptWorkFactor=12` explicit; `new GameKitArgon2Options()` defaults; `AllowInsecureParametersForTesting` never assigned |
| `tests/GameKit.LoadTests/Benchmarks/Glicko2Benchmarks.cs` | Glicko-2 Apply at batch sizes 2/10/100 | VERIFIED | `Apply_2()`, `Apply_10()`, `Apply_100()` present; 200-player `RankingState` in `[GlobalSetup]` |
| `tests/GameKit.LoadTests/Benchmarks/MatchmakingTicketBenchmarks.cs` | Redis round-trip via Testcontainers | VERIFIED | `TicketEnqueueAsync()` benchmark; `[GlobalSetup]` starts containers; `[MinIterationCount(15)]` applied |
| `benchmarks/baselines/report-baseline.json` | Real BDN report with `Statistics.Mean` for all 7 benchmarks | VERIFIED | 7 benchmarks with real nanosecond means (ValidateToken=18020 ns, BCryptVerify=202468939 ns, Argon2idVerify=237691726 ns, Apply_2=10093 ns, Apply_10=21872 ns, Apply_100=191501 ns, TicketEnqueueAsync=2959642 ns) |
| `benchmarks/BASELINES.md` | Machine spec + .NET version + per-benchmark means | VERIFIED | 142 lines; CPU (i7-11700K), RAM (30 GiB), OS (Ubuntu 26.04), .NET 10.0.109, BDN 0.15.8, IterationCount=15/WarmupCount=5; all 7 means transcribed; 20% noise-tolerance documented |
| `benchmarks/CompareBaseline/Program.cs` | Regression gate tool; exits 1 on >20% | VERIFIED | `Threshold.Regression = 0.20`; zero NuGet PackageReferences; `Comparator.CompareReports()` static testable method |
| `benchmarks/CompareBaseline.Tests/CompareBaselineTests.cs` | Self-test proving exit 1 on >20% regression | VERIFIED | 7 tests; `Main_RegressedFixture_Returns1` is the gate proof; all 7 pass in `dotnet test` |
| `tests/k6/helpers/signalr.js` | Reusable SignalR negotiate+handshake helpers | VERIFIED | Uses `k6/websockets` (stable); `negotiateVersion=1` query param present; RECORD_SEP (`\x1e`) used |
| `tests/k6/spike-signalr.js` | Standalone SignalR handshake GO/NO-GO spike | VERIFIED | Six-step protocol; `/hubs/lobby`; `JoinLobbyAsync`; all tokens from `__ENV` |
| `tests/k6/matchmaking-burst.js` | 500-VU enqueue burst + auth throughput + match polling, p99 thresholds | VERIFIED | 3 scenarios (burst, auth_throughput, match_poll); POST `/api/mm/queue`; GET `/api/mm/queue/{ticketId}/status`; `p(99)<2000`; `match_formation_time_ms` Trend; all credentials via `__ENV` |
| `tests/k6/lobby-signalr-fanout.js` | N-client fan-out delivery distribution | VERIFIED | Imports `./helpers/signalr.js`; `signalr_delivery_time_ms` Trend; `k6/websockets` only (no experimental); all tokens via `__ENV` |
| `tests/k6/README.md` | Docker invocation + AGPLv3 posture + host.docker.internal | VERIFIED | All three required elements confirmed present |
| `docs/performance-tuning.md` | BCrypt/Argon2 tables, Npgsql pool sizing, top-5 hot queries | VERIFIED | 347 lines; all required sections present with index recommendations; BASELINES.md cross-reference; k6 AGPLv3 + BDN MIT licensing posture noted |
| `.github/workflows/ci.yml` | Benchmarks job (push-to-main only) with CompareBaseline gate | VERIFIED | `if: github.event_name == 'push' && github.ref == 'refs/heads/main'`; `ubuntu-24.04`; runs LoadTests with `--exporters json`; pipes to `CompareBaseline`; graceful baseline-missing guard; no `-p:NuGetAudit=false` in any step |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `tests/GameKit.LoadTests/Program.cs` | Benchmark classes | `BenchmarkRunner.Run(typeof(Program).Assembly, args)` | WIRED | Assembly-scan discovers all `[Benchmark]` methods |
| `benchmarks/CompareBaseline.Tests/CompareBaselineTests.cs` | `benchmarks/CompareBaseline/Program.cs` | `Program.Main(new[]{newPath, basePath})` call; direct `Comparator.CompareReports()` call | WIRED | Test calls both the static method and the entry point; exit code asserted |
| `.github/workflows/ci.yml` benchmarks job | `benchmarks/CompareBaseline/Program.cs` | `dotnet run --project benchmarks/CompareBaseline -c Release -- "$REPORT" "$BASELINE"` | WIRED | Regression gate exit code propagates to CI job failure |
| `tests/k6/matchmaking-burst.js` | `/api/mm/queue` + `/api/mm/queue/{ticketId}/status` | k6 HTTP POST/GET against `${BASE_URL}` | WIRED | Route strings confirmed in scenario source |
| `tests/k6/lobby-signalr-fanout.js` | `tests/k6/helpers/signalr.js` | `import { negotiateSignalR, RECORD_SEP } from './helpers/signalr.js'` | WIRED | Import confirmed; `negotiateSignalR` called in VU function |
| `docs/performance-tuning.md` | `benchmarks/BASELINES.md` | cross-reference links + BCrypt/Argon2 mean values transcribed | WIRED | Text references `benchmarks/BASELINES.md` 4+ times; measured means cited in tables |

---

## Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| `tests/GameKit.LoadTests` builds in Release | `dotnet build tests/GameKit.LoadTests -c Release` | 0 Warning(s), 0 Error(s) | PASS |
| Regression gate exits 1 on >20% injected regression | `dotnet run --project benchmarks/CompareBaseline -c Release -- regressed-report.json baseline.json` | "REGRESSION: BCryptVerify: 130.000 ms vs baseline 100.000 ms (+30.0%, threshold 20%)" + "Exit code: 1" | PASS |
| Gate self-test suite passes 7/7 | `dotnet test benchmarks/CompareBaseline.Tests -c Release` | "Passed! — Failed: 0, Passed: 7" | PASS |
| Baseline JSON has real means for all 7 benchmarks | `python3 -c "import json; d=json.load(...)"` | 7 benchmarks with non-zero nanosecond means | PASS |
| BASELINES.md contains machine spec + .NET version + 20% notes | `grep -qi "CPU|SDK|20%|BCryptVerify" BASELINES.md` | All 4 greps confirmed | PASS |
| k6 scenarios use stable module only (no experimental) | `grep -rn "experimental/websockets|'k6/ws'" tests/k6/` | No matches | PASS |
| No hardcoded JWTs in k6 scripts | `grep -rniE "eyJ[A-Za-z0-9_-]{10}" tests/k6/` | No matches | PASS |
| k6 not referenced as NuGet dependency | `grep -rn "PackageReference.*k6" **/*.csproj` | No matches | PASS |
| CI benchmarks job gated push-to-main | `grep "github.event_name.*push.*refs/heads/main" .github/workflows/ci.yml` | Match on line 166 | PASS |
| No `-p:NuGetAudit=false` in any CI step | All CI `run:` steps | Only in comments, never as an actual argument | PASS |

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| PERF-01 | 19-01 | BenchmarkDotNet micro-benchmarks for JWT, BCrypt, Argon2id, Glicko-2, Redis round-trip | SATISFIED | 7 `[Benchmark]` methods across 4 classes; all at production params |
| PERF-02 | 19-04 | Committed hardware-annotated baselines in `benchmarks/BASELINES.md` + `report-baseline.json` | SATISFIED | Real BDN Release-mode capture (IterationCount=15, WarmupCount=5); machine spec documented |
| PERF-03 | 19-05 | k6 matchmaking burst scenario with p99 thresholds + match-formed polling | SATISFIED | `matchmaking-burst.js`; 500-VU ramp + auth throughput + status polling Trend; run confirmed p(99)=36.71ms |
| PERF-04 | 19-02 + 19-05 | Spike confirms stock k6 WebSocket sufficiency; fan-out scenario with delivery-time distribution | SATISFIED | Spike: GO (100% checks); fanout: 50/50 VUs, p(95)=15.39ms delivery; stable module confirmed |
| PERF-05 | 19-05 | `docs/performance-tuning.md` — BCrypt/Argon2 cost-vs-latency, Npgsql sizing, top-5 hot queries | SATISFIED | All sections present; BASELINES.md cross-referenced; sizing formula + monitoring note + index recommendations |
| PERF-06 | 19-03 | CI regression gate exits 1 on any benchmark mean >20% regression | SATISFIED | Gate proven by 7-test xUnit suite; manual proof: exit code 1 on +30% injected regression; CI job push-to-main-only |

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None found | — | — | — | — |

No `TBD`, `FIXME`, or `XXX` markers found in phase-modified files. No stubs, no hardcoded credentials, no empty implementations.

---

## Human Verification Required

None. All must-haves are verifiable programmatically via build, test, grep, and file content inspection. The k6 run outputs (burst p99, fanout delivery p95) are committed in the SUMMARY.md and corroborated by the SpikeHost GO/NO-GO checkpoint evidence.

---

## Summary

All five PERF success criteria are met:

1. **PERF-01/PERF-02 (Benchmarks + Baselines):** `tests/GameKit.LoadTests` builds cleanly in Release; 7 `[Benchmark]` methods cover all 5 hot-path categories at production parameters (BCrypt wf=12, Argon2id m=65536/t=3/p=1, RSA-2048 JWT, 200-player Glicko-2, Testcontainers Redis round-trip); `benchmarks/baselines/report-baseline.json` contains real nanosecond means for all 7 methods; `benchmarks/BASELINES.md` documents machine spec (i7-11700K, 30 GiB RAM, Ubuntu 26.04, .NET 10.0.109), BDN config, and per-benchmark means with noise notes.

2. **PERF-06 (CI Regression Gate):** `CompareBaseline` tool has zero NuGet dependencies, threshold constant `0.20`, and is proven by a 7-test xUnit suite — `Main_RegressedFixture_Returns1` and the matching `CompareReports_RegressedFixture_HasRegressionTrue` together constitute hard proof that the gate exits 1 on a +30% injected regression. The CI benchmarks job is correctly gated to push-to-main-only, runs on `ubuntu-24.04`, has no `NuGetAudit=false`, and includes a graceful baseline-missing guard.

3. **PERF-03 (k6 Matchmaking Burst):** `matchmaking-burst.js` implements a 500-VU ramping burst against `POST /api/mm/queue`, a 100-VU auth throughput scenario, and a match-formation polling scenario with `Trend` metric and p99 threshold. All credentials sourced from `__ENV`. k6 is external Docker CLI only — zero NuGet references confirmed.

4. **PERF-04 (k6 SignalR Fan-out):** The Plan 02 spike produced a documented GO decision (3/3 k6 checks passed against the live Lobby hub using stock `grafana/k6` v2.0.0). `lobby-signalr-fanout.js` reuses the Plan 02 `helpers/signalr.js` module, uses stable `k6/websockets` (no experimental import), connects N VUs to `/hubs/lobby`, broadcasts one `SendChatMessageAsync`, and records delivery time into `signalr_delivery_time_ms` Trend. AGPLv3 posture documented in both `tests/k6/README.md` and `docs/performance-tuning.md`.

5. **PERF-05 (Performance-Tuning Guide):** `docs/performance-tuning.md` (347 lines) covers BCrypt cost-factor table (wf 10–14), Argon2id cost-factor table (m/t/p combinations), both cross-referencing `benchmarks/BASELINES.md`; Npgsql MaxPoolSize=25 recommendation, sizing formula `ceil(peak_concurrent_requests × avg_connection_hold_ms / avg_request_ms) + safety_margin`, and `npgsql.pool.available_idle_connections` monitoring alert; 5 hot queries with partial-index SQL recommendations; BenchmarkDotNet MIT + k6 AGPLv3 external-CLI-only licensing section.

---

## VERIFICATION COMPLETE

**Status: PASSED**
**Score: 5/5 success criteria verified**

All phase goal components — hardware-annotated benchmarks, committed baselines, a proven CI regression gate (>20% threshold), and reproducible k6 load scenarios — exist in the codebase as substantive, wired artifacts.

---

_Verified: 2026-06-23_
_Verifier: Claude (gsd-verifier)_
