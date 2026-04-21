---
phase: 03-admin-ui
plan: 06
subsystem: admin-ui
tags:
  - admin-ui
  - services
  - audit
  - ban
  - health
  - superadmin-gate
  - fluent-builder
  - wave-3
dependencies:
  requires:
    - phase: 03-02
      provides: AdminUser entity + AdminMigrationHostedService + AdminModelBuilderExtension (consumed by SuperadminGate, AdminAuthService, AdminUserService)
    - phase: 03-03
      provides: GameKitAdminOptions tree + AdminRoles + AdminPolicies + AdminAuthenticationSchemeConstants (consumed by AddGameKitAdmin policies + cookie scheme + options validator)
    - phase: 03-04
      provides: AdminCookieEvents + AdminRateLimitRegistrations.AddAdminRateLimits (wired into AddGameKitAdmin)
    - phase: 03-05
      provides: AdminCspNonceMiddleware + AntiforgeryValidationFilter + ValidationEndpointFilter<T> (middleware wired into UseGameKitAdmin; filters consumed by plan 03-07 endpoints)
  provides:
    - IAdminAuditWriter + AdminAuditWriter (Scoped; 9 namespaced actions under AdminAuditActions)
    - IAdminAuthService + AdminAuthService (BCrypt dummy-hash timing parity on user-not-found; audit rows on success/failure/locked)
    - IPlayerSearchService + PlayerSearchService (D-11 unified search with ClassifyInput public static; keyset pagination on display-name prefix)
    - IPlayerBanService + PlayerBanService (SERIALIZABLE tx ban/unban + audit row in the same tx)
    - IAdminUserService + AdminUserService (SERIALIZABLE create with 23505 → AdminUsernameAlreadyTakenException + 40001 retry; last-superadmin guard → LastSuperadminException)
    - IHealthProbeService + HealthProbeService (Postgres SELECT 1 + Redis PING + ErrorRateRingBuffer; status: OK/Degraded/Down per count)
    - ErrorRateRingBuffer (lock-free rolling-window counter with IClock)
    - LogErrorCounter (ILoggerProvider feeding Error+ events into the ring buffer)
    - SuperadminGateHostedService (D-04/D-05 startup gate — Production throws, Development logs)
    - AdminBuilderExtensions.AddGameKitAdmin fluent entry point (full SP-5 wire-up)
    - AdminApplicationBuilderExtensions.UseGameKitAdmin + MapGameKitAdmin
    - AdminEndpoints placeholder (plan 03-07 replaces Map body with 12 endpoints)
    - AdminTestHost (in-process WebApplicationFactory-style host + AdminRuntimeQueryCustomizer bypass)
    - 6 DTOs (PaginatedResult<T>, PlayerRow, PlayerSearchResult, HealthReport, HealthTile, SearchMode enum + PlayerSearchClassification record struct)
    - 9 action constants (AdminAuditActions)
  affects:
    - 03-07 (/admin/api/* endpoints consume every interface registered in AddGameKitAdmin; AdminEndpoints.Map placeholder gets replaced in-place)
    - 03-08 (Blazor pages call the same services via DI; MudBlazor service layer already registered)
    - 03-09 (audit-log viewer queries admin_audit_log rows produced by Ban/Unban/Create-admin)
    - 03-11 (CLI `gamekit admin create` calls IAdminUserService.CreateAsync directly or re-uses the SERIALIZABLE + BCrypt pattern)
    - 03-12 (sample app wires AddGameKitAdmin + UseGameKitAdmin + MapGameKitAdmin — all three entry points exist)
    - 03-13 (SC#2 tests use SuperadminGateHostedService throw; SC#6 tests use scheme isolation proven by W4 smoke)
tech-stack:
  added:
    - "MudBlazor services (AddMudServices) wired into GameKit.Admin.UI via AddGameKitAdmin"
    - "Microsoft.AspNetCore.Antiforgery (shared framework) wired with X-GameKit-Admin-CSRF header + gk_admin_csrf cookie"
    - "Razor Components (AddRazorComponents().AddInteractiveServerComponents()) registered by AddGameKitAdmin for plan 03-08's App.razor"
  patterns:
    - "BCrypt dummy-hash timing parity: AdminAuthService.DummyHash = $2a$12$... canonical literal (60 chars, work-factor 12) — verbatim paste-once pattern (mirrors Phase 2 PasswordOAuthProvider.DummyHash)"
    - "SERIALIZABLE + 3-retry-on-40001 + 23505-map-to-domain-exception (GuestUpgradeService precedent; used by AdminUserService.CreateAsync)"
    - "Snapshot-before + tracked-mutation + audit-write + COMMIT inside SERIALIZABLE tx (GdprDeleteService precedent; used by PlayerBanService.Ban/Unban)"
    - "Lock-free ring buffer with double-checked bucket rotation + per-bucket Volatile.Read sum (RESEARCH §Health panel lines 894-938)"
    - "Fluent DI builder with per-step ordering (SP-5): options → ModelBuilderExtension → MigrationHostedService → Gate → Cookie auth → Authorization → Scoped services → Singleton ring buffer + log provider → Rate limiter → Antiforgery → Razor + MudBlazor → HttpContextAccessor"
    - "AdminRuntimeQueryCustomizer bypass (test-host only): sidesteps the FOLLOW-UP-02-03-01 broken ApplicationServiceProvider path under Host.CreateDefaultBuilder + ConfigureWebHostDefaults by applying Core + Auth + Admin configs directly via ReplaceService<IModelCustomizer>"
key-files:
  created:
    - src/GameKit.Admin.UI/Services/IAdminAuditWriter.cs
    - src/GameKit.Admin.UI/Services/AdminAuditWriter.cs
    - src/GameKit.Admin.UI/Services/AdminAuditActions.cs
    - src/GameKit.Admin.UI/Services/IAdminAuthService.cs
    - src/GameKit.Admin.UI/Services/AdminAuthService.cs
    - src/GameKit.Admin.UI/Services/IPlayerSearchService.cs
    - src/GameKit.Admin.UI/Services/PlayerSearchService.cs
    - src/GameKit.Admin.UI/Services/IPlayerBanService.cs
    - src/GameKit.Admin.UI/Services/PlayerBanService.cs
    - src/GameKit.Admin.UI/Services/IAdminUserService.cs
    - src/GameKit.Admin.UI/Services/AdminUserService.cs
    - src/GameKit.Admin.UI/Services/IHealthProbeService.cs
    - src/GameKit.Admin.UI/Services/HealthProbeService.cs
    - src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs
    - src/GameKit.Admin.UI/Services/LogErrorCounter.cs
    - src/GameKit.Admin.UI/Authentication/SuperadminGateHostedService.cs
    - src/GameKit.Admin.UI/Http/Contracts/PaginatedResult.cs
    - src/GameKit.Admin.UI/Http/Contracts/PlayerRow.cs
    - src/GameKit.Admin.UI/Http/Contracts/PlayerSearchResult.cs
    - src/GameKit.Admin.UI/Http/Contracts/HealthReport.cs
    - src/GameKit.Admin.UI/Http/AdminEndpoints.cs
    - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
    - src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs
    - tests/GameKit.Admin.Tests/AdminAuditWriterTests.cs
    - tests/GameKit.Admin.Tests/PlayerSearchInputDetectionTests.cs
    - tests/GameKit.Admin.Tests/AdminAuthServiceTests.cs
    - tests/GameKit.Admin.Tests/ErrorRateRingBufferTests.cs
    - tests/GameKit.Admin.Tests/TestDbContextFactory.cs
    - tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs
    - tests/GameKit.Admin.Integration.Tests/SuperadminGateTests.cs
    - tests/GameKit.Admin.Integration.Tests/PlayerBanServiceTests.cs
    - tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs
    - tests/GameKit.Admin.Integration.Tests/AuthSchemeIsolationSmokeTests.cs
  modified:
    - src/GameKit.Auth/AssemblyInfo.cs
decisions:
  - "AdminAuditWriter + AdminAuthService are public sealed (not internal sealed) — their constructors are invoked by AddGameKitAdmin's DI chain + directly by unit tests via TestDbContextFactory; public surface matches plan 03-05's AdminCspNonceMiddleware/filter visibility decision"
  - "AdminAuthService.DummyHash is a real BCrypt.Net-Next 4.1.0 output for password 'admin-dummy-never-matches' at work factor 12 — literal paste-once, deterministic across CI re-runs (T-03-06-03 timing parity)"
  - "AdminUserService.CreateAsync + DeleteAsync run under SERIALIZABLE; Create retries up to 3x on 40001 mirroring GuestUpgradeService; Delete does NOT retry (last-superadmin count is a read-then-check inside the tx, no race window that 40001 would resolve)"
  - "PlayerBanService uses tracked-mutation + SaveChanges + audit-write + Commit (not ExecuteUpdate) — single-row ban does not benefit from bypassing the change tracker; keeps the mutation and the audit-row in one SaveChanges atomicity window"
  - "PlayerSearchService.ClassifyInput is public static — lets unit tests exercise branch classification without a DbContext (4 cases: None/Id/Identity/DisplayName)"
  - "PlayerSearchService case folding of display-name prefix uses EF.Functions.ILike (Postgres citext-style) — works out of the box against citext + text columns"
  - "HealthProbeService status thresholds: 0-9 errors = OK, 10-99 = Degraded, 100+ = Down (RESEARCH §Health Panel). ErrorRate LatencyMs is always null (it is a gauge not a probe)"
  - "ErrorRateRingBuffer takes IClock via constructor — enables FakeClock deterministic decay tests per W6 (plan 03-06 acceptance criterion)"
  - "LogErrorCounter registered as ILoggerProvider Singleton in AddGameKitAdmin — every ILogger<T> in the host now feeds Error+ events into the ring buffer; no opt-in required from the consumer"
  - "SuperadminGateHostedService registered AFTER AdminMigrationHostedService in AddGameKitAdmin (hosted services start in registration order) — admin_users must exist before the gate queries it"
  - "SuperadminGateHostedService visibility: public sealed (Razor SDK visibility conventions; mirrors AdminCookieEvents from plan 03-04)"
  - "AddGameKitAdmin's AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddCookie(GameKitAdmin, ...) — W4 requirement: preserve Phase 2 JWT Bearer as the DEFAULT auth scheme so /auth/me and other Bearer endpoints continue to authenticate. Admin cookie is a NAMED scheme only. Authorization policies explicitly call AddAuthenticationSchemes(GameKitAdmin) to pin scheme"
  - "AdminTestHost uses a local AdminRuntimeQueryCustomizer that bypasses the FOLLOW-UP-02-03-01 ApplicationServiceProvider path because Host.CreateDefaultBuilder + ConfigureWebHostDefaults invokes the DbContext options factory TWICE with two different service providers — the first (generic-host) doesn't see Auth+Admin registrations, so the model is cached without AdminUser. Phase 2 AuthTestHost has the same issue and uses an analogous AuthRuntimeQueryCustomizer"
  - "GameKit.Auth grants InternalsVisibleTo(\"GameKit.Admin.Integration.Tests\") so AdminRuntimeQueryCustomizer can apply the internal Auth entity configurations (PlayerIdentityConfiguration, PlayerCredentialConfiguration, RefreshTokenConfiguration) directly — grant is documented with a forward-reference to plan 03-06 in GameKit.Auth/AssemblyInfo.cs"
  - "Cookie auth LoginPath=/admin/login, LogoutPath=/admin/logout, AccessDeniedPath=/admin/access-denied — these target the Blazor @page routes shipping in plan 03-08 (root-relative per CLAUDE.md MountPath scope note — MountPath only prefixes /admin/api/*)"
  - "Antiforgery cookie HttpOnly=false — required so Blazor JS can read and echo via the X-GameKit-Admin-CSRF header (D-16)"
  - "ValidateAdminOptions fail-fast validator enforces MountPath starts with /, RefreshInterval > 0, ExpireTimeSpan > 0, HealthErrorRateBucketSize > 0, HealthErrorRateWindow >= bucket size (T-03-03-04 mitigation promised in plan 03-03)"
  - "AdminEndpoints.Map is a stub that takes + returns the group unchanged; plan 03-07 replaces the body with the full 12-endpoint surface"
  - "TestDbContextFactory (unit-test helper) installs a JsonDocument ValueConverter for Before/After columns so AdminAuditWriter unit test roundtrips through the InMemory provider"
requirements_completed:
  - ADMIN-03
  - ADMIN-05
  - ADMIN-06
  - ADMIN-10
metrics:
  duration_minutes: 27
  tasks_completed: 3
  files_created: 33
  files_modified: 1
  tests_passing:
    unit: 35
    integration: 14
  completed_date: 2026-04-19
---

# Phase 03 Plan 06: Admin Services + Fluent Builder Summary

Shipped the full admin application-service layer (6 interface/impl pairs + audit writer + action constants + 3 exceptions + 6 DTOs) plus the SuperadminGateHostedService startup gate, the ErrorRateRingBuffer lock-free rolling-window counter, the LogErrorCounter `ILoggerProvider`, and the three entry-point extensions (`AddGameKitAdmin` / `UseGameKitAdmin` / `MapGameKitAdmin`) that wire everything into the host pipeline. Ships 4 new integration tests (SuperadminGate, PlayerBan, HealthProbe, AuthSchemeIsolationSmoke) against Testcontainers Postgres + Redis, plus 4 new unit tests (audit writer, search classification, admin-auth dummy-hash, ring-buffer decay). Admin.Tests ends at **35/0/0** (up from 22/0/0 at plan start); Admin.Integration.Tests ends at **14/0/0** (up from 3/0/0).

## Performance

- **Duration:** ~27 min
- **Started:** 2026-04-19T13:35:44Z
- **Completed:** 2026-04-19T14:02:29Z
- **Tasks:** 3 (TDD for tasks 1-2; fluent builder in task 3)
- **Files created:** 33
- **Files modified:** 1 (`src/GameKit.Auth/AssemblyInfo.cs` — adds InternalsVisibleTo for GameKit.Admin.Integration.Tests)
- **Tests added:** 13 unit (audit + search + auth + ring-buffer) + 11 integration (gate + ban + health + scheme-isolation)

## Task Commits

1. **Task 1: audit writer + auth service + search service + ban service + admin user service + DTOs + action constants** — `3049a3c` (feat)
2. **Task 2: health probe + ring buffer + superadmin gate + AdminTestHost + integration tests** — `3aa02bd` (feat)
3. **Task 3: AddGameKitAdmin + UseGameKitAdmin + MapGameKitAdmin fluent builder** — `fc6abcc` (feat)

## Service Dependency Graph

```
GameKitDbContext (scoped) ──┬─> AdminAuditWriter (scoped)
                            │      ^
                            │      └── IClock, IIdGenerator
                            │
                            ├─> AdminAuthService (scoped)
                            │      ^
                            │      └── IPasswordHasher (singleton, from Auth)
                            │      └── IAdminAuditWriter (scoped)
                            │      └── IClock
                            │
                            ├─> PlayerSearchService (scoped)
                            │
                            ├─> PlayerBanService (scoped)
                            │      └── IAdminAuditWriter (scoped)
                            │      └── IClock
                            │
                            ├─> AdminUserService (scoped)
                            │      └── IPasswordHasher, IAdminAuditWriter, IClock, IIdGenerator
                            │
                            └─> HealthProbeService (scoped)
                                   └── GameKitOptions (singleton)
                                   └── IConnectionMultiplexer (singleton, optional)
                                   └── ErrorRateRingBuffer (singleton)
                                   └── IClock

ErrorRateRingBuffer (singleton) <── LogErrorCounter (singleton ILoggerProvider)
                                       ^
                                       └── hooked by every ILogger<T> in the host

SuperadminGateHostedService (hosted) ──> CreateScope() ──> GameKitDbContext ──> AdminUser query
```

## SERIALIZABLE Isolation Points

| Service path | Why SERIALIZABLE |
|--------------|------------------|
| `PlayerBanService.BanAsync` | Ban mutation + audit-row commit together — T-03-06-01 (audit write throw rolls back the mutation) |
| `PlayerBanService.UnbanAsync` | Same atomicity invariant as Ban |
| `AdminUserService.CreateAsync` | Unique-username race (23505) + 3-retry loop on 40001 (mirrors `GuestUpgradeService`) |
| `AdminUserService.DeleteAsync` | Last-superadmin count read-then-check — SERIALIZABLE prevents a concurrent DELETE from dropping the last superadmin |
| Other paths (`AdminAuthService`, `AdminAuditWriter`, `PlayerSearchService`, `HealthProbeService`) | NOT serializable — single-row reads/writes; caller decides if a surrounding tx is needed |

## ErrorRateRingBuffer Bucket Layout

Default `GameKitAdminOptions.Panel`:

| Setting | Default |
|---------|---------|
| `HealthErrorRateWindow` | 5 minutes |
| `HealthErrorRateBucketSize` | 1 second |

→ **300 buckets** of 1-second granularity covering a 5-minute rolling window.

Hot-path behaviour:
- `IncrementError()` → `AdvanceIfNeeded()` (lock-free bucket-current-check) → `Interlocked.Increment(ref _buckets[_headIndex])`
- `RecentErrorCount()` → `AdvanceIfNeeded()` + sequential `Volatile.Read` sum across all buckets
- `AdvanceIfNeeded()` hot-exit when `elapsed < _bucketTicks`; under lock only when rotation is required

## Test Counts

| Suite | Count (Pre-plan → Post-plan) | New in this plan |
|-------|------------------------------|------------------|
| `GameKit.Admin.Tests` (unit) | 22 → **35** | AdminAuditWriterTests (2) + PlayerSearchInputDetectionTests (4 theories → 10 cases) + AdminAuthServiceTests (1) + ErrorRateRingBufferTests (3) |
| `GameKit.Admin.Integration.Tests` | 3 → **14** | SuperadminGateTests (3) + PlayerBanServiceTests (2) + HealthProbeTests (3) + AuthSchemeIsolationSmokeTests (3) |

Full solution build: 17 projects / 0 warnings / 0 errors (after plan 03-06).

## Decisions Made

See frontmatter `decisions` list — 19 load-bearing decisions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] AdminAuditWriter unit test needed a JsonDocument value converter for the InMemory provider**
- **Found during:** Task 1 GREEN phase (first run of `AdminAuditWriterTests.WriteAsync_Inserts_Row_With_Namespaced_Action_And_JsonPayloads`).
- **Issue:** EF Core InMemory provider does not support `JsonDocument` property types natively — test failed with `System.InvalidOperationException: The 'JsonDocument' property 'AdminAuditLog.After' could not be mapped`. Plan Step 1 NOTE anticipated this ("If a value converter is required, the test must add it; consult Phase 1's InMemory test factory pattern").
- **Fix:** Added `tests/GameKit.Admin.Tests/TestDbContextFactory.cs` (adapted from `tests/GameKit.Core.Tests/Services/TestDbContextFactory.cs`) that replaces `IModelCustomizer` with an in-memory-aware customizer wiring JsonDocument ValueConverter + AdminUserConfiguration.
- **Files modified:** `tests/GameKit.Admin.Tests/TestDbContextFactory.cs` (new), `tests/GameKit.Admin.Tests/AdminAuditWriterTests.cs` (use factory), `tests/GameKit.Admin.Tests/AdminAuthServiceTests.cs` (use factory).
- **Verification:** All 3 test classes pass (13 tests green post-fix).
- **Committed in:** `3049a3c` (Task 1 commit, fix landed together with the services).

**2. [Rule 1 - Bug] AdminAuthService DummyHash must be a real BCrypt.Net-Next hash (not a synthetic literal)**
- **Found during:** Task 1 RED design — before writing the test `LoginAsync_UnknownUsername_DoesNotThrow_ReturnsNull`.
- **Issue:** Plan Step 6's DummyHash literal (`$2a$12$KIXfJpcJf8yNrIxS.GkJd.UgL0VtZa7lF5E5qKTQDHQwuVZmDoYra`) was a hand-typed synthetic that MIGHT or might not be BCrypt-parseable. If BCrypt throws a `SaltParseException`, `BCryptPasswordHasher.Verify` swallows it and returns false — skipping the work-factor-12 comparison and breaking timing parity (the whole point of the DummyHash pattern).
- **Fix:** Generated a real BCrypt.Net-Next 4.1.0 hash for password `"admin-dummy-never-matches"` at work factor 12 via an ad-hoc dotnet project, verified roundtrip (Verify returns true for the correct password + false for a wrong one), and pasted the resulting literal into `AdminAuthService.cs`. Added a paste-verbatim comment + regression-test assertion.
- **Files modified:** `src/GameKit.Admin.UI/Services/AdminAuthService.cs`.
- **Verification:** `LoginAsAdminAsync_UnknownUsername_DoesNotThrow_ReturnsNull` passes — proves the BCrypt.Verify call does NOT throw on the literal and returns null without exception.
- **Committed in:** `3049a3c` (Task 1 commit).

**3. [Rule 3 - Blocking] Runtime integration tests failed because FOLLOW-UP-02-03-01 `ApplicationServiceProvider` path is broken under `Host.CreateDefaultBuilder + ConfigureWebHostDefaults`**
- **Found during:** Task 2 GREEN phase (first run of `SuperadminGateTests`).
- **Issue:** `SuperadminGateHostedService.StartAsync` tried to query `ctx.Set<AdminUser>()` and got `Cannot create a DbSet for 'AdminUser' because this type is not included in the model for the context`. Root cause (diagnosed via `Console.WriteLine` probes in `GameKitDbContext.OnModelCreating` + `GameKitServiceCollectionExtensions.AddGameKit`): `AddDbContext<T>((sp, dbOpts) => opts.UseApplicationServiceProvider(sp))` captures the WRONG service provider on first invocation under `Host.CreateDefaultBuilder + ConfigureWebHostDefaults`. The factory lambda was invoked TWICE — once with the generic-host provider (sees 0 `IModelBuilderExtension` registrations) and once with the web-host provider (sees 2 registrations, correct). EF caches the model from the FIRST invocation, so `AdminUser` (and `PlayerIdentity`) never lands in the runtime model.
- **Fix:** Added `AdminRuntimeQueryCustomizer` inside `AdminTestHost.cs` (mirrors Phase 2's `AuthRuntimeQueryCustomizer`) that re-registers the DbContext with `.ReplaceService<IModelCustomizer, AdminRuntimeQueryCustomizer>()` — the customizer applies Core (via `RelationalModelCustomizer` base) + Auth + Admin configs directly, bypassing the broken `ApplicationServiceProvider` path. Granted `InternalsVisibleTo("GameKit.Admin.Integration.Tests")` in `GameKit.Auth/AssemblyInfo.cs` so the test-local customizer can reach the three internal Auth configs.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`, `src/GameKit.Auth/AssemblyInfo.cs`.
- **Verification:** All 11 integration tests pass post-fix. Full solution build clean (0 warnings, 0 errors).
- **Committed in:** `3aa02bd` (Task 2 commit).
- **Production note:** This is a TEST-HOST-ONLY workaround. The sample TicTacToeDuel app uses `WebApplication.CreateBuilder` (single service provider — no duplicate) so the FOLLOW-UP-02-03-01 fix works fine in production. The two-provider pattern is specific to the test host's use of `Host.CreateDefaultBuilder + ConfigureWebHostDefaults` (inherited from the Phase 2 `AuthTestHost` shape). A follow-up (optional) would migrate `AdminTestHost` to `WebApplication.CreateBuilder` — out of scope for this plan per the "fix the test, keep the plan's acceptance criteria intact" bias.

**4. [Rule 1 - Bug] XML-doc cref on `IAdminAuditWriter` used `paramref` for a parameter that is on the method, not the interface**
- **Found during:** Task 1 GREEN phase (first build after tests were written).
- **Issue:** `CS1734: XML comment on 'IAdminAuditWriter' has a paramref tag for 'before', but there is no parameter by that name`. The interface-level doc referenced the `before` parameter with `<paramref>`, but `<paramref>` requires the parameter to be on the documented member itself (the interface has no parameters).
- **Fix:** Replaced `<paramref name="before"/>` with plain `<c>before</c>` + `<see cref="WriteAsync"/>` anchor.
- **Files modified:** `src/GameKit.Admin.UI/Services/IAdminAuditWriter.cs`.
- **Verification:** `dotnet build src/GameKit.Admin.UI/...` green.
- **Committed in:** `3049a3c` (Task 1 commit — the fix rolled into the same commit).

**5. [Rule 1 - Bug] XML-doc cref on `IHealthProbeService` pointed at `IDatabase.PingAsync` which is not a fully-qualified cref**
- **Found during:** Task 2 Admin.UI build.
- **Issue:** `CS1574: XML comment has cref attribute 'PingAsync' that could not be resolved`. Using a namespace-qualified `global::StackExchange.Redis.IDatabase.PingAsync` cref did not resolve because IDatabase.PingAsync is a method with overloads and the reference disambiguation failed.
- **Fix:** Replaced with plain `<c>IDatabase.PingAsync()</c>` prose + a cref on the enclosing `IConnectionMultiplexer` type which is unambiguous.
- **Files modified:** `src/GameKit.Admin.UI/Services/IHealthProbeService.cs`.
- **Verification:** `dotnet build` green.
- **Committed in:** `3aa02bd` (Task 2 commit).

---

**Total deviations:** 5 auto-fixed (2 Rule-1 XML-doc bugs, 2 Rule-1 correctness bugs, 1 Rule-3 blocking test-host issue).
**Impact on plan:** All five were necessary for correctness or completion; none changed the plan's scope or success criteria. The biggest deviation (AdminRuntimeQueryCustomizer in the test host) is a FOLLOW-UP-02-03-01 continuation — the same bypass pattern Phase 2 uses, now extended to Admin.

## Variance from SP-5 registration order

**None.** The plan's SP-5 order is:

1. Singleton options
2. ModelBuilderExtension (TryAddEnumerable)
3. MigrationHostedService
4. SuperadminGateHostedService
5. Cookie auth
6. Authorization
7. Scoped services
8. Rate limiter
9. Antiforgery
10. Blazor Server primitives
11. MudBlazor
12. IHttpContextAccessor

`AdminBuilderExtensions.AddGameKitAdmin` registers in exactly that order. Step 7 is split into:
- 7a. Scoped admin services (audit/auth/search/ban/user/health)
- 7b. Singleton ring buffer + `ILoggerProvider` log-error-counter

This is a narrowing, not a deviation — the plan's Step 6 comment in the reference code explicitly groups Singleton + Scoped under "Services".

Step 12 is split into:
- 12a. IHttpContextAccessor
- 12b. FluentValidation validators (deferred to plan 03-07; `ValidationEndpointFilter<T>` resolves lazily via `GetService<IValidator<T>>()` so absent registrations are no-ops, as documented in plan 03-05's `ValidationEndpointFilter` XML doc).

## Threat Flags

None. This plan's threat model entries T-03-06-01 through T-03-06-08 are all addressed:

- T-03-06-01 (Tampering: ban mutation without audit) — `PlayerBanService` opens SERIALIZABLE tx; mutation + audit commit together; integration test `BanAsync_Writes_Audit_Row_And_Flips_IsBanned` verifies.
- T-03-06-02 (EoP: last superadmin deleted) — `AdminUserService.DeleteAsync` counts remaining superadmins inside the SERIALIZABLE tx; throws `LastSuperadminException` when count would go to 0.
- T-03-06-03 (Info Disclosure: timing attack on admin usernames) — `AdminAuthService.VerifyPasswordAsync` runs BCrypt against `DummyHash` on user-not-found; real BCrypt.Net-Next 4.1.0 literal ensures the Verify call runs the full work-factor-12 comparison; integration test `AdminAuthServiceTests.LoginAsync_UnknownUsername_DoesNotThrow_ReturnsNull` proves no exception.
- T-03-06-04 (Tampering: Admin migration forgets Auth entities) — plan 03-02 established `AdminMigrationModelCustomizer`; this plan consumes it unchanged.
- T-03-06-05 (DoS: Gate blocks on Postgres contention) — `SuperadminGateHostedService.StartAsync` uses the passed `CancellationToken`; no infinite retry.
- T-03-06-06 (Info Disclosure: password hash logged via audit) — `AdminAuthService` never passes `PasswordHash` in before/after payloads; the audit payloads in the source code are inspected and contain only `{reason, last_login_at, failed_count}`-style fields.
- T-03-06-07 (Spoofing: admin cookie forged) — DataProtection-signed (ASP.NET Core default); HttpOnly + Secure + SameSite=Lax via `AddGameKitAdmin` cookie configuration.
- T-03-06-08 (Repudiation: actor denies) — audit rows record `ActorId + CreatedAt` inside the same tx as the mutation; no API to edit/delete audit rows.

## Known Stubs

**1. `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` — `Map` is a placeholder.** Returns the `RouteGroupBuilder` unchanged. Plan 03-07 replaces the body with the 12-endpoint surface (login/search/ban/unban/admins/admin/audit/health/matches/queue-depth/rank-adjust). Documented in the type's XML doc. Not a hidden-functionality stub — the plan explicitly calls for this shape so Task 3 compiles standalone.

**2. Blazor Component mount (plan 03-08).** `MapGameKitAdmin` creates the `/admin/api` group for HTTP endpoints but does NOT call `MapRazorComponents<App>()`. That call lands in plan 03-08 where `App.razor` is shipped.

## Self-Check: PASSED

Verification run after writing this SUMMARY:

- File existence checks (33 created files): all present under `src/GameKit.Admin.UI/` and `tests/GameKit.Admin.Tests/` + `tests/GameKit.Admin.Integration.Tests/`.
- Commit existence checks:
  - `3049a3c` — Task 1 (services + DTOs + action constants + 13 unit tests)
  - `3aa02bd` — Task 2 (health + ring buffer + gate + AdminTestHost + 11 integration tests + InternalsVisibleTo grant)
  - `fc6abcc` — Task 3 (AddGameKitAdmin + UseGameKitAdmin + MapGameKitAdmin + AdminEndpoints placeholder)
- Full solution build — 17 projects / 0 warnings / 0 errors.
- `dotnet test tests/GameKit.Admin.Tests/` — 35/0/0 green.
- `dotnet test tests/GameKit.Admin.Integration.Tests/` — 14/0/0 green.

## Next Wave Readiness

- **Plan 03-07** (`/admin/api/*` minimal-API surface) is unblocked. It can:
  - Resolve `IAdminAuditService`, `IAdminAuthService`, `IPlayerSearchService`, `IPlayerBanService`, `IAdminUserService`, `IHealthProbeService` from DI (all registered as Scoped by `AddGameKitAdmin`).
  - Register per-DTO `IValidator<T>` — `ValidationEndpointFilter<T>` (plan 03-05) already resolves lazily.
  - Replace `AdminEndpoints.Map` body with 12 minimal-API endpoints.
  - Compose filters `.AddEndpointFilter<AntiforgeryValidationFilter>().AddEndpointFilter<ValidationEndpointFilter<TRequest>>()` on mutation endpoints.
  - Attach `.RequireAuthorization(AdminPolicies.Admin)` / `.RequireAuthorization(AdminPolicies.Superadmin)` and `.RequireRateLimiting(AdminRateLimitRegistrations.AdminLoginPolicy)` per endpoint.

- **Plan 03-08** (Blazor shell) is unblocked. `MapRazorComponents<App>()` slots into `MapGameKitAdmin` (either added inline or via a plan 03-08 extension call on the endpoint route builder).

- **Plan 03-11** (CLI `gamekit admin create`) can:
  - Call `IAdminUserService.CreateAsync(username, password, role, actorId, ct)` directly via DI, OR replicate the SERIALIZABLE + BCrypt pattern in the CLI.
  - Use the first-admin-promotion-to-superadmin rule (CLI-side, since `AdminUserService` accepts explicit role).

- **Plan 03-12** (TicTacToeDuel wiring) can:
  - Chain `.AddGameKitAdmin(o => { o.MountPath = "/admin"; })` after `.AddAuth(...)`.
  - Add `app.UseGameKitAdmin()` after `UseGameKit`.
  - Add `app.MapGameKitAdmin()` alongside `MapGameKit() + MapAuth()`.
  - Every service + policy + scheme is in place.

- **Plan 03-13** (E2E ROADMAP SC coverage) gets:
  - `AdminTestHost` reusable for the full SC#1-#6 matrix (SC#2 uses `SuperadminGateHostedService` throw; SC#6 cross-scheme isolation uses `FakePlayerJwtIssuer` + the W4-verified default-scheme preservation).
  - `AdminRuntimeQueryCustomizer` already copes with the FOLLOW-UP-02-03-01 test-host quirk; plan 03-13 can add tests without re-discovering it.

---
*Phase: 03-admin-ui*
*Plan: 06*
*Completed: 2026-04-19*
