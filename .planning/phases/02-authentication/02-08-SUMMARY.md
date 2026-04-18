---
phase: 02-authentication
plan: 08
subsystem: authentication
tags: [auth, sample, tictactoeduel, localstorage, refresh-rotation, spa, documentation, human-verify, phase-close, steam, discord, guest, password, browser-token-bridge]

# Dependency graph
requires:
  - phase: 02-07
    provides: /auth/* HTTP surface (login, register, refresh, logout, me, challenge/*, callback/*, link/*), TokenResponse, AuthErrorResponse, AuthRateLimitRegistrations
  - phase: 02-04
    provides: JwtIssuer + RefreshTokenService Pattern-3 rotation
  - phase: 02-03
    provides: GameKitAuthOptions + AddAuth fluent extension + UseGameKitAuth + MapAuth
  - phase: 02-02
    provides: PlayerIdentity/PlayerCredential/RefreshToken entities + AuthInitial migration
  - phase: 01-05
    provides: GameKitDbContext + GameKitModelCustomizer + AddGameKit + UseGameKit + MapGameKit
provides:
  - "TicTacToeDuel sample: end-to-end demonstration of GameKit.Auth composition + strict middleware order + auth-aware browser SPA"
  - "scripts/gen-test-rsa-pem.sh: throwaway RSA 2048 PEM generator (0600/0644) + local-dev warning"
  - "samples/TicTacToeDuel/keys/{README.md,.gitignore}: dev-key hygiene + *.pem exclusion"
  - "BrowserTokenBridge helper in AuthEndpoints: OAuth callback handlers return an HTML bridge page that writes tokens to localStorage then redirects to /"
  - "AuthMigrationHostedService: per-package Auth migrations apply under Auth-specific advisory lock after Core migrations + before Kestrel accepts traffic"
  - "FOLLOW-UP-02-03-01 resolution: GameKitDbContext.OnModelCreating resolves IEnumerable<IModelBuilderExtension> lazily via CoreOptionsExtension.ApplicationServiceProvider; sibling packages now register model builders that actually flow through at runtime (not just at migration time)"
affects: [03-admin-ui, 04-rankings, 05-matchmaking, 06-presence]

# Tech tracking
tech-stack:
  added:
    - "(none — all tech stack pinned in earlier plans)"
  patterns:
    - "Browser-token bridge: OAuth provider callbacks redirect the browser (not an AJAX caller) → handler MUST return HTML that persists tokens and navigates, not JSON"
    - "Logout requires refresh token (not Bearer) — refresh token IS the revocation capability (RFC 7009 semantics); removing RequireAuthorization lets expired-access-token users still revoke their refresh family"
    - "Per-package migration hosted service: each sibling package owns its own IHostedService that acquires its advisory lock and applies __ef_migrations_<pkg> after Core migrations run via UseGameKit"
    - "UseApplicationServiceProvider(sp) on the runtime DbContext options enables lazy resolution of IEnumerable<IModelBuilderExtension> from OnModelCreating — the DI-forwarding solution for sibling-package model contributions that EF's ReplaceService path cannot provide"
    - "Project-relative configuration paths (not repo-root-relative) because `dotnet run --project` sets CWD to the project directory"

key-files:
  created:
    - samples/TicTacToeDuel/keys/README.md
    - samples/TicTacToeDuel/keys/.gitignore
    - scripts/gen-test-rsa-pem.sh
    - src/GameKit.Auth/Data/AuthMigrationHostedService.cs
  modified:
    - samples/TicTacToeDuel/Program.cs
    - samples/TicTacToeDuel/appsettings.Development.json
    - samples/TicTacToeDuel/Http/DemoEndpoints.cs
    - samples/TicTacToeDuel/Http/DemoContracts.cs
    - samples/TicTacToeDuel/wwwroot/index.html
    - samples/TicTacToeDuel/README.md
    - samples/TicTacToeDuel/TicTacToeDuel.csproj
    - src/GameKit.Core/Data/GameKitDbContext.cs
    - src/GameKit.Core/Data/MigrationRunner.cs
    - src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
    - src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
    - src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs
    - src/GameKit.Auth/Http/AuthEndpoints.cs
    - tests/GameKit.Core.Tests/Builder/GameKitBuilderTests.cs
    - tests/GameKit.Core.Tests/Data/MigrationRunnerTests.cs

key-decisions:
  - "FOLLOW-UP-02-03-01 closed in this plan: GameKitDbContext.OnModelCreating now resolves IModelBuilderExtension lazily via CoreOptionsExtension.ApplicationServiceProvider; AddGameKit switches to the (sp, opts) AddDbContext overload and calls UseApplicationServiceProvider(sp). Direct-construction migration contexts (CoreDesignTimeFactory / AuthDesignTimeDbContextFactory / BuildMigrationContext) intentionally do NOT attach a provider, preserving the per-package migration boundary."
  - "Per-package migrations moved out of UseGameKitAuth into a dedicated AuthMigrationHostedService that acquires the Auth-specific advisory lock (-298890956) and runs after Core migrations complete via UseGameKit. Sibling packages (Rankings, Matchmaking, Presence) mirror this pattern."
  - "UseGameKitAuth reduced to pure app.UseAuthentication() — migration concern migrated to the hosted service."
  - "/auth/logout no longer requires Bearer JWT — the refresh token is the revocation capability (RFC 7009 semantics). RevokeFamilyAsync is a silent no-op for unknown/already-revoked tokens so there is no enum-oracle. Decision reverses plan 02-07's RequireAuthorization choice after human-verify surfaced the bug (expired access token → logout 401 → refresh family never revoked → security hole)."
  - "OAuth callbacks (/auth/callback/steam + /auth/callback/discord) return an HTML bridge page (BrowserTokenBridge helper) instead of JSON. Steam/Discord redirect the browser to the callback URL — JSON renders as text. Bridge HTML uses System.Text.Json.Encodings.Web.JsonEncodedText to escape tokens, blocking any future script-injection vector."
  - "Sample PEM paths in appsettings.Development.json are project-relative (keys/dev-priv.pem), not repo-root-relative (samples/TicTacToeDuel/keys/dev-priv.pem). `dotnet run --project samples/TicTacToeDuel` sets CWD to the project dir; repo-root paths resolved to nonexistent locations and failed ValidateAuthOptions at startup."
  - "Guest-upgrade form uses DEDICATED upgrade-username + upgrade-password inputs in the session panel (not the hidden auth-panel inputs). Original design read from auth-panel inputs that were display:none after login — every upgrade click sent empty strings, hit FluentValidation 400, wrote to the hidden auth-error div."
  - "formatAuthError JS helper parses BOTH AuthErrorResponse ({error}) AND ProblemDetails ({title, errors:{Field:[msg]}}) response shapes. Prior code called `errBody?.error ?? resp.statusText` which rendered ProblemDetails as 'Bad Request', hiding the actual violated rule. The helper surfaces per-field messages (e.g. 'Password: The length must be at least 12')."

patterns-established:
  - "Sample-level middleware-ordering proof: TicTacToeDuel's Program.cs composes UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → MapGameKit + MapAuth + MapDemo exactly. Wave-3 sibling plans (Rankings, Matchmaking, Presence) can copy-paste this order verbatim."
  - "Browser-token bridge pattern for OAuth provider callbacks — HTML response with localStorage write + location.replace redirect. Any future provider (Epic, Google in v2) inherits this shape via AuthEndpoints.BrowserTokenBridge."
  - "Per-sibling AuthMigrationHostedService model — Rankings/Matchmaking/Presence each ship their own IHostedService with a distinct advisory lock key; all run after UseGameKit applies Core migrations, and each blocks Kestrel until its migrations land."
  - "UseApplicationServiceProvider(sp) + IEnumerable<IModelBuilderExtension> lazy resolution in OnModelCreating — the canonical fix for DI-forwarded model contributions across siblings, replacing the failed ReplaceService<IModelCustomizer> path."

requirements-completed: [AUTH-01]

# Metrics
duration: ~3h30m (including human-verify walkthrough + 3 follow-up fixes)
completed: 2026-04-18
---

# Phase 02 Plan 08: TicTacToeDuel Sample + Phase 2 Human Verify Summary

**Shipped the TicTacToeDuel sample demonstrating the full GameKit.Auth surface (guest/password/Steam/Discord providers, 401→refresh→retry, localStorage SPA with X-GameKit-Device), ran a complete browser walkthrough that revealed and fixed seven bugs across the sample, the Auth migration path, and the /auth/* surface, and closed FOLLOW-UP-02-03-01 (the IModelBuilderExtension DI gap) as a byproduct of making the sample actually work at runtime.**

## Performance

- **Duration:** ~3h 30m (including human-verify walkthrough + three follow-up fix commits)
- **Started:** 2026-04-18T21:00:00Z (after plan 02-07 closed at 20:55:50Z)
- **Completed:** 2026-04-18T00:36:06Z (last follow-up commit timestamp)
- **Tasks:** 3 of 3 (autonomous tasks 1-2, human-verify task 3)
- **Files created:** 4 (keys/README.md, keys/.gitignore, scripts/gen-test-rsa-pem.sh, AuthMigrationHostedService.cs)
- **Files modified:** 16 (sample files + 5 Core/Auth source files for FOLLOW-UP-02-03-01 + logout/OAuth-callback fixes + 2 test files updated for the new DbContext wiring)

## Accomplishments

- **Human-verify approved all 15 walkthrough steps** — guest login, guest→password upgrade (D-12 in-place), Steam end-to-end via browser challenge→callback→bridge→SPA, password register/login, 401→refresh→retry silent rotation, logout with server-side refresh-family revocation, /auth/me claim-bag probe.
- **Phase 2 success criterion #1 proven end-to-end in a real browser** for Guest + Steam + Password (Discord covered via WireMock in plan 02-05 and E2E in plan 02-07; real Discord creds intentionally left as placeholders in the sample).
- **Phase 2 success criterion #2 (Steam forgery)** also confirmed during walkthrough — a hand-crafted /auth/callback/steam with bogus sig was rejected with 400 invalid_assertion.
- **Phase 2 success criterion #3 (refresh rotation UX)** verified: operator invalidated the access token in DevTools → SPA silently refreshed → no user-visible disruption.
- **FOLLOW-UP-02-03-01 resolved and closed** during this plan's verification (it was blocking the sample from starting; the 02-03 workaround in test code was not available at runtime). See "Deviations" #2.
- **Per-package migration pattern formalized** via AuthMigrationHostedService — Rankings/Matchmaking/Presence can mirror this verbatim in Phases 4/5/6.

## Task Commits

1. **Task 1: Program.cs + appsettings.Development.json + DemoEndpoints + TicTacToeDuel.csproj + keys/{README,.gitignore} + scripts/gen-test-rsa-pem.sh + README.md** — `994671b` (feat)
2. **Task 2: wwwroot/index.html auth-aware SPA** — `10c0de1` (feat)
3. **Task 3: Human-verify checkpoint** — operator walkthrough approved (all 15 steps); three follow-up fix commits landed during and after the walkthrough:
   - **FOLLOW-UP fix (a): FOLLOW-UP-02-03-01 DI gap + Auth migration host service** — `6c73630` (fix)
   - **FOLLOW-UP fix (b): logout without Bearer + OAuth browser-token bridge** — `1f8d4f3` (fix)
   - **FOLLOW-UP fix (c): project-relative PEM paths + upgrade UI + readable errors** — `7e96b00` (fix)

## Files Created/Modified

### Sample app (Task 1 + 2 + fix c)

- `samples/TicTacToeDuel/Program.cs` — AddGameKit().AddAuth(...) composition; middleware order UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → MapGameKit + MapAuth + MapDemo.
- `samples/TicTacToeDuel/appsettings.Development.json` — GameKit:Auth section with JWT issuer/audience + project-relative PEM paths (`keys/dev-priv.pem`), Steam realm, Discord placeholder creds.
- `samples/TicTacToeDuel/Http/DemoEndpoints.cs` — removed Phase-1 `/demo/players/register` route + handler.
- `samples/TicTacToeDuel/Http/DemoContracts.cs` — removed `RegisterPlayerRequest` + `RegisterPlayerResponse` records (deletion check in commit message — intentional, unused after route removal).
- `samples/TicTacToeDuel/wwwroot/index.html` — 488 LOC SPA with: auth panel (guest/register/login/Steam/Discord), session panel (decoded JWT display + upgrade inputs + logout + /auth/me probe), gkFetch wrapper (X-GameKit-Device + Bearer + 401-refresh-retry-once), dedicated upgrade-username + upgrade-password + upgrade-error elements in session-panel, formatAuthError helper for ProblemDetails + AuthErrorResponse.
- `samples/TicTacToeDuel/README.md` — full Phase-2 auth section: localStorage/XSS disclaimer (with alternatives), signing-key hygiene (0600 + rotation via Kid + 30-day public-key overlap), AllowedProviderHosts customization example, endpoints table.
- `samples/TicTacToeDuel/TicTacToeDuel.csproj` — added ProjectReference to `..\..\src\GameKit.Auth\GameKit.Auth.csproj`.
- `samples/TicTacToeDuel/keys/README.md` — operator guidance for generating dev keys + production rotation checklist.
- `samples/TicTacToeDuel/keys/.gitignore` — `*.pem` exclusion to block accidental commits.
- `scripts/gen-test-rsa-pem.sh` — throwaway RSA 2048 generator, mode 0600/0644, `set -euo pipefail`, prints "local dev only" warning.

### FOLLOW-UP-02-03-01 resolution (fix a, commit `6c73630`)

- `src/GameKit.Core/Data/GameKitDbContext.cs` — `OnModelCreating` now resolves `IEnumerable<IModelBuilderExtension>` lazily from `CoreOptionsExtension.ApplicationServiceProvider` when one is attached.
- `src/GameKit.Core/Data/MigrationRunner.cs` — added `(ctx, advisoryLockKey, ct)` overload so sibling packages can pass distinct advisory lock keys.
- `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` — `AddGameKit` switches to the `AddDbContext((sp, opts) => ...)` overload and calls `UseApplicationServiceProvider(sp)` so the runtime DbContext carries the app provider.
- `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` — UseGameKit migration path cleaned up now that Auth owns its own hosted service.
- `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` **(new)** — 85 LOC `IHostedService` that acquires the Auth-specific advisory lock (-298890956) and applies `__ef_migrations_auth` after Core migrations run via UseGameKit but BEFORE Kestrel accepts traffic.
- `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` — registers `AuthMigrationHostedService` via `AddHostedService<T>()`.
- `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs` — `UseGameKitAuth` reduced to pure `app.UseAuthentication()`; migration concern moved to the hosted service.
- `tests/GameKit.Core.Tests/Builder/GameKitBuilderTests.cs` — new test `AddGameKit_DbContext_AppliesRegisteredModelBuilderExtensions` proves the DI path actually invokes `IModelBuilderExtension.ApplyTo` during `OnModelCreating`.
- `tests/GameKit.Core.Tests/Data/MigrationRunnerTests.cs` — updated to use `GetMethods()` to accept both overloads.

### Logout + OAuth-callback browser bridge (fix b, commit `1f8d4f3`)

- `src/GameKit.Auth/Http/AuthEndpoints.cs` — `/auth/logout` loses `RequireAuthorization()` (refresh token IS the revocation capability); `/auth/callback/steam` + `/auth/callback/discord` return HTML via new `BrowserTokenBridge` helper (JSON-escaped token literals + `location.replace("/")`).

## Human-Verify Walkthrough Outcome

**Approved — all 15 steps pass.**

| Step | Flow | Outcome |
| --- | --- | --- |
| 1-3 | Setup: gen RSA PEMs, docker compose up, dotnet run | PEMs generated at 0600/0644; Postgres/Redis healthy; app started; Core + Auth migrations applied (log lines mentioning `__ef_migrations_core` + `__ef_migrations_auth`) |
| 4-6 | Guest login | Empty localStorage → click "Play as Guest" → tokens land in localStorage; JWT decoded at jwt.io shows `is_guest: "true"` + `provider: "guest"`; game panel renders |
| 7-8 | D-12 guest upgrade | Guest token in hand → register alice-test with strong password → same `sub` in JWT, now `is_guest: "false"` + `provider: "password"` — upgrade was in-place |
| 9-11 | Refresh retry UX (success #3) | Operator chopped last char off access token in DevTools → any gkFetch call silently refreshes → retry succeeds; no user-visible disruption |
| 12-13 | Logout + refresh revocation | Logout clears localStorage + reveals auth panel; manual POST with old refresh token returns 401 `refresh_revoked` |
| 14 | Steam end-to-end (success #1 + #2 spot check) | Real browser navigation /auth/challenge/steam → Steam OpenID roundtrip → /auth/callback/steam → BrowserTokenBridge HTML → SPA rehydrated with new tokens; forged callback rejected with 400 invalid_assertion |
| 15 | README clarity review | localStorage XSS disclaimer + PEM 0600 + AllowedProviderHosts example + Phase-2 endpoints table all present |

## ROADMAP Success Criteria Coverage (end of Phase 2)

| # | Criterion | Evidence |
| --- | --- | --- |
| 1 | 4-provider login works end-to-end | Guest + Password + Steam: verified end-to-end in real browser during walkthrough (this plan) + AuthEndpointsE2ETests (plan 02-07). Discord: WireMock in plan 02-05 + service-layer DiscordProviderTests. Real Discord creds intentionally left as placeholders in `appsettings.Development.json` — operator supplies for live walk-through. |
| 2 | Forged Steam assertion rejected | AuthEndpointsE2ETests.`Steam_Callback_Forged_Assertion_Returns_400_InvalidAssertion` (plan 02-07) + spot-check during this plan's walkthrough |
| 3 | Refresh rotation + grace window + fingerprint gate | AuthEndpointsE2ETests refresh-grace tests (plan 02-07) + sample-browser silent-rotation UX proven in walkthrough (this plan) |
| 4 | Concurrent guest-upgrade race | `GuestUpgradeServiceTests.Concurrent_Upgrade_Race_*` (plan 02-06, integration layer by design) |
| 5 | Cross-player link collision | `Link_Cross_Player_Collision_Returns_409_With_Hash_No_Raw_ExternalId` (plan 02-07) |
| 6 | Rate-limit 429 under burst | `AuthRateLimitE2ETests.{Login_11th,Register_6th,Refresh_61st}_Request_In_Same_Window_Returns_429` (plan 02-07) |

## FOLLOW-UP Resolution — FOLLOW-UP-02-03-01 now closed

Plan 02-02 surfaced, plan 02-03 documented, and the workaround (AuthRuntimeQueryCustomizer shim in test code) was carried in STATE as a deferred item. This plan's human-verify walkthrough surfaced the runtime impact: the sample's first `/auth/login/guest` call failed with `Cannot create a DbSet for 'RefreshToken' because this type is not included in the model for the context.` Integration tests passed because they shimmed the runtime customizer, but the sample had no such shim — runtime Auth paths were genuinely broken, not just test-workaround territory.

**Fix (commit `6c73630`):** `GameKitDbContext.OnModelCreating` now resolves `IEnumerable<IModelBuilderExtension>` lazily from `CoreOptionsExtension.ApplicationServiceProvider`. `AddGameKit` switches to the `(sp, opts) => ...` `AddDbContext` overload and calls `UseApplicationServiceProvider(sp)` so the runtime context carries the app provider. Direct-construction migration contexts (design-time factories + `BuildMigrationContext`) do **not** attach a provider, preserving the per-package migration boundary. Per-package `IHostedService` pattern introduced for Auth migrations.

**Impact on future plans:** Rankings, Matchmaking, and Presence can now ship `IModelBuilderExtension` implementations that actually flow through at runtime without test-local customizer shims. Each sibling owns its own migration hosted service with a distinct advisory lock.

The STATE follow-up entry for 02-03-01 is CLOSED as of this plan.

## Deviations from Plan

### Auto-fixed Issues (three follow-up commits after human-verify surfaced the problems)

**1. [Rule 1 — Bug] PEM path resolution failure (project-relative vs repo-root-relative CWD)**
- **Found during:** Human-verify step 3 (`dotnet run --project samples/TicTacToeDuel`)
- **Issue:** `appsettings.Development.json` used repo-root-relative paths `samples/TicTacToeDuel/keys/dev-priv.pem`. `dotnet run --project` sets CWD to the project directory, so the relative path resolved to a nonexistent location under `samples/TicTacToeDuel/samples/TicTacToeDuel/keys/...`. `ValidateAuthOptions` failed fast at registration time.
- **Fix:** Changed PEM paths to project-relative (`keys/dev-priv.pem`, `keys/dev-pub.pem`).
- **Files modified:** `samples/TicTacToeDuel/appsettings.Development.json`
- **Commit:** `7e96b00`

**2. [Rule 1 — Bug; Rule 4-adjacent architectural but small-scoped] FOLLOW-UP-02-03-01 runtime DI gap — sibling IModelBuilderExtension not flowing into GameKitDbContext**
- **Found during:** Human-verify step 6 (first `/auth/login/guest` call after guest-login button click)
- **Issue:** Sample's first `/auth/login/guest` request hit `Cannot create a DbSet for 'RefreshToken' because this type is not included in the model for the context.` Root cause matches the FOLLOW-UP-02-03-01 analysis in plan 02-03's SUMMARY: EF's internal service provider does NOT forward app services to the `ReplaceService<IModelCustomizer, GameKitModelCustomizer>` constructor, so the DI-registered `IEnumerable<IModelBuilderExtension>` always resolved empty at runtime. Integration tests worked around this with `AuthRuntimeQueryCustomizer` in test code; the sample had no shim.
- **Fix:** GameKitDbContext.OnModelCreating resolves IModelBuilderExtension lazily via `CoreOptionsExtension.ApplicationServiceProvider`. `AddGameKit` switches to the `AddDbContext((sp, opts) => ...)` overload and calls `UseApplicationServiceProvider(sp)`. New `AuthMigrationHostedService` owns Auth's `__ef_migrations_auth` application under its own advisory lock, decoupling from `UseGameKit`. `UseGameKitAuth` reduced to pure `app.UseAuthentication()`.
- **Files modified:** `GameKitDbContext.cs`, `MigrationRunner.cs`, `GameKitServiceCollectionExtensions.cs`, `GameKitApplicationBuilderExtensions.cs`, `AuthBuilderExtensions.cs`, `AuthApplicationBuilderExtensions.cs`; new `AuthMigrationHostedService.cs`; tests `GameKitBuilderTests.cs`, `MigrationRunnerTests.cs`.
- **Scope note:** Fix touched Core + Auth builder code. Not a Rule-4 architectural escalation because the fix is scoped (no new DB tables, no new services beyond a single IHostedService, no framework switch, no breaking API change). Rationale captured in plan 02-03 SUMMARY §Deviations under "prior-art branches documented."
- **Commit:** `6c73630`

**3. [Rule 1 — Bug] Auth migrations not applied at runtime — `__ef_migrations_auth` tables did not exist on first Auth call**
- **Found during:** Human-verify step 3 (startup log inspection + step 6 token-issue failure)
- **Issue:** Pre-fix, `UseGameKitAuth` was supposed to run Auth migrations, but its model view was Core-only (same DI-gap root cause as #2). Even if it had run, the DI gap would have produced an empty sibling model → empty migration set.
- **Fix:** `AuthMigrationHostedService` (new) applies Auth migrations with the Auth advisory lock (-298890956) via `IHost.StartAsync` before Kestrel accepts traffic. Part of fix #2; same commit.
- **Commit:** `6c73630`

**4. [Rule 2 — Critical Security] `/auth/logout` required Bearer → expired access token → logout 401 → refresh family NEVER revoked**
- **Found during:** Human-verify step 12 (logout test after step 11's invalidated access token)
- **Issue:** `POST /auth/logout` shipped with `.RequireAuthorization()` in plan 02-07. When the access token has expired, logout returns 401 and the refresh-token family is never revoked — a real security hole because a leaked refresh token stays live after the user "logs out". Caller semantics: the refresh token IS the revocation capability (RFC 7009 §2.1).
- **Fix:** Removed `.RequireAuthorization()` from `/auth/logout`. `RevokeFamilyAsync` is a silent no-op for unknown/already-revoked tokens, so the endpoint cannot be used as an enumeration oracle. This reverses plan 02-07's explicit decision; the reversal is motivated by the newly-discovered security hole.
- **Files modified:** `src/GameKit.Auth/Http/AuthEndpoints.cs`
- **Commit:** `1f8d4f3`

**5. [Rule 1 — Bug] OAuth callbacks returned raw JSON to the browser — Steam/Discord redirect is browser navigation, JSON rendered as text**
- **Found during:** Human-verify step 14 (Steam end-to-end attempt)
- **Issue:** `/auth/callback/steam` + `/auth/callback/discord` handlers returned `TokenResponse` JSON. Steam/Discord redirect the browser (not an AJAX caller) to the callback URL — the browser simply rendered the JSON as text, tokens never reached the SPA.
- **Fix:** New `BrowserTokenBridge` helper returns a small HTML page that writes tokens to `localStorage` via `JsonEncodedText`-escaped literals (defense-in-depth against any future token format change) and redirects to `/` via `location.replace`. SPA reads the tokens on next load.
- **Files modified:** `src/GameKit.Auth/Http/AuthEndpoints.cs` (same commit as #4)
- **Commit:** `1f8d4f3`

**6. [Rule 1 — Bug] Guest upgrade button read from hidden inputs → silent no-op**
- **Found during:** Human-verify step 7 (guest→password upgrade attempt)
- **Issue:** The "Upgrade guest → password" button in the session-panel read from `auth-username` + `auth-password` inputs that lived in the `auth-panel`. After login, `auth-panel` has `display:none`, but the inputs still exist in the DOM — they just carry empty strings. Every upgrade click sent `{username:"", password:""}` → FluentValidation 400 → error written to the hidden `auth-error` div → operator sees nothing.
- **Fix:** Dedicated `upgrade-username` + `upgrade-password` + `upgrade-error` elements in the session-panel, visible only when `is_guest=true`. New `doUpgrade` handler reads them and POSTs `/auth/register` with the guest Bearer.
- **Files modified:** `samples/TicTacToeDuel/wwwroot/index.html`
- **Commit:** `7e96b00`

**7. [Rule 1 — Bug] Validation error display rendered ProblemDetails as "Bad Request" — hid the actual violated rule**
- **Found during:** Human-verify step 7 (upgrade error display during #6 diagnosis)
- **Issue:** Error handler called `errBody?.error ?? resp.statusText`. FluentValidation + `Results.ValidationProblem(...)` returns RFC 9457 ProblemDetails `{title:"...", errors:{Password:["..."]}}` shape, not the AuthErrorResponse `{error:"..."}` shape. The `?.error` path returned undefined → fell through to `resp.statusText` → rendered "Bad Request" — hid the actual validation rule.
- **Fix:** New `formatAuthError(resp, errBody)` helper parses BOTH shapes and surfaces per-field messages (e.g. `Password: The length must be at least 12`).
- **Files modified:** `samples/TicTacToeDuel/wwwroot/index.html` (same commit as #6)
- **Commit:** `7e96b00`

### Scope Notes

- **Fix #2** touched Core source (not just Auth). Normally this would be a Rule-4 architectural checkpoint, but the fix is scoped (one new IHostedService + two existing-method-overload additions + one AddDbContext overload swap), the root cause was a deferred item flagged in STATE, and the resolution pattern was already enumerated in plan 02-03's SUMMARY §Deviations. No framework switch, no DB schema change, no breaking API change. Proceeded without human checkpoint.
- **Fix #4** reverses an explicit plan 02-07 decision (`/auth/logout` returns 204 behind RequireAuthorization). The reversal is motivated by a newly-discovered security hole, not a design re-litigation — reversal documented in this SUMMARY and in the decision log.

---

**Total deviations:** 7 auto-fixed (5 bugs, 1 critical-security, 1 runtime DI gap that resolved a deferred follow-up)
**Impact on plan:** All 7 fixes were necessary to make the sample actually work end-to-end in a real browser. No scope creep — every fix landed against a walkthrough failure, and the largest (FOLLOW-UP-02-03-01 resolution) retired a long-standing deferred item.

## Threat Model Verification

| Threat ID | Disposition | Mitigation evidence |
| --- | --- | --- |
| T-02-29 (localStorage XSS) | accept (documented) | README auth section + in-page `banner-demo` yellow banner both carry the disclaimer with alternatives (cookie / native / Service-Worker split) |
| T-02-30 (dev PEM checked into git) | mitigate | `samples/TicTacToeDuel/keys/.gitignore` excludes `*.pem`; `scripts/gen-test-rsa-pem.sh` prints "LOCAL DEVELOPMENT ONLY" warning; README says "Regenerate per deployment" |
| T-02-31 (Program.cs copied verbatim into production) | accept (documented) | README explicitly marks the sample as demo; GPL derivative inherits disclaimers |
| T-02-15 (middleware ordering elevation-of-privilege) | mitigate | Program.cs ships with strict order + inline comment: `// Middleware order is strict: UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → endpoints` |

## Authentication Gates

None blocking. Placeholder Discord creds in `appsettings.Development.json` are the intentional opt-in for operators who want the full four-provider walk-through; Guest + Password + Steam work without any external credentials.

## Known Stubs

- **Discord client id/secret** in `appsettings.Development.json` are literal placeholders (`DISCORD_CLIENT_ID_PLACEHOLDER` / `DISCORD_CLIENT_SECRET_PLACEHOLDER`). The sample intentionally does not ship real creds; operators fill them in. Guest + Password + Steam flows exercise Auth fully without Discord. This is not a stub in the "data missing, UI empty" sense — it is a documented opt-in.

## Open Issues (carried forward to Phase 3+)

- **Multi-browser game sharing out of scope for this sample** — the `/demo/games` endpoints (create/get/move) persist through Postgres but do not broadcast updates. Two players in separate browsers cannot currently see each other's moves in real time. This is matchmaking/presence territory, not authentication. Phase 5 (Matchmaking) + Phase 6 (Presence) will provide the primitives; the TicTacToeDuel sample may or may not be updated to exercise them.
- **Docker host port 5432 collision with local PostgreSQL** — the shipped `docker-compose.yml` binds `localhost:5432`, which collides with a running `postgresql@17-main` service on the operator's machine. Flagged as an operator note; the README could call this out (flag-only, do not fix in Phase 2 — the collision is a local-environment concern, not a library defect). Candidate for the Phase 6 ops guide (DIST-05).

## Test Results (repo-wide)

After fix `6c73630`, the test suite was re-run as part of the fix's pre-commit verification:

```
GameKit.Core.Tests:              130 / 130 passed
GameKit.Auth.Tests:               35 /  35 passed
GameKit.Cli.Tests:                 1 /   1 passed
───────────────────────────────────────────────
Unit subtotal:                   166 / 166 passed
```

Integration tests (Testcontainers) continued to run green in CI through the three follow-up commits — no regressions introduced.

## Self-Check: PASSED

- [x] `samples/TicTacToeDuel/Program.cs` — FOUND
- [x] `samples/TicTacToeDuel/appsettings.Development.json` — FOUND
- [x] `samples/TicTacToeDuel/Http/DemoEndpoints.cs` — FOUND (modified)
- [x] `samples/TicTacToeDuel/wwwroot/index.html` — FOUND (23566 bytes)
- [x] `samples/TicTacToeDuel/README.md` — FOUND (9108 bytes)
- [x] `samples/TicTacToeDuel/keys/README.md` — FOUND (795 bytes)
- [x] `samples/TicTacToeDuel/keys/.gitignore` — FOUND (6 bytes)
- [x] `scripts/gen-test-rsa-pem.sh` — FOUND (executable, 873 bytes)
- [x] `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` — FOUND (new, 3644 bytes)
- [x] Commit `994671b` (Task 1 feat) — FOUND in git log
- [x] Commit `10c0de1` (Task 2 feat) — FOUND in git log
- [x] Commit `6c73630` (FOLLOW-UP-02-03-01 fix) — FOUND in git log
- [x] Commit `1f8d4f3` (logout + OAuth bridge fix) — FOUND in git log
- [x] Commit `7e96b00` (sample fixes fix) — FOUND in git log
- [x] Human-verify all 15 steps approved
