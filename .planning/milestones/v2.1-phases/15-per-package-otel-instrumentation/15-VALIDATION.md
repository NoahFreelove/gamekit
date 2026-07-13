---
phase: 15
slug: per-package-otel-instrumentation
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-06-22
---

# Phase 15 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 (+ Moq 4.20.72; Testcontainers 4.11.0 for the integration projects) |
| **Config file** | none — existing per-package test projects (`tests/GameKit.*.Tests`, `tests/GameKit.Lobby.Integration.Tests`) |
| **Quick run command** | `dotnet test tests/GameKit.Matchmaking.Tests -p:NuGetAudit=false` (per-package quick test; `-p:NuGetAudit=false` avoids the pre-existing MessagePack NU1903 advisory — see MEMORY "Pre-existing MessagePack NU1903") |
| **Full suite command** | `dotnet build src/GameKit.Rankings src/GameKit.Lobby src/GameKit.Matchmaking -p:NuGetAudit=false && dotnet test tests/GameKit.Core.Tests tests/GameKit.Matchmaking.Tests tests/GameKit.Rankings.Tests tests/GameKit.Lobby.Integration.Tests -p:NuGetAudit=false --filter "Category!=LoadTest"` (Plan 06 Task 3 regression gate — rebuild the 3 instrumented packages first so the Core reflection Facts `Assembly.LoadFrom` the final per-package dlls) |
| **Estimated runtime** | ~90–150 seconds (4 unit/integration suites + 3 package rebuilds; the Lobby Integration project may spin Testcontainers for non-telemetry tests) |

---

## Sampling Rate

- **After every task commit:** Run the task's `<automated>` command (per-package `dotnet build`/`dotnet test --filter` as listed in the map below)
- **After every plan wave:** Run the affected-package quick suite(s) for that wave's packages (e.g. Wave 2 → Matchmaking + Rankings + Lobby unit suites)
- **Before `/gsd-verify-work`:** Full suite command must be green (except the documented pre-existing stale reds below)
- **Max feedback latency:** 150 seconds

Documented pre-existing stale reds (NOT regressions — ignore in gates):
- Core.Integration `Migrate_Twice_Is_Idempotent` asserts `Single()` but Core has 4 migrations — stale since Phase 1 (MEMORY "Pre-existing MigrationDeterminism red").
- MessagePack NU1903 advisory on full-solution restore — pre-dates Phase 13; build/test affected packages with `-p:NuGetAudit=false` (MEMORY "Pre-existing MessagePack NU1903").

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------|-------------------|-------------|--------|
| 15-01-01 | 01 | 1 | OBS-04 / OBS-05 / OBS-06 | T-15-01-DRIFT | unit | `dotnet test tests/GameKit.Core.Tests --filter "TelemetryConstants\|GameKitTelemetryConstantsTests"` | ✅ | ⬜ pending |
| 15-01-02 | 01 | 1 | OBS-04 / OBS-05 / OBS-06 | T-15-01-PII | unit (Wave-0 stubs) | `dotnet build tests/GameKit.Matchmaking.Tests tests/GameKit.Rankings.Tests tests/GameKit.Lobby.Integration.Tests -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 15-02-01 | 02 | 2 | OBS-04 | T-15-02-DOS / T-15-02-CARD | unit (build gate) | `dotnet build src/GameKit.Matchmaking -p:NuGetAudit=false && dotnet build tests/GameKit.Matchmaking.Tests -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-02-02 | 02 | 2 | OBS-04 | T-15-02-PII | unit | `dotnet test tests/GameKit.Matchmaking.Tests --filter "PiiTagKey\|MatchmakingMetrics\|TickerLag\|QueueDepth\|LeaderLock" -p:NuGetAudit=false` | ❌ W0 (fills 15-01-02 Matchmaking stub) | ⬜ pending |
| 15-03-01 | 03 | 3 | OBS-06 | T-15-03-TRACE-INJ / T-15-03-PII | unit (build gate) | `dotnet build src/GameKit.Matchmaking -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-03-02 | 03 | 3 | OBS-06 | T-15-03-TRACE-INJ / T-15-03-SAMPLE | unit (build gate) | `dotnet build src/GameKit.Matchmaking -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-03-03 | 03 | 3 | OBS-06 | T-15-03-TRACE-INJ | unit | `dotnet test tests/GameKit.Matchmaking.Tests --filter "TraceParent\|SpanLink\|NonSampled\|W3CTracePropagation" -p:NuGetAudit=false` | ❌ W0 (un-skips 15-01-02 W3C stub) | ⬜ pending |
| 15-04-01 | 04 | 2 | OBS-04 | T-15-04-PII | unit (build gate) | `dotnet build src/GameKit.Rankings -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-04-02 | 04 | 2 | OBS-04 / OBS-06 | T-15-04-PII / T-15-04-TIME / T-15-04-TRACE | unit | `dotnet test tests/GameKit.Rankings.Tests --filter "PiiTagKey\|RankingsMetrics\|DecayDuration" -p:NuGetAudit=false` | ❌ W0 (fills 15-01-02 Rankings stub) | ⬜ pending |
| 15-05-01 | 05 | 2 | OBS-05 / OBS-06 | T-15-05-PII / T-15-05-CARD | unit (build gate) | `dotnet build src/GameKit.Lobby -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-05-02 | 05 | 2 | OBS-05 | T-15-05-PII | unit (build gate) | `dotnet build src/GameKit.Lobby -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-05-03 | 05 | 2 | OBS-05 / OBS-06 | T-15-05-PII / T-15-05-TRACE | unit (build gate) | `dotnet build src/GameKit.Lobby -p:NuGetAudit=false` | ✅ | ⬜ pending |
| 15-05-04 | 05 | 2 | OBS-05 | T-15-05-PII | unit | `dotnet test tests/GameKit.Lobby.Integration.Tests --filter "PiiTagKey\|LobbyMetrics\|ConnectedClients" -p:NuGetAudit=false` | ❌ W0 (fills 15-01-02 Lobby stub) | ⬜ pending |
| 15-06-01 | 06 | 4 | OBS-04 / OBS-05 / OBS-06 | T-15-06-PII | unit | `dotnet test tests/GameKit.Core.Tests --filter "AddGameKitObservability\|GameKitTelemetryConstantsTests"` | ✅ | ⬜ pending |
| 15-06-02 | 06 | 4 | OBS-04 | T-15-06-EXPOSE / T-15-06-PII | integration (config grep) | `grep -q "namespace: gamekit" .../otel-collector-config.yml && ! grep -E "tick_duration_ms_bucket\|drain_ladder_duration_ms_bucket" .../ticker-health.json && ! grep -E '[^_]matchmaking_analytics_dropped_events_total' .../matchmaking-queue-depth.json && grep -q "gamekit_matchmaking_analytics_dropped_events_total" .../matchmaking-queue-depth.json` | ✅ | ⬜ pending |
| 15-06-03 | 06 | 4 | OBS-04 / OBS-05 / OBS-06 | T-15-06-PII | integration (full regression gate) | `dotnet build src/GameKit.Rankings src/GameKit.Lobby src/GameKit.Matchmaking -p:NuGetAudit=false && dotnet test tests/GameKit.Core.Tests tests/GameKit.Matchmaking.Tests tests/GameKit.Rankings.Tests tests/GameKit.Lobby.Integration.Tests -p:NuGetAudit=false --filter "Category!=LoadTest"` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*File Exists ❌ W0 = the meaningful assertion lands on a Wave-0 stub file created in 15-01-02; the stub compiles/passes trivially until the owning task fills it.*

---

## Wave 0 Requirements

Task 15-01-02 scaffolds the per-package PII tag-key test stubs and the in-process W3C trace-descendant test stub. These compile (and pass trivially / Skip-marked) at Wave 0 and are filled by the per-package plans:

- [ ] `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs` — MeterListener PII tag-key stub for OBS-04 criterion #1 (Matchmaking); exercises existing `DroppedEvents` today, filled by 15-02-02.
- [ ] `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs` — MeterListener PII tag-key stub for OBS-04 criterion #1 (Rankings); trivial pass until 15-04-02.
- [ ] `tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs` — MeterListener PII tag-key stub for OBS-05 criterion #1 (Lobby); trivial pass until 15-05-04.
- [ ] `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs` — in-process `ActivityListener` W3C trace-descendant stub for OBS-06 criterion #2 (parent restoration, fan-in link, non-sampled no-op); `Skip`-marked until un-skipped by 15-03-03.

Also created at Wave 0 by 15-01-01: the cross-package reflection `[Fact]`s in `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` (RED until 15-04/15-05 ship `RankingsMeter`/`LobbyMeter`/`LobbyActivitySource`) — the intended Wave-0 RED→GREEN gate.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Matchmaking queue-depth + ticker-health Grafana dashboards render real data against the live sample stack | ROADMAP success criterion #4 / D-06 | Requires the docker-compose observability sample stack (Collector→Prometheus+Tempo→Grafana) running with live matchmaking traffic and a visual Grafana panel check — no headless assertion can confirm the panels render correct data | 1. `cd samples/TicTacToeDuel && docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d` 2. Run the sample app and drive matchmaking traffic (enqueue tickets across pools until matches form). 3. Open Grafana; import/open the queue-depth + ticker-health dashboard JSON. 4. Confirm the queue-depth panels show non-zero per-pool depth and the ticker-health panels (ticker lag p50/p99, lease acquired/lost, pool-sweep + decay duration buckets) render real series. Record the result in 15-06-SUMMARY (note if deferred to a manual run). |
| Matchmaking enqueue→MatchFormation appears as a single causal descendant trace in Tempo | OBS-06 criterion #2 (live proof) | The automated proxy is the in-process `ActivityListener` Facts (15-03-03); the live Tempo descent check needs the running sample stack + a real enqueue request | After the dashboard stack is up, issue a matchmaking-enqueue request, then in Tempo confirm the `MatchFormation` span is a descendant of the originating enqueue HTTP span with co-matched tickets attached as span links. |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 150s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** approved 2026-06-22
