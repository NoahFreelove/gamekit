---
phase: 19
slug: load-performance-testing
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-23
---

# Phase 19 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | BenchmarkDotNet 0.15.8 (MIT) console app + xUnit (gate self-test) + k6 via `grafana/k6` Docker image (AGPLv3, external CLI only) + Testcontainers |
| **Quick run command** | `dotnet build tests/GameKit.LoadTests` (compile-check; full benchmark run is slow) |
| **Benchmark run** | `dotnet run -c Release --project tests/GameKit.LoadTests -- --exporters json` (SLOW — warmup + iterations) |
| **Gate self-test** | `dotnet test benchmarks/CompareBaseline.Tests` (proves >20% regression → exit 1, within-threshold → exit 0) |
| **k6** | `docker run --rm -i grafana/k6 run - < scenario.js` against the local Testcontainers stack |
| **Estimated runtime** | benchmarks: minutes (statistical); k6 burst: ~1–3 min |

---

## Sampling Rate

- **After every task commit:** compile the affected project (`dotnet build`); run the gate self-test if the comparison tool changed
- **After every plan wave:** run the affected benchmark subset / k6 spike once
- **Before verification:** the comparison gate self-test is green; BASELINES.md committed; k6 scenarios run locally without error
- **Max feedback latency:** compile ~30s; full benchmark run is intentionally slow (out of the fast-feedback loop — the gate self-test is the fast proxy)

---

## Per-Task Verification Map

| Task | Requirement | Secure Behavior | Test Type | Automated Command | Status |
|------|-------------|-----------------|-----------|-------------------|--------|
| BDN micro-benchmarks | PERF-01 | benchmarks compile + run for JWT/BCrypt/Argon2id/Glicko-2/Redis round-trip | compile + run | `dotnet build tests/GameKit.LoadTests` then a smoke `--filter *` short run | ⬜ |
| BASELINES.md | PERF-02 | committed baseline w/ machine spec + .NET version + per-benchmark mean | docs + json presence | file-exists + JSON schema assertion | ⬜ |
| Regression gate + self-test | PERF-06 | CompareBaseline exits 1 on >20% regress, 0 within threshold | unit (gate self-test) | `dotnet test benchmarks/CompareBaseline.Tests` | ⬜ |
| k6 matchmaking burst | PERF-03 | 500 VUs queue vs local stack → measured p99; never CI-vs-prod | k6 run (local) | `docker run --rm -i grafana/k6 run - < k6/matchmaking-burst.js` exits 0 | ⬜ |
| k6 SignalR spike + fan-out | PERF-04 | spike confirms k6 WS speaks SignalR handshake BEFORE the fan-out scenario is committed | k6 spike then scenario | spike script exits 0 (handshake ok); fan-out produces delivery distribution | ⬜ |
| performance-tuning.md | PERF-05 | BCrypt/Argon2 cost vs latency, Npgsql pool sizing, top-5 hot queries | docs presence | file-exists + content assertion | ⬜ |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/GameKit.LoadTests` BenchmarkDotNet console project (PERF-01) — new, separate from Matchmaking.LoadTests
- [ ] `benchmarks/CompareBaseline` comparison tool + `CompareBaseline.Tests` self-test (PERF-06) — the gate must be proven, not just present
- [ ] `benchmarks/baselines/report-baseline.json` + `benchmarks/BASELINES.md` (PERF-02)
- [ ] `k6/` scenarios: matchmaking-burst (PERF-03), signalr-spike + signalr-fanout (PERF-04)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| k6 SignalR spike interpretation | PERF-04 | If the spike fails, a human decides xk6-extension vs scope adjustment (research Open Q2) | Run the spike; if handshake fails, escalate before committing the fan-out scenario |
| Tuning-guide prose accuracy | PERF-05 | latency-table values are human-reviewed against measured benchmarks | Cross-check the cost-factor table against BASELINES.md |

*Functional gates (benchmarks compile/run, regression gate self-test, k6 scenarios exit 0) are automated; only the spike-failure decision + prose are manual.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency acceptable (gate self-test is the fast proxy for the slow benchmark run)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
