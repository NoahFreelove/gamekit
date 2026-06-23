---
phase: 17
slug: backup-dr-migration-ops
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-22
---

# Phase 17 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11 (Postgres + Redis) + Roslyn analyzer test SDK (mirrors Phase 13 PII analyzer) |
| **Quick run command** | `dotnet test tests/GameKit.Core.Tests -p:NuGetAudit=false` |
| **Full suite command** | `dotnet test GameKit.sln -p:NuGetAudit=false` (Docker required) |
| **Estimated runtime** | ~120–300 s (DR round-trip spins 2 sequential Postgres containers) |

---

## Sampling Rate

- **After every task commit:** affected package's unit suite
- **After every plan wave:** affected-package full suites including Integration.Tests
- **Before verification:** full affected-package suites green (full-suite gate rule)
- **Max feedback latency:** ~300 s

---

## Per-Task Verification Map

| Task ID | Plan | Requirement | Secure Behavior | Test Type | Automated Command | Status |
|---------|------|-------------|-----------------|-----------|-------------------|--------|
| 17-xx (Down gate) | — | DR-04 | Every migration `Down()` contains only `throw new NotSupportedException(...)` | analyzer (GK0003) + analyzer test | `dotnet test tests/GameKit.Analyzers.Tests` (or migration-parse unit test) | ⬜ |
| 17-xx (Down convert) | — | DR-04 | All 14 existing migration `Down()` bodies converted; build + migrations still apply | unit/integration | `dotnet test tests/GameKit.Core.Integration.Tests` | ⬜ |
| 17-xx (migrations list) | — | DR-02 | `gamekit migrations list` prints per-package pending counts + order Core→Auth→Admin→Rankings→Matchmaking→Lobby | CLI test | `dotnet test tests/GameKit.Cli.Tests` (assert stdout) | ⬜ |
| 17-xx (apply --dry-run) | — | DR-03 | `gamekit migrations apply --dry-run` prints idempotent SQL, executes zero DDL | CLI/integration | assert SQL output + row-count unchanged in Testcontainers PG | ⬜ |
| 17-xx (DR round-trip) | — | DR-01 | pg_dump → destroy → pg_restore → app starts → `GET /health/ready` == 200 | integration (Testcontainers, ExecAsync + bind-mount) | `dotnet test ...Integration.Tests --filter "Category=DisasterRecovery"` | ⬜ |
| 17-xx (timestamp order) | — | DR-05 | Each package latest migration timestamp > previous package's latest (Core<Auth<Admin<Rankings<Matchmaking<Lobby) | unit | `dotnet test ... --filter MigrationTimestampTests` | ⬜ |
| 17-xx (runbooks) | — | DR-06/07 | `docs/runbooks/` has backup/restore (PG + Redis) + migration-apply runbooks | docs presence + link check | file-exists assertion / markdown lint | ⬜ |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] DR-04 gate (analyzer test project OR migration-parse unit test) — the check that fails on a non-conforming `Down()`
- [ ] `MigrationTimestampTests` — cross-package ordering (DR-05); will require the 5 no-op marker migrations to be added first
- [ ] DR round-trip integration test (`Category=DisasterRecovery`) — pg_dump/pg_restore via Testcontainers ExecAsync
- [ ] CLI test project / cases for `migrations list` + `apply --dry-run` output

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Operator runbook readability | DR-06/07 | Prose quality is human-judged | Review docs/runbooks/* for completeness against the success criteria |

*All functional behaviors have automated verification; only runbook prose is manual.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 300s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
