---
phase: 11-gamekit-lobby
plan: "02"
subsystem: lobby
tags: [lobby, ef-core, migration, postgres, data-model, advisory-lock]

requires:
  - phase: 11-01
    provides: GameKit.Lobby skeleton, LobbyMigrationConstants.AdvisoryLockKey=12178347L

provides:
  - Lobby entity (Id, OwnerId?, LadderId?, State, MaxMembers, RegionName?, CreatedAt, UpdatedAt, Members nav)
  - LobbyMember entity (Id, LobbyId, PlayerId, Ready, JoinedAt)
  - LobbyState enum (Open=0, ReadyChecking=1, Closed=2, InGame=3) — integer-backed
  - LobbyConfiguration + LobbyMemberConfiguration (EF configurations, internal sealed)
  - LobbyModelBuilderExtension (runtime IModelBuilderExtension)
  - LobbyDesignTimeDbContextFactory (dotnet ef design-time factory)
  - LobbyMigrationModelCustomizer (20-entity ExcludeFromMigrations list)
  - LobbyMigrationHostedService (IHostedService, advisory-lock-serialized migration)
  - 20260522000000_LobbyInitial migration (lobbies + lobby_members ONLY)
  - LobbySchemaTests: 4/4 green (tables exist, history row, no chat table, unique constraint)

affects:
  - 11-03 (Wave 2+: LobbyService, LobbyHub — depends on lobbies + lobby_members schema)
  - Future lobby service tests (IntegrationTestHelpers.ApplyLobbyMigrationsAsync reusable)

tech-stack:
  added: []
  patterns:
    - "LobbyMigrationModelCustomizer: 20 typeof() exclusions — explicit list forces CS0246 if any prior package adds new entity"
    - "LobbyMigrationHostedService.BuildLobbyMigrationContext internal static — reused by test IntegrationTestHelpers"
    - "using-alias workaround (LobbyEntity, LobbyMemberEntity) for GameKit.Lobby namespace/class ambiguity in EF configuration files"
    - "LobbyMember uses CASCADE on both FKs (deviates from PartyMember Restrict on player FK — lobby membership has no audit purpose)"
    - "Integer enum storage: LobbyState configured with IsRequired() only, no HasConversion<string>() (Phase 5 mandatory pattern)"

key-files:
  created:
    - src/GameKit.Lobby/Entities/LobbyState.cs
    - src/GameKit.Lobby/Entities/Lobby.cs
    - src/GameKit.Lobby/Entities/LobbyMember.cs
    - src/GameKit.Lobby/Data/Configurations/LobbyConfiguration.cs
    - src/GameKit.Lobby/Data/Configurations/LobbyMemberConfiguration.cs
    - src/GameKit.Lobby/Data/LobbyModelBuilderExtension.cs
    - src/GameKit.Lobby/Data/LobbyDesignTimeDbContextFactory.cs
    - src/GameKit.Lobby/Data/LobbyMigrationModelCustomizer.cs (in LobbyDesignTimeDbContextFactory.cs)
    - src/GameKit.Lobby/Data/LobbyMigrationHostedService.cs
    - src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs
    - src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.Designer.cs
    - src/GameKit.Lobby/Data/Migrations/GameKitDbContextModelSnapshot.cs
    - tests/GameKit.Lobby.Integration.Tests/IntegrationTestHelpers.cs
    - tests/GameKit.Lobby.Integration.Tests/LobbySchemaTests.cs
  modified: []

key-decisions:
  - "20260522000000_LobbyInitial: deterministic migration timestamp for Lobby (Matchmaking=20260516, Lobby=20260522)"
  - "LobbyMember CASCADE on both FKs (player + lobby): lobby membership is ephemeral with no audit purpose — GDPR player delete should cascade"
  - "Snapshot model: 7 entities (4 Core excluded, 1 Ladder excluded, 2 Lobby active) — Auth/Admin/Matchmaking entities not present because GameKitDbContext.OnModelCreating only runs Core configurations in design-time path"
  - "BuildLobbyMigrationContext marked internal static (not private) to enable reuse in IntegrationTestHelpers without duplicating the options builder"
  - "LobbyConfiguration + LobbyMemberConfiguration use using-alias (LobbyEntity) to resolve GameKit.Lobby namespace/class ambiguity"

requirements-completed: [LOBBY-01, LOBBY-02]

duration: 20min
completed: 2026-06-06
---

# Phase 11 Plan 02: Lobby Data Model + LobbyInitial Migration Summary

**lobbies + lobby_members EF data model, 20-entity LobbyMigrationModelCustomizer, advisory-lock-serialized 20260522000000_LobbyInitial migration, and schema tests confirming tables exist with zero lobby_message% tables (LOBBY-04 anti-feature enforced at DB level)**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-06-06T00:00:00Z
- **Completed:** 2026-06-06T00:20:00Z
- **Tasks:** 3
- **Files modified:** 14

## Accomplishments

- Lobby data model with integer-backed `LobbyState` enum (Open/ReadyChecking/Closed/InGame), `Lobby` entity with SET NULL FKs to players+ladders, `LobbyMember` entity with CASCADE on both FKs for GDPR compliance
- Complete per-package migration infrastructure: design-time factory, 20-entity exclusion customizer (compile-error-on-new-entity), hosted service, deterministic `20260522000000_LobbyInitial` migration creating only `lobbies` + `lobby_members`
- 4/4 LobbySchemaTests green: tables exist, history row recorded, NO `lobby_message%` table (LOBBY-04 anti-feature at schema level), unique constraint enforced (23505)

## Task Commits

1. **Task 1: Entities + EF configurations** — `6cc0090` (feat)
2. **Task 2: Model builder extension, customizer, hosted service, migration** — `ee07d7b` (feat)
3. **Task 3: IntegrationTestHelpers + LobbySchemaTests** — `103a390` (test)

## Files Created/Modified

- `src/GameKit.Lobby/Entities/LobbyState.cs` — Open=0, ReadyChecking=1, Closed=2, InGame=3 (integer, no HasConversion<string>)
- `src/GameKit.Lobby/Entities/Lobby.cs` — Id, OwnerId?, LadderId?, State, MaxMembers, RegionName?, CreatedAt, UpdatedAt, Members nav
- `src/GameKit.Lobby/Entities/LobbyMember.cs` — Id, LobbyId, PlayerId, Ready, JoinedAt
- `src/GameKit.Lobby/Data/Configurations/LobbyConfiguration.cs` — ToTable("lobbies"), integer enum, FK OwnerId SET NULL, FK LadderId SET NULL
- `src/GameKit.Lobby/Data/Configurations/LobbyMemberConfiguration.cs` — ToTable("lobby_members"), unique (LobbyId, PlayerId), CASCADE both FKs
- `src/GameKit.Lobby/Data/LobbyModelBuilderExtension.cs` — IModelBuilderExtension for runtime model contribution
- `src/GameKit.Lobby/Data/LobbyDesignTimeDbContextFactory.cs` — EF CLI design-time factory + LobbyMigrationModelCustomizer (20-entity exclusion list)
- `src/GameKit.Lobby/Data/LobbyMigrationHostedService.cs` — IHostedService; BuildLobbyMigrationContext internal static helper
- `src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs` — creates lobbies + lobby_members; hand-corrected FK principal tables
- `src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.Designer.cs` — migration designer (7-entity model)
- `src/GameKit.Lobby/Data/Migrations/GameKitDbContextModelSnapshot.cs` — model snapshot (4 Core excluded, 1 Ladder excluded, 2 Lobby active)
- `tests/GameKit.Lobby.Integration.Tests/IntegrationTestHelpers.cs` — CreateFreshDatabaseAsync + ApplyLobbyMigrationsAsync (Core→Rankings→Matchmaking→Lobby)
- `tests/GameKit.Lobby.Integration.Tests/LobbySchemaTests.cs` — 4 schema tests (LOBBY-01, LOBBY-02, LOBBY-04)

## Decisions Made

- `LobbyMember` uses `CASCADE` on both FKs (player + lobby). This deviates from `PartyMember` which uses `Restrict` on the player FK. Rationale: lobby membership is ephemeral and has no audit purpose; GDPR player deletion should cascade through to membership rows without requiring application-level cleanup.
- `BuildLobbyMigrationContext` is `internal static` (not `private`) to allow reuse from `IntegrationTestHelpers` in the test project without duplicating the options builder.
- `LobbyDesignTimeDbContextFactory.cs` contains both the factory and the customizer (matches Matchmaking pattern where they coexist in the same file).
- Snapshot model contains 7 entities (4 Core excluded + 1 Ladder excluded + 2 Lobby active). Auth/Admin/Rankings/Matchmaking entities beyond Ladder are NOT in the snapshot — they're not registered in `GameKitDbContext.OnModelCreating` during design-time factory paths (no `IModelBuilderExtension` applied), and the `ExcludeEntity` helper handles null gracefully.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] using-alias workaround for namespace/class ambiguity**
- **Found during:** Task 1 (build after entity creation)
- **Issue:** `LobbyConfiguration` and `LobbyMemberConfiguration` are in namespace `GameKit.Lobby.Data.Configurations`. The identifier `Lobby` is ambiguous — it resolves to both the namespace `GameKit.Lobby` and the class `GameKit.Lobby.Entities.Lobby`. CS0118 compilation error.
- **Fix:** Added `using LobbyEntity = GameKit.Lobby.Entities.Lobby;` and `using LobbyMemberEntity = GameKit.Lobby.Entities.LobbyMember;` aliases. Updated all references and XML doc crefs accordingly.
- **Files modified:** `LobbyConfiguration.cs`, `LobbyMemberConfiguration.cs`
- **Committed in:** 6cc0090 (Task 1 commit)

**2. [Rule 3 - Blocking] Missing `using Microsoft.EntityFrameworkCore.Infrastructure` in IntegrationTestHelpers**
- **Found during:** Task 3 (test project build)
- **Issue:** `IModelCustomizer` not resolved — missing `using` directive.
- **Fix:** Added `using Microsoft.EntityFrameworkCore.Infrastructure;`
- **Files modified:** `tests/GameKit.Lobby.Integration.Tests/IntegrationTestHelpers.cs`
- **Committed in:** 103a390 (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (1 bug, 1 blocking)
**Impact on plan:** Both required for compilation; no scope creep.

## Verification Results

```
dotnet build GameKit.sln -warnaserror → Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test --filter "FullyQualifiedName~LobbySchemaTests"
→ Passed! Failed: 0, Passed: 4, Skipped: 0, Total: 4
  - Migration_Creates_Lobbies_And_LobbyMembers: PASS
  - Migration_Records_LobbyHistory: PASS
  - No_Chat_Message_Table_Exists: PASS (LOBBY-04 anti-feature)
  - LobbyMembers_Unique_Constraint_Enforced: PASS (23505)
```

## Migration Details

- **Migration ID:** `20260522000000_LobbyInitial`
- **Tables created:** `gamekit.lobbies`, `gamekit.lobby_members`
- **History table:** `gamekit.__ef_migrations_lobby`
- **Advisory lock key:** `12178347L` (live-verified in Plan 11-01)
- **NO chat table:** `lobby_message%` pattern returns 0 rows (LOBBY-04 anti-feature proven at schema level)

## Known Stubs

None — this plan creates only schema/migration artifacts. No services or endpoints with data flow are implemented.

## Threat Surface Scan

No new network endpoints, auth paths, or trust-boundary changes introduced. Migration boundary maintained — `lobbies` and `lobby_members` only. The LOBBY-04 anti-feature (no chat persistence) is enforced at the schema level and verified by `No_Chat_Message_Table_Exists` test.

## Next Phase Readiness

- Lobby schema deployed and verified — Wave 2 can implement `ILobbyService`, `LobbyEndpoints`, and `LobbyHub` against the `lobbies` + `lobby_members` tables
- `IntegrationTestHelpers.ApplyLobbyMigrationsAsync` is reusable for all future Lobby integration tests

---
*Phase: 11-gamekit-lobby*
*Completed: 2026-06-06*
