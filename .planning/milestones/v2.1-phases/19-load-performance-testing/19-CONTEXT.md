# Phase 19: Load / Performance Testing - Context

**Gathered:** 2026-06-23
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Repeatable, hardware-annotated benchmarks establish documented performance baselines for every hot path; a CI regression gate fails if any benchmark regresses more than 20%; k6 load scenarios validate multi-replica correctness under realistic Redis RTT.

**Requirements:** PERF-01..PERF-06
**Depends on:** Phase 16, Phase 18 (load tests run against the final audited hardened codebase; Phase 16 split-brain gate is a prerequisite)
**UI hint:** no — benchmarks + load scripts + docs phase. Plan with `--skip-ui`.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices at Claude's discretion (discuss skipped).

### Requirements (authoritative text)
- **PERF-01** BenchmarkDotNet (MIT) micro-benchmarks for hot paths: JWT validation, BCrypt + Argon2id verify, Glicko-2 rating calculation, matchmaking-ticket Redis round-trip.
- **PERF-02** Committed baselines (`benchmarks/BASELINES.md`): machine spec + .NET version + result per benchmark.
- **PERF-03** k6 (AGPLv3 CLI; NO library dependency) load scenarios: matchmaking burst (500 players queue simultaneously, assert p99 match time) + auth throughput. Runnable against a LOCAL Testcontainers stack; NEVER run in CI against production.
- **PERF-04** k6 Lobby SignalR fan-out (N clients, one broadcast, delivery-time distribution) exercising the real Redis backplane. A short spike confirms k6 WebSocket sufficiency for SignalR BEFORE committing the scenario.
- **PERF-05** `docs/performance-tuning.md`: BCrypt/Argon2 cost-factor vs latency table, Npgsql connection-pool sizing, top-5 hot-query notes.
- **PERF-06** CI benchmark regression gate — build fails if any hot-path benchmark regresses >20% from the committed baseline.

</decisions>

<code_context>
## Existing Code Insights

- **`tests/GameKit.Matchmaking.LoadTests` already exists** — reconcile with PERF-01's `tests/GameKit.LoadTests`: the planner decides whether to (a) add a new `tests/GameKit.LoadTests` BenchmarkDotNet project for the micro-benchmarks and keep Matchmaking.LoadTests for k6/load harness, or (b) extend the existing one. Be explicit.
- **Hot-path seams:** `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` (+ `IPasswordHasher`); `src/GameKit.Auth.Argon2/` (Argon2id hasher); `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` (+ `RankingBatch.cs`); the matchmaking-ticket Redis round-trip (enqueue→ticker restore from Phase 15/16). JWT validation lives in GameKit.Auth (TokenValidationParameters / JwtIssuer).
- **k6 is not installed** as a binary → run via the **`grafana/k6` Docker image** (Docker available) so the scenarios are reproducible offline with zero external services/cloud credentials. Document the `docker run --rm -i grafana/k6 run -` invocation.
- **Existing Testcontainers fixtures** (Postgres + Redis, the Phase 16 `MatchmakingTestApp`, Lobby `BackplaneTests`) are the local stack the k6 scenarios target.

</code_context>

<specifics>
## Specific Ideas

- **PERF-06 (the hard one):** BenchmarkDotNet has NO built-in >20% regression gate. Approach: run benchmarks with `--exporters json` (BenchmarkDotNet JSON exporter), then a comparison script/tool diffs the new mean against `benchmarks/BASELINES.md` (or a committed `*-report.json` baseline) and exits non-zero if any benchmark's mean regresses >20%. Research the cleanest mechanism (custom script, `BenchmarkDotNet.Tool`, or a GitHub-action like `benchmark-action/github-action-benchmark`, GPL-compatible). Benchmarks are HARDWARE-DEPENDENT — the gate must be robust to CI-runner noise (consistent runner, multiple iterations, and the generous 20% threshold absorb jitter). Document the machine class the baseline was measured on.
- **Licensing:** BenchmarkDotNet is MIT (fine to depend on). k6 is AGPLv3 — used ONLY as an external CLI/Docker process, never linked as a library, never shipped inside a GameKit package (preserves GPL/self-host posture). State this in the perf docs.
- Build/test clean WITHOUT `-p:NuGetAudit=false` (Phase 18 resolved the MessagePack CVE + turned the audit gate on). Do not re-add the flag.

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
