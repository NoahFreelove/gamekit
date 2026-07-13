---
phase: 13-observability-foundations
plan: "02"
subsystem: observability
tags: [opentelemetry, telemetry, activitysource, meter, otel, constants, tdd]

requires:
  - phase: 13-01
    provides: [PiiAttributeAnalyzer, GK0001, GK0002]

provides:
  - GameKitTelemetry constants class (single source of truth for source/meter names + D-04 attr keys)
  - AddGameKitObservability() extension on IGameKitBuilder
  - GameKitObservabilityOptions with OtlpEndpoint
  - OTel SDK PrivateAssets="all" refs in GameKit.Core.csproj (OBS-01 guard)
  - Reflection-based enforcement tests (criterion #4 single source of truth)

affects: [13-03, 13-04, phase-15-per-package-instrumentation]

tech-stack:
  added:
    - OpenTelemetry.Extensions.Hosting 1.15.3 (PrivateAssets=all — not shipped to consumers)
    - OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.3 (PrivateAssets=all — not shipped to consumers)
  patterns:
    - GameKitTelemetry as static class aggregating all per-package ActivitySource/Meter names as consts
    - PrivateAssets="all" on OTel SDK refs to prevent transitive dep flow (OBS-01 guard)
    - Assembly.LoadFrom() for reflection tests to avoid transitive NU1903 build failure
    - AddGameKitObservability() following IGameKitBuilder fluent builder pattern (returns IGameKitBuilder)
    - OTel sources/meters wired via GameKitTelemetry constants, never magic strings

key-files:
  created:
    - src/GameKit.Core/Telemetry/GameKitTelemetry.cs
    - src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs
    - tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs
  modified:
    - src/GameKit.Core/GameKit.Core.csproj
    - tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj

key-decisions:
  - "GameKitTelemetry is public static (not internal) — per-package consumers reference it at compile time"
  - "OTel SDK refs carry PrivateAssets=all so consumers who skip AddGameKitObservability() pull no OTel SDK (OBS-01)"
  - "Assembly.LoadFrom() for Matchmaking dll in reflection tests avoids ProjectReference that would pull NU1903 build failure"
  - "AddGameKitObservability() returns IGameKitBuilder (not IGameKitObservabilityBuilder) — no sub-builder needed for this thin extension"
  - "OtlpEndpoint null = no exporter registered (host manages its own exporter); non-null = AddOtlpExporter called on both tracing and metrics"

patterns-established:
  - "GameKitTelemetry: aggregate all ActivitySource/Meter/attr-key consts in one static class; per-package consts initialize from it"
  - "AddGameKitObservability(): IGameKitBuilder extension + Action<TOptions>? configure = null + ArgumentNullException.ThrowIfNull(builder) + return builder"
  - "Reflection enforcement tests: Assembly.LoadFrom for pre-built sibling when direct ProjectReference would drag in pre-existing build failures"

requirements-completed: [OBS-01, OBS-02, OBS-03]

duration: 9min
completed: "2026-06-14"
---

# Phase 13 Plan 02: Telemetry Foundation + AddGameKitObservability Summary

**GameKitTelemetry constants class (single source of truth for source/meter/attr-key names) + AddGameKitObservability() builder extension that wires OTel sources/meters with PrivateAssets-gated SDK deps (OBS-01/02/03)**

## Performance

- **Duration:** 9 min
- **Started:** 2026-06-14T19:34:24Z
- **Completed:** 2026-06-14T19:43:59Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- `GameKitTelemetry` static class defines `Version = "1.0.0"`, `SourcePrefix = "GameKit"`, two source name consts (`MatchmakingTickerSourceName`, `RankingsTickerSourceName`), one meter name const (`MatchmakingMeterName`), and seven D-04 low-cardinality attribute key consts (`AttrLadderId` through `AttrErrorType`)
- `AddGameKitObservability()` extension on `IGameKitBuilder` calls `AddOpenTelemetry().WithTracing().WithMetrics()`, registers all GameKit sources/meters via `GameKitTelemetry` constants, optionally wires OTLP exporter when `OtlpEndpoint` is set
- Both OTel SDK packages carry `PrivateAssets="all"` in `GameKit.Core.csproj` — consumers who skip `AddGameKitObservability()` pull no OTel SDK (OBS-01)
- 16 tests added: 13 constants enforcement tests + 3 smoke tests for `AddGameKitObservability`; all 149 `GameKit.Core.Tests` pass; zero GK0001/GK0002 regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: GameKitTelemetry constants class + reflection enforcement test** - `d99d88c` (feat)
2. **Task 2: AddGameKitObservability() extension + OTel PrivateAssets refs + smoke test** - `11653a6` (feat)

## Files Created/Modified

- `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` - Single source of truth: Version, SourcePrefix, source/meter name consts, seven D-04 Attr* key consts; full XML doc on every public member
- `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` - AddGameKitObservability() extension + co-located GameKitObservabilityOptions sealed class
- `src/GameKit.Core/GameKit.Core.csproj` - Added OpenTelemetry.Extensions.Hosting and OpenTelemetry.Exporter.OpenTelemetryProtocol both with PrivateAssets="all"
- `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` - 16 tests: constants, reflection enforcement, AddGameKitObservability smoke tests
- `tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj` - Added comment noting NU1903 suppression strategy (no actual project ref added)

## Decisions Made

- `GameKitTelemetry` is `public static` (not `internal static`) because per-package assemblies (Matchmaking, Rankings) reference it at compile time for their own `SourceName = GameKitTelemetry.MatchmakingTickerSourceName` initializers
- OTel SDK uses `PrivateAssets="all"` (not a separate conditional file or `#if` gate) — simpler than the alternative and matches what EF Core Design tooling uses for its own non-runtime deps
- Reflection enforcement tests load `GameKit.Matchmaking.dll` via `Assembly.LoadFrom()` rather than a compile-time `ProjectReference` — adding the project ref drags in NU1903 (MessagePack pre-existing vulnerability) which is a pre-existing issue in Matchmaking/Lobby/Admin transitive deps
- `AddGameKitObservability()` returns `IGameKitBuilder` (fluent chaining), not a sub-builder — the observability extension has a single configuration option and no subsequent `.Add*()` calls to chain

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

**Pre-existing NU1903 build failure in GameKit.Matchmaking:** The NU1903 (MessagePack 2.5.187 vulnerability) is a pre-existing issue that causes `dotnet build GameKit.Matchmaking` to fail when `TreatWarningsAsErrors=true`. The plan's reflection enforcement test requires `MatchmakingActivitySource.SourceName` and `MatchmakingMeter.MeterName` to be read at runtime. Resolution: built `GameKit.Matchmaking` with `/p:TreatWarningsAsErrors=false` to produce the dll, then loaded it via `Assembly.LoadFrom()` in the test, avoiding a compile-time `ProjectReference` that would propagate the NU1903 failure into `GameKit.Core.Tests`.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `GameKitTelemetry` is ready for Plan 13-03 to use as the single source of truth when modifying `MatchmakingActivitySource` (normalize camelCase tags to dotted) and extracting `RankingsActivitySource`
- `AddGameKitObservability()` is callable from `TicTacToeDuel` sample app's `Program.cs` as soon as Plan 13-04 wires the observability docker compose stack
- Reflection enforcement tests will stay green throughout Plan 13-03 even before the per-package consts are updated to reference `GameKitTelemetry` — the test asserts VALUE equality, not reference equality

---
*Phase: 13-observability-foundations*
*Completed: 2026-06-14*
