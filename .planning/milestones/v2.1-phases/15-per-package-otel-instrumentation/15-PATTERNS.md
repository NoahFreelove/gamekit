# Phase 15: Per-Package OTel Instrumentation - Pattern Map

**Mapped:** 2026-06-22
**Files analyzed:** 16 new/modified files
**Analogs found:** 16 / 16

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` | utility (constants) | — | self (extend) | exact |
| `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` | utility (DI registration) | request-response | self (extend) | exact |
| `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` | utility (metrics) | event-driven | self (extend) | exact |
| `src/GameKit.Rankings/Telemetry/RankingsMeter.cs` | utility (metrics) | event-driven | `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` | exact |
| `src/GameKit.Lobby/Telemetry/LobbyMeter.cs` | utility (metrics) | event-driven | `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` | exact |
| `src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs` | utility (tracing) | event-driven | `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` | exact |
| `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` | utility (constants) | — | self (extend) | exact |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | service | request-response | self (extend) | exact |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | service (BackgroundService) | event-driven | self (extend) | exact |
| `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` | service | event-driven | self (extend) | exact |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` | service (BackgroundService) | event-driven | `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | role-match |
| `src/GameKit.Lobby/Hubs/LobbyHub.cs` | hub (SignalR) | event-driven | self (extend) | exact |
| `src/GameKit.Lobby/AssemblyInfo.cs` | config | — | `src/GameKit.Matchmaking/AssemblyInfo.cs` | exact |
| `src/GameKit.Rankings/AssemblyInfo.cs` | config | — | `src/GameKit.Rankings/AssemblyInfo.cs` (extend) | exact |
| `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs` | test (unit) | event-driven | `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` | exact |
| `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs` | test (unit) | event-driven | `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` | role-match |
| `tests/GameKit.Lobby.Tests/Telemetry/LobbyPiiTagKeyTests.cs` | test (unit) | event-driven | `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` | role-match |
| `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs` | test (unit) | event-driven | `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` | role-match |
| `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` | test (unit/reflection) | — | self (extend) | exact |
| `samples/TicTacToeDuel/observability/otel-collector-config.yml` | config | — | existing file (extend) | exact |
| `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json` | config (dashboard) | — | existing file (update PromQL) | exact |
| `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` | config (dashboard) | — | existing file (update PromQL) | exact |

---

## Pattern Assignments

### NEW: `src/GameKit.Rankings/Telemetry/RankingsMeter.cs` (utility, event-driven)

**Analog:** `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs`

**Full file to copy, then adapt** (lines 1–62):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics.Metrics;

namespace GameKit.Rankings.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="Meter"/> for <c>GameKit.Rankings</c> diagnostics.
/// </summary>
internal static class RankingsMeter
{
    /// <summary>The Rankings meter name. Operators must register <c>AddMeter</c> with this exact value.</summary>
    public const string MeterName = "GameKit.Rankings";    // must equal GameKitTelemetry.RankingsMeterName

    /// <summary>The meter version, pinned to <c>1.0.0</c> for v1 wire compatibility.</summary>
    public const string MeterVersion = "1.0.0";

    /// <summary>The <see cref="Meter"/> instance backing every Rankings histogram / counter.</summary>
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    /// <summary>
    /// Histogram recording the wall-clock duration of a single RankDecayBackgroundService.RunOnceAsync
    /// decay run, measured after lease acquisition and before lease release (ms).
    /// </summary>
    public static readonly Histogram<double> DecayDuration = Meter.CreateHistogram<double>(
        name: "rankings.decay.duration",
        unit: "ms",
        description: "Wall-clock duration of one RankDecayBackgroundService.RunOnceAsync decay run");

    /// <summary>
    /// Counter tracking the number of player_ranks rows updated in a single decay run.
    /// </summary>
    public static readonly Counter<long> DecayRowsUpdated = Meter.CreateCounter<long>(
        name: "rankings.decay.rows_updated",
        unit: "rows",
        description: "Count of player_ranks rows updated per RankDecayBackgroundService decay run");
}
```

**Key deltas from analog:**
- `namespace GameKit.Rankings.Telemetry` (not Matchmaking)
- `MeterName = "GameKit.Rankings"` (must equal the new `GameKitTelemetry.RankingsMeterName` constant)
- Instruments: `DecayDuration` Histogram + `DecayRowsUpdated` Counter (not `DroppedEvents` Counter)

---

### NEW: `src/GameKit.Lobby/Telemetry/LobbyMeter.cs` (utility, event-driven)

**Analog:** `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs`

**Full file to copy, then adapt** (lines 1–62 of analog):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics.Metrics;

namespace GameKit.Lobby.Telemetry;

internal static class LobbyMeter
{
    public const string MeterName = "GameKit.Lobby";   // must equal GameKitTelemetry.LobbyMeterName
    public const string MeterVersion = "1.0.0";
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    // ObservableGauge: callback reads a singleton int field; no Redis needed.
    // Register during static init; callback supplies the current _connectedClients value.
    public static readonly ObservableGauge<int> ConnectedClients = Meter.CreateObservableGauge<int>(
        name: "lobby.connected_clients",
        unit: "connections",
        description: "Current number of connected SignalR clients to the LobbyHub (per-replica)");

    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        name: "lobby.messages.sent",
        unit: "messages",
        description: "Count of chat messages relayed through LobbyHub.SendChatMessageAsync");

    public static readonly Counter<long> ReadyCheckStarted = Meter.CreateCounter<long>(
        name: "lobby.ready_check.started",
        unit: "checks",
        description: "Count of ready-check initiations");

    public static readonly Counter<long> ReadyCheckCompleted = Meter.CreateCounter<long>(
        name: "lobby.ready_check.completed",
        unit: "checks",
        description: "Count of ready-check completions. Tag: check.result=all_ready|timeout|cancelled");
}
```

**ObservableGauge wiring note:** the callback for `ConnectedClients` is registered at class-init time and reads a `volatile int` or `Interlocked` counter maintained by `LobbyHub.OnConnectedAsync`/`OnDisconnectedAsync` via a singleton `LobbyConnectionTracker` service. Pattern:
```csharp
// In LobbyConnectionTracker (singleton, injected into LobbyHub):
private int _count;
public void Increment() => Interlocked.Increment(ref _count);
public void Decrement() => Interlocked.Decrement(ref _count);
public int Current => Volatile.Read(ref _count);

// In LobbyMeter static init, pass the tracker reference to the ObservableGauge callback.
// Because LobbyMeter is static, inject via a static Init(tracker) method called from DI setup.
```

---

### NEW: `src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs` (utility, tracing)

**Analog:** `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` (lines 1–81)

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics;
using GameKit.Core.Telemetry;

namespace GameKit.Lobby.Telemetry;

public static class LobbyActivitySource
{
    public const string SourceName = "GameKit.Lobby";   // must equal GameKitTelemetry.LobbySourceName

    internal static readonly ActivitySource Source = new(SourceName, GameKitTelemetry.Version);

    /// <summary>
    /// Starts a span named <c>"ReadyCheck"</c> wrapping the ready-check broadcast in
    /// <c>ILobbyService.MarkReadyAsync</c>. Parent context comes from the caller's
    /// <c>Activity.Current</c> captured at the hub invocation site.
    /// </summary>
    public static Activity? StartReadyCheckActivity(ActivityContext parentContext = default)
    {
        return parentContext == default
            ? Source.StartActivity("ReadyCheck")
            : Source.StartActivity("ReadyCheck", ActivityKind.Internal, parentContext);
    }
}
```

---

### EXTEND: `src/GameKit.Core/Telemetry/GameKitTelemetry.cs`

**Analog:** self — extend the existing static class (lines 39–125).

Add after existing constants:

```csharp
// ── Phase 15 additions ────────────────────────────────────────────────────────

/// <summary>
/// <c>ActivitySource</c> name for GameKit.Lobby SignalR hub instrumentation (OBS-05).
/// Operators MUST call <c>AddSource("GameKit.Lobby")</c> to subscribe.
/// </summary>
/// <remarks>Equals <c>LobbyActivitySource.SourceName</c> in <c>GameKit.Lobby</c>.</remarks>
public const string LobbySourceName = "GameKit.Lobby";

/// <summary>
/// <c>Meter</c> name for <c>GameKit.Rankings</c> diagnostics (decay duration, rows updated).
/// Operators MUST call <c>AddMeter("GameKit.Rankings")</c> to subscribe.
/// </summary>
/// <remarks>Equals <c>RankingsMeter.MeterName</c> in <c>GameKit.Rankings</c>.</remarks>
public const string RankingsMeterName = "GameKit.Rankings";

/// <summary>
/// <c>Meter</c> name for <c>GameKit.Lobby</c> diagnostics (connected clients, messages, ready-checks).
/// Operators MUST call <c>AddMeter("GameKit.Lobby")</c> to subscribe.
/// </summary>
/// <remarks>Equals <c>LobbyMeter.MeterName</c> in <c>GameKit.Lobby</c>.</remarks>
public const string LobbyMeterName = "GameKit.Lobby";

/// <summary>
/// Span/metric attribute key for the result of a ready-check operation.
/// Low-cardinality values: <c>"all_ready"</c>, <c>"timeout"</c>, <c>"cancelled"</c>.
/// </summary>
public const string AttrCheckResult = "check.result";
```

---

### EXTEND: `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs`

**Analog:** self — extend `WithTracing` and `WithMetrics` calls (lines 99–128).

```csharp
// BEFORE (lines 101–119):
.WithTracing(tracing =>
{
    tracing
        .AddSource(GameKitTelemetry.MatchmakingTickerSourceName)
        .AddSource(GameKitTelemetry.RankingsTickerSourceName);
    // ...
})
.WithMetrics(metrics =>
{
    metrics.AddMeter(GameKitTelemetry.MatchmakingMeterName);
    // ...
});

// AFTER (Phase 15 additions in bold):
.WithTracing(tracing =>
{
    tracing
        .AddSource(GameKitTelemetry.MatchmakingTickerSourceName)
        .AddSource(GameKitTelemetry.RankingsTickerSourceName)
        .AddSource(GameKitTelemetry.LobbySourceName);          // NEW Phase 15
    // ...
})
.WithMetrics(metrics =>
{
    metrics
        .AddMeter(GameKitTelemetry.MatchmakingMeterName)
        .AddMeter(GameKitTelemetry.RankingsMeterName)          // NEW Phase 15
        .AddMeter(GameKitTelemetry.LobbyMeterName);            // NEW Phase 15
    // ...
});
```

Also update the XML doc `<remarks>` on `AddGameKitObservability` to list Phase-15 sources/meters.

---

### EXTEND: `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs`

**Analog:** self — add new static fields after the existing `DroppedEvents` counter (line 58).

**Imports already present:** `System.Diagnostics.Metrics` (line 4). No new using required.

New instruments to add (copy `DroppedEvents` shape for counters; introduce `Histogram<double>` and `ObservableGauge<long>` shapes):
```csharp
// --- Phase 15 additions ---

public static readonly Histogram<double> TickerLag = Meter.CreateHistogram<double>(
    name: "matchmaking.ticker.lag",
    unit: "ms",
    description: "Wall-clock duration of MatchmakerTickerService.RunOnceAsync from start to before lease release");

public static readonly Histogram<double> PoolSweepDuration = Meter.CreateHistogram<double>(
    name: "matchmaking.pool_sweep.duration",
    unit: "ms",
    description: "Duration of each ProcessPoolAsync call. Tag: ladder.id");

// ObservableGauge: callback registered at init; reads Redis ZCARD per pool.
// Use Init(IDatabase db, IReadOnlyList<MatchmakingLadderConfig> ladders) static method
// called from MatchmakingBuilderExtensions so the static class gets its Redis reference.
public static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge<long>(
    name: "matchmaking.queue.depth",
    unit: "tickets",
    description: "Current count of tickets in each matchmaking pool sorted set. Tags: pool.name, ladder.id");

public static readonly Counter<long> LockAcquisitionFailures = Meter.CreateCounter<long>(
    name: "matchmaking.leader_lock.acquisition_failures",
    unit: "failures",
    description: "Count of TryAcquireLeaseAsync calls that returned false (another replica holds leader or Redis error)");

public static readonly Counter<long> MatchesFormed = Meter.CreateCounter<long>(
    name: "matchmaking.matches.formed",
    unit: "matches",
    description: "Count of match proposals created. Tag: ladder.id");

public static readonly Counter<long> BudgetBail = Meter.CreateCounter<long>(
    name: "matchmaking.ticker.budget_bail",
    unit: "events",
    description: "Count of ticker iterations that exited early due to time-budget exhaustion. Tag: ladder.id");

public static readonly Counter<long> LeaseAcquired = Meter.CreateCounter<long>(
    name: "matchmaking.lease.acquired",
    unit: "events",
    description: "Count of successful TryAcquireLeaseAsync calls");

public static readonly Counter<long> LeaseLost = Meter.CreateCounter<long>(
    name: "matchmaking.lease.lost",
    unit: "events",
    description: "Count of ticker iterations that returned MatcherTickResult.LeaseLost (Lua fencing check failed)");
```

---

### EXTEND: `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs`

**Analog:** self — add hash field name constants after the existing `ProposalAcceptsSuffix` constant (line 66).

```csharp
// ── Ticket hash field names (for otel.traceparent / otel.tracestate) ─────────

/// <summary>
/// Ticket hash field storing the W3C <c>traceparent</c> string of the enqueue HTTP span.
/// Written by <c>MatchmakingService.EnqueueAsync</c>; read by
/// <c>MatchmakerTickerService.ProcessPoolAsync</c> to restore the parent <see cref="System.Diagnostics.ActivityContext"/>.
/// Value is <c>Activity.Current?.Id</c> (already W3C-format on .NET 10 with ASP.NET Core).
/// Omitted from the Redis HSET when <c>Activity.Current</c> is <see langword="null"/>.
/// </summary>
public const string TicketTraceParent = "otel.traceparent";

/// <summary>
/// Ticket hash field storing the W3C <c>tracestate</c> string of the enqueue HTTP span.
/// Written alongside <see cref="TicketTraceParent"/> when <c>Activity.Current?.TraceStateString</c>
/// is non-null and non-empty. Carrying <c>tracestate</c> preserves vendor-specific
/// propagation (e.g., Jaeger baggage) across the Redis fan-in.
/// </summary>
public const string TicketTraceState = "otel.tracestate";
```

---

### EXTEND: `src/GameKit.Matchmaking/Services/MatchmakingService.cs` (enqueue path)

**Analog:** self — instrument the existing enqueue step 6 (HSET ticket hash). Pattern is write-after-current-activity:

```csharp
// In EnqueueAsync, after building the HSET field list and before/during the Redis pipeline call:
// Write traceparent into the ticket hash if an active span exists.
var currentActivity = System.Diagnostics.Activity.Current;
if (currentActivity is not null)
{
    // Activity.Id is already W3C traceparent format on .NET 10 (see RESEARCH Pitfall 2).
    hashFields.Add(new HashEntry(MatchmakingRedisKeys.TicketTraceParent, currentActivity.Id!));

    var traceState = currentActivity.TraceStateString;
    if (!string.IsNullOrEmpty(traceState))
        hashFields.Add(new HashEntry(MatchmakingRedisKeys.TicketTraceState, traceState));
}
```

No new `using` needed — `System.Diagnostics` is already in the BCL.

---

### EXTEND: `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` (ticker instrumentation)

**Analog:** self — instrument `RunOnceAsync` and `ProcessPoolAsync`. Key patterns:

**Ticker-lag histogram** (wrap `RunOnceAsync` body with Stopwatch):
```csharp
// After lock acquired (line 177), before existing tick logic:
var tickSw = System.Diagnostics.Stopwatch.StartNew();

// At the end of RunOnceAsync finally block (before lease release):
MatchmakingMeter.TickerLag.Record(tickSw.Elapsed.TotalMilliseconds);
```

**Leader-lock counters** (at `TryAcquireLeaseAsync` call sites):
```csharp
var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
if (!acquired)
{
    MatchmakingMeter.LockAcquisitionFailures.Add(1);
    // existing log + return
}
else
{
    MatchmakingMeter.LeaseAcquired.Add(1);
}
```

**Match-formation span with restored W3C parent** (in `ProcessPoolAsync`, on `AtomicClaimResult.Success`):
```csharp
// Read hash fields otel.traceparent / otel.tracestate from the ticket hash (already HGETALL'd).
string? storedTraceparent = ticketHash.TryGetValue(MatchmakingRedisKeys.TicketTraceParent, out var tp) ? tp : null;
string? storedTracestate  = ticketHash.TryGetValue(MatchmakingRedisKeys.TicketTraceState,  out var ts) ? ts : null;

ActivityContext restoredCtx = default;
bool hasParent = storedTraceparent is not null &&
    ActivityContext.TryParse(storedTraceparent, storedTracestate, isRemote: true, out restoredCtx);

using var matchActivity = hasParent
    ? MatchmakingActivitySource.Source.StartActivity("MatchFormation", ActivityKind.Internal, restoredCtx)
    : MatchmakingActivitySource.Source.StartActivity("MatchFormation");

// Fan-in: attach non-primary tickets as span links (D-03)
foreach (var nonPrimary in matchedTickets.Skip(1))
{
    if (nonPrimary.TraceparentStr is not null &&
        ActivityContext.TryParse(nonPrimary.TraceparentStr, nonPrimary.TracestateStr,
            isRemote: true, out var linkCtx))
    {
        matchActivity?.AddLink(new ActivityLink(linkCtx));
    }
}

MatchmakingMeter.MatchesFormed.Add(1,
    new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, ladder.LadderId.ToString()));
```

**Missing using** — add `using System.Diagnostics;` and `using GameKit.Core.Telemetry;` if not already present.

---

### EXTEND: `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs`

**Analog:** self — instrument `TryAcquireLeaseAsync`. The counter call site is in `MatchmakerTickerService` (see above); `MatchmakerLeaseHelper` itself needs no direct counter call — the ticker is the caller that knows the semantic meaning of a false return. No changes required to the helper class itself unless the `IMatchmakerLease` interface path is used (then instrument the caller, not the implementation).

---

### EXTEND: `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs`

**Analog:** `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` ticker-lag pattern.

**Decay-duration histogram** (lines 121–139 of RankDecayBackgroundService are the `RunOnceAsync` body):
```csharp
// After lease acquired (line 124), start Stopwatch:
var decaySw = System.Diagnostics.Stopwatch.StartNew();

// Using existing RankingsActivitySource.Source.StartActivity("RankDecay") (fresh root span —
// no inbound traceparent to restore for a background job per RESEARCH §Rank-decay):
using var decayActivity = RankingsActivitySource.Source.StartActivity("RankDecay");

// After Postgres UPDATE commits (end of try block):
decaySw.Stop();
RankingsMeter.DecayDuration.Record(decaySw.Elapsed.TotalMilliseconds);
RankingsMeter.DecayRowsUpdated.Add(rowsUpdated);   // rowsUpdated from EF SaveChangesAsync result
```

**New using directives** needed at top of file:
```csharp
using System.Diagnostics;
using GameKit.Rankings.Telemetry;
```

---

### EXTEND: `src/GameKit.Lobby/Hubs/LobbyHub.cs`

**Analog:** self — add telemetry calls at existing lifecycle event sites.

**OnConnectedAsync** (line 73):
```csharp
public override async Task OnConnectedAsync()
{
    LobbyConnectionTracker.Instance.Increment();   // singleton counter for ObservableGauge
    // ... existing logic unchanged
}
```

**OnDisconnectedAsync** (override to add):
```csharp
public override async Task OnDisconnectedAsync(Exception? exception)
{
    LobbyConnectionTracker.Instance.Decrement();
    await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
}
```

**SendChatMessageAsync** — add counter increment after successful relay:
```csharp
LobbyMeter.MessagesSent.Add(1);
```

**MarkReadyAsync** — capture Activity.Current at hub invocation, pass to service, add counters:
```csharp
public async Task MarkReadyAsync(Guid lobbyId)
{
    LobbyMeter.ReadyCheckStarted.Add(1);

    // Capture Activity.Current while still in the hub pipeline (has SignalR HTTP span as parent).
    var callerContext = System.Diagnostics.Activity.Current?.Context ?? default;

    var result = await _lobby.MarkReadyAsync(playerId, lobbyId, callerContext, ct).ConfigureAwait(false);

    var checkResult = result switch
    {
        ReadyCheckResult.AllReady  => "all_ready",
        ReadyCheckResult.Timeout   => "timeout",
        ReadyCheckResult.Cancelled => "cancelled",
        _                          => "unknown",
    };
    LobbyMeter.ReadyCheckCompleted.Add(1,
        new KeyValuePair<string, object?>(GameKitTelemetry.AttrCheckResult, checkResult));
}
```

**New using directives**:
```csharp
using System.Diagnostics;
using GameKit.Core.Telemetry;
using GameKit.Lobby.Telemetry;
```

---

### EXTEND: `src/GameKit.Lobby/AssemblyInfo.cs`

**Analog:** `src/GameKit.Matchmaking/AssemblyInfo.cs` (lines 12–14).

Add `InternalsVisibleTo` grants for `LobbyMeter` (internal static class):
```csharp
// Phase 15: LobbyMeter is internal — test assemblies need InternalsVisibleTo to subscribe MeterListener.
// GameKit.Lobby.Tests and GameKit.Lobby.Integration.Tests are already present (lines 8–9).
// No new lines needed — the existing grants cover Phase 15 test access to LobbyMeter.
```

Verify existing `AssemblyInfo.cs` lines 8–9 already include `GameKit.Lobby.Tests` and `GameKit.Lobby.Integration.Tests` — confirmed present.

---

### EXTEND: `src/GameKit.Rankings/AssemblyInfo.cs`

**Analog:** `src/GameKit.Matchmaking/AssemblyInfo.cs` pattern.

`GameKit.Rankings.Tests` is already in line 9. No new grants needed for `RankingsMeter` — existing test grant covers it.

---

## Shared Patterns

### MeterListener PII Tag-Key Test Pattern
**Source:** `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` (lines 29–93)
**Apply to:** `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs`, `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs`, `tests/GameKit.Lobby.Tests/Telemetry/LobbyPiiTagKeyTests.cs`

```csharp
// Copy this exact structure; replace MeterName + instrument names per package:
[Trait("Category", "Unit")]
public sealed class {Package}PiiTagKeyTests
{
    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ticketId", "ticket_id", "playerId", "player_id",
        "sessionId", "session_id", "matchId", "match_id",
        "userId", "user_id", "email", "token", "fingerprint",
    };

    [Fact]
    public void NoInstrument_EmitsTagKey_MatchingForbiddenSet()
    {
        var emittedTagKeys = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == {Package}Meter.MeterName)
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags) emittedTagKeys.Add(tag.Key);
        });
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags) emittedTagKeys.Add(tag.Key);
        });
        listener.Start();   // MUST be called BEFORE exercising instruments

        // Exercise all instruments with their allowed tag keys:
        // ... per-package instrument Add/Record calls ...
        listener.RecordObservableInstruments();   // trigger ObservableGauge callbacks

        Assert.Empty(emittedTagKeys.Where(k => ForbiddenKeys.Contains(k)));
    }
}
```

**Critical:** `listener.Start()` must precede instrument calls. `MatchmakingMeter`/`RankingsMeter`/`LobbyMeter` are static — listener must be wired before any code exercises the instruments. See `TicketEventChannelDropTests` line 51 for the ordering.

---

### W3C Trace Propagation Test Pattern
**Source:** to create as `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs`
**Analog for test structure:** `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs`

```csharp
// ActivityListener for span tag key / parentage assertion:
using var activityListener = new ActivityListener
{
    ShouldListenTo = source => source.Name == MatchmakingActivitySource.SourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => { /* collect activity.ParentId, activity.Links */ },
};
ActivitySource.AddActivityListener(activityListener);
```

---

### Reflection Enforcement Test Pattern
**Source:** `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` (lines 106–180)
**Apply to:** extend existing file with `LoadRankingsAssembly()` + `LoadLobbyAssembly()` helpers mirroring `LoadMatchmakingAssembly()`.

```csharp
// Copy LoadMatchmakingAssembly() (lines 106–147) and adapt:
// - "GameKit.Rankings" directory + "GameKit.Rankings.dll"
// - "GameKit.Lobby" directory + "GameKit.Lobby.dll"
// Then add reflection [Fact] tests for:
//   RankingsActivitySource.SourceName == GameKitTelemetry.RankingsTickerSourceName  (already exists)
//   RankingsMeter.MeterName == GameKitTelemetry.RankingsMeterName                   (NEW)
//   LobbyActivitySource.SourceName == GameKitTelemetry.LobbySourceName             (NEW)
//   LobbyMeter.MeterName == GameKitTelemetry.LobbyMeterName                        (NEW)
```

---

### Dashboard PromQL Correction Pattern
**Source:** `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json` + `ticker-health.json`
**Apply to:** both JSON files (find/replace `gamekit_` prefix in PromQL expressions) AND `otel-collector-config.yml`

**Approach A (preferred — minimal dashboard change):** Add `namespace: gamekit` to the prometheus exporter in `otel-collector-config.yml`:
```yaml
exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"
    namespace: gamekit          # ADD THIS — makes all OTel metrics appear as gamekit_* in Prometheus
```

**Approach B (alternative):** Strip `gamekit_` prefix from all PromQL strings in both JSON files (more dashboard edits, higher risk of missing one).

**Dashboard name corrections needed regardless of approach** — the existing dashboard PromQL uses the WRONG instrument names (from Phase 13 planning, before instruments existed). After adding `namespace: gamekit` to the collector, the required metric name translations are:

| Dashboard panel PromQL (current) | Correct after namespace=gamekit |
|----------------------------------|----------------------------------|
| `gamekit_matchmaking_queue_depth` | `gamekit_matchmaking_queue_depth_tickets` (or confirm unit suffix) |
| `increase(gamekit_matchmaking_matches_formed_total[5m])` | `increase(gamekit_matchmaking_matches_formed_total[5m])` — correct if namespace adds prefix |
| `increase(gamekit_matchmaking_budget_bail_total[5m])` | `increase(gamekit_matchmaking_ticker_budget_bail_total[5m])` |
| `histogram_quantile(0.50, rate(gamekit_matchmaking_tick_duration_ms_bucket[5m]))` | `histogram_quantile(0.50, rate(gamekit_matchmaking_ticker_lag_ms_bucket[5m]))` |
| `rate(gamekit_matchmaking_lease_acquired_total[5m])` | correct |
| `rate(gamekit_matchmaking_lease_lost_total[5m])` | correct |
| `histogram_quantile(0.50, rate(gamekit_rankings_drain_ladder_duration_ms_bucket[5m]))` | `histogram_quantile(0.50, rate(gamekit_rankings_decay_duration_ms_bucket[5m]))` |

Verify actual Prometheus metric names with `curl http://prometheus:9090/api/v1/label/__name__/values` after first emission — the OTel → Prometheus name translation adds `_total` to counters and `_bucket/_count/_sum` to histograms, and replaces `.` with `_`.

---

## No Analog Found

All files have close analogs. No greenfield work without a reference pattern.

---

## Metadata

**Analog search scope:** `src/GameKit.Matchmaking/Telemetry/`, `src/GameKit.Rankings/Telemetry/`, `src/GameKit.Core/Telemetry/`, `src/GameKit.Core/Builder/`, `src/GameKit.Lobby/Hubs/`, `src/GameKit.Matchmaking/Services/`, `src/GameKit.Rankings/Services/`, `tests/GameKit.Matchmaking.Tests/Services/`, `tests/GameKit.Core.Tests/Telemetry/`
**Files scanned:** 17 source files read directly
**Pattern extraction date:** 2026-06-22
