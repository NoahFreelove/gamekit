# GameKit.Matchmaking — Concepts

## What It Does

`GameKit.Matchmaking` implements a Redis-backed matchmaking queue with a periodic ticker,
party support, proposal accept/decline flow, backfill, and admin controls. It stores
durable ticket state in Postgres and live queue state in Redis sorted sets, with a background
reconciler that keeps them in sync. A single-instance leader election using `IMatchmakerLease`
ensures only one ticker runs across replicas.

## The Replaceable Strategy

The canonical extension point is **`IMatchmakingStrategy`**:

```csharp
public interface IMatchmakingStrategy
{
    string Name { get; }   // matched against ladder config, e.g. "elo-range", "skill-band"
    MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now);
}
```

For each pool, the ticker picks the `IMatchmakingStrategy` whose `Name` matches the ladder's
configuration and calls `Match` for each candidate party. If `Match` returns a `MatchResult`,
a proposal is created and sent to the matched players.

**Default:** `EloRangeMatchmakingStrategy` (`Name = "elo-range"`) — expands the Elo bracket
linearly over wait time so long-waiting players eventually match anyone. Implementations must
be **stateless, thread-safe, and deterministic** for bracket-overlap logic.

```csharp
// Register a custom strategy before AddMatchmaking():
services.AddSingleton<IMatchmakingStrategy, SkillBandStrategy>();

// Configure the ladder to use it:
gk.AddMatchmaking(mm =>
{
    mm.AddLadder("ranked", ladder =>
    {
        ladder.StrategyName = "skill-band";  // matches SkillBandStrategy.Name
        ladder.TickIntervalMs = 500;
    });
});
```

## Pool Names and the Default Pool

Tickets enqueue into a named pool within a ladder. When `PoolName` is `null` in the enqueue
request, tickets route to the `"default"` pool. Two tickets only compare for a match if they
are in the same ladder **and** the same pool. This is the most common source of "match never
forms" bugs — ensure both enqueue requests use the same `PoolName` (or both use `null`).

## Key Public Interfaces

### `IMatchmakingStrategy`

The primary strategy seam. Replace `EloRangeMatchmakingStrategy` with any algorithm that
receives a candidate party + pool snapshot and returns a `MatchResult` or `null`. See the
interface XML doc for the statelessness, thread-safety, and determinism requirements.

### `IMatchmakerTicker`

Drives the matchmaking loop — iterates the Redis sorted sets, calls the configured strategy,
emits proposals, handles TTL-expired proposals. Not intended for consumer implementation;
exposed for testing and observability.

### `IMatchmakerLease`

Extends `ILeaderLease` from Core — the Redis distributed lock that ensures a single ticker
leader per ladder across replicas. Automatically managed; not intended for consumer implementation.

### `IProposalService`

Handles the accept/decline flow after the ticker emits a proposal:
- `AcceptAsync` — runs an atomic Lua script to record the accept; when all tickets accept,
  inserts the `GameSession` and emits a `Matched` event.
- `DeclineAsync` — re-queues accepting partner tickets with their original queue-at score
  (no queue-position penalty for innocent parties), records a cooldown for the decliner.

### `IBackfillService`

Fills empty team slots in an existing in-progress session (e.g. a player disconnected). Not
used in the default TicTacToeDuel flow; available for games with backfill semantics.

### `IMatchmakingControlService`

Admin control surface for per-ladder pause and drain operations. Exposes `PauseAsync` and
`DrainAsync`, each writing a Redis flag plus an audit row. Called by the admin UI and by
`POST /admin/api/matchmaking/{ladderId}/pause` and `/drain`.

### `IPartyCodeGenerator`

Generates short human-readable party invitation codes. Default implementation uses a
cryptographically random alphanumeric string. Replace for custom code formats (e.g. shorter
codes, specific character sets).

### `IPartyService`

Manages party lifecycle — create, join by invite code, leave. Parties are the unit that
enters the queue together; a solo player is a one-member party.

### `IGameKitMatchmakingBuilder`

Sub-builder returned from `gk.AddMatchmaking(...)`. Exposes ladder configuration methods
(`AddLadder`, ticker interval, queue reconciler interval).

## Wire-Up

```csharp
gk.AddMatchmaking(mm =>
{
    mm.AddLadder("tictactoe", ladder =>
    {
        ladder.StrategyName      = "elo-range";  // default
        ladder.PlayersPerMatch   = 2;
        ladder.TickIntervalMs    = 500;
        // PoolName null → "default" pool
    });
});

// In the pipeline:
app.MapMatchmaking();  // /api/parties/* + /api/mm/*
```

## Library-vs-Consumer Responsibility Line

| GameKit.Matchmaking owns | Consumer owns |
|--------------------------|---------------|
| Redis queue + ticker loop | Custom matching strategy (`IMatchmakingStrategy`) |
| Proposal state machine (accept / decline / TTL) | Game-client accept/decline UI |
| Party lifecycle + invite codes | Custom invite code format (`IPartyCodeGenerator`) |
| Queue reconciliation (Redis ↔ Postgres) | Pool name consistency in enqueue requests |
| Leader election across replicas | None — managed by `IMatchmakerLease` |
| Backfill slot management | Decision of when/whether to trigger backfill |
| Admin pause/drain | Operational decisions on when to pause |

## See Also

- [API reference](../../api/GameKit.Matchmaking.yml) — full member-level docs.
- [docs/runbooks/matchmaking-outage.md](../runbooks/matchmaking-outage.md) — incident response.
- [docs/performance-tuning.md](../performance-tuning.md) — ticker interval and queue tuning.
- [docs/ops/redis-aof.md](../ops/redis-aof.md) — Redis durability for queue state.
