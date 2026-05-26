---
phase: 06-presence-openapi-distribution
plan: 08
subsystem: distribution
tags: [game-server, distribution, reader-role, release-train, nuspec-exact-pin, postgres-roles, msbuild-targets, reflection-emit]

# Dependency graph
requires:
  - phase: 06-presence-openapi-distribution
    provides: PostgresFixture 3-role bootstrap (Plan 01), GameKitVersionAssertionHostedService at index 0 (Plan 06-02), DistributionIntegrationFixture (Plan 06-03), GameKit.Build source generator + GameKit.targets ItemDefinitionGroup (Plan 06-01), Sessions /start /complete /abandon (Plan 06-05)
provides:
  - "Production 2-process topology demonstration: TicTacToeDuel.GameServer console app reading via gamekit_reader + HTTP-calling the web tier (D-13)"
  - "scripts/run-game-server.sh launcher (mirrors scripts/run-sample.sh)"
  - "DIST-02 empirical: gamekit_reader denied INSERT (Postgres SQLSTATE 42501) on gamekit.game_sessions"
  - "OPS-04 empirical: all 7 GameKit src packages stamp the SAME MinVer version via Internal.GameKitMarker.GameKitVersion reflection"
  - "OPS-05 empirical: GameKitVersionMismatchException fires at IHost.StartAsync when a synthetic Reflection.Emit assembly reports a divergent version"
  - "OPS-06 extension: clean-install migrations apply with no drift across Core + Auth + Rankings + Matchmaking + Admin.UI (Presence + OpenApi correctly excluded — no EF migrations)"
  - "D-26 primary defense: produced .nuspec sibling GameKit.* deps carry literal [X.Y.Z] exact-pin square brackets; source-side csproj wildcard guard"
  - "Working GameKit.targets sibling-ref bracket injection (fixes silent no-op in Plan 06-01's ItemDefinitionGroup approach)"
affects: [06-09 (template package clones TicTacToeDuel + TicTacToeDuel.GameServer; consumes the proven release-train invariants), v1 release tagging]

# Tech tracking
tech-stack:
  added:
    - "Microsoft.Extensions.Http 10.0.6 (CPM pin — IHttpClientFactory for the GameServer sample; required transitive floor from M.E.Http.Resilience 10.5.0)"
  patterns:
    - "Production 2-process topology: web tier (gamekit_owner — writes) + game-server tier (gamekit_reader — reads); cross-tier orchestration via HTTP + service-account JWT"
    - "Reflection-emit synthetic assembly pattern for runtime invariant tests (collectible AssemblyBuilderAccess.RunAndCollect)"
    - "MSBuild hook target rewriting @(_ProjectReferencesWithVersions) AfterTargets=_GetProjectReferenceVersions BeforeTargets=GenerateNuspec to stamp [X.Y.Z] exact-pin into produced .nuspec"
    - "Per-test isolated Testcontainers Postgres for clean-install assertions (avoids shared-fixture pollution)"
    - "EF Core model-cache-aware test boundaries (DIST-02 uses raw DDL to avoid poisoning composite-model tests in the same xUnit process)"

key-files:
  created:
    - "samples/TicTacToeDuel.GameServer/TicTacToeDuel.GameServer.csproj — Microsoft.NET.Sdk, OutputType=Exe, net10.0, NO GameKit.* ProjectRef"
    - "samples/TicTacToeDuel.GameServer/Program.cs — top-level statements: Npgsql SELECT via gamekit_reader, HttpClient GET /openapi/v1.json, optional POST /api/sessions/{id}/start"
    - "samples/TicTacToeDuel.GameServer/appsettings.json + appsettings.Development.json — gamekit_reader connection string per docker/postgres/init/01-roles.sql"
    - "samples/TicTacToeDuel.GameServer/README.md — topology + Postgres role separation + ops-docs pointer"
    - "scripts/run-game-server.sh — convenience launcher"
    - "tests/GameKit.Distribution.Integration.Tests/DIST02_GamekitReaderInsertDeniedTests.cs — 2 tests"
    - "tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs — 2 tests"
    - "tests/GameKit.Distribution.Integration.Tests/OPS05_VersionMismatchAssertionThrowsTests.cs — 1 test (Reflection.Emit synthetic)"
    - "tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs — 1 test (isolated Postgres container)"
    - "tests/GameKit.Distribution.Integration.Tests/D26_NuspecExactPinGuardTests.cs — 2 tests (dotnet pack + .nuspec parse + source csproj grep)"
  modified:
    - "Directory.Packages.props — added Microsoft.Extensions.Http 10.0.6 CPM pin"
    - "GameKit.targets — replaced silently-ignored ItemDefinitionGroup with working _ApplyExactPinToSiblingGameKitReferences hook target (Plan 06-01 D-17 implementation fix)"
    - "GameKit.sln — added samples/TicTacToeDuel.GameServer/TicTacToeDuel.GameServer.csproj"

key-decisions:
  - "Plan placeholder '.AddAdminUi()' resolved to actual extension '.AddGameKitAdmin()' per src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs (Rule 3 - name resolution)"
  - "OPS-06 uses WebApplication.CreateBuilder (web host) rather than Host.CreateApplicationBuilder (generic host) because AddGameKitAdmin requires IWebHostEnvironment (RazorComponents + MudBlazor)"
  - "OPS-06 runs in Development environment to bypass SuperadminGateHostedService (T-03-06-05); admin bootstrap is out of scope"
  - "OPS-06 uses its OWN per-test isolated Postgres container (separate from shared PostgresFixture) so DIST-02 seed data + Core schema bootstrap don't leak"
  - "OPS-06 removes GameKitVersionAssertionHostedService from its DI container via string-based descriptor lookup (defends against OPS-05's collectible-assembly residual)"
  - "DIST-02 schema bootstrap uses raw DDL (CREATE TABLE IF NOT EXISTS) NOT EF migration — calling AddGameKit would cache the Core-only model process-wide and poison OPS-06's composite DbContext (StartupLadderUpserter calling ctx.Set<Ladder>())"
  - "GameKit.targets ItemDefinitionGroup approach is silently ignored by NuGet 10's pack pipeline; replaced with hook target that rewrites @(_ProjectReferencesWithVersions) (Rule 1 bug fix)"

patterns-established:
  - "Two-process game topology: web tier owns DB writes via gamekit_owner; game-server tier reads via gamekit_reader + HTTP-orchestrates session lifecycle via service-account JWT (D-13)"
  - "Reflection.Emit + AssemblyBuilderAccess.RunAndCollect + GC tickle loop for runtime-invariant tests that need synthetic GameKit.* assemblies"
  - "Per-test Testcontainers isolation for migration-coverage assertions"
  - "MSBuild hook AfterTargets=_GetProjectReferenceVersions BeforeTargets=GenerateNuspec for NuGet sibling-ref metadata rewriting"
  - "String-based ImplementationType.FullName descriptor lookup to remove internal Core hosted services from test DI containers without InternalsVisibleTo"

requirements-completed: [DIST-02, DIST-03, OPS-04, OPS-05]

# Metrics
duration: ~92 min
completed: 2026-05-26
---

# Phase 6 Plan 08: GameServer Console + Release-Train Empirical Tests Summary

**TicTacToeDuel.GameServer console proves the 2-process production topology + 5 new integration tests prove the Postgres 3-role bootstrap, MinVer release train, version-mismatch assertion, clean-install migrations, and pack-time exact-pin defense — with a real D-17/D-26 implementation bug fixed inline in GameKit.targets.**

## Performance

- **Duration:** ~92 min
- **Started:** 2026-05-26T03:21:53Z
- **Completed:** 2026-05-26T04:53:36Z
- **Tasks:** 4 (all completed)
- **Files created:** 11
- **Files modified:** 3 (Directory.Packages.props, GameKit.targets, GameKit.sln)
- **Test results:** 9/9 passed in Distribution.Integration suite (smoke x1 + DIST-02 x2 + OPS-04 x2 + OPS-05 x1 + OPS-06 x1 + D-26 x2); full solution build green

## Accomplishments

- **D-13 production topology demonstration:** new `samples/TicTacToeDuel.GameServer/` console app (Microsoft.NET.Sdk, OutputType=Exe, net10.0) connects to Postgres as `gamekit_reader` via Npgsql, fetches `/openapi/v1.json` via HttpClient, and optionally POSTs `/api/sessions/{id}/start` with a service-account JWT. NO ProjectRef to GameKit.* runtime packages — the game-server tier is an outside consumer of the web API surface, matching real production where the game binary is independent of the web tier's assembly graph.
- **DIST-02 empirical (Postgres role enforcement):** `gamekit_reader` can SELECT on `gamekit.players` but raises Postgres SQLSTATE `42501` ("insufficient_privilege") when attempting INSERT on `gamekit.game_sessions`. The default-privileges grants in `docker/postgres/init/01-roles.sql` are now in-CI-enforced.
- **OPS-04 empirical (release train uniformity):** all 7 GameKit src packages (Core, Auth, Rankings, Matchmaking, Admin.UI, Presence, OpenApi) stamp the SAME `Internal.GameKitMarker.GameKitVersion` constant. Captured value on current main: **`"1.0.0"`**.
- **OPS-05 empirical (version-mismatch assertion):** `GameKitVersionAssertionHostedService` throws `GameKitVersionMismatchException` at `IHost.StartAsync` when a `GameKit.SyntheticTest` assembly (built via Reflection.Emit with `GameKitVersion = "99.99.99"`) is loaded into the AppDomain alongside the real Core stamp. Validates the hosted-service-at-index-0 invariant (PATTERNS warning #2) — the assertion fires BEFORE any migration hosted service touches the DB. Captured exception `Message` format: `"GameKit version mismatch detected across loaded assemblies: <name1>=<ver1>, <name2>=<ver2>, ... . All GameKit.* packages must be pinned to the same version (see MSBuild pack-time exact-pin enforcement in GameKit.targets, D-17)."`
- **OPS-06 extension (clean install + no drift):** boots a `WebApplication` host with `AddGameKit().AddAuth().AddRankings().AddLadder().AddMatchmaking().AddLadder().AddGameKitAdmin()` against a fresh isolated Testcontainers Postgres. Asserts every `__ef_migrations_{core,auth,rankings,matchmaking,admin}` history table exists and Core's `GetPendingMigrationsAsync` returns empty. Presence + OpenApi are correctly excluded (no EF migrations).
- **D-26 primary defense (pack-time exact-pin) — IMPLEMENTATION BUG FIXED:** the original `GameKit.targets` shipped by Plan 06-01 used an `ItemDefinitionGroup`/`PackageVersion` metadata approach that NuGet 10's pack pipeline silently ignores. Verified empirically: pre-fix produced `.nuspec`s contained `<dependency id="GameKit.Core" version="0.0.0-alpha.0.131" />` (NO brackets). Replaced with a working hook target `_ApplyExactPinToSiblingGameKitReferences` (AfterTargets=`_GetProjectReferenceVersions`, BeforeTargets=`GenerateNuspec`) that rewrites `@(_ProjectReferencesWithVersions)` in place. Empirical proof from post-fix `dotnet pack src/GameKit.Auth/`: `<dependency id="GameKit.Core" version="[0.0.0-alpha.0.131]" exclude="Build,Analyzers" />` — exact-pin square brackets correctly applied.

## Task Commits

Each task was committed atomically:

1. **Task 1: TicTacToeDuel.GameServer console app + run-game-server.sh** — `1e69269` (feat)
2. **Task 2: DIST-02 gamekit_reader INSERT denied (SQLSTATE 42501)** — `b723210` (test)
3. **Task 3: OPS-04 + OPS-05 + OPS-06 release-train + clean-install assertions** — `37ee5fb` (test)
4. **Task 4: D-26 nuspec exact-pin guard + GameKit.targets implementation fix** — `4b21017` (test)

## Files Created/Modified

### Created

- `samples/TicTacToeDuel.GameServer/TicTacToeDuel.GameServer.csproj` — console-app csproj (Exe, net10.0, NO GameKit.* refs).
- `samples/TicTacToeDuel.GameServer/Program.cs` — top-level statements: Npgsql SELECT + HttpClient GET + optional POST.
- `samples/TicTacToeDuel.GameServer/appsettings.json` + `appsettings.Development.json` — `gamekit_reader`/`gamekit_reader_dev` connection string + WebApi base URL.
- `samples/TicTacToeDuel.GameServer/README.md` — topology documentation, role-separation notes, ops-doc pointer for production credential rotation.
- `scripts/run-game-server.sh` — convenience launcher mirroring `scripts/run-sample.sh`.
- `tests/GameKit.Distribution.Integration.Tests/DIST02_GamekitReaderInsertDeniedTests.cs` — two tests (Reader_CanSelect + Reader_InsertOnGameSessions_IsDeniedWith42501).
- `tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs` — two tests (marker presence + uniform stamp).
- `tests/GameKit.Distribution.Integration.Tests/OPS05_VersionMismatchAssertionThrowsTests.cs` — synthetic-assembly version-mismatch test via Reflection.Emit + collectible AssemblyBuilder.
- `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs` — clean-install host + isolated Postgres container.
- `tests/GameKit.Distribution.Integration.Tests/D26_NuspecExactPinGuardTests.cs` — two tests (produced .nuspec exact-pin assertion via `dotnet pack` + .nupkg/.nuspec parse; source csproj wildcard grep).

### Modified

- `Directory.Packages.props` — added `Microsoft.Extensions.Http 10.0.6` CPM pin (Rule 3 — IHttpClientFactory dependency for the GameServer sample; 10.0.6 floor mandated by transitive M.E.Http.Diagnostics).
- `GameKit.targets` — REPLACED the silently-no-op `ItemDefinitionGroup`/`PackageVersion` approach with a working `_ApplyExactPinToSiblingGameKitReferences` hook target (Rule 1 — D-17/D-26 implementation bug fix).
- `GameKit.sln` — added the new `samples/TicTacToeDuel.GameServer/TicTacToeDuel.GameServer.csproj`.

## Decisions Made

- **`.AddGameKitAdmin()` not `.AddAdminUi()`** — plan wording uses the placeholder name; verified actual extension is `AddGameKitAdmin` in `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`.
- **OPS-06 uses `WebApplication.CreateBuilder` (web host)** instead of `Host.CreateApplicationBuilder` (generic host) — `AddGameKitAdmin` registers `RazorComponents` + MudBlazor services that need `IWebHostEnvironment`, which only the web host provides. No actual HTTP server starts — only `StartAsync` drives the migration chain.
- **OPS-06 runs in `Environments.Development`** — bypasses `SuperadminGateHostedService` (T-03-06-05) which otherwise throws on empty `admin_users` in Production. Admin bootstrap is out of OPS-06's scope.
- **OPS-06 uses its OWN isolated Postgres container** (not the shared `DistributionIntegrationFixture` Postgres) — DIST-02's owner-side seed row + the `Core` schema dance would leak into the migration apply chain and produce false-positive failures.
- **OPS-06 removes `GameKitVersionAssertionHostedService` from its DI container via string-based descriptor lookup** — Core's assertion type is `internal` (no `InternalsVisibleTo` to Distribution.Integration.Tests); the lookup uses `ImplementationType?.FullName == "GameKit.Core.Hosting.GameKitVersionAssertionHostedService"`. Required because OPS-05's collectible `AssemblyBuilder` may not GC-collect before OPS-06 runs in the same xUnit process; the lingering synthetic `GameKit.SyntheticTest=99.99.99` marker would false-fail OPS-06's host startup.
- **DIST-02 schema bootstrap uses raw `CREATE TABLE IF NOT EXISTS` DDL** instead of EF Core's `MigrationRunner` — invoking `AddGameKit` here would build + cache an EF model in the process-wide model cache (no `IModelCacheKeyFactory` override in the repo), poisoning later tests in the same xUnit assembly that expect the composite (Core + Auth + Rankings + ...) model on the same context type. Documented in the test class's XML doc on `EnsureCoreTablesExistAsync`.
- **GameKit.targets uses a hook target, not an `ItemDefinitionGroup`** — verified empirically that NuGet 10's pack pipeline reads sibling-dep versions from `$(PackageVersion)` of the referenced project verbatim via `_GetProjectVersion`, bypassing `ProjectReference` item metadata entirely. The only working mechanism is to rewrite `@(_ProjectReferencesWithVersions)` AFTER `_GetProjectReferenceVersions` populates it but BEFORE `Pack` invokes `PackTask`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing Microsoft.Extensions.Http CPM pin**
- **Found during:** Task 1 (`samples/TicTacToeDuel.GameServer/Program.cs` build).
- **Issue:** `services.AddHttpClient(...)` and `IHttpClientFactory` failed to resolve — `Microsoft.Extensions.Http` was not in `Directory.Packages.props` (only `Microsoft.Extensions.Http.Resilience` was).
- **Fix:** Added `<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.0" />` to CPM. Later bumped to `10.0.6` (see deviation #5).
- **Files modified:** `Directory.Packages.props`.
- **Verification:** GameServer csproj rebuilds clean (0 warnings, 0 errors).
- **Committed in:** `1e69269` (Task 1 commit).

**2. [Rule 3 - Blocking] Plan placeholder `.AddAdminUi()` doesn't exist**
- **Found during:** Task 3 (OPS-06 first build).
- **Issue:** The plan repeatedly references `.AddAdminUi()` in the AddGameKit chain; the actual extension is `.AddGameKitAdmin()`.
- **Fix:** Used real name `.AddGameKitAdmin()` per `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:40`.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs`.
- **Verification:** OPS-06 build green; test passes.
- **Committed in:** `37ee5fb` (Task 3 commit).

**3. [Rule 3 - Blocking] OPS-06 missing `IConnectionMultiplexer` registration**
- **Found during:** Task 3 (first OPS-06 run).
- **Issue:** Host fails to start — `MatchmakingReconcilerService` constructor-injects `IConnectionMultiplexer`. The Matchmaking package builder intentionally does NOT auto-register the multiplexer (operator-owned lifecycle per `samples/TicTacToeDuel/Program.cs:23-25` convention).
- **Fix:** Added `services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn))` in OPS-06's DI setup; used `_redis.ConnectionString` from the shared `RedisFixture`.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs`.
- **Verification:** OPS-06 progresses past DI validation.
- **Committed in:** `37ee5fb` (Task 3 commit).

**4. [Rule 3 - Blocking] OPS-06 generic host can't resolve `IWebHostEnvironment`**
- **Found during:** Task 3 (after deviation #3 fix).
- **Issue:** `Host.CreateApplicationBuilder` builds a generic host; `AddGameKitAdmin` registers `RazorComponents` + MudBlazor services that need `IWebHostEnvironment` (only web host provides it). DI container-validation failed with ~20 cascading errors.
- **Fix:** Swapped to `WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development })`. No HTTP server actually starts — only `StartAsync` drives the migration hosted services.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs`.
- **Verification:** OPS-06 host build + StartAsync succeeds.
- **Committed in:** `37ee5fb` (Task 3 commit).

**5. [Rule 3 - Blocking] OPS-06 SuperadminGate throws in Production**
- **Found during:** Task 3 (after deviation #4 fix).
- **Issue:** `SuperadminGateHostedService` (T-03-06-05) throws `InvalidOperationException` on Production env when `admin_users` is empty.
- **Fix:** Set `EnvironmentName = Environments.Development` in `WebApplicationOptions`. Admin bootstrap is out of OPS-06's scope (this is a migration-coverage test).
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs` (in conjunction with deviation #4).
- **Committed in:** `37ee5fb` (Task 3 commit).

**6. [Rule 1 - Bug] PendingModelChangesWarning in OPS-06 Core migration apply**
- **Found during:** Task 3 (after deviation #5 fix).
- **Issue:** Core migration apply via DI-scoped `GameKitDbContext` failed: "The model for context 'GameKitDbContext' has pending changes" — the runtime DbContext sees the COMPOSITE model (Core + Auth + Rankings + Matchmaking + Admin via `IEnumerable<IModelBuilderExtension>`) but Core's migration snapshot is Core-only. Per-package migration boundary (PITFALLS #3) intentional.
- **Fix:** Added `BuildCoreOnlyMigrationContext` helper that mirrors `GameKitApplicationBuilderExtensions.UseGameKit.BuildMigrationContext` — uses the single-arg `GameKitDbContext(DbContextOptions)` ctor (no app provider attached, no `IModelBuilderExtension` resolution), keeping the migration model Core-only.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs`.
- **Verification:** OPS-06 migration apply succeeds; subsequent assertion of `__ef_migrations_*` history tables passes.
- **Committed in:** `37ee5fb` (Task 3 commit).

**7. [Rule 1 - Bug] OPS-05 synthetic assembly pollutes OPS-06 in same xUnit process**
- **Found during:** Task 3 (running OPS-04+05+06 together).
- **Issue:** OPS-05's `GameKit.SyntheticTest=99.99.99` Reflection.Emit assembly stays in `AppDomain.CurrentDomain.GetAssemblies()` after the test exits (xUnit shares processes). OPS-06's `GameKitVersionAssertionHostedService` (at index 0) picks it up and throws `GameKitVersionMismatchException` before the migration chain runs.
- **Fix (a):** OPS-05 uses `AssemblyBuilderAccess.RunAndCollect` (collectible) + scopes the AssemblyBuilder reference to an inner block + `for (var attempt = 0; attempt < 50 && asmWeak.IsAlive; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); }` GC-tickle loop after the assertion fires.
- **Fix (b) — belt + suspenders:** OPS-06 removes `GameKitVersionAssertionHostedService` from its DI container via string-based descriptor lookup (`d.ImplementationType?.FullName == "GameKit.Core.Hosting.GameKitVersionAssertionHostedService"`). Required because the GC tickle in (a) doesn't always collect within the budget — the assertion service is fully validated by OPS-05 anyway, so removing it from OPS-06 doesn't relax coverage.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS05_VersionMismatchAssertionThrowsTests.cs` + `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs`.
- **Verification:** All OPS-04/05/06 tests pass together.
- **Committed in:** `37ee5fb` (Task 3 commit).

**8. [Rule 1 - Bug] OPS-06 shared Postgres conflicts with DIST-02 schema**
- **Found during:** Task 3 (running full Distribution suite together).
- **Issue:** OPS-06 first failed with `42P07 relation "game_sessions" already exists` because DIST-02's `EnsureCoreTablesExistAsync` ran the raw DDL bootstrap on the shared `PostgresFixture` before OPS-06's clean Core migration apply. Then after a DIST-02 fix attempt (use EF instead), OPS-06 failed with `Cannot create a DbSet for 'Ladder'` because EF Core's process-wide model cache (no `IModelCacheKeyFactory` override) cached the Core-only model from DIST-02's `AddGameKit` call.
- **Fix:** OPS-06 spins up its OWN per-test `PostgreSqlContainer` (separate from the shared `PostgresFixture`); kept DIST-02 on raw DDL. Documented the model-cache poisoning rationale in DIST-02's `EnsureCoreTablesExistAsync` XML doc.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs` (new container + `[Collection("Redis")]`) + `tests/GameKit.Distribution.Integration.Tests/DIST02_GamekitReaderInsertDeniedTests.cs` (doc-only).
- **Verification:** Full Distribution suite (9 tests) passes.
- **Committed in:** `37ee5fb` (Task 3 commit).

**9. [Rule 3 - Blocking] D-26 `dotnet pack` subprocess deadlocks against parent `dotnet test` MSBuild nodes**
- **Found during:** Task 4 (first D-26 test run hung indefinitely).
- **Issue:** xUnit's `dotnet test` host runs MSBuild nodes with `NodeReuse=true` (global default); spawning a child `dotnet pack` Process.Start tries to reuse the same node pool and deadlocks (Microsoft-acknowledged at dotnet/sdk#14922).
- **Fix:** Added `MSBUILDDISABLENODEREUSE=1` + `DOTNET_CLI_USE_MSBUILD_SERVER=0` to the spawned process environment; added `-p:UseSharedCompilation=false -p:BuildInParallel=false` to the `dotnet pack` CLI.
- **Files modified:** `tests/GameKit.Distribution.Integration.Tests/D26_NuspecExactPinGuardTests.cs`.
- **Verification:** D-26 test completes in ~11 s instead of hanging.
- **Committed in:** `4b21017` (Task 4 commit).

**10. [Rule 1 - Bug] GameKit.targets `ItemDefinitionGroup` approach silently no-op'd**
- **Found during:** Task 4 (D-26 test correctly detected the defect).
- **Issue:** Plan 06-01 shipped `GameKit.targets` with `<ItemDefinitionGroup><ProjectReference><PackageVersion>[$(Version)]</PackageVersion></ProjectReference></ItemDefinitionGroup>`. NuGet 10's pack pipeline IGNORES this metadata when converting sibling `ProjectReference`s to `PackageReference`s — `_GetProjectReferenceVersions` -> `_GetProjectVersion` reads `$(PackageVersion)` of the referenced project verbatim, never consulting the ProjectReference item's metadata. Empirically verified: pre-fix nuspecs contained `version="0.0.0-alpha.0.131"` (no brackets); D-26 primary defense was a no-op.
- **Fix:** Replaced `ItemDefinitionGroup` with a working hook target `_ApplyExactPinToSiblingGameKitReferences` that runs `AfterTargets="_GetProjectReferenceVersions"` + `BeforeTargets="GenerateNuspec"` and rewrites `@(_ProjectReferencesWithVersions)` in place — wraps every sibling `GameKit.*` entry's `ProjectVersion` metadata with literal `[` `]` brackets. Diagnostic Message at Normal verbosity for traceability.
- **Files modified:** `GameKit.targets`.
- **Verification:** D-26 Test 1 (`Produced_Nuspec_For_Every_GameKit_Package_Pins_Sibling_GameKit_Deps_With_Exact_Square_Brackets`) passes; manual `dotnet pack src/GameKit.Auth/` produces `<dependency id="GameKit.Core" version="[0.0.0-alpha.0.131]" exclude="Build,Analyzers" />`.
- **Committed in:** `4b21017` (Task 4 commit).

**11. [Rule 3 - Blocking] NU1109 downgrade on Microsoft.Extensions.Http**
- **Found during:** Task 4 (full solution rebuild after deviation #1 pin at 10.0.0).
- **Issue:** `GameKit.Cli` + `GameKit.Cli.Tests` transitively require `Microsoft.Extensions.Http >= 10.0.6` (via `Microsoft.Extensions.Http.Resilience 10.5.0` -> `Microsoft.Extensions.Http.Diagnostics 10.5.0`). The 10.0.0 CPM pin from deviation #1 triggered NU1109.
- **Fix:** Bumped pin to `10.0.6`.
- **Files modified:** `Directory.Packages.props`.
- **Verification:** Full `GameKit.sln` rebuild green (0 warnings, 0 errors).
- **Committed in:** `4b21017` (Task 4 commit).

---

**Total deviations:** 11 auto-fixed (3 Rule 1 bug-fixes, 8 Rule 3 blocking-issue fixes)
**Impact on plan:** All 11 fixes were necessary for correctness — the most consequential is #10, which surfaced a real D-17/D-26 implementation bug in GameKit.targets that Plan 06-01 had assumed worked. The replacement hook-target approach is the actually-functional mechanism for NuGet 10's pack pipeline and matches the plan's design intent ("CI grep on produced .nuspec asserts the literal `[X.Y.Z]` syntax is present"). No scope creep — every deviation is in service of the plan's stated success criteria.

## Issues Encountered

- **Background-shell test runs interfered with each other.** During Task 4, the agent's shell sandbox auto-backgrounded long-running `dotnet test` invocations; multiple parallel attempts on the same artifacts directory occasionally deadlocked. Killed stale processes and re-ran cleanly with redirected output.
- **OPS-05 collectible-assembly unload non-determinism.** The Reflection.Emit `AssemblyBuilderAccess.RunAndCollect` + GC-tickle loop typically unloads within 1-2 cycles per Microsoft docs, but the test does not assert successful unload (would be flaky). The belt-suspenders fix in OPS-06 (removing the version-assertion hosted service) makes OPS-06 robust to OPS-05's residual regardless.

## Empirical Captures (per `<output>` requirements)

### (a) GameKitMarker.GameKitVersion value (OPS-04 reflection)

```
"1.0.0"
```

Verified via `src/GameKit.Core/obj/Debug/net10.0/generated/GameKit.Build/GameKit.Build.GameKitVersionGenerator/GameKitMarker.g.cs`:

```csharp
// <auto-generated/>
// Emitted by GameKit.Build source generator.
// SPDX-License-Identifier: GPL-3.0-or-later
namespace GameKit.Core.Internal;

internal static partial class GameKitMarker
{
    public const string GameKitVersion = "1.0.0";
    public const string AssemblyName   = "GameKit.Core";
}
```

All 7 packages (Core, Auth, Rankings, Matchmaking, Admin.UI, Presence, OpenApi) report the same `"1.0.0"` constant.

### (b) GameKitVersionMismatchException.Message format (OPS-05)

```
GameKit version mismatch detected across loaded assemblies: GameKit.Admin.UI=1.0.0, GameKit.Auth=1.0.0, GameKit.Core=1.0.0, GameKit.Matchmaking=1.0.0, GameKit.OpenApi=1.0.0, GameKit.Presence=1.0.0, GameKit.Rankings=1.0.0, GameKit.SyntheticTest=99.99.99. All GameKit.* packages must be pinned to the same version (see MSBuild pack-time exact-pin enforcement in GameKit.targets, D-17).
```

Sorted alphabetically by assembly name; synthetic mismatch entry visible at end (`GameKit.SyntheticTest=99.99.99`).

### (c) OPS-06 migration coverage

Asserted history tables (all present after `host.StartAsync` against fresh isolated Postgres):

- `gamekit.__ef_migrations_core`
- `gamekit.__ef_migrations_auth`
- `gamekit.__ef_migrations_rankings`
- `gamekit.__ef_migrations_matchmaking`
- `gamekit.__ef_migrations_admin`

Pending migrations on Core-only context: **empty** (no drift).

**Correctly excluded** (no migrations — Presence is Redis-only per PRES-01, OpenApi is doc-generation only): `GameKit.Presence`, `GameKit.OpenApi`. Neither package ships an EF migration set.

### (d) D-26 produced .nuspec sibling-dep proof

Captured via manual `dotnet pack src/GameKit.Auth/GameKit.Auth.csproj -c Debug -o /tmp/d26-proof --nologo -p:UseSharedCompilation=false`:

```xml
<dependency id="GameKit.Core" version="[0.0.0-alpha.0.131]" exclude="Build,Analyzers" />
```

The literal square brackets confirm the D-26 primary defense is now live. (The MinVer-resolved version `0.0.0-alpha.0.131` is the height-from-tag stamp — would be `[1.0.0]` after the v1 release tag.)

## User Setup Required

None — no external service configuration required. All changes use existing infrastructure (docker-compose Postgres + Redis, in-CI Testcontainers).

## Next Phase Readiness

- Plan 06-08 is complete. Plan 06-09 (template package) can now safely clone both `samples/TicTacToeDuel/` (web tier) AND `samples/TicTacToeDuel.GameServer/` (game-server tier) into `templates/GameKit.Templates/content/GameKit.SampleGame/`.
- The release-train invariants (uniform MinVer stamp + version-mismatch assertion + clean-install migrations + nuspec exact-pin) are all empirically gated. CI will catch regressions on every push.
- The GameKit.targets implementation bug fix (deviation #10) is significant: future `dotnet pack` runs against tagged releases will produce correctly-pinned nuspecs from this commit forward. v1 release tagging is now safe.

## Self-Check: PASSED

All 11 created files verified present on disk. All 4 task commits verified in git log (`1e69269`, `b723210`, `37ee5fb`, `4b21017`). All 9 Distribution.Integration tests pass (smoke + DIST-02 x2 + OPS-04 x2 + OPS-05 + OPS-06 + D-26 x2). Full `GameKit.sln` build green.

---
*Phase: 06-presence-openapi-distribution*
*Completed: 2026-05-26*
