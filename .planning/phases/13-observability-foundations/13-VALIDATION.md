---
phase: 13
slug: observability-foundations
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-14
---

# Phase 13 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Derived from `13-RESEARCH.md` § Validation Architecture.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 (existing repo-wide) + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` 1.1.2 (new, analyzer tests) |
| **Config file** | None — inherits repo-wide `dotnet test` conventions |
| **Quick run command** | `dotnet test tests/GameKit.Build.Tests/` (analyzer tests only — fast) |
| **Full suite command** | `dotnet test` (solution-wide) |
| **Estimated runtime** | Analyzer suite ~5s; full suite ~minutes (Testcontainers Postgres/Redis) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/GameKit.Build.Tests/` (analyzer tests, < 5s)
- **After every plan wave:** Run `dotnet test` (full suite — catches Core/Matchmaking/Rankings telemetry regressions)
- **Before `/gsd:verify-work`:** Full suite must be green AND the criterion #3 Docker isolation check must pass
- **Max feedback latency:** ~5s per task (analyzer), full suite per wave

---

## Per-Task Verification Map

> Task IDs are assigned by the planner. Rows below are keyed to requirements + success criteria;
> each row maps to whichever task(s) the planner creates for that behavior.

| Behavior | Wave | Requirement | Criterion | Threat Ref | Secure Behavior | Test Type | Automated Command | File (Wave 0 = new) | Status |
|----------|------|-------------|-----------|------------|-----------------|-----------|-------------------|---------------------|--------|
| PII literal key (`player.id`) blocked by analyzer | 1 | OBS-07 | #1 | T-13-PII | PII attribute key emits GK0001, fails build | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | ❌ W0 `PiiAttributeAnalyzerTests.cs` | ⬜ pending |
| Clean key (`ladder.id`) passes analyzer | 1 | OBS-07 | #1 | T-13-PII | Non-PII key produces no diagnostic | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | ❌ W0 | ⬜ pending |
| camelCase PII key (`playerCount`) blocked (case-split) | 1 | OBS-07 | #1 | T-13-PII | Case-boundary token split catches `player` | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | ❌ W0 | ⬜ pending |
| False-positive guard (`recipient.count`, `zip.code`) clean | 1 | OBS-07 | #1 | T-13-PII | Whole-token match avoids substring hits | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | ❌ W0 | ⬜ pending |
| Allow-listed key passes despite denylist token | 1 | OBS-07/08 | #1 | T-13-PII | Documented allow-list exempts intentional keys | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | ❌ W0 | ⬜ pending |
| `GameKitTelemetry` constants are single source of truth | 2 | OBS-02 | #4 | — | No magic strings; per-package classes reference Core const | unit (reflection) | `dotnet test tests/GameKit.Core.Tests/` | ❌ W0 | ⬜ pending |
| `AddGameKitObservability()` registers sources, no forced SDK | 2 | OBS-01/02 | #2 | — | Consumers omitting the call pull no OTel SDK | unit (smoke) | `dotnet test tests/GameKit.Core.Tests/` | ❌ W0 | ⬜ pending |
| `RankingsActivitySource.SourceName == GameKitTelemetry.RankingsTickerSourceName` | 2 | OBS-02 | #5 | — | Extracted source mirrors Matchmaking pattern | unit (reflection) | `dotnet test tests/GameKit.Rankings.Tests/` | ❌ W0 | ⬜ pending |
| Matchmaking camelCase tags normalized to lowercase-dotted | 2 | OBS-03 | #4 | — | Keys match OTel semantic conventions | unit/source assert | `dotnet test tests/GameKit.Matchmaking.Tests/` | existing + update | ⬜ pending |
| `curl http://localhost:9090` connection refused (Prometheus host-isolated) | 3 | OBS-08 | #3 | T-13-METRICS | Prometheus has no `ports:` mapping | integration (manual/CI) | `docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d && curl -f http://localhost:9090 ; test $? -ne 0` | ❌ W0 | ⬜ pending |
| Stack starts: Collector + Prometheus + Grafana + Tempo | 3 | OBS-08 | #3 | — | All 4 containers healthy; Grafana :3000 reachable | integration (smoke) | `curl -f http://localhost:3000/api/health` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/GameKit.Build.Tests/GameKit.Build.Tests.csproj` — new `net10.0` xUnit project, references `GameKit.Build` as a plain `ProjectReference` (NOT `OutputItemType="Analyzer"`)
- [ ] `tests/GameKit.Build.Tests/PiiAttributeAnalyzerTests.cs` — analyzer positive/negative fixtures (OBS-07)
- [ ] Package adds: `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4 + `...XUnit` 1.1.2 (gate behind `checkpoint:human-verify` before adding to `Directory.Packages.props` per research slopcheck note)
- [ ] `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` — criterion #4 enforcement + `AddGameKitObservability()` smoke
- [ ] `tests/GameKit.Rankings.Tests/Telemetry/RankingsActivitySourceTests.cs` — criterion #5

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Docker network isolation (criterion #3) | OBS-08 | Requires Docker daemon + live container stack; not a unit test | From `samples/TicTacToeDuel`: `docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`, then `curl -f http://localhost:9090` MUST fail (non-zero exit), `curl -f http://localhost:3000/api/health` MUST succeed |
| Grafana dashboards auto-provision | OBS-08 | Visual confirmation of provisioned-as-code datasources + 2 dashboards | Open `http://localhost:3000`, confirm Prometheus + Tempo datasources and the matchmaking-queue-depth + ticker-health dashboards load without click-ops |

---

## Validation Sign-Off

- [ ] All tasks have automated verify or Wave 0 dependencies (Docker isolation is manual-but-scriptable)
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (4 new test files)
- [ ] No watch-mode flags
- [ ] Feedback latency < ~5s for the analyzer quick-run
- [ ] `nyquist_compliant: true` set in frontmatter (set after planner maps every task to a verify)

**Approval:** pending
