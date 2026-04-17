---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: planning
last_updated: "2026-04-17T03:24:56.842Z"
progress:
  total_phases: 6
  completed_phases: 1
  total_plans: 7
  completed_plans: 7
  percent: 100
---

# STATE: GameKit

## Project Reference

**Core Value:** A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.

**License:** GPL
**Runtime:** .NET 10 LTS (released 2026-04-14)
**Mode:** YOLO / Quality model profile / parallel execution enabled
**Current Focus:** Phase 01 — Foundation (Core + Migrations + Ops Defaults + GPL)

## Current Position

Phase: 01 (Foundation (Core + Migrations + Ops Defaults + GPL)) — EXECUTING
Plan: 7 of 7
**Milestone:** v1 (initial 6-phase build-out)
**Phase:** 2
**Plan:** Not started
**Status:** Ready to plan

**Progress:** [██████████] 100%

**Pre-Flight Gate (Phase 1):**

- [x] Verify `Npgsql.EntityFrameworkCore.PostgreSQL` `net10.0` TFM GA on NuGet — 10.0.1 verified GA
- [ ] Verify `AspNet.Security.OpenId.Steam` 10.0.x + `AspNet.Security.OAuth.Discord` 10.0.x `net10.0` TFM (blocks Phase 2, not Phase 1 start, but track now)
- [x] Verify `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Polly`, `FluentValidation` 12, `Scrutor`, `MinVer` 7, `Microsoft.SourceLink.GitHub` all resolve on `net10.0` — all GA, pinned in Directory.Packages.props
- [x] Record workarounds (preview pins, compatibility shims) in STATE before first migration is authored — NO workarounds needed, all packages GA

## Performance Metrics

| Metric | Value |
|--------|-------|
| Phases complete | 0 / 6 |
| v1 requirements mapped | 92 / 92 |
| v1 requirements validated | 0 / 92 |
| Packages released | 0 / 6 |
| Phase 01 P01 | 4min | 3 tasks | 9 files |
| Phase 01 P02 | 2min | 3 tasks | 4 files |
| Phase 01 P03 | 9min | 3 tasks | 22 files |
| Phase 01 P04 | 11min | 3 tasks | 16 files |
| Phase 01 P05 | 14min | 2 tasks | 21 files |
| Phase 01 P06 | 5min | 3 tasks | 20 files |
| Phase 01 P07 | 23min | 5 tasks | 37 files |

## Accumulated Context

### Decisions Locked (from research)

| Decision | Source |
|----------|--------|
| Single fully-owned `GameKitDbContext` in DI (not a base class) | PROJECT.md Key Decisions |
| Per-package migrations assembly + per-package `__ef_migrations_<pkg>` history table + per-package `IDesignTimeDbContextFactory` | ARCHITECTURE.md + PITFALLS.md #3 |
| `BackgroundService` + `PeriodicTimer` + Polly (NOT Hangfire/Quartz) | STACK.md + PROJECT.md |
| MinVer coordinated release train, all 6 packages stamped to same version, sibling refs exact-pinned `[X.Y.Z]` | STACK.md + PITFALLS.md #11 |
| Reject MediatR / AutoMapper (RPL v13+ commercial license) | STACK.md |
| Presence in its own package (`GameKit.Presence`), Core defines `IPresenceProvider` | PROJECT.md Key Decisions |
| Parties live in `GameKit.Matchmaking` for v1 (ticket model 1-N from day one) | PROJECT.md Key Decisions |
| Blazor Server in RCL for Admin UI | PROJECT.md Key Decisions |
| Rating columns stored as `double precision`, not `NUMERIC(8,2)` | PITFALLS.md #13 |
| `IRankingAlgorithm.Apply(state, batch)` — batched, not per-match | PITFALLS.md #1 |
| Glicko-2 vendored from MaartenStaa/glicko2-csharp (MIT) | STACK.md |
| Steam provider implemented in-house against xPaw reference with server-side `check_authentication` roundtrip | PITFALLS.md #12 |
| Redis with `--appendonly yes --appendfsync everysec` in shipped `docker-compose.yml` | PITFALLS.md #17 |
| Three Postgres roles: `gamekit_owner`, `gamekit_app`, `gamekit_reader`; SampleGame game-server uses reader | PITFALLS.md #7 |
| GPL LICENSE + per-file headers + CI check from Phase 1 | Task prompt |
| Runtime guard asserts zero outbound HTTP from Core (except configured providers) | PROJECT.md + task prompt |
| Used legacy .sln format (not .slnx) for broad IDE compatibility | 01-01 execution — .NET 10 defaults to .slnx |
| MinVer 7.0.0 and SourceLink 10.0.202 (updated from CLAUDE.md stale 6.0.0/8.0.0) | 01-01 execution — verified GA on nuget.org |
| POSTGRES_USER=postgres (not gamekit_owner) as bootstrap superuser for init scripts | 01-02 execution — superuser needed for CREATE EXTENSION + REVOKE |
| Redis --maxmemory-policy noeviction for loud failures over silent key eviction | 01-02 execution — matchmaking/presence prefer errors over data loss |
| Npgsql transitive pin bumped 10.0.1 -> 10.0.2 (required by Npgsql.EFCore.PG 10.0.1) | 01-03 execution — NuGet restore error |
| Microsoft.Extensions.Caching.Memory bumped 10.0.0 -> 10.0.6 (required by EF Core 10.0.6) | 01-03 execution — transitive downgrade error |
| GameSessionState stored as string (HasConversion<string>) not integer | 01-03 execution — stable across enum reorderings |
| All entity Ids use ValueGeneratedNever (UUIDv7 from IIdGenerator, not DB) | 01-03 execution — per threat T-03-05 |
| Explicit snake_case table names in EF configs (defensive, not relying on naming convention) | 01-03 execution — Plan 04 may add UseSnakeCaseNamingConvention |
| Advisory lock key corrected to 1800940027 (live Postgres 17.9 verified via Testcontainers) | 01-07 execution — RESEARCH.md value was wrong |
| Migration timestamp renamed to 20260415000000 for deterministic cross-package ordering | 01-04 execution — EF CLI generated current timestamp |
| EF Core InMemory provider added to test project (Npgsql with fake conn string used for model tests) | 01-04 execution — InMemory can't handle jsonb column types |
| FrameworkReference Microsoft.AspNetCore.App replaces explicit Caching.Memory PackageReference | 01-05 execution — NU1510 warning: transitive dep redundant |
| PlayerDisplayNameResolver registered as Scoped (not Singleton per plan) | 01-05 execution — depends on scoped GameKitDbContext |
| GDPR ExecuteDeleteAsync round-trip test deferred to Plan 07 Testcontainers integration tests | 01-05 execution — InMemory provider does not support bulk operations |
| InMemory test factory with custom ModelCustomizer for JsonDocument value converters | 01-05 execution — InMemory can't handle jsonb/JsonDocument natively |

### Open Questions

None. All open questions from PROJECT.md were resolved before research completed. Research confirmed the resolutions.

### Todos

(none yet — accumulated during plan execution)

### Blockers

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 260416-tlm | Build Tic-Tac-Toe Duel sample app demonstrating Phase 1 GameKit | 2026-04-17 | 677260e | [260416-tlm-build-tic-tac-toe-duel-sample-app-demons](./quick/260416-tlm-build-tic-tac-toe-duel-sample-app-demons/) |

## Session Continuity

**Last action:** 2026-04-17 — Captured Phase 2 (Authentication) context: 14 locked decisions across JWT, fingerprint, egress, guest upgrade

**Next action:** `/gsd-plan-phase 2`
**Resume file:** .planning/phases/02-authentication/02-CONTEXT.md

**Context preserved:**

- PROJECT.md, REQUIREMENTS.md, research/{SUMMARY,STACK,FEATURES,ARCHITECTURE,PITFALLS}.md, config.json
- ROADMAP.md (6 phases, 92/92 coverage)
- 01-01-SUMMARY.md (repo chassis complete, 7 requirements marked complete)
- 01-02-SUMMARY.md (docker-compose + init scripts complete, DIST-01 + OPS-08 requirements)
- 01-03-SUMMARY.md (Core entities + EF configs, 8 requirements: CORE-01/03/04/06/07/08/09/17)
- 01-04-SUMMARY.md (DbContext + ModelCustomizer + MigrationRunner + CoreInitial migration, 5 requirements: CORE-02/04/11/13/14)
- 01-05-SUMMARY.md (Core runtime services + fluent builder, 6 requirements: CORE-05/10/11/12/13/16)
- 01-06-SUMMARY.md (5 sibling csprojs + CLI + SampleGame, 3 requirements: CORE-05/CORE-13/DIST-01)
- 01-07-SUMMARY.md (Test suite + CI + license-check, 18 requirements verified)
- All NuGet versions verified GA on net10.0 — Npgsql bumped to 10.0.2, Caching.Memory to 10.0.6
- CLAUDE.md updated from stale .NET 9 to verified .NET 10 LTS pins
- 141 tests (130 unit + 11 integration) all green; CI pipeline ready
- AdvisoryLockKey corrected to 1800940027 (live Postgres 17.9 verified)

---
*Initialized: 2026-04-15 at roadmap creation.*
