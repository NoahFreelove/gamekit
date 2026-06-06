---
phase: 9
slug: regional-matchmaking-pools-backfill
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-06
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 (.NET 10) + Testcontainers 4.11 (real Postgres + Redis) + Moq |
| **Config file** | `tests/GameKit.Matchmaking.Tests/`, `tests/GameKit.Matchmaking.Integration.Tests/` (existing projects) |
| **Quick run command** | `dotnet test tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj` |
| **Full suite command** | `dotnet test GameKit.sln` |
| **Estimated runtime** | ~unit <30s; integration (Testcontainers spin-up) ~3-6min |

---

## Sampling Rate

- **After every task commit:** Run the unit quick command for the affected package.
- **After every plan wave:** Run the affected package's integration test project.
- **Before `/gsd:verify-work`:** Full suite must be green.
- **Max feedback latency:** ~30s (unit); integration on wave boundaries.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 09-xx-xx | TBD | TBD | MATCH-18 | — | RegionName mismatch/missing rejected; null routes to "default" pool | unit + integration | `dotnet test tests/GameKit.Matchmaking.Tests/...` | ❌ W0 | ⬜ pending |
| 09-xx-xx | TBD | TBD | MATCH-18 | — | Redis key `mm:queue:{ladderId}:{regionName}` distinct from `:default`; ticker glob picks up both | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/...` | ❌ W0 | ⬜ pending |
| 09-xx-xx | TBD | TBD | MATCH-19 | — | `POST /api/matchmaking/backfill` creates backfill-typed ticket; processed at higher priority (Redis score 0) | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/...` | ❌ W0 | ⬜ pending |
| 09-xx-xx | TBD | TBD | MATCH-19 | — | ParticipationFraction below minimum → no rating change (PendingRatingUpdate INSERT skipped) | integration | `dotnet test tests/GameKit.Rankings.Integration.Tests/...` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*Task IDs finalized by the planner; this strategy seeds the four success-criteria behaviors.*

---

## Wave 0 Requirements

- [ ] Matchmaking migration `20260520000000` adds `ParticipationFraction` (session_participants) + `TicketType` (matchmaking_tickets) — schema-freeze integration test stubs.
- [ ] Regional pool routing unit + integration test stubs for MATCH-18 (validation + Redis key distinctness + ticker pool enumeration).
- [ ] Backfill endpoint + priority integration test stubs for MATCH-19.
- [ ] ParticipationFraction rating-guard integration test stub (cross-package: Matchmaking config → Rankings PendingRatingUpdatesAdapter).

*Existing Matchmaking + Rankings test infrastructure (MatchmakingTestApp, IntegrationTestHelpers, collection fixtures) covers harness needs.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| (none) | — | — | — |

*All phase behaviors have automated verification (xUnit + Testcontainers).*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s (unit)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
