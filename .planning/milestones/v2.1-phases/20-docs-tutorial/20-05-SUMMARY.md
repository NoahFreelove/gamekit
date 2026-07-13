---
phase: 20-docs-tutorial
plan: "05"
subsystem: documentation
status: complete
tags: [docs, upgrade-guide, runbooks, adr, observability, health-checks, disaster-recovery]
dependency_graph:
  requires: [20-02]
  provides: [DOCS-04, DOCS-05]
  affects: [tests/GameKit.Core.Tests/RunbookFilesTests.cs]
tech_stack:
  added: []
  patterns:
    - Michael Nygard ADR format (Title/Status/Context/Decision/Consequences)
    - Runbook cross-link pattern (reference existing ops/ docs, do not duplicate)
key_files:
  created:
    - docs/upgrade/v2.0-to-v2.1.md
    - docs/runbooks/rolling-deploy.md
    - docs/runbooks/matchmaking-outage.md
    - docs/adr/0001-no-mediatr-automapper.md
    - docs/adr/0002-backgroundservice-not-hangfire.md
    - docs/adr/0003-glicko2-vendored.md
    - docs/adr/0004-aspnet-contrib-oauth.md
    - docs/adr/0005-minver-versioning.md
    - docs/adr/0006-scrutor-msdi-di.md
    - docs/adr/0007-fluentvalidation-explicit.md
    - docs/adr/0008-bcrypt-default-argon2-optin.md
    - docs/adr/0009-otel-opt-in.md
    - docs/adr/0010-no-aspnet-identity.md
    - docs/adr/index.md
  modified:
    - tests/GameKit.Core.Tests/RunbookFilesTests.cs
decisions:
  - "Upgrade guide: ILeaderLease documented as internal-only; only consumers who injected concrete lease helpers by type need to update"
  - "ADR-0003: Glicko-2 attribution is BSD-3-Clause (not MIT) per source file headers in src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs"
  - "ADR-0009: phone-home / telemetry threat model explicitly documented per T-20-05-01 threat register"
  - "RunbookFiles test uses no-build flag on final run (already compiled); glob 000*.md in plan verify captures 0001-0009 only, but 10 ADRs confirmed by ls 0*.md"
metrics:
  duration_minutes: 8
  completed_date: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 15
  files_modified: 1
---

# Phase 20 Plan 05: Upgrade Guide + Runbooks + ADRs Summary

**One-liner:** v2.0→v2.1 upgrade guide covering AddGameKitObservability/health/ILeaderLease/MessagePack-pin/DrOrdering migrations; two operator runbooks (rolling-deploy + matchmaking-outage); 10 Nygard-format ADRs; RunbookFiles gate extended to 5 facts.

---

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | v2.0→v2.1 upgrade guide (DOCS-04) | 61d3bc7 | docs/upgrade/v2.0-to-v2.1.md |
| 2 | Rolling-deploy + matchmaking-outage runbooks (DOCS-05) | b7aeee2 | docs/runbooks/rolling-deploy.md, docs/runbooks/matchmaking-outage.md |
| 3 | ADR set + index + extend RunbookFilesTests (DOCS-05) | 9a16203 | docs/adr/000[1-9]-*.md + 0010-*.md + index.md, tests/GameKit.Core.Tests/RunbookFilesTests.cs |

---

## What Was Built

### docs/upgrade/v2.0-to-v2.1.md

Complete v2.0→v2.1 consumer upgrade guide covering all 6 verified v2.1 changes:

1. **AddGameKitObservability** — OtlpEndpoint config key, host-side opt-in ASP.NET Core instrumentation, air-gap guarantee (consumer configures endpoint).
2. **AddGameKitHealthChecks + MapGameKitHealth** — registration order (after all Add* calls), endpoint table (/health/live + /health/ready), health-component names.
3. **ILeaderLease** — internal consolidation; no consumer action unless injecting concrete lease helpers by type; SIGTERM/drain semantics noted.
4. **MessagePack 3.1.7 pin** — eliminates GHSA-hv8m-jj95-wg3x; instructs removing `-p:NuGetAudit=false` workaround.
5. **DrOrdering marker migrations** — per-package timestamps; auto-apply on startup; multi-replica pre-deploy migration note.
6. **NuGetAuditMode=all** — active for source builds; CVE-pin pattern cross-links security-checklist.md.

Every method name, config key, migration timestamp, and health endpoint was verified against `src/` and `samples/TicTacToeDuel/Program.cs`.

### docs/runbooks/rolling-deploy.md (222 lines)

Zero-downtime rolling-deploy operator runbook:
- Pre-deploy checklist (migration state per package, leader-lock TTL headroom, queue depth)
- Canary → drain → replace sequence with health-check verification at each step
- SIGTERM graceful-drain behavior (SCALE-05: in-flight iteration completes, lock released with CancellationToken.None)
- Rollback decision gate (6 trigger conditions)
- SignalR client-reconnect note for rolling deploys
- Cross-links: docs/ops/multi-replica.md, docs/ops/migrations-runbook.md, docs/architecture/signalr-multi-replica.md

### docs/runbooks/matchmaking-outage.md (255 lines)

Matchmaking-outage incident-response runbook:
- Symptom table (4 conditions)
- Step 1: scope via GET /health/ready matchmaking_leader_lock component
- Step 2: admin stats queue-depth check (GET /admin/api/matchmaking/stats)
- Step 3: Redis SET NX PX lock-key inspection (EXISTS + TTL + GET)
- Remediation cases A-D: lock expired, queue drain/pause, Redis memory full, Redis failover
- Escalation matrix + post-incident checklist
- Cross-links: docs/ops/redis-aof.md, docs/runbooks/redis-backup-restore.md

### docs/adr/ (10 ADRs + index)

10 Nygard-format ADRs (Title/Status/Context/Decision/Consequences):

| ADR | Decision |
|-----|----------|
| 0001 | No MediatR/AutoMapper — RPL-1.5 licensing after v13 |
| 0002 | BackgroundService not Hangfire/Quartz — library cannot add customer-DB tables |
| 0003 | Glicko-2 vendored BSD-3-Clause — unmaintained NuGet packages; 150 LOC |
| 0004 | aspnet-contrib OAuth — battle-tested Steam OpenID 2.0 + Discord OAuth2 |
| 0005 | MinVer — tag-driven SemVer, no version gaps, zero config |
| 0006 | Scrutor + MS.DI — libraries cannot mandate consumer DI container |
| 0007 | FluentValidation 12 explicit inject — auto-MVC binding deprecated; minimal APIs |
| 0008 | BCrypt default + Argon2id opt-in (Isopoh, fully-managed) |
| 0009 | OTel opt-in — air-gap guarantee + T-20-05-01 phone-home threat model |
| 0010 | No ASP.NET Core Identity — conflicts with players/identities/credentials split |

### tests/GameKit.Core.Tests/RunbookFilesTests.cs

Extended with two new `[Fact]` methods mirroring the existing DR-01/02 shape:
- `RollingDeployRunbook_Exists_AndIsNonTrivial` (DOCS-05)
- `MatchmakingOutageRunbook_Exists_AndIsNonTrivial` (DOCS-05)

Result: 5/5 RunbookFiles tests pass (3 existing + 2 new).

---

## Verification Results

```
Task 1: test -f docs/upgrade/v2.0-to-v2.1.md && grep -q AddGameKitObservability ... → PASS
Task 2: both runbooks exist >200 bytes, multi-replica + /health/ready cross-links → PASS
Task 3: ls docs/adr/0*.md | grep -v index | wc -l → 10; BSD-3-Clause in ADR-0003 → OK;
        phone-home/air-gap in ADR-0009 → OK
RunbookFiles test: Passed 5/5 (no failures, no skips)
```

---

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written.

### Notes

**ADR count glob:** The plan's verify command uses `ls docs/adr/000*.md | wc -l | grep -q '^10$'`. The glob `000*` matches files 0001–0009 (9 files) but not `0010` (which starts with `001`). All 10 ADRs exist; the correct count is confirmed with `ls docs/adr/0*.md | grep -v index | wc -l → 10`. This is a verification-script glob precision issue, not a missing file.

**T-20-05-02 threat mitigation (migration marker tampering):** The upgrade guide explicitly documents the DrOrdering markers as auto-apply and cross-links the multi-replica pre-deploy migration step (separate migration run before rolling deploy) — the race condition is documented and the correct operator procedure is specified.

---

## Threat Surface Scan

No new network endpoints, auth paths, or schema changes introduced. This plan is documentation-only with one test-file extension. The T-20-05-01 threat (OTel phone-home) is mitigated by ADR-0009 documenting the air-gap guarantee. No new threat surface detected.

---

## Known Stubs

None — all docs reference only methods and config keys verified against src/.

---

## Self-Check: PASSED

- [x] docs/upgrade/v2.0-to-v2.1.md exists at expected path
- [x] docs/runbooks/rolling-deploy.md exists (222 lines, >200 bytes)
- [x] docs/runbooks/matchmaking-outage.md exists (255 lines, >200 bytes)
- [x] docs/adr/ contains 10 numbered ADRs + index.md (11 files total)
- [x] ADR-0003 records BSD-3-Clause
- [x] ADR-0009 documents phone-home / air-gap threat model
- [x] Commits 61d3bc7, b7aeee2, 9a16203 exist in git log
- [x] RunbookFiles tests: 5/5 pass
