---
phase: 13
slug: observability-foundations
status: verified
nyquist_compliant: true
wave_0_complete: true
created: 2026-06-14
validated: 2026-06-14
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
| PII literal key (`player.id`) blocked by analyzer | 1 | OBS-07 | #1 | T-13-PII | PII attribute key emits GK0001, fails build | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `PiiAttributeAnalyzerTests.PlayerDotId_Literal_ReportsGK0001` | ✅ green |
| Clean key (`ladder.id`) passes analyzer | 1 | OBS-07 | #1 | T-13-PII | Non-PII key produces no diagnostic | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…LadderId_Clean_NoDiagnostic` | ✅ green |
| camelCase PII key (`playerCount`) blocked (case-split) | 1 | OBS-07 | #1 | T-13-PII | Case-boundary token split catches `player` | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…PlayerCount_CamelCase_ReportsGK0001` | ✅ green |
| snake_case PII key (`player_id`) blocked (CR-01 regression) | 1 | OBS-07 | #1 | T-13-PII | `_` token split catches `player` (CR-01 fix) | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…PlayerUnderscoreId_SnakeCase_ReportsGK0001` | ✅ green |
| kebab-case PII key (`client-ip`) blocked (CR-01 regression) | 1 | OBS-07 | #1 | T-13-PII | `-` token split catches `ip` (CR-01 fix) | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…ClientHyphenIp_KebabCase_ReportsGK0001` | ✅ green |
| `ActivityTagsCollection.Add("player.id")` blocked (WR-02 fix) | 1 | OBS-07 | #1 | T-13-PII | `Add` method now analyzed, not just SetTag/AddTag | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…TagsCollectionAdd_PlayerId_ReportsGK0001` | ✅ green |
| False-positive guard (`recipient.count`, `zip.code`) clean | 1 | OBS-07 | #1 | T-13-PII | Whole-token match avoids substring hits | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…RecipientCount_Clean_NoDiagnostic`, `…ZipCode_Clean_NoDiagnostic` | ✅ green |
| Allow-listed key passes (incl. case-insensitive, WR-03 fix) | 1 | OBS-07/08 | #1 | T-13-PII | Documented allow-list exempts intentional keys, casing-agnostic | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…AllowListed_Key_NoDiagnostic`, `…AllowListed_Key_CaseInsensitive_NoDiagnostic` | ✅ green |
| Non-literal key emits GK0002 Warning (T-13-PII-FN signal) | 1 | OBS-07 | #1 | T-13-PII-FN | Dynamic key surfaced as GK0002 Warning | analyzer unit | `dotnet test tests/GameKit.Build.Tests/` | `…NonLiteralKey_Variable_ReportsGK0002` | ✅ green |
| `GameKitTelemetry` constants are single source of truth | 2 | OBS-02 | #4 | T-13-MAGIC | No magic strings; per-package classes reference Core const | unit (reflection) | `dotnet test tests/GameKit.Core.Tests/ --filter ~Telemetry` | `GameKitTelemetryConstantsTests.MatchmakingActivitySource_SourceName_Equals_…` (+ const value tests) | ✅ green |
| `AddGameKitObservability()` registers sources, no forced SDK | 2 | OBS-01/02 | #2 | T-13-DEP-FLOW | Consumers omitting the call pull no OTel SDK | unit (smoke) | `dotnet test tests/GameKit.Core.Tests/ --filter ~Telemetry` | `…AddGameKitObservability_DoesNotThrow_*`, `…_ReturnsIGameKitBuilder` (SDK-flow enforced by `PrivateAssets="all"`, structural) | ✅ green |
| `RankingsActivitySource.SourceName == GameKitTelemetry.RankingsTickerSourceName` | 2 | OBS-02 | #5 | T-13-MAGIC | Extracted source mirrors Matchmaking pattern | unit (reflection) | `dotnet test tests/GameKit.Rankings.Tests/ --filter ~Telemetry` | `RankingsActivitySourceTests.SourceName_EqualsGameKitTelemetry_RankingsTickerSourceName` | ✅ green |
| RankingsTickerService has no inline `new ActivitySource(` | 2 | OBS-02 | #5 | T-13-MAGIC | Inline source fully extracted | unit/source assert | `dotnet test tests/GameKit.Rankings.Tests/ --filter ~Telemetry` | `…RankingsTickerService_DoesNotContain_InlineActivitySourceDeclaration` | ✅ green |
| Matchmaking camelCase tags normalized to lowercase-dotted | 2 | OBS-03 | #4 | T-13-MAGIC | Keys match OTel semantic conventions | unit/source assert (Theory ×9) | `dotnet test tests/GameKit.Matchmaking.Tests/ --filter ~Telemetry` | `MatchmakingTagNamingTests.MatchmakingSource_DoesNotContain_OldCamelCaseTagKey` + Attr refs | ✅ green |
| `curl http://localhost:9090` connection refused (Prometheus host-isolated) | 3 | OBS-08 | #3 | T-13-METRICS | Prometheus has no `ports:` mapping | integration (manual) | `docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d && curl -f http://localhost:9090 ; test $? -ne 0` | Manual — live-validated ISOLATION-OK (SUMMARY 13-04); structurally re-confirmed (no `ports:` key) | ⚠️ manual |
| Stack starts: Collector + Prometheus + Grafana + Tempo | 3 | OBS-08 | #3 | — | All 4 containers healthy; Grafana :3000 reachable | integration (smoke) | `curl -f http://localhost:3000/api/health` | Manual — live-validated GRAFANA-OK (SUMMARY 13-04) | ⚠️ manual |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ manual (intentional, see Manual-Only) · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `tests/GameKit.Build.Tests/GameKit.Build.Tests.csproj` — new `net10.0` xUnit project, references `GameKit.Build` as a plain `ProjectReference` (NOT `OutputItemType="Analyzer"`)
- [x] `tests/GameKit.Build.Tests/PiiAttributeAnalyzerTests.cs` — analyzer positive/negative fixtures (OBS-07); 13 facts incl. CR-01 snake/kebab regressions + WR-02 `ActivityTagsCollection.Add` + WR-03 case-insensitive allow-list
- [x] Package adds: `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` + `...XUnit` (both `1.1.2`, pinned in `Directory.Packages.props` after the human-verify checkpoint)
- [x] `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` — criterion #4 enforcement (reflection) + `AddGameKitObservability()` smoke (16 facts)
- [x] `tests/GameKit.Rankings.Tests/Telemetry/RankingsActivitySourceTests.cs` — criterion #5 (3 facts)
- [x] `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingTagNamingTests.cs` — criterion #4 camelCase source-asserts (delivered beyond original Wave 0 list; 12 cases)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Docker network isolation (criterion #3) | OBS-08 | Requires Docker daemon + live container stack; not a unit test | From `samples/TicTacToeDuel`: `docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`, then `curl -f http://localhost:9090` MUST fail (non-zero exit), `curl -f http://localhost:3000/api/health` MUST succeed |
| Grafana dashboards auto-provision | OBS-08 | Visual confirmation of provisioned-as-code datasources + 2 dashboards | Open `http://localhost:3000`, confirm Prometheus + Tempo datasources and the matchmaking-queue-depth + ticker-health dashboards load without click-ops |

---

## Validation Audit 2026-06-14

| Metric | Count |
|--------|-------|
| Behaviors audited | 16 |
| Automated (COVERED, ✅ green) | 14 |
| Manual-only (intentional, live-validated) | 2 |
| Gaps found (MISSING/PARTIAL) | 0 |
| Resolved this run | 0 (no auditor spawn needed) |
| Escalated | 0 |

**Method:** State A audit. Cross-referenced every plan-time behavior against the delivered
test files and re-ran the automated suites first-hand:

| Suite | Command | Result |
|-------|---------|--------|
| Analyzer | `dotnet test tests/GameKit.Build.Tests/` | Passed 13/13 |
| Core telemetry | `dotnet test tests/GameKit.Core.Tests/ --filter ~Telemetry` | Passed 16/16 |
| Rankings telemetry | `dotnet test tests/GameKit.Rankings.Tests/ --filter ~Telemetry` | Passed 3/3 |
| Matchmaking telemetry | `dotnet test tests/GameKit.Matchmaking.Tests/ --filter ~Telemetry` | Passed 12/12 |

Total 44 automated validation tests green. Coverage was *strengthened* beyond the plan-time
map: CR-01 snake_case/kebab-case regressions, WR-02 `ActivityTagsCollection.Add`, WR-03
case-insensitive allow-list, and the GK0002 non-literal signal are all now locked by tests.
The 2 Docker-isolation behaviors remain intentional manual-only (require a live Docker
daemon) and were live-validated during execution (ISOLATION-OK / GRAFANA-OK, SUMMARY 13-04);
T-13-METRICS host-isolation was additionally re-confirmed structurally (no `ports:` key).

---

## Validation Sign-Off

- [x] All tasks have automated verify or Wave 0 dependencies (Docker isolation is manual-but-scriptable)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (all new test files delivered + green)
- [x] No watch-mode flags
- [x] Feedback latency < ~5s for the analyzer quick-run (measured ~1s)
- [x] `nyquist_compliant: true` set in frontmatter (every automated task maps to a green verify)

**Approval:** verified 2026-06-14
