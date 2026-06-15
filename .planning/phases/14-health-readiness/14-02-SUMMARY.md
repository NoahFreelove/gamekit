---
phase: 14-health-readiness
plan: "02"
subsystem: health-reporters-auth-rankings-lobby
tags: [health, readiness, migrations, reporters, auth, rankings, lobby]
dependency_graph:
  requires:
    - IMigrationReadinessReporter (from plan 14-01)
    - AddGameKitHealthChecks / MigrationAggregateHealthCheck (from plan 14-01)
  provides:
    - AuthMigrationReadinessReporter (queries __ef_migrations_auth, no warning-suppress)
    - RankingsMigrationReadinessReporter (queries __ef_migrations_rankings, with PendingModelChangesWarning suppression)
    - LobbyMigrationReadinessReporter (queries __ef_migrations_lobby, with PendingModelChangesWarning suppression)
  affects:
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs (AddAuth registers AuthMigrationReadinessReporter)
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs (AddRankings registers RankingsMigrationReadinessReporter)
    - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs (AddLobby registers LobbyMigrationReadinessReporter)
tech_stack:
  added: []
  patterns:
    - volatile bool _latched for once-per-lifetime migration readiness (D-07)
    - Build*MigrationContext verbatim from *MigrationHostedService (per-package migration boundary)
    - ConfigureWarnings(PendingModelChangesWarning) in Rankings + Lobby; absent in Auth (variation table)
    - AddSingleton<IMigrationReadinessReporter, TReporter> enumerable registration (D-05)
key_files:
  created:
    - src/GameKit.Auth/Health/AuthMigrationReadinessReporter.cs
    - src/GameKit.Rankings/Health/RankingsMigrationReadinessReporter.cs
    - src/GameKit.Lobby/Health/LobbyMigrationReadinessReporter.cs
  modified:
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs
    - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
decisions:
  - "BuildAuthMigrationContext copied verbatim from AuthMigrationHostedService — ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer> without ConfigureWarnings (Auth snapshot matches model hash)"
  - "BuildRankingsMigrationContext and BuildLobbyMigrationContext copied verbatim including ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)) — hand-authored snapshots do not match EF Core model hash; omitting it would cause GetPendingMigrationsAsync to throw at startup"
  - "All three reporters inject GameKitOptions to select MigrationsConnectionString over ConnectionString (same pattern as MigrationHostedService)"
  - "No new NuGet pins — IMigrationReadinessReporter is GameKit.Core; RelationalEventId is in the already-referenced Microsoft.EntityFrameworkCore.Relational shared framework"
metrics:
  duration: 5 minutes
  completed_date: "2026-06-15"
  tasks: 2
  files: 6
---

# Phase 14 Plan 02: Auth + Rankings + Lobby Migration Readiness Reporters Summary

**One-liner:** Three IMigrationReadinessReporter implementations (Auth, Rankings, Lobby) with per-package history table targeting, latch pattern, and PendingModelChangesWarning suppression in Rankings + Lobby.

## What Was Built

Three new reporter files + three builder modifications providing Auth, Rankings, and Lobby participation in the Core `"migrations"` aggregate health check:

- **`AuthMigrationReadinessReporter`** — `internal sealed` in `GameKit.Auth.Health`; replicates `BuildAuthMigrationContext` from `AuthMigrationHostedService` verbatim (no `ConfigureWarnings` — Auth snapshot matches model hash); `volatile bool _latched` skips Postgres after first all-applied; registered from `AddAuth()` as `AddSingleton<IMigrationReadinessReporter, AuthMigrationReadinessReporter>()`

- **`RankingsMigrationReadinessReporter`** — `internal sealed` in `GameKit.Rankings.Health`; replicates `BuildRankingsMigrationContext` from `RankingsMigrationHostedService` verbatim including `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` (Pitfall 3 — Rankings hand-authored snapshot does not match model hash); `volatile bool _latched`; registered from `AddRankings()`

- **`LobbyMigrationReadinessReporter`** — `internal sealed` in `GameKit.Lobby.Health`; replicates `BuildLobbyMigrationContext` from `LobbyMigrationHostedService` verbatim including `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`; `volatile bool _latched`; registered from `AddLobby()`

- **Three builder modifications**: Each `Add*` extension adds `builder.Services.AddSingleton<IMigrationReadinessReporter, *MigrationReadinessReporter>()` alongside the existing `AddHostedService<*MigrationHostedService>()` registration

## Deviations from Plan

None — plan executed exactly as written.

## Security Review

- T-14-04 (DoS via per-probe Postgres round-trips): Mitigated by `volatile bool _latched` in all three reporters. Once `GetPendingMigrationsAsync()` returns empty, the field is set and all subsequent calls return `true` without touching Postgres.
- T-14-05 (Information Disclosure): Reporters return only `bool`. No table names, schemas, or connection details cross the reporter→aggregate boundary. The aggregate check (Plan 01) renders a count-only description.

## Threat Flags

None.

## Known Stubs

None. All three reporters are fully implemented and functional.

## Commits

| Task | Hash | Description |
|------|------|-------------|
| Task 1 | 6214577 | feat(14-02): add AuthMigrationReadinessReporter and register from AddAuth |
| Task 2 | 7d4f87a | feat(14-02): add RankingsMigrationReadinessReporter and LobbyMigrationReadinessReporter |

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `AuthMigrationReadinessReporter.cs` | FOUND |
| `RankingsMigrationReadinessReporter.cs` | FOUND |
| `LobbyMigrationReadinessReporter.cs` | FOUND |
| `AuthBuilderExtensions.cs` has `AddSingleton<IMigrationReadinessReporter, AuthMigrationReadinessReporter>` | FOUND |
| `RankingsBuilderExtensions.cs` has `AddSingleton<IMigrationReadinessReporter, RankingsMigrationReadinessReporter>` | FOUND |
| `LobbyBuilderExtensions.cs` has `AddSingleton<IMigrationReadinessReporter, LobbyMigrationReadinessReporter>` | FOUND |
| Commit 6214577 | FOUND |
| Commit 7d4f87a | FOUND |
| No new NuGet pins (`Directory.Packages.props` unchanged) | CONFIRMED |
| Auth reporter: 0 `ConfigureWarnings` calls in implementation | CONFIRMED |
| Rankings + Lobby reporters: `PendingModelChangesWarning` suppression present | CONFIRMED |
