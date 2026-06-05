---
phase: 01-foundation-core-migrations-ops-defaults-gpl
plan: 06
subsystem: infra
tags: [csproj, scaffold, cli, spectre-console, sample-app, aspnet-core, dotnet-tool]

# Dependency graph
requires:
  - phase: 01-05
    provides: "IGameKitBuilder, AddGameKit/UseGameKit/MapGameKit pipeline, GameKitOptions, MigrationRunner"
provides:
  - "Five sibling csprojs (Auth, Rankings, Matchmaking, Presence, Admin.UI) with ProjectReference to Core"
  - "GameKit.Cli dotnet tool with functional migrate command and stub admin create"
  - "SampleGame ASP.NET Core 10 boot harness with three-role connection strings"
  - "Spectre.Console.Cli 0.49.1 centrally pinned in Directory.Packages.props"
affects: [01-07, phase-02, phase-03, phase-04, phase-05, phase-06]

# Tech tracking
tech-stack:
  added: [Spectre.Console.Cli 0.49.1]
  patterns: [sibling-csproj-scaffold, dotnet-tool-packaging, sample-app-bootstrap]

key-files:
  created:
    - src/GameKit.Auth/GameKit.Auth.csproj
    - src/GameKit.Rankings/GameKit.Rankings.csproj
    - src/GameKit.Matchmaking/GameKit.Matchmaking.csproj
    - src/GameKit.Presence/GameKit.Presence.csproj
    - src/GameKit.Admin.UI/GameKit.Admin.UI.csproj
    - src/GameKit.Cli/GameKit.Cli.csproj
    - src/GameKit.Cli/Program.cs
    - src/GameKit.Cli/Commands/MigrateCommand.cs
    - src/GameKit.Cli/Commands/AdminCreateCommand.cs
    - samples/SampleGame/SampleGame.csproj
    - samples/SampleGame/Program.cs
    - samples/SampleGame/appsettings.json
    - samples/SampleGame/appsettings.Development.json
  modified:
    - Directory.Packages.props
    - GameKit.sln

key-decisions:
  - "Spectre.Console.Cli 0.49.1 (Apache-2.0) selected for CLI framework per RESEARCH.md OQ#2 resolution"
  - "SampleGame uses Microsoft.NET.Sdk.Web SDK, not Microsoft.NET.Sdk (required for WebApplication.CreateBuilder)"

patterns-established:
  - "Sibling csproj scaffold: Microsoft.NET.Sdk + PackageId/Description/PackageTags/RootNamespace/AssemblyName + ProjectReference to Core + AssemblyInfo.cs SPDX header"
  - "Dotnet tool packaging: PackAsTool=true + ToolCommandName=gamekit + Spectre CommandApp"
  - "SampleGame bootstrap: AddGameKit(opts => {...}).UseGameKit().MapGameKit() with config-driven connection strings"

requirements-completed: [CORE-05, CORE-13, DIST-01]

# Metrics
duration: 5min
completed: 2026-04-16
---

# Phase 01 Plan 06: Sibling Csprojs + CLI + SampleGame Summary

**Five empty sibling package scaffolds, Spectre.Console.Cli-based dotnet tool with functional `gamekit migrate`, and SampleGame ASP.NET Core 10 boot harness wired to three-role docker-compose Postgres**

## Performance

- **Duration:** 5 min
- **Started:** 2026-04-16T15:23:46Z
- **Completed:** 2026-04-16T15:29:08Z
- **Tasks:** 3
- **Files modified:** 20

## Accomplishments
- Five sibling csprojs (Auth, Rankings, Matchmaking, Presence, Admin.UI) scaffolded with ProjectReference to Core and GPL SPDX headers
- GameKit.Cli dotnet tool with functional `gamekit migrate` invoking MigrationRunner.MigrateWithLockAsync and stub `gamekit admin create` (Phase 3)
- SampleGame ASP.NET Core 10 boot harness calling AddGameKit/UseGameKit/MapGameKit with three-role connection strings matching docker-compose
- Spectre.Console.Cli 0.49.1 (Apache-2.0, GPL-compatible) centrally pinned
- Full 9-project solution builds green with -warnaserror (Core + 5 siblings + CLI + SampleGame + Tests)

## Task Commits

Each task was committed atomically:

1. **Task 1: Pin Spectre.Console.Cli + scaffold five sibling csprojs** - `5fc394f` (feat)
2. **Task 2: Scaffold GameKit.Cli dotnet tool** - `1849a87` (feat)
3. **Task 3: Scaffold SampleGame + full-solution build** - `50125cf` (feat)

## Files Created/Modified
- `Directory.Packages.props` - Added Spectre.Console.Cli 0.49.1 central pin
- `GameKit.sln` - Registered 6 new projects (5 siblings + CLI + SampleGame)
- `src/GameKit.Auth/GameKit.Auth.csproj` - Auth sibling scaffold with Core ProjectReference
- `src/GameKit.Auth/AssemblyInfo.cs` - GPL SPDX header
- `src/GameKit.Rankings/GameKit.Rankings.csproj` - Rankings sibling scaffold
- `src/GameKit.Rankings/AssemblyInfo.cs` - GPL SPDX header
- `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` - Matchmaking sibling scaffold
- `src/GameKit.Matchmaking/AssemblyInfo.cs` - GPL SPDX header
- `src/GameKit.Presence/GameKit.Presence.csproj` - Presence sibling scaffold
- `src/GameKit.Presence/AssemblyInfo.cs` - GPL SPDX header
- `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` - Admin.UI sibling scaffold
- `src/GameKit.Admin.UI/AssemblyInfo.cs` - GPL SPDX header
- `src/GameKit.Cli/GameKit.Cli.csproj` - Dotnet tool csproj (PackAsTool + Spectre)
- `src/GameKit.Cli/Program.cs` - Spectre CommandApp entry point
- `src/GameKit.Cli/Commands/MigrateCommand.cs` - Functional migrate command using MigrationRunner
- `src/GameKit.Cli/Commands/AdminCreateCommand.cs` - Phase 3 stub returning exit code 2
- `samples/SampleGame/SampleGame.csproj` - Web SDK, IsPackable=false, Core reference
- `samples/SampleGame/Program.cs` - AddGameKit/UseGameKit/MapGameKit boot sequence
- `samples/SampleGame/appsettings.json` - Production-safe logging defaults
- `samples/SampleGame/appsettings.Development.json` - Three-role connection strings + Redis

## Decisions Made
- Spectre.Console.Cli 0.49.1 (Apache-2.0) selected per RESEARCH.md Open Question #2 resolution -- lightweight, no DI container dependency, command-tree model matches CLI surface
- SampleGame uses `Microsoft.NET.Sdk.Web` SDK (not `Microsoft.NET.Sdk`) -- required for `WebApplication.CreateBuilder` and ASP.NET Core hosting
- Admin.UI uses `Microsoft.NET.Sdk` in Phase 1; Phase 3 may switch to `Microsoft.NET.Sdk.Razor` when Razor Class Library content lands

## Deviations from Plan

None - plan executed exactly as written.

## Known Stubs

| Stub | File | Reason |
|------|------|--------|
| `gamekit admin create` returns exit code 2 | `src/GameKit.Cli/Commands/AdminCreateCommand.cs:17` | Phase 3 deliverable (ADMIN-11); intentional per plan |

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All sibling csprojs ready for Phase 2+ type population (Auth entities, Rankings algorithm, etc.)
- GameKit.Cli `migrate` command ready for Plan 07 integration tests (MigrateCommandTests)
- SampleGame ready for Plan 07 runtime smoke test (CleanInstallMigrationTests)
- Full solution graph proven: Core -> 5 siblings, Core -> CLI, Core -> SampleGame all compile cleanly

---
*Phase: 01-foundation-core-migrations-ops-defaults-gpl*
*Completed: 2026-04-16*
