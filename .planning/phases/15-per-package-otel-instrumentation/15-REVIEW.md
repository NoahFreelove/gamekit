---
phase: 15-per-package-otel-instrumentation
reviewed: 2026-06-22T00:00:00Z
depth: standard
files_reviewed: 32
files_reviewed_list:
  - samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json
  - samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json
  - samples/TicTacToeDuel/observability/otel-collector-config.yml
  - src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs
  - src/GameKit.Core/Telemetry/GameKitTelemetry.cs
  - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
  - src/GameKit.Lobby/Hubs/LobbyHub.cs
  - src/GameKit.Lobby/Services/ILobbyService.cs
  - src/GameKit.Lobby/Services/LobbyService.cs
  - src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs
  - src/GameKit.Lobby/Telemetry/LobbyConnectionTracker.cs
  - src/GameKit.Lobby/Telemetry/LobbyMeter.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
  - src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs
  - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
  - src/GameKit.Matchmaking/Services/MatchmakingService.cs
  - src/GameKit.Matchmaking/Strategy/QueuedParty.cs
  - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs
  - src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs
  - src/GameKit.Rankings/Services/RankDecayBackgroundService.cs
  - src/GameKit.Rankings/Telemetry/RankingsMeter.cs
  - tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs
  - tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs
  - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyMetricsTests.cs
  - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs
  - tests/GameKit.Matchmaking.Tests/MatchmakingMeterCollection.cs
  - tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingMetricsTests.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs
  - tests/GameKit.Rankings.Tests/Telemetry/RankingsMetricsTests.cs
  - tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs
findings:
  critical: 1
  warning: 4
  info: 4
  total: 9
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-06-22
**Depth:** standard
**Files Reviewed:** 32
**Status:** issues_found

## Summary

Phase 15 wires per-package OpenTelemetry instrumentation (OBS-04 metrics, OBS-05 SignalR
metrics, OBS-06 W3C trace propagation) across Matchmaking, Rankings, Lobby, and Core. The
PII discipline is strong: every metric/trace tag uses low-cardinality keys (`ladder.id`,
`pool.name`, `check.result`, `reason`), the forbidden-key runtime tests are comprehensive,
and no player id / username / IP leaks into any tag key or value that I could find. The
ObservableGauge callbacks are correctly synchronous and Redis-error-safe (`yield break` /
`continue` on `RedisException`), the `Interlocked`/`Volatile` connection counter is
thread-correct in isolation, and the W3C traceparent restore path honours sampling flags
by treating a null `MatchFormation` activity as a no-op.

The review surfaced one correctness defect that causes the connected-clients gauge to drift
permanently upward when `OnConnectedAsync` throws (CR-01), plus a dashboard panel whose
legend variable will never populate, an instrument-disposal gap that affects scrape
correctness on host shutdown, and several smaller robustness / convention issues. The
matchmaking ticker hot path is not measurably degraded — the new instruments are
`Stopwatch` reads and `Counter.Add` calls outside the per-candidate inner loop.

## Critical Issues

### CR-01: `LobbyConnectionTracker` leaks upward when `OnConnectedAsync` throws after Increment

**File:** `src/GameKit.Lobby/Hubs/LobbyHub.cs:79-96`
**Issue:** `OnConnectedAsync` calls `_connectionTracker.Increment()` as its first statement
(line 82), then `await`s `_lobby.GetPlayerLobbyIdsAsync(...)` (line 88) and
`Task.WhenAll(addTasks)` over `Groups.AddToGroupAsync(...)` (line 92). If any of those awaits
throws — a transient Postgres failure in `GetPlayerLobbyIdsAsync`, a Redis-backplane error
in `AddToGroupAsync`, or `Context.ConnectionAborted` firing mid-connect — SignalR aborts the
connection and **does not invoke `OnDisconnectedAsync`** when `OnConnectedAsync` throws. The
matching `_connectionTracker.Decrement()` (line 107) therefore never runs, so the
`lobby.connected_clients` ObservableGauge over-counts by one per failed connect. Under any
sustained connect-failure condition (DB blip, backplane reconnect) the gauge drifts
monotonically upward and never recovers without a process restart, defeating the OBS-05
metric's purpose and potentially tripping capacity alerts.

**Fix:** Increment only after the connect work that can throw has succeeded, or wrap the body
so a throw decrements before rethrowing:

```csharp
public override async Task OnConnectedAsync()
{
    _connectionTracker.Increment();
    try
    {
        var playerId = GetPlayerIdOrNull();
        if (playerId.HasValue)
        {
            var lobbyIds = await _lobby.GetPlayerLobbyIdsAsync(playerId.Value, Context.ConnectionAborted)
                .ConfigureAwait(false);
            var addTasks = lobbyIds.Select(id =>
                Groups.AddToGroupAsync(Context.ConnectionId, $"lobby:{id}", Context.ConnectionAborted));
            await Task.WhenAll(addTasks).ConfigureAwait(false);
        }
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
    catch
    {
        // SignalR does not call OnDisconnectedAsync when OnConnectedAsync throws — undo the
        // increment here so the gauge does not drift upward.
        _connectionTracker.Decrement();
        throw;
    }
}
```

## Warnings

### WR-01: Rankings decay dashboard panel uses `{{ladder_id}}` legend but the metric emits no `ladder.id` tag

**File:** `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json:147,153`
(see also `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs:186`)
**Issue:** Panel 4 ("Rankings Ticker: Decay Duration") sets
`"legendFormat": "p50 ms ({{ladder_id}})"` / `"p99 ms ({{ladder_id}})"`. But
`RankingsMeter.DecayDuration.Record(...)` is called with **no tags** —
`RankDecayBackgroundService` records the histogram once per run in the `finally` block, not
per ladder, and deliberately attaches no `ladder.id` (T-15-04-PII keeps it tag-free). The
`{{ladder_id}}` template variable will always render empty, producing a misleading legend
like `p50 ms ()`. The matchmaking pool-sweep panel (lines 111/117) has the same legend but
there the metric *does* carry `ladder.id`, so only the rankings panel is wrong.

**Fix:** Drop the `({{ladder_id}})` suffix from panel 4's two `legendFormat` strings (e.g.
`"p50 ms"` / `"p99 ms"`), or, if per-ladder decay timing is genuinely wanted, move the
`DecayDuration.Record` into `DecayLadderAsync` and tag it with `ladder.id` (note this changes
the metric's cardinality and timing semantics — the current single-run timing is intentional
per Pitfall 5).

### WR-02: Telemetry `Meter` / `ActivitySource` singletons are never disposed

**File:** `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs:53`,
`src/GameKit.Rankings/Telemetry/RankingsMeter.cs:48`,
`src/GameKit.Lobby/Telemetry/LobbyMeter.cs:43`,
`src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs:40`,
`src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs:45`
**Issue:** Every `Meter` and `ActivitySource` is a `static readonly` field with no disposal
path. `Meter` implements `IDisposable`; disposing it deregisters its instruments from the
process-wide `MeterListener`/OTel pipeline. For a single long-lived host this is benign, but
GameKit ships as a library and the integration-test suites repeatedly build and tear down
hosts in one process. Because the static `Meter` is process-global and never disposed, its
`ObservableGauge` callbacks (`ObserveConnectedClients`, `ObserveQueueDepths`) remain
registered after a host is torn down and will be invoked by any later `MeterListener` in the
same process — reading a `_tracker`/`_multiplexer` that now points at a disposed Redis
multiplexer or a stale tracker from a prior host. This is the same root cause the test
projects work around with `DisableParallelization` collections, but production multi-host
scenarios (and test ordering) are not covered.

**Fix:** This is an accepted trade-off for static-singleton telemetry in many libraries, but
it should be documented explicitly (a remark on each `Meter` field stating it is intentionally
process-lifetime and never disposed), OR the `ObservableGauge` callbacks should be hardened to
tolerate a disposed/replaced backing reference (the matchmaking callback already swallows
`RedisException`, but `LobbyMeter.ObserveConnectedClients` reads `_tracker?.Current` with no
guard against a stale tracker). At minimum, add a code comment recording the
never-disposed decision so a future maintainer does not "fix" it by disposing mid-process.

### WR-03: `MatchmakingMeter.Init` / `LobbyMeter.Init` silently overwrite the backing reference on re-registration

**File:** `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs:74-78`,
`src/GameKit.Lobby/Telemetry/LobbyMeter.cs:60-64`
**Issue:** `Init` assigns a process-static field (`_multiplexer` / `_tracker`). The init is
driven by an `IHostedService` (`MatchmakingMeterInitService`, `LobbyMeterInitService`). If a
consumer calls `AddMatchmaking()`/`AddLobby()` and stands up two hosts in the same process
(or a test does), the second host's `StartAsync` overwrites the static reference with its own
multiplexer/tracker, so the first host's `QueueDepth`/`ConnectedClients` gauge now scrapes the
*second* host's Redis/tracker. There is no last-writer detection or warning. The write is also
a plain field assignment (not `Volatile.Write`/`Interlocked.Exchange`), so the
ObservableGauge callback thread is not guaranteed to observe the new value promptly — though
in practice the OTel scrape happens long after `StartAsync`, so this is the lesser concern.

**Fix:** Document the single-host-per-process assumption on `Init`, and use
`Volatile.Write(ref _multiplexer, multiplexer)` to pair with the callback read. If
multi-host is a real scenario, the gauge backing reference cannot be a static singleton — it
would need to be per-`Meter`-instance, which conflicts with the static-`Meter` design (WR-02).

### WR-04: `ready_check.started` and `ready_check.completed` counters are structurally unbalanced

**File:** `src/GameKit.Lobby/Services/LobbyService.cs:168,284-289`
**Issue:** `ReadyCheckStarted` is incremented on every Open→ReadyChecking transition (line
168), but `ReadyCheckCompleted` is only ever incremented with `check.result="all_ready"`
(line 286) — the `"timeout"` and `"cancelled"` result values are acknowledged as unimplemented
(TODO at line 283). A lobby that enters ReadyChecking but never reaches all-ready (a player
leaves, the lobby is abandoned) increments `started` with no matching `completed`. Any
dashboard or alert computing a completion ratio (`completed / started`) will read
systematically below 1.0 and an operator cannot distinguish "abandoned ready-checks" from
"instrumentation gap." This is a metric-correctness limitation, not a crash.

**Fix:** Either emit `ReadyCheckCompleted` with `check.result="cancelled"`/`"timeout"` at the
abandonment/teardown sites (preferred — closes the TODO), or clearly annotate the metric
description that `started` and `completed` are not a closed pair in v1 so dashboard authors do
not build ratio alerts on them.

## Info

### IN-01: `MarkReadyAsync` places `ActivityContext` after the `CancellationToken` parameter

**File:** `src/GameKit.Lobby/Services/ILobbyService.cs:97-101`,
`src/GameKit.Lobby/Services/LobbyService.cs:210-214`
**Issue:** The new optional `ActivityContext parentContext = default` is added *after*
`CancellationToken ct = default`. The .NET convention (and analyzer CA1068) is that
`CancellationToken` should be the last parameter. Existing 3-argument callers still bind
correctly because the new parameter is optional, so this is not a breaking change — but it
inverts the conventional order and any future positional call reads awkwardly
(`MarkReadyAsync(id, pid, ct, ctx)`).

**Fix:** If churn is acceptable, reorder to `(lobbyId, playerId, parentContext = default, ct =
default)`. Given the documented non-breaking constraint for existing callers, leaving it and
suppressing CA1068 with a justifying comment is also defensible.

### IN-02: `TicketEvent` analytics payload embeds raw `playerId` (out-of-phase, flagged for awareness)

**File:** `src/GameKit.Matchmaking/Services/MatchmakingService.cs:353-359,450`
**Issue:** The `Queued`/`Cancelled` `TicketEvent.Payload` JSON contains `playerId`. This is
*not* a metric/trace tag (it is a Postgres analytics row body), so it does not violate the
OBS-04/05/07 tag-PII guardrail this phase enforces, and it predates Phase 15. Flagging only so
the PII boundary is explicit: the no-PII rule applies to OTel tags, while analytics row bodies
intentionally carry player ids. No action required for this phase.

**Fix:** None for Phase 15. If a later phase extends OTel coverage to analytics-drain spans,
ensure the payload is not copied verbatim into a span attribute.

### IN-03: `ExtractLadderId` / `TryParseQueueKey` duplicate queue-key parsing with divergent strictness

**File:** `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:794-801`,
`src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs:262-281`
**Issue:** Two near-identical parsers extract the ladder id from `mm:queue:{ladderId}:{pool}`.
`ExtractLadderId` (ticker) splits on `:` and returns `Guid.Empty` on malformed input;
`TryParseQueueKey` (meter gauge) uses a span-based parse and returns `false`. They differ in
failure mode and in pool handling (the meter parser requires a non-empty pool; the ticker
parser ignores the pool entirely). Divergent duplicate parsers tend to drift; a future key
format change must be made in two places with two different conventions.

**Fix:** Extract a single shared helper in `MatchmakingRedisKeys` (e.g.
`TryParseQueueKey(string, out Guid ladderId, out string pool)`) and have both call sites use
it, choosing one consistent malformed-input contract.

### IN-04: `BuildQueuedPartyFromHash` swallows malformed members JSON without any signal

**File:** `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:643-653`
**Issue:** A `JsonException` deserializing the `members` field is caught and silently
discarded ("loud logging on the hot path would blow the budget"). The reasoning is sound for
the hot path, but a ticket whose members JSON is corrupt then matches with an empty member
list, which can skew the rating aggregator / spread checks downstream with no diagnostic at
all. There is no counter or sampled log to surface that corruption is occurring.

**Fix:** Increment a low-cardinality counter (e.g. a `matchmaking.ticket.malformed_members`
counter, no PII tags) on the catch so operators can detect systemic corruption without paying
per-event logging cost on the hot path. Pure-allocation-free `Counter.Add(1)` does not
meaningfully affect the iteration budget.

---

_Reviewed: 2026-06-22_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
