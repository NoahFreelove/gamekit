---
phase: 10
slug: account-merge
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-06
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 (.NET 10) + Testcontainers 4.11 (real Postgres + Redis) + Moq |
| **Config file** | `tests/GameKit.Auth.Integration.Tests/` (merge service + FK re-point + crash-resume) |
| **Quick run command** | `dotnet test tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` |
| **Full suite command** | `dotnet test GameKit.sln` |
| **Estimated runtime** | unit <30s; Auth integration (Testcontainers) ~3-6min |

---

## Sampling Rate

- **After every task commit:** run the affected package's unit quick command.
- **After every plan wave:** run the affected package's integration project.
- **Before `/gsd:verify-work`:** full suite green.
- **Max feedback latency:** ~30s unit; integration on wave boundaries.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 10-xx | TBD | TBD | AUTH-23 | T-10-resume | Process killed mid-merge resumes from last committed checkpoint via `account_merges` state machine; no duplicate work | integration | `dotnet test tests/GameKit.Auth.Integration.Tests/...` | ❌ W0 | ⬜ pending |
| 10-xx | TBD | TBD | AUTH-24 | — | All source player_identities/player_credentials/session_participants re-point to target; refresh tokens revoked; source soft-deleted with merged_into_player_id tombstone | integration | `dotnet test ...` | ❌ W0 | ⬜ pending |
| 10-xx | TBD | TBD | AUTH-25 | — | player_ranks conflict: keep higher-rated row per ladder, sum W/L/D | integration | `dotnet test ...` | ❌ W0 | ⬜ pending |
| 10-xx | TBD | TBD | AUTH-26 | T-10-authz | Merge recorded in admin_audit_log (before/after JSON); actor_id FK ON DELETE SET NULL; endpoint requires gamekit.admin.superadmin; response never includes source player_id | integration | `dotnet test ...` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*Task IDs finalized by the planner; this strategy seeds the five success-criteria behaviors.*

---

## Wave 0 Requirements

- [ ] Core migrations: `merged_into_player_id` + `deleted_at` on players (ON DELETE SET NULL); `admin_audit_log.actor_id` FK ON DELETE SET NULL — schema-freeze integration test stubs.
- [ ] `account_merges` idempotency table + state machine (pending→committed→redis_cleaned) — owning-package migration + crash-resume test stub.
- [ ] FK re-point integration test stubs (identities, credentials with PK-conflict handling, session_participants ALL rows, player_ranks conflict-merge).
- [ ] Superadmin authz + audit + ON DELETE SET NULL test stubs.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| (none) | — | — | — |

*All phase behaviors have automated verification (xUnit + Testcontainers, incl. simulated mid-merge crash + re-request).*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s (unit)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
