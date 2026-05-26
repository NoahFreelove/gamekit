---
phase: 6
slug: presence-openapi-distribution
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-25
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Derived from 06-RESEARCH.md §Validation Architecture; expand the Per-Task table as plans are authored.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Moq 4.20.72 + Testcontainers.PostgreSql 4.11.0 + Testcontainers.Redis 4.11.0 + bUnit 1.40.0 (admin component renders) |
| **Config file** | `tests/Directory.Build.props` + per-project `xunit.runner.json` |
| **Quick run command** | `dotnet test tests/GameKit.<Pkg>.Tests/ -c Debug --no-build` (unit-only, per package being edited) |
| **Full suite command** | `dotnet test --filter "Category!=LoadTest"` at repo root (skip 1k-ticket load test) |
| **Estimated runtime** | ~90 seconds quick / ~6 minutes full |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/GameKit.<Pkg>.Tests/ -c Debug --no-build` for the unit project of whichever GameKit.<Pkg> the task edited.
- **After every plan wave:** Run `dotnet test --filter "Category!=LoadTest"` at repo root.
- **Before `/gsd:verify-work`:** Full suite must be green (including new tests/GameKit.Distribution.Integration.Tests/ + tests/GameKit.OpenApi.Integration.Tests/ + tests/GameKit.Presence.Integration.Tests/).
- **Max feedback latency:** 90 seconds (unit tests) / 6 minutes (full).

---

## Per-Task Verification Map

> Filled in as plans are authored; each task in each PLAN.md gets one row.
> Phase requirements covered: PRES-01..06, OPEN-01, DIST-02..06, OPS-04, OPS-05.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | 01 | 0 | PRES-01..06, OPEN-01, DIST-02..06, OPS-04, OPS-05 | T-06-XX (TBD) | Scaffolding for 3 new test csprojs | unit (smoke) | `dotnet test tests/GameKit.Presence.Tests tests/GameKit.OpenApi.Integration.Tests tests/GameKit.Distribution.Integration.Tests --filter FullyQualifiedName~SmokeTests` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

*Note: full per-task table will be appended as gsd-planner produces PLAN.md files. Source of truth for the requirement→test mapping is `06-RESEARCH.md §Phase Requirements → Test Map` (lines 991-1007).*

---

## Wave 0 Requirements

- [ ] **`tests/GameKit.Presence.Tests/GameKit.Presence.Tests.csproj`** — xUnit + Moq unit tests (RedisPresenceProvider with `IConnectionMultiplexer` mock; covers PRES-01, PRES-02, PRES-04).
- [ ] **`tests/GameKit.Presence.Integration.Tests/GameKit.Presence.Integration.Tests.csproj`** — Testcontainers Postgres + Redis + WebApplicationFactory (heartbeat TTL, status states, abandonment, in-match precedence; covers PRES-03, PRES-05).
- [ ] **`tests/GameKit.OpenApi.Integration.Tests/GameKit.OpenApi.Integration.Tests.csproj`** — WebApplicationFactory + EndpointDataSource enumeration + admin-route exclusion assertion (OPEN-01).
- [ ] **`tests/GameKit.Distribution.Integration.Tests/GameKit.Distribution.Integration.Tests.csproj`** — Testcontainers Postgres with 3-role init + template installer harness + multi-assembly version-assertion (DIST-02, DIST-03, DIST-04, OPS-04, OPS-05).
- [ ] **`tests/GameKit.Admin.Integration.Tests/PresencePanelTests.cs`** — new file in existing csproj; bUnit + WebApplicationFactory presence panel test (PRES-06).
- [ ] **CollectionDefinitions.cs** in each new test csproj — composes `PostgresFixture` + `RedisFixture` (Phase 5 pattern).
- [ ] **PresenceCollection / OpenApiCollection / DistributionCollection** — xUnit collection definitions per phase 5 idiom.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Production-readiness ops guide content accuracy + link integrity | DIST-05 | docs/ops/*.md prose is reference content; no test framework reads it for correctness. CI link-check (markdown-link-check) catches stale URLs; the guide's substantive correctness is operator judgment. | `for f in docs/ops/*.md; do markdown-link-check "$f"; done` + manual operator review during /gsd:verify-work. |
| `dotnet new install GameKit.Templates` + `dotnet new gamekit` UX feel | DIST-04 | The template-install command is exercised by DIST-03 in tests, but the *developer experience* (CLI prompts, IDE form generation in VS / Rider) is human judgment. | Manual walkthrough during /gsd:verify-work HUMAN-UAT. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (3 new test csprojs + 1 new file in existing csproj)
- [ ] No watch-mode flags (`dotnet test` runs once and exits)
- [ ] Feedback latency < 90s for unit tests
- [ ] `nyquist_compliant: true` set in frontmatter once planner fills per-task table

**Approval:** pending
