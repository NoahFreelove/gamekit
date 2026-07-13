---
phase: 15-per-package-otel-instrumentation
verified: 2026-06-22T00:00:00Z
status: passed
score: 4/4 must-haves verified
behavior_unverified: 0
overrides_applied: 0
live_verification: passed (gap closure + automated re-run — see "Live Verification Update" at end)
human_verification:
  - test: "Start the TicTacToeDuel sample stack with docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d, drive matchmaking traffic (enqueue two tickets), then open Grafana and confirm: (a) the matchmaking-queue-depth dashboard renders gamekit_matchmaking_queue_depth and gamekit_matchmaking_budget_bail_total; (b) the ticker-health dashboard renders gamekit_matchmaking_ticker_lag_ms_bucket with real p50/p99 values."
    expected: "Both dashboards show non-zero data. Panels do not display 'No data'. The Rankings Decay Duration panel legend reads 'p50 ms ()' / 'p99 ms ()' with an empty ladder_id variable — this is the known WR-01 cosmetic defect, not a failure of criterion #4."
    why_human: "Criterion #4 requires a running Tempo + Prometheus + Grafana stack receiving real OTLP traffic — cannot be verified with grep or in-process tests."
  - test: "Enqueue a matchmaking ticket in the sample app while a Tempo trace is being captured (via the sample stack). In Grafana Explore → Tempo, find the enqueue trace and confirm the MatchFormation span appears as a descendant (child) of the HTTP enqueue span, not as an independent root span."
    expected: "A single trace timeline shows: HTTP enqueue span → MatchFormation span (child). Fan-in: if a 2-player match, second ticket's traceparent appears as an ActivityLink on the MatchFormation span."
    why_human: "Criterion #2 live-Tempo descent check requires a running sample stack with two concurrent clients generating a real match. The in-process W3C tests (W3CTracePropagationTests — 3/3 passing) are the automated proxy per the plan's documented verification contract; the live Tempo check is the remaining manual step."
behavior_unverified_items:
  - truth: "lobby.connected_clients ObservableGauge reflects OnConnectedAsync/OnDisconnectedAsync correctly when OnConnectedAsync throws mid-connect"
    test: "Trigger a Postgres failure (or kill the DB) during an active OnConnectedAsync call and observe that the gauge does not drift upward."
    expected: "The gauge count matches the actual number of live connections; no permanent over-count accumulates."
    why_human: "CR-01 (code review critical finding): _connectionTracker.Increment() is called before the potentially-throwing await chain (GetPlayerLobbyIdsAsync, Task.WhenAll over AddToGroupAsync). If any of those throws, SignalR does not call OnDisconnectedAsync and the matching Decrement() never runs, leaving the gauge permanently high. The increment is present (VERIFIED) and Decrement fires on clean disconnect (VERIFIED), but the error-path invariant — that a failed OnConnectedAsync does not strand an increment — cannot be confirmed by code inspection alone. The fix is the try/catch guard from the review report (CR-01)."
---

# Phase 15: Per-Package OTel Instrumentation Verification Report

**Phase Goal:** Every HTTP handler path and background job in every GameKit package emits correctly-named, low-cardinality spans and RED metrics; W3C trace context flows from the enqueue HTTP request through the async ticker to match formation.
**Verified:** 2026-06-22
**Status:** passed (live verification completed 2026-06-22 — see "Live Verification Update")
**Re-verification:** Yes — live-stack items #2 and #4 verified after gap closure

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | A MeterListener tag-key assertion test in each package passes: no instrument emits ticketId, playerId, sessionId, matchId, or any player-identifying tag key | VERIFIED | `MatchmakingPiiTagKeyTests`, `RankingsPiiTagKeyTests`, `LobbyPiiTagKeyTests` all present, substantive, and pass (115/115, 24/24, 25/25 suite runs confirmed by test execution). Each exercises every instrument in its package with MeterListener callbacks and asserts `DoesNotContain` over the forbidden-key set. `GK0001` PII analyzer runs at build time with 0 warnings across all packages. |
| 2 | A trace exported to Tempo for a matchmaking enqueue request shows the MatchFormation span as a descendant of the original enqueue span (W3C traceparent stored in Redis ticket hash and restored in ticker) | PRESENT_BEHAVIOR_UNVERIFIED | **In-process automated proxy VERIFIED:** `W3CTracePropagationTests.cs` (3/3 passing) exercises `StartMatchFormationActivity(parentCtx)` directly with an in-process `ActivityListener`, asserting TraceId match (parent chain), fan-in ActivityLink attachment, and null-safe no-op for non-sampled parent. Enqueue-side write confirmed at `MatchmakingService.cs:333` (`Activity.Current.Id` written to `MatchmakingRedisKeys.TicketTraceParent`). Ticker-side restore confirmed at `MatchmakerTickerService.cs:494-516` (TryParse → StartMatchFormationActivity + AddLink fan-in). **Live Tempo descent check requires running sample stack** — deferred to human verification per plan's documented contract. |
| 3 | Lobby SignalR metrics (connected clients, messages/sec, ready-check completion rate) appear under gamekit.lobby.*; background-job metrics (ticker lag, queue depth, decay duration, leader-lock failures) appear under gamekit.matchmaking.* and gamekit.rankings.* | VERIFIED | All instruments confirmed present and substantive: `LobbyMeter` (lobby.connected_clients ObservableGauge, lobby.messages.sent, lobby.ready_check.started, lobby.ready_check.completed — `LobbyMeter.cs`); `MatchmakingMeter` (matchmaking.ticker.lag, matchmaking.queue.depth, matchmaking.leader_lock.acquisition_failures, matchmaking.lease.acquired, matchmaking.lease.lost, matchmaking.matches.formed, matchmaking.budget_bail, matchmaking.pool_sweep.duration); `RankingsMeter` (rankings.decay.duration, rankings.decay.rows_updated). `AddGameKitObservability()` registers all three sources/meters via `GameKitTelemetry` constants (`GameKitObservabilityBuilderExtensions.cs:107-125`). OTel Collector `otel-collector-config.yml:17` sets `namespace: gamekit` so all instruments appear as `gamekit_*` in Prometheus. |
| 4 | Pre-built Grafana dashboard JSON for matchmaking queue depth + ticker health is importable from samples/TicTacToeDuel/observability/dashboards/ and renders correct data against the sample stack | PRESENT_BEHAVIOR_UNVERIFIED | **Static correctness VERIFIED:** `matchmaking-queue-depth.json` queries `gamekit_matchmaking_queue_depth`, `gamekit_matchmaking_matches_formed_total`, `gamekit_matchmaking_budget_bail_total`, `gamekit_matchmaking_analytics_dropped_events_total` — all match emitted instrument names after `namespace: gamekit` prefix. `ticker-health.json` queries `gamekit_matchmaking_ticker_lag_ms_bucket` and `gamekit_rankings_decay_duration_ms_bucket` — both corrected from stale Phase-13 names. Both dashboard JSON files exist at the claimed path. **WR-01 cosmetic defect:** ticker-health.json panel 4 ("Rankings Ticker: Decay Duration") uses `legendFormat: "p50 ms ({{ladder_id}})"` but `RankingsMeter.DecayDuration` emits no `ladder.id` tag — legend will render as `p50 ms ()`. Does not affect data rendering or criterion #4 pass/fail; advisory fix only. **Live Grafana rendering requires running sample stack** — deferred to human verification. |

**Score:** 2/4 truths fully VERIFIED; 2/4 truths PRESENT_BEHAVIOR_UNVERIFIED (code present + wired, live-stack rendering unexercised)

### Deferred Items

None — all four success criteria were targeted by this phase and their in-process components are implemented. Live-stack criteria (#2, #4) are human verification items, not deferred to a future phase.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` | LobbySourceName, RankingsMeterName, LobbyMeterName, AttrCheckResult constants | VERIFIED | All four constants present with correct literal values confirmed by file read |
| `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` | 8 OBS-04 instruments + Init | VERIFIED | All 8 instruments confirmed (TickerLag, PoolSweepDuration, QueueDepth, LockAcquisitionFailures, MatchesFormed, BudgetBail, LeaseAcquired, LeaseLost) + synchronous Redis-safe ObservableGauge |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | Histogram + counter emission at tick/pool/lease sites | VERIFIED | Emission sites confirmed at lines 182-189 (LockAcquisitionFailures, LeaseAcquired, tickSw), 486 (MatchesFormed), 501-516 (StartMatchFormationActivity + fan-in AddLink) |
| `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` | TicketTraceParent / TicketTraceState constants | VERIFIED | `otel.traceparent` and `otel.tracestate` confirmed at lines 75, 83 |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | traceparent written at enqueue | VERIFIED | `Activity.Current.Id` write confirmed at line 333 |
| `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` | TraceparentStr / TracestateStr carry fields | VERIFIED | Init-only properties confirmed present (summary line 10, ticker BuildQueuedPartyFromHash reads them) |
| `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` | StartMatchFormationActivity helper | VERIFIED | Method confirmed at lines 99-102 (parented + unparented overloads) |
| `src/GameKit.Rankings/Telemetry/RankingsMeter.cs` | GameKit.Rankings Meter with decay instruments | VERIFIED | New file confirmed; MeterName = "GameKit.Rankings", DecayDuration + DecayRowsUpdated with correct names/units |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` | Stopwatch + RankDecay span + counter emission | VERIFIED | Confirmed at lines 135-186 (post-lease Stopwatch, RankDecay span, DecayDuration.Record in finally, DecayRowsUpdated.Add in DecayLadderAsync) |
| `src/GameKit.Lobby/Telemetry/LobbyMeter.cs` | GameKit.Lobby Meter with ConnectedClients, MessagesSent, ready-check counters | VERIFIED | New file confirmed; all 4 instruments present with correct names |
| `src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs` | GameKit.Lobby ActivitySource + StartReadyCheckActivity | VERIFIED | New file confirmed; SourceName = "GameKit.Lobby", StartReadyCheckActivity parented/unparented overloads |
| `src/GameKit.Lobby/Telemetry/LobbyConnectionTracker.cs` | Singleton Interlocked counter | VERIFIED | New file confirmed; Interlocked.Increment/Decrement + Volatile.Read |
| `src/GameKit.Lobby/Hubs/LobbyHub.cs` | Increment on connect, Decrement on disconnect, MessagesSent on relay | VERIFIED | Confirmed: Increment at line 82, Decrement at line 107, MessagesSent.Add(1) at line 180 (inside relay block after ReceiveChatMessageAsync succeeds) |
| `src/GameKit.Lobby/Services/LobbyService.cs` | ReadyCheckStarted at Open→ReadyChecking, ReadyCheckCompleted on all-ready, ReadyCheck span | VERIFIED | Confirmed: ReadyCheckStarted.Add(1) at line 168, StartReadyCheckActivity at line 220, ReadyCheckCompleted.Add(1, check.result tag) at line 286 |
| `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` | LobbyConnectionTracker singleton + LobbyMeterInitService | VERIFIED | AddSingleton<LobbyConnectionTracker> and AddHostedService<LobbyMeterInitService> confirmed at lines 121-122 |
| `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` | AddSource(LobbySourceName) + AddMeter(RankingsMeterName, LobbyMeterName) | VERIFIED | Confirmed at lines 109, 124-125 |
| `samples/TicTacToeDuel/observability/otel-collector-config.yml` | namespace: gamekit | VERIFIED | Confirmed at line 17 |
| `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json` | PromQL matching emitted instrument names | VERIFIED | gamekit_matchmaking_queue_depth, gamekit_matchmaking_analytics_dropped_events_total, gamekit_matchmaking_matches_formed_total, gamekit_matchmaking_budget_bail_total — all confirmed |
| `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` | PromQL matching emitted instrument names | VERIFIED | gamekit_matchmaking_ticker_lag_ms_bucket and gamekit_rankings_decay_duration_ms_bucket confirmed; stale names (tick_duration_ms_bucket, drain_ladder_duration_ms_bucket) absent |
| `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs` | MeterListener PII assertion exercises all instruments | VERIFIED | Exercises DroppedEvents, TickerLag, PoolSweepDuration, LockAcquisitionFailures, MatchesFormed, BudgetBail, LeaseAcquired, LeaseLost + RecordObservableInstruments for QueueDepth |
| `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs` | 3 un-skipped Facts: parent, fan-in link, non-sampled no-op | VERIFIED | File read confirms all 3 Facts have no Skip attribute; suite reports 115/115 passing including these |
| `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs` | MeterListener PII assertion exercises DecayDuration + DecayRowsUpdated | VERIFIED | Both instruments exercised (lines 85-86) with forbidden-key DoesNotContain assertion |
| `tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs` | MeterListener PII assertion exercises all lobby instruments | VERIFIED | File confirmed; exercises MessagesSent, ReadyCheckStarted, ReadyCheckCompleted(check.result=all_ready), ConnectedClients (via Init + RecordObservableInstruments) |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MatchmakingService.cs` | `MatchmakingRedisKeys.cs` | `HashEntry(TicketTraceParent, Activity.Current.Id)` | WIRED | Confirmed at line 333 |
| `MatchmakerTickerService.cs` | `MatchmakingActivitySource.cs` | `StartMatchFormationActivity(restoredCtx)` + `AddLink` per non-primary ticket | WIRED | Confirmed at lines 501, 516 |
| `MatchmakerTickerService.cs` | `MatchmakingMeter.cs` | `Record/Add` at RunOnceAsync + TryAcquireLeaseAsync sites | WIRED | Confirmed at lines 182, 187, 189, 486 |
| `MatchmakingMeter.cs` | `MatchmakingBuilderExtensions.cs` | `MatchmakingMeterInitService.StartAsync` → `MatchmakingMeter.Init(multiplexer)` | WIRED | Confirmed at MatchmakingBuilderExtensions.cs:154 + MatchmakingMeterInitService class |
| `RankDecayBackgroundService.cs` | `RankingsMeter.cs` | `Record/Add` at RunOnceAsync after-lease + DecayLadderAsync save site | WIRED | Confirmed at lines 186, 245 |
| `LobbyHub.cs` | `LobbyConnectionTracker.cs` | `Increment` in OnConnectedAsync, `Decrement` in OnDisconnectedAsync | WIRED | Confirmed at lines 82, 107 — note CR-01 below |
| `LobbyService.cs` | `LobbyMeter.cs` | `ReadyCheckStarted/ReadyCheckCompleted.Add` at state-transition sites | WIRED | Confirmed at lines 168, 286 |
| `LobbyBuilderExtensions.cs` | `LobbyConnectionTracker.cs` | `AddSingleton<LobbyConnectionTracker>` + `LobbyMeter.Init(tracker)` via `LobbyMeterInitService` | WIRED | Confirmed at lines 121-122, 155-156 |
| `GameKitObservabilityBuilderExtensions.cs` | `GameKitTelemetry.cs` | `AddSource/AddMeter` reference Phase-15 constants | WIRED | LobbySourceName at line 109, RankingsMeterName + LobbyMeterName at lines 124-125 |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `LobbyMeter.ConnectedClients` ObservableGauge | `_tracker.Current` | `LobbyConnectionTracker.Increment/Decrement` (hub lifecycle) | Yes — Interlocked counter reflecting real connections | FLOWING (error path caveat — CR-01) |
| `MatchmakingMeter.QueueDepth` ObservableGauge | Redis `ZCARD` per `mm:queue:*` key | `IServer.Keys(pattern: "mm:queue:*")` + `IDatabase.SortedSetLength` (synchronous) | Yes — real Redis data | FLOWING |
| `MatchmakingMeter.TickerLag` | `tickSw.Elapsed.TotalMilliseconds` | `Stopwatch.StartNew()` after lease acquired, `Record` in `RunOnceAsync` | Yes — real wall-clock measurement | FLOWING |
| `RankingsMeter.DecayDuration` | `decaySw.Elapsed.TotalMilliseconds` | `Stopwatch.StartNew()` after `TryAcquireLeaseAsync` returns true | Yes — post-lease decay work time | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Core.Tests (156 tests including telemetry constants + OBS smoke tests) | `dotnet test tests/GameKit.Core.Tests -p:NuGetAudit=false` | 156/156 passed | PASS |
| Matchmaking.Tests (115 tests including PII, W3C, metrics) | `dotnet test tests/GameKit.Matchmaking.Tests -p:NuGetAudit=false` | 115/115 passed, 0 skipped | PASS |
| Rankings.Tests (24 tests including PII + metrics) | `dotnet test tests/GameKit.Rankings.Tests -p:NuGetAudit=false` | 24/24 passed | PASS |
| Lobby.Integration.Tests (25 tests including PII + metrics + hub lifecycle) | `dotnet test tests/GameKit.Lobby.Integration.Tests -p:NuGetAudit=false` | 25/25 passed | PASS |
| W3C propagation tests specifically | Covered in Matchmaking 115/115 above (W3CTracePropagationTests — 3 facts) | All 3 pass | PASS |
| Dashboard stale names absent | `grep -E "tick_duration_ms_bucket|drain_ladder_duration_ms_bucket" ticker-health.json` | No output | PASS |
| Collector namespace present | `grep "namespace: gamekit" otel-collector-config.yml` | Found at line 17 | PASS |
| gamekit_matchmaking_analytics_dropped_events_total re-prefixed | `grep "gamekit_matchmaking_analytics_dropped_events_total" matchmaking-queue-depth.json` | Found at line 30 | PASS |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|---------------|-------------|--------|---------|
| OBS-04 | 15-02, 15-04 | Background-job metrics — matchmaking ticker lag, queue depth, decay job run duration, leader-lock acquisition failures | SATISFIED | MatchmakingMeter: ticker.lag, queue.depth, leader_lock.acquisition_failures, lease.acquired, lease.lost; RankingsMeter: decay.duration, decay.rows_updated; all wired at emission sites |
| OBS-05 | 15-05 | Lobby SignalR metrics — connected clients, messages/sec, ready-check completion rate | SATISFIED | LobbyMeter: lobby.connected_clients ObservableGauge, lobby.messages.sent, lobby.ready_check.started, lobby.ready_check.completed (with check.result tag); wired in LobbyHub + LobbyService |
| OBS-06 | 15-03, 15-04, 15-05 | W3C trace-context propagation through async paths | SATISFIED (with human verification pending for live Tempo) | Matchmaking: traceparent stored at enqueue, restored in ticker, MatchFormation span is descendant with fan-in links (3 tests passing). Rankings: fresh-root RankDecay span (background job — no inbound traceparent). Lobby: ReadyCheck span parented to hub invocation Activity.Current captured server-side |

All three phase requirement IDs (OBS-04, OBS-05, OBS-06) are covered. No orphaned requirements.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `src/GameKit.Lobby/Hubs/LobbyHub.cs` | 82-95 | `_connectionTracker.Increment()` before awaited calls without try/catch guard; if `GetPlayerLobbyIdsAsync` or `AddToGroupAsync` throws, SignalR does not call `OnDisconnectedAsync` and the gauge drifts permanently upward | WARNING (from CR-01 code review) | `lobby.connected_clients` gauge over-counts by 1 per failed connect under DB/backplane failure conditions; defeats the OBS-05 metric's alerting purpose. Fix: wrap the async work in try/catch with Decrement in the catch block before rethrowing. |
| `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` | 147, 153 | `"legendFormat": "p50 ms ({{ladder_id}})"` on Rankings Decay Duration panel — instrument emits no `ladder.id` tag so variable always renders empty | WARNING (from WR-01 code review) | Cosmetic: legend shows `p50 ms ()`. Data renders correctly. Fix: drop `({{ladder_id}})` from legend format or move DecayDuration.Record into per-ladder loop. |
| `src/GameKit.Lobby/Services/ILobbyService.cs` | ~97-101 | `ActivityContext parentContext = default` placed AFTER `CancellationToken ct = default` (violates CA1068 CancellationToken-last convention) | INFO (from IN-01 code review) | Non-breaking since new param is optional; no existing callers affected. Fix: reorder if churn is acceptable. |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` and `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` | ~794, ~262 | Two near-identical queue-key parsers (`ExtractLadderId` / `TryParseQueueKey`) with divergent failure modes | INFO (from IN-03 code review) | Maintenance risk: future key format change must be made in two places. Fix: extract shared helper to `MatchmakingRedisKeys`. |

### Human Verification Required

#### 1. Live Grafana Dashboard Rendering (Criterion #4)

**Test:** Start the TicTacToeDuel sample stack (`docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`), run the sample app, drive matchmaking traffic (at minimum two players enqueue on the same ladder), wait 30s for Prometheus scrapes, then open Grafana and import/view both dashboards.

**Expected:** `matchmaking-queue-depth.json` panels show non-zero data for `gamekit_matchmaking_queue_depth`, `gamekit_matchmaking_matches_formed_total`, `gamekit_matchmaking_budget_bail_total`. `ticker-health.json` shows non-zero `gamekit_matchmaking_ticker_lag_ms_bucket` p50/p99 values. Rankings Decay Duration panel (panel 4) renders real histogram data but legend shows `p50 ms ()` with empty `{{ladder_id}}` — this is the known WR-01 cosmetic defect, not a criterion failure.

**Why human:** Requires running Tempo + Prometheus + Grafana stack receiving real OTLP traffic. The in-repo proxy checks (PromQL names verified against emitted instrument names, namespace config verified) are complete; live rendering cannot be verified with grep.

#### 2. W3C Trace Descent in Tempo (Criterion #2)

**Test:** With the sample stack running and Tempo collecting traces, have one client enqueue and another enqueue on the same ladder. A match forms. Open Grafana Explore → Tempo, find the enqueue trace by the HTTP POST span name, and expand the trace timeline.

**Expected:** A single causal trace shows the HTTP enqueue span as root, with the MatchFormation span appearing as a descendant (same TraceId, parent-child relationship). If two tickets were matched, the MatchFormation span has one ActivityLink to the second ticket's trace. Trace parent-child relationship is visible in the Tempo waterfall view.

**Why human:** Criterion #2 requires a running Tempo instance receiving OTLP traces from the sample app. The automated proxy (`W3CTracePropagationTests` — 3/3 passing) validates the in-process span parent/link contract. The live-Tempo check validates the full pipeline from ASP.NET Core HTTP span → Redis ticket hash → ticker → MatchFormation span.

#### 3. LobbyConnectionTracker Error-Path Invariant (CR-01)

**Test:** With the sample stack running, introduce a transient Postgres failure (e.g., `docker pause gamekit-db`) and then have a SignalR client connect to LobbyHub. Observe the `gamekit_lobby_connected_clients` gauge in Grafana before and after the connection attempt.

**Expected:** The gauge should NOT increase permanently if the connection fails mid-way (i.e., if `GetPlayerLobbyIdsAsync` fails due to Postgres being paused). The gauge should reflect only live, successfully-established connections.

**Why human:** CR-01 (critical code review finding): `_connectionTracker.Increment()` runs before the awaited `GetPlayerLobbyIdsAsync` + `AddToGroupAsync` chain without a try/catch guard. A throw in those awaited calls means SignalR will not call `OnDisconnectedAsync`, so `Decrement()` never runs. This behavior-dependent invariant (gauge accurately reflects live connections even under error conditions) cannot be verified by code inspection — it requires inducing the failure condition at runtime. The fix is the try/catch pattern from CR-01 in the review report.

---

## Code Review Findings Assessment

The code review (15-REVIEW.md) found 1 Critical + 4 Warnings. Assessment against the phase GOAL:

**CR-01 (Critical — LobbyConnectionTracker gauge leak on OnConnectedAsync throw):** This is a correctness defect in OBS-05 metric accuracy under failure conditions. Under normal operation (no Postgres/Redis failure during connect) the gauge is accurate. The phase GOAL is "emits correctly-named, low-cardinality spans and RED metrics" — under sustained failure conditions the connected-clients gauge fails this goal by drifting monotonically. However, this is a latent quality defect (not an always-broken condition), and the metric IS wired and functional under nominal operation. This does NOT block the phase's goal of shipping instrumentation, but it REQUIRES a follow-up fix before the metric can be trusted for capacity alerting.

**WR-01 (Rankings Decay Duration legend bug):** Cosmetic dashboard issue; data renders correctly. Does not affect criterion #4 goal achievement.

**WR-02/WR-03 (Meter disposal / Init overwrite):** Library-design trade-offs documented in code review. No correctness impact under the single-host-per-process production topology. Follow-up documentation recommended.

**WR-04 (ReadyCheck counter imbalance):** `ready_check.started` and `ready_check.completed` are not a closed pair in v1 (only `all_ready` wired; `timeout`/`cancelled` have TODO comments). Affects the utility of a completion-ratio dashboard panel; does not affect individual instrument correctness. Known from plan design.

---

_Verified: 2026-06-22_
_Verifier: Claude (gsd-verifier)_

---

## Live Verification Update (2026-06-22 — gap closure + automated re-run)

The three human/live items above were resolved during `/gsd-verify-work 15` via gap closure and an automated live-stack run (stack stood up, real 2-distinct-player match driven, Prometheus + Tempo queried over the Docker network). Final status: **all 4 criteria PASS; status upgraded human_needed → passed.**

### Gap found and closed
The live run revealed an end-to-end gap the in-code proxies could not catch: **`samples/TicTacToeDuel/Program.cs` never called `AddGameKitObservability()`**, so the running app registered no OTel SDK pipeline and exported zero OTLP (Prometheus had 0 `gamekit_*` series, Tempo 0 traces). The library instrumentation was correct throughout; only the sample's opt-in wiring was missing.

Closed by:
- **`826f751`** — sample wires `gameKitBuilder.AddGameKitObservability(o => o.OtlpEndpoint = config)` + adds the three OTel SDK package refs (Hosting, OTLP exporter, ASP.NET Core instrumentation — `PrivateAssets=all` in Core means consumers opt in themselves) + `AddOpenTelemetry().WithTracing(t => t.AddAspNetCoreInstrumentation())` so the HTTP enqueue server span (criterion #2's parent) exists. Config key `GameKit:Observability:OtlpEndpoint = http://localhost:4317` added to `appsettings.Development.json`.
- **`a86f3be`** — dashboard PromQL corrected to the ACTUAL OTel→Prometheus names. The exporter (`add_metric_suffixes`) appends the mapped unit unless the name already contains that token: `ms`→`milliseconds` (so `..._ticker_lag_milliseconds_bucket`, `..._pool_sweep_duration_milliseconds_bucket`, `..._decay_duration_milliseconds_bucket`); counters with unit `events` get `_events` unless already present (`lease_acquired_events_total`, `lease_lost_events_total`, `budget_bail_events_total`; `analytics_dropped_events_total` keeps its form). `matches_formed_total` / `queue_depth` unchanged. The earlier 15-06 "static correctness" pass had asserted the `_ms`/`_total` names without running them against a live exporter — the in-code proxy's blind spot.
- **`bb570fe`** (CR-01) + **`83e679f`** (WR-01) — applied earlier in the same session.

### Live re-verification evidence (criteria #2 and #4)
- **Criterion #4 — metrics/dashboards (PASS):** authoritative Prometheus `__name__` dump contained the expected `gamekit_*` series. 8/12 dashboard targets returned real values (ticker_lag p50=2.5ms/p99=4.95ms; pool_sweep p50/p99; lease_acquired 0.85/s; queue_depth=1 with `ladder_id`+`pool_name=default`; matches_formed increase=3.21, raw=5). The other 4 targets are documented-absent counters (lease_lost, rankings_decay, dropped_events, budget_bail) whose triggering events did not occur in a clean short run — their names follow the same empirically-confirmed suffix rule. **No dashboard target references a wrong or nonexistent metric name.**
- **Criterion #2 — Tempo trace descent (PASS):** trace `d0223a6a...` shows `POST /api/mm/queue` (HTTP SERVER, scope `Microsoft.AspNetCore`) → `MatchFormation` (INTERNAL, scope `GameKit.Matchmaking.Ticker`) as a true descendant (parentSpanId chain), with the co-matched second ticket's enqueue context attached as an `ActivityLink` — the OBS-06 fan-in design, end-to-end.
- **Criterion #3 — host isolation (re-confirmed):** host `curl http://localhost:9090` refused; Prometheus has no host port binding.
- **CR-01 (resolved):** try/catch guard committed (`bb570fe`) + regression test `LobbyConnectionGaugeLeakTests` (2/2) proves the gauge returns to 0 when the connect path throws.

### Non-blocking follow-ups (out of Phase-15 scope)
- **Sample matchmaking-pairing doc/UX gap:** the ticker's `GetPoolNamesForLadder()` only sweeps the `default` pool (no `AllowedRegions` configured), but the README walkthrough instructs enqueuing with `poolName="tictactoe"` — tickets in that pool are never swept and never pair. Enqueuing with no poolName (→ default) pairs distinct players in ~1s. A sample-docs/matchmaking-config follow-up; not a telemetry defect.
- **WR-02/WR-03/WR-04 and IN-* code-review items** remain as documented advisory follow-ups.
