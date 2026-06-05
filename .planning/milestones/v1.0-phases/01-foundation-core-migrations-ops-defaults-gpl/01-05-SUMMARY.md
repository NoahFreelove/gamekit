---
phase: 01-foundation-core-migrations-ops-defaults-gpl
plan: 05
subsystem: core
tags: [ef-core, postgres, gdpr, rate-limiting, di, aspnetcore, uuid-v7, caching]

# Dependency graph
requires:
  - phase: 01-04
    provides: GameKitDbContext, GameKitModelCustomizer, MigrationRunner, CoreInitial migration, entity configs
provides:
  - GameKitOptions configuration class with connection string separation
  - IClock, IIdGenerator, ICurrentPlayer cross-cutting service interfaces + defaults
  - IPresenceProvider interface (no Phase 1 impl)
  - IGdprDeleteService with hard-delete + SERIALIZABLE transaction + audit-before-delete
  - IPlayerDisplayNameResolver with 5-min sliding cache + configurable tombstone
  - IGameKitRateLimitPolicies with 5 named policy constants
  - PlayerEndpoints GET /api/players paginated endpoint
  - AddGameKit/UseGameKit/MapGameKit fluent builder API
  - IGameKitBuilder interface for sibling package extension
affects: [01-06, 01-07, 02-auth, 03-admin-ui, 04-rankings, 05-matchmaking, 06-presence]

# Tech tracking
tech-stack:
  added: [FrameworkReference Microsoft.AspNetCore.App]
  patterns: [fluent builder (AddGameKit/UseGameKit/MapGameKit), options-pattern, scoped GDPR service, singleton clock/id-generator, memory-cached resolver]

key-files:
  created:
    - src/GameKit.Core/GameKitOptions.cs
    - src/GameKit.Core/Builder/IGameKitBuilder.cs
    - src/GameKit.Core/Builder/GameKitBuilder.cs
    - src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
    - src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs
    - src/GameKit.Core/Services/IClock.cs
    - src/GameKit.Core/Services/SystemClock.cs
    - src/GameKit.Core/Services/IIdGenerator.cs
    - src/GameKit.Core/Services/UuidV7IdGenerator.cs
    - src/GameKit.Core/Services/ICurrentPlayer.cs
    - src/GameKit.Core/Services/HttpContextCurrentPlayer.cs
    - src/GameKit.Core/Services/IPresenceProvider.cs
    - src/GameKit.Core/Services/IGdprDeleteService.cs
    - src/GameKit.Core/Services/GdprDeleteService.cs
    - src/GameKit.Core/Services/PlayerNotFoundException.cs
    - src/GameKit.Core/Services/IPlayerDisplayNameResolver.cs
    - src/GameKit.Core/Services/PlayerDisplayNameResolver.cs
    - src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs
    - src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs
    - src/GameKit.Core/Http/PlayerEndpoints.cs
  modified:
    - src/GameKit.Core/GameKit.Core.csproj

key-decisions:
  - "FrameworkReference Microsoft.AspNetCore.App replaces explicit Microsoft.Extensions.Caching.Memory PackageReference (provided transitively)"
  - "PlayerDisplayNameResolver registered as Scoped (not Singleton) because it depends on scoped GameKitDbContext"
  - "GdprDeleteService unit tests use InMemory provider with custom ModelCustomizer for JsonDocument value converters; full ExecuteDeleteAsync round-trip deferred to Plan 07 Testcontainers integration tests"
  - "IPresenceProvider is interface-only in Core (no default impl) per CORE-10 design"

patterns-established:
  - "Fluent builder: services.AddGameKit(opts => ...) returns IGameKitBuilder; sibling packages extend via .AddAuth() etc."
  - "UseGameKit() applies auto-migrations via MigrationRunner.MigrateWithLockAsync with advisory lock serialization"
  - "MapGameKit() delegates to package-specific endpoint mapping (PlayerEndpoints.MapPlayers)"
  - "Rate-limit policy names as const strings in IGameKitRateLimitPolicies for cross-package attribute references"
  - "Tombstone resolver pattern: IPlayerDisplayNameResolver.Resolve(null) returns configurable deleted-player name"
  - "InMemory test factory (TestDbContextFactory) with JsonDocument value converters for unit tests"

requirements-completed: [CORE-05, CORE-10, CORE-11, CORE-12, CORE-13, CORE-16]

# Metrics
duration: 14min
completed: 2026-04-16
---

# Phase 01 Plan 05: Core Runtime Services + Fluent Builder API Summary

**AddGameKit/UseGameKit/MapGameKit fluent API with GDPR hard-delete service, cross-cutting services (IClock, IIdGenerator, ICurrentPlayer), rate-limit policy constants, and paginated player endpoint**

## Performance

- **Duration:** 14 min
- **Started:** 2026-04-16T15:03:35Z
- **Completed:** 2026-04-16T15:18:18Z
- **Tasks:** 2
- **Files modified:** 21 (20 created, 1 modified)

## Accomplishments
- Full runtime service surface for GameKit.Core: GameKitOptions, IClock/SystemClock, IIdGenerator/UuidV7IdGenerator, ICurrentPlayer/HttpContextCurrentPlayer, IGdprDeleteService/GdprDeleteService, IPlayerDisplayNameResolver/PlayerDisplayNameResolver, IGameKitRateLimitPolicies/GameKitRateLimitPolicies, IPresenceProvider (interface-only)
- AddGameKit fluent builder registers all services with correct lifetimes, configures GameKitDbContext with Npgsql + MigrationsHistoryTable + GameKitModelCustomizer replacement
- UseGameKit auto-migration via MigrationRunner.MigrateWithLockAsync with advisory lock serialization (opt-out via AutoMigrate=false)
- MapGameKit endpoint delegation to PlayerEndpoints GET /api/players with pagination (skip/take, max 200)
- Egress guard Layer 1 verified: zero System.Net.Http references in Core source files
- 124 tests pass including TDD RED/GREEN cycles for both tasks

## Task Commits

Each task was committed atomically (TDD RED then GREEN):

1. **Task 1 RED: Failing tests for Core services** - `002015a` (test)
2. **Task 1 GREEN: Implement Core services** - `22b8086` (feat)
3. **Task 2 RED: Failing tests for builder API** - `c9a95cd` (test)
4. **Task 2 GREEN: Implement builder API** - `dd70321` (feat)

## Services Registered by AddGameKit

| Service | Implementation | Lifetime |
|---------|---------------|----------|
| `GameKitOptions` | (instance) | Singleton |
| `IClock` | `SystemClock` | Singleton |
| `IIdGenerator` | `UuidV7IdGenerator` | Singleton |
| `IHttpContextAccessor` | (framework) | Singleton |
| `ICurrentPlayer` | `HttpContextCurrentPlayer` | Scoped |
| `IMemoryCache` | (framework) | Singleton |
| `IPlayerDisplayNameResolver` | `PlayerDisplayNameResolver` | Scoped |
| `IGdprDeleteService` | `GdprDeleteService` | Scoped |
| `IGameKitRateLimitPolicies` | `GameKitRateLimitPolicies` | Singleton |
| `GameKitDbContext` | (AddDbContext) | Scoped |

## Connection String Model

- `GameKitOptions.ConnectionString` — runtime DML, uses `gamekit_app` role
- `GameKitOptions.MigrationsConnectionString` — optional DDL, uses `gamekit_owner` role; falls back to `ConnectionString` when null

## GDPR Delete Flow

1. Open SERIALIZABLE transaction
2. Snapshot player state (Id, DisplayName, CreatedAt, IsBanned)
3. Write AdminAuditLog row with before-snapshot (action="gdpr.delete")
4. SaveChangesAsync (audit row committed to DB)
5. ExecuteDeleteAsync on Players where Id = playerId
6. Commit transaction
7. FK fan-out: Postgres ON DELETE SET NULL on session_participants, CASCADE on future identities/credentials

## Rate-Limit Policy Constants

| Constant | Value |
|----------|-------|
| `AuthLoginPolicy` | `gamekit:auth:login` |
| `AuthRefreshPolicy` | `gamekit:auth:refresh` |
| `AuthRegisterPolicy` | `gamekit:auth:register` |
| `MmEnqueuePolicy` | `gamekit:mm:enqueue` |
| `PresenceHeartbeatPolicy` | `gamekit:presence:heartbeat` |

## Files Created/Modified
- `src/GameKit.Core/GameKit.Core.csproj` - Added FrameworkReference Microsoft.AspNetCore.App, removed explicit Caching.Memory ref
- `src/GameKit.Core/GameKitOptions.cs` - Configuration options with connection strings, AutoMigrate, DeletedPlayerDisplayName
- `src/GameKit.Core/Builder/IGameKitBuilder.cs` - Fluent builder interface (Services + Options)
- `src/GameKit.Core/Builder/GameKitBuilder.cs` - Internal builder implementation
- `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` - AddGameKit extension method
- `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` - UseGameKit + MapGameKit extension methods
- `src/GameKit.Core/Services/IClock.cs` - Clock abstraction interface
- `src/GameKit.Core/Services/SystemClock.cs` - Default clock (DateTimeOffset.UtcNow)
- `src/GameKit.Core/Services/IIdGenerator.cs` - Id generation abstraction
- `src/GameKit.Core/Services/UuidV7IdGenerator.cs` - Default UUIDv7 generator (Guid.CreateVersion7)
- `src/GameKit.Core/Services/ICurrentPlayer.cs` - Current player accessor interface
- `src/GameKit.Core/Services/HttpContextCurrentPlayer.cs` - HttpContext claim-based player accessor
- `src/GameKit.Core/Services/IPresenceProvider.cs` - Presence interface + PresenceStatus enum (no impl)
- `src/GameKit.Core/Services/IGdprDeleteService.cs` - GDPR delete service interface
- `src/GameKit.Core/Services/GdprDeleteService.cs` - SERIALIZABLE tx hard-delete with audit-before-delete
- `src/GameKit.Core/Services/PlayerNotFoundException.cs` - Exception for missing player operations
- `src/GameKit.Core/Services/IPlayerDisplayNameResolver.cs` - Display name resolver interface
- `src/GameKit.Core/Services/PlayerDisplayNameResolver.cs` - Memory-cached resolver with 5-min sliding expiration
- `src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs` - Rate-limit policy name interface
- `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs` - 5 named policy constants
- `src/GameKit.Core/Http/PlayerEndpoints.cs` - GET /api/players paginated endpoint
- `tests/GameKit.Core.Tests/Services/TestDbContextFactory.cs` - InMemory test helper with JsonDocument converters

## Decisions Made
- **FrameworkReference replaces explicit Caching.Memory**: Adding `FrameworkReference Microsoft.AspNetCore.App` to Core csproj caused NU1510 warning-as-error for the now-redundant `Microsoft.Extensions.Caching.Memory` PackageReference. Removed the explicit ref since it's provided transitively.
- **PlayerDisplayNameResolver as Scoped**: Plan specified Singleton but the resolver depends on GameKitDbContext (Scoped). Registered as Scoped to match DbContext lifetime.
- **InMemory test factory for JsonDocument**: InMemory provider doesn't support jsonb/JsonDocument or ExecuteDeleteAsync. Created TestDbContextFactory with custom ModelCustomizer adding value converters. Full GDPR round-trip tests deferred to Plan 07 integration tests with Testcontainers.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed redundant Microsoft.Extensions.Caching.Memory PackageReference**
- **Found during:** Task 1 (csproj update)
- **Issue:** Adding FrameworkReference Microsoft.AspNetCore.App caused NU1510 error because Caching.Memory is already provided by the shared framework
- **Fix:** Removed explicit PackageReference; type is available via FrameworkReference
- **Files modified:** src/GameKit.Core/GameKit.Core.csproj
- **Verification:** dotnet build -warnaserror exits 0
- **Committed in:** 22b8086 (Task 1 commit)

**2. [Rule 1 - Bug] Fixed InMemory test compatibility for JsonDocument properties**
- **Found during:** Task 1 (test execution)
- **Issue:** InMemory provider cannot handle JsonDocument (jsonb) column types or ExecuteDeleteAsync bulk operations
- **Fix:** Created TestDbContextFactory with custom InMemoryTestModelCustomizer that adds ValueConverters for all JsonDocument properties; restructured GDPR tests to verify audit-before-delete pattern without requiring ExecuteDeleteAsync support
- **Files modified:** tests/GameKit.Core.Tests/Services/TestDbContextFactory.cs, tests/GameKit.Core.Tests/Services/GdprDeleteServiceTests.cs
- **Verification:** All 124 tests pass
- **Committed in:** 22b8086 (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both fixes necessary for correctness. No scope creep. GDPR ExecuteDeleteAsync round-trip test properly deferred to Plan 07 integration tests.

## Issues Encountered
None beyond the auto-fixed deviations above.

## User Setup Required
None - no external service configuration required.

## Pending for Plan 07

- Five empty sibling csprojs (Auth, Rankings, Matchmaking, Presence, Admin.UI)
- GameKit.Cli (migrate + admin-create stub) with Spectre.Console.Cli
- SampleGame boot harness with AddGameKit().UseGameKit().MapGameKit()
- Runtime ConnectCallback egress guard (Layer 2)
- CI wiring and full integration test suite
- CLAUDE.md stack-table update

## Next Phase Readiness
- Core runtime surface complete: AddGameKit/UseGameKit/MapGameKit pipeline is the canonical integration API
- All cross-cutting services (clock, id-gen, current-player, GDPR, resolver, rate-limit policies) ready for sibling packages
- Plan 06 (CI + license checks) and Plan 07 (siblings + CLI + SampleGame + integration tests) can proceed
- Egress guard Layer 1 verified (zero System.Net.Http in Core); Layer 2 runtime test lands in Plan 07

## Self-Check: PASSED

All 20 created files verified present. All 4 task commits (002015a, 22b8086, c9a95cd, dd70321) verified in git log.

---
*Phase: 01-foundation-core-migrations-ops-defaults-gpl*
*Plan: 05*
*Completed: 2026-04-16*
