---
phase: 11-gamekit-lobby
plan: "01"
subsystem: lobby
tags: [lobby, signalr, redis, migration, advisory-lock, wave0]
dependency_graph:
  requires: []
  provides: [GameKit.Lobby skeleton, LobbyMigrationConstants, LobbyAdvisoryLockKeyTests]
  affects: [GameKit.sln, Directory.Packages.props]
tech_stack:
  added:
    - "Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8 (CPM, runtime dep)"
    - "Microsoft.AspNetCore.SignalR.Client 10.0.8 (CPM, test-only)"
  patterns:
    - "New NuGet package skeleton mirrors GameKit.Matchmaking (FrameworkReference, CPM, MinVer)"
    - "Per-package advisory-lock live-verify via Testcontainers (placeholder-then-verify pattern)"
    - "tests/Directory.Build.props supplies xUnit/test runner packages globally — do not duplicate in test csproj"
key_files:
  created:
    - src/GameKit.Lobby/GameKit.Lobby.csproj
    - src/GameKit.Lobby/AssemblyInfo.cs
    - src/GameKit.Lobby/LobbyMarker.cs
    - src/GameKit.Lobby/Data/LobbyMigrationConstants.cs
    - tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj
    - tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs
  modified:
    - Directory.Packages.props
    - GameKit.sln
decisions:
  - "LobbyMigrationConstants.AdvisoryLockKey = 12178347L (live-verified on Postgres 17.9 via Testcontainers, SELECT hashtext('gamekit.lobby.migrations')::bigint)"
  - "Duplicate package refs (xunit/Sdk/runner/coverlet) removed from test csproj — supplied globally by tests/Directory.Build.props (same NU1504 pattern as all other test projects)"
  - "LobbyMarker.cs added as minimal compilable entry point for the skeleton csproj"
  - "InternalsVisibleTo grants: Tests, Integration.Tests, OpenApi.Integration.Tests (mirrors Matchmaking pattern)"
metrics:
  duration: "5min"
  completed_date: "2026-06-06"
  tasks: 3
  files: 9
---

# Phase 11 Plan 01: GameKit.Lobby Wave 0 Skeleton + Advisory Lock Gate Summary

GameKit.Lobby package skeleton with CPM-pinned SignalR backplane dep and live-verified advisory-lock key 12178347L pairwise-distinct from all five existing package keys.

## What Was Built

### Task 1: CPM pins + project skeleton + solution entries

Two new CPM pins added to `Directory.Packages.props`:
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 — runtime dep for the Redis backplane
- `Microsoft.AspNetCore.SignalR.Client` 10.0.8 — test-only dep for `HubConnectionBuilder` in Wave 2+ tests

`src/GameKit.Lobby/GameKit.Lobby.csproj` created mirroring `GameKit.Matchmaking.csproj`:
- TargetFramework net10.0 (inherited from Directory.Build.props)
- ProjectReferences: Core, Rankings, Auth, Admin.UI, Matchmaking, GameKit.Build (Analyzer)
- PackageReferences: SignalR.StackExchangeRedis, EF Core stack, Npgsql, FluentValidation, StackExchange.Redis (all CPM, no version attrs)
- FrameworkReference Microsoft.AspNetCore.App

`tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj` created with:
- SignalR.Client, Npgsql, StackExchange.Redis, Mvc.Testing, Testcontainers.PostgreSql/Redis
- ProjectReferences to all 6 src packages + GameKit.TestFixtures
- xUnit/Sdk/runner/coverlet NOT duplicated (supplied by tests/Directory.Build.props)

Both projects added to `GameKit.sln` under src/tests folder nodes with fresh GUIDs.

### Task 2: LobbyMigrationConstants placeholder

`src/GameKit.Lobby/Data/LobbyMigrationConstants.cs` created with:
- `MigrationsHistoryTable = "__ef_migrations_lobby"`
- `AdvisoryLockKey = 0L` (placeholder, documented for live-verify gate)
- Full XML docs on type and both members (CS1591-as-error compliant)
- GPL SPDX header

### Task 3: Live-verify advisory key + distinctness tests GREEN

`tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs` created:
- `[CollectionDefinition("Lobby")]` (PostgresFixture + RedisFixture)
- `[CollectionDefinition("Postgres")]` (PostgresFixture)
- `[CollectionDefinition("Redis")]` (RedisFixture)

`tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs` created with two tests:
- **Test A** `PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` — ran RED (0L != 12178347), confirmed live value **12178347L**, updated constant, re-ran GREEN.
- **Test B** `LobbyKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Matchmaking_Keys` — asserts non-equality by symbolic constant AND integer literal against all five existing keys.

`LobbyMigrationConstants.AdvisoryLockKey` updated from `0L` to `12178347L` with updated XML doc.

## Live-Verified Lobby Advisory Lock Key

**`LobbyMigrationConstants.AdvisoryLockKey = 12178347L`**

- Computed by: `SELECT hashtext('gamekit.lobby.migrations')::bigint` on Postgres 17.9 via Testcontainers
- Pairwise-distinct from:
  - Core: 1800940027
  - Auth: -298890956
  - Admin: -2101739634
  - Rankings: -156812172
  - Matchmaking: 388956820
- SC#1 / OPS-11: SATISFIED

## Verification Results

```
dotnet build GameKit.sln -c Debug → Build succeeded. 0 Warning(s). 0 Error(s).
dotnet test LobbyAdvisoryLockKeyTests → Passed: 2, Failed: 0, Skipped: 0
```

## Commits

| Task | Description | Commit |
|------|-------------|--------|
| 1 | GameKit.Lobby skeleton + test project + CPM pins + solution entries | ac59d91 |
| 2 | LobbyMigrationConstants with placeholder advisory key | 0dff59d |
| 3 | Live-verify Lobby advisory key (12178347L) + SC#1 distinctness tests GREEN | 573445e |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Duplicate PackageReference entries in test csproj**
- **Found during:** Task 3 (build)
- **Issue:** Test csproj explicitly listed xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, and coverlet.collector — which are already injected globally by `tests/Directory.Build.props` via `<PackageReference Include=.../>`. This caused NU1504 "Duplicate PackageReference items found" (WarningsAsErrors).
- **Fix:** Removed the four duplicate entries from the test csproj. Comment added noting they are supplied by Directory.Build.props.
- **Files modified:** `tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj`
- **Commit:** 573445e

## Threat Surface Scan

No new network endpoints, auth paths, or trust-boundary-crossing schema changes introduced in this plan. All files are skeleton/constants/test code. No threat flags.

## Self-Check: PASSED

- [x] `src/GameKit.Lobby/Data/LobbyMigrationConstants.cs` — FOUND
- [x] `tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs` — FOUND
- [x] `tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj` — FOUND
- [x] `Directory.Packages.props` contains SignalR.StackExchangeRedis — FOUND
- [x] Commits ac59d91, 0dff59d, 573445e — all present in git log
- [x] `dotnet build GameKit.sln -c Debug` — 0 errors, 0 warnings
- [x] `LobbyAdvisoryLockKeyTests` — 2/2 passed
- [x] AdvisoryLockKey = 12178347L (no longer 0L)
