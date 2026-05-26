---
phase: 06-presence-openapi-distribution
plan: 03
subsystem: tests
tags: [tests, scaffolding, wave-0]
requires:
  - tests/GameKit.TestFixtures/PostgresFixture.cs
  - tests/GameKit.TestFixtures/RedisFixture.cs
  - tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs (template)
  - src/GameKit.Presence/GameKit.Presence.csproj (Phase 1 stub)
provides:
  - tests/GameKit.Presence.Tests/ (unit test host for Plan 06-04)
  - tests/GameKit.Presence.Integration.Tests/ (heartbeat / in-match IT host for Plans 06-04, 06-05)
  - tests/GameKit.OpenApi.Integration.Tests/ (contract test host for Plan 06-06)
  - tests/GameKit.Distribution.Integration.Tests/ (DIST-02/03 + OPS-04/06 host for Plans 06-08, 06-09)
  - src/GameKit.OpenApi/ minimal skeleton (Rule 3 blocker fix; Plan 06-01 will overlay Analyzer ref + richer AssemblyInfo)
  - DistributionIntegrationFixture exposing PostgresFixture.ReaderConnectionString verbatim for DIST-02
affects:
  - GameKit.sln (added 5 new project entries: 4 test csprojs + GameKit.OpenApi skeleton)
  - src/GameKit.Presence/AssemblyInfo.cs (added InternalsVisibleTo grants)
tech-stack:
  added: []  # no new CPM pins — all packages (xUnit 2.9.2, Moq 4.20.72, Testcontainers 4.11.0, Mvc.Testing 10.0.0) already pinned via Directory.Packages.props
  patterns:
    - Per-package xUnit collection definitions (PATTERNS Shared Pattern F / xUnit1041)
    - Composite IAsyncLifetime fixture over shared collection fixtures (Phase 5 MatchmakingIntegrationFixture precedent)
    - SmokeTests Assembly.Load sentinel for Wave-0 build wiring (Phase 5 GameKit.Matchmaking.Tests precedent)
key-files:
  created:
    - tests/GameKit.Presence.Tests/GameKit.Presence.Tests.csproj
    - tests/GameKit.Presence.Tests/SmokeTests.cs
    - tests/GameKit.Presence.Integration.Tests/GameKit.Presence.Integration.Tests.csproj
    - tests/GameKit.Presence.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.Presence.Integration.Tests/Fixtures/PresenceIntegrationFixture.cs
    - tests/GameKit.Presence.Integration.Tests/SmokeTests.cs
    - tests/GameKit.OpenApi.Integration.Tests/GameKit.OpenApi.Integration.Tests.csproj
    - tests/GameKit.OpenApi.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.OpenApi.Integration.Tests/SmokeTests.cs
    - tests/GameKit.Distribution.Integration.Tests/GameKit.Distribution.Integration.Tests.csproj
    - tests/GameKit.Distribution.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.Distribution.Integration.Tests/Fixtures/DistributionIntegrationFixture.cs
    - tests/GameKit.Distribution.Integration.Tests/SmokeTests.cs
    - src/GameKit.OpenApi/GameKit.OpenApi.csproj  # Rule 3 blocker fix; Plan 06-01 owns the final version
    - src/GameKit.OpenApi/AssemblyInfo.cs         # Rule 3 blocker fix; Plan 06-01 owns the final version
  modified:
    - GameKit.sln
    - src/GameKit.Presence/AssemblyInfo.cs
decisions:
  - Drop Rankings / Matchmaking / Admin.UI ProjectRefs from Presence.Integration.Tests (PATTERNS Block 12 line 925) — Presence does not depend on them at runtime
  - DistributionIntegrationFixture composes PostgresFixture + RedisFixture as a thin pass-through, exposing ReaderConnectionString verbatim — PATTERNS warning #11 (no custom Testcontainer; the 3-role bind-mount is already wired in PostgresFixture lines 36-53)
  - Distribution SmokeTests asserts ALL 7 GameKit src assemblies load (Wave-0 sentinel for missing ProjectRefs — catches a dropped ref before OPS-04 / DIST-02 / OPS-06 reach for the missing assembly)
  - Created minimal src/GameKit.OpenApi/ skeleton as Rule 3 blocker fix (Plan 06-01 also Wave 0 — orchestrator merge resolves the overlap)
metrics:
  duration: 11m
  tasks: 3
  files_created: 15
  files_modified: 2
  commits: 3
  completed: 2026-05-26
---

# Phase 6 Plan 03: Wave-0 Test Project Scaffolding Summary

Stood up the four new test csproj hosts (`GameKit.Presence.Tests`, `GameKit.Presence.Integration.Tests`, `GameKit.OpenApi.Integration.Tests`, `GameKit.Distribution.Integration.Tests`) so Waves 1-4 can author test classes without touching csproj or fixture wiring. All four projects build under `TreatWarningsAsErrors=true` and each ships a passing `SmokeTests.TestProject_Loads` sentinel.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Scaffold GameKit.Presence.Tests unit-test csproj + SmokeTests + InternalsVisibleTo grants | `983ea3b` | 4 (csproj, SmokeTests, AssemblyInfo, sln) |
| 2 | Scaffold GameKit.Presence.Integration.Tests csproj + CollectionDefinitions + Fixture composite + SmokeTests | `e3abffb` | 5 (csproj, CollectionDefinitions, PresenceIntegrationFixture, SmokeTests, sln) |
| 3 | Scaffold GameKit.OpenApi + Distribution Integration.Tests csprojs + src/GameKit.OpenApi skeleton | `3236dd7` | 10 (4 OpenApi-test files, 4 Distribution-test files, 2 src/GameKit.OpenApi files, sln) |

## What Was Built

### 1. `tests/GameKit.Presence.Tests/` (unit-test host)

Mirrors `tests/GameKit.Matchmaking.Tests/` but deliberately omits `FrameworkReference Microsoft.AspNetCore.App` per Plan 06-03 Task 1 behavior ("no Testcontainers, no FrameworkReference Microsoft.AspNetCore.App — Plan 06-04 will use Moq for IConnectionMultiplexer if needed"). The csproj's `<ItemGroup>` ProjectReference block (so Wave 1-4 plans know what is pre-wired):

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\GameKit.Presence\GameKit.Presence.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
</ItemGroup>
```

`SmokeTests.TestProject_Loads()` asserts `Assembly.Load("GameKit.Presence")` returns non-null and the assembly name matches.

### 2. `tests/GameKit.Presence.Integration.Tests/` (heartbeat / in-match IT host)

`<ItemGroup>` ProjectReference block:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\GameKit.Presence\GameKit.Presence.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
  <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
</ItemGroup>
```

`CollectionDefinitions.cs` declares three local collections (xUnit1041 enforcement): `Presence` (Postgres+Redis composite), `Postgres` (Postgres only), `Redis` (Redis only). `Fixtures/PresenceIntegrationFixture.cs` is an internal sealed composite over `PostgresFixture` + `RedisFixture` with a `NotImplementedException` `BuildServiceProvider(suffix)` placeholder (filled in by Plan 06-04).

### 3. `tests/GameKit.OpenApi.Integration.Tests/` (OPEN-01 contract test host)

`<ItemGroup>` ProjectReference block:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Rankings\GameKit.Rankings.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Presence\GameKit.Presence.csproj" />
  <ProjectReference Include="..\..\src\GameKit.OpenApi\GameKit.OpenApi.csproj" />
  <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  <ProjectReference Include="..\..\samples\TicTacToeDuel\TicTacToeDuel.csproj" />
</ItemGroup>
```

CollectionDefinitions declares `OpenApi` (Postgres+Redis composite), `Postgres`, `Redis`. SmokeTests loads `GameKit.OpenApi` + `GameKit.Core` assemblies.

### 4. `tests/GameKit.Distribution.Integration.Tests/` (DIST-02/03 + OPS-04/06 host)

`<ItemGroup>` ProjectReference block (the OPS-04 test reflects on each marker type, so all 7 GameKit src refs are mandatory):

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Rankings\GameKit.Rankings.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />
  <ProjectReference Include="..\..\src\GameKit.Presence\GameKit.Presence.csproj" />
  <ProjectReference Include="..\..\src\GameKit.OpenApi\GameKit.OpenApi.csproj" />
  <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  <ProjectReference Include="..\..\samples\TicTacToeDuel\TicTacToeDuel.csproj" />
</ItemGroup>
```

`Fixtures/DistributionIntegrationFixture.cs` is the **PATTERNS warning #11** payoff: a thin composite over the existing `PostgresFixture` + `RedisFixture` that re-exposes `PostgresFixture.ReaderConnectionString` verbatim for the DIST-02 (`gamekit_reader` INSERT-denied) test. The fixture does NOT construct a new Testcontainer — the 3-role bootstrap is already wired into `PostgresFixture.cs:36-53` via `WithBindMount(initDir, "/docker-entrypoint-initdb.d")`.

The `DistributionIntegrationFixture` confirmation that ReaderConnectionString is exposed verbatim:

```csharp
public string ReaderConnectionString => _pg.ReaderConnectionString;
```

`SmokeTests.TestProject_Loads_AllSevenGameKitPackages()` iterates the 7 GameKit src assembly names and asserts each loads — a Wave-0 sentinel that catches a dropped ProjectReference before OPS-04 / DIST-02 / OPS-06 trip on a missing assembly.

## PATTERNS Warning #11 Honored

The Plan's `<verification>` line "DistributionIntegrationFixture composes the existing PostgresFixture + RedisFixture without modifying either (PATTERNS warning #11)" is satisfied:

- No new container plumbing was added.
- `tests/GameKit.TestFixtures/PostgresFixture.cs` was NOT modified.
- `tests/GameKit.TestFixtures/RedisFixture.cs` was NOT modified.
- `DistributionIntegrationFixture` constructor takes both fixtures by injection and exposes `ReaderConnectionString` as a verbatim pass-through getter.

`git diff master -- tests/GameKit.TestFixtures/` confirms zero changes to the shared fixtures package.

## sln Registration

`dotnet sln list | grep -E "(Presence|OpenApi|Distribution)"` output:

```
src/GameKit.OpenApi/GameKit.OpenApi.csproj
tests/GameKit.Distribution.Integration.Tests/GameKit.Distribution.Integration.Tests.csproj
tests/GameKit.OpenApi.Integration.Tests/GameKit.OpenApi.Integration.Tests.csproj
tests/GameKit.Presence.Integration.Tests/GameKit.Presence.Integration.Tests.csproj
tests/GameKit.Presence.Tests/GameKit.Presence.Tests.csproj
```

All 4 new test projects + the `src/GameKit.OpenApi` skeleton are registered in `GameKit.sln`. The previously-registered `src/GameKit.Presence/GameKit.Presence.csproj` was unchanged structurally; only its `AssemblyInfo.cs` was updated to add `InternalsVisibleTo` grants.

## Build + Test Verification

All four new csprojs build under `TreatWarningsAsErrors=true` (no `<NoWarn>` exception beyond the standard `CS1591` test-project carve-out from `Directory.Build.props` precedent + the `<WarningsAsErrors />` empty-override that matches the existing test projects):

| Project | Build | SmokeTest |
|---------|-------|-----------|
| tests/GameKit.Presence.Tests | succeeded 0/0/0 | Passed: 1, Failed: 0 |
| tests/GameKit.Presence.Integration.Tests | succeeded 0/0/0 | Passed: 1, Failed: 0 |
| tests/GameKit.OpenApi.Integration.Tests | succeeded 0/0/0 | Passed: 1, Failed: 0 |
| tests/GameKit.Distribution.Integration.Tests | succeeded 0/0/0 | Passed: 1, Failed: 0 |

## InternalsVisibleTo Grants Added

`src/GameKit.Presence/AssemblyInfo.cs` (mirrors the GameKit.Matchmaking Phase 5 grant pattern):

```csharp
[assembly: InternalsVisibleTo("GameKit.Presence.Tests")]
[assembly: InternalsVisibleTo("GameKit.Presence.Integration.Tests")]
```

`src/GameKit.OpenApi/AssemblyInfo.cs` (created in the Rule 3 blocker fix; Plan 06-01 owns the canonical version):

```csharp
[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocker] Created minimal `src/GameKit.OpenApi/` skeleton (csproj + AssemblyInfo)**

- **Found during:** Task 3 — `tests/GameKit.OpenApi.Integration.Tests` and `tests/GameKit.Distribution.Integration.Tests` both require `<ProjectReference Include="..\..\src\GameKit.OpenApi\GameKit.OpenApi.csproj" />`. The Plan 06-03 verify command explicitly greps for this ref in the Distribution csproj. The src project does not yet exist in this worktree.
- **Issue:** Without the src/GameKit.OpenApi/ skeleton, both new test csprojs fail to restore with NU1101 "Unable to find package GameKit.OpenApi". Plan 06-03 is in Wave 0 alongside Plan 06-01 (which is the canonical creator of `src/GameKit.OpenApi/`); execution order between sibling Wave 0 plans is not guaranteed by the orchestrator.
- **Fix:** Created a minimal `src/GameKit.OpenApi/GameKit.OpenApi.csproj` mirroring `src/GameKit.Presence/GameKit.Presence.csproj` shape (PackageId + Description + PackageTags + ProjectRef to Core + FrameworkReference Microsoft.AspNetCore.App) plus `src/GameKit.OpenApi/AssemblyInfo.cs` with SPDX GPL header and an `InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")` grant. Plan 06-01 Task 4 ships the same shape PLUS the `<ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` Analyzer wire — the orchestrator's wave-merge resolves the overlap by taking Plan 06-01's richer version.
- **Files modified:** `src/GameKit.OpenApi/GameKit.OpenApi.csproj` (created), `src/GameKit.OpenApi/AssemblyInfo.cs` (created)
- **Commit:** `3236dd7`

**2. [Rule 1 - Bug] Removed manual `[assembly: AssemblyDescription(...)]` from src/GameKit.OpenApi/AssemblyInfo.cs**

- **Found during:** Task 3 first build attempt
- **Issue:** `CS0579: Duplicate 'System.Reflection.AssemblyDescriptionAttribute' attribute` — the SDK auto-emits `[assembly: AssemblyDescription]` from the csproj `<Description>` element and conflicts with my hand-written attribute.
- **Fix:** Removed the manual `[assembly: AssemblyDescription(...)]` line and `using System.Reflection` (no longer needed). Left a comment noting why. Verified plan 06-01's own AssemblyInfo content does NOT include the duplicate either (Plan 06-01 Task 4 action only specifies `AssemblyDescription` as an item to add, but MSBuild's auto-emission of the same attribute from `<Description>` would force the same fix in Plan 06-01).
- **Files modified:** `src/GameKit.OpenApi/AssemblyInfo.cs`
- **Commit:** `3236dd7`

### Non-deviations (clarifications)

- **`samples/TicTacToeDuel` Program class visibility:** Plan task 3 references that `WebApplicationFactory<Program>` is auto-public via top-level statements. Wave 0 SmokeTests do NOT exercise WebApplicationFactory — they only load assemblies. Plan 06-06 will own any needed `public partial class Program {}` shim work when the OpenApi contract test starts running against the sample.

- **`templates/GameKit.Templates/`:** Plan 06-09's DIST-04 test exercises a `templates/` directory that does not yet exist. None of this plan's csprojs reference `templates/`; Plan 06-07 creates that directory.

## Phase 6 Wave 1+ Handoff

Plans 06-04, 06-05, 06-06, 06-08, 06-09 can now drop test classes into the new projects without touching any csproj or fixture wiring:

| Plan | Drop test classes into | Use fixture |
|------|------------------------|-------------|
| 06-04 RedisPresenceProvider unit tests | `tests/GameKit.Presence.Tests/` | Moq<IConnectionMultiplexer> |
| 06-04 heartbeat TTL IT | `tests/GameKit.Presence.Integration.Tests/` | `PresenceIntegrationFixture` |
| 06-05 in-match precedence IT | `tests/GameKit.Presence.Integration.Tests/` | `PresenceIntegrationFixture` |
| 06-06 OpenApi contract test | `tests/GameKit.OpenApi.Integration.Tests/` | `OpenApiCollection` + `WebApplicationFactory<Program>` |
| 06-08 DIST-02 reader-INSERT denied | `tests/GameKit.Distribution.Integration.Tests/` | `DistributionIntegrationFixture.ReaderConnectionString` |
| 06-08 OPS-04 version stamping | `tests/GameKit.Distribution.Integration.Tests/` | `DistributionCollection` (reflection over `Internal.GameKitMarker`) |
| 06-08 OPS-06 clean-install migration | `tests/GameKit.Distribution.Integration.Tests/` | `DistributionIntegrationFixture.OwnerConnectionString` |
| 06-09 DIST-03 SampleGame smoke | `tests/GameKit.Distribution.Integration.Tests/` | `DistributionIntegrationFixture` + `WebApplicationFactory<Program>` |
| 06-09 DIST-04 template package shape | `tests/GameKit.Distribution.Integration.Tests/` | none (file-I/O over `dotnet pack` output) |

## Self-Check: PASSED

Verified:

- `tests/GameKit.Presence.Tests/GameKit.Presence.Tests.csproj` — FOUND
- `tests/GameKit.Presence.Tests/SmokeTests.cs` — FOUND
- `tests/GameKit.Presence.Integration.Tests/GameKit.Presence.Integration.Tests.csproj` — FOUND
- `tests/GameKit.Presence.Integration.Tests/CollectionDefinitions.cs` — FOUND
- `tests/GameKit.Presence.Integration.Tests/Fixtures/PresenceIntegrationFixture.cs` — FOUND
- `tests/GameKit.Presence.Integration.Tests/SmokeTests.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/GameKit.OpenApi.Integration.Tests.csproj` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/CollectionDefinitions.cs` — FOUND
- `tests/GameKit.OpenApi.Integration.Tests/SmokeTests.cs` — FOUND
- `tests/GameKit.Distribution.Integration.Tests/GameKit.Distribution.Integration.Tests.csproj` — FOUND
- `tests/GameKit.Distribution.Integration.Tests/CollectionDefinitions.cs` — FOUND
- `tests/GameKit.Distribution.Integration.Tests/Fixtures/DistributionIntegrationFixture.cs` — FOUND
- `tests/GameKit.Distribution.Integration.Tests/SmokeTests.cs` — FOUND
- `src/GameKit.OpenApi/GameKit.OpenApi.csproj` — FOUND
- `src/GameKit.OpenApi/AssemblyInfo.cs` — FOUND
- Commit `983ea3b` — FOUND
- Commit `e3abffb` — FOUND
- Commit `3236dd7` — FOUND
