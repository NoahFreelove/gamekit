---
phase: 06-presence-openapi-distribution
plan: 06
subsystem: api
tags: [openapi, swagger, jwt, bearerAuth, contract-test, microsoft-aspnetcore-openapi, source-generator]

# Dependency graph
requires:
  - phase: 06-presence-openapi-distribution
    provides: "Plan 06-01: GameKit.OpenApi csproj skeleton + Microsoft.AspNetCore.OpenApi 10.0.8 CPM pin + GameKit.Build source generator emitting GameKitMarker.GameKitVersion"
  - phase: 06-presence-openapi-distribution
    provides: "Plan 06-04: GameKit.Presence runtime — AddPresence + MapPresence + /api/presence/heartbeat endpoint (covered by the contract test)"
  - phase: 06-presence-openapi-distribution
    provides: "Plan 06-05: Sessions /start + /abandon endpoints + observer fan-out (covered by the contract test)"
provides:
  - "GameKit.OpenApi runtime — AddGameKitOpenApi + MapGameKitOpenApi + GameKitOpenApiOptions + two IOpenApiDocumentTransformer implementations"
  - "Inline OpenApiOptions.ShouldInclude lambda filtering admin routes (D-19 verbatim, NO trailing slash so bare /admin is also caught)"
  - "GameKitBearerSchemeTransformer — injects bearerAuth (type=http, scheme=bearer, bearerFormat=JWT) globally when JwtBearer scheme is registered (D-08)"
  - "GameKitInfoTransformer — populates document.Info.Title from options + document.Info.Version from GameKitMarker.GameKitVersion source-gen const (D-10)"
  - "Three contract tests (Coverage + BearerScheme + AdminRouteExclusion) green; D-09 EndpointDataSource enumeration in place for OPEN-01"
  - "Microsoft.OpenApi 2.0.0 SerializeAsV3/SerializeAsV31 serialization-bug workaround (WorkingSecurityRequirement subclass)"
affects:
  - "Phase 06 distribution gate — OPEN-01 requirement satisfied; combined /openapi/v1.json doc is the consumer-facing API contract"
  - "Future endpoint additions will be auto-covered by the D-09 contract test failing loudly when a new endpoint is missing OpenAPI-compatible metadata"

# Tech tracking
tech-stack:
  added:
    - "Microsoft.AspNetCore.OpenApi 10.0.8 (PackageReference on GameKit.OpenApi; CPM pin from 06-01)"
  patterns:
    - "OpenApi document-transformer pattern (IOpenApiDocumentTransformer + options.AddDocumentTransformer<T>) for cross-cutting doc decoration"
    - "Inline OpenApiOptions.ShouldInclude lambda for path-level filtering (cannot be done via IOpenApiOperationTransformer — operation transformers cannot remove paths)"
    - "Source-gen const consumption (GameKitMarker.GameKitVersion) — read MinVer-derived version at compile time without reflection"
    - "Partial-class extension split mirrors Presence Plan 06-04 (.cs base + .Options.cs reserved slot)"

key-files:
  created:
    - "src/GameKit.OpenApi/Configuration/GameKitOpenApiOptions.cs"
    - "src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs"
    - "src/GameKit.OpenApi/Transformers/GameKitInfoTransformer.cs"
    - "src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.cs"
    - "src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.Options.cs"
    - "src/GameKit.OpenApi/Builder/OpenApiApplicationBuilderExtensions.cs"
    - "tests/GameKit.OpenApi.Integration.Tests/OpenApiTestApp.cs"
    - "tests/GameKit.OpenApi.Integration.Tests/OpenApiRuntimeModelCustomizer.cs"
    - "tests/GameKit.OpenApi.Integration.Tests/OpenApiCoverageTests.cs"
    - "tests/GameKit.OpenApi.Integration.Tests/OpenApiBearerSchemeTests.cs"
    - "tests/GameKit.OpenApi.Integration.Tests/OpenApiAdminRouteExclusionTests.cs"
  modified:
    - "src/GameKit.OpenApi/GameKit.OpenApi.csproj (+Microsoft.AspNetCore.OpenApi PackageReference)"
    - "samples/TicTacToeDuel/TicTacToeDuel.csproj (+ProjectRef GameKit.OpenApi)"
    - "samples/TicTacToeDuel/Program.cs (+AddGameKitOpenApi + MapGameKitOpenApi)"
    - "src/GameKit.Auth/AssemblyInfo.cs (+InternalsVisibleTo OpenApi.Integration.Tests)"
    - "src/GameKit.Admin.UI/AssemblyInfo.cs (+InternalsVisibleTo OpenApi.Integration.Tests)"
    - "src/GameKit.Rankings/AssemblyInfo.cs (+InternalsVisibleTo OpenApi.Integration.Tests)"
    - "src/GameKit.Matchmaking/AssemblyInfo.cs (+InternalsVisibleTo OpenApi.Integration.Tests)"

key-decisions:
  - "Hardcoded JwtBearer scheme name literal (\"Bearer\") in GameKitBearerSchemeTransformer instead of taking a PackageReference on Microsoft.AspNetCore.Authentication.JwtBearer — keeps OpenAPI optional and dependency-free for Core-only consumers"
  - "Admin-route exclusion is an INLINE OpenApiOptions.ShouldInclude lambda (NOT a separate IOpenApiOperationTransformer) per D-19 + PATTERNS Critical Misuse Warning #1; operation transformers cannot remove paths from a document"
  - "Subclassed OpenApiSecurityRequirement (private sealed WorkingSecurityRequirement) to override SerializeAsV3 + SerializeAsV31 because Microsoft.OpenApi 2.0.0's base implementation emits empty `{ }` instead of `{ \"bearerAuth\": [] }` (upstream serialization bug; verified by reflection-driven repro)"
  - "AddGameKitOpenApi is an IServiceCollection extension (not an IGameKitBuilder extension) because OpenAPI registration is orthogonal to the per-package builder chain; consumers can opt in without touching their AddGameKit() + AddAuth() chain"

patterns-established:
  - "Document-transformer pattern: internal sealed IOpenApiDocumentTransformer with DI dependencies, registered via options.AddDocumentTransformer<T>() inside the AddOpenApi configure delegate"
  - "MountPath + DocumentName composition: routes pattern = `{MountPath.TrimEnd('/')}/{DocumentName}.json` so consumers can relocate the doc without breaking the AddOpenApi/MapOpenApi document-name coupling"

requirements-completed: [OPEN-01]

# Metrics
duration: 29min
completed: 2026-05-26
---

# Phase 6 Plan 06-06: GameKit.OpenApi Runtime + Contract Tests Summary

**Combined /openapi/v1.json document covering 24 player-facing endpoints across 7 packages with global bearerAuth security + D-19 inline admin-route exclusion + 3 contract tests proving the OPEN-01 invariant.**

## Performance

- **Duration:** 29 min
- **Started:** 2026-05-26T03:17:16Z
- **Completed:** 2026-05-26T03:46:33Z
- **Tasks:** 2 (1 runtime + 1 TDD)
- **Files created:** 11 (6 src + 5 tests)
- **Files modified:** 7 (1 src csproj + 2 sample + 4 AssemblyInfo)

## Accomplishments

- **GameKit.OpenApi 7th-package runtime complete.** AddGameKitOpenApi extension + MapGameKitOpenApi mount + GameKitOpenApiOptions POCO + two IOpenApiDocumentTransformer implementations (Info + BearerScheme). Inline OpenApiOptions.ShouldInclude lambda filters admin routes (D-19 verbatim).
- **3 contract tests green.** OpenApiCoverageTests (D-09 EndpointDataSource enumeration vs document.paths), OpenApiBearerSchemeTests (D-08 bearerAuth present + applied globally), OpenApiAdminRouteExclusionTests (D-08 + D-19 admin paths absent, non-vacuous).
- **Sample TicTacToeDuel wired.** `builder.Services.AddGameKitOpenApi()` + `app.MapGameKitOpenApi()` produce /openapi/v1.json at runtime.
- **Microsoft.OpenApi 2.0.0 serialization-bug workaround** (WorkingSecurityRequirement subclass) so `op.Security[].bearerAuth: []` serializes correctly.

## Task Commits

Each task was committed atomically:

1. **Task 1: GameKit.OpenApi runtime + sample wiring** — `0a3aaa6` (feat)
2. **Task 2: 3 contract tests + Microsoft.OpenApi serialization workaround + IVT grants** — `f2a3941` (test, includes Rule 1 bug-fix + Rule 3 blocking-fix)

_Note: Task 2 was `tdd="true"` in the plan but the tests were written in their final form and passed once Task 1's wiring was in place — RED was satisfied conceptually (tests-before-implementation discipline) but a separate RED commit was not produced because Task 1 had already shipped the implementation. The plan acknowledged this ordering in its `<action>` block: "in practice the tests should pass once Task 1's empirical wiring is done."_

## Files Created/Modified

### Created (11)

**src/GameKit.OpenApi/ (6 new files):**

- `src/GameKit.OpenApi/Configuration/GameKitOpenApiOptions.cs` — POCO with DocumentName="v1", Title="GameKit API", MountPath="/openapi" defaults; consumer override via Action<T>.
- `src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs` — IOpenApiDocumentTransformer that probes IAuthenticationSchemeProvider for the "Bearer" scheme, injects bearerAuth into components.securitySchemes, and applies it globally to every operation. Includes private nested WorkingSecurityRequirement subclass overriding SerializeAsV3/SerializeAsV31 (Microsoft.OpenApi 2.0.0 bug workaround).
- `src/GameKit.OpenApi/Transformers/GameKitInfoTransformer.cs` — IOpenApiDocumentTransformer that populates document.Info.Title from options + document.Info.Version from GameKitMarker.GameKitVersion (source-gen const emitted into the same assembly by GameKit.Build).
- `src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.cs` — AddGameKitOpenApi extension on IServiceCollection. Calls services.AddOpenApi(opts.DocumentName, o => { o.ShouldInclude = ...; o.AddDocumentTransformer<...>(); }) with the inline admin-filter lambda per D-19.
- `src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.Options.cs` — Empty partial; reserved partial-split slot mirroring Presence Plan 06-04 convention.
- `src/GameKit.OpenApi/Builder/OpenApiApplicationBuilderExtensions.cs` — MapGameKitOpenApi extension on IEndpointRouteBuilder; resolves the options singleton + builds `{MountPath}/{DocumentName}.json` route + calls routes.MapOpenApi(pattern).

**tests/GameKit.OpenApi.Integration.Tests/ (5 new files):**

- `OpenApiTestApp.cs` — In-process Host.CreateDefaultBuilder + UseTestServer test host composing the FULL GameKit chain (Core+Auth+Rankings+Matchmaking+Presence+Admin+OpenApi) with the same endpoint mapping shape as samples/TicTacToeDuel/Program.cs. Runs in Development env to bypass SuperadminGate's Production-throw. Migration sequence Core → Auth → Admin → Rankings → Matchmaking via per-package MigrationRunner.MigrateWithLockAsync.
- `OpenApiRuntimeModelCustomizer.cs` — RelationalModelCustomizer subclass applying Auth + Admin entity configurations + Rankings/Matchmaking IModelBuilderExtension.ApplyTo. FOLLOW-UP-02-03-01 ApplicationServiceProvider workaround.
- `OpenApiCoverageTests.cs` — [Collection("OpenApi")] D-09 contract test. Walks EndpointDataSource, normalizes route-constraint suffixes ({id:guid} → {id}) + trailing slashes, filters out admin/openapi/_blazor/_framework/_content prefixes, and asserts every (METHOD, PATH) tuple appears in /openapi/v1.json.
- `OpenApiBearerSchemeTests.cs` — Two [Fact]s. (1) SecuritySchemes_Contains_BearerAuth — type=http + scheme=bearer + bearerFormat=JWT. (2) BearerAuth_Is_Applied_To_Every_Operation — asserts the global-application invariant (all 24 operations covered).
- `OpenApiAdminRouteExclusionTests.cs` — Two [Fact]s. (1) No_Admin_Path_Appears_In_OpenApi_Document — no doc path starts with "admin" (case-insensitive, NO trailing slash). (2) Host_Registers_Admin_Endpoints_So_Exclusion_Is_Non_Vacuous — admin endpoints DO appear via EndpointDataSource, proving the exclusion is observable.

### Modified (7)

- `src/GameKit.OpenApi/GameKit.OpenApi.csproj` — Added `<PackageReference Include="Microsoft.AspNetCore.OpenApi" />` (CPM-pinned 10.0.8 in Directory.Packages.props by Plan 06-01).
- `samples/TicTacToeDuel/TicTacToeDuel.csproj` — Added ProjectRef to GameKit.OpenApi.
- `samples/TicTacToeDuel/Program.cs` — Added `using GameKit.OpenApi.Builder;` + `builder.Services.AddGameKitOpenApi();` in fluent chain + `app.MapGameKitOpenApi();` in endpoint mapping.
- `src/GameKit.Auth/AssemblyInfo.cs` — `+[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]`.
- `src/GameKit.Admin.UI/AssemblyInfo.cs` — same IVT grant.
- `src/GameKit.Rankings/AssemblyInfo.cs` — same IVT grant.
- `src/GameKit.Matchmaking/AssemblyInfo.cs` — same IVT grant.

## Decisions Made

1. **Hardcoded JwtBearer scheme name literal `"Bearer"`** — instead of taking a PackageReference on Microsoft.AspNetCore.Authentication.JwtBearer + `using JwtBearerDefaults.AuthenticationScheme`. Rationale: every GameKit.OpenApi consumer would otherwise force the JwtBearer runtime onto Core-only deployments. The literal value is OAuth-2.0 standardized and matches GameKit.Auth's `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` registration verbatim. Documented in the transformer's XML doc.

2. **Admin-route exclusion is INLINE** — `o.ShouldInclude = description => !(description.RelativePath ?? "").StartsWith("admin", OrdinalIgnoreCase)` registered directly inside the AddOpenApi configure delegate, NOT a separate IOpenApiOperationTransformer. Operation transformers cannot remove paths from a document — they can only decorate (RESEARCH §Pitfall 4 + PATTERNS Critical Misuse Warning #1). The literal is "admin" with NO trailing slash so the bare /admin Blazor console root is also filtered.

3. **WorkingSecurityRequirement subclass** — Microsoft.OpenApi 2.0.0's OpenApiSecurityRequirement.SerializeAsV3/SerializeAsV31 emit empty `{ }` instead of `{ "bearerAuth": [] }` (the base SerializeInternal action-callback path does not enumerate base-Dictionary entries). Repro: req.Count == 1 with `bearerAuth` key + correct ref, SerializeAsV3 still produces `{ }`. Workaround overrides both serialize methods to walk `(IDictionary<,>)this` and emit the canonical shape via IOpenApiWriter.WritePropertyName/WriteStartArray/WriteEndArray. Remove the subclass once Microsoft.OpenApi ships a fix.

4. **AddGameKitOpenApi is an IServiceCollection extension** — not an IGameKitBuilder extension. OpenAPI registration is orthogonal to the per-package builder chain; consumers can opt in without disturbing their AddGameKit/AddAuth/... chain.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Microsoft.OpenApi 2.0.0 OpenApiSecurityRequirement serialization workaround**
- **Found during:** Task 2 (BearerAuth_Is_Applied_To_Every_Operation initially failed Expected 24, Actual 0)
- **Issue:** The library's base SerializeAsV3/SerializeAsV31 implementations of OpenApiSecurityRequirement emit empty `{ }` instead of `{ "bearerAuth": [] }`. The base SerializeInternal path uses an action-callback over base-Dictionary entries that does NOT enumerate when invoked from within OpenApiDocument serialization. Verified via reflection: `req.Count == 1` and `req.Keys.Single().Reference.Id == "bearerAuth"`, but SerializeAsV31 still emits `{ }`.
- **Fix:** Added a private sealed `WorkingSecurityRequirement : OpenApiSecurityRequirement` nested class inside GameKitBearerSchemeTransformer that overrides SerializeAsV3 + SerializeAsV31. Implementation walks `(IDictionary<OpenApiSecuritySchemeReference, List<string>>)this` and writes `WritePropertyName(reference.Id) + WriteStartArray() + WriteEndArray()` per entry. Also passes `document` (not null) as the OpenApiSecuritySchemeReference's hostDocument so the $ref resolves to `#/components/securitySchemes/bearerAuth`.
- **Files modified:** src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs
- **Verification:** BearerAuth_Is_Applied_To_Every_Operation now reports 24/24 operations carry `security: [{ "bearerAuth": [] }]`.
- **Committed in:** `f2a3941` (Task 2 commit)

**2. [Rule 3 - Blocking] InternalsVisibleTo grants for 4 packages**
- **Found during:** Task 2 (boot of OpenApiTestApp failed with "Cannot create a DbSet for 'AdminUser' / 'Ladder'")
- **Issue:** The OpenApi-test host composes the full GameKit chain (Core+Auth+Rankings+Matchmaking+Presence+Admin+OpenApi). At boot, SuperadminGateHostedService queries admin_users + StartupLadderUpserter queries ladders. The runtime GameKitDbContext's IModelCustomizer doesn't apply the per-package entity configurations because of FOLLOW-UP-02-03-01 (ApplicationServiceProvider captures the wrong service-collection under Host.CreateDefaultBuilder + ConfigureWebHostDefaults). The workaround is a custom RelationalModelCustomizer that applies the configurations directly — but those configurations are `internal sealed` in each package.
- **Fix:** Added `[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]` to GameKit.Auth/AssemblyInfo.cs + GameKit.Admin.UI/AssemblyInfo.cs + GameKit.Rankings/AssemblyInfo.cs + GameKit.Matchmaking/AssemblyInfo.cs. Mirrors precedents already present in each file (Admin.Integration.Tests + Presence.Integration.Tests grants).
- **Files modified:** 4 AssemblyInfo.cs files
- **Verification:** OpenApiRuntimeModelCustomizer compiles + Application boots cleanly.
- **Committed in:** `f2a3941` (Task 2 commit)

**3. [Rule 3 - Blocking] Run OpenApiTestApp in Development environment**
- **Found during:** Task 2 (boot failure after Issue 2 was fixed)
- **Issue:** SuperadminGateHostedService throws in Production env when admin_users is empty: "GameKit.Admin.UI is mounted in Production but no superadmin exists. Bootstrap via `dotnet gamekit admin create`."
- **Fix:** Added `.UseEnvironment("Development")` to the Host.CreateDefaultBuilder() call so the gate downgrades to a warning instead of throwing.
- **Files modified:** tests/GameKit.OpenApi.Integration.Tests/OpenApiTestApp.cs
- **Verification:** Host starts cleanly without seeding admin rows.
- **Committed in:** `f2a3941` (Task 2 commit)

**4. [Rule 3 - Blocking] Route-pattern normalization in OpenApiCoverageTests**
- **Found during:** Task 2 (8 endpoints missing — `GET /api/players/`, `GET /api/players/{id:guid}/export`, etc.)
- **Issue:** ASP.NET Core's RoutePattern.RawText includes route-constraint suffixes like `{id:guid}` and may include trailing slashes (e.g. `/api/players/`), but the OpenAPI document keys paths without constraints and without trailing slashes (`/api/players/{id}`). The naive comparison missed every route with a `:guid` constraint or trailing slash.
- **Fix:** Added a NormalizeRoutePattern helper that strips `{name:constraint}` → `{name}` via regex + trims trailing slash on non-root paths + ensures leading slash. Applied to every enumerated route before the contract assertion.
- **Files modified:** tests/GameKit.OpenApi.Integration.Tests/OpenApiCoverageTests.cs
- **Verification:** Every_NonAdmin_Endpoint_Is_In_OpenApi_Document now matches 24/24 enumerated tuples against document paths.
- **Committed in:** `f2a3941` (Task 2 commit)

**5. [Rule 3 - Blocking] Removed AddLadder calls from OpenApiTestApp**
- **Found during:** Task 2 (pre-Issue 2 attempt — earlier failure mode)
- **Issue:** First boot attempt called `.AddLadder("main", ...).AddLadder("tictactoe", ...)`, triggering StartupLadderUpserter which queried `ctx.Set<Ladder>()` — Ladder wasn't in the runtime model. Even after the IModelCustomizer fix this was unnecessary noise since OPEN-01 doesn't care about ladder seeding.
- **Fix:** Removed AddLadder calls — Rankings + Matchmaking are composed without ladders. StartupLadderUpserter returns early ("no ladders registered"). The endpoint surface (MapRankings/MapMatchmaking) is unaffected.
- **Files modified:** tests/GameKit.OpenApi.Integration.Tests/OpenApiTestApp.cs
- **Verification:** Host startup faster + no ladder-related failures.
- **Committed in:** `f2a3941` (Task 2 commit)

---

**Total deviations:** 5 auto-fixed (1 Rule 1 bug, 4 Rule 3 blocking).
**Impact on plan:** All deviations were necessary for the runtime to actually generate a correct OpenAPI document + for the test infrastructure to boot the same surface as the sample. No scope creep — every fix was confined to the OpenApi runtime + the per-test infrastructure. The Microsoft.OpenApi serialization-bug workaround is bounded (private nested class scoped to GameKitBearerSchemeTransformer) and time-limited (XML doc + inline comments call out that it should be removed once upstream ships a fix).

## Issues Encountered

- **Microsoft.OpenApi 2.0.0 namespace flattening.** The library moved every type from `Microsoft.OpenApi.Models` to `Microsoft.OpenApi` (no more nested namespace). Required updating the transformer `using` directives. Also: `OpenApiSecurityRequirement` now extends `Dictionary<OpenApiSecuritySchemeReference, List<string>>` (not v1's `Dictionary<OpenApiSecurityScheme, IList<string>>`), and `Components.SecuritySchemes` is `IDictionary<string, IOpenApiSecurityScheme>` (interface-typed). Resolved by reading the package's XML docs + running a small probe project to introspect the runtime types.
- **EndpointDataSource path-pattern normalization.** ASP.NET Core's route patterns include constraint syntax (`{id:guid}`) and may include trailing slashes that the OpenAPI doc strips. Resolved by adding NormalizeRoutePattern in the test.

## Output Requirements (per PLAN <output>)

**(a) Literal info block from a sample run — proves GameKitInfoTransformer + GameKitMarker wire correctly:**

```json
{
  "title": "GameKit API",
  "version": "1.0.0"
}
```

The version "1.0.0" is MinVer's at-head fallback (no Git tag on this commit) emitted by GameKit.Build's GameKitVersionGenerator into `GameKit.OpenApi.Internal.GameKitMarker.GameKitVersion` at compile time. The title comes from `GameKitOpenApiOptions.Title` default. When a Git tag like `v1.2.0-rc.1` is set, MinVer + GameKit.Build will substitute the value automatically.

**(b) Path count comparison — D-09 contract numbers must match:**

- **OpenAPI document path count:** 24
- **EndpointDataSource non-admin endpoint count (after normalization):** 24
- **Match: ✅** (the contract test enforces the equality)

The 24 paths cover: `/api/players`, `/api/sessions/{id}/{complete,start,abandon}` (3), `/auth/{login/{provider},refresh,register,logout,logout/all,me,challenge/{provider},callback/{provider},link/{provider}}` (9), `/api/players/{id}/export`, `/api/parties` + `/api/parties/join` + `/api/parties/{id}` + `/api/parties/{id}/dissolve` (4), `/api/mm/queue` + `/api/mm/queue/{ticketId}/status` + `/api/mm/queue/{ticketId}` (3), `/api/mm/proposal/{proposalId}/{accept,decline}` (2), `/api/presence/heartbeat` (1).

**(c) Empirical confirmation NO /admin path appears in the document:**

OpenApiAdminRouteExclusionTests.No_Admin_Path_Appears_In_OpenApi_Document iterates `document.paths.Keys` and asserts NO key starts with "admin" (case-insensitive, NO trailing slash). The test passed; the document has zero `/admin/*` keys.

OpenApiAdminRouteExclusionTests.Host_Registers_Admin_Endpoints_So_Exclusion_Is_Non_Vacuous proves the exclusion is observable: the host's EndpointDataSource contains 30+ admin endpoints (from MapGameKitAdmin), and the test asserts `adminPaths.Count > 0`. The exclusion mechanism (D-19 inline ShouldInclude lambda) is empirically validated.

## Threat Flags

None. The threat model in PLAN.md `<threat_model>` covers all surface introduced; no new security-relevant surfaces were added beyond what was planned (OPEN-API doc is anonymous-readable per design, admin paths filtered, JwtBearer literal hardcoded — already accepted as T-06-06-04 + T-06-06-06 acks).

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- **OPEN-01 satisfied.** The combined /openapi/v1.json document is generated at runtime + covers every player-facing GameKit endpoint + filters out admin endpoints + carries the bearerAuth scheme. The D-09 contract test will fail loudly if a future endpoint addition forgets OpenAPI metadata.
- **Phase 6 distribution work can proceed.** Plans 06-08 (CLI templates) + 06-09 (Distribution.Integration.Tests) + 06-10 (production-ops docs) all depend on a stable OpenAPI document for consumer onboarding documentation. The /openapi/v1.json output is now a stable artifact.
- **No blockers carried forward.** The Microsoft.OpenApi 2.0.0 serialization-bug workaround (WorkingSecurityRequirement) is bounded and self-contained — when upstream ships a fix, removing the subclass is a one-method-deletion change.

## Self-Check: PASSED

**Files created (11):**
- `src/GameKit.OpenApi/Configuration/GameKitOpenApiOptions.cs` — FOUND
- `src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs` — FOUND
- `src/GameKit.OpenApi/Transformers/GameKitInfoTransformer.cs` — FOUND
- `src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.cs` — FOUND
- `src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.Options.cs` — FOUND
- `src/GameKit.OpenApi/Builder/OpenApiApplicationBuilderExtensions.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/OpenApiTestApp.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/OpenApiRuntimeModelCustomizer.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/OpenApiCoverageTests.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/OpenApiBearerSchemeTests.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/OpenApiAdminRouteExclusionTests.cs` — FOUND

**Commits exist:**
- `0a3aaa6` (Task 1) — FOUND
- `f2a3941` (Task 2) — FOUND

**Test status:** 6/6 [Fact]s green in tests/GameKit.OpenApi.Integration.Tests/ (verified via `dotnet test`).
**Build status:** `dotnet build GameKit.sln -c Debug` reports 0 warnings, 0 errors across the full 41-project solution.

---
*Phase: 06-presence-openapi-distribution*
*Plan: 06*
*Completed: 2026-05-26*
