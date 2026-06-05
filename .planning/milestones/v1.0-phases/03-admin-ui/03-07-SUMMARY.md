---
phase: 03-admin-ui
plan: 07
subsystem: admin-ui
tags:
  - admin-ui
  - http-api
  - minimal-apis
  - fluent-validation
  - antiforgery
  - rate-limiting
  - wave-4
dependencies:
  requires:
    - phase: 03-03
      provides: AdminAuthenticationSchemeConstants + AdminPolicies + GameKitAdminOptions (consumed by LoginAsync SignInAsync + per-endpoint RequireAuthorization + RememberMeDuration)
    - phase: 03-04
      provides: AdminRateLimitRegistrations.AdminLoginPolicy (consumed by /login .RequireRateLimiting)
    - phase: 03-05
      provides: AntiforgeryValidationFilter + ValidationEndpointFilter<T> (consumed by every mutation endpoint + search validator)
    - phase: 03-06
      provides: IAdminAuthService / IPlayerSearchService / IPlayerBanService / IAdminUserService / IHealthProbeService / IAdminAuditWriter / AdminAuditActions (every endpoint handler depends on one or more of these) + AdminTestHost / WebApplicationFactoryExtensions
  provides:
    - 6 DTOs under GameKit.Admin.UI.Http.Contracts (LoginRequest / BanPlayerRequest / UnbanPlayerRequest / CreateAdminRequest / PlayerSearchRequest / GdprDeleteRequest)
    - 4 FluentValidation validators under GameKit.Admin.UI.Http.Validators (LoginRequestValidator / BanPlayerRequestValidator / CreateAdminRequestValidator / PlayerSearchRequestValidator)
    - Validator DI registrations wired into AdminBuilderExtensions.AddGameKitAdmin (Step 13 — fills the placeholder comment left by plan 03-06)
    - AdminEndpoints.Map — 12 minimal-API endpoints replacing the plan 03-06 placeholder stub
  affects:
    - 03-08 (Blazor components call GET /admin/api/health for the dashboard tile + GET /admin/api/audit for the audit page + GET /admin/api/match-history for per-player history)
    - 03-09 (admin Blazor forms POST /admin/api/players/{id}/ban with CSRF header + /admin/api/admins superadmin CRUD)
    - 03-11 (CLI gamekit admin create can call POST /admin/api/admins instead of bypassing the service — the SERIALIZABLE + last-superadmin guard already ride via AdminUserService)
    - 03-13 (CrossSchemeIsolationTests can now assert BOTH cookie + Bearer schemes: Bearer player-JWT → 404 at /admin/api/* paths; cookie-less hits → 404; GET /health with admin cookie → 200)
tech-stack:
  added: []
  patterns:
    - "Minimal-API endpoint group with per-endpoint composed filters: .RequireAuthorization(AdminPolicies.*).AddEndpointFilter<AntiforgeryValidationFilter>().AddEndpointFilter<ValidationEndpointFilter<T>>() (mutations). Mirrors the GameKit.Auth AuthEndpoints shape exactly; diverges only in (a) cookie-based SignInAsync on /login, (b) antiforgery filters on mutations."
    - "[AsParameters] binding for query-string DTOs (PlayerSearchRequest) so ValidationEndpointFilter<PlayerSearchRequest> still runs without a JSON body — D-16 antiforgery cleanly stays off GET paths per W8."
    - "Logout handler cast to Delegate so ASP.NET Core 10's ASP0016 analyzer binds the IResult return value as the response body instead of treating the single-HttpContext parameter as a plain RequestDelegate."
    - "Nested record types (AuditQuery / AuditRow / MatchHistoryRow) co-located with the endpoint mapping to keep admin-only projection shapes out of the Contracts namespace (those live in Contracts only when Blazor / CLI plans need them cross-project)."
    - "LoginAsRoot test helper plumbs Set-Cookie head back into the client's default Cookie header because TestServer's default HttpMessageHandler does NOT persist cookies across calls (unlike WebApplicationFactory's cookie-capable handler)."
  explicitly_not_added:
    - "NOT adding CookieContainer-capable HttpMessageHandler to AdminTestHost — cookie plumbing is a per-test concern; not all tests need a logged-in client and making TestServer persist cookies would pollute rate-limit tests that require cold partitions."
    - "NOT adding an /admin/api/ping health-check — /admin/api/health returns the 3-probe HealthReport which is the canonical health surface."
key-files:
  created:
    - src/GameKit.Admin.UI/Http/Contracts/LoginRequest.cs
    - src/GameKit.Admin.UI/Http/Contracts/BanPlayerRequest.cs
    - src/GameKit.Admin.UI/Http/Contracts/UnbanPlayerRequest.cs
    - src/GameKit.Admin.UI/Http/Contracts/CreateAdminRequest.cs
    - src/GameKit.Admin.UI/Http/Contracts/PlayerSearchRequest.cs
    - src/GameKit.Admin.UI/Http/Contracts/GdprDeleteRequest.cs
    - src/GameKit.Admin.UI/Http/Validators/LoginRequestValidator.cs
    - src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs
    - src/GameKit.Admin.UI/Http/Validators/CreateAdminRequestValidator.cs
    - src/GameKit.Admin.UI/Http/Validators/PlayerSearchRequestValidator.cs
    - tests/GameKit.Admin.Tests/BanPlayerRequestValidatorTests.cs
    - tests/GameKit.Admin.Tests/CreateAdminRequestValidatorTests.cs
    - tests/GameKit.Admin.Integration.Tests/AdminLoginEndpointTests.cs
    - tests/GameKit.Admin.Integration.Tests/PlayerSearchEndpointTests.cs
  modified:
    - src/GameKit.Admin.UI/Http/AdminEndpoints.cs
    - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
decisions:
  - "BanPlayerRequestValidator emits the three literal error messages verbatim per plan acceptance: 'A reason is required.', 'Reason must be at least 3 characters.', 'Reason is too long (max 512 characters.)'. These strings are ROADMAP SC anchors — tests assert on them word-for-word, so future copy changes require updating the integration test first."
  - "CreateAdminRequestValidator uses RegexOptions.Compiled + CultureInvariant for the ^[a-z0-9_-]{3,32}$ regex — matches Phase-2 RegisterRequestValidator convention (compiled once per validator instance via a private static readonly)."
  - "PlayerSearchRequestValidator caps page size at [1, 50] (T-03-07-05 DoS mitigation). PlayerSearchService also clamps to the same bounds — defense-in-depth."
  - "LoginRequest is a 3-param positional record with RememberMe — supports the D-01 30-day remember-me window. The endpoint keys off req.RememberMe to set AuthenticationProperties.IsPersistent + ExpiresUtc using GameKitAdminOptions.Cookie.RememberMeDuration (wired up in plan 03-03)."
  - "GdprDeleteRequest includes a ConfirmUsername field (defense-in-depth against misclicks on the list view); the endpoint echoes the value into the admin.player.gdpr_delete audit payload so the audit trail captures what the admin typed AT the moment of deletion, not just the server-side display-name state."
  - "/gdpr-delete handler catches PlayerNotFoundException and maps to 404 with a structured body (error = 'player_not_found'). PlayerNotFoundException is defined in GameKit.Core."
  - "/admins POST handler catches AdminUsernameAlreadyTakenException and maps to 409 with a structured body (error = 'username_taken', username). /admins DELETE handler catches KeyNotFoundException → 404 and LastSuperadminException → 409 — aligns with Phase-2 patterns (UsernameAlreadyTakenException → 409 in RegisterAsync)."
  - "/logout cast to Delegate to resolve ASP0016: LogoutAsync(HttpContext) has a single parameter of type HttpContext, which the ASP0016 analyzer treats as a plain RequestDelegate and would discard the returned IResult. Casting keeps the handler symmetric with Phase-2 AuthEndpoints shape."
  - "/players/search read-only GET — NO antiforgery filter per W8/D-16 (antiforgery applies to mutations only). Binds via [AsParameters] PlayerSearchRequest from the query string so the filter chain still runs the validator."
  - "/audit handler uses nested AuditRow projection to prevent the JsonDocument-typed Before/After columns from serializing (they roundtrip through EF JsonDocument values which would require a JsonConverter; the admin UI will fetch full before/after via a future /audit/{id} detail endpoint). Only the audit row metadata is returned."
  - "/match-history joins SessionParticipant → GameSession manually because SessionParticipantConfiguration defines the FK via HasOne<GameSession>().WithMany() with no navigation property (Phase-1 decision to keep SessionParticipant navigation-free for GDPR cascade clarity). Orders by GameSession.CompletedAt DESC; filters to GameSessionState.Completed only."
  - "AdminEndpoints.Map retained under 'public static class AdminEndpoints' — matches the plan 03-06 shape exactly so AdminApplicationBuilderExtensions.MapGameKitAdmin continues to compile without signature changes."
  - "BanPlayer antiforgery integration test sets host.Client.BaseAddress = new Uri('https://localhost/') to sidestep the AntiforgeryOptions.Cookie.SecurePolicy = Always SSL pre-check under TestServer (which defaults to http://localhost/). Production serves over HTTPS so this is a test-host detail only."
requirements_completed:
  - ADMIN-02
  - ADMIN-05
  - ADMIN-06
  - ADMIN-07
  - ADMIN-08
  - ADMIN-12
metrics:
  duration_minutes: 10
  tasks_completed: 3
  files_created: 14
  files_modified: 2
  tests_passing:
    unit: 54
    integration: 23
  completed_date: 2026-04-19
---

# Phase 03 Plan 07: /admin/api/* 12-Endpoint Minimal-API Surface Summary

Shipped the full `/admin/api/*` minimal-API surface — 12 endpoints, 6 DTOs, 4 FluentValidation validators, and 9 new integration tests covering login success + invalid-creds + unknown-user + empty-username validation + rate-limit 429 + player search across all three input modes + antiforgery enforcement on mutations. Admin HTTP contract is now feature-complete for plan 03-09 (Blazor admin pages) to consume via `HttpClient`.

## Performance

- **Duration:** approximately 10 min
- **Started:** 2026-04-19T14:12:39Z
- **Completed:** 2026-04-19T14:22:21Z
- **Tasks:** 3 (Task 1 TDD RED-GREEN; Tasks 2 + 3 straight-through implementation)
- **Files created:** 14
- **Files modified:** 2
- **Tests added:** 19 unit (4 ban + 15 create-admin theories) + 9 integration (5 login + 4 search/antiforgery)

## Task Commits

1. **Task 1: Admin DTOs + FluentValidation validators + DI registration** — `3a21aff` (feat) — TDD RED-GREEN.
2. **Task 2: AdminEndpoints.cs minimal-API /admin/api/* surface** — `b1bbe23` (feat).
3. **Task 3: Admin login / rate-limit / antiforgery / player-search integration tests** — `858364e` (test).

## Endpoint Matrix

| Method | Path | Authorization | Filter Chain | Handler |
|--------|------|---------------|--------------|---------|
| POST | `/login` | AllowAnonymous | RequireRateLimiting(AdminLoginPolicy) + ValidationEndpointFilter&lt;LoginRequest&gt; | `LoginAsync` — `HttpContext.SignInAsync("GameKitAdmin")` |
| POST | `/logout` | AllowAnonymous (cookie presence is auth) | — | `LogoutAsync` (cast to Delegate for ASP0016) — `SignOutAsync("GameKitAdmin")` |
| GET | `/players/search` | AdminPolicies.Admin | ValidationEndpointFilter&lt;PlayerSearchRequest&gt; | `SearchPlayersAsync` — [AsParameters] binding, NO antiforgery per W8 |
| POST | `/players/{id:guid}/ban` | AdminPolicies.Admin | AntiforgeryValidationFilter + ValidationEndpointFilter&lt;BanPlayerRequest&gt; | `BanPlayerAsync` — IPlayerBanService.BanAsync |
| POST | `/players/{id:guid}/unban` | AdminPolicies.Admin | AntiforgeryValidationFilter | `UnbanPlayerAsync` — reason optional, no validator |
| POST | `/players/{id:guid}/gdpr-delete` | AdminPolicies.Superadmin | AntiforgeryValidationFilter | `GdprDeletePlayerAsync` — IGdprDeleteService + admin-audit row |
| GET | `/admins` | AdminPolicies.Superadmin | — | `ListAdminsAsync` — hash-free projection |
| POST | `/admins` | AdminPolicies.Superadmin | AntiforgeryValidationFilter + ValidationEndpointFilter&lt;CreateAdminRequest&gt; | `CreateAdminAsync` — 409 on AdminUsernameAlreadyTakenException |
| DELETE | `/admins/{id:guid}` | AdminPolicies.Superadmin | AntiforgeryValidationFilter | `DeleteAdminAsync` — 404 on KeyNotFoundException, 409 on LastSuperadminException |
| GET | `/audit` | AdminPolicies.Admin | — | `GetAuditAsync` — keyset pagination on (CreatedAt DESC, Id DESC), optional action filter, page size [1, 100] |
| GET | `/match-history` | AdminPolicies.Admin | — | `GetMatchHistoryAsync` — manual SessionParticipant → GameSession join (no nav prop) |
| GET | `/health` | AdminPolicies.Admin | — | `GetHealthAsync` — IHealthProbeService.ProbeAsync |

## Validator Rules + Literal Error Messages

### BanPlayerRequestValidator (D-09)

| Rule | Message (verbatim — ROADMAP SC anchors) |
|------|------------------------------------------|
| `NotEmpty` | `"A reason is required."` |
| `MinimumLength(3)` | `"Reason must be at least 3 characters."` |
| `MaximumLength(512)` | `"Reason is too long (max 512 characters)."` |

### CreateAdminRequestValidator (D-06)

| Rule | Message |
|------|---------|
| `NotEmpty` + regex `^[a-z0-9_-]{3,32}$` | `"Username must be 3-32 chars, lowercase letters, digits, underscore, or hyphen."` |
| `MinimumLength(8)` on Password | `"Password must be at least 8 characters."` |
| Role in `{admin, superadmin}` | `"Role must be 'admin' or 'superadmin'."` |

### LoginRequestValidator

| Rule | Scope |
|------|-------|
| Username: NotEmpty + MaxLength 32 | Matches admin_users.username citext column limit (32) |
| Password: NotEmpty + MaxLength 256 | Oversized-body DoS defense before BCrypt cost |

### PlayerSearchRequestValidator

| Rule | Scope |
|------|-------|
| Query: NotEmpty + MaxLength 256 | Ample for UUID / provider:external_id / display-name prefix |
| PageSize: InclusiveBetween(1, 50) | T-03-07-05 DoS; service-layer clamp mirrors (defense-in-depth) |

## Test Counts

| Suite | Count (Pre-plan → Post-plan) | New in this plan |
|-------|------------------------------|------------------|
| `GameKit.Admin.Tests` (unit) | 35 → **54** | BanPlayerRequestValidatorTests (4) + CreateAdminRequestValidatorTests (15 theories / facts) |
| `GameKit.Admin.Integration.Tests` | 14 → **23** | AdminLoginEndpointTests (5) + PlayerSearchEndpointTests (4) |

Full solution build (`dotnet build GameKit.sln -c Debug --nologo`) — **17 projects / 0 warnings / 0 errors.**

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `POST /logout` handler triggered ASP0016 analyzer error**
- **Found during:** Task 2 first build after writing AdminEndpoints.cs.
- **Issue:** `LogoutAsync(HttpContext)` has a single `HttpContext` parameter, which the ASP.NET Core 10 analyzer ASP0016 interprets as a plain `RequestDelegate` (whose `Task` return value is discarded). The `Task<IResult>` return would never reach the response.
- **Fix:** Cast the handler reference to `Delegate` at map time — `group.MapPost("/logout", (Delegate)LogoutAsync).AllowAnonymous()` — forcing the route-handler path so ASP.NET writes the `IResult` return value to the response.
- **Files modified:** `src/GameKit.Admin.UI/Http/AdminEndpoints.cs`.
- **Verification:** `dotnet build src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` succeeds (0 warnings / 0 errors).
- **Committed in:** `b1bbe23` (Task 2 commit).

**2. [Rule 3 - Blocking] `BanPlayer_WithoutAntiforgeryToken_Returns400CsrfValidationFailed` threw `InvalidOperationException` under TestServer**
- **Found during:** Task 3 GREEN phase (initial test run).
- **Issue:** `AddGameKitAdmin` configures `AntiforgeryOptions.Cookie.SecurePolicy = Always` (production-safe default). `DefaultAntiforgery.ValidateRequestAsync` pre-checks `HttpContext.Request.IsHttps` and throws `InvalidOperationException("The antiforgery system has the configuration value AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an SSL request.")` before the normal `AntiforgeryValidationException`. TestServer's default `HttpClient.BaseAddress = "http://localhost/"` so all requests are HTTP. The filter's `catch (AntiforgeryValidationException)` does not intercept this exception, so the test got a 500 instead of a 400.
- **Fix:** Set `host.Client.BaseAddress = new Uri("https://localhost/")` inside the failing test. TestServer honors the scheme of the request URI, so the pre-check passes and the normal "missing token" code path runs, raising `AntiforgeryValidationException` which the filter catches and maps to 400 + `csrf_validation_failed`.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/PlayerSearchEndpointTests.cs`.
- **Verification:** Test passes on rerun (all 9 new integration tests green).
- **Committed in:** `858364e` (Task 3 commit).
- **Production note:** This is a TEST-HOST detail. Production serves the admin UI over HTTPS, so the pre-check always passes. The filter continues to behave correctly in both paths.

**3. [Rule 1 - Bug] Plan-reference `IAdminUserService.ListAsync` signature mismatch**
- **Found during:** Task 2 writing `ListAdminsAsync` handler.
- **Issue:** Plan Task 2 reference code called `svc.ListAsync(afterId: null, pageSize: 50, ct)` but plan 03-06's `IAdminUserService.ListAsync` signature is `ListAsync(CancellationToken)` — no pagination parameters (service returns the full list; admin count is low enough).
- **Fix:** Call `svc.ListAsync(ct)` and let the handler project to the hash-free DTO in-line. No pagination added (defer to plan 03-09 if the Blazor admins page needs it — service surface is easily extended then).
- **Files modified:** `src/GameKit.Admin.UI/Http/AdminEndpoints.cs`.
- **Verification:** `dotnet build GameKit.sln` green; ListAdminsAsync returns `IReadOnlyList<AdminUser>` projected to hash-free records.
- **Committed in:** `b1bbe23` (Task 2 commit).

**4. [Rule 1 - Bug] Plan-reference `IHealthProbeService.GetReportAsync` and `SessionParticipant.Delta`/`.Session!.EndedAt` do not exist**
- **Found during:** Task 2 writing `GetHealthAsync` + `GetMatchHistoryAsync` handlers.
- **Issue:** Plan reference code used `IHealthProbeService.GetReportAsync` (actual: `ProbeAsync`), `p.Session!.EndedAt` (actual: no `Session` navigation property; `GameSession.CompletedAt`, not `EndedAt`), `p.Delta` (actual: `RatingDelta`), and `p.Session!.LadderId` (actual: field is on `GameSession`, not accessible via nav prop).
- **Fix:** Rewrote the handlers to call `ProbeAsync`, replaced `EndedAt` with `CompletedAt`, replaced `Delta` with `RatingDelta`, and used a manual query join `from p in SessionParticipants join s in GameSessions on p.SessionId equals s.Id` (SessionParticipantConfiguration defines the FK via `HasOne<GameSession>().WithMany()` with no nav — Phase-1 decision for GDPR cascade clarity).
- **Files modified:** `src/GameKit.Admin.UI/Http/AdminEndpoints.cs`.
- **Verification:** `dotnet build` green.
- **Committed in:** `b1bbe23` (Task 2 commit).

---

**Total deviations:** 4 auto-fixed (3 Rule-1 drift between plan reference code and the actual service/entity surfaces in the repo; 1 Rule-3 blocking test-host SSL pre-check).
**Impact on plan:** None of these changed the plan's scope or acceptance criteria. The ASP0016 fix is a one-cast idiom change; the HTTPS BaseAddress is a one-line test fix; the service signature drifts were cosmetic (the plan's reference code used different names for the same concepts). All acceptance criteria met verbatim — 12 Map* calls with exact filter/policy chains, SignIn/SignOutAsync("GameKitAdmin"), ban-validator error messages verbatim, login rate-limit pinned.

## Threat Flags

None. The `<threat_model>` entries T-03-07-01 through T-03-07-08 are all addressed:

- T-03-07-01 (Tampering: CSRF on mutation) — Every POST/DELETE has `.AddEndpointFilter<AntiforgeryValidationFilter>()`. `BanPlayer_WithoutAntiforgeryToken_Returns400CsrfValidationFailed` proves 400 + `csrf_validation_failed` body.
- T-03-07-02 (EoP: admin → superadmin endpoints) — `.RequireAuthorization(AdminPolicies.Superadmin)` on 5 endpoints (/gdpr-delete, /admins GET/POST, /admins/{id} DELETE). Cookie-events returns 403 on role mismatch.
- T-03-07-03 (Info Disclosure: ban reason HTML injection) — Validator caps reason at 512 chars; Blazor's default output encoding mitigates the rendered-side risk (plan 03-09); audit log writer does not interpolate reason into a log-message template.
- T-03-07-04 (Spoofing: forged Bearer on admin endpoints) — `AddPolicy.AddAuthenticationSchemes(GameKitAdmin)` on both admin policies pins the scheme. Plan 03-13 CrossSchemeIsolationTests prove E2E. Smoke in `AuthSchemeIsolationSmokeTests` (plan 03-06) already confirms admin paths 404 anonymously.
- T-03-07-05 (DoS: unbounded page size) — `PlayerSearchRequestValidator` caps PageSize to [1, 50]; `PlayerSearchService` also clamps (defense-in-depth); `GetAuditAsync` clamps to [1, 100]; `GetMatchHistoryAsync` clamps to [1, 50].
- T-03-07-06 (Tampering: ban without actor) — `GetAdminId(http)` extracts `ClaimTypes.NameIdentifier`; throws on missing (cookie principal must have NameIdentifier — `LoginAsync` always populates).
- T-03-07-07 (EoP: GDPR delete by admin) — `.RequireAuthorization(AdminPolicies.Superadmin)` on `/gdpr-delete`.
- T-03-07-08 (Info Disclosure: audit full rows) — Accepted; admins by design have audit-log access. `AuditRow` projection deliberately excludes Before/After JSON payloads (plan 03-09 can fetch them via a future `/audit/{id}` detail endpoint).

## Known Stubs

None. All 12 endpoints have concrete handlers wired to the plan-03-06 service layer. Every validator is registered. No placeholder returns or `NotImplementedException`.

## Self-Check: PASSED

Verification run after writing this SUMMARY:

- File existence checks (14 created files):
  - `src/GameKit.Admin.UI/Http/Contracts/LoginRequest.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Contracts/BanPlayerRequest.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Contracts/UnbanPlayerRequest.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Contracts/CreateAdminRequest.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Contracts/PlayerSearchRequest.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Contracts/GdprDeleteRequest.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Validators/LoginRequestValidator.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Validators/CreateAdminRequestValidator.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/Validators/PlayerSearchRequestValidator.cs` — FOUND
  - `tests/GameKit.Admin.Tests/BanPlayerRequestValidatorTests.cs` — FOUND
  - `tests/GameKit.Admin.Tests/CreateAdminRequestValidatorTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/AdminLoginEndpointTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/PlayerSearchEndpointTests.cs` — FOUND
- Commit existence checks:
  - `3a21aff` — FOUND (Task 1)
  - `b1bbe23` — FOUND (Task 2)
  - `858364e` — FOUND (Task 3)
- Full solution build — 17 projects / 0 warnings / 0 errors.
- `dotnet test tests/GameKit.Admin.Tests/` — 54/0/0 green.
- `dotnet test tests/GameKit.Admin.Integration.Tests/` — 23/0/0 green.
- `grep -cE 'group\.Map(Post|Get|Delete)' src/GameKit.Admin.UI/Http/AdminEndpoints.cs` = 12.
- Verification greps: `SignInAsync(AdminAuthenticationSchemeConstants.Scheme` FOUND; `RequireAuthorization(AdminPolicies.Superadmin)` FOUND; `RequireRateLimiting(AdminRateLimitRegistrations.AdminLoginPolicy)` FOUND.

## Next Wave Readiness

- **Plan 03-08 (Blazor shell + MudBlazor):** Every HTTP endpoint the admin pages need is live. `MapRazorComponents<App>()` layers on top of `MapGameKitAdmin` unchanged.
- **Plan 03-09 (Blazor admin pages):** Can `@inject HttpClient` (or an admin-scoped typed client in plan 03-08) and call the 12 endpoints with the `X-GameKit-Admin-CSRF` header echoed from the `gk_admin_csrf` cookie per D-16.
- **Plan 03-11 (CLI admin create):** Can either call `POST /admin/api/admins` with superadmin cookie OR reuse `IAdminUserService.CreateAsync` directly via DI. Same 409 semantics either way.
- **Plan 03-12 (TicTacToeDuel wiring):** `AddGameKitAdmin` + `UseGameKitAdmin` + `MapGameKitAdmin` already wire the 12 endpoints; the sample needs only the fluent chain (no endpoint registrations to copy).
- **Plan 03-13 (E2E SC tests):** Can assert SC#6 (player JWT → 404 at /admin/api/*) using existing `AuthSchemeIsolationSmokeTests` pattern + the now-live endpoints. Can also assert the SC anchor strings the BanPlayerRequestValidator emits verbatim.

---
*Phase: 03-admin-ui*
*Plan: 07*
*Completed: 2026-04-19*
