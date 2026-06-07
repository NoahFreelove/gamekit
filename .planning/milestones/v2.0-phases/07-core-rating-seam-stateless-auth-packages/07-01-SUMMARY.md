---
phase: 07-core-rating-seam-stateless-auth-packages
plan: "01"
subsystem: GameKit.Core
tags: [core, rating, optional-port, seam, di, null-object]
dependency_graph:
  requires: []
  provides: [IPlayerRatingProvider, PlayerRatingSnapshot, NullPlayerRatingProvider]
  affects: [GameKit.Rankings (Phase 8 — overrides via TryAddSingleton after AddGameKit)]
tech_stack:
  added: []
  patterns:
    - "Optional-port pattern: TryAddSingleton<IAbstraction, NullImpl>() — same as IPresenceProvider"
    - "Null-object returning ImmutableDictionary.Empty — zero allocation, deterministic"
    - "InternalsVisibleTo: existing GameKit.Core.Tests grant covers internal NullPlayerRatingProvider"
key_files:
  created:
    - src/GameKit.Core/Services/IPlayerRatingProvider.cs
    - src/GameKit.Core/Services/NullPlayerRatingProvider.cs
    - tests/GameKit.Core.Tests/IPlayerRatingProviderTests.cs
  modified:
    - src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
decisions:
  - "NullPlayerRatingProvider is internal sealed (not public) — visible to Core.Tests via existing InternalsVisibleTo grant, not part of public API"
  - "TryAddSingleton used (not AddSingleton) so Phase 8 Rankings can override by registering after AddGameKit()"
  - "ImmutableDictionary<Guid,PlayerRatingSnapshot>.Empty returned by null-object — zero allocation, thread-safe"
  - "Added using Microsoft.Extensions.DependencyInjection.Extensions to GameKitServiceCollectionExtensions.cs for TryAddSingleton availability"
metrics:
  duration: "~2 minutes"
  completed: "2026-06-05"
  tasks: 3
  files: 4
requirements_satisfied: [CORE-18]
---

# Phase 7 Plan 1: IPlayerRatingProvider Rating Seam (CORE-18) Summary

**One-liner:** `IPlayerRatingProvider` optional-port seam with `NullPlayerRatingProvider` null-object registered via `TryAddSingleton` in `AddGameKit()` so Core-only installs degrade gracefully to zero-rated behaviour.

## What Was Built

Delivered the `IPlayerRatingProvider` optional-port seam in `GameKit.Core` (CORE-18):

1. **`IPlayerRatingProvider` interface** — mirrors the `IPresenceProvider` optional-port pattern. Single method `GetRatingsAsync` returning `ValueTask<IReadOnlyDictionary<Guid, PlayerRatingSnapshot>>`. Fully XML-documented (CS1591-clean).

2. **`PlayerRatingSnapshot` record** — carries `PlayerId`, `Rating`, `RatingDeviation`, `Volatility` (all `double` matching `PlayerRank.cs` field types from Phase 4 Rankings).

3. **`NullPlayerRatingProvider`** — `internal sealed` null-object returning `ImmutableDictionary<Guid, PlayerRatingSnapshot>.Empty` for any query. Accessible to `GameKit.Core.Tests` via the pre-existing `InternalsVisibleTo("GameKit.Core.Tests")` grant in `AssemblyInfo.cs`.

4. **`TryAddSingleton` registration** in `GameKitServiceCollectionExtensions.AddGameKit()` — placed before `IGameKitRateLimitPolicies`, after the optional-port session-service block. `Microsoft.Extensions.DependencyInjection.Extensions` using directive added.

5. **Two unit tests** — `NullPlayerRatingProvider_Returns_EmptyDictionary_For_Any_Players` and `AddGameKit_Registers_NullPlayerRatingProvider_As_Singleton` — both green with no Postgres/Testcontainers dependency.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 — Interface + Record | `8497df5` | `feat(07-01): define IPlayerRatingProvider interface + PlayerRatingSnapshot record` |
| 2 — NullImpl + Registration | `a4d4d8f` | `feat(07-01): add NullPlayerRatingProvider + TryAddSingleton registration (CORE-18)` |
| 3 — Unit tests | `6c6d8f7` | `test(07-01): unit tests for NullPlayerRatingProvider + DI registration (CORE-18)` |

## Verification

- `dotnet build src/GameKit.Core/GameKit.Core.csproj --nologo` — 0 warnings, 0 errors
- `dotnet test ... --filter "FullyQualifiedName~IPlayerRatingProviderTests" --nologo` — 2 passed, 0 failed
- `git diff --name-only` limited to `src/GameKit.Core/` and `tests/GameKit.Core.Tests/` — zero Matchmaking files touched
- Zero migrations introduced — no EF migration files present in diff

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Missing `using GameKit.Core.Builder;` in test file**
- **Found during:** Task 3 — first test run failed with CS1061 (`ServiceCollection` does not contain `AddGameKit`)
- **Issue:** Test file initially lacked the `using GameKit.Core.Builder;` directive needed to bring `AddGameKit` extension method into scope
- **Fix:** Added `using GameKit.Core.Builder;` to `IPlayerRatingProviderTests.cs`
- **Files modified:** `tests/GameKit.Core.Tests/IPlayerRatingProviderTests.cs`
- **Commit:** `6c6d8f7` (fixed before commit)

## Known Stubs

None. All functionality is fully implemented: the interface has a real contract, the null-object returns a concrete (empty) result, and the DI registration is live in `AddGameKit()`.

## Threat Flags

No new security-relevant surface introduced. `IPlayerRatingProvider` is an optional-port abstraction — no network endpoints, no auth paths, no schema changes, no trust-boundary crossings in this plan. T-07-01-01 mitigation (DoS via missing DI registration) is satisfied by the `TryAddSingleton` registration verified by Task 3 Test 2.

## Self-Check: PASSED

All created files exist on disk. All three task commits verified in git log.
