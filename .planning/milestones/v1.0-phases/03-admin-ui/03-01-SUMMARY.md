<!-- REUSE-IgnoreStart -->
---
phase: 03-admin-ui
plan: 01
subsystem: admin-ui
tags:
  - admin-ui
  - test-scaffolding
  - wave-0
dependencies:
  requires:
    - Phase 2 complete (GameKit.Auth shipped, AuthIntegrationFixture pattern proven)
    - Phase 1 TestFixtures (PostgresFixture + RedisFixture shipped)
    - src/GameKit.Admin.UI skeleton csproj (Phase 1)
  provides:
    - tests/GameKit.Admin.Tests                 # unit test project consumed by 03-03, 03-05, 03-07, 03-11
    - tests/GameKit.Admin.Integration.Tests     # integration test project consumed by 03-04, 03-07, 03-10, 03-13
    - AdminIntegrationFixture (Postgres + Redis composite; no WireMock)
    - AdminCollection xUnit collection (shared-fixture scope, declared in both TestFixtures and Admin.Integration.Tests assemblies)
    - WebApplicationFactoryExtensions.LoginAsAdminAsync / HarvestAntiforgeryTokenAsync (signatures locked for plans 03-04 / 03-07 / 03-13)
    - FakePlayerJwtIssuer (throwaway RSA; mints D-03 shaped player JWTs for SC#6)
    - Directory.Packages.props MudBlazor 9.3.0 CPM pin
  affects:
    - CLAUDE.md (new GameKit.Admin.UI per-package section + MudBlazor row in Core Technologies table)
    - GameKit.sln (two new csproj entries registered via dotnet sln add)
    - Directory.Packages.props
tech-stack:
  added:
    - "MudBlazor 9.3.0 (MIT; net10.0 GA 2026-04-18) — central package pin only; not yet referenced by any csproj (plan 03-03 will PackageReference it)"
  patterns:
    - "Shared AdminCollection re-declared per assembly (xUnit1041) — mirrors the Phase 2 AuthCollection pattern"
    - "AdminIntegrationFixture mirrors AuthIntegrationFixture minus WireMock (admin surface has zero outbound HTTP)"
    - "FakePlayerJwtIssuer: throwaway RSA per instance; IDisposable scrubs keypair on teardown — defends T-03-01-02"
key-files:
  created:
    - tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj
    - tests/GameKit.Admin.Tests/SmokeTests.cs
    - tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj
    - tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.Admin.Integration.Tests/WebApplicationFactoryExtensions.cs
    - tests/GameKit.Admin.Integration.Tests/Mocks/FakePlayerJwtIssuer.cs
    - tests/GameKit.TestFixtures/AdminIntegrationFixture.cs
  modified:
    - Directory.Packages.props
    - tests/GameKit.TestFixtures/CollectionDefinitions.cs
    - GameKit.sln
    - CLAUDE.md
decisions:
  - "MudBlazor 9.3.0 pinned in Directory.Packages.props as a CPM-only entry; no csproj PackageReferences it yet (plan 03-03 wires it into src/GameKit.Admin.UI)"
  - "Admin integration tests get Microsoft.EntityFrameworkCore.InMemory via the unit csproj (parity with GameKit.Auth.Tests) — not currently used by the smoke test but preserved so plans 03-03/03-05/03-07/03-11 can drop in service-layer unit tests without csproj churn"
  - "WireMock.Net deliberately NOT added to tests/GameKit.Admin.Integration.Tests.csproj — admin surface makes no outbound HTTP (health probe uses in-process Npgsql + StackExchange.Redis clients); confirmed by acceptance criterion grep returning 0"
  - "AdminCollection bundles PostgresFixture + RedisFixture + AdminIntegrationFixture; the composite fixture is the handle WebApplicationFactory bootstraps will use in plans 03-04/03-07/03-13"
  - "FakePlayerJwtIssuer issues D-03 claim-shaped tokens (sub/provider=\"guest\"/sid) with MapInboundClaims=false semantics — 15-minute default lifetime matches GameKitAuthOptions.Jwt.AccessTokenLifetime default"
  - "CLAUDE.md W5 + B1 step 4 blocks locked verbatim: Dependency direction (Admin.UI -> Auth ProjectReference) + Route/MountPath scope (API prefix only; Blazor routes + MudBlazor static assets root-relative)"
metrics:
  duration_minutes: 8
  tasks_completed: 3
  files_created: 7
  files_modified: 4
  tests_passing:
    unit_smoke: 1
    integration_smoke: 0
  completed_date: 2026-04-19
---

# Phase 03 Plan 01: Wave-0 Admin UI Test Scaffolding Summary

Laid the Wave-0 test foundation for Phase 3: two new xUnit csprojs (`GameKit.Admin.Tests` + `GameKit.Admin.Integration.Tests`), a Postgres + Redis composite `AdminIntegrationFixture` (no WireMock — admin surface has zero outbound HTTP), cookie-login + CSRF-harvest helpers with signatures later plans call verbatim, a throwaway-RSA `FakePlayerJwtIssuer` for SC#6 scheme-isolation testing, the MudBlazor 9.3.0 CPM pin, and a new `### GameKit.Admin.UI` per-package section in CLAUDE.md (plus the MudBlazor row in the Core Technologies table).

## Outcomes

### Task 1 — Admin test csprojs + MudBlazor CPM pin + sln registration (commit `02b1028`)

- Added `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` (unit; xUnit + Moq + EF InMemory; references `GameKit.Admin.UI`, `GameKit.Auth`, `GameKit.TestFixtures`).
- Added `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` (integration; `FrameworkReference Microsoft.AspNetCore.App` + Moq + Npgsql + StackExchange.Redis + `Microsoft.AspNetCore.Mvc.Testing`; **no** WireMock). Per the SP-17 PATTERNS shape.
- `SmokeTests.cs` with a single `TestProject_Loads` `[Fact]` asserting `true` — keeps the csproj green between waves.
- Registered both csprojs in `GameKit.sln` via `dotnet sln add` (Visual-Studio-format sln; ProjectGuid + ProjectConfigurationPlatforms block auto-populated by the CLI).
- Pinned MudBlazor 9.3.0 in `Directory.Packages.props`. The inserted block:

```xml
    <!-- Phase 3 Admin UI — MudBlazor 9.3.0 verified GA on net10.0 2026-04-18 (MIT / GPL-compatible)
         net10.0 TFM confirmed via nuget.org/packages/MudBlazor/9.3.0 (see 03-RESEARCH.md §Version verification). -->
    <PackageVersion Include="MudBlazor" Version="9.3.0" />
```

**Verification:** `dotnet build tests/GameKit.Admin.Tests` + `tests/GameKit.Admin.Integration.Tests` — 0 warnings, 0 errors. `dotnet test tests/GameKit.Admin.Tests` — 1 passed / 0 failed / 0 skipped.

### Task 2 — AdminIntegrationFixture + collection defs + WebApplicationFactoryExtensions + FakePlayerJwtIssuer (commit `878a372`)

- `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs` — composite over `PostgresFixture` + `RedisFixture`. No WireMock.
- Extended `tests/GameKit.TestFixtures/CollectionDefinitions.cs` with:

```csharp
[CollectionDefinition("Admin")]
public sealed class AdminCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture>,
      ICollectionFixture<AdminIntegrationFixture> { }
```

- `tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs` re-declares the same `AdminCollection` in the integration-test assembly (xUnit analyzer rule xUnit1041 requires the `[CollectionDefinition]` attribute to live in the assembly that consumes it — same pattern as the Phase 2 `AuthCollection` re-declaration).
- `tests/GameKit.Admin.Integration.Tests/WebApplicationFactoryExtensions.cs` exposes two helpers with signatures locked for later plans:
  - `LoginAsAdminAsync(HttpClient client, string username, string password) -> Task<HttpClient>` — POSTs `/admin/api/login`, lets the client's `CookieContainer` capture `gk_admin_session`, throws `InvalidOperationException` on non-200.
  - `HarvestAntiforgeryTokenAsync(HttpClient client) -> Task<string>` — GETs `/admin/login`, regex-matches `__RequestVerificationToken`, returns the token value for the `X-GameKit-Admin-CSRF` header.
- `tests/GameKit.Admin.Integration.Tests/Mocks/FakePlayerJwtIssuer.cs` — throwaway-RSA minter for SC#6. `IssueValidPlayerJwt(Guid playerId, Guid sessionId, TimeSpan? lifetime = null)` emits D-03 claim shape (`sub`, `provider="guest"`, `sid`) under a fresh 2048-bit RSA keypair. `IDisposable` scrubs the keypair. `PublicSigningKey` lets the test harness register the matching `IssuerSigningKey` on `TokenValidationParameters`.

**Verification:** `dotnet build tests/GameKit.TestFixtures` + `tests/GameKit.Admin.Integration.Tests` — 0 warnings, 0 errors. Every new `.cs` file starts with `// SPDX-License-Identifier: GPL-3.0-or-later`.

### Task 3 — CLAUDE.md per-package section + MudBlazor stack row + full solution build (commit `2889eb3`)

- Replaced the empty `### GameKit.Admin.UI` stub under `## Per-Package NuGet Dependencies` with a 5-row table (MudBlazor 9.3.0 / StackExchange.Redis 2.8.41 / FluentValidation 12.1.1 / Antiforgery shared-framework / Cookies shared-framework) + "Out of scope" note + Dependency-direction note (W5) + `MountPath` scope note (B1 step 4).
- Added one row to `### Core Technologies (pinned — reference only)` between the StackExchange.Redis and JwtBearer rows:

```
| MudBlazor | **9.3.0** | Blazor Server component library (Admin UI) | MIT; `net10.0` TFM GA on nuget.org 2026-04-18 |
```

- Ran `dotnet build GameKit.sln -c Debug --nologo` — 0 warnings, 0 errors across all 17 projects.

## Files Created / Modified (authoritative list)

### Created (7)
- `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj`
- `tests/GameKit.Admin.Tests/SmokeTests.cs`
- `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj`
- `tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs`
- `tests/GameKit.Admin.Integration.Tests/WebApplicationFactoryExtensions.cs`
- `tests/GameKit.Admin.Integration.Tests/Mocks/FakePlayerJwtIssuer.cs`
- `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs`

### Modified (4)
- `Directory.Packages.props` — appended MudBlazor 9.3.0 CPM pin block (one `<PackageVersion>` + 2-line comment).
- `tests/GameKit.TestFixtures/CollectionDefinitions.cs` — appended `AdminCollection` `[CollectionDefinition]` (Postgres + Redis + AdminIntegrationFixture).
- `GameKit.sln` — appended two `Project(...)` entries (Admin.Tests + Admin.Integration.Tests) via `dotnet sln add`; corresponding `ProjectConfigurationPlatforms` / `NestedProjects` blocks auto-populated.
- `CLAUDE.md` — 15 new lines under `### GameKit.Admin.UI` (per-package dep table + Out-of-scope + Dependency direction + MountPath scope); 1 new line in Core Technologies table (MudBlazor row).

## Baseline Test Counts

| Project | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| GameKit.Admin.Tests (unit) | 1 | 0 | 0 |
| GameKit.Admin.Integration.Tests | 0 | 0 | 0 (no tests yet — integration tests land in plan 03-03+) |

Full solution (`dotnet build GameKit.sln`) builds green: 17 projects / 0 warnings / 0 errors.

## CLAUDE.md Edits (context)

- Per-package section inserted between `### GameKit.Presence` and `### tests/*` stubs at roughly line 86 (original location of the `### GameKit.Admin.UI` stub heading).
- MudBlazor row inserted between the StackExchange.Redis row and the `Microsoft.AspNetCore.Authentication.JwtBearer` row in the "Core Technologies" table at roughly line 41.

## Deviations from Plan

None — plan executed exactly as written. All acceptance criteria met without modification:
- Both new csprojs build clean with `<Project Sdk="Microsoft.NET.Sdk">`.
- `dotnet test tests/GameKit.Admin.Tests/` reports `Passed: 1, Failed: 0, Skipped: 0`.
- `grep -c 'MudBlazor" Version="9.3.0"' Directory.Packages.props` returns 1.
- `grep -c 'WireMock.Net' tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` returns 0.
- `grep 'GameKit.Admin.Tests' GameKit.sln` matches.
- `grep 'GameKit.Admin.Integration.Tests' GameKit.sln` matches.
- CLAUDE.md contains the MudBlazor per-package row, the Core Technologies MudBlazor row, the `.planning/phases/03-admin-ui/03-RESEARCH.md §Version verification` cross-reference, the W5 Dependency-direction note, and the B1-step-4 MountPath-scope note.
- Full solution builds green.

## Known Stubs

None. The `SmokeTests.TestProject_Loads` `[Fact]` asserts `true` as a deliberate placeholder that keeps the csproj green until plans 03-03/03-05/03-07/03-11 append real assertions (this is documented as Task 1 Step 4 NOTE in the plan itself — not a stub hiding missing functionality).

## Self-Check: PASSED

Verification run after writing SUMMARY.md:

- File existence checks (7 created files):
  - `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` — FOUND
  - `tests/GameKit.Admin.Tests/SmokeTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/WebApplicationFactoryExtensions.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/Mocks/FakePlayerJwtIssuer.cs` — FOUND
  - `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs` — FOUND
- Commit existence checks:
  - `02b1028` — FOUND (task 1)
  - `878a372` — FOUND (task 2)
  - `2889eb3` — FOUND (task 3)
<!-- REUSE-IgnoreEnd -->
