---
phase: 02-authentication
plan: 05
subsystem: authentication
tags:
  - authentication
  - steam
  - discord
  - oauth
  - openid-2.0
  - pluggable-providers
  - scrutor
  - wave-2
dependencies:
  requires:
    - phase: 02-authentication
      plan: 02
      provides: "PlayerIdentity entity + UNIQUE(provider, external_id) constraint"
    - phase: 02-authentication
      plan: 03
      provides: "GameKitAuthOptions.Steam + .Discord, named HttpClients 'gamekit.auth.provider.steam' and 'gamekit.auth.provider.discord' with EgressAllowListHandler + resilience pipeline"
    - phase: 02-authentication
      plan: 04
      provides: "IRefreshTokenService.IssueRootAsync, TokenPair DTO, AddAuth fluent extension extended with JwtBearer scheme"
  provides:
    - "GameKit.Auth.Providers.IOAuthProvider (pluggable strategy contract)"
    - "GameKit.Auth.Providers.OAuthResult (Ok/Fail DTO)"
    - "GameKit.Auth.Providers.Steam.SteamConstants (claimed_id regex via GeneratedRegex)"
    - "GameKit.Auth.Providers.Steam.SteamVerificationResult"
    - "GameKit.Auth.Providers.Steam.SteamOpenIdVerifier (in-house check_authentication roundtrip)"
    - "GameKit.Auth.Providers.Steam.SteamOAuthProvider (IOAuthProvider for steam; upsert + TokenPair)"
    - "GameKit.Auth.Providers.Discord.DiscordOAuthProvider (IOAuthProvider for discord)"
    - "GameKit.Auth.Providers.Discord.DiscordBackchannelPostConfigure (IPostConfigureOptions<DiscordAuthenticationOptions>)"
    - "AddAuth: Scrutor assembly-scan for IOAuthProvider + SteamOpenIdVerifier registration + Discord scheme (identify-only) + backchannel post-configure"
  affects:
    - "02-06 (Guest + Password providers extend IOAuthProvider; Scrutor scan picks them up automatically)"
    - "02-07 (/auth/callback/steam endpoint consumes SteamOpenIdVerifier then SteamOAuthProvider; /auth/callback/discord reads TokenPair stashed by OnCreatingTicket)"
    - "02-08 (TicTacToeDuel sample demonstrates Steam + Discord login flows via these providers)"
tech-stack:
  added:
    - "AspNet.Security.OAuth.Discord 10.0.0 (PackageReference in GameKit.Auth.csproj; central pin already landed in 02-01)"
    - "Scrutor 7.0.0 (PackageReference; assembly-scan-based DI registration for IOAuthProvider implementations)"
  patterns:
    - "Pluggable strategy via IOAuthProvider + Scrutor assembly scan — customer apps drop a custom IOAuthProvider in their own assembly and AddAuth picks it up automatically"
    - "In-house OpenID 2.0 server-side verification — NEVER trust the browser-provided claimed_id + sig without POSTing back to the OP with openid.mode=check_authentication (CONTEXT D-09; defends against T-02-17 forged-sig attacks at the protocol layer)"
    - "aspnet-contrib Discord handler integrated via IPostConfigureOptions<DiscordAuthenticationOptions> — .AddDiscord(d => d.Backchannel = ...) captures by value at options-creation time and is not DI-aware, so backchannel injection MUST happen post-configure (RESEARCH §6.3 + §6.6)"
    - "Discord scope is locked to 'identify' ONLY via Scope.Clear() + Scope.Add(\"identify\") — defends against T-02-18 (scope creep disclosing email/guilds); AUTH-07 / D-10"
    - "Provider-agnostic Player+PlayerIdentity upsert shape — both SteamOAuthProvider and DiscordOAuthProvider use the same AsNoTracking-lookup → track-if-found / create-both-if-not-found pattern; display name + avatar refresh on subsequent logins"
    - "SteamOpenIdVerifier is a scoped helper (NOT an IOAuthProvider) — it is invoked directly by the /auth/callback/steam endpoint (plan 02-07) BEFORE SteamOAuthProvider.CompleteLoginAsync; the provider trusts the caller to have verified"
  removed: []
key-files:
  created:
    - "src/GameKit.Auth/Providers/IOAuthProvider.cs (44 lines)"
    - "src/GameKit.Auth/Providers/OAuthResult.cs (24 lines)"
    - "src/GameKit.Auth/Providers/Steam/SteamConstants.cs (24 lines)"
    - "src/GameKit.Auth/Providers/Steam/SteamVerificationResult.cs (20 lines)"
    - "src/GameKit.Auth/Providers/Steam/SteamOpenIdVerifier.cs (107 lines)"
    - "src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs (104 lines)"
    - "src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs (97 lines)"
    - "src/GameKit.Auth/Providers/Discord/DiscordBackchannelPostConfigure.cs (43 lines)"
    - "tests/GameKit.Auth.Tests/SteamOpenIdVerifierTests.cs (126 lines)"
    - "tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs (83 lines)"
    - "tests/GameKit.Auth.Integration.Tests/SteamProviderTests.cs (235 lines)"
    - "tests/GameKit.Auth.Integration.Tests/DiscordProviderTests.cs (143 lines)"
  modified:
    - "src/GameKit.Auth/GameKit.Auth.csproj (+AspNet.Security.OAuth.Discord, +Scrutor; AspNet.Security.OpenId.Steam intentionally NOT added per D-09)"
    - "src/GameKit.Auth/Builder/AuthBuilderExtensions.cs (Scrutor scan + SteamOpenIdVerifier registration + DiscordBackchannelPostConfigure registration + Discord scheme with identify-only scope)"
decisions:
  - "publicOnly: false on Scrutor's AddClasses — our built-in providers are internal sealed, and Scrutor's default publicOnly: true would silently skip them. Discovered via ScrutorProviderDiscoveryTests initially failing; documented inline with a comment above the scan call."
  - "Discord scheme is only registered when ClientId+ClientSecret are supplied — prevents the aspnet-contrib handler from throwing on the first /auth/login/discord request in a test harness that doesn't plan to exercise Discord. JwtBearer is always registered when SkipAuthenticationSchemeRegistration=false."
  - "Steam is deliberately NOT a registered authentication scheme. SteamOpenIdVerifier is invoked directly by the /auth/callback/steam endpoint (plan 02-07). This is the D-09 split: in-house OpenID 2.0 implementation, no contrib package."
  - "SteamOpenIdVerifier is Scoped (not Singleton) — it is a thin helper around a named HttpClient and GameKitAuthOptions singleton, but Scoped preserves the option of future scoped dependencies (e.g., per-request replay-nonce tracking) without a lifetime-mismatch refactor."
  - "DiscordBackchannelPostConfigure is Singleton IPostConfigureOptions<DiscordAuthenticationOptions> — scoped narrowly to the Discord options type, NOT a global IPostConfigureOptions<AuthenticationSchemeOptions>. Mitigates T-02-19 (Discord backchannel pointed at attacker URL via accidental global handler collision)."
  - "OnCreatingTicket resolves the Discord IOAuthProvider by filtering IEnumerable<IOAuthProvider> for Provider==\"discord\" rather than via GetRequiredService<DiscordOAuthProvider>(). Scrutor registers as IOAuthProvider only; this avoids a second (redundant) concrete-type registration."
  - "Replay-attack dedupe (T-02-20) is accepted as residual risk for v1. OpenID 2.0 §11.4.2 says check_authentication is single-response per assertion; Steam tracks nonce reuse on its side. Application-layer nonce tracking deferred — documented in <threat_model> as 'accept'."
  - "External id hashing for PlayerIdentity.ExternalId is NOT applied in this plan — the column stores the raw SteamID64 / Discord snowflake (both are public identifiers anyway). D-11 409-body hashing (ExternalIdHasher from plan 02-04) applies only to conflict-response surfaces, not to row storage."
requirements-completed:
  - AUTH-05
  - AUTH-06
  - AUTH-07
threat_flags: []
metrics:
  duration_minutes: 12
  tasks_completed: 3
  files_created: 12
  files_modified: 2
  tests_passing:
    auth_unit_new: 6
    auth_unit_total: 35
    auth_integration_new: 4
    auth_integration_total: 21
    core_unit_total: 130
    core_integration_total: 9
    cli_total: 1
    grand_total: 196
  completed_date: 2026-04-18
---

# Phase 02 Plan 05: Steam + Discord OAuth Providers (Pluggable Strategy) Summary

**Ships the pluggable `IOAuthProvider` contract, the in-house `SteamOpenIdVerifier` (server-side `check_authentication` roundtrip per CONTEXT D-09 — no `AspNet.Security.OpenId.Steam` dependency), the `DiscordOAuthProvider` wired through aspnet-contrib's `.AddDiscord` with `Options.Backchannel` routed through the egress-allow-listed named HttpClient via `IPostConfigureOptions<DiscordAuthenticationOptions>`, and Scrutor-based provider discovery in `AddAuth`. Success Criterion #2 (forged Steam callback rejected) proven at both unit (WireMock `is_valid:false`) and integration (no PlayerIdentity row written) scopes. 10 new tests (6 unit + 4 integration) green; zero regressions across all 6 test projects.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-04-18T18:48:39Z
- **Completed:** 2026-04-18T19:00:29Z
- **Tasks:** 3 (all auto-executed)
- **Files created:** 12 (8 production + 4 tests)
- **Files modified:** 2 (GameKit.Auth.csproj + AuthBuilderExtensions.cs)

## What Shipped

### Task 1: IOAuthProvider + Steam primitives (commit 7bfdb59)

- `IOAuthProvider` — pluggable strategy contract with a security-contract remarks block: implementers MUST perform provider-side verification BEFORE calling `CompleteLoginAsync`. Customers dropping custom providers into their own assembly are documented as inside their own trust boundary.
- `OAuthResult` — sealed record with `Ok(playerId, tokens)` and `Fail(errorCode)` factories.
- `SteamConstants` — `[GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(\d{17})$")]` partial class + `DefaultOpenIdEndpoint` + `IsValidTrueLine` string literal.
- `SteamVerificationResult` — sealed record with `Ok(steamId64)` / `Invalid(errorCode)` factories.
- `SteamOpenIdVerifier` — in-house OpenID 2.0 verifier. POSTs every `openid.*` query param back to the OP endpoint with `openid.mode=check_authentication`; parses Key-Value form response line-by-line; returns `SteamID64` on `is_valid:true`, error codes on `claimed_id_missing` / `claimed_id_malformed` / `check_authentication_http_error` / `is_valid_false`. Consumes the pre-configured `gamekit.auth.provider.steam` named HttpClient (egress-allow-listed + resilience pipeline from plan 02-03).

### Task 2: Provider implementations + AddAuth extensions (commit 03255c0)

- `SteamOAuthProvider : IOAuthProvider` (internal sealed) — upserts `Player` + `PlayerIdentity` for an already-verified Steam ID; display-name fallback `SteamUser-{last-8-of-steamid64}`; refreshes `DisplayName` / `AvatarUrl` / `UpdatedAt` on subsequent logins; issues `TokenPair` via `IRefreshTokenService.IssueRootAsync`.
- `DiscordOAuthProvider : IOAuthProvider` (internal sealed) — same upsert shape; display-name fallback `DiscordUser-{last-6-of-snowflake}`.
- `DiscordBackchannelPostConfigure : IPostConfigureOptions<DiscordAuthenticationOptions>` — injects `options.Backchannel = factory.CreateClient("gamekit.auth.provider.discord")`. Narrowly scoped to Discord's options type (T-02-19 mitigation).
- `AuthBuilderExtensions.AddAuth` extended with:
  - Scrutor assembly-scan `.FromAssemblyOf<IOAuthProvider>().AddClasses(c => c.AssignableTo<IOAuthProvider>(), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime()` — picks up both providers here and future Guest/Password in plan 02-06.
  - `builder.Services.AddScoped<SteamOpenIdVerifier>()` so the endpoint layer can resolve it.
  - `builder.Services.AddSingleton<IPostConfigureOptions<DiscordAuthenticationOptions>, DiscordBackchannelPostConfigure>()` — registered UNCONDITIONALLY (even when scheme registration is skipped, for DI introspection).
  - Inside the `SkipAuthenticationSchemeRegistration == false` block: conditional `.AddDiscord(...)` wiring (only when `ClientId`+`ClientSecret` supplied) with `Scope.Clear(); Scope.Add("identify");` (AUTH-07 / D-10), `SaveTokens = false`, and an `OnCreatingTicket` handler that resolves the Discord `IOAuthProvider` via `IEnumerable<IOAuthProvider>` filter and stashes the minted TokenPair in `Properties.Items` for plan 02-07's callback endpoint.
  - Steam scheme deliberately NOT registered (D-09).
- `GameKit.Auth.csproj`: `+<PackageReference Include="AspNet.Security.OAuth.Discord" />`, `+<PackageReference Include="Scrutor" />`. `AspNet.Security.OpenId.Steam` intentionally ABSENT and commented in the csproj.

### Task 3: Four test classes (commit 6a46fca)

- `SteamOpenIdVerifierTests` (unit, 4 cases): `Valid_Assertion_Returns_SteamID64`, `Malformed_ClaimedId_Returns_Invalid`, `Forged_Assertion_IsValid_False_Returns_Invalid` (the **unit-level proof of Success Criterion #2**), `Empty_ClaimedId_Returns_Invalid`. Uses `WireMockFixture` + `WireMockSteamStubs.StubIsValidFalse()` to drive the forgery rejection path.
- `ScrutorProviderDiscoveryTests` (unit, 2 cases): asserts both built-in providers are registered as `IOAuthProvider` descriptors and all descriptors carry `ServiceLifetime.Scoped`.
- `SteamProviderTests` (integration, 3 cases): real Postgres + WireMock. `CompleteLoginAsync_Creates_Player_And_Identity_On_First_Login`, `CompleteLoginAsync_Second_Call_Same_SteamId_Reuses_Player`, `Forged_Assertion_Rejected_By_Verifier_And_No_Row_Written` (the **integration-level proof of Success Criterion #2** — forged callback → `SteamOpenIdVerifier.VerifyAsync` returns `is_valid_false` → `PlayerIdentity` row count for the attempted external id remains 0).
- `DiscordProviderTests` (integration, 1 case): real Postgres upsert for a verified Discord snowflake.

All four test classes mirror the `AuthRuntimeQueryCustomizer` workaround from RefreshTokenServiceTests / IsGuestResolverTests (FOLLOW-UP-02-03-01 DI-gap).

## D-09 Compliance

- `AspNet.Security.OpenId.Steam` is NOT pinned in `Directory.Packages.props` (count: 0 for the raw package name, commented explanatory lines only).
- `AspNet.Security.OpenId.Steam` is NOT referenced in `src/GameKit.Auth/GameKit.Auth.csproj` (only a comment documenting the intentional absence).
- `grep -rn AspNet.Security.OpenId.Steam src/ Directory.Packages.props` returns only XML doc comments explicitly documenting the absence.

## AUTH-07 / D-10 Compliance (Discord scope lock)

`grep -c 'discord.Scope.Clear()' src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` → 1
`grep -c 'discord.Scope.Add("identify")' src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` → 1

Both guards are present; no other scope value is ever added.

## Backchannel Egress Compliance

`grep -c 'CreateClient("gamekit.auth.provider.discord")' src/GameKit.Auth/Providers/Discord/DiscordBackchannelPostConfigure.cs` → 1
`grep -c 'CreateClient("gamekit.auth.provider.steam")' src/GameKit.Auth/Providers/Steam/SteamOpenIdVerifier.cs` → 1

Both providers use the pre-configured named HttpClients that wear `EgressAllowListHandler` + resilience pipeline.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Scrutor `publicOnly: false` flag required for internal providers**
- **Found during:** Task 3 (ScrutorProviderDiscoveryTests initial run — 2 failures, 4 passes)
- **Issue:** `SteamOAuthProvider` and `DiscordOAuthProvider` are `internal sealed`; Scrutor's `AddClasses(selector)` defaults to `publicOnly: true` and silently registered zero types. `IEnumerable<IOAuthProvider>` resolved to an empty collection.
- **Fix:** Pass `publicOnly: false` to `AddClasses(c => c.AssignableTo<IOAuthProvider>(), publicOnly: false)`. Added inline comment explaining why this is safe for customer-authored providers.
- **Files modified:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs`
- **Commit:** 6a46fca (folded into the Task 3 test commit because the fix was discovered by the tests and was required for them to pass).

**2. [Rule 1 - Plan divergence] `OnCreatingTicket` resolves Discord provider via IEnumerable filter, not concrete type**
- **Found during:** Task 2 authoring
- **Issue:** The plan's pseudo-code did `ctx.HttpContext.RequestServices.GetRequiredService<DiscordOAuthProvider>()`. Scrutor registers the class only under its `IOAuthProvider` interface (via `AsImplementedInterfaces()`), not under its concrete type. Asking for the concrete type would throw at runtime.
- **Fix:** Filter `GetServices<IOAuthProvider>()` by `p.Provider == "discord"`. This is idiomatic (no double registration needed) and the first-match loop is O(n) over ≤4 providers — negligible cost.
- **Files modified:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (inside the `.AddDiscord(...)` `OnCreatingTicket` callback)
- **Commit:** 03255c0

### No other deviations

Plan tasks, verification steps, and done criteria executed as written.

## Authentication Gates

None encountered. All verification used local Testcontainers (Docker) + in-process WireMock — no external service auth required.

## Test Counts

| Class                                    | Cases  | Status |
| ---------------------------------------- | ------ | ------ |
| SteamOpenIdVerifierTests (unit)          | 4      | Passed |
| ScrutorProviderDiscoveryTests (unit)     | 2      | Passed |
| SteamProviderTests (integration)         | 3      | Passed |
| DiscordProviderTests (integration)       | 1      | Passed |
| **New (this plan)**                      | **10** | Passed |
| Auth unit total                          | 35     | Passed |
| Auth integration total                   | 21     | Passed |
| Core unit (regression)                   | 130    | Passed |
| Core integration (regression)            | 9      | Passed |
| Cli (regression)                         | 1      | Passed |
| **Grand total**                          | **196** | Passed |

## Package Version Verified

- `AspNet.Security.OAuth.Discord` **10.0.0** restored cleanly (already pinned in `Directory.Packages.props` via plan 02-01 Task 1).
- `Scrutor` **7.0.0** restored cleanly (already pinned in `Directory.Packages.props` via plan 02-01 Task 1).
- No `AspNet.Security.OpenId.Steam` package at any version — intentional per D-09.

## Known Stubs

None. Every public and internal surface produced in this plan has a live implementation and at least one test exercising it. The `/auth/login/steam` + `/auth/callback/steam` + `/auth/callback/discord` minimal-API endpoints are explicitly deferred to plan 02-07 and will consume (not stub) the types produced here.

## Follow-ups

- `FOLLOW-UP-02-03-01` (DI-gap around `IModelBuilderExtension`) remains open; the four new integration test classes continue to use the local `AuthRuntimeQueryCustomizer` workaround. Fix belongs to a dedicated gap plan.
- T-02-20 (OpenID 2.0 assertion replay) accepted as residual risk; Steam's OP tracks nonce reuse on its side. Application-layer nonce tracking can be added in a follow-up plan if operators report reuse attacks in practice.
- Plan 02-07's WebApplicationFactory tests will prove success criterion #2 at the full HTTP pipeline level (302-redirect flow, real callback URL, end-to-end egress) — this plan's tests cover the verifier + provider unit/integration boundary.

## Self-Check: PASSED

- 8 production files exist under `src/GameKit.Auth/Providers/`.
- 4 test files exist under `tests/GameKit.Auth.Tests/` and `tests/GameKit.Auth.Integration.Tests/`.
- 3 commits recorded: 7bfdb59 (Task 1), 03255c0 (Task 2), 6a46fca (Task 3).
- All 196 tests green.
- D-09 verified: no Steam contrib package anywhere in `src/` or `Directory.Packages.props`.
- AUTH-07 verified: `Scope.Clear()` + `Scope.Add("identify")` present exactly once each.
- AUTH-06 verified: `openid.mode=check_authentication` roundtrip + forgery rejection proven at both unit and integration scopes.
- AUTH-05 verified: Scrutor scan + Scoped-lifetime registrations asserted by `ScrutorProviderDiscoveryTests`.
