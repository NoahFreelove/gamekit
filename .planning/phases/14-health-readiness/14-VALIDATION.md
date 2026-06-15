---
phase: 14
slug: health-readiness
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-06-14
completed: 2026-06-15
---

# Phase 14 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Derived from `14-RESEARCH.md` § Validation Architecture.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11.0 (Postgres + Redis) |
| **Config file** | `tests/xunit.runner.json` |
| **Quick run command** | `dotnet test tests/GameKit.Core.Integration.Tests/ -x` |
| **Full suite command** | `dotnet test tests/ --filter "Category=Integration" -x` |
| **Estimated runtime** | ~120–240 seconds (Testcontainers spin-up dominates) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/GameKit.Core.Integration.Tests/ -x`
- **After every plan wave:** Run `dotnet test tests/ --filter "Category=Integration" -x`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** ~240 seconds

---

## Per-Task Verification Map

> Task IDs are assigned by the planner; rows below are keyed by requirement until plans exist.
> The Nyquist auditor reconciles task IDs after planning/execution.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | — | — | HLTH-01 | — | `/health/live` returns 200 even when Postgres container stopped | Integration | `dotnet test tests/GameKit.Core.Integration.Tests/ -x --filter "FullyQualifiedName~HealthEndpointTests"` | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-01 | — | `/health/ready` 503 while any migration pending, 200 once all six reporters ready | Integration | same as above | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-02 | — | Core-only (no Redis): `/health/ready` 503 when Postgres down, 200 when up; Redis absence never blocks | Integration | same as above | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-02 | — | With Redis configured: `/health/ready` 503 when Redis `PING` fails, 200 when up | Integration | same as above | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-03 | T-14 EoP | Follower replica (no leader lock): `/health/ready` returns 200 (Degraded, not Unhealthy) | Integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ -x --filter "FullyQualifiedName~MatchmakingLeaderHealthCheckTests"` | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-04 | — | Leader probe reports holder InstanceId + TTL remaining (non-acquiring read) | Integration | same as HLTH-03 | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-05 | T-14 InfoDisc | No response body contains host / port / `Password=` / `Host=` / connection-string fragments | Integration | `dotnet test tests/GameKit.Core.Integration.Tests/ -x --filter "FullyQualifiedName~HealthLeakTests"` | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-06 | — | Admin.UI `HealthProbeService` delegates to `HealthCheckService` — no raw `NpgsqlConnection`/`IDatabase` ctor params remain | Unit | `dotnet test tests/GameKit.Admin.Tests/ -x --filter "FullyQualifiedName~HealthProbeServiceDelegationTests"` | ❌ W0 | ⬜ pending |
| TBD | — | — | HLTH-06 | — | Admin health panel renders Core-sourced Postgres + Redis tiles + Admin-local error-rate tile | Integration | `dotnet test tests/GameKit.Admin.Integration.Tests/ -x --filter "FullyQualifiedName~HealthProbeTests"` | ⚠️ exists (needs update) | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs` — HLTH-01, HLTH-02 (live-200-when-pg-down; ready 503→200 on migration + Postgres + Redis gating)
- [ ] `tests/GameKit.Core.Integration.Tests/HealthLeakTests.cs` — HLTH-05 (payload contains no host/port/credential fragments)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs` — HLTH-03, HLTH-04 (follower→Degraded→200; holder + TTL surfaced)
- [ ] `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs` — HLTH-06 unit (no `NpgsqlConnection`/`IDatabase` constructor param remains after delegation)
- [ ] Update `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` — existing panel tests pass after the delegation refactor

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| K8s probe wiring against a real cluster | HLTH-01 | Out of scope (manifest/docs deferred to docs phase) | Not validated in this phase; integration tests use `WebApplicationFactory` + Testcontainers instead |

*All in-scope phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 240s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
