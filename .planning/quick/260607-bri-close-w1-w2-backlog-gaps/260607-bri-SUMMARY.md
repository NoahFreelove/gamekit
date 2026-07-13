---
status: complete
phase: quick-260607-bri
plan: 01
subsystem: GameKit.Lobby + GameKit.Auth
tags: [w1, w2, backlog, lobby, account-merge, fail-fast, lobby-members]
dependency_graph:
  requires: []
  provides: [W-1-resolved, W-2-resolved]
  affects: [GameKit.Lobby, GameKit.Auth, GameKit.Auth.AccountMerge.Integration.Tests, GameKit.Lobby.Integration.Tests]
tech_stack:
  added: []
  patterns: [GetService-null-guard, dedup-then-repoint-sql, raw-ddl-in-tests]
key_files:
  created:
    - tests/GameKit.Lobby.Integration.Tests/RedisRequirementTests.cs
  modified:
    - src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs
    - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
    - src/GameKit.Auth/Services/AccountMergeService.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/TestHelpers.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs
    - .planning/milestones/v2.0-MILESTONE-AUDIT.md
decisions:
  - "W-1 fix: GetService + null-guard is the correct pattern; GetRequiredService leaks the framework exception without context"
  - "W-2: dedup-then-repoint (DELETE dup, then UPDATE remaining) mirrors player_credentials Step 6 precedent; differs from party_members which aborts on conflict because lobby membership is ephemeral with no audit purpose"
  - "Lobby tables in AccountMerge test project created via raw DDL in TestHelpers.ApplyMigrations Step 5 and MergeTestHost.MigrateAsync; no ProjectReference to GameKit.Lobby added"
  - "Rule 1 auto-fix: existing AccountMerge tests (27 of them) started failing with 42P01 because the new Step 11b SQL ran before lobby tables existed; fix: create tables in both migration helpers"
metrics:
  duration: ~40 minutes
  completed: 2026-06-07
  tasks: 3
  files: 7
---

# Phase quick-260607-bri Plan 01: Close W-1/W-2 Backlog Gaps Summary

**One-liner:** Fail-fast Redis guard for AddLobby() and lobby_members re-point with same-lobby dedup in AccountMergeService, closing two v2.0 tech-debt items.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | W-1: Fail-fast clear error when AddLobby() has no IConnectionMultiplexer | `0923f64` | LobbyRedisBackplanePostConfigure.cs, LobbyBuilderExtensions.cs, RedisRequirementTests.cs |
| 2 | W-2: Re-point lobby_members in AccountMergeService | `79596ef` | AccountMergeService.cs, AccountMergeServiceTests.cs |
| 2a | W-2 test infra: ensure lobby tables in TestHelpers.ApplyMigrations | `ec917d8` | TestHelpers.cs |
| 2b | W-2 test infra: ensure lobby tables in MergeTestHost.MigrateAsync | `ce27c8f` | AccountMergeEndpointTests.cs |
| 3 | Docs: mark W-1/W-2 resolved in v2.0 milestone audit | `f6b3ad7` | v2.0-MILESTONE-AUDIT.md |

## Full-Suite Gate Results (project memory)

All three affected-package suites green after all changes:

- **GameKit.Lobby.Integration.Tests:** Passed — 18/18 (includes 3 new W-1 RedisRequirementTests)
- **GameKit.Auth.AccountMerge.Integration.Tests:** Passed — 29/29 (includes 2 new W-2 lobby dedup tests; all 27 existing tests green)
- **GameKit.Auth.Integration.Tests:** Passed — 46/46 (no regressions)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Existing AccountMerge tests broke with 42P01 after W-2 implementation**

- **Found during:** Full-suite gate run for Task 3
- **Issue:** The new Step 11b SQL (`DELETE FROM gamekit.lobby_members ...` and `UPDATE gamekit.lobby_members ...`) executes inside every call to `MergeTransactionBodyAsync`. The 27 existing `AccountMergeServiceTests` call `TestHelpers.ApplyMigrations` which ran Core+Auth+Rankings+Matchmaking migrations but did NOT create the Lobby tables. Similarly, `AccountMergeEndpointTests.MergeTestHost.MigrateAsync` had its own migration sequence without Lobby tables. Result: 4 test failures with `Npgsql.PostgresException: 42P01: relation "gamekit.lobby_members" does not exist`.
- **Fix:** Added Step 5 (raw DDL, IF NOT EXISTS) to both `TestHelpers.ApplyMigrations` and `MergeTestHost.MigrateAsync` creating `gamekit.lobbies` and `gamekit.lobby_members` with the production schema and UNIQUE index. No GameKit.Lobby ProjectReference added.
- **Files modified:** `tests/GameKit.Auth.AccountMerge.Integration.Tests/TestHelpers.cs`, `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs`
- **Commits:** `ec917d8`, `ce27c8f`

## Verification

- `dotnet build src/GameKit.Lobby/GameKit.Lobby.csproj -warnaserror`: clean (0 warnings, 0 errors)
- `dotnet build src/GameKit.Auth/GameKit.Auth.csproj -warnaserror`: clean (0 warnings, 0 errors)
- `grep "GameKit.Lobby" src/GameKit.Auth/GameKit.Auth.csproj`: no match (no ProjectReference added)
- No new EF migration files added under any `*/Migrations/` directory
- All three full suites green (see above)

## Self-Check: PASSED

- LobbyRedisBackplanePostConfigure.cs: FOUND and modified
- LobbyBuilderExtensions.cs: FOUND and modified
- AccountMergeService.cs: FOUND and modified
- RedisRequirementTests.cs: FOUND (new file)
- AccountMergeServiceTests.cs: FOUND and modified
- TestHelpers.cs: FOUND and modified
- AccountMergeEndpointTests.cs: FOUND and modified
- v2.0-MILESTONE-AUDIT.md: FOUND and modified
- Commits `0923f64`, `79596ef`, `ec917d8`, `ce27c8f`, `f6b3ad7`: all verified in git log
