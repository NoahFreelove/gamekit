---
phase: 21-final-demo-3d-multiplayer-platformer
plan: "01"
subsystem: platformer3d-scaffold
status: complete
tags: [scaffold, csproj, testcontainers, spdx, reuse, solution]
dependency_graph:
  requires: []
  provides:
    - samples/Platformer3D/Platformer3D.csproj
    - samples/Platformer3D.GameServer/Platformer3D.GameServer.csproj
    - tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj
    - tests/GameKit.Platformer3D.Integration.Tests/GameKit.Platformer3D.Integration.Tests.csproj
    - tests/GameKit.Platformer3D.Integration.Tests/PlatformerIntegrationFixture.cs
    - LICENSES/GPL-3.0-or-later.txt
    - LICENSES/BSD-3-Clause.txt
  affects:
    - GameKit.sln (append-only: 4 new Project entries)
tech_stack:
  added:
    - reuse 6.2.0 (FSFE REUSE CLI, host tooling — pip install)
    - LICENSES/GPL-3.0-or-later.txt (REUSE compliance)
    - LICENSES/BSD-3-Clause.txt (REUSE compliance)
  patterns:
    - Microsoft.NET.Sdk.Web host project shell (mirrors TicTacToeDuel.csproj)
    - Microsoft.NET.Sdk class library (D-13 embedded GameServer pattern)
    - Testcontainers integration fixture with 5-package migration chain
    - xUnit Skip-marked scaffold (Nyquist sampling continuity)
key_files:
  created:
    - samples/Platformer3D/Platformer3D.csproj
    - samples/Platformer3D/Program.cs
    - samples/Platformer3D.GameServer/Platformer3D.GameServer.csproj
    - samples/Platformer3D.GameServer/GameServerPlaceholder.cs
    - tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj
    - tests/GameKit.Platformer3D.Tests/Strategy/BestTimeMatchmakingStrategyTests.cs
    - tests/GameKit.Platformer3D.Tests/Rankings/TimeMarginRankingAlgorithmTests.cs
    - tests/GameKit.Platformer3D.Integration.Tests/GameKit.Platformer3D.Integration.Tests.csproj
    - tests/GameKit.Platformer3D.Integration.Tests/PlatformerIntegrationFixture.cs
    - LICENSES/GPL-3.0-or-later.txt
    - LICENSES/BSD-3-Clause.txt
  modified:
    - GameKit.sln (append-only: 4 Project entries + GlobalSection configs + NestedProjects)
decisions:
  - "D-13: GameServer embedded as IHostedService class library, not a standalone process like TicTacToeDuel.GameServer"
  - "D-15: All sln edits append-only near existing TicTacToeDuel entries to minimize merge conflict with concurrent worktrees"
  - "Downloaded GPL-3.0-or-later.txt + BSD-3-Clause.txt via `reuse download --all` (pre-existing gap in repo)"
  - "Migration chain order: Core→Auth→Rankings→Matchmaking→Lobby (dependency order; all 5 required for Platformer3D demo)"
  - "GUIDs: Platformer3D={958F328C-4802-406D-8479-9DA85B853CA9}, GameServer={ACA8B36A-E12B-4334-9D7B-B4D13BAAD25B}, Tests={72CBCB17-C797-41E0-B3AC-9FF742BCB230}, IntTests={2E5737A9-8250-4144-994E-49BBE95DC7A4}"
metrics:
  duration: 10min
  completed: "2026-06-23"
  tasks_completed: 3
  files_created: 11
  files_modified: 1
---

# Phase 21 Plan 01: Project Scaffold Summary

**One-liner:** Platformer3D two-project sample shell + two test projects + Testcontainers 5-package migration fixture + Nyquist Skip scaffolds + LICENSES/ REUSE compliance — all wired into GameKit.sln append-only (D-15).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Install reuse CLI for R11 license-lint gate | (host tooling, no commit) | — |
| 2 | Create two sample project shells and wire GameKit.sln | 6992a1c | 5 files |
| 3 | Create two test projects, Testcontainers fixture, Nyquist scaffolds | aa4a7cd | 7 files |

## What Was Built

### Task 1: reuse CLI
Installed `reuse 6.2.0` via `pip install reuse --break-system-packages` (dev environment; pipx was unavailable). The CLI resolves at `/home/noah/.local/bin/reuse`.

### Task 2: Sample Project Shells
- `samples/Platformer3D/Platformer3D.csproj`: `Microsoft.NET.Sdk.Web` host shell, `IsPackable=false`, `RootNamespace=Platformer3D`. ProjectRefs: all 8 GameKit packages (Core, Auth, Rankings, Matchmaking, Lobby, Presence, OpenApi, Admin.UI) + Platformer3D.GameServer + 3 OTel PackageRefs (mirrors TicTacToeDuel pattern exactly, D-15).
- `samples/Platformer3D/Program.cs`: Wave 1 stub with GPL-3.0-or-later SPDX header.
- `samples/Platformer3D.GameServer/Platformer3D.GameServer.csproj`: `Microsoft.NET.Sdk` class library, `IsPackable=false`, `RootNamespace=Platformer3D.GameServer`. ProjectRefs: GameKit.Core + GameKit.Rankings only (D-13 embedded game server).
- `samples/Platformer3D.GameServer/GameServerPlaceholder.cs`: Wave 1 stub with SPDX header (placeholder until Plan 21-03 writes GameServerService.cs).
- `GameKit.sln`: Appended 4 new Project entries + GlobalSection(ProjectConfigurationPlatforms) Debug/Release/x64/x86 + GlobalSection(NestedProjects) entries. All append-only (D-15).

**GUIDs assigned:**
| Project | GUID |
|---------|------|
| Platformer3D (host) | `{958F328C-4802-406D-8479-9DA85B853CA9}` |
| Platformer3D.GameServer | `{ACA8B36A-E12B-4334-9D7B-B4D13BAAD25B}` |
| GameKit.Platformer3D.Tests | `{72CBCB17-C797-41E0-B3AC-9FF742BCB230}` |
| GameKit.Platformer3D.Integration.Tests | `{2E5737A9-8250-4144-994E-49BBE95DC7A4}` |

### Task 3: Test Projects, Fixture, and Nyquist Scaffolds
- `tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj`: xUnit + Moq unit test project. References Platformer3D host + GameKit.Matchmaking + GameKit.Rankings (ready for BestTimeMatchmakingStrategy and TimeMarginRankingAlgorithm once written in 21-02).
- `tests/GameKit.Platformer3D.Tests/Strategy/BestTimeMatchmakingStrategyTests.cs`: 2 Skip-marked scaffolds (`BestTimeMatchmakingStrategyResolutionTests`, `BestTimeMatchmakingStrategyMatchTests`).
- `tests/GameKit.Platformer3D.Tests/Rankings/TimeMarginRankingAlgorithmTests.cs`: 2 Skip-marked scaffolds (`TimeMarginRankingAlgorithm_WinLossDelta`, `TimeMarginRankingAlgorithm_DrawEdge`).
- `tests/GameKit.Platformer3D.Integration.Tests/GameKit.Platformer3D.Integration.Tests.csproj`: xUnit + Testcontainers.PostgreSql + Testcontainers.Redis + GameKit.TestFixtures integration project.
- `tests/GameKit.Platformer3D.Integration.Tests/PlatformerIntegrationFixture.cs`: `ApplyPlatformerMigrationsAsync` chains Core → Auth (-298890956) → Rankings (-156812172) → Matchmaking (388956820) → Lobby (12178347) migrations in dependency order.
- `LICENSES/GPL-3.0-or-later.txt` + `LICENSES/BSD-3-Clause.txt`: Downloaded via `reuse download --all` (pre-existing REUSE compliance gap — no LICENSES/ directory existed in the repo).

**Migration chain wired into `ApplyPlatformerMigrationsAsync`** (21-06 dependency):
1. Core — `GameKitDbContext` + `AddGameKit()` (advisory lock key 1800940027)
2. Auth — `AuthMigrationConstants.AdvisoryLockKey` = -298890956, `MigrationsHistoryTable` = `__ef_migrations_auth`
3. Rankings — `RankingsMigrationConstants.AdvisoryLockKey` = -156812172, `MigrationsHistoryTable` = `__ef_migrations_rankings`
4. Matchmaking — `MatchmakingMigrationConstants.AdvisoryLockKey` = 388956820, `MigrationsHistoryTable` = `__ef_migrations_matchmaking`
5. Lobby — `LobbyMigrationConstants.AdvisoryLockKey` = 12178347, `MigrationsHistoryTable` = `__ef_migrations_lobby`

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build samples/Platformer3D` | PASS (0 errors) |
| `dotnet build samples/TicTacToeDuel` | PASS (0 errors, unchanged) |
| `dotnet test tests/GameKit.Platformer3D.Tests` | PASS (4 skipped, 0 failed) |
| `dotnet build tests/GameKit.Platformer3D.Integration.Tests` | PASS (0 errors) |
| `command -v reuse` | PASS (/home/noah/.local/bin/reuse) |
| `grep -c Platformer3D GameKit.sln` | PASS (4 entries) |
| `git diff --name-only samples/TicTacToeDuel/` | PASS (empty — no changes) |
| `git diff --name-only src/` | PASS (empty — no package modifications) |
| All new .cs files carry SPDX header | PASS (grep -l verified) |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Duplicate package references from tests/Directory.Build.props**
- **Found during:** Task 3 first build attempt
- **Issue:** `tests/Directory.Build.props` auto-adds `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector` to all test projects. Explicitly declaring them again in the new csproj files caused NU1504 (duplicate PackageReference).
- **Fix:** Removed the 4 auto-included packages from both test csproj files; added a comment explaining why they are absent.
- **Files modified:** Both test `.csproj` files.

**2. [Rule 2 - Missing critical functionality] Pre-existing REUSE compliance gap**
- **Found during:** Task 3 `reuse lint` verification
- **Issue:** The repo had no `LICENSES/` directory with actual license text files, causing `reuse lint` to exit 1 with "Missing licenses: BSD-3-Clause, GPL-3.0-or-later". This pre-dates Phase 21 but blocks the R11 gate.
- **Fix:** Ran `reuse download --all` to download both license text files into `LICENSES/`. The new `LICENSES/GPL-3.0-or-later.txt` and `LICENSES/BSD-3-Clause.txt` are now committed. All new `.cs` files carry their own SPDX headers.
- **Remaining pre-existing reuse issues:** `reuse lint` still exits 1 due to (a) 824 files without copyright headers (mostly `bin/`/`obj/` build artifacts not covered by `.reuse/ignorefile`) and (b) 27 invalid SPDX expressions in planning markdown docs where SPDX identifiers appear in documentation text. Neither issue involves the new Platformer3D scaffold files — none of our new source files appear in the `reuse lint` violations. These are pre-Phase-21 issues to be tracked separately.
- **Files committed:** `LICENSES/GPL-3.0-or-later.txt`, `LICENSES/BSD-3-Clause.txt`

## Known Stubs

| Stub | File | Line | Reason |
|------|------|------|--------|
| `Program.cs` one-liner | `samples/Platformer3D/Program.cs` | 9 | Wave 1 shell — real startup code in Plan 21-04 |
| `GameServerPlaceholder.cs` | `samples/Platformer3D.GameServer/GameServerPlaceholder.cs` | all | D-13 IHostedService implementation in Plan 21-03 |
| `BestTimeMatchmakingStrategyTests` | `tests/...Strategy/BestTimeMatchmakingStrategyTests.cs` | all | Skip-marked — filled in Plan 21-02 |
| `TimeMarginRankingAlgorithmTests` | `tests/...Rankings/TimeMarginRankingAlgorithmTests.cs` | all | Skip-marked — filled in Plan 21-02 |

These stubs are intentional Wave 1 scaffolds — the plan explicitly calls for them and later plans fill them in.

## Threat Surface Scan

No new security-relevant surface introduced in this plan. All changes are:
- Project scaffolding (`.csproj` files, solution entries)
- Wave 1 stubs with no real functionality
- Testcontainers migration fixture (test-only, no runtime path)
- SPDX license text files

## Self-Check: PASSED

All 11 created files exist on disk. Both task commits (6992a1c, aa4a7cd) present in git log.

| Check | Result |
|-------|--------|
| All 11 new files found | PASSED |
| Commit 6992a1c (Task 2) | FOUND |
| Commit aa4a7cd (Task 3) | FOUND |
| No deletions in Task 3 commit | PASSED |
| No untracked files after commit | PASSED |
