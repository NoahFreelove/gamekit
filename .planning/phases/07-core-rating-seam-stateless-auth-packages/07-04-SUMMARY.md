---
phase: 07-core-rating-seam-stateless-auth-packages
plan: 04
subsystem: auth
tags: [auth, oauth, apple, sign-in-with-apple, es256, sibling-package, gpl, jwt]

requires:
  - phase: 07-02
    provides: "AspNet.Security.OAuth.Apple 10.0.0 + Microsoft.IdentityModel.Protocols.OpenIdConnect 8.14.0 pinned in Directory.Packages.props; GameKit.Auth.Apple sln entry"
  - phase: 07-03
    provides: "IOAuthProvider self-registration pattern (AddScoped<IOAuthProvider,Impl>()); InternalsVisibleTo BannedCheckHelper grants; GoogleOAuthProvider sibling shape as template"

provides:
  - "GameKit.Auth.Apple package: AppleOAuthProvider (IOAuthProvider, discriminator 'apple', sub-as-external_id, first-login-only relay-email+name to Metadata JSONB)"
  - "AppleBuilderExtensions.AddApple(): GenerateClientSecret=true, per-exchange ES256 via PrivateKey delegate, ClientSecretExpiresAfter=170d, conditional scheme registration"
  - "GameKitAppleOptions: ServiceId, TeamId, KeyId, PrivateKeyBase64, CallbackPath, ClientSecretExpiresAfter"
  - "AppleProviderTests: 4 tests — DI smoke, options-shape (expiry<180d), conditional-scheme guard, discriminator"

affects:
  - "Phase 10: account-merge logic will need IOAuthProvider.Provider=='apple' to dedup sub-keyed identities"
  - "Sample app: AddApple() wiring + GAMEKIT_APPLE_PRIVATEKEY_BASE64 env var documentation"

tech-stack:
  added:
    - "AspNet.Security.OAuth.Apple 10.0.0 (aspnet-contrib, Apache-2.0, same release train as Discord 10.0.0)"
  patterns:
    - "PrivateKey delegate returns ReadOnlyMemory<char> (PEM content) — not an ECDsa object; base64-decode UTF8 bytes at request time"
    - "Relay email passed through IOAuthProvider.avatarUrl slot on first login; stored to PlayerIdentity.Metadata JSONB (first-login-only); avatarUrl column remains null (Apple has no avatar)"
    - "IOAuthProvider self-registration (AddScoped<IOAuthProvider,AppleOAuthProvider>()) before conditional scheme — Scrutor scope-gap workaround"
    - "ClientSecretExpiresAfter default 170d (<180d Apple cap) in options class; asserted by unit test"

key-files:
  created:
    - "src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj"
    - "src/GameKit.Auth.Apple/AssemblyInfo.cs"
    - "src/GameKit.Auth.Apple/Configuration/GameKitAppleOptions.cs"
    - "src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs"
    - "src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs"
    - "tests/GameKit.Auth.Apple.Tests/GameKit.Auth.Apple.Tests.csproj"
    - "tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs"
  modified:
    - "src/GameKit.Auth/AssemblyInfo.cs — added InternalsVisibleTo('GameKit.Auth.Apple') for BannedCheckHelper access"

key-decisions:
  - "AppleOAuthProvider uses Apple sub (NOT email/relay-email) as external_id — relay email only provided by Apple on first authorization; email-as-key would break the UNIQUE(provider,external_id) contract on re-auth (T-07-04-02 mitigation)"
  - "Relay email + name passed through IOAuthProvider.avatarUrl slot on first login (keeps public interface intact); stored in PlayerIdentity.Metadata JSONB only when existing==null; never overwritten on subsequent logins"
  - "AppleAuthenticationOptions.PrivateKey delegate returns ReadOnlyMemory<char> (PEM text) — v10.0.0 API; the aspnet-contrib UsePrivateKey(Func<string,IFileInfo>) extension is file-system-based; the PrivateKey property supports in-memory PEM delivery"
  - "InternalsVisibleTo('GameKit.Auth.Apple') added to GameKit.Auth/AssemblyInfo.cs to grant BannedCheckHelper access — mirrors Google/Presence/OpenApi grants"
  - "Task 4 live round-trip DEFERRED: requires real Apple Developer .p8 + Service ID; all automatable logic (package, DI, options-shape, conditional-scheme) is fully implemented and unit-tested with throwaway P-256 key"

patterns-established:
  - "Apple sibling-package csproj: FrameworkReference + AspNet.Security.OAuth.Apple + ProjectReference to GameKit.Auth + GameKit.Build analyzer (same as Google)"
  - "First-login-only Metadata write: check existing==null branch; serialize relay_email+name to JsonDocument; subsequent logins do NOT update Metadata"

requirements-completed: [AUTH-20, AUTH-22]

duration: 15min
completed: 2026-06-05
---

# Phase 7 Plan 04: GameKit.Auth.Apple Summary

**Sign-In-with-Apple sibling package with per-exchange ES256 client secret (GenerateClientSecret=true), Apple sub-as-external_id, first-login-only relay email+name to PlayerIdentity.Metadata JSONB, and conditional scheme registration**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-06-05T22:15:00Z
- **Completed:** 2026-06-05T22:31:49Z
- **Tasks:** 3 automated + 1 deferred (human-verify gate)
- **Files modified:** 8

## Accomplishments

- GameKit.Auth.Apple package: `AppleOAuthProvider` implementing `IOAuthProvider` with `Provider="apple"`, Apple `sub` (not email) as `external_id`, relay email + name written to `PlayerIdentity.Metadata` JSONB on first login only — Apple does not return these on subsequent authorizations
- `AppleBuilderExtensions.AddApple()`: `GenerateClientSecret=true` (T-07-04-01), `PrivateKey` delegate decodes base64 PEM at request time, `ClientSecretExpiresAfter=170d` (< 180d Apple cap), conditional scheme registration (T-07-04-05), IOAuthProvider self-registration bypassing Scrutor scope gap (T-07-04-04)
- `GameKitAppleOptions`: `ServiceId`, `TeamId`, `KeyId`, `PrivateKeyBase64` (documented as env/secret, never bake into image — T-07-04-03), `CallbackPath=/signin-apple`, `ClientSecretExpiresAfter=170d`
- 4 unit tests: DI smoke (Scoped descriptor), options-shape (expiry<180d), conditional-scheme guard (no Apple scheme when credentials absent), discriminator ("apple") — all pass with throwaway P-256 key generated inline

## Task Commits

1. **Task 1+2: Apple package scaffold + AppleBuilderExtensions** — `e3f28dd` (feat)
2. **Task 3: AppleProviderTests** — `ee0fe0b` (test)
3. **Task 4: Live round-trip** — DEFERRED (human-verify gate, see below)

## Files Created/Modified

- `src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj` — package config; AspNet.Security.OAuth.Apple dep; no EFCore.Design
- `src/GameKit.Auth.Apple/AssemblyInfo.cs` — GPL header + InternalsVisibleTo("GameKit.Auth.Apple.Tests")
- `src/GameKit.Auth.Apple/Configuration/GameKitAppleOptions.cs` — ServiceId, TeamId, KeyId, PrivateKeyBase64, CallbackPath, ClientSecretExpiresAfter=170d
- `src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs` — IOAuthProvider; sub-as-external_id; first-login Metadata JSONB write
- `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs` — AddApple(); GenerateClientSecret=true; PrivateKey PEM delegate; FindFirst("sub"); conditional scheme
- `src/GameKit.Auth/AssemblyInfo.cs` — added InternalsVisibleTo("GameKit.Auth.Apple") for BannedCheckHelper
- `tests/GameKit.Auth.Apple.Tests/GameKit.Auth.Apple.Tests.csproj` — test project; mirrors Google.Tests shape
- `tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs` — 4 tests; throwaway key generated inline

## Decisions Made

1. `PrivateKey` property (not `UsePrivateKey` extension) — `AspNet.Security.OAuth.Apple` 10.0.0 `UsePrivateKey(Func<string,IFileInfo>)` is a file-system-based extension; `AppleAuthenticationOptions.PrivateKey` accepts `Func<string,CancellationToken,Task<ReadOnlyMemory<char>>>` which supports in-memory PEM delivery from an env var
2. Relay email passed through `IOAuthProvider.avatarUrl` slot — keeps the `IOAuthProvider` signature intact; Apple has no avatar URL concept; provider stores relay email in `Metadata` JSONB (not `AvatarUrl` column)
3. First-login-only Metadata write — `existing==null` branch serializes `{relay_email, name}` to `JsonDocument`; subsequent login branch intentionally does NOT touch `Metadata`
4. `InternalsVisibleTo("GameKit.Auth.Apple")` grant added to `GameKit.Auth/AssemblyInfo.cs` — mirrors the Google, Presence.Integration.Tests, OpenApi.Integration.Tests precedents

## Deviations from Plan

None — plan executed as written. The `UsePrivateKey` API discovery (only takes `Func<string,IFileInfo>` in v10.0.0, not an async ECDsa factory) was resolved by using the `PrivateKey` property directly — this is within the plan's intent (RESEARCH §Apple-specific delta mentions `ImportPkcs8PrivateKey` via BCL ECDsa; the actual delivery mechanism via the `PrivateKey` delegate achieves the same result with the PEM approach the aspnet-contrib library expects).

## Human-Verify Gate (Task 4) — DEFERRED

**Status:** Pending external Apple Developer credentials

The live Apple Sign-In round-trip (real .p8 key + Service ID → Apple token endpoint → `sub` extraction → `PlayerIdentity` upsert) requires credentials that are not available in this environment.

**To verify when ready for production:**

1. In the Apple Developer Portal, create a Sign-In-with-Apple Key (.p8), note Team ID + Key ID, and create a Services ID. Set the return URL to your host's `/signin-apple`.
2. Base64-encode the .p8 file content: `base64 -i AuthKey_XXXXX.p8 | tr -d '\n'`
3. Set `GAMEKIT_APPLE_PRIVATEKEY_BASE64` environment variable; configure `ServiceId`/`TeamId`/`KeyId` via `AddApple(...)`.
4. Run the sample host, perform a Sign-In-with-Apple flow in a browser.
5. Confirm a `player_identities` row exists with `provider = 'apple'` and `external_id` equal to the Apple `sub` (a stable opaque string — NOT an email address).
6. Confirm the relay email + name are in `Metadata` JSONB on the first login, and are NOT overwritten on a second login.

**Gate status:** deferred to production-readiness (covered by unit tests + in-code documentation of the security properties).

## Threat Mitigation Coverage

| Threat ID | Status |
|-----------|--------|
| T-07-04-01 (Static client secret → 6-month outage) | Mitigated — `GenerateClientSecret=true` + `ClientSecretExpiresAfter=170d` asserted by unit test |
| T-07-04-02 (Relay email as external_id) | Mitigated — `FindFirst("sub")` in OnCreatingTicket; relay email to Metadata only |
| T-07-04-03 (.p8 key leak via logs/image) | Mitigated — `PrivateKeyBase64` documented as env/secret; never logged; ephemeral PEM used inside handler only |
| T-07-04-04 (Provider not discovered) | Mitigated — explicit `AddScoped<IOAuthProvider, AppleOAuthProvider>()` verified by DI smoke test |
| T-07-04-05 (Handler throws in credential-less harness) | Mitigated — conditional scheme registration verified by ConditionalScheme_Absent test |

## Known Stubs

None — all implemented logic is wired to real DI and correct Apple authentication flow. The live round-trip human-verify gate is a credential availability constraint, not a stub.

## Threat Flags

None — no new security surface introduced beyond what the plan's threat model covers.

## Self-Check: PASSED

Files confirmed present:
- `src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj` ✓
- `src/GameKit.Auth.Apple/AssemblyInfo.cs` ✓
- `src/GameKit.Auth.Apple/Configuration/GameKitAppleOptions.cs` ✓
- `src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs` ✓
- `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs` ✓
- `tests/GameKit.Auth.Apple.Tests/GameKit.Auth.Apple.Tests.csproj` ✓
- `tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs` ✓
- `src/GameKit.Auth/AssemblyInfo.cs` (modified) ✓

Commits confirmed present: `e3f28dd` (feat), `ee0fe0b` (test) ✓

Build: `dotnet build src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj --nologo` → 0 warnings, 0 errors, NO NU1109 ✓
Tests: `dotnet test tests/GameKit.Auth.Apple.Tests/...` → 4/4 passed ✓
Migrations: none ✓

## Next Phase Readiness

- AUTH-20 satisfied: `GameKit.Auth.Apple` with per-exchange ES256 client secret, sub-as-canonical-identity, first-login-only name/email to Metadata, relay email stored as-is
- AUTH-22 satisfied (Apple): self-registered `IOAuthProvider` under `(provider="apple", external_id=sub)`; conditional scheme; minimal scopes (name, email)
- Zero migrations; no diamond-dependency downgrade
- Live round-trip deferred to production-readiness gate (external Apple Developer credentials required)

---
*Phase: 07-core-rating-seam-stateless-auth-packages*
*Completed: 2026-06-05*
