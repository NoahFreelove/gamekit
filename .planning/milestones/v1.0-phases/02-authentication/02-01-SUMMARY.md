---
phase: 02-authentication
plan: 01
subsystem: authentication
tags:
  - authentication
  - test-scaffolding
  - wave-0
dependencies:
  requires:
    - Phase 1 complete (GameKit.Core + TestFixtures PostgresFixture/RedisFixture shipped)
  provides:
    - tests/GameKit.Auth.Tests         # unit-test project consumed by 02-02..02-08
    - tests/GameKit.Auth.Integration.Tests  # integration-test project consumed by 02-02..02-08
    - WireMockFixture + Steam/Discord default stubs (reusable across every Auth integration test)
    - AuthCollection xUnit collection (Postgres + Redis + WireMock shared-fixture scope)
    - AuthIntegrationFixture composite (used by WebApplicationFactory bootstrap in 02-07)
    - Directory.Packages.props Auth-stack pins (BCrypt, Http.Resilience, Discord, WireMock, Mvc.Testing, IdentityModel)
  affects:
    - Directory.Packages.props
    - .planning/STATE.md (pre-flight checkbox + Decisions Locked rows)
    - GameKit.sln
    - src/GameKit.Auth/AssemblyInfo.cs (InternalsVisibleTo + AuthMarker sentinel)
tech-stack:
  added:
    - "AspNet.Security.OAuth.Discord 10.0.0 (net10.0 explicit)"
    - "BCrypt.Net-Next 4.1.0 (net10.0 explicit — bump from 4.0.3)"
    - "Microsoft.Extensions.Http.Resilience 10.5.0 (net10.0 explicit)"
    - "Microsoft.IdentityModel.Tokens 8.3.0 (net9.0 via rollforward)"
    - "System.IdentityModel.Tokens.Jwt 8.3.0 (net9.0 via rollforward)"
    - "WireMock.Net 2.2.0 (net8.0 via rollforward)"
    - "Microsoft.AspNetCore.Mvc.Testing 10.0.0 (net10.0 explicit)"
  patterns:
    - "WireMockFixture: IAsyncLifetime wrapping WireMockServer.Start() with exposed Steam/Discord stub URLs"
    - "Shared xUnit collection (AuthCollection) bundling Postgres + Redis + WireMock; re-declared per test assembly (xUnit1041)"
    - "Composite AuthIntegrationFixture hand-constructed by WebApplicationFactory bootstrap code"
    - "Central AspNet.Security.OpenId.Steam NOT pinned — D-09 locks in-house SteamOpenIdVerifier"
key-files:
  created:
    - tests/GameKit.TestFixtures/WireMockFixture.cs
    - tests/GameKit.TestFixtures/WireMockSteamStubs.cs
    - tests/GameKit.TestFixtures/WireMockDiscordStubs.cs
    - tests/GameKit.TestFixtures/AuthIntegrationFixture.cs
    - tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj
    - tests/GameKit.Auth.Tests/SmokeTests.cs
    - tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj
    - tests/GameKit.Auth.Integration.Tests/WireMockReachabilitySmokeTests.cs
    - tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs
  modified:
    - Directory.Packages.props
    - tests/GameKit.TestFixtures/GameKit.TestFixtures.csproj
    - tests/GameKit.TestFixtures/CollectionDefinitions.cs
    - src/GameKit.Auth/AssemblyInfo.cs
    - GameKit.sln
    - .planning/STATE.md
decisions:
  - "WireMock.Net 2.2.0 consumed via net8.0 TFM rollforward — acceptable per RESEARCH §4 / PLAN §Notes"
  - "IdentityModel.Tokens 8.3.0 + System.IdentityModel.Tokens.Jwt 8.3.0 pinned (latest 8.3.x; newer 8.x also available, 8.3.0 matches RESEARCH §4 guidance)"
  - "Microsoft.Extensions.Hosting PackageReference intentionally omitted from GameKit.Auth.Integration.Tests.csproj — NU1510-as-error; supplied via FrameworkReference Microsoft.AspNetCore.App (same pattern as Phase 1 P05 Caching.Memory precedent)"
  - "AuthCollection re-declared in tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs — xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in the same assembly as consuming test classes"
  - "AspNet.Security.OpenId.Steam intentionally NOT pinned — D-09 (in-house SteamOpenIdVerifier)"
metrics:
  duration_minutes: 6
  tasks_completed: 3
  files_created: 9
  files_modified: 6
  tests_passing:
    unit_smoke: 1
    integration_smoke: 4
  completed_date: 2026-04-18
---

# Phase 02 Plan 01: Wave-0 Test Scaffolding Summary

Stood up the Wave-0 test scaffolding every subsequent Phase-2 plan depends on: two new xUnit test projects (`GameKit.Auth.Tests` + `GameKit.Auth.Integration.Tests`), a reusable `WireMockFixture` + Steam/Discord default stubs, an `AuthIntegrationFixture` composite for WebApplicationFactory bootstrap, Directory.Packages.props pins for the full Auth stack, and the flipped STATE.md pre-flight checkbox documenting the verified net10.0 TFM for every Auth dependency.

## Outcomes

### Task 1 — Directory.Packages.props pins + STATE pre-flight flip

**Commit:** `1c0d8cc`

All pins verified against the NuGet flat-container `.nuspec` endpoint on 2026-04-18 before commit:

| Package                                    | Pinned Version | Verified TFM        |
|--------------------------------------------|----------------|---------------------|
| AspNet.Security.OAuth.Discord              | 10.0.0         | `net10.0` (explicit) |
| BCrypt.Net-Next                            | 4.1.0          | `net10.0` (explicit) |
| Microsoft.Extensions.Http.Resilience       | 10.5.0         | `net10.0` (explicit) |
| Microsoft.IdentityModel.Tokens             | 8.3.0          | `net9.0` (rollforward) |
| System.IdentityModel.Tokens.Jwt            | 8.3.0          | `net9.0` (rollforward) |
| WireMock.Net                               | 2.2.0          | `net8.0` (rollforward) |
| Microsoft.AspNetCore.Mvc.Testing           | 10.0.0         | `net10.0` (explicit) |

**AspNet.Security.OpenId.Steam is intentionally NOT pinned** per CONTEXT D-09 — GameKit ships an in-house `SteamOpenIdVerifier` that performs the server-side `check_authentication` roundtrip using a named `HttpClient` against `steamcommunity.com`. The contrib package must not be added as a dependency.

STATE.md pre-flight gate changes:
- Old line 40: `- [ ] Verify AspNet.Security.OpenId.Steam 10.0.x + AspNet.Security.OAuth.Discord 10.0.x net10.0 TFM (blocks Phase 2, not Phase 1 start, but track now)`
- New: `- [x] Verify AspNet.Security.OAuth.Discord 10.0.x net10.0 TFM — 10.0.0 verified GA 2026-04-18 (nuspec explicit net10.0)`
- New: `- [N/A] AspNet.Security.OpenId.Steam — intentionally NOT pinned per D-09 (in-house SteamOpenIdVerifier replaces contrib package)`

STATE.md "Decisions Locked" appended three rows:
- `BCrypt.Net-Next pin bumped 4.0.3 -> 4.1.0 (RESEARCH §4 verified net10.0 TFM) | 02-01 execution`
- `Microsoft.Extensions.Http.Resilience 10.5.0 pinned for named-HttpClient resilience pipelines | 02-01 execution`
- `AspNet.Security.OpenId.Steam intentionally NOT pinned — in-house SteamOpenIdVerifier replaces contrib package (D-09) | 02-01 execution`

Verification: `dotnet restore GameKit.sln` exits 0; `dotnet build GameKit.sln` succeeds with 0 errors, 0 warnings.

### Task 2 — WireMockFixture + Steam/Discord stubs + AuthCollection

**Commit:** `b11302d`

- **`WireMockFixture.cs`** (57 lines): `IAsyncLifetime` starting a WireMockServer on an ephemeral port; exposes `SteamOpenIdLoginUrl`, `DiscordTokenUrl`, `DiscordUserInfoUrl`; applies Steam + Discord defaults on init; `ResetDefaultStubs()` helper for tests that override per-case.
- **`WireMockSteamStubs.cs`** (41 lines): exact OpenID 2.0 §11.4.2 Key-Value form bodies for the `is_valid:true` / `is_valid:false` paths (ns + is_valid lines separated by `\n`, trailing `\n`); `StubIsValidFalse(server)` helper for the forgery-rejection test (Phase 2 success criterion #2).
- **`WireMockDiscordStubs.cs`** (48 lines): default positive-path stubs for the token endpoint (access/refresh token + `scope = "identify"`) and `/users/@me` (default snowflake `123456789012345678` + username `mock_user` + discriminator `0001`). Uses synthetic IDs not drawn from the real Steam `7656119…` space to avoid accidental real-user collision (threat T-02-13).
- **`CollectionDefinitions.cs`** (modified): appended `[CollectionDefinition("Auth")]` class bundling Postgres + Redis + WireMock as `ICollectionFixture`s. The three existing collections (`PostgresCollection`, `RedisCollection`, `PostgresAndRedisCollection`) are preserved unchanged.
- **`AuthIntegrationFixture.cs`** (31 lines): pass-through composite holding all three fixtures so WebApplicationFactory bootstrap code in Plan 02-07 can inject one parameter instead of three. Not an `ICollectionFixture` itself.
- **`GameKit.TestFixtures.csproj`**: `WireMock.Net` `PackageReference` added.

Verification: `dotnet build tests/GameKit.TestFixtures/GameKit.TestFixtures.csproj` succeeds with 0 errors, 0 warnings.

### Task 3 — Auth test projects + AuthMarker sentinel + solution wiring

**Commit:** `62d6644`

- **`tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj`** (21 lines): unit-test project with `FrameworkReference Microsoft.AspNetCore.App`, `Moq` + `Microsoft.EntityFrameworkCore.InMemory` package refs, and project refs to `src/GameKit.Auth` + `tests/GameKit.TestFixtures`. Inherits xUnit + Test.Sdk from `tests/Directory.Build.props`.
- **`tests/GameKit.Auth.Tests/SmokeTests.cs`** (21 lines): `[Trait("Category", "Smoke")]` class with one `[Fact]` `Assembly_Loads()` that verifies the `GameKit.Auth` assembly name via `typeof(GameKit.Auth.AuthMarker).Assembly`. Runs in 3 ms.
- **`tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj`** (27 lines): integration-test project with `FrameworkReference Microsoft.AspNetCore.App`, package refs to `Npgsql` + `StackExchange.Redis` + `WireMock.Net` + `Microsoft.AspNetCore.Mvc.Testing`, and project refs to `src/GameKit.Auth` + `tests/GameKit.TestFixtures`.
- **`tests/GameKit.Auth.Integration.Tests/WireMockReachabilitySmokeTests.cs`** (61 lines): `[Collection("Auth")] [Trait("Category", "Smoke")]` class with four `[Fact]`s — Steam default stub returns `is_valid:true` body, Discord `/users/@me` stub returns default snowflake + username, Postgres fixture exposes all three role connection strings, Redis fixture is up. All 4 pass in 369 ms on the primed fixtures (Testcontainers Postgres 17.9 + Redis 8.6.2 + WireMock.Net 2.2.0).
- **`tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs`** (20 lines): local re-declaration of `[CollectionDefinition("Auth")]` required by xUnit analyzer rule xUnit1041 (see Deviations).
- **`src/GameKit.Auth/AssemblyInfo.cs`** (modified): appends `[assembly: InternalsVisibleTo("GameKit.Auth.Tests")]` + `[assembly: InternalsVisibleTo("GameKit.Auth.Integration.Tests")]`; defines `internal static class AuthMarker` as the sentinel type the smoke test uses to prove the assembly loaded.
- **`GameKit.sln`**: both new test projects registered via `dotnet sln add`.

Verification:
- `dotnet build tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` — exits 0, 0 warnings
- `dotnet build tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj` — exits 0, 0 warnings
- `dotnet test tests/GameKit.Auth.Tests --filter Category=Smoke` — **Passed 1/1** (Duration 3 ms)
- `dotnet test tests/GameKit.Auth.Integration.Tests --filter Category=Smoke` — **Passed 4/4** (Duration 369 ms)
- `dotnet build GameKit.sln` — 0 errors, 0 warnings

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed redundant `Microsoft.Extensions.Hosting` PackageReference from `GameKit.Auth.Integration.Tests.csproj`**

- **Found during:** Task 3 build
- **Issue:** `error NU1510: Warning As Error: PackageReference Microsoft.Extensions.Hosting will not be pruned. Consider removing this package from your dependencies, as it is likely unnecessary.` The csproj already declares `FrameworkReference Microsoft.AspNetCore.App`, which transitively supplies `Microsoft.Extensions.Hosting`.
- **Fix:** Removed the explicit `PackageReference Include="Microsoft.Extensions.Hosting"` line; left a short comment explaining the rationale pointing at the Phase 1 P05 NU1510 precedent (STATE.md Decisions Locked row: "FrameworkReference Microsoft.AspNetCore.App replaces explicit Caching.Memory PackageReference").
- **Files modified:** `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj`
- **Commit:** `62d6644`

**2. [Rule 3 - Blocking] Added local `CollectionDefinitions.cs` in `GameKit.Auth.Integration.Tests` to re-declare `[CollectionDefinition("Auth")]`**

- **Found during:** Task 3 build
- **Issue:** xUnit analyzer rule `xUnit1041` errored three times: `Fixture argument 'pg' does not have a fixture source (if it comes from a collection definition, ensure the definition is in the same assembly as the test)`. The `[CollectionDefinition("Auth")]` that Task 2 placed in `tests/GameKit.TestFixtures/CollectionDefinitions.cs` is not recognized cross-assembly by xUnit's analyzer, even though the runtime would pick it up at test discovery.
- **Fix:** Added `tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs` re-declaring `AuthCollection` locally. This matches the established Phase 1 pattern (`tests/GameKit.Core.Integration.Tests/CollectionDefinitions.cs` already re-declares `Postgres`, `Redis`, and `PostgresAndRedis` collections locally for the same reason).
- **Files modified:** `tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs` (new)
- **Commit:** `62d6644`

## Known Stubs

None — every file authored in this plan performs real work. The two "smoke" tests are intentional Wave-0 smoke tests proving the harness is green before Plan 02-02 lands production code; they are documented as such in the PLAN's objective and are part of the plan's exit contract (total 5 smoke tests pass across the two new assemblies).

## Authentication Gates

None triggered. Docker was available on the sequential executor host, so Testcontainers Postgres + Redis containers spun up for the `GameKit.Auth.Integration.Tests` smoke run without manual intervention.

## Wave 0 Readiness

Plans 02-02 through 02-08 have their test-project scaffolding available:

- **02-02** (entities + migration): adds new `*Tests.cs` + `*IntegrationTests.cs` files under the two new projects; uses `PostgresFixture` directly via `[Collection("Postgres")]`.
- **02-03** (options + egress handler): adds unit tests to `GameKit.Auth.Tests`; adds `EgressAllowListTests` to `GameKit.Auth.Integration.Tests` using `[Collection("Auth")]`.
- **02-04** (BCrypt hasher + JWT issuer + refresh service): unit tests land in `GameKit.Auth.Tests`; uses `Moq` + `Microsoft.EntityFrameworkCore.InMemory` already referenced.
- **02-05** (SteamOAuthProvider + DiscordOAuthProvider): Steam forgery test overrides the default stub via `WireMockSteamStubs.StubIsValidFalse(server)`; Discord `identify` stub already wired.
- **02-06** (Guest + Password + upgrade service): SERIALIZABLE race test uses `[Collection("Postgres")]`; identity-collision test uses `[Collection("Auth")]`.
- **02-07** (HTTP endpoints + WebApplicationFactory tests): `WebApplicationFactory<TProgram>` harness is unblocked — `Microsoft.AspNetCore.Mvc.Testing` is pinned + referenced; `AuthIntegrationFixture` composes Postgres + Redis + WireMock endpoints for the factory's `ConfigureServices` override.
- **02-08** (TicTacToeDuel Program.cs + HTML client + checkpoint): sample-app tests exercise the real endpoints; Wave-0 scaffolding delivers the smoke infrastructure these tests build on.

## Threat Flags

None. The files authored in this plan introduce no new network endpoints, auth paths, or trust boundaries beyond those in the plan's `<threat_model>`. WireMock stubs are strictly test-scope; they cannot be accidentally consumed in production because `WireMock.Net` is only referenced from `tests/**` csprojs, never from `src/**`.

## Self-Check: PASSED

**Files verified present on disk:**
- FOUND: Directory.Packages.props
- FOUND: tests/GameKit.TestFixtures/WireMockFixture.cs
- FOUND: tests/GameKit.TestFixtures/WireMockSteamStubs.cs
- FOUND: tests/GameKit.TestFixtures/WireMockDiscordStubs.cs
- FOUND: tests/GameKit.TestFixtures/AuthIntegrationFixture.cs
- FOUND: tests/GameKit.TestFixtures/CollectionDefinitions.cs
- FOUND: tests/GameKit.TestFixtures/GameKit.TestFixtures.csproj
- FOUND: tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj
- FOUND: tests/GameKit.Auth.Tests/SmokeTests.cs
- FOUND: tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj
- FOUND: tests/GameKit.Auth.Integration.Tests/WireMockReachabilitySmokeTests.cs
- FOUND: tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs
- FOUND: src/GameKit.Auth/AssemblyInfo.cs
- FOUND: GameKit.sln (contains GameKit.Auth.Tests + GameKit.Auth.Integration.Tests entries)
- FOUND: .planning/STATE.md (pre-flight flipped, Decisions Locked appended)

**Commits verified in git log:**
- FOUND: 1c0d8cc (Task 1 — Directory.Packages.props pins + STATE flip)
- FOUND: b11302d (Task 2 — WireMock fixture + stubs + AuthCollection)
- FOUND: 62d6644 (Task 3 — Auth test projects + AuthMarker + sln wiring)
