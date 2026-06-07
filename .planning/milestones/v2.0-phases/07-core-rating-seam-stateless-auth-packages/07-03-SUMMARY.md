---
phase: 07
plan: 03
subsystem: auth
tags: [auth, oauth, google, provider, sibling-package]
dependency_graph:
  requires: [07-02]
  provides: [GameKit.Auth.Google, GoogleOAuthProvider, AddGoogle()]
  affects: [GameKit.Auth/AssemblyInfo.cs]
tech_stack:
  added:
    - Microsoft.AspNetCore.Authentication.Google 10.0.8 (first-party, pinned by 07-02)
  patterns:
    - IOAuthProvider self-registration (sibling assembly Scrutor gap workaround)
    - Conditional OAuth scheme registration (only when credentials present)
    - Google sub claim as external_id (not email — T-07-03-01 mitigation)
    - BannedCheckHelper + IssueRootAsync upsert flow (exact DiscordOAuthProvider analog)
    - InternalsVisibleTo grant for BannedCheckHelper access from sibling package
key_files:
  created:
    - src/GameKit.Auth.Google/GameKit.Auth.Google.csproj
    - src/GameKit.Auth.Google/AssemblyInfo.cs
    - src/GameKit.Auth.Google/Configuration/GameKitGoogleOptions.cs
    - src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs
    - src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs
    - tests/GameKit.Auth.Google.Tests/GameKit.Auth.Google.Tests.csproj
    - tests/GameKit.Auth.Google.Tests/GoogleProviderTests.cs
  modified:
    - src/GameKit.Auth/AssemblyInfo.cs (added InternalsVisibleTo("GameKit.Auth.Google"))
decisions:
  - "GoogleOAuthProvider uses ClaimTypes.NameIdentifier (Google sub) as external_id — NOT email. Email can change and is not unique across Google accounts (T-07-03-01 mitigation)"
  - "InternalsVisibleTo('GameKit.Auth.Google') added to GameKit.Auth/AssemblyInfo.cs to grant sibling package access to BannedCheckHelper — mirrors the Admin.Integration.Tests + Presence.Integration.Tests precedent"
  - "AddGoogle() unconditionally registers AddScoped<IOAuthProvider, GoogleOAuthProvider>() before the conditional scheme block — Scrutor only scans GameKit.Auth assembly (RESEARCH §Pitfall 4)"
  - "ConditionalScheme test uses GetService<IAuthenticationSchemeProvider>() (nullable) not GetRequiredService — when both SkipAuthenticationSchemeRegistration=true AND ClientId is absent, no auth infrastructure is registered at all"
metrics:
  duration: "5 min"
  completed: "2026-06-05"
  tasks_completed: 3
  files_created: 7
  files_modified: 1
---

# Phase 7 Plan 3: GameKit.Auth.Google Summary

Google OAuth2 provider for GameKit — IOAuthProvider backed by Microsoft.AspNetCore.Authentication.Google 10.0.8, using the Google `sub` claim as the stable external identity key, with conditional scheme registration and DI self-registration.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Google package scaffold (csproj, AssemblyInfo, Options) + GoogleOAuthProvider | 369cff2 | src/GameKit.Auth.Google/*.cs + InternalsVisibleTo grant on GameKit.Auth |
| 2 | GoogleBuilderExtensions — self-register provider + conditional Google scheme | 8178573 | src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs |
| 3 | Google DI-smoke + conditional-scheme + sub-not-email tests (TDD GREEN) | 2f5ac24 | tests/GameKit.Auth.Google.Tests/** |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] BannedCheckHelper inaccessible from sibling assembly**

- **Found during:** Task 1 build attempt
- **Issue:** `BannedCheckHelper` is `internal static` in `GameKit.Auth`. The `GameKit.Auth.Google` package is a separate assembly and cannot access internal types without an explicit `InternalsVisibleTo` grant. Build failed with CS0122.
- **Fix:** Added `[assembly: InternalsVisibleTo("GameKit.Auth.Google")]` to `src/GameKit.Auth/AssemblyInfo.cs` with a comment documenting the reason and the Apple/Epic sibling precedent for Plans 07-04/07-05.
- **Files modified:** `src/GameKit.Auth/AssemblyInfo.cs`
- **Commit:** 369cff2

**2. [Rule 1 - Bug] ConditionalScheme test used GetRequiredService for IAuthenticationSchemeProvider**

- **Found during:** Task 3 TDD RED run — test failed with `InvalidOperationException: No service for type IAuthenticationSchemeProvider`
- **Issue:** When `SkipAuthenticationSchemeRegistration=true` AND `ClientId` is null, `AddAuthentication()` is never called by either `AddAuth` or `AddGoogle`. The `IAuthenticationSchemeProvider` service is not registered, so `GetRequiredService<IAuthenticationSchemeProvider>()` throws.
- **Fix:** Changed to `GetService<IAuthenticationSchemeProvider>()` (nullable). When the service is null, no schemes are registered at all — which trivially satisfies the "Google scheme absent" assertion. The null-scheme path is documented with a comment explaining the T-07-03-04 mitigation is confirmed either way.
- **Files modified:** `tests/GameKit.Auth.Google.Tests/GoogleProviderTests.cs`
- **Commit:** 2f5ac24

## Verification Results

- `dotnet build src/GameKit.Auth.Google/GameKit.Auth.Google.csproj --nologo` — **PASS** (0 warnings, 0 errors, CS1591 enforced)
- `dotnet test tests/GameKit.Auth.Google.Tests/GameKit.Auth.Google.Tests.csproj --nologo` — **PASS** (3/3 tests, 0 failed)
- Migrations directory: **absent** (zero migrations — confirmed)

## Success Criteria Check

- [x] AUTH-19: GameKit.Auth.Google wraps Microsoft.AspNetCore.Authentication.Google 10.0.8 with GoogleOAuthProvider
- [x] AUTH-22 (Google): self-registered IOAuthProvider under (provider="google", external_id=sub) uniqueness; conditional scheme; minimal scopes
- [x] GoogleOAuthProvider discriminator "google" (not email); sub from ClaimTypes.NameIdentifier
- [x] AddGoogle() self-registers AddScoped<IOAuthProvider, GoogleOAuthProvider>() unconditionally
- [x] Conditional scheme: Google scheme absent when ClientId/ClientSecret absent
- [x] `dotnet build` succeeds — 0 warnings, CS1591 enforced
- [x] GoogleProviderTests — 3/3 passing
- [x] ZERO database migrations

## Known Stubs

None. The GoogleOAuthProvider wires the full upsert path identical to DiscordOAuthProvider. The live Google OAuth round-trip (code exchange → userinfo → claim extraction) is intentionally out-of-scope for unit tests (requires real credentials); this is documented in the test class XML comment.

## Threat Flags

No new threat surface beyond what was analyzed in the plan's threat model.
