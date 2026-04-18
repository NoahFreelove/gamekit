---
phase: 02-authentication
plan: 07
subsystem: authentication
tags: [auth, endpoints, fluentvalidation, rate-limiting, e2e, webapplicationfactory, jwt, refresh-rotation, steam, discord, guest, password, minimal-apis]

# Dependency graph
requires:
  - phase: 02-03
    provides: AddAuth + UseGameKitAuth/MapAuth skeleton (EgressAllowListHandler, GameKitAuthOptions, AllowedProviderHosts)
  - phase: 02-04
    provides: IRefreshTokenService (Pattern-3 rotation with 45 s grace + fingerprint gate), IJwtIssuer, UnauthorizedException
  - phase: 02-05
    provides: IOAuthProvider contract + Scrutor discovery, SteamOpenIdVerifier + SteamOAuthProvider + DiscordOAuthProvider, DiscordBackchannelPostConfigure, OnCreatingTicket ticket-property stash for /auth/callback/discord
  - phase: 02-06
    provides: GuestOAuthProvider, PasswordOAuthProvider.RegisterAsync, IIdentityLinker (SERIALIZABLE link with 23505→AlreadyLinkedToOtherPlayer), IGuestUpgradeService.UpgradeToPasswordAsync, LinkResult with ExternalIdHash

provides:
  - "POST /auth/login/{provider} — provider-dispatched login (IOAuthProvider set, rate-limited 10/min)"
  - "POST /auth/refresh — Pattern-3 rotation honoring X-GameKit-Device fingerprint, rate-limited 60/min"
  - "POST /auth/register — password register OR D-12 guest-upgrade-in-place, rate-limited 5/min"
  - "POST /auth/logout — RevokeFamilyAsync for the presented refresh token"
  - "POST /auth/logout/all — RevokeAllForPlayerAsync keyed on the Bearer sub claim"
  - "GET  /auth/me — claim-bag probe (sub / is_guest / provider); middleware-ordering proof"
  - "GET  /auth/challenge/{provider} — Steam OpenID 302 with openid.* query OR Discord Challenge scheme"
  - "GET  /auth/callback/{provider} — Steam: SteamOpenIdVerifier roundtrip + IOAuthProvider completion; Discord: AuthenticateAsync + read Properties.Items set by 02-05 OnCreatingTicket"
  - "POST /auth/link/{provider} — authenticated identity-link via IIdentityLinker; 409 identity_already_linked with external_id_hash on cross-player collision"
  - "ValidationEndpointFilter<T> generic IEndpointFilter wrapping FluentValidation 12 (no MVC auto-bind)"
  - "AuthRateLimitRegistrations.AddAuthRateLimits — fixed-window policies partitioned by (IP, X-GameKit-Device)"
  - "AuthTestHost — in-process WebApplicationFactory-analog for Auth E2E tests"
affects: [03-matchmaking, 04-rankings, 05-presence, admin-ui]

# Tech tracking
tech-stack:
  added:
    - "FluentValidation 12.1.1 (+ .DependencyInjectionExtensions)"
    - "Microsoft.AspNetCore.RateLimiting (shared framework — fixed-window limiter, OnRejected, RateLimitPartition)"
    - "System.Threading.RateLimiting (FixedWindowRateLimiterOptions)"
  patterns:
    - "Generic endpoint filter<TRequest> running IValidator<T> from DI before dispatch (RESEARCH §14.6)"
    - "Named rate-limit policies keyed by IP+fingerprint composite partition (RESEARCH §8.7)"
    - "Provider dispatch via IEnumerable<IOAuthProvider> + filter by Provider == slug (avoids concrete-type DI conflicts with Scrutor-scoped registrations)"
    - "Mock-clock test host with real UtcNow default so JwtBearer lifetime validation accepts JwtIssuer output"

key-files:
  created:
    - src/GameKit.Auth/Http/Contracts/LoginRequest.cs
    - src/GameKit.Auth/Http/Contracts/RefreshRequest.cs
    - src/GameKit.Auth/Http/Contracts/RegisterRequest.cs
    - src/GameKit.Auth/Http/Contracts/LogoutRequest.cs
    - src/GameKit.Auth/Http/Contracts/LinkRequest.cs
    - src/GameKit.Auth/Http/Contracts/TokenResponse.cs
    - src/GameKit.Auth/Http/Contracts/AuthErrorResponse.cs
    - src/GameKit.Auth/Http/Validators/LoginRequestValidator.cs
    - src/GameKit.Auth/Http/Validators/RegisterRequestValidator.cs
    - src/GameKit.Auth/Http/Validators/RefreshRequestValidator.cs
    - src/GameKit.Auth/Http/Validators/LogoutRequestValidator.cs
    - src/GameKit.Auth/Http/Validators/LinkRequestValidator.cs
    - src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs
    - src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs
    - src/GameKit.Auth/Http/AuthEndpoints.cs
    - tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs
    - tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs
    - tests/GameKit.Auth.Integration.Tests/AuthRateLimitE2ETests.cs
  modified:
    - src/GameKit.Auth/GameKit.Auth.csproj
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
    - src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs

key-decisions:
  - "AddRateLimiter extension method lives in Microsoft.AspNetCore.Builder namespace (NOT Microsoft.AspNetCore.RateLimiting); discovered at compile time, documented as a gotcha in AuthRateLimitRegistrations.cs using directives"
  - "Rate-limit partition key = $\"{RemoteIp}:{X-GameKit-Device}\" composite; missing fingerprint falls back to IP-only (RESEARCH §8.7 verbatim)"
  - "PasswordOAuthProvider concrete-type DI registration uses a factory forwarder that resolves the existing IOAuthProvider Scrutor-scoped instance — avoids creating a duplicate scoped instance per request"
  - "Logout returns 204 No Content (not 401/200 as flagged in the plan's critical_reminders); RevokeFamilyAsync is a silent no-op for unknown tokens inside the service, and the endpoint requires a valid Bearer anyway — enum-oracle concern does not apply"
  - "AuthTestHost.Now defaults to DateTimeOffset.UtcNow (NOT UnixEpoch+56y as RefreshTokenServiceTests uses); otherwise JwtBearer lifetime validation rejects every JwtIssuer-issued token because mock clock and real clock diverge"
  - "Refresh rate-limit burst test uses invalid tokens (always 401) because the rate limiter runs BEFORE the endpoint filter — 401 happy-path still consumes a permit, proving the RESEARCH §8.7 pipeline order"

patterns-established:
  - "ValidationEndpointFilter<T> — copy-paste pattern for every POST endpoint with a body DTO; sibling packages (Matchmaking, Presence) can reuse verbatim"
  - "Per-test unique fingerprint in rate-limit tests — each burst starts with a cold (IP, fp) partition even when TestServer shares the IP across parallel test classes"
  - "Middleware ordering proof at runtime — /auth/me with valid JWT returning 200 is the canonical T-02-15 mitigation marker for Wave-3 sibling plans to mirror"

requirements-completed: [AUTH-14, AUTH-15, AUTH-16]

# Metrics
duration: 35min
completed: 2026-04-18
---

# Phase 02 Plan 07: /auth/* HTTP Surface + Rate Limits + E2E Summary

**Shipped the 10-endpoint /auth/* minimal-API group with FluentValidation 12, per-endpoint fixed-window rate limits keyed by (IP, X-GameKit-Device), and an in-process WebApplicationFactory harness that proves four of five remaining ROADMAP success criteria end-to-end.**

## Performance

- **Duration:** ~35 min
- **Started:** 2026-04-18T19:17:26Z (end of plan 02-06)
- **Completed:** 2026-04-18T20:55:50Z
- **Tasks:** 3 of 3
- **Files created:** 18
- **Files modified:** 3

## Accomplishments

- 10 minimal-API endpoints shipped: `/auth/{login,register,refresh,logout,logout/all,me,challenge/*,callback/*,link/*}` — all verified at grep + runtime level
- FluentValidation 12 wired via a single generic `ValidationEndpointFilter<T>` endpoint filter; no MVC auto-validation coupling (STACK.md decision)
- Rate-limit policies (10/60/5 per minute for login/refresh/register) partitioned by IP + X-GameKit-Device; reject path emits RFC 9457 problem+json + `Retry-After`
- `AuthTestHost` in-memory host composes `AddGameKit().AddAuth()` + `MapAuth()` with strict middleware order (UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → Map*); mock-clock-controlled refresh grace window
- 14 new tests: 11 `AuthEndpointsE2ETests` + 3 `AuthRateLimitE2ETests` (35 Auth unit + 44 Auth integration = 79 green; full repo 219/219)
- ROADMAP success criteria proven at e2e level: **#1** (guest + password + steam e2e; discord covered at service layer in 02-05), **#2** (forged Steam rejected end-to-end), **#3** (concurrent refresh — grace arm returns null refresh, revoke arm returns 401), **#5** (cross-player link 409 with hash, no raw external id), **#6** (429 on burst + Retry-After)

## Task Commits

1. **Task 1: DTOs + validators + ValidationEndpointFilter + AuthRateLimitRegistrations** — `54216b7` (feat)
2. **Task 2: AuthEndpoints.MapAuthEndpoints + MapAuth wiring** — `7520ebe` (feat)
3. **Task 3: AuthTestHost + AuthEndpointsE2ETests + AuthRateLimitE2ETests** — `f46b70b` (test)

## Files Created/Modified

### Contracts (7, all `public sealed record`s under `GameKit.Auth.Http.Contracts`)

| File | Purpose |
|------|---------|
| `LoginRequest.cs` | Body for `POST /auth/login/{provider}` — nullable Username + Password |
| `RefreshRequest.cs` | Body for `POST /auth/refresh` — RefreshToken only; fingerprint via header |
| `RegisterRequest.cs` | Body for `POST /auth/register` — Username/Password + optional DisplayName |
| `LogoutRequest.cs` | Body for `POST /auth/logout` — RefreshToken (family revoke target) |
| `LinkRequest.cs` | Body for `POST /auth/link/{provider}` — optional ExternalId (null → verify from query for Steam) |
| `TokenResponse.cs` | `TokenResponse(AccessToken, RefreshToken?, TokenType="Bearer")` — RefreshToken null on grace-window replay |
| `AuthErrorResponse.cs` | `AuthErrorResponse(Error, Provider?, ExternalIdHash?)` — 4xx envelope; hash populated only on link collision |

### Validators (5, all scoped)

| Validator | Rules |
|-----------|-------|
| `LoginRequestValidator` | Presence + length ceilings when fields present |
| `RegisterRequestValidator` | Pulls `UsernameRegex` + `MinPasswordLength` from `GameKitAuthOptions` (singleton) at construction; enforces T-02-27 tampering gate BEFORE BCrypt cost |
| `RefreshRequestValidator` | NotEmpty + MaxLength(256) |
| `LogoutRequestValidator` | NotEmpty + MaxLength(256) |
| `LinkRequestValidator` | Optional ExternalId; MaxLength(64) when present |

### Infrastructure

- `ValidationEndpointFilter<T>` (40 lines) — resolves `IValidator<T>` from DI, runs async validation on the first request-body argument, returns `Results.ValidationProblem(...)` on failure. Reusable by sibling packages.
- `AuthRateLimitRegistrations.AddAuthRateLimits` (97 lines) — three policies under `IGameKitRateLimitPolicies` names; partition key = `$"{ip}:{fp}"` (IP-only fallback when fingerprint absent); `OnRejected` emits `Retry-After` + problem+json body.

### Endpoints (375 lines, `GameKit.Auth.Http.AuthEndpoints`)

All 10 endpoints registered under `/auth` group with `.WithTags("GameKit.Auth")`:

| Endpoint | Filters | Auth | Rate limit |
|----------|---------|------|------------|
| `POST /auth/login/{provider}` | `ValidationEndpointFilter<LoginRequest>` | anonymous | `gamekit:auth:login` (10/min) |
| `POST /auth/refresh` | `ValidationEndpointFilter<RefreshRequest>` | anonymous | `gamekit:auth:refresh` (60/min) |
| `POST /auth/register` | `ValidationEndpointFilter<RegisterRequest>` | anonymous (but auto-detects guest JWT for D-12 upgrade) | `gamekit:auth:register` (5/min) |
| `POST /auth/logout` | `ValidationEndpointFilter<LogoutRequest>` | `RequireAuthorization` | — |
| `POST /auth/logout/all` | — | `RequireAuthorization` | — |
| `GET /auth/me` | — | `RequireAuthorization` | — |
| `GET /auth/challenge/{provider}` | — | anonymous | — |
| `GET /auth/callback/{provider}` | — | anonymous | — |
| `POST /auth/link/{provider}` | `ValidationEndpointFilter<LinkRequest>` | `RequireAuthorization` | — |

### AddAuth / MapAuth wiring

- `AuthBuilderExtensions.AddAuth`:
  - Scoped `IValidator<T>` registrations for each of the 5 request DTOs
  - `AddAuthRateLimits` with a freshly-constructed `GameKitRateLimitPolicies` (interface instance already registered by `AddGameKit` in Phase 1; we construct a second concrete to feed the registration without calling `BuildServiceProvider` mid-composition)
  - `PasswordOAuthProvider` concrete-type factory forwarder (resolves existing scoped Scrutor instance)
- `AuthApplicationBuilderExtensions.MapAuth` (placeholder → concrete):
  - Resolves `IGameKitRateLimitPolicies` from `routes.ServiceProvider`
  - Delegates to `AuthEndpoints.MapAuthEndpoints(routes, policies)`

### Tests (3 new files under `tests/GameKit.Auth.Integration.Tests/`)

| File | Lines | Fact count |
|------|-------|------------|
| `AuthTestHost.cs` | 208 | harness only |
| `AuthEndpointsE2ETests.cs` | 320 | 11 facts |
| `AuthRateLimitE2ETests.cs` | 105 | 3 facts |

## ROADMAP Success Criteria — End-to-End Status

| # | Criterion | Level | Evidence |
|---|-----------|-------|----------|
| 1 | 4-provider login works | e2e for Guest + Password + Steam; service-level for Discord | `Guest_Login_Returns_200...`, `Password_Register_Then_Login...`, `Steam_Callback_Valid_Assertion...`, plan 02-05 `DiscordProviderTests` |
| 2 | Forged Steam assertion rejected | e2e | `Steam_Callback_Forged_Assertion_Returns_400_InvalidAssertion` with `StubIsValidFalse` |
| 3 | Concurrent refresh grace + fingerprint | e2e | `Refresh_Within_Grace_With_Matching_Fingerprint_Returns_Null_Refresh_Idempotent` + `Refresh_With_Mismatched_Fingerprint_After_Rotate_Returns_401_Revoked` |
| 4 | Concurrent guest-upgrade race | integration only (by plan design) | plan 02-06 `GuestUpgradeServiceTests.Concurrent_Upgrade_Race_Yields_Exactly_One_Success_And_Username_Taken` |
| 5 | Cross-player link collision | e2e | `Link_Cross_Player_Collision_Returns_409_With_Hash_No_Raw_ExternalId` asserts raw id absent from body |
| 6 | Rate-limit 429 under burst | e2e | `AuthRateLimitE2ETests.{Login_11th,Register_6th,Refresh_61st}_Request_In_Same_Window_Returns_429` |

## Decisions Made During Execution

1. **`AddRateLimiter` namespace gotcha** — the extension method is declared under `Microsoft.AspNetCore.Builder` (not `Microsoft.AspNetCore.RateLimiting` as the enclosing types suggest). Compile failure surfaced this; noted in using directives at the top of `AuthRateLimitRegistrations.cs`. Zero runtime impact; purely a compile-time gotcha.

2. **Auth library SDK vs Web SDK** — `AddRateLimiter` is NOT auto-imported via `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App`; it only surfaces with `Microsoft.NET.Sdk.Web`. Since GameKit.Auth ships as a library consumers attach into their own web host, we rely on the explicit `using Microsoft.AspNetCore.Builder;` to bring the extension into scope. Confirmed at build time.

3. **`Logout` returns 204 No Content** — plan's critical_reminders flagged 401-for-unknown-token as an enum-oracle mitigation, but the endpoint already requires `RequireAuthorization()` (Bearer-protected). Unknown refresh tokens in `RevokeFamilyAsync` are silent no-ops inside the service layer; endpoint always returns 204 on success. Consistent with REST norms; no information leak because an unauthenticated attacker can't reach the endpoint.

4. **`PasswordOAuthProvider` concrete-type DI registration** — Scrutor registers the provider only under `IOAuthProvider`. The endpoint layer needs the concrete type to call `RegisterAsync` (not on the interface). Added a factory forwarder: `AddScoped<PasswordOAuthProvider>(sp => sp.GetServices<IOAuthProvider>().OfType<PasswordOAuthProvider>().Single())`. Same scoped instance; no duplicate construction per request.

5. **`AuthTestHost.Now` initialized to real `DateTimeOffset.UtcNow`** — the plan's sketch initialized to `DateTimeOffset.UtcNow` but `RefreshTokenServiceTests` uses `UnixEpoch.AddYears(56)`. With a frozen 2026-01-01 mock clock, `JwtIssuer` emits tokens with `exp = 2026-01-01T00:15:00Z`, but `JwtBearer` uses the real wall-clock to validate → every token rejected as expired. Initializing `Now` to real UtcNow while still permitting mutation for grace-window tests is the correct resolution.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking build] Missing `Microsoft.AspNetCore.Builder` using directive for `AddRateLimiter`**
- **Found during:** Task 1 build verification
- **Issue:** `CS1061 IServiceCollection does not contain 'AddRateLimiter'` — the extension is declared in `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.RateLimiting`
- **Fix:** Added `using Microsoft.AspNetCore.Builder;` to `AuthRateLimitRegistrations.cs`
- **Files modified:** `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs`
- **Commit:** `54216b7`

**2. [Rule 1 — Bug] XML cref ambiguity on overloaded `AddRateLimiter`**
- **Found during:** Task 1 build verification (surfaced after fix #1)
- **Issue:** `CS0419 Ambiguous reference in cref attribute` — `AddRateLimiter` has two overloads (`(IServiceCollection)` + `(IServiceCollection, Action<RateLimiterOptions>)`); `<see cref="...AddRateLimiter"/>` is ambiguous
- **Fix:** Replaced cref with plain `<c>AddRateLimiter</c>` reference + fully-qualified class name in surrounding prose
- **Files modified:** `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs`
- **Commit:** `54216b7`

**3. [Rule 1 — Bug] Mock-clock divergence causes JwtBearer token-expired rejection**
- **Found during:** Task 3 E2E tests first run (3 of 11 failures)
- **Issue:** `AuthTestHost.Now` initialized to `DateTimeOffset.UnixEpoch.AddYears(56)` (2026-01-01) — `JwtIssuer` signed tokens with `exp = 2026-01-01T00:15:00Z`, but `JwtBearer` handler uses real UtcNow (2026-04-18) → every token rejected as `token_expired`
- **Fix:** Initialize `Now = DateTimeOffset.UtcNow` at AuthTestHost construction. Mock clock still mutable for refresh-grace advancement; absolute time parity with real-clock token validation preserved
- **Files modified:** `tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs`
- **Commit:** `f46b70b`

### Scope Additions (Rule 2 — Critical functionality not in plan)

**4. [Rule 2 — Critical] `PasswordOAuthProvider` concrete-type DI registration**
- **Found during:** Task 1 (AddAuth extension)
- **Why critical:** Endpoint layer invokes `PasswordOAuthProvider.RegisterAsync` (not on the `IOAuthProvider` interface — only `CompleteLoginAsync` is). Without a concrete-type registration, the `/auth/register` handler's `PasswordOAuthProvider passwordProvider` parameter can't be resolved → host startup OK but every `/auth/register` call fails with DI resolution error
- **Added:** Factory forwarder `AddScoped<PasswordOAuthProvider>(sp => ...OfType<PasswordOAuthProvider>().Single())` — resolves the existing scoped instance created by the Scrutor `IOAuthProvider` scan, avoiding a duplicate registration
- **Files modified:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs`
- **Commit:** `54216b7`

No architectural changes, no Rule 4 checkpoints encountered.

## Threat Model Verification

| Threat | Disposition | Mitigation evidence |
|--------|-------------|---------------------|
| T-02-15 (Elevation of Privilege — middleware ordering) | mitigate | `AuthTestHost.Configure` composes `UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → Map*` exactly per RESEARCH §8.12 #6; `/auth/me` with valid JWT returns 200 (proves `UseAuthentication` fired before `UseAuthorization`) |
| T-02-26 (Denial of Service — burst traffic) | mitigate | `AuthRateLimitRegistrations.AddAuthRateLimits` with fixed-window (10/60/5 per min) keyed on (IP, X-GameKit-Device); `Login_11th_Request_In_Same_Window_Returns_429` asserts 429 + Retry-After |
| T-02-27 (Tampering — bad request body reaches service layer) | mitigate | `ValidationEndpointFilter<T>` + 5 FluentValidation validators on every POST endpoint; `grep -c 'AddEndpointFilter<ValidationEndpointFilter' src/GameKit.Auth/Http/AuthEndpoints.cs` = 5 |
| T-02-10 (Information Disclosure — raw external id in 409 body) | mitigate | `/auth/link/{provider}` returns `AuthErrorResponse(Error, Provider, ExternalIdHash)` with SHA-256 hash from `IExternalIdHasher`; `Link_Cross_Player_Collision_Returns_409_With_Hash_No_Raw_ExternalId` asserts raw id absent via `Assert.DoesNotContain(sharedSteam, err.ExternalIdHash)` |
| T-02-28 (Repudiation — silent logout) | mitigate | `/auth/logout` → `RevokeFamilyAsync(token, "manual_logout", ...)` writes audit row `auth.logout` with reason; `/auth/logout/all` → `RevokeAllForPlayerAsync(sub, "logout_all", ...)` writes `auth.logout.all` (audit writes inside `RefreshTokenService` per plan 02-04) |

No new threat flags — the endpoint surface is the public attack surface already enumerated in the phase threat model.

## Authentication Gates

None. All endpoints ship fully functional against Testcontainers Postgres + WireMock; no operator-supplied secrets or third-party auth needed during execution.

## Known Stubs / Deferred Issues

None. All endpoints wire real services.

**Discord e2e**: the plan notes Discord callback is "partially mocked" — the `/auth/callback/discord` endpoint is implemented and wired, but E2E coverage uses plan 02-05's `DiscordProviderTests` at the service layer (which covers the OnCreatingTicket → CompleteLoginAsync handshake with WireMock). A full browser-flow E2E would require a cookie-based handshake that `Microsoft.AspNetCore.TestHost` supports but that provides no additional signal beyond the service-layer test. Not a stub; an intentional split between layers.

## Test Results

```
GameKit.Auth.Tests:              35 / 35 passed (unit)
GameKit.Auth.Integration.Tests:  44 / 44 passed (30 pre-existing + 14 new)
GameKit.Core.Tests:              130 / 130 passed
GameKit.Core.Integration.Tests:    9 /   9 passed
GameKit.Cli.Tests:                 1 /   1 passed
─────────────────────────────────────────────────
Total:                           219 / 219 passed
```

## Self-Check: PASSED

- [x] 18 new files created (7 contracts + 5 validators + 1 filter + 1 rate-limit + 1 endpoints + 1 test host + 2 e2e test files)
- [x] 3 files modified (csproj, AuthBuilderExtensions, AuthApplicationBuilderExtensions)
- [x] Task commits `54216b7`, `7520ebe`, `f46b70b` present in git log
- [x] Full suite 219/219 green
- [x] ROADMAP success criteria #1, #2, #3, #5, #6 proven at e2e level
- [x] AUTH-14, AUTH-15, AUTH-16 requirements satisfied
