---
phase: 05
plan: 01
subsystem: matchmaking
tags: [matchmaking, scaffolding, test-infrastructure, wave-0, advisory-lock]
dependency_graph:
  requires:
    - phase-01-core
    - phase-02-auth
    - phase-03-admin-ui
    - phase-04-rankings
  provides:
    - tests/GameKit.Matchmaking.Tests (unit)
    - tests/GameKit.Matchmaking.Integration.Tests (Testcontainers PG + Redis)
    - tests/GameKit.Matchmaking.LoadTests (SC#3 phase-gate harness)
    - MatchmakingCollection xUnit composite
    - MatchmakingTestModelCustomizer (Pitfall §3 bypass)
    - MatchmakingIntegrationFixture (per-class fixture w/ BuildServiceProvider factory contract)
    - LoadTestFixture (Maximum Pool Size=25 — Pitfall §8 mitigation)
    - StepClock (verbatim port of Phase 4 Glicko2ConvergenceTests:420)
    - MatchmakingAdvisoryLockKeyTests (Wave-0 mandatory advisory-lock gate)
  affects:
    - GameKit.sln (3 new test project entries under `tests` solution folder)
    - src/GameKit.Rankings/AssemblyInfo.cs (InternalsVisibleTo grant)
    - tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj
      (Auth + Admin.UI ProjectReferences added for advisory-key distinctness test)
tech_stack:
  added: []  # zero new NuGet pins — every dep already in Directory.Packages.props (RESEARCH §Decision 17 verified)
  patterns:
    - xUnit CollectionDefinition composing PostgresFixture + RedisFixture (precedent: RankingsCollection, AuthCollection, AdminCollection)
    - RelationalModelCustomizer subclass bypassing EF global model cache (precedent: TickerTestModelCustomizer, RankingsCliModelCustomizer)
    - Live-Postgres advisory-key verification (precedent: RankingsAdvisoryLockKeyTests, AdminAdvisoryLockKeyTests, AuthAdvisoryLockKeyTests, GameKitMigrationAdvisoryLockTests)
    - StepClock IClock for deterministic time advancement in integration tests
    - Maximum Pool Size=25 Npgsql cap for load-test isolation
key_files:
  created:
    - tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj
    - tests/GameKit.Matchmaking.Tests/SmokeTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj
    - tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestModelCustomizer.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/Fixtures/MatchmakingIntegrationFixture.cs
    - tests/GameKit.Matchmaking.Integration.Tests/Fixtures/StepClock.cs
    - tests/GameKit.Matchmaking.LoadTests/GameKit.Matchmaking.LoadTests.csproj
    - tests/GameKit.Matchmaking.LoadTests/Fixtures/LoadTestFixture.cs
  modified:
    - GameKit.sln (3 new test project rows; sibling tests solution folder)
    - src/GameKit.Rankings/AssemblyInfo.cs (added InternalsVisibleTo for GameKit.Matchmaking.Integration.Tests)
decisions:
  - "Three csprojs all use Central Package Management (CPM): every `<PackageReference>` is versionless and resolved from `Directory.Packages.props`. Zero new pins added — every dep already present from Phases 1-4 (xunit 2.9.2, Moq 4.20.72, Testcontainers* 4.11.0, Npgsql 10.0.2, StackExchange.Redis 2.8.41, EFCore.InMemory 10.0.6, Microsoft.AspNetCore.Mvc.Testing 10.0.0)."
  - "LoadTests csproj has `<IsPackable>false</IsPackable>` (matches the global `tests/Directory.Build.props` default) and ships the same Postgres/Redis Testcontainers deps as Integration.Tests. Test methods will decorate with `[Fact(Timeout=15*60*1000)]` (15 min) in Plan 05-10 so opt-in `dotnet test tests/GameKit.Matchmaking.LoadTests` is the canonical execution path; rapid CI loops on `dotnet test` against the full solution still build the project but the long timeout prevents accidental run-time on every PR."
  - "MatchmakingTestModelCustomizer applies BOTH MatchmakingModelBuilderExtension (lands in 05-02) AND RankingsModelBuilderExtension — Matchmaking has a ProjectReference to Rankings because EloRangeMatchmakingStrategy reads `player_ranks` directly, so the test EF model needs both packages' configurations active in one DbContext."
  - "StepClock.cs is a verbatim port of `tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs:420-432` (same class body; only the namespace changes). Property + ctor + Advance(TimeSpan) signatures identical."
  - "MatchmakingAdvisoryLockKeyTests' distinct-check uses BOTH symbolic constants AND duplicated integer literals (1800940027 / -298890956 / -2101739634 / -156812172). Defense-in-depth: a future rename of a sibling-package constant cannot mask an accidental collision because the literal value comparison still fires."
  - "Wave 0 gate convention: `MatchmakingMigrationConstants.AdvisoryLockKey` (lands in 05-02) starts as placeholder `0L`. Test A (PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation) is RED at Wave 0; Plan 05-02's verification is exactly 'replace 0L with the live `SELECT hashtext('gamekit.matchmaking.migrations')::bigint` value, then flip the test green'. This mirrors the same placeholder-then-live-verify pattern used by Plans 02-02 (Auth: -298890956), 03-02 (Admin: -2101739634), and 04-02 (Rankings: -156812172)."
metrics:
  duration_min: 5
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 11
---

# Phase 5 Plan 01: Wave-0 Matchmaking Test Scaffolding Summary

One-liner: Three Matchmaking test projects (Unit / Integration / LoadTests), shared fixture composite + EF model-cache bypass + StepClock helper, and the live-Postgres advisory-lock-key verification test — every downstream Phase 5 plan now has a Wave-0 home for its automated verification artifacts.

## What This Plan Delivers

1. **Three test projects** registered in `GameKit.sln` under the `tests` solution folder:
   - `tests/GameKit.Matchmaking.Tests` — xUnit + Moq + EF Core InMemory; unit-level tests (bracket-flex math, cooldown escalation, party-code generation, channel-drop semantics) land in 05-03 / 05-04.
   - `tests/GameKit.Matchmaking.Integration.Tests` — Testcontainers Postgres 17.9 + Redis 8.6.2 + `Microsoft.AspNetCore.Mvc.Testing`; integration-level scenarios (happy path, chaos, reconciler, leader election, rate limit, observability) land in 05-05 through 05-09.
   - `tests/GameKit.Matchmaking.LoadTests` — Phase 5 SC#3 gate harness (1k concurrent tickets sustained 10 min); Plan 05-10 fills the body. Same deps as Integration.Tests, plus a `LoadTestFixture` that pins `Maximum Pool Size=25` on the Npgsql connection string (RESEARCH §Decision 13 + Pitfall §8 mitigation).

2. **Shared scaffolding**:
   - `CollectionDefinitions.cs`: `[CollectionDefinition("Matchmaking")] : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture>` (composes the two shared TestFixtures) + a `[CollectionDefinition("Postgres")]` re-declaration for xUnit1041's same-assembly rule.
   - `MatchmakingTestModelCustomizer.cs`: `RelationalModelCustomizer` subclass that calls `base.Customize(...)`, then applies `MatchmakingModelBuilderExtension` (Plan 05-02) and `RankingsModelBuilderExtension`. Bypasses EF's global model cache (PITFALLS §3) so cross-package tests see both schemas in one DbContext.
   - `Fixtures/MatchmakingIntegrationFixture.cs`: per-test-class fixture exposing `ConnectionString`/`RedisConnectionString` and a `BuildServiceProvider(string instanceSuffix)` factory contract for the 05-05 leader-election test (two providers race for the same Redis lock).
   - `Fixtures/StepClock.cs`: 1:1 port of `Glicko2ConvergenceTests.cs:420`.
   - `LoadTests/Fixtures/LoadTestFixture.cs`: same shape as `MatchmakingIntegrationFixture` but appends `Maximum Pool Size=25` via `NpgsqlConnectionStringBuilder.MaxPoolSize = 25`.

3. **Wave-0 mandatory advisory-lock test** (`MatchmakingAdvisoryLockKeyTests.cs`):
   - Test A `PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` — opens an `NpgsqlConnection` against `PostgresFixture.OwnerConnectionString`, runs `SELECT hashtext('gamekit.matchmaking.migrations')::bigint`, asserts equal to `MatchmakingMigrationConstants.AdvisoryLockKey`. **Wave 0 expected state: RED** (placeholder `0L` from Plan 05-02 does not match the live hashtext output).
   - Test B `MatchmakingKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Keys` — pairwise non-equality with all four prior-package advisory keys (`1800940027` / `-298890956` / `-2101739634` / `-156812172`) asserted both via symbolic constants AND duplicated integer literals (defense-in-depth). **Wave 0 expected state: GREEN** (0 distinct from all four).

## Wave-0 → Plan 05-02 Gate (Expected State)

The Integration.Tests project intentionally **does not build** at the end of Wave 0. The single compile error is the deterministic gate Plan 05-02 closes:

```
error CS0234: The type or namespace name 'Data' does not exist in the namespace 'GameKit.Matchmaking'
  (referenced by MatchmakingAdvisoryLockKeyTests.cs line 8 + MatchmakingTestModelCustomizer.cs line 42)
```

This is the explicit Wave-0 design from the plan body. Plan 05-02 will:

1. Create `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` with `MigrationsHistoryTable = "__ef_migrations_matchmaking"` and `AdvisoryLockKey = 0L` placeholder.
2. Create `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs`.
3. Run the Integration.Tests build → both files now exist → CS0234 resolves → Test A runs against live Postgres → RED → Plan 05-02 reads the computed `hashtext` value from the test output → replaces `0L` with that value → Test A flips GREEN.

This mirrors the placeholder-then-live-verify pattern used in Plans 02-02 / 03-02 / 04-02.

The **Unit Tests** project (`tests/GameKit.Matchmaking.Tests`) builds green at the end of Wave 0 (placeholder `SmokeTests.TestProject_Loads` exercises a real test method). The **LoadTests** project also builds green at the end of Wave 0 (no reference to the not-yet-existing Matchmaking data layer; `LoadTestFixture` body is a `NotImplementedException` placeholder Plan 05-10 fills in).

## Build State Snapshot (post-plan)

| Project | dotnet build status | Why |
|---------|--------------------|-----|
| `tests/GameKit.Matchmaking.Tests` | GREEN (0/0) | References only `GameKit.Matchmaking` + `GameKit.Core` + CPM-pinned NuGets; smoke test compiles cleanly. |
| `tests/GameKit.Matchmaking.Integration.Tests` | RED (1 error) | `MatchmakingTestModelCustomizer.cs` + `MatchmakingAdvisoryLockKeyTests.cs` reference `GameKit.Matchmaking.Data.{MatchmakingModelBuilderExtension, MatchmakingMigrationConstants}` — both ship in Plan 05-02. **Expected gate; documented in plan body.** |
| `tests/GameKit.Matchmaking.LoadTests` | GREEN (0/0) | No reference to the not-yet-existing Matchmaking data layer; `LoadTestFixture` is self-contained. |
| `src/GameKit.Rankings` + all sibling Rankings tests | GREEN (regression-checked) | The `InternalsVisibleTo` grant added to `AssemblyInfo.cs` does not change the public API; only adds a fifth assembly to the existing IVT list. |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Auto-fix blocking issue] Added `InternalsVisibleTo` grant to `GameKit.Rankings`**
- **Found during:** Task 2 build verification.
- **Issue:** `MatchmakingTestModelCustomizer.cs` calls `new RankingsModelBuilderExtension().ApplyTo(modelBuilder)` — but `RankingsModelBuilderExtension` is declared `internal sealed` and its existing IVT grants cover only `GameKit.Rankings.Tests`, `GameKit.Rankings.Integration.Tests`, `GameKit.Cli.Tests`, and `gamekit`. The plan body explicitly requires the new test customizer to call this exact API.
- **Fix:** Added `[assembly: InternalsVisibleTo("GameKit.Matchmaking.Integration.Tests")]` to `src/GameKit.Rankings/AssemblyInfo.cs`. Mirrors the `GameKit.Auth → GameKit.Admin.Integration.Tests` grant established in Plan 03-06.
- **Files modified:** `src/GameKit.Rankings/AssemblyInfo.cs`
- **Commit:** `cabf94f`

**2. [Rule 3 — Auto-fix blocking issue] Added Auth + Admin.UI ProjectReferences to Integration.Tests csproj**
- **Found during:** Task 3 build verification.
- **Issue:** `MatchmakingAdvisoryLockKeyTests.cs` imports `GameKit.Auth.Data.AuthMigrationConstants` and `GameKit.Admin.UI.Data.AdminMigrationConstants` to assert pairwise distinctness — but the initial csproj had only `GameKit.Matchmaking`, `GameKit.Core`, `GameKit.Rankings`, `GameKit.TestFixtures` ProjectReferences. Build failed with two extra CS0234 errors on the missing Auth/Admin namespaces.
- **Fix:** Added `ProjectReference` rows for `..\..\src\GameKit.Auth\GameKit.Auth.csproj` and `..\..\src\GameKit.Admin.UI\GameKit.Admin.UI.csproj` to `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj`. Mirrors `tests/GameKit.Rankings.Integration.Tests.csproj` which has exactly the same three sibling-package ProjectReferences (Auth, Admin.UI, plus Rankings itself).
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj`
- **Commit:** `4f3a9c6`
- **Result:** Integration.Tests build error count reduced from 3 → 1; the remaining error is the intended Wave-0 gate.

### Other Deviations

None. The plan's stated Wave-0 build behavior matched the actual outcome exactly: Tests + LoadTests build green; Integration.Tests has exactly one compile error pointing at the 05-02 type gate.

## Threat Surface Notes

No new attack surface. The plan added zero runtime code paths — only test infrastructure that runs inside Testcontainers (PostgresFixture + RedisFixture randomize credentials per run; bound only to the test process; teardown via container disposal). The threat register in 05-01-PLAN.md identified:

- `T-05-01-SC` (Tampering: NuGet additions) — **mitigated** by adding zero new pins (Directory.Packages.props unchanged).
- `T-05-01-01` (Information Disclosure: Testcontainer password) — **accepted** as documented; password is randomized per run and never committed.

## Self-Check: PASSED

- `tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj` — FOUND
- `tests/GameKit.Matchmaking.Tests/SmokeTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestModelCustomizer.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/Fixtures/MatchmakingIntegrationFixture.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/Fixtures/StepClock.cs` — FOUND
- `tests/GameKit.Matchmaking.LoadTests/GameKit.Matchmaking.LoadTests.csproj` — FOUND
- `tests/GameKit.Matchmaking.LoadTests/Fixtures/LoadTestFixture.cs` — FOUND
- Commit `684d538` (Task 1 — three csprojs + sln) — FOUND
- Commit `cabf94f` (Task 2 — scaffolding + IVT grant) — FOUND
- Commit `4f3a9c6` (Task 3 — advisory-lock test + csproj fixup) — FOUND
- `dotnet build tests/GameKit.Matchmaking.Tests` exit code 0 — VERIFIED (0 warnings / 0 errors)
- `dotnet build tests/GameKit.Matchmaking.LoadTests` exit code 0 — VERIFIED (0 warnings / 0 errors)
- `dotnet build tests/GameKit.Matchmaking.Integration.Tests` fails with exactly 1 CS0234 on `GameKit.Matchmaking.Data` — VERIFIED (matches plan's documented Wave-0 → 05-02 gate)
- `dotnet sln list` shows all three new Matchmaking test projects — VERIFIED
