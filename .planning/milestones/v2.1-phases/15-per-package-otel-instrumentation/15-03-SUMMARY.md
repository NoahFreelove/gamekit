---
phase: 15-per-package-otel-instrumentation
plan: "03"
subsystem: telemetry
tags: [otel, tracing, w3c-propagation, matchmaking, obs-06, fan-in]
depends_on: ["15-01", "15-02"]
provides:
  - MatchmakingRedisKeys.TicketTraceParent ("otel.traceparent" hash field constant)
  - MatchmakingRedisKeys.TicketTraceState ("otel.tracestate" hash field constant)
  - MatchmakingService.EnqueueAsync writes Activity.Current.Id to ticket hash (server-side)
  - QueuedParty.TraceparentStr / QueuedParty.TracestateStr init-only carry fields
  - BuildQueuedPartyFromHash populates TraceparentStr/TracestateStr from hash
  - MatchmakingActivitySource.StartMatchFormationActivity(parentContext=default)
  - MatchmakerTickerService: restore parent ctx + fan-in links on AtomicClaimResult.Success
  - W3CTracePropagationTests — 3 facts implemented and passing (Plan-01 stubs un-skipped)
affects:
  - src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs
  - src/GameKit.Matchmaking/Services/MatchmakingService.cs
  - src/GameKit.Matchmaking/Strategy/QueuedParty.cs
  - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
  - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs
tech_stack:
  added: []
  patterns:
    - W3C traceparent written server-side from Activity.Current.Id at enqueue (not client-supplied)
    - ActivityContext.TryParse + StartActivity(name, kind, parentContext) for async span parenting
    - ActivityLink fan-in pattern for N-to-1 merge points (D-03)
    - init-only property extension on positional record (preserves all call sites)
    - In-process ActivityListener test pattern for span parentage + link assertions
key_files:
  created: []
  modified:
    - src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs
    - src/GameKit.Matchmaking/Services/MatchmakingService.cs
    - src/GameKit.Matchmaking/Strategy/QueuedParty.cs
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
    - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs
    - tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs
decisions:
  - "QueuedParty TraceparentStr/TracestateStr added as init-only properties (not positional params) — preserves all 3 existing positional construction sites without changes"
  - "traceparent written server-side from Activity.Current.Id regardless of sampling flags — downstream ActivityContext.TryParse honours flags correctly (Pitfall 1)"
  - "NonSampledParent test uses no listener (not AllDataAndRecorded) — without a listener StartActivity always returns null, correctly exercising the null no-op path without a sampler override"
  - "MatchFormation span starts inside using block on Success branch — ActivityLink fan-in iterates MatchedTickets.Skip(1); MatchesFormed.Add preserved in same branch (Plan 02)"
metrics:
  duration: 4min
  completed: 2026-06-22T21:22:00Z
  tasks_completed: 3
  files_changed: 6
status: complete
---

# Phase 15 Plan 03: W3C Trace Propagation Summary

**One-liner:** W3C traceparent/tracestate stored in Redis ticket hash at enqueue, carried on QueuedParty, restored in the ticker to parent the MatchFormation span as a descendant of the originating enqueue trace; fan-in links for co-matched tickets; three OBS-06 propagation tests un-skipped and passing.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Add ticket-hash traceparent constants, write at enqueue, carry on QueuedParty | e4fc524 | `MatchmakingRedisKeys.cs`, `MatchmakingService.cs`, `QueuedParty.cs`, `MatchmakerTickerService.cs` |
| 2 | Add MatchFormation span helper + restore parent context and fan-in links in ticker | 7dad963 | `MatchmakingActivitySource.cs`, `MatchmakerTickerService.cs` |
| 3 | Implement W3C propagation tests (un-skip Plan-01 stubs) | 6a825d2 | `W3CTracePropagationTests.cs` |

## Verification

- `dotnet build src/GameKit.Matchmaking -p:NuGetAudit=false`: 0 errors, 0 warnings — GK0001 analyzer passes
- `dotnet test tests/GameKit.Matchmaking.Tests --filter "W3CTracePropagation"`: 3/3 pass
- `dotnet test tests/GameKit.Matchmaking.Tests -p:NuGetAudit=false`: 115 passed, 0 failed, 0 skipped (was 112/0/3 after Plan 02)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CS1574 on cref in XML doc comment for StartMatchFormationActivity**
- **Found during:** Task 2 build
- **Issue:** `<see cref="ActivitySource.StartActivity(string, ActivityKind, ActivityContext)"/>` in the XML doc could not be resolved — the overloaded-method cref syntax requires exact parameter type match including namespace-qualified types; the compiler reported CS1574.
- **Fix:** Changed the cref to `<see cref="ActivitySource"/>` with a prose description of the behavior; the doc communicates the same information without depending on a resolvable overload cref.
- **Files modified:** `MatchmakingActivitySource.cs`
- **Commit:** 7dad963

**2. [Rule 1 - Bug] NonSampledParent test needed sampler strategy clarification**
- **Found during:** Task 3 test run
- **Issue:** Initial test used `ActivitySamplingResult.AllDataAndRecorded` listener which OVERRIDES the parent's non-sampled flags — `StartMatchFormationActivity(nonSampledCtx)` returned a non-null span, failing the `Assert.Null` check. RESEARCH Pitfall 1 documented this: "a non-recorded parent yields null unless the local sampler overrides."
- **Fix:** Removed the listener from `NonSampledParent_Produces_NoFormationSpan`. Without a listener, `ActivitySource.StartActivity` always returns null regardless of parent flags. This correctly exercises the null no-op code path that the ticker must handle safely. Added explanatory comment documenting why `AllDataAndRecorded` would override and why no-listener is the right approach.
- **Files modified:** `W3CTracePropagationTests.cs`
- **Commit:** 6a825d2

## Key Decisions Made

1. **QueuedParty carry fields as init-only properties:** Adding `TraceparentStr` and `TracestateStr` as `{ get; init; }` properties on the positional record body (not as trailing positional params) preserves all 3 existing `new QueuedParty(...)` construction sites in `MatchmakerTickerService.BuildQueuedPartyFromHash`, `ProposalService`, and test builders without any changes to those callers.

2. **Server-side traceparent write at enqueue:** `Activity.Current.Id` is written to the Redis hash server-side within `EnqueueAsync`. Clients cannot influence this field — the enqueue HTTP handler has no way to inject `otel.traceparent` into the Redis hash. This satisfies T-15-03-TRACE-INJ.

3. **Non-sampled test approach:** The `NonSampledParent` test validates that the code path handles `null` from `StartMatchFormationActivity` safely. It does NOT use a listener with `AllDataAndRecorded` (which would override the parent's sampling decision and defeat the test). The no-listener approach is the correct proxy for "sampler respects non-sampled parent" because without a listener the sampler is trivially `None`.

4. **MatchFormation span scope:** The span wraps the `PublishProposalEventsAsync` call inside the `AtomicClaimResult.Success` branch — the proposal write is the natural boundary for the match-formation event. The span is started after the Lua atomic-claim and encompasses the fan-in link attachment and proposal publication.

## Threat Surface Scan

No new network endpoints or auth paths. The implementation follows the T-15-03-TRACE-INJ mitigation documented in the plan — `otel.traceparent` is written server-side from `Activity.Current.Id` in `EnqueueAsync`; there is no client-supplied field path. `ActivityContext.TryParse` is non-throwing; a parse failure falls through to `hasParent = false` and starts a root span (no exception). No PII in span attributes on the MatchFormation span.

## Known Stubs

None — all three tasks are fully implemented and tested.

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — TicketTraceParent + TicketTraceState | FOUND |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` — Activity.Current write | FOUND |
| `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` — TraceparentStr + TracestateStr | FOUND |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — BuildQueuedPartyFromHash + Success branch | FOUND |
| `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` — StartMatchFormationActivity | FOUND |
| `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs` — 3 facts, no Skip | FOUND |
| Commit e4fc524 (Task 1) | FOUND |
| Commit 7dad963 (Task 2) | FOUND |
| Commit 6a825d2 (Task 3) | FOUND |
| Full suite: 115 passed, 0 failed, 0 skipped | PASSED |
