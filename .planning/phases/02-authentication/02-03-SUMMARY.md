---
phase: 02-authentication
plan: 03
subsystem: authentication
tags:
  - authentication
  - options
  - builder
  - egress
  - http-resilience
  - middleware
dependencies:
  requires:
    - phase: 02-authentication
      plan: 01
      provides: "GameKit.Auth.Tests + Directory.Packages.props Auth pins (Microsoft.Extensions.Http.Resilience 10.5.0)"
  provides:
    - "GameKit.Auth.GameKitAuthOptions — root composite options type"
    - "GameKit.Auth.JwtOptions — issuer/audience/RSA PEM paths/kid/lifetimes"
    - "GameKit.Auth.SteamOptions — OpenID endpoint/callback/realm/api key"
    - "GameKit.Auth.DiscordOptions — client id/secret/callback/OAuth endpoints"
    - "GameKit.Auth.PasswordOptions — BCrypt work factor/username regex/min length"
    - "GameKit.Auth.Egress.DefaultAllowedHosts.All — public literal 4-host constant"
    - "GameKit.Auth.Egress.EgressViolationException — Auth-namespaced exception (host + message)"
    - "GameKit.Auth.Egress.EgressAllowListHandler — DelegatingHandler enforcing allow-list"
    - "GameKit.Auth.Builder.AuthBuilderExtensions.AddAuth — fluent extension on IGameKitBuilder"
    - "GameKit.Auth.Builder.AuthApplicationBuilderExtensions.UseGameKitAuth + MapAuth"
    - "Two named HttpClients (gamekit.auth.provider.steam / .discord) with resilience + egress"
    - "ValidateAuthOptions fail-fast validator invoked at AddAuth time"
  affects:
    - 02-04 (extends AddAuth with JwtIssuer + BCryptPasswordHasher)
    - 02-05 (extends AddAuth with IOAuthProvider discovery + Steam/Discord scheme wiring)
    - 02-06 (uses GameKitAuthOptions for password policy + username regex)
    - 02-07 (MapAuth filled in with /auth/* endpoints)
    - 02-08 (TicTacToeDuel sample app consumes AddAuth + UseGameKitAuth)
tech-stack:
  added:
    - "Microsoft.Extensions.Http.Resilience 10.5.0 (PackageReference in GameKit.Auth.csproj; central pin lands via 02-01)"
  patterns:
    - "Named HttpClient + AddHttpMessageHandler + AddStandardResilienceHandler (Polly v8 under the hood)"
    - "Public-literal-constant allow-list defaults (not config-only) — CONTEXT `<specifics>` contract"
    - "DelegatingHandler with snapshot HashSet<string> allow-list (case-insensitive)"
    - "Fail-fast options validator at AddAuth call-site (not IValidateOptions — no deferred validation)"
    - "SkipAuthenticationSchemeRegistration feature flag lets unit tests build DI without real PEM files"
    - "Dedicated `UseGameKitAuth()` extension in GameKit.Auth resolves RESEARCH Open Q #1 (Option B — keeps Core free of auth-scheme awareness)"
key-files:
  created:
    - "src/GameKit.Auth/GameKitAuthOptions.cs"
    - "src/GameKit.Auth/JwtOptions.cs"
    - "src/GameKit.Auth/SteamOptions.cs"
    - "src/GameKit.Auth/DiscordOptions.cs"
    - "src/GameKit.Auth/PasswordOptions.cs"
    - "src/GameKit.Auth/Egress/DefaultAllowedHosts.cs"
    - "src/GameKit.Auth/Egress/EgressViolationException.cs"
    - "src/GameKit.Auth/Egress/EgressAllowListHandler.cs"
    - "src/GameKit.Auth/Builder/AuthBuilderExtensions.cs"
    - "src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs"
    - "tests/GameKit.Auth.Tests/EgressAllowListHandlerTests.cs"
    - "tests/GameKit.Auth.Tests/AuthBuilderOptionsValidationTests.cs"
  modified:
    - "src/GameKit.Auth/GameKit.Auth.csproj (Microsoft.Extensions.Http.Resilience PackageReference)"
decisions:
  - "SkipAuthenticationSchemeRegistration flag — feature-flags the JwtBearer + Steam + Discord scheme registration so 02-03 unit tests can build the DI container without real RSA PEM files. Plans 02-04 (JwtIssuer) and 02-05 (Steam/Discord schemes) consume the skeleton and this flag stays off by default in production."
  - "AuthBuilderExtensions.AddAuth calls services.AddAuthentication(\"Bearer\") when scheme registration is NOT skipped — a placeholder so downstream plans (02-04/02-05) do not need to gate on whether AddAuthentication has already been called. Actual handler registration happens in plan 02-04."
  - "Egress handler registered as Transient (MS guidance for DelegatingHandler; handler itself is stateless apart from the snapshot HashSet)"
  - "HashSet allow-list is snapshotted from options at handler-construction time (not resolved per request) — reduces per-call overhead and makes the contract predictable when options change mid-lifetime"
  - "Public Egress namespace (GameKit.Auth.Egress) — ships DefaultAllowedHosts as a literal constant so a misconfigured appsettings.json cannot silently clear the list (CONTEXT `<specifics>` hard requirement)"
  - "Fail-fast validator inside AddAuth rather than IValidateOptions — we never want the process to boot with an unreadable PEM and only fail on the first login request"
  - "UseGameKitAuth lives in GameKit.Auth (Option B from RESEARCH Open Q #1) — Core stays free of authentication-scheme awareness. Consumer ordering: UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → MapGameKit → MapAuth"
  - "02-02 DI audit outcome: attempted fix, broader architectural scope discovered, workaround stays for now (see Deviations §1 and Follow-Up Items)"
requirements-completed: []
metrics:
  duration_minutes: 32
  tasks_completed: 3
  files_created: 12
  files_modified: 1
  tests_passing:
    auth_unit_new: 15
    auth_unit_total: 16
    auth_integration: 8
    core_unit: 130
    core_integration: 9
  completed_date: 2026-04-18
---

# Phase 02 Plan 03: Options + AddAuth + Egress Handler Summary

**GameKitAuthOptions composite options tree (Jwt / Steam / Discord / Password), literal `DefaultAllowedHosts.All` public constant, `EgressAllowListHandler` DelegatingHandler, `AddAuth(...)` fluent extension on `IGameKitBuilder` that registers two named HttpClients with resilience pipelines + egress enforcement, and `UseGameKitAuth()` / `MapAuth()` application-builder extensions that land `UseAuthentication()` in the correct middleware-ordering slot. Plus 15 new unit tests (9 egress handler + 6 options validation). Workaround from 02-02 deviation #4 is NOT retired — DI audit surfaced deeper architectural scope (see Deviations §1 and Follow-Up Items).**

## Performance

- **Duration:** 32 min
- **Tasks:** 3
- **Files created:** 12
- **Files modified:** 1

## Accomplishments

- Five options classes (GameKitAuthOptions / JwtOptions / SteamOptions / DiscordOptions / PasswordOptions) shipped with full XML doc comments on every public member; `TreatWarningsAsErrors` + `CS1591` clean.
- `GameKitAuthOptions.AllowedProviderHosts` is a mutable `List<string>` pre-populated from the public `DefaultAllowedHosts.All` literal constant (4 hosts: steamcommunity.com, api.steampowered.com, discord.com, discordapp.com) — a misconfigured `appsettings.json` cannot silently clear the list.
- `EgressAllowListHandler : DelegatingHandler` snapshots the allow-list at construction (case-insensitive HashSet) and throws `EgressViolationException` for any request whose URI host is not on the list.
- `AddAuth(opts => ...)` extends `IGameKitBuilder`: validates options, registers singleton `GameKitAuthOptions`, `TryAddEnumerable`s `AuthModelBuilderExtension : IModelBuilderExtension`, registers `EgressAllowListHandler` as transient, and creates two named HttpClients (`gamekit.auth.provider.steam`, `gamekit.auth.provider.discord`) with `AddHttpMessageHandler<EgressAllowListHandler>()` and `AddStandardResilienceHandler()` (Polly v8 pipelines via `Microsoft.Extensions.Http.Resilience`).
- `ValidateAuthOptions` fails fast on empty Issuer/Audience, missing RSA PEM files (skipped when `SkipAuthenticationSchemeRegistration = true`), and cleared `AllowedProviderHosts`.
- `UseGameKitAuth()` inserts `UseAuthentication()` ahead of Core's `UseAuthorization()` (RESEARCH §8.12 #6 middleware-ordering fix); `MapAuth()` stub is the extension point plan 02-07 fills in.
- 15 new unit tests green across two test classes (4 allowed-host theory rows + 3 off-list theory rows + 1 case-insensitive fact + 1 additional-host fact = 9 egress; 4 validator-failure facts + 1 skip-happy-path fact + 1 real-keys-happy-path fact = 6 validation) — total auth unit suite 16/16.
- Full-solution `dotnet test` green: 130 Core unit + 9 Core integration + 1 Core integration skipped (expected CleanInstall) + 16 Auth unit + 8 Auth integration + 1 CLI unit. No regressions.

## Task Commits

Each task committed atomically:

1. **Task 1: Options tree + egress primitives** — `8367ef6` (feat)
2. **Task 2: EgressAllowListHandler + AddAuth + UseGameKitAuth/MapAuth** — `5250fdc` (feat)
3. **Task 3: Unit tests** — `57c30b2` (test)

## Option Property Surface

### `GameKitAuthOptions` (root)

| Property | Type | Default |
|----------|------|---------|
| `Jwt` | `JwtOptions` | `new()` |
| `Steam` | `SteamOptions` | `new()` |
| `Discord` | `DiscordOptions` | `new()` |
| `Password` | `PasswordOptions` | `new()` |
| `AllowedProviderHosts` | `List<string>` | `new(DefaultAllowedHosts.All)` (4 hosts) |
| `SkipAuthenticationSchemeRegistration` | `bool` | `false` |

### `JwtOptions`

| Property | Type | Default |
|----------|------|---------|
| `Issuer` | `string` | `""` (required) |
| `Audience` | `string` | `""` (required) |
| `PrivateKeyPemPath` | `string` | `""` (required unless SkipAuthenticationSchemeRegistration) |
| `PublicKeyPemPath` | `string` | `""` (required unless SkipAuthenticationSchemeRegistration) |
| `Kid` | `string` | `"gamekit-jwt-kid-1"` |
| `AccessTokenLifetime` | `TimeSpan` | `15 min` (CONTEXT D-01) |
| `RefreshTokenLifetime` | `TimeSpan` | `30 days` (CONTEXT D-02) |
| `RefreshReuseInterval` | `TimeSpan` | `45 s` (CONTEXT D-05, D-06) |
| `ClockSkew` | `TimeSpan` | `30 s` (OWASP) |

### `SteamOptions`

| Property | Type | Default |
|----------|------|---------|
| `OpenIdEndpoint` | `string` | `"https://steamcommunity.com/openid/login"` |
| `CallbackPath` | `string` | `"/auth/callback/steam"` |
| `Realm` | `string` | `""` (required) |
| `ApiKey` | `string?` | `null` |

### `DiscordOptions`

| Property | Type | Default |
|----------|------|---------|
| `ClientId` | `string` | `""` (required) |
| `ClientSecret` | `string` | `""` (required) |
| `CallbackPath` | `string` | `"/auth/callback/discord"` |
| `AuthorizationEndpoint` | `string` | `"https://discord.com/api/oauth2/authorize"` |
| `TokenEndpoint` | `string` | `"https://discord.com/api/oauth2/token"` |
| `UserInfoEndpoint` | `string` | `"https://discord.com/api/users/@me"` |

### `PasswordOptions`

| Property | Type | Default |
|----------|------|---------|
| `BCryptWorkFactor` | `int` | `12` |
| `UsernameRegex` | `string` | `"^[a-zA-Z0-9_-]{3,32}$"` |
| `MinPasswordLength` | `int` | `12` |

## Verification

- `SkipAuthenticationSchemeRegistration` exists on `GameKitAuthOptions` (line 40) — verified.
- `TryAddEnumerable` registration of `AuthModelBuilderExtension` lands in `AuthBuilderExtensions.AddAuth` (one call-site) — verified by `AuthBuilderOptionsValidationTests.AddAuth_Happy_Path_With_Skip_Registers_Options_And_HttpClients` which asserts descriptor lifetime is `Singleton` and implementation-type name is `AuthModelBuilderExtension`.
- Test result counts per class: `EgressAllowListHandlerTests` — 9 (7 theory rows + 2 facts) passing. `AuthBuilderOptionsValidationTests` — 6 facts passing.
- No NuGet restore oddities with `Microsoft.Extensions.Http.Resilience 10.5.0` — package was pre-pinned in Directory.Packages.props by plan 02-01.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 4 — Architectural] `GameKitModelCustomizer` DI-resolution gap audit — attempted fix, reverted to workaround due to broader scope**

- **Found during:** Audit per 02-02 flagged follow-up (deviation #4 there — required test-local `AuthRuntimeQueryCustomizer` in `PlayerIdentityUniqueTests.cs`).
- **Investigation (3 fix attempts):**
  1. **Attempt 1 — `UseApplicationServiceProvider(sp)` in `AddGameKit`** (matching the `BuildMigrationContext` wiring in `GameKitApplicationBuilderExtensions.cs:87`). Switched `services.AddDbContext<GameKitDbContext>(dbOpts => ...)` to the `(sp, dbOpts)` overload + `.UseApplicationServiceProvider(sp)`. **Outcome:** Core unit tests stayed green (130/130), but `PlayerIdentity` was still not in the runtime query model — diagnostic test `DiAuditTests` confirmed the app-side `IEnumerable<IModelBuilderExtension>` contained `AuthModelBuilderExtension` but the EF-internal provider still did not flow it into the `ReplaceService`d `GameKitModelCustomizer` constructor.
  2. **Attempt 2 — Lazy resolution via `DbContext.GetService<IEnumerable<IModelBuilderExtension>>()`** inside `Customize()` with an optional constructor-injected collection fallback. **Outcome:** Same failure. `DbContext.GetService<T>` hits EF's internal provider, not the app provider, so the open-generic `IEnumerable<IModelBuilderExtension>` is still unresolved.
  3. **Attempt 3 — Reach through `IDbContextOptions.FindExtension<CoreOptionsExtension>().ApplicationServiceProvider`** and resolve from the app provider directly. **Outcome (this one worked for queries):** `PlayerIdentity` now appeared in the runtime query model AND `DiAuditTests` passed. But the Core migration path (which shares the same DI-resolved context) started failing with `PendingModelChangesWarning` because `MigrationRunner.MigrateWithLockAsync` was now seeing Auth entities in the Core model view during Core-migration-time — exactly the boundary violation that `AuthMigrationModelCustomizer` was invented to avoid.
- **Root cause:** The full fix requires splitting the model view into a **query path** (all entities visible via DI) and a **Core-migration path** (Core-only view), mirroring the existing `AuthMigrationModelCustomizer` pattern. That means introducing a new `CoreMigrationModelCustomizer`, updating `GameKitApplicationBuilderExtensions.UseGameKit` + `MigrationRunner` to use it, and running the Core migration context through its own `DbContextOptionsBuilder` path. Meaningful architectural surgery that affects every call-site of `GameKitDbContext` migration execution (Core unit tests, Core integration tests, `PackInstallMigrationTests`, CLI `gamekit migrate`).
- **Decision:** Per fix-attempt limit (Rule 4 architectural), revert all Core changes. Workaround (local `AuthRuntimeQueryCustomizer` inside `PlayerIdentityUniqueTests.cs`) stays. Documented in **Follow-Up Items** below for a dedicated Phase 2 gap plan or incorporation into a later plan.
- **Artifacts left in place:** None — all three Core-side experiments reverted (`GameKitServiceCollectionExtensions.cs` + `GameKitModelCustomizer.cs` unchanged from 02-02 state). Diagnostic `DiAuditTests.cs` deleted.
- **Tests still relying on workaround:** `tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs` still contains `AuthRuntimeQueryCustomizer` + uses it via `.ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>()`.

### Auto-fixed — Minor

**2. [Rule 1 — Bug] Forward-reference to `EgressAllowListHandler` in Task-1 XML docs triggered CS1574**

- **Found during:** First build after Task 1 option-class files were authored.
- **Issue:** `GameKitAuthOptions.AllowedProviderHosts` and `EgressViolationException` XML docs contained `<see cref="EgressAllowListHandler"/>` — a type that Task 2 authors. TreatWarningsAsErrors + DocumentationFile turned CS1574 into a build error.
- **Fix:** Initial Task-1 commit used plain `<c>EgressAllowListHandler</c>` text references; Task-2 commit upgraded them to full `<see cref="EgressAllowListHandler"/>` once the type existed.
- **Files affected:** `src/GameKit.Auth/GameKitAuthOptions.cs`, `src/GameKit.Auth/Egress/EgressViolationException.cs`
- **Committed in:** Task 1 (`8367ef6`, plain `<c>` references) + Task 2 (`5250fdc`, upgraded to `<see cref>`)

---

**Total deviations:** 1 Rule-4 architectural (investigation + revert, workaround retained), 1 Rule-1 minor (forward-cref ordering).
**Impact on plan:** No scope change. The DI audit conclusion (workaround stays, full fix deferred) is now documented with exact technical findings so the next attempt has three prior-art branches to reference.

## Follow-Up Items

- **FOLLOW-UP-02-03-01: `GameKitDbContext` model-view split for Core-migration vs runtime-query paths.**
  - **Concrete fix path:** (a) create `CoreMigrationModelCustomizer : RelationalModelCustomizer` that applies Core entity configurations + `ExcludeFromMigrations()` on Auth/Rankings/Matchmaking/Presence entity types; (b) update `MigrationRunner.MigrateWithLockAsync` to accept a factory that produces a migration-scoped `GameKitDbContext` with `ReplaceService<IModelCustomizer, CoreMigrationModelCustomizer>`; (c) update `GameKitApplicationBuilderExtensions.UseGameKit` + `GameKitServiceCollectionExtensions.AddGameKit` to thread the app `IServiceProvider` into EF options via `UseApplicationServiceProvider(sp)`; (d) rewrite the runtime `GameKitModelCustomizer.Customize` to resolve `IEnumerable<IModelBuilderExtension>` through `context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>().ApplicationServiceProvider?.GetServices<IModelBuilderExtension>()`; (e) delete the local `AuthRuntimeQueryCustomizer` from `PlayerIdentityUniqueTests.cs`.
  - **Affects:** `src/GameKit.Core/Data/MigrationRunner.cs`, `src/GameKit.Core/Data/GameKitModelCustomizer.cs`, `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs`, `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs`; new `src/GameKit.Core/Data/CoreMigrationModelCustomizer.cs`; test updates in `tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs`, potentially `tests/GameKit.Core.Integration.Tests/MigrationDeterminismTests.cs` + `CleanInstallMigrationTests.cs`.
  - **Recommendation:** Dedicated Phase 2 gap plan (proposed 02-03b or fold into 02-04) — it touches Core and the architecture rationale (query vs migration view split) is worth its own PLAN.md. The fix is cross-cutting: Rankings, Matchmaking, and Presence will all need the same split when they ship sibling IModelBuilderExtensions.

## Known Stubs

- `AuthApplicationBuilderExtensions.MapAuth` — intentional stub documented in its XML summary. Plan 02-07 registers concrete `/auth/*` endpoints. Flagging for the stub audit: the method currently just returns `routes` without mapping anything; the stub is intentional and documented both in the XML summary and inline comment.
- The `AddAuthentication("Bearer")` call inside `AddAuth` (when `SkipAuthenticationSchemeRegistration = false`) is a placeholder — no actual JwtBearer handler is wired yet. Plan 02-04 adds the JwtIssuer + the concrete `.AddJwtBearer(...)` scheme configuration; plan 02-05 adds `.AddSteam(...)` + `.AddDiscord(...)`. Documented in-source via the code comment above the call.

## Threat Flags

None. The files authored in this plan do not introduce any new network endpoints, auth paths, or trust boundaries beyond those in the plan's `<threat_model>` (which already enumerates T-02-04 SSRF via OAuth-callback-redirecting `Backchannel`, T-02-05 allow-list clearing via config merge, T-02-06 missing RSA PEM at startup, T-02-15 `UseAuthorization`-before-`UseAuthentication` ordering). Each threat's Rule-2 mitigation is implemented and verified:

| Threat ID | Mitigation | Verified By |
|-----------|------------|-------------|
| T-02-04 | `EgressAllowListHandler` throws `EgressViolationException` on off-list host; `DefaultAllowedHosts.All` is a public literal constant | `EgressAllowListHandlerTests.OffList_Host_Throws_EgressViolationException` (3 InlineData rows) |
| T-02-05 | `ValidateAuthOptions` throws when `AllowedProviderHosts.Count == 0` | `AuthBuilderOptionsValidationTests.AddAuth_Cleared_AllowedHosts_Throws` |
| T-02-06 | `ValidateAuthOptions` requires `PrivateKeyPemPath` + `PublicKeyPemPath` to exist on disk (unless `SkipAuthenticationSchemeRegistration = true`) | `AuthBuilderOptionsValidationTests.AddAuth_Missing_PrivateKey_Throws_When_Scheme_Registration_Not_Skipped` and `AddAuth_Happy_Path_With_Real_Keys_Does_Not_Throw` |
| T-02-15 | `UseGameKitAuth()` calls `UseAuthentication()` (dedicated extension in GameKit.Auth — Option B from RESEARCH Open Q #1); plan 02-07 will add an end-to-end `/auth/me` test asserting 200 with a valid token | Documented in `AuthApplicationBuilderExtensions` XML docs with strict ordering comment; full e2e verification scheduled for 02-07 |

## Self-Check: PASSED

**Files verified present on disk:**
- FOUND: src/GameKit.Auth/GameKitAuthOptions.cs
- FOUND: src/GameKit.Auth/JwtOptions.cs
- FOUND: src/GameKit.Auth/SteamOptions.cs
- FOUND: src/GameKit.Auth/DiscordOptions.cs
- FOUND: src/GameKit.Auth/PasswordOptions.cs
- FOUND: src/GameKit.Auth/Egress/DefaultAllowedHosts.cs
- FOUND: src/GameKit.Auth/Egress/EgressViolationException.cs
- FOUND: src/GameKit.Auth/Egress/EgressAllowListHandler.cs
- FOUND: src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
- FOUND: src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs
- FOUND: src/GameKit.Auth/GameKit.Auth.csproj (Microsoft.Extensions.Http.Resilience PackageReference present)
- FOUND: tests/GameKit.Auth.Tests/EgressAllowListHandlerTests.cs
- FOUND: tests/GameKit.Auth.Tests/AuthBuilderOptionsValidationTests.cs

**Commits verified in git log:**
- FOUND: 8367ef6 (Task 1 — options tree + egress primitives)
- FOUND: 5250fdc (Task 2 — AddAuth + EgressAllowListHandler + UseGameKitAuth/MapAuth)
- FOUND: 57c30b2 (Task 3 — unit tests)

---
*Phase: 02-authentication*
*Completed: 2026-04-18*
