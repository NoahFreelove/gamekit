# Phase 15: Per-Package OTel Instrumentation — Research

**Researched:** 2026-06-22
**Domain:** OpenTelemetry instrumentation in .NET 10 / ASP.NET Core 10 — spans, metrics, W3C trace propagation via Redis, SignalR hub instrumentation
**Confidence:** HIGH (all implementation mechanics derived from first-party .NET and OTel docs patterns and direct codebase inspection; no training-data assumptions for the mechanics section)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**D-01** — Lean on built-in ASP.NET Core instrumentation for HTTP RED — do NOT hand-roll per-endpoint counters. The host's `AddAspNetCoreInstrumentation()` already emits `http.server.request.duration`. GameKit emits its own `gamekit.<package>.*` spans/metrics only at domain + async/background/SignalR boundaries.

**D-02** — Store `traceparent` (and `tracestate` if present) in the ticket hash at enqueue; ticker reads and reconstructs the parent `ActivityContext` so the match-formation span is a descendant of the originating enqueue trace.

**D-03** — Multi-ticket fan-in: parent = first/initiating ticket's restored traceparent; every other co-matched ticket is attached as an OTel span link. One clean parent chain; causal visibility preserved for all participants.

**D-04** — Instrument Matchmaking, Rankings, and Lobby only. Auth/Presence/Core/Admin.UI rely on built-in HTTP instrumentation + existing spans. The `MeterListener` PII tag-key test runs in every package regardless.

**D-05** — Instrument shapes: ObservableGauge for queue depth per pool and connected lobby clients; Histogram for ticker lag and rank-decay job duration; Counter for leader-lock acquisition failures, lobby messages, ready-check started/completed pairs.

**D-06** — Make the existing two matchmaking dashboards (queue depth + ticker health) render real data. Lobby/rankings dashboards are optional nice-to-have.

### Claude's Discretion

- Exact instrument names under `gamekit.<package>.*`
- Histogram bucket boundaries
- ObservableGauge polling cost vs push-on-tick fallback if per-scrape Redis reads prove expensive
- Precise `Telemetry/` class layout for the new Lobby meter + Matchmaking/Rankings additions
- Whether to carry `tracestate` alongside `traceparent`
- How the ticker reconstructs `ActivityContext` from the stored string
- Whether Lobby gets a single Meter+ActivitySource pair or separate

### Deferred Ideas (OUT OF SCOPE)

- Custom instruments for Auth/Presence/Core/Admin.UI
- Lobby + Rankings Grafana dashboards
- Multi-replica leader-churn / SIGTERM-drain / split-brain correctness (Phase 16)
- Shipping the GK0001 PII analyzer to consumers
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| OBS-04 | Background-job metrics — matchmaking ticker lag, queue depth per pool, rank-decay job run duration, leader-lock acquisition failures | §Instrument Recommendations covers instrument type, name, unit, bucket boundaries, and hook site for each metric |
| OBS-05 | Lobby SignalR metrics — connected clients, messages/sec, ready-check completion rate | §Lobby Telemetry covers greenfield Meter+ActivitySource layout, hook sites in LobbyHub, and derived-in-Grafana rate pattern |
| OBS-06 | W3C trace-context propagation through async paths | §W3C Trace Propagation covers exact API path from enqueue write to ticker ActivityContext restoration and span link attachment |
</phase_requirements>

---

## Summary

Phase 15 turns the Phase-13 telemetry foundation — `GameKitTelemetry` constants, `AddGameKitObservability()`, the GK0001 PII analyzer, and the sample Grafana stack — into a system that emits real spans and metrics. Three packages get new instruments: Matchmaking gains ticker-lag and queue-depth metrics plus the W3C trace-propagation path through Redis; Rankings gains a `RankingsMeter` for the decay-job duration histogram; Lobby is greenfield and gets both an `ActivitySource` and a `Meter` built to the Matchmaking pattern.

The single highest-complexity item is the W3C trace propagation design (OBS-06): capturing `Activity.Current`'s `traceparent` string at HTTP enqueue time, persisting it as a Redis hash field alongside the ticket, then reconstructing an `ActivityContext` in the ticker's `ProcessPoolAsync` to parent the match-formation span. The .NET `System.Diagnostics` API does this without taking any OTel SDK dependency in the shipped packages — only in-box types are needed. The fan-in pattern (first ticket is parent, rest are span links) is the idiomatic OTel approach for N→1 merge points and is surfaced through `Activity.AddLink(ActivityLink)`.

The dashboards in `samples/TicTacToeDuel/observability/grafana/dashboards/` already contain the correct Prometheus metric names (derived mechanically from OTel instrument names via the dots→underscores + unit-suffix rules). The implementation simply needs to emit instruments whose names, after Prometheus translation, match what the dashboard panels already query.

**Primary recommendation:** implement the W3C propagation path first (it touches the most files and is the hardest to retrofit), then add the Matchmaking metrics (ticker-lag histogram, queue-depth gauge, lock-failure counter), then the Rankings decay histogram, then the Lobby greenfield Telemetry/ folder. Run the MeterListener PII tag-key test suite last as a cross-cutting validation step.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| HTTP RED metrics (rate, error, latency) | ASP.NET Core built-in (`http.server.request.duration`) | — | D-01: built-in instrumentation covers all GameKit HTTP routes; no bespoke per-endpoint counter |
| Match-formation span + W3C parent chain | Matchmaking BackgroundService (ticker) | — | The causal work is in `ProcessPoolAsync`; the HTTP span is already the built-in root |
| `traceparent` write at enqueue | Matchmaking HTTP handler / Service | — | Enqueue is in `MatchmakingService.EnqueueAsync`; this is where `Activity.Current` is live |
| Ticker-lag histogram | Matchmaking BackgroundService | — | Lag is measured per-tick in `RunOnceAsync`; only meaningful in the ticker, not the HTTP layer |
| Queue-depth gauge (ObservableGauge) | Matchmaking BackgroundService / Meter | Redis | Callback reads Redis `ZCARD` per pool at scrape time |
| Leader-lock acquisition failures counter | Matchmaking BackgroundService / LeaseHelper | — | Failure site is `TryAcquireLeaseAsync` in `MatchmakerLeaseHelper` |
| Rank-decay job duration histogram | Rankings BackgroundService | — | `RankDecayBackgroundService.ExecuteAsync` controls the timed scope |
| Lobby connected-client gauge | Lobby SignalR Hub | — | `OnConnectedAsync` / `OnDisconnectedAsync` are the lifecycle events |
| Lobby message counter | Lobby SignalR Hub | — | `SendChatMessageAsync` is the only message relay path |
| Ready-check started/completed counters | Lobby SignalR Hub + Service | — | `MarkReadyAsync` in `LobbyHub` + `ILobbyService.MarkReadyAsync` transition logic |

---

## W3C Trace Propagation Across Redis (OBS-06, D-02, D-03)

### How it works in .NET System.Diagnostics

The .NET runtime's `System.Diagnostics.Activity` already produces and consumes W3C `traceparent` + `tracestate` without any OTel SDK import. The relevant in-box API surface is:

**Reading the current context at enqueue time** — `Activity.Current` is set by the ASP.NET Core built-in middleware on every inbound HTTP request. To extract the W3C string form:

```csharp
// Source: System.Diagnostics.Activity (in-box, no OTel SDK needed)
// [ASSUMED] — standard .NET API surface; confirmed by CLAUDE.md OTel opt-in requirement

var currentActivity = Activity.Current;
if (currentActivity is not null)
{
    // W3C traceparent: "00-{traceId}-{spanId}-{flags}"
    string traceparent = currentActivity.Id!;          // Already W3C format when W3C propagation is active
    string? tracestate = currentActivity.TraceStateString; // null if not set
}
```

**Storing in the Redis ticket hash** — add two new string fields to the existing `HSET mm:ticket:{id}` call in `MatchmakingService.EnqueueAsync`. New field names follow the existing lowercase-dotted key convention:

```csharp
// Fields to add to MatchmakingRedisKeys
public const string TicketTraceParent = "otel.traceparent";
public const string TicketTraceState  = "otel.tracestate";   // only written when non-null
```

**Reconstructing ActivityContext in the ticker** — `MatchmakerTickerService.ProcessPoolAsync` reads `HGETALL mm:ticket:{id}` already (pipelined). After collecting the hash, parse the stored string:

```csharp
// Source: System.Diagnostics.ActivityContext.TryParse  [ASSUMED]
// ActivityContext.TryParse (not ActivityContext.Parse) is the correct API in .NET 6+
// It is non-throwing on invalid input.

string? storedTraceparent = /* from hash field "otel.traceparent" */;
string? storedTracestate  = /* from hash field "otel.tracestate" */ ;
ActivityContext restoredCtx = default;
bool hasParent = false;

if (storedTraceparent is not null)
{
    hasParent = ActivityContext.TryParse(storedTraceparent, storedTracestate,
        isRemote: true, out restoredCtx);
}
```

**Starting the match-formation span with a parent** — use the three-argument `ActivitySource.StartActivity` overload:

```csharp
// Source: System.Diagnostics.ActivitySource.StartActivity  [ASSUMED]
Activity? matchActivity = null;
if (hasParent)
{
    matchActivity = MatchmakingActivitySource.Source.StartActivity(
        "MatchFormation",
        ActivityKind.Internal,
        restoredCtx);          // ← restored parent context
}
else
{
    matchActivity = MatchmakingActivitySource.Source.StartActivity("MatchFormation");
}
```

**Fan-in span links (D-03)** — when N > 1 tickets are matched, link the non-primary tickets:

```csharp
// First ticket → parent (already set via parentContext above)
// Tickets[1..N-1] → span links (idiomatic OTel fan-in pattern)  [ASSUMED]
foreach (var nonPrimaryTicket in match.MatchedTickets.Skip(1))
{
    if (nonPrimaryTicket.TraceparentStr is not null &&
        ActivityContext.TryParse(nonPrimaryTicket.TraceparentStr, nonPrimaryTicket.TracestateStr,
            isRemote: true, out var linkCtx))
    {
        matchActivity?.AddLink(new ActivityLink(linkCtx));
    }
}
```

**Sampling flag pitfall** — if the parent's `traceparent` encodes `flags=00` (not sampled), `ActivityContext.TryParse` will produce an `ActivityContext` whose `TraceFlags` does not include `Recorded`. `ActivitySource.StartActivity` with a non-recorded parent will return `null` (same as no-listener behaviour) unless the local sampler overrides it. This is correct behaviour — if the originating enqueue was not sampled, the formation span should not be either. The implementation must not `null`-guard away this case; just treat a `null` match-formation span as no-op.

**tracestate** — carry it alongside `traceparent`. The cost is one extra Redis hash field (a string, typically empty or absent). Discarding `tracestate` silently breaks vendor-specific propagation (e.g. Jaeger baggage) — carry it for correctness at zero meaningful cost.

### Rank-decay and lobby ready-check propagation

The same pattern applies:

- `RankDecayBackgroundService`: there is no inbound HTTP request to carry a trace, so no incoming traceparent to restore. The decay job is a background process; start a fresh root span using `RankingsActivitySource.Source.StartActivity("RankDecay")`. Propagation from a triggering request to this background job is not required by any criterion — OBS-06 says "propagated through the rank-decay BackgroundService", which means the decay run itself is traced (not necessarily linked to an inbound request).
- `LobbyHub.MarkReadyAsync`: the ASP.NET Core SignalR hub invocation does populate `Activity.Current` (the `Microsoft.AspNetCore.SignalR.HubActivator` runs under the ASP.NET Core middleware pipeline). Capture `Activity.Current` at the start of `MarkReadyAsync` and pass it as the parent context to the ready-check broadcast span started in `LobbyService`. This links the ready-check trace to the client connection's hub invocation span.

---

## Instrument Recommendations (D-05, OBS-04, OBS-05)

### Matchmaking — additions to `MatchmakingMeter`

| Instrument | Type | Name | Unit | Tags | Rationale |
|------------|------|------|------|------|-----------|
| Ticker lag | Histogram | `matchmaking.ticker.lag` | `ms` | — | Wall-clock duration of `RunOnceAsync` from start to before lease release; p50/p99 surface in dashboard |
| Queue depth per pool | ObservableGauge | `matchmaking.queue.depth` | `tickets` | `pool.name`, `ladder.id` | `ZCARD mm:queue:{ladderId}:{pool}` per pool on scrape |
| Lock acquisition failures | Counter | `matchmaking.leader_lock.acquisition_failures` | `failures` | — | Incremented in `TryAcquireLeaseAsync` when return is `false` AND caller confirms the reason is transient (i.e., another replica holds it vs. Redis error) |
| Matches formed | Counter | `matchmaking.matches.formed` | `matches` | `ladder.id` | Increment in `ProcessPoolAsync` on `AtomicClaimResult.Success` |
| Budget bail | Counter | `matchmaking.ticker.budget_bail` | `events` | `ladder.id` | Increment when `budgetSw.ElapsedMilliseconds >= budgetMs` |
| Lease acquired | Counter | `matchmaking.lease.acquired` | `events` | — | Increment on successful `TryAcquireLeaseAsync` (used by ticker-health dashboard) |
| Lease lost | Counter | `matchmaking.lease.lost` | `events` | — | Increment on `MatcherTickResult.LeaseLost` |
| Pool sweep duration | Histogram | `matchmaking.pool_sweep.duration` | `ms` | `ladder.id` | Duration of each `ProcessPoolAsync` call; used by ticker-health dashboard |

**Histogram bucket boundaries for sub-second latencies** — OTel .NET `Histogram<T>` uses the SDK's `ExplicitBucketHistogramConfiguration`. For ticker-lag (expected 1–50 ms, alert at 50 ms per dashboard thresholds):

```csharp
// Recommended boundaries (ms):
// 1, 5, 10, 20, 30, 40, 50, 75, 100, 200, 500
// These give sub-tick resolution without excessive bucket cardinality.
// [ASSUMED] — bucket values are discretion; the above match the dashboard's 40ms/50ms thresholds
```

For pool-sweep-duration (similar range), use the same boundaries. Configure via `AddMeter` with explicit bucket boundaries in `AddGameKitObservability()` or via the OTel SDK's `View` API; buckets set at the instrument level using `ExplicitBucketHistogramConfiguration` passed to `Meter.CreateHistogram<double>` are the simpler approach:

```csharp
// [ASSUMED] — System.Diagnostics.Metrics API  
public static readonly Histogram<double> TickerLag = Meter.CreateHistogram<double>(
    name: "matchmaking.ticker.lag",
    unit: "ms",
    description: "Wall-clock duration of one MatchmakerTickerService.RunOnceAsync iteration");
```

Explicit bucket boundaries in `System.Diagnostics.Metrics` (in-box) require the OTel SDK to interpret. The histogram instrument itself carries no bucket config — boundaries are a hint applied at the OTel SDK layer. For the shipped GameKit packages (no hard OTel SDK dep), define only the instrument; recommend boundaries in the `AddGameKitObservability()` XML doc so the operator can configure them. The sample stack can pre-configure boundaries in the `AddGameKitObservability()` overload.

### ObservableGauge cost analysis (D-05 — queue depth via Redis)

**Per-scrape cost:** Prometheus default scrape interval is 15–30 s. Each scrape invokes the ObservableGauge callback once. The callback must call `ZCARD mm:queue:{ladderId}:{pool}` for each active pool. For a typical v1 deployment (1–3 ladders × 1–3 pools = 3–9 ZCARD calls), this is 3–9 round-trips to Redis per scrape cycle. At Prometheus default 15 s scrape and a local Redis (~0.1 ms RTT), the cost is <1 ms per scrape — negligible.

**Bounded and safe:** the number of pools is operator-configured and fixed at startup (`GetPoolNamesForLadder` yields `"default"` + AllowedRegions). The callback does not open new connections — it reads `_redis.GetDatabase()` synchronously (StackExchange.Redis `ZCARD` is synchronous via `IDatabase.SortedSetLength`). No async in the callback needed.

**Push-on-tick alternative** — not needed. The ObservableGauge callback approach is correct for this use case. A push-on-tick would require storing the depth in an in-memory `long` field and writing a `Gauge<long>` instead; that is fine but introduces state. ObservableGauge is cleaner and idiomatic for values that are cheap to read on demand.

**Connected lobby clients gauge** — for `gamekit.lobby.connected_clients`: the hub does not maintain a connection count in memory by default. Two options:
1. Maintain an `int` (or `Interlocked` counter) in a singleton service incremented in `OnConnectedAsync` and decremented in `OnDisconnectedAsync`. The `ObservableGauge` callback reads this field — no Redis needed.
2. Use `IHubContext<LobbyHub>` to query groups — not available without a Redis-backed group tracker.

**Recommendation:** option 1, singleton counter. Simple, zero external dependency, correct for single-replica (Phase 15 scope). Document that multi-replica deployments will see per-replica counts rather than a global sum until a cross-replica aggregation approach is chosen.

### Rankings — new `RankingsMeter`

| Instrument | Type | Name | Unit | Tags |
|------------|------|------|------|------|
| Decay job duration | Histogram | `rankings.decay.duration` | `ms` | `ladder.id` (if per-ladder batches are timed) |
| Decay rows updated | Counter | `rankings.decay.rows_updated` | `rows` | — |

**Hook site:** `RankDecayBackgroundService.ExecuteAsync` — wrap each leader-only decay run with a `Stopwatch` started after the lease is acquired and stopped (and recorded) after the Postgres UPDATE commits.

### Lobby — greenfield `LobbyMeter` and `LobbyActivitySource`

| Instrument | Type | Name | Unit | Tags |
|------------|------|------|------|------|
| Connected clients | ObservableGauge | `lobby.connected_clients` | `connections` | — |
| Messages sent | Counter | `lobby.messages.sent` | `messages` | — |
| Ready checks started | Counter | `lobby.ready_check.started` | `checks` | — |
| Ready checks completed | Counter | `lobby.ready_check.completed` | `checks` | `result` (`"all_ready"` / `"timeout"` / `"cancelled"`) |

**Rates derived in Grafana** — `rate(lobby_messages_sent_total[1m])` gives messages/sec; `lobby_ready_check_completed_total{result="all_ready"}` / `lobby_ready_check_started_total` gives the completion rate. Do NOT pre-compute ratios in code (D-05 / Specific Ideas).

**Lobby Telemetry class layout:**

```
src/GameKit.Lobby/Telemetry/
  LobbyActivitySource.cs   — SourceName = "GameKit.Lobby"; StartReadyCheckActivity() typed helper
  LobbyMeter.cs            — MeterName = "GameKit.Lobby"; all Meter instruments as static fields
```

This mirrors the Matchmaking pattern exactly:
- `internal static class LobbyMeter { public const string MeterName = "GameKit.Lobby"; ... }`
- `public static class LobbyActivitySource { public const string SourceName = "GameKit.Lobby"; ... }`
- `InternalsVisibleTo("GameKit.Lobby.Tests")` and `InternalsVisibleTo("GameKit.Lobby.Integration.Tests")` added to `src/GameKit.Lobby/AssemblyInfo.cs`

A **single Meter + single ActivitySource** for Lobby is correct. There is no sub-domain split in Lobby analogous to the Matchmaking/Rankings split (where the ticker source is distinct from the HTTP source). Lobby's SignalR lifecycle and ready-check trace both belong to one domain.

---

## GameKitTelemetry Constants Additions

The following constants must be added to `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` (single source of truth — `GameKitTelemetryConstantsTests` will be extended to cover them):

### New ActivitySource names

```csharp
// For Lobby (OBS-05)
public const string LobbySourceName = "GameKit.Lobby";

// Matchmaking Enqueue span source — the ticker source already exists;
// the enqueue HTTP span uses the same source or a new sub-source.
// Recommendation: reuse GameKit.Matchmaking.Ticker for all matchmaking spans
// (including match-formation). Simpler; one AddSource() call covers all matchmaking.
// [Claude's discretion — this is the recommendation]
```

### New Meter names

```csharp
public const string RankingsMeterName = "GameKit.Rankings";
public const string LobbyMeterName    = "GameKit.Lobby";
```

### New attribute key constants (low-cardinality only, D-04)

```csharp
// "check.result" for ready-check completed counter tag
public const string AttrCheckResult   = "check.result";

// "reason" already used by matchmaking dropped_events counter — not needed as new const
// "pool.name" already exists
// "ladder.id" already exists
```

No new PII-adjacent keys. The GK0001 analyzer will guard any accidental addition.

### `GameKitTelemetryConstantsTests` extension

For each new constant added to `GameKitTelemetry`, the reflection enforcement test must add:
1. A value-equality `[Fact]` for the constant itself.
2. A reflection-based `[Fact]` that loads the per-package assembly and asserts `SourceName` / `MeterName` values match the Core constant.

The existing `LoadMatchmakingAssembly()` pattern (Assembly.LoadFrom probe via sibling build output path) must be replicated for `GameKit.Rankings` and `GameKit.Lobby` assemblies.

---

## `AddGameKitObservability()` Registration Updates

The method in `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` currently registers only `MatchmakingTickerSourceName`, `RankingsTickerSourceName`, and `MatchmakingMeterName`. Phase 15 adds:

```csharp
.WithTracing(tracing =>
{
    tracing
        .AddSource(GameKitTelemetry.MatchmakingTickerSourceName)
        .AddSource(GameKitTelemetry.RankingsTickerSourceName)
        .AddSource(GameKitTelemetry.LobbySourceName);    // NEW Phase 15
    // ... OTLP exporter if configured
})
.WithMetrics(metrics =>
{
    metrics
        .AddMeter(GameKitTelemetry.MatchmakingMeterName)
        .AddMeter(GameKitTelemetry.RankingsMeterName)    // NEW Phase 15
        .AddMeter(GameKitTelemetry.LobbyMeterName);      // NEW Phase 15
    // ... OTLP exporter if configured
})
```

Without adding the new sources/meters here, the sample stack (which calls `AddGameKitObservability()` via `Program.cs`) will not scrape the new instruments — the dashboards will remain empty despite instruments being emitted.

The XML doc comments on `AddGameKitObservability` must be updated to list the Phase-15 additions.

---

## Dashboard Metric Name Mapping (D-06, criterion #4)

The OTel → Prometheus metric name translation follows the OTel Collector's `prometheusexporter`:

| OTel instrument name | Unit | Type | Prometheus metric name |
|----------------------|------|------|------------------------|
| `matchmaking.analytics.dropped_events` | `events` | Counter | `matchmaking_analytics_dropped_events_total` |
| `matchmaking.ticker.lag` | `ms` | Histogram | `matchmaking_ticker_lag_ms_bucket` / `_count` / `_sum` |
| `matchmaking.queue.depth` | `tickets` | ObservableGauge | `matchmaking_queue_depth_tickets` (or `matchmaking_queue_depth` if unitless) |
| `matchmaking.matches.formed` | `matches` | Counter | `matchmaking_matches_formed_total` |
| `matchmaking.ticker.budget_bail` | `events` | Counter | `matchmaking_ticker_budget_bail_total` |
| `matchmaking.leader_lock.acquisition_failures` | `failures` | Counter | `matchmaking_leader_lock_acquisition_failures_total` |
| `matchmaking.lease.acquired` | `events` | Counter | `matchmaking_lease_acquired_total` |
| `matchmaking.lease.lost` | `events` | Counter | `matchmaking_lease_lost_total` |
| `matchmaking.pool_sweep.duration` | `ms` | Histogram | `matchmaking_pool_sweep_duration_ms_bucket` / `_count` / `_sum` |

**Dashboard panel PromQL cross-reference:**

`matchmaking-queue-depth.json` currently queries:
- `rate(matchmaking_analytics_dropped_events_total[5m])` — already matches the existing counter (was emitted before Phase 15)
- `gamekit_matchmaking_queue_depth` — does NOT match the expected name after OTel translation. Correct name will be `matchmaking_queue_depth_tickets` (with unit suffix) or `matchmaking_queue_depth` (if unit omitted). **The dashboard JSON must be updated** to use the actual Prometheus metric name produced by the OTel Collector — verify with `curl http://prometheus:9090/api/v1/label/__name__/values` after first instrument emission. The safest approach: name the instrument `matchmaking.queue.depth` with no explicit unit field, producing `matchmaking_queue_depth` without a suffix. The dashboard panel already queries `gamekit_matchmaking_queue_depth` — note the `gamekit_` prefix is WRONG; OTel does not add a prefix. Dashboard panels will need updating.
- `increase(gamekit_matchmaking_matches_formed_total[5m])` → correct name: `matchmaking_matches_formed_total`
- `increase(gamekit_matchmaking_budget_bail_total[5m])` → correct name: `matchmaking_ticker_budget_bail_total`

`ticker-health.json` currently queries:
- `histogram_quantile(0.50, rate(gamekit_matchmaking_tick_duration_ms_bucket[5m]))` → correct name: `matchmaking_ticker_lag_ms_bucket`
- `rate(gamekit_matchmaking_lease_acquired_total[5m])` → correct name: `matchmaking_lease_acquired_total`
- `rate(gamekit_matchmaking_lease_lost_total[5m])` → correct name: `matchmaking_lease_lost_total`
- `histogram_quantile(0.50, rate(gamekit_matchmaking_pool_sweep_duration_ms_bucket[5m]))` → correct name: `matchmaking_pool_sweep_duration_ms_bucket`
- `histogram_quantile(0.50, rate(gamekit_rankings_drain_ladder_duration_ms_bucket[5m]))` → correct name: `rankings_decay_duration_ms_bucket`

**Important finding:** both dashboard files use a `gamekit_` prefix on every metric name. The OTel Prometheus exporter does NOT add a prefix. The dashboard JSON will need to be corrected — either strip the `gamekit_` prefix everywhere, or configure the OTel Collector `prometheusexporter` with a `namespace: gamekit` setting to re-add it. The simpler fix is to add `namespace: gamekit` to the Prometheus exporter in `otel-collector-config.yml`, which will produce `gamekit_matchmaking_*` names matching the dashboard. **This collector-config change is required for criterion #4.**

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| HTTP RED metrics per endpoint | Custom counters in GameKit endpoints | Built-in `AddAspNetCoreInstrumentation()` + `http.server.request.duration` | D-01; avoids duplication; ASP.NET Core already tags by route + status |
| W3C header serialization | String manipulation for traceparent format | `Activity.Id` (already W3C format when W3C propagation is active) + `Activity.TraceStateString` | In-box; no error-prone format construction |
| W3C header deserialization | String parsing for traceparent format | `ActivityContext.TryParse` (in-box) | Non-throwing; handles edge cases |
| Span links for fan-in | Custom parent-chain gymnastics | `Activity.AddLink(new ActivityLink(ctx))` (in-box) | Idiomatic OTel; preserved in Tempo |
| Histogram quantiles | Pre-computing p50/p99 in code | `histogram_quantile()` in Grafana PromQL | OTel Histogram + Prometheus `*_bucket` is the correct pipeline |
| Messages/sec and completion rate | Counters with pre-computed ratios | Raw counters + `rate()` / division in Grafana | D-05 / Specific Ideas; pre-computing forces scrape-interval assumptions |

---

## MeterListener PII Tag-Key Assertion Test Pattern (criterion #1)

Every package test project needs one test class that:
1. Attaches a `MeterListener` to the package's named `Meter`.
2. Calls the instrumented code (or directly invokes the instrument `Add`/`Record` in a unit test).
3. Collects every tag KEY emitted.
4. Asserts none of the keys are in the forbidden set.

The forbidden keys (from CONTEXT.md + GK0001 analyzer denylist): `ticketId`, `playerId`, `sessionId`, `matchId`, and any token/email/ip/fingerprint derivative.

**Exact xUnit pattern** (mirrors `TicketEventChannelDropTests` already in the codebase):

```csharp
// Source: existing TicketEventChannelDropTests pattern in GameKit.Matchmaking.Tests
// [ASSUMED] — pattern extension; MeterListener API is in-box System.Diagnostics.Metrics

[Fact]
public void NoInstrument_EmitsTagKey_MatchingForbiddenSet()
{
    var forbiddenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ticketId", "ticket_id", "playerId", "player_id",
        "sessionId", "session_id", "matchId", "match_id",
        "userId", "user_id", "email", "token", "fingerprint",
    };

    var emittedTagKeys = new List<string>();

    using var listener = new MeterListener
    {
        InstrumentPublished = (instr, l) =>
        {
            if (instr.Meter.Name == MatchmakingMeter.MeterName)   // or RankingsMeter / LobbyMeter
                l.EnableMeasurementEvents(instr);
        },
    };
    listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
    {
        foreach (var tag in tags)
            emittedTagKeys.Add(tag.Key);
    });
    listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
    {
        foreach (var tag in tags)
            emittedTagKeys.Add(tag.Key);
    });
    listener.Start();

    // --- exercise the instruments ---
    MatchmakingMeter.DroppedEvents.Add(1, new KeyValuePair<string, object?>("reason", "test"));
    // ... add calls for each new instrument

    listener.RecordObservableInstruments();  // for ObservableGauge

    Assert.Empty(emittedTagKeys.Where(k => forbiddenKeys.Contains(k)));
}
```

**Note:** `MeterListener` captures tag keys emitted via `Add`/`Record`/observable callbacks. `ActivityListener` is a SEPARATE listener type needed to capture Activity (span) tag keys. The PII tag-key test for SPANS requires an `ActivityListener`:

```csharp
// [ASSUMED] — ActivityListener for span tag key assertion
using var activityListener = new ActivityListener
{
    ShouldListenTo = source => source.Name == MatchmakingActivitySource.SourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        foreach (var tag in activity.Tags)
            emittedSpanTagKeys.Add(tag.Key);
    },
};
ActivitySource.AddActivityListener(activityListener);
```

Criterion #1 says "a `MeterListener` tag-key assertion test" specifically — span tag key assertions are a bonus; the GK0001 analyzer already guards span tags at build time, so a runtime `ActivityListener` test is belt-and-suspenders rather than required.

**InternalsVisibleTo** — `LobbyMeter` and `RankingsMeter` will be `internal static`, so `src/GameKit.Lobby/AssemblyInfo.cs` and `src/GameKit.Rankings/AssemblyInfo.cs` need `InternalsVisibleTo` grants mirroring the pattern in `src/GameKit.Matchmaking/AssemblyInfo.cs`.

---

## Standard Stack

No new NuGet packages are needed for Phase 15. All required types are in-box:

| Type | Assembly | Purpose |
|------|----------|---------|
| `System.Diagnostics.Activity` | `System.Diagnostics.DiagnosticSource` (already referenced) | Source of `Activity.Current`, `Id`, `TraceStateString` |
| `System.Diagnostics.ActivityContext` | Same | `TryParse` for restoring propagated context |
| `System.Diagnostics.ActivityLink` | Same | Span links for fan-in (D-03) |
| `System.Diagnostics.ActivityListener` | Same | In-test span tag key assertion |
| `System.Diagnostics.Metrics.Meter` | `System.Diagnostics.DiagnosticSource` | Already used by `MatchmakingMeter` |
| `System.Diagnostics.Metrics.MeterListener` | Same | Already used in existing tests |
| `System.Diagnostics.Stopwatch` | `System` | Timing ticker lag and decay duration |

The OTel SDK (`OpenTelemetry` 1.15.3) is already pinned in `Directory.Packages.props` and used only in `AddGameKitObservability()` (in `GameKit.Core` with `PrivateAssets="all"`). No version change needed.

## Package Legitimacy Audit

> No new external packages are added in this phase. All instrumentation uses in-box `System.Diagnostics.*` types.

**Packages removed due to SLOP verdict:** none
**Packages flagged as suspicious (SUS):** none

---

## Architecture Patterns

### System Architecture Diagram

```
HTTP Client
    │
    ▼
[ASP.NET Core middleware]
    │─── http.server.request.duration (built-in, RED)
    │
    ▼
[MatchmakingEndpoints / MatchmakingService.EnqueueAsync]
    │─── write Activity.Id → Redis ticket hash field "otel.traceparent"
    │─── write Activity.TraceStateString → Redis ticket hash field "otel.tracestate"
    │
    ▼  (Redis ticket hash mm:ticket:{id})
    │
[MatchmakerTickerService.ProcessPoolAsync (BackgroundService)]
    │─── HGETALL mm:ticket:{id} (includes otel.traceparent)
    │─── ActivityContext.TryParse(storedTraceparent, …)
    │─── ActivitySource.StartActivity("MatchFormation", …, restoredCtx) ← child of enqueue span
    │─── matchActivity.AddLink(…) for each non-primary ticket
    │─── MatchmakingMeter.TickerLag.Record(ms)
    │─── MatchmakingMeter.QueueDepth callback (ObservableGauge via ZCARD)
    │─── MatchmakingMeter.LockAcquisitionFailures.Add(1) on TryAcquireLeaseAsync==false
    │
    ▼ (OTLP push → OTel Collector)
    │
    ├──► Tempo  (traces, search by traceId shows enqueue→formation in one view)
    └──► Prometheus (metrics, scraped from Collector Prometheus exporter)
             │
             ▼
          Grafana dashboards
```

### Recommended Project Structure additions

```
src/GameKit.Lobby/
└── Telemetry/
    ├── LobbyActivitySource.cs    # "GameKit.Lobby" source, StartReadyCheckActivity() typed helper
    └── LobbyMeter.cs             # "GameKit.Lobby" meter, all instruments as static readonly fields

src/GameKit.Rankings/
└── Telemetry/
    ├── RankingsActivitySource.cs # already exists (Phase 13)
    └── RankingsMeter.cs          # NEW: "GameKit.Rankings" meter, decay duration histogram

src/GameKit.Matchmaking/
└── Telemetry/
    ├── MatchmakingActivitySource.cs  # exists; no structural change
    └── MatchmakingMeter.cs           # exists; add new instruments as static readonly fields
```

### Anti-Patterns to Avoid

- **Adding an OTel SDK `using` to shipped package code** — shipped packages (`GameKit.Matchmaking`, `GameKit.Rankings`, `GameKit.Lobby`) must never reference `OpenTelemetry.*` namespaces. Only `System.Diagnostics.*` primitives. The SDK is in `GameKit.Core` with `PrivateAssets="all"`.
- **Async in ObservableGauge callback** — the callback must be synchronous. `IDatabase.SortedSetLength` (sync StackExchange.Redis API) is the correct call; never `await` inside an observable callback.
- **Using `Activity.Current.SetTag` to write `traceparent`** — `traceparent` is a propagation header, not a span attribute. Store it as a Redis hash field, not as an OTel tag.
- **Using `player_id`, `ticket_id`, etc. as tag keys** — the GK0001 analyzer will fail the build, and the MeterListener PII test will catch runtime emission.
- **Pre-computing rates/ratios in metrics code** — emit raw counters; compute rates in Grafana `rate()` PromQL.

---

## Common Pitfalls

### Pitfall 1: `Activity.Current` is null at enqueue time

**What goes wrong:** if no listener subscribes to the Matchmaking ActivitySource, `Activity.Current` reflects the ASP.NET Core built-in HTTP span — which IS present (assuming `AddAspNetCoreInstrumentation()` is called). However, if the HTTP server span has `TraceFlags = 00` (not sampled), `Activity.Current.Id` may still be non-null but the downstream `ActivityContext.TryParse` will return a non-recorded context, causing `StartActivity` with that parent to return `null`.

**Prevention:** always null-guard `Activity.Current` and store the Id string regardless of sampling flags. The ticker will then correctly propagate the sampling decision — a non-recorded parent means a non-recorded formation span, which is correct.

### Pitfall 2: `Activity.Id` format depends on W3C vs hierarchical propagation mode

**What goes wrong:** if the host has not set `Activity.DefaultIdFormat = ActivityIdFormat.W3C`, `Activity.Id` produces a hierarchical ID (`|traceId.spanId.`) rather than a W3C `traceparent` (`00-…-…-01`). `ActivityContext.TryParse` only accepts the W3C format.

**Prevention:** the sample app (`Program.cs`) or `AddAspNetCoreInstrumentation()` configures W3C format by default in .NET 6+. Confirm with `Activity.DefaultIdFormat == ActivityIdFormat.W3C` in the enqueue path or by checking the OTel SDK initialisation. The CLAUDE.md stack is .NET 10 and ASP.NET Core 10 — W3C is the default. Add a defensive assertion in tests.

### Pitfall 3: ObservableGauge callback throws during scrape

**What goes wrong:** if Redis is unavailable when Prometheus scrapes, the ObservableGauge callback calling `IDatabase.SortedSetLength` will throw a `RedisConnectionException`. An unhandled exception in the OTel observable callback propagates up and may corrupt the metrics pipeline state.

**Prevention:** wrap the Redis call in a try/catch inside the callback; on exception, either yield no measurement (do nothing) or yield `-1` as a sentinel. The gauge disappearing briefly during a Redis outage is acceptable and expected.

### Pitfall 4: `gamekit_` prefix mismatch between dashboards and actual Prometheus metric names

**What goes wrong:** both existing dashboard JSON files query metric names prefixed with `gamekit_` (e.g., `gamekit_matchmaking_queue_depth`). The OTel Collector's `prometheusexporter` by default does NOT add a prefix. If the collector config does not add a namespace, all panels will show no data.

**Prevention:** add `namespace: gamekit` to the `exporters.prometheus` section of `otel-collector-config.yml`. This makes all GameKit metrics appear as `gamekit_*` in Prometheus, matching the dashboard queries. This is a configuration change to the sample stack, not a code change. Alternatively, update the dashboard JSON to remove the `gamekit_` prefix — but changing the collector config is the lower-friction fix since it validates that the entire pipeline is wired correctly.

### Pitfall 5: Rankings timer measuring wall-clock but including lock-wait time

**What goes wrong:** if the `Stopwatch` for rank-decay duration starts before `TryAcquireLeaseAsync`, the histogram will measure lock contention rather than actual decay work.

**Prevention:** start the `Stopwatch` after `TryAcquireLeaseAsync` returns `true`.

### Pitfall 6: Lobby `LobbyMeter` instruments using null-forgiving on a null Meter

**What goes wrong:** if `LobbyMeter` is `internal static` with a static initializer and no listener ever subscribes, the `Meter` itself is live but instruments produce no measurements. This is correct OTel behaviour, but tests that try to subscribe a `MeterListener` after instruments are already created must call `listener.Start()` BEFORE the code-under-test creates or uses the meter. Since `MatchmakingMeter`/`LobbyMeter` are static, they are initialized at class-load time — the listener must be registered before the test exercises the instruments.

**Prevention:** register `MeterListener` BEFORE invoking the code under test. The existing `TicketEventChannelDropTests` does this correctly; replicate the pattern.

---

## Validation Architecture

Nyquist validation is enabled (`workflow.nyquist_validation: true`).

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | `xunit.runner.json` per test project |
| Quick run command | `dotnet test tests/GameKit.Matchmaking.Tests --no-build -x` |
| Full suite command | `dotnet test --no-build --filter "Category!=LoadTest"` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| OBS-04 | No instrument emits PII tag keys — Matchmaking | Unit (MeterListener) | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "PiiTagKey"` | ❌ Wave 0 |
| OBS-04 | Ticker-lag histogram records measurement per tick | Unit (MeterListener) | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "TickerLag"` | ❌ Wave 0 |
| OBS-04 | Queue-depth ObservableGauge callback invoked during `RecordObservableInstruments()` | Unit (MeterListener) | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "QueueDepth"` | ❌ Wave 0 |
| OBS-04 | Leader-lock failure counter increments when `TryAcquireLeaseAsync` returns false | Unit (MeterListener) | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "LeaderLock"` | ❌ Wave 0 |
| OBS-04 | No instrument emits PII tag keys — Rankings | Unit (MeterListener) | `dotnet test tests/GameKit.Rankings.Tests -x --filter "PiiTagKey"` | ❌ Wave 0 |
| OBS-04 | Decay-duration histogram records per decay run | Unit (MeterListener) | `dotnet test tests/GameKit.Rankings.Tests -x --filter "DecayDuration"` | ❌ Wave 0 |
| OBS-05 | No instrument emits PII tag keys — Lobby | Unit (MeterListener) | `dotnet test tests/GameKit.Lobby.Tests -x --filter "PiiTagKey"` | ❌ Wave 0 |
| OBS-05 | Connected-clients gauge increments/decrements correctly | Unit (MeterListener) | `dotnet test tests/GameKit.Lobby.Tests -x --filter "ConnectedClients"` | ❌ Wave 0 |
| OBS-05 | Messages counter increments on SendChatMessageAsync | Unit (MeterListener) | `dotnet test tests/GameKit.Lobby.Tests -x --filter "MessageCounter"` | ❌ Wave 0 |
| OBS-05 | Ready-check started/completed counters fire in correct sequence | Unit (MeterListener) | `dotnet test tests/GameKit.Lobby.Tests -x --filter "ReadyCheck"` | ❌ Wave 0 |
| OBS-06 | Ticker produces MatchFormation span with correct parent from restored traceparent | Unit (ActivityListener) | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "TraceParent"` | ❌ Wave 0 |
| OBS-06 | Fan-in: second ticket attached as ActivityLink on match-formation span | Unit (ActivityListener) | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "SpanLink"` | ❌ Wave 0 |
| OBS-06 | Non-sampled parent produces no formation span (returns null) | Unit | `dotnet test tests/GameKit.Matchmaking.Tests -x --filter "NonSampled"` | ❌ Wave 0 |
| Criterion #4 | `GameKitTelemetryConstantsTests` covers new LobbyMeterName, RankingsMeterName, LobbySourceName | Unit (reflection) | `dotnet test tests/GameKit.Core.Tests -x --filter "TelemetryConstants"` | ❌ Wave 0 (extend existing file) |
| Criterion #4 | Dashboard JSON panels query metric names that exist in Prometheus namespace | Manual / sample stack | `docker compose up -d && curl grafana:3000/api/dashboards/uid/...` | Manual — criterion #4 requires live stack |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Matchmaking.Tests --no-build -x`
- **Per wave merge:** `dotnet test --no-build --filter "Category!=LoadTest"` (full affected-package suite per project memory)
- **Phase gate:** Full suite green before `/gsd-verify-work`. Criterion #4 (dashboard renders real data) requires a live sample stack run — this is the one success criterion that cannot be fully automated in-process.

### Wave 0 Gaps

New test files to create before Wave 1 implementation:
- [ ] `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs` — covers OBS-04 criterion #1 (Matchmaking)
- [ ] `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs` — covers OBS-04 criterion #1 (Rankings)
- [ ] `tests/GameKit.Lobby.Tests/Telemetry/LobbyPiiTagKeyTests.cs` — covers OBS-05 criterion #1 (Lobby)
- [ ] `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs` — covers OBS-06 criteria #2 parent/link assertions
- [ ] Extend `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` with new constant assertions

---

## Security Domain

The GK0001 Roslyn PII analyzer (Phase 13, D-06/D-07) is the primary security control for this phase. It guards `SetTag`/`AddTag` call sites in `src/` at build time against the token-split denylist `{player, user, email, token, ip, fingerprint}`.

| ASVS Category | Applies | Standard Control |
|---------------|---------|------------------|
| V5 Input Validation | Partial | OTel tag keys are from GameKit-controlled constants, not user input. No user-supplied strings used as tag keys. |
| PII / Data Minimisation | Yes — primary concern | GK0001 analyzer + MeterListener test + attribute allow-list (Phase 13 D-07) |

No new auth, crypto, or session concerns introduced by this phase.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker / docker compose | Criterion #4 dashboard smoke test | Must be confirmed by implementer | — | Manual import + visual check |
| Redis (live) | ObservableGauge integration test | ✓ (Testcontainers fixture) | Testcontainers.Redis 4.11.0 | — |
| Prometheus (in stack) | Dashboard rendering | Via docker compose overlay | prom/prometheus:v3.11.2 | — |
| Tempo | Trace descent assertion via live stack | Via docker compose overlay | grafana/tempo:2.6.1 | In-process ActivityListener test (criterion #2 proxy) |

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hierarchical `|traceId.spanId.` Activity ID format | W3C `traceparent` format by default | .NET 5 (W3C became default) | `ActivityContext.TryParse` works; hierarchical would require custom parsing |
| Manual W3C propagation via `TextMapPropagator` | `Activity.Id` already W3C-formatted when DefaultIdFormat=W3C | .NET 6 | No propagator injection needed for Redis-stored string propagation |
| `System.Diagnostics.Metrics` API | Shipped in .NET 6 as stable | .NET 6 (2021) | In-box meter/histogram/gauge — no NuGet needed |
| `MeterListener` for in-process test assertions | Same (in-box) | .NET 6 | Tests can subscribe without OTel SDK |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Activity.Id` is already W3C-formatted (`00-{traceId}-{spanId}-{flags}`) when `AddAspNetCoreInstrumentation()` is active on .NET 10 | W3C Trace Propagation | If hierarchical format, `ActivityContext.TryParse` fails silently; formation span loses parent. Mitigation: assert format in tests. |
| A2 | `ActivityContext.TryParse(traceparent, tracestate, isRemote, out ctx)` is the correct non-throwing API in .NET 10 | W3C Trace Propagation | If API surface changed, compilation error — immediately visible. |
| A3 | `Activity.AddLink(new ActivityLink(ctx))` is available on `Activity` instances started via `ActivitySource.StartActivity` | W3C Trace Propagation (fan-in) | Compile error if wrong; ActivityLink is in-box System.Diagnostics. |
| A4 | OTel Collector `prometheusexporter` adds a `namespace` prefix when configured (`namespace: gamekit`) | Dashboard Metric Name Mapping | If not supported in the pinned Collector version (0.154.0), dashboards will still show no-data. Mitigation: verify in otel-collector-config.yml docs for that version. |
| A5 | Histogram bucket boundaries in `System.Diagnostics.Metrics` are a hint to the OTel SDK layer, not part of the in-box metric descriptor | Instrument Recommendations | If SDK ignores boundaries, quantile resolution degrades — non-blocking; can be configured via SDK `View` API. |
| A6 | `IDatabase.SortedSetLength` (sync StackExchange.Redis API) is callable inside the synchronous ObservableGauge callback without deadlock | Instrument Recommendations | StackExchange.Redis 2.8.41 supports synchronous calls on the returned `IDatabase`; if the multiplexer is in a degraded state it may throw rather than deadlock. Mitigation: try/catch in callback. |

---

## Open Questions

1. **OTel Collector `namespace` config in v0.154.0** — confirm that `otel/opentelemetry-collector-contrib:0.154.0` (pinned in `docker-compose.observability.yml`) supports `exporters.prometheus.namespace`. If not, the fix is to update the dashboard JSON to remove `gamekit_` prefix instead.
   - What we know: `prometheusexporter` has had `namespace` support for years; 0.154 (2025) almost certainly includes it.
   - What's unclear: exact key name in the config YAML (`namespace` vs `metric_expiration`).
   - Recommendation: verify by consulting `docker run otel/opentelemetry-collector-contrib:0.154.0 --help` or the Collector changelog for that version.

2. **`Activity.DefaultIdFormat` in the sample app** — is W3C format explicitly set, or is it relying on the default?
   - What we know: .NET 6+ defaults to W3C; `AddAspNetCoreInstrumentation()` forces W3C.
   - Recommendation: add `Activity.DefaultIdFormat = ActivityIdFormat.W3C; Activity.ForceDefaultIdFormat = true;` early in `Program.cs` as belt-and-suspenders; document in the enqueue path.

3. **Existing `phase.*` tags on `MatchmakingActivitySource.StartPoolActivity` span** — the ticker currently sets `phase.hash_fanout_ms` as a tag on the pool activity. This is NOT in the `GameKitTelemetry` attribute-key constants and uses a dotted key that the GK0001 denylist would NOT flag (no PII tokens). But it is a magic string. These existing tags need to be added to `GameKitTelemetry` as constants in Phase 15 (or documented as acknowledged deviations from the single-source-of-truth rule).

---

## Sources

### Primary (HIGH confidence — codebase inspection)
- `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` — single source of truth, read directly
- `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — canonical Meter pattern, read directly
- `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` — canonical ActivitySource pattern, read directly
- `src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` — Rankings source, read directly
- `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` — AddGameKitObservability, read directly
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — tick anatomy and ProcessPoolAsync, read directly
- `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` — lock acquire/release sites, read directly
- `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` — decay job structure, read directly
- `src/GameKit.Lobby/Hubs/LobbyHub.cs` — OnConnectedAsync, SendChatMessageAsync, MarkReadyAsync, read directly
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — ticket hash key layout, read directly
- `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json` — dashboard PromQL, read directly
- `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` — dashboard PromQL, read directly
- `samples/TicTacToeDuel/docker-compose.observability.yml` — sample stack, read directly
- `samples/TicTacToeDuel/Program.cs` — AddGameKitObservability wiring site, read directly
- `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` — reflection enforcement test pattern, read directly
- `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` — MeterListener test pattern, read directly
- `tests/GameKit.Matchmaking.Integration.Tests/AnalyticsDrainServiceTests.cs` — MeterListener integration pattern, read directly

### Secondary (MEDIUM confidence — documented .NET API knowledge)
- `System.Diagnostics.ActivityContext.TryParse` API — in-box .NET 6+ [ASSUMED]
- `Activity.AddLink(ActivityLink)` fan-in pattern — OTel semantic convention [ASSUMED]
- OTel Prometheus exporter `namespace` config — [ASSUMED] (verify in OTel Collector docs)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all in-box
- W3C propagation mechanics: MEDIUM — API surface from training knowledge; verify `TryParse` signature in .NET 10 SDK docs
- Instrument type choices: HIGH — directly from D-05 decisions + idiomatic OTel patterns
- Dashboard metric name mapping: HIGH — mechanically derived from OTel → Prometheus translation rules + dashboard JSON read directly; `gamekit_` prefix finding requires validation
- Architecture: HIGH — derived from direct codebase inspection

**Research date:** 2026-06-22
**Valid until:** 2026-09-22 (stable .NET/OTel API surface; OTel Collector config stable within minor versions)
