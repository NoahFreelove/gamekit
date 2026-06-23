# GameKit.Lobby — Concepts

## What It Does

`GameKit.Lobby` provides pre-game lobby rooms with real-time membership and ready-check state
via a SignalR hub backed by a Redis backplane (for multi-replica support). Each lobby has a
REST API for CRUD and membership management, and a SignalR hub (`/hubs/lobby`) for real-time
events (chat, state updates). Lobby state is durable in Postgres; chat messages are
**ephemeral** — they relay through SignalR and are never written to the database.

The ready-check flow is automatic: when the lobby fills to its `MaxMembers` cap, it
transitions from `Open` to `ReadyChecking` and notifies all members. When every member marks
themselves ready, it transitions to `Ready` and the game server can start a session.

## Key Public Interfaces

### `ILobbyService`

The application service for lobby lifecycle and membership:

```csharp
public interface ILobbyService
{
    Task<Lobby> CreateLobbyAsync(Guid ownerId, int? maxMembers, Guid? ladderId, string? regionName, CancellationToken ct);
    Task JoinLobbyAsync(Guid lobbyId, Guid playerId, CancellationToken ct);
    Task LeaveLobbyAsync(Guid lobbyId, Guid playerId, CancellationToken ct);
    Task MarkReadyAsync(Guid lobbyId, Guid playerId, CancellationToken ct);
    // … additional membership and query methods
}
```

State transitions use SERIALIZABLE transactions to prevent concurrent double-transitions
(e.g. two players' `MarkReadyAsync` calls both seeing the second-to-last state).

### `ILobbyMessageHandler`

The extension seam for chat message handling — a relay/gate invoked before each chat message
is broadcast to the SignalR group:

```csharp
public interface ILobbyMessageHandler
{
    Task<bool> OnMessageAsync(Guid lobbyId, Guid senderId, string message, CancellationToken ct);
    // Returns true to relay the message; false to suppress it.
}
```

The default implementation is `NullLobbyMessageHandler` (always relays). Replace it to add
rate-limiting, content moderation, or structured logging:

```csharp
services.AddSingleton<ILobbyMessageHandler, MyRateLimitedHandler>();
// Register before AddLobby() — AddLobby uses TryAddSingleton
```

Note: `ILobbyMessageHandler` must **not** write messages to durable storage — chat is
intentionally ephemeral (no persistence contract).

### `ILobbyClient`

The typed SignalR client interface — defines the methods the server pushes to connected lobby
members:

```csharp
public interface ILobbyClient
{
    Task ReceiveChatMessageAsync(Guid senderId, string message);
    Task ReceiveStateUpdateAsync(LobbyStateUpdate update);
}
```

Used via `IHubContext<LobbyHub, ILobbyClient>` when server-side code needs to push updates
outside the hub (e.g. from `ILobbyService` after a state transition).

## Wire-Up

```csharp
gk.AddLobby();    // registers ILobbyService + ILobbyMessageHandler (null default)
                  // registers SignalR + Redis backplane (StackExchange.Redis)

// In the pipeline:
app.MapLobby();   // /api/lobbies REST + /hubs/lobby SignalR hub
```

For multi-replica deployments, add the Redis backplane before calling `AddLobby()`:

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(config.GetConnectionString("Redis")!);
gk.AddLobby();
```

## Library-vs-Consumer Responsibility Line

| GameKit.Lobby owns | Consumer owns |
|--------------------|---------------|
| Lobby CRUD + membership enforcement | Lobby invite UX in the game client |
| Ready-check state machine | Decision of when to start a session after `Ready` |
| Ephemeral chat relay via SignalR | Message moderation / rate-limiting (`ILobbyMessageHandler`) |
| Redis backplane for multi-replica SignalR | Redis connection string configuration |
| Lobby→session handoff (emits `Ready` state) | Game-server session start call (`POST /api/sessions/{id}/start`) |

## Security Notes

- Chat messages are relayed verbatim. GameKit does **not** sanitize content. The game client
  is responsible for safe rendering (HTML-escape before inserting into the DOM).
- The `ILobbyMessageHandler` suppress return value (`false`) is the correct place for
  server-side rate-limiting or content filtering.

## See Also

- [API reference](../api/GameKit.Lobby.yml) — full member-level docs.
- [docs/architecture/signalr-multi-replica.md](../architecture/signalr-multi-replica.md) — multi-replica SignalR topology.
- [docs/ops/redis-aof.md](../ops/redis-aof.md) — Redis durability configuration.
