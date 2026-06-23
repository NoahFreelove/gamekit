# GameKit.Presence — Concepts

## What It Does

`GameKit.Presence` tracks each player's online status in Redis with a configurable TTL. Status
has three states: `online` (heartbeat fresh), `in_match` (in an active game session), and
`offline` (heartbeat expired or never sent). The in-match state is set and cleared
automatically through `ISessionLifecycleObserver` — when a session starts, all participants
become `in_match`; when the session completes or is abandoned, they revert to `online` (if
their heartbeat is still fresh) or `offline`.

The `in_match` state has precedence over a concurrent heartbeat: if a player sends a heartbeat
while in a session, the TTL is refreshed but the value is not downgraded to `online`. This
precedence rule is enforced atomically via a Lua script.

## Key Public Interfaces

### `IPresenceWriter`

The write-side port — the extension seam for consumers who need custom presence state logic
or a custom backing store:

```csharp
public interface IPresenceWriter
{
    ValueTask WriteHeartbeatAsync(Guid playerId, CancellationToken ct);
    ValueTask WriteInMatchAsync(Guid playerId, CancellationToken ct);
    ValueTask WriteOnlineAsync(Guid playerId, CancellationToken ct);
    ValueTask ClearInMatchAsync(Guid playerId, CancellationToken ct);
}
```

The default implementation is `RedisPresenceProvider`. Replace it to route presence writes to
a custom store (e.g. a game-specific Redis cluster, a presence microservice, or a custom TTL
strategy):

```csharp
services.AddSingleton<IPresenceWriter, MyPresenceWriter>();
gk.AddPresence();   // uses TryAddSingleton — your registration wins
```

All four methods are idempotent. `WriteHeartbeatAsync` and `WriteInMatchAsync` use atomic
Redis primitives to prevent race conditions between the heartbeat endpoint and the
session-lifecycle observer.

### `IPresenceProvider` (read side — lives in `GameKit.Core`)

The read-only query port for presence — used by other packages (lobby, admin panel, etc.) to
render presence indicators without coupling to the Redis write path. Defined in
`GameKit.Core` so packages that render presence information do not take a dependency on
`GameKit.Presence` directly.

## Wire-Up

```csharp
gk.AddPresence();   // registers RedisPresenceWriter + ISessionLifecycleObserver adapter

// In the pipeline:
app.MapPresence();  // POST /api/presence/heartbeat (JWT-bearer required)
```

The heartbeat endpoint requires a valid JWT. The TTL is configured via:

```csharp
gk.AddPresence(opts =>
{
    opts.HeartbeatTtl = TimeSpan.FromSeconds(30);  // default: 30 s
});
```

## Library-vs-Consumer Responsibility Line

| GameKit.Presence owns | Consumer owns |
|-----------------------|---------------|
| Presence write/read against Redis | Custom write strategy (`IPresenceWriter`) |
| In-match / online / offline state machine | Game-client heartbeat polling cadence |
| Session lifecycle integration (ISessionLifecycleObserver) | None — automatic |
| Atomic Lua script for in-match precedence | None |
| TTL configuration | `HeartbeatTtl` value (via `AddPresence(opts => ...)`) |

## See Also

- [API reference](../api/GameKit.Presence.yml) — full member-level docs.
- [core.md](core.md) — `ISessionLifecycleObserver` and `IPresenceProvider` in Core.
- [docs/ops/redis-aof.md](../ops/redis-aof.md) — Redis durability configuration.
