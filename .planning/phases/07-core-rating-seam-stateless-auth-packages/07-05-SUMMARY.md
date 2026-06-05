---
phase: 07
plan: 05
subsystem: auth
tags: [auth, oauth, epic, provider, custom-handler, wireMock, tdd]
dependency_graph:
  requires: [07-02]
  provides: [GameKit.Auth.Epic package, EpicOAuthHandler, EpicOAuthProvider, AddEpic()]
  affects: [src/GameKit.Auth/AssemblyInfo.cs]
tech_stack:
  added: []
  patterns:
    - "Custom OAuthHandler<T> derivation (shared-framework; zero new NuGet dep)"
    - "Authorization: Basic base64(clientId:clientSecret) header override in ExchangeCodeAsync"
    - "Internal test-seam subclass (TestEpicOAuthHandler) + InternalsVisibleTo for WireMock Basic-auth proof"
    - "Conditional scheme registration (ClientId+ClientSecret guard)"
    - "Unconditional IOAuthProvider self-registration (Scrutor gap fix)"
key_files:
  created:
    - src/GameKit.Auth.Epic/GameKit.Auth.Epic.csproj
    - src/GameKit.Auth.Epic/AssemblyInfo.cs
    - src/GameKit.Auth.Epic/Configuration/GameKitEpicOptions.cs
    - src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs
    - src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs
    - src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs
    - src/GameKit.Auth.Epic/Builder/EpicBuilderExtensions.cs
    - tests/GameKit.Auth.Epic.Tests/GameKit.Auth.Epic.Tests.csproj
    - tests/GameKit.Auth.Epic.Tests/EpicProviderTests.cs
  modified:
    - src/GameKit.Auth/AssemblyInfo.cs
decisions:
  - "EpicOAuthHandler is internal (not sealed) to allow InternalsVisibleTo test-seam subclass TestEpicOAuthHandler; no behavioral change since it cannot be extended from outside the assembly anyway"
  - "RESEARCH Open Q2 (Basic vs form-body auth) resolved at stub level via WireMock; live EOS confirmation deferred to Task 4 human-verify gate"
  - "account_id used as external_id (not email) per T-07-05-02 mitigation — Epic does not expose email in basic_profile scope"
metrics:
  duration: "8m 31s"
  completed: "2026-06-05T22:44:18Z"
  tasks: 3
  files: 10
requirements: [AUTH-21, AUTH-22]
---

# Phase 07 Plan 05: GameKit.Auth.Epic Summary

Epic Games OAuth provider for GameKit — custom `OAuthHandler<EpicOAuthOptions>` with Basic-auth `ExchangeCodeAsync` override, `EpicOAuthProvider` (discriminator "epic", external_id = account_id), `AddEpic()` self-registration, zero new NuGet dependencies.

## What Was Built

### GameKit.Auth.Epic Package (7 source files)

**`EpicOAuthOptions : OAuthOptions`** — Pre-configures the three EOS endpoints (authorize, token, userInfo), `basic_profile` scope, and `/signin-epic` callback path. Zero new NuGet dependency (OAuthHandler<T>/OAuthOptions are in the ASP.NET Core shared framework).

**`EpicOAuthHandler : OAuthHandler<EpicOAuthOptions>`** — Custom handler with two overrides:
- `ExchangeCodeAsync`: POSTs `grant_type/code/redirect_uri` as form body while sending client credentials in `Authorization: Basic base64(clientId:clientSecret)` header only — never in the form body (T-07-05-01 mitigation; RESEARCH §Pitfall 6).
- `CreateTicketAsync`: Calls the EOS userInfo endpoint with the bearer token; maps `account_id` → `ClaimTypes.NameIdentifier` and `display_name` → `ClaimTypes.Name`. `account_id` is the stable canonical identity key, not email (T-07-05-02 mitigation).

**`EpicOAuthProvider : IOAuthProvider`** — Provider discriminator `"epic"`; upserts Player + PlayerIdentity keyed by `(provider="epic", external_id=account_id)`; BannedCheckHelper ban check; fallback display name `EpicUser-{last6}`.

**`EpicBuilderExtensions.AddEpic()`** — Unconditionally `AddScoped<IOAuthProvider, EpicOAuthProvider>()` (Scrutor scans GameKit.Auth assembly only; Pitfall 4 fix); conditionally registers `AddOAuth<EpicOAuthOptions, EpicOAuthHandler>("Epic", ...)` when both ClientId and ClientSecret are present (T-07-05-04 mitigation).

**`src/GameKit.Auth/AssemblyInfo.cs`** — Added `InternalsVisibleTo("GameKit.Auth.Epic")` grant for `BannedCheckHelper` access (mirrors Google/Apple precedent from Plans 07-03/07-04).

### Tests (GameKit.Auth.Epic.Tests)

4 tests, all passing:
1. `DI_Smoke_EpicOAuthProvider_Registered_As_IOAuthProvider_Scoped` — AUTH-22 DI smoke
2. `ConditionalScheme_Absent_WhenClientIdEmpty_SchemeNotRegistered_ButProviderStillExists` — AUTH-22 conditional guard
3. `ProviderDiscriminator_IsEpic` — structural discriminator + no duplicates
4. `TokenExchange_UsesBasicAuth_WithWireMockStub` — **AUTH-21 key test**: starts a WireMock server, stubs the Epic token endpoint to require `Authorization: Basic <base64>`, drives `TestEpicOAuthHandler.ExchangeCodePublicAsync` (internal test-seam subclass), asserts exact header value matches `base64(clientId:clientSecret)` and no `client_id`/`client_secret` form fields. Resolves RESEARCH Open Q2 at the stub level.

## Task 4: Live EOS Round-Trip — DEFERRED (human-verify gate)

Task 4 is a `gate="blocking-human"` checkpoint that cannot be automated without real Epic EOS sandbox credentials. It is NOT fabricated as a pass.

**What is automated:** The Basic-auth wire format is proven against a WireMock stub (TokenExchange_UsesBasicAuth_WithWireMockStub passes). DI wiring, conditional scheme, and discriminator are fully verified.

**What requires human verification:**
1. In the Epic Games Dev Portal, create a product + EOS client (Client ID/Secret) with redirect URI set to `<host>/signin-epic`.
2. Configure `AddEpic(o => { o.ClientId = ...; o.ClientSecret = ...; })` in the sample host.
3. Perform an Epic login in a browser — confirm no `400 invalid_client` error (verifying that Epic's live token endpoint accepts `Authorization: Basic` exactly as the handler sends).
4. Confirm a `player_identities` row exists with `provider = 'epic'` and `external_id` equal to the Epic `account_id`.

**Documented fallback:** If Epic's live endpoint returns `400 invalid_client` with Basic auth, switch `ExchangeCodeAsync` to form-body client auth (`client_id`/`client_secret` as form fields) and re-run the WireMock test with a form-body stub instead.

**Resume signal:** Type "approved" (live exchange succeeded with Basic auth), "deferred" (ship without live verification — covered by stub tests + production-readiness gate), or describe the failure.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] EpicOAuthHandler changed from `sealed` to `internal` (non-sealed)**
- **Found during:** Task 3 (RED phase build)
- **Issue:** `EpicOAuthHandler` was declared `internal sealed`. The `TestEpicOAuthHandler` test-seam subclass (needed to expose `protected ExchangeCodeAsync` for WireMock testing) cannot derive from a `sealed` type.
- **Fix:** Removed `sealed` modifier. The class is still `internal`, so it cannot be extended from outside the assembly. No behavioral change.
- **Files modified:** `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs`
- **Commit:** 8a60fa7

None other — plan executed per spec.

## Known Stubs

None. The package is fully functional for the automated test surface. The only deferred item is the live EOS credential round-trip (Task 4, human-verify gate), which is not a stub — it is a deliberate human gate.

## Threat Flags

No new threat surface beyond what the plan's threat model covers. T-07-05-01 through T-07-05-05 are all addressed:
- T-07-05-01: Client secret is in Basic auth header only; never logged; confirmed by WireMock test.
- T-07-05-02: account_id used as external_id; NOT email; confirmed by CreateTicketAsync + OnCreatingTicket.
- T-07-05-03: Explicit AddScoped<IOAuthProvider, EpicOAuthProvider>; confirmed by DI_Smoke test.
- T-07-05-04: Conditional scheme registration; confirmed by ConditionalScheme_Absent test.
- T-07-05-05: Wire format proven at stub level; live confirmation deferred to Task 4 gate.

## Self-Check: PASSED

Files created/exist:
- src/GameKit.Auth.Epic/GameKit.Auth.Epic.csproj: FOUND
- src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs: FOUND
- src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs: FOUND
- src/GameKit.Auth.Epic/Builder/EpicBuilderExtensions.cs: FOUND
- tests/GameKit.Auth.Epic.Tests/EpicProviderTests.cs: FOUND

Commits exist:
- fa83a3d (Task 1 scaffold): FOUND
- 0c15fa0 (Task 2 provider + extensions): FOUND
- b5434a3 (Task 3 RED tests): FOUND
- 8a60fa7 (Task 3 GREEN handler fix): FOUND

Tests: 4 passed, 0 failed (dotnet test green).
Build: 0 warnings, 0 errors.
Zero migrations: confirmed (no Migrations/ directory added).
Zero new NuGet deps: confirmed (OAuthHandler<T> is shared-framework).
