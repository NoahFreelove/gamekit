---
phase: 12
slug: admin-multi-replica-distribution-close-out
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-07
---

# Phase 12 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 (.NET 10) + Testcontainers 4.11 (real Postgres + Redis) + Moq + bUnit (Blazor) + SignalR.Client |
| **Config file** | `tests/GameKit.Admin.Integration.Tests/` + `tests/GameKit.Admin.Tests/` (existing) |
| **Quick run command** | `dotnet test tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` |
| **Full suite command** | `dotnet test GameKit.sln` |
| **Estimated runtime** | unit <30s; Admin integration (Testcontainers) ~3-6min |

---

## Sampling Rate

- **After every task commit:** run the affected package's unit/quick command.
- **After every plan wave:** run the Admin integration project.
- **Before `/gsd:verify-work`:** full suite green.
- **Max feedback latency:** ~30s unit; integration on wave boundaries.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-----------|--------|
| 12-xx | TBD | TBD | ADMIN-13 | — | RedisErrorRateCounter: error written in context A visible in context B (cross-replica aggregate) | integration | `dotnet test tests/GameKit.Admin.Integration.Tests/... --filter ErrorRate` | ❌ W0 | ⬜ pending |
| 12-xx | TBD | TBD | ADMIN-14 | T-12-hub-authz | AdminEventHub message via Redis Pub/Sub gamekit:admin:events reaches admin sessions on another replica; hub gated by GameKitAdmin cookie scheme (NOT player JWT) | integration (2 TestServer) | `dotnet test ... --filter AdminBackplane` | ❌ W0 | ⬜ pending |
| 12-xx | TBD | TBD | ADMIN-15 | T-12-rankadjust-authz | /admin/rankings/adjust renders working form (existing RankAdjustDialog) → IRankAdjustService → admin_audit_log row; stub replaced | bUnit/integration | `dotnet test ... --filter RankAdjust` | ❌ W0 | ⬜ pending |
| 12-xx | TBD | TBD | DIST-07 | — | All 5 new packages on the MinVer train: same version, exact-pinned [X.Y.Z] sibling refs, covered by GameKitVersionAssertionHostedService; none report 0.0.0 | unit/integration | `dotnet test ... --filter VersionTrain` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*Task IDs finalized by the planner; this strategy seeds the four success-criteria behaviors.*

---

## Wave 0 Requirements

- [ ] RedisErrorRateCounter cross-replica test stub (two contexts / shared Redis).
- [ ] AdminEventHub two-TestServer shared-Redis backplane test stub (cookie-scheme auth).
- [ ] RankAdjust page bUnit/integration test stub (stub-replaced → dialog → IRankAdjustService → audit row).
- [ ] Version-train coherence test stub (no package reports 0.0.0; assertion service covers all 5 new packages).

*Existing Admin test infrastructure (AdminTestHost, AdminCollection, PostgresFixture+RedisFixture, bUnit) covers harness needs.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Multi-replica Blazor circuit + Data Protection key sharing | ADMIN-13/DIST-07 | Requires 2 live replicas behind a load balancer; ops-docs deliverable | Follow the multi-replica ops guide; confirm key-ring shared + sticky/backplane circuit |

*Code behaviors have automated verification; the live multi-replica deployment is an ops-docs item.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s (unit)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
