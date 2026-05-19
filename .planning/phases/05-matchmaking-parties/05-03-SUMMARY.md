---
phase: 05
plan: 03
subsystem: matchmaking
tags: [matchmaking, options, builder, redis-keys, fluent-api, wave-1]
dependency_graph:
  requires:
    - phase-05-01 (Wave-0 test scaffolding)
    - phase-05-02 (data layer + advisory-lock key 388956820L)
  provides:
    - src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs (root options + nested Ticker/Cooldown/Analytics/Reconciler sections)
    - src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs (IValidateOptions fail-fast guard)
    - src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs (centralised Redis-key constants + formatters)
    - src/GameKit.Matchmaking/Strategy/PartyRatingAggregator.cs (integer enum Mean/Max/GlickoWeighted)
    - src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs (per-ladder bracket curve + aggregator + spread cap)
    - src/GameKit.Matchmaking/Builder/IGameKitMatchmakingBuilder.cs + GameKitMatchmakingBuilder.cs (builder + case-insensitive ladder dedup)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs + .Ladder.cs (AddMatchmaking + AddLadder fluent surface)
    - src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs (UseGameKitMatchmaking + MapMatchmaking stubs)
    - tests/GameKit.Matchmaking.Tests/Builder/MatchmakingOptionsValidationTests.cs (9 tests)
    - tests/GameKit.Matchmaking.Tests/Builder/LadderConfigDefaultsTests.cs (12 tests)
    - tests/GameKit.Matchmaking.Tests/Builder/AddMatchmakingFluentChainTests.cs (2 tests)
  affects:
    - 05-04..05-10 (every downstream plan resolves IOptions<GameKitMatchmakingOptions>, the ladder list singleton, or the MatcherLock constant)
tech_stack:
  added: []  # zero new NuGet pins
  patterns:
    - IOptions<T> + IValidateOptions<T> + ValidateOnStart() — first use of this triple in the GameKit codebase; mirrors MS Learn IOptionsBuilder guidance
    - Case-insensitive ladder name dedup via HashSet<string>(StringComparer.OrdinalIgnoreCase) — mirrors Rankings precedent (Plan 04-04)
    - Fail-fast at registration time inside AddLadder (vs deferred-runtime validation) — mirrors Phase 4 precedent
    - Partial-class extension pattern (MatchmakingBuilderExtensions + .Ladder partial) — mirrors RankingsBuilderExtensions / .Ticker
    - Forward-compatible no-op stub on MapMatchmaking — locks in consumer call sites before downstream endpoint plan (05-08) lands
key_files:
  created:
    - src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs
    - src/GameKit.Matchmaking/GameKitMatchmakingTickerOptions.cs
    - src/GameKit.Matchmaking/GameKitMatchmakingCooldownOptions.cs
    - src/GameKit.Matchmaking/GameKitMatchmakingAnalyticsOptions.cs
    - src/GameKit.Matchmaking/GameKitMatchmakingReconcilerOptions.cs
    - src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs
    - src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs
    - src/GameKit.Matchmaking/Strategy/PartyRatingAggregator.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs
    - src/GameKit.Matchmaking/Builder/IGameKitMatchmakingBuilder.cs
    - src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ladder.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs
    - tests/GameKit.Matchmaking.Tests/Builder/MatchmakingOptionsValidationTests.cs
    - tests/GameKit.Matchmaking.Tests/Builder/LadderConfigDefaultsTests.cs
    - tests/GameKit.Matchmaking.Tests/Builder/AddMatchmakingFluentChainTests.cs
  modified: []
decisions:
  - "MatcherLock = \"gamekit:matchmaking:matcher:lock\" defined in two places (MatchmakingRedisKeys.MatcherLock const + GameKitMatchmakingTickerOptions.LockKey default). Documented on both surfaces. Operators overriding GameKitMatchmakingTickerOptions.LockKey MUST update both consistently — the matchmaker resolves the value at runtime from options, but ad-hoc Redis tooling (admin dashboards, gamekit CLI) reads MatchmakingRedisKeys.MatcherLock directly."
  - "Scrutor scan for IMatchmakingStrategy implementations DEFERRED to Plan 05-04. The interface symbol does not exist yet; emitting the scan in Plan 05-03 would force a compile-time dependency on a not-yet-existing type. Plan 05-03 leaves a TODO comment at the deferral site so Plan 05-04 can locate it directly."
  - "MapMatchmaking() is intentionally a no-op stub today — Plan 05-08 wires the endpoints. Forward-compatible stub locks in the consumer call shape (app.MapMatchmaking()) so the TicTacToeDuel sample app (Plan 05-09) can wire its Program.cs against the Plan 05-03 build."
  - "Per-ladder invariants enforced at registration time inside AddLadder (BracketRampSeconds > 0, BracketEnd >= BracketStart, MaxPartyRatingSpread null or > 0) — fail-fast at host startup. The IValidateOptions surface validates only the options-tree invariants because the ladder list is held by the builder, not the options object."
  - "IOptions<GameKitMatchmakingOptions> ladder list is published twice: once as IGameKitMatchmakingBuilder (for tests + future builder-aware services) and once as IReadOnlyList<MatchmakingLadderConfig> (for downstream services that should not take a dep on the builder interface). Both are backed by the same underlying list — no double-allocation."
metrics:
  duration_min: 7
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 17
requirements_completed:
  - MATCH-01  # partial — package surface (options + builder + endpoint stub); full satisfaction continues through 05-10
  - MATCH-07  # partial — ticker/cooldown/analytics/reconciler options pinned (BackgroundService impls land in 05-05/05-07)
  - MATCH-08  # partial — LockKey + LockTtlSeconds pinned (leader election impl lands in 05-05)
  - MATCH-10  # partial — per-ladder bracket curve (Start/End/Ramp) pinned; default strategy impl lands in 05-04
  - MATCH-11  # partial — enqueue rate pinned (5/min); endpoint + sliding-window policy land in 05-08
  - MATCH-14  # partial — MatcherLock + Control* Redis keys pinned (observability port lands in 05-08)
---

# Phase 5 Plan 03: Matchmaking Configuration + Fluent-Builder Surface Summary

**Five Matchmaking options classes (root + Ticker + Cooldown + Analytics + Reconciler), an IValidateOptions fail-fast guard, the central `MatchmakingRedisKeys` constants/formatters class, the `PartyRatingAggregator` integer enum, the per-ladder `MatchmakingLadderConfig` with builder interface + impl (case-insensitive name dedup), the `AddMatchmaking()` + `AddLadder()` fluent extensions, and the forward-compatible `MapMatchmaking()` stub — a downstream consumer's `Program.cs` can now compile `services.AddGameKit().AddMatchmaking(...).AddLadder("main", ...).AddLadder("tournament", ...)` and `app.MapMatchmaking()`, even though the strategy / party service / ticker / reconciler / endpoints land in Plans 05-04..05-08.**

## Performance

- **Duration:** ~7 min
- **Started:** 2026-05-17T05:40Z
- **Completed:** 2026-05-17T05:47Z
- **Tasks:** 3
- **Files created:** 17
- **Files modified:** 0
- **Test count delta:** +23 (1 pre-existing SmokeTest → 24 total in `GameKit.Matchmaking.Tests`)

## Accomplishments

1. **Five-options tree with every default pinned to its decision source.** `GameKitMatchmakingOptions` nests `Ticker / Cooldown / Analytics / Reconciler` sub-options; every default value cites the originating RESEARCH §Decision or CONTEXT D-* in its XML doc remark. Concrete defaults:

   | Surface | Field | Default | Source |
   |---|---|---|---|
   | Ticker | TickIntervalMs | 500 | RESEARCH §Architecture diagram |
   | Ticker | LockTtlSeconds | 90 | mirror of Rankings ticker TTL |
   | Ticker | LockKey | "gamekit:matchmaking:matcher:lock" | RESEARCH §Decision 11 + CONTEXT §Reusable Assets |
   | Ticker | MaxIterationBudgetMs | 50 | RESEARCH §Decision 13 (load test) |
   | Cooldown | WindowMinutes | 60 | CONTEXT D-08 |
   | Cooldown | Step1/Step2/Step3 Minutes | 3 / 15 / 30 | CONTEXT D-08 |
   | Analytics | ChannelCapacity | 10000 | RESEARCH §Decision 7 / D-15 |
   | Analytics | DrainBatchSize | 100 | RESEARCH §Decision 7 |
   | Analytics | DrainIntervalSeconds | 5 | RESEARCH §Decision 7 |
   | Analytics | PollyMaxRetryAttempts | 4 | RESEARCH §Decision 7 |
   | Analytics | PollyBaseDelayMs | 500 | RESEARCH §Decision 7 |
   | Analytics | PollyTimeoutSeconds | 30 | RESEARCH §Decision 7 |
   | Reconciler | SweepIntervalSeconds | 30 | RESEARCH §Decision 6 |
   | Reconciler | StaleTicketThresholdMinutes | 5 | RESEARCH §Decision 6 |
   | Reconciler | OrphanSessionThresholdMinutes | 10 | RESEARCH §Decision 6 |
   | Reconciler | LeaderOnly | true | RESEARCH §Decision 6 |
   | (root) | AcceptTimeoutSeconds | 10 | CONTEXT D-07 (CS:GO-style) |
   | (root) | TicketRetentionDays | 30 | CONTEXT D-17 |
   | (root) | MatchmakingEnqueueRatePerMinute | 5 | RESEARCH §Decision 10 / MATCH-11 |

2. **IValidateOptions fail-fast guard** (`MatchmakingOptionsValidator`). Mitigates threat **T-05-03-01** (misconfigured matchmaker causes runtime divide-by-zero / infinite loop). Rejects: AcceptTimeoutSeconds < 1, MatchmakingEnqueueRatePerMinute < 1, TicketRetentionDays < 1, Ticker.TickIntervalMs < 1, Ticker.LockTtlSeconds < 1, Ticker.MaxIterationBudgetMs < 1, empty LockKey, Cooldown.WindowMinutes < 1, Step* < 0, Analytics.ChannelCapacity < 100, DrainBatchSize < 1, DrainIntervalSeconds < 1, PollyMaxRetryAttempts < 0, PollyBaseDelayMs < 1, PollyTimeoutSeconds < 1, Reconciler.SweepIntervalSeconds < 5, StaleTicketThresholdMinutes < 1, OrphanSessionThresholdMinutes < 1. **9 unit tests** in `MatchmakingOptionsValidationTests` exercise every rule and the default-passes-validation sanity case.

3. **`MatchmakingRedisKeys` centralised key surface.** Eight key namespaces in one class: queue / ticket / proposal / proposal-accepts / status-channel formatters plus the four control constants (`MatcherLock`, `ControlPaused`, `ControlDrain`, `ProposalAcceptsSuffix`). `MatcherLock` literal equals **`"gamekit:matchmaking:matcher:lock"`** per the plan's `must_haves.artifacts` requirement and CONTEXT §Reusable Assets.

4. **`PartyRatingAggregator` + `MatchmakingLadderConfig`.** Integer enum stored as `int` (Phase 5 mandatory — no `HasConversion<string>()`). Per-ladder defaults: `BracketStart=100`, `BracketEnd=500`, `BracketRampSeconds=40`, `PartyRatingAggregator=Mean`, `MaxPartyRatingSpread=null` — every value cited to CONTEXT D-11..D-14 in XML docs. The `Name` field doubles as the case-insensitive JOIN KEY against the Rankings-owned ladder of the same name (documented on the property's XML doc).

5. **Builder interface + impl with registration-time fail-fast.** `GameKitMatchmakingBuilder.AddLadder(name, configure)` enforces case-insensitive dedup via `HashSet<string>(StringComparer.OrdinalIgnoreCase)` AND per-ladder invariants (`BracketRampSeconds > 0`, `BracketEnd >= BracketStart`, `MaxPartyRatingSpread null or > 0`). Misconfiguration throws at `AddLadder` time, never at runtime — mirrors the Phase 4 builder precedent. **12 unit tests** in `LadderConfigDefaultsTests` cover every default, duplicate-name guard, every invariant, and null/empty-name rejection.

6. **`AddMatchmaking()` fluent extension** (`MatchmakingBuilderExtensions.AddMatchmaking`). Single call site that:
   - binds `GameKitMatchmakingOptions` with `ValidateOnStart()` so host startup fails fast on bad config;
   - registers `MatchmakingOptionsValidator` via `TryAddEnumerable Singleton`;
   - registers `MatchmakingModelBuilderExtension` via `TryAddEnumerable Singleton` (Plan 05-02);
   - `AddHostedService<MatchmakingMigrationHostedService>()` (Plan 05-02);
   - registers the `GameKitMatchmakingBuilder` as a singleton `IGameKitMatchmakingBuilder`;
   - registers the accumulated ladder list as a singleton `IReadOnlyList<MatchmakingLadderConfig>` so downstream services can inject the per-ladder tree without a dep on the builder interface.

7. **`AddLadder()` partial-class extension** (`MatchmakingBuilderExtensions.Ladder`). Split from the main file mirroring `RankingsBuilderExtensions.Ticker.cs` precedent. Delegates to the builder interface — fluent-API surface only, no logic duplication.

8. **`MapMatchmaking()` forward-compatible stub.** Returns `routes` unchanged with TODO markers pinning the four downstream-endpoint groups Plan 05-08 will register. `UseGameKitMatchmaking()` is a similar no-op stub. **2 unit tests** in `AddMatchmakingFluentChainTests` exercise both the full `AddMatchmaking().AddLadder().AddLadder()` chain (options bound, builder + list singletons registered) and the `MapMatchmaking()` zero-endpoints contract.

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | Options tree + IValidateOptions guard + Redis-key constants + 9 validation tests | `fd34a72` | feat |
| 2 | MatchmakingLadderConfig + IGameKitMatchmakingBuilder + GameKitMatchmakingBuilder + PartyRatingAggregator + 12 ladder-config tests | `bb2d7f6` | feat |
| 3 | AddMatchmaking + AddLadder fluent extensions + MapMatchmaking stub + 2 fluent-chain smoke tests | `cdbaaae` | feat |

**Plan metadata commit:** see final commit covering SUMMARY + STATE + ROADMAP + REQUIREMENTS.

## Verification Evidence

- `dotnet build src/GameKit.Matchmaking --nologo` → **0 warnings, 0 errors** (final state).
- `dotnet test tests/GameKit.Matchmaking.Tests --nologo` → **24 / 24 PASS** (1 pre-existing SmokeTest + 9 MatchmakingOptionsValidationTests + 12 LadderConfigDefaultsTests + 2 AddMatchmakingFluentChainTests).
- `MatchmakingRedisKeys.MatcherLock` literal-equality check against `"gamekit:matchmaking:matcher:lock"` — VERIFIED at the constant declaration site (`src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs:38`).
- Per-ladder defaults asserted live by `LadderConfigDefaultsTests` (`BracketStart=100`, `BracketEnd=500`, `BracketRampSeconds=40`, `PartyRatingAggregator=Mean`, `MaxPartyRatingSpread=null`).
- Validation tests assert default options pass + every invalid value trips `OptionsValidationException` on `IOptions<T>.Value` resolution — the same eager-fail path `ValidateOnStart()` triggers at host startup.
- Fluent-chain smoke test (`AddMatchmaking_With_AddLadder_Registers_Options_And_Builder`) proves `services.AddGameKit()-shape → AddMatchmaking(...) → AddLadder(...).AddLadder(...)` compiles, options are bound, and both `IGameKitMatchmakingBuilder` + `IReadOnlyList<MatchmakingLadderConfig>` singletons are resolvable from DI.

## Decisions Made

- **Scrutor `IMatchmakingStrategy` scan deferred to Plan 05-04.** The interface symbol does not exist in Plan 05-03's compile graph — emitting the scan now would create a forward dep on a not-yet-existing type. A TODO comment at the deferral site in `MatchmakingBuilderExtensions.AddMatchmaking` documents the location Plan 05-04 must edit. No build-order coupling; Plan 05-04 ships the interface + the scan inside its own extension method called from `AddMatchmaking`.
- **`MapMatchmaking()` ships as a no-op stub.** Consumer call sites are locked in now so `TicTacToeDuel` (Plan 05-09) can wire `app.MapMatchmaking()` against the Plan 05-03 build. Plan 05-08's endpoint registration is purely additive — no breaking change.
- **`MatcherLock` literal pinned in two places** (`MatchmakingRedisKeys.MatcherLock` constant + `GameKitMatchmakingTickerOptions.LockKey` default). Operator override semantics are documented on both surfaces: the matchmaker resolves the value from options at runtime, but ad-hoc tooling (`gamekit` CLI, admin Redis dashboard) reads the constant directly — operators overriding the option MUST update both consistently. This duplication is intentional defense-in-depth: a rename of the option default cannot silently desync the operator tooling.
- **Per-ladder invariants enforced at registration time** (inside `GameKitMatchmakingBuilder.AddLadder`), not in `MatchmakingOptionsValidator`. The ladder list is held by the builder (singleton), not the options object — `IValidateOptions` cannot reach it without an awkward DI roundtrip. Fail-fast at `AddLadder` is strictly stronger because it throws inside `services.AddGameKit().AddMatchmaking().AddLadder(...)` itself, before any service is built.
- **Ladder list published twice in DI** (as `IGameKitMatchmakingBuilder` and as `IReadOnlyList<MatchmakingLadderConfig>`). Backed by the same underlying `List<MatchmakingLadderConfig>` — no double-allocation. Downstream services should prefer `IReadOnlyList<MatchmakingLadderConfig>` to avoid depending on the builder interface.

## Deviations from Plan

### Auto-fixed Issues

None. The plan body's `<action>` and `<behavior>` sections matched the codebase patterns exactly; no Rule 1/2/3 fixes were needed.

### Plan-Body-Documented Deferrals

The plan body explicitly documented two deferrals which were executed as planned:

1. **Scrutor scan for `IMatchmakingStrategy` deferred to Plan 05-04.** Plan body Task 3 `<action>` "Decision: Option A — Plan 05-03 emits a `// TODO(05-04): add Scrutor scan for IMatchmakingStrategy implementations` comment placeholder". Executed verbatim.
2. **`MapMatchmaking()` endpoint registration deferred to Plan 05-08.** Plan body Task 3 `<behavior>` "currently maps zero endpoints (Plan 05-08 fills this in)". Executed verbatim; XML doc on `MapMatchmaking` explicitly cites Plan 05-08.

### Other Deviations

None.

## Threat Surface Notes

The plan's `<threat_model>` identified two threats — both addressed at the configuration level:

- **T-05-03-01 (DoS via misconfigured BracketRampSeconds=0 / AcceptTimeoutSeconds=0):** mitigated. Per-ladder invariants are fail-fast inside `GameKitMatchmakingBuilder.AddLadder` (`BracketRampSeconds > 0`, `BracketEnd >= BracketStart`). Top-level option invariants are fail-fast via `MatchmakingOptionsValidator` + `ValidateOnStart()` (`AcceptTimeoutSeconds >= 1`, etc.). Both throw before any HTTP request is served.
- **T-05-03-02 (Information Disclosure: ladder names colliding with admin-internal names):** accepted as documented. Case-insensitive dedup catches double-registration; intentional collision with Rankings ladder names is the documented JOIN behavior (the `MatchmakingLadderConfig.Name` XML doc cites the convention explicitly).

No new threat flags surfaced during execution. No new network endpoints / auth paths / file access patterns / schema changes were introduced — this plan is configuration + DI wiring only.

## Self-Check: PASSED

### Files
- `src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs` — FOUND
- `src/GameKit.Matchmaking/GameKitMatchmakingTickerOptions.cs` — FOUND
- `src/GameKit.Matchmaking/GameKitMatchmakingCooldownOptions.cs` — FOUND
- `src/GameKit.Matchmaking/GameKitMatchmakingAnalyticsOptions.cs` — FOUND
- `src/GameKit.Matchmaking/GameKitMatchmakingReconcilerOptions.cs` — FOUND
- `src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs` — FOUND
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — FOUND (MatcherLock = "gamekit:matchmaking:matcher:lock")
- `src/GameKit.Matchmaking/Strategy/PartyRatingAggregator.cs` — FOUND (Mean=0, Max=1, GlickoWeighted=2)
- `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/IGameKitMatchmakingBuilder.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ladder.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Builder/MatchmakingOptionsValidationTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Builder/LadderConfigDefaultsTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Builder/AddMatchmakingFluentChainTests.cs` — FOUND

### Commits
- `fd34a72` (Task 1 — options tree + validator + Redis-keys + 9 validation tests) — FOUND
- `bb2d7f6` (Task 2 — ladder config + builder + 12 ladder tests + enum) — FOUND
- `cdbaaae` (Task 3 — fluent extensions + MapMatchmaking stub + 2 chain tests) — FOUND

### Verification gates
- `dotnet build src/GameKit.Matchmaking` exit 0 — VERIFIED (0 warnings / 0 errors)
- `dotnet test tests/GameKit.Matchmaking.Tests --filter MatchmakingOptionsValidation` exit 0 (9 passed) — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Tests --filter LadderConfigDefaults` exit 0 (12 passed) — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Tests` full suite exit 0 (24 passed) — VERIFIED
- `MatchmakingRedisKeys.MatcherLock` literal = `"gamekit:matchmaking:matcher:lock"` — VERIFIED (Redis/MatchmakingRedisKeys.cs:38)
- Fluent-chain smoke test (`AddMatchmaking_With_AddLadder_Registers_Options_And_Builder`) passes — VERIFIED
- Zero new NuGet pins (Directory.Packages.props unchanged) — VERIFIED

## Next Plan Readiness

- **05-04** (IMatchmakingStrategy + EloRangeMatchmakingStrategy + PartyService): can ship. The `MatchmakingLadderConfig` shape it consumes (BracketStart/End/RampSeconds + PartyRatingAggregator + MaxPartyRatingSpread) is fixed. `IOptions<GameKitMatchmakingOptions>` + `IReadOnlyList<MatchmakingLadderConfig>` singletons are resolvable from DI. The Scrutor scan deferred from 05-03 must be added to `AddMatchmaking` (TODO comment marks the line).
- **05-05** (MatchmakerLeaseHelper + MatchmakerTickerService): can ship. `GameKitMatchmakingTickerOptions.LockKey` / `LockTtlSeconds` / `TickIntervalMs` / `MaxIterationBudgetMs` are pinned. The TickerService can `AddHostedService` onto `builder.Services` after `AddMatchmaking()`.
- **05-06** (ProposalService): can ship. `GameKitMatchmakingOptions.AcceptTimeoutSeconds` is pinned (10s). `MatchmakingRedisKeys.Proposal()` + `ProposalAccepts()` + `StatusChannel()` formatters are in place.
- **05-07** (Reconciler + AnalyticsDrainService + RetentionCleanupService): can ship. `Reconciler.*` + `Analytics.*` + `TicketRetentionDays` options pinned.
- **05-08** (HTTP endpoints): can ship. `MatchmakingEnqueueRatePerMinute` (5/min) pinned; `MapMatchmaking()` stub is the call site to populate. The TODO comments inside the stub mark the four endpoint groups to register.
- **05-09** (TicTacToeDuel sample app): can ship. `services.AddGameKit().AddMatchmaking().AddLadder("tictactoe", ...)` and `app.MapMatchmaking()` both compile against the Plan 05-03 build.

---
*Phase: 05-matchmaking-parties*
*Plan: 03*
*Completed: 2026-05-17*
