# GameKit.Core — Concepts

## What It Does

`GameKit.Core` is the foundation package every other GameKit module builds on. It owns the
player table, the game-session lifecycle (start → complete / abandon), the GDPR delete pipeline,
rate-limiting, idempotency, and the fluent builder that wires sibling packages together. It has
no opinion about auth, rankings, or matchmaking — those concerns live in their own packages and
hook into Core through well-defined port interfaces.

## The Fluent Builder

```csharp
var gk = builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString       = builder.Configuration.GetConnectionString("GameKit")!;
    opts.MigrationConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations")!;
});
// Sibling packages extend the same builder:
gk.AddAuth(...);
gk.AddRankings(...);
```

`AddGameKit` returns an `IGameKitBuilder`. Every sibling package extends it with its own
`Add*` method so the consumer mounts only the packages they install. The builder is a
thin shell exposing `IServiceCollection Services` and `GameKitOptions Options` — there is
no magic container, just plain `Microsoft.Extensions.DependencyInjection`.

## Key Public Interfaces

### `IGameKitBuilder`

The fluent builder root returned from `services.AddGameKit(...)`. Sibling packages discover
it from DI and extend it with their own `Add*` methods. Consumers rarely implement this
interface themselves.

### `ISessionLifecycleObserver`

Cross-package observer invoked on every `game_sessions.state` transition (start → complete /
abandon). Presence, and any package that cares about in-match state, implements this port.
Runs inside the same ambient transaction as the state update — **implementations must be
idempotent**. The default registration is a no-op (Core-only installs keep functioning).

```csharp
public class MyObserver : ISessionLifecycleObserver
{
    public Task OnSessionStartedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct) { ... }
    public Task OnSessionCompletedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct) { ... }
    public Task OnSessionAbandonedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct) { ... }
}
// Register before AddGameKit or before Build():
services.AddSingleton<ISessionLifecycleObserver, MyObserver>();
```

### `IPostSessionCompleteHandler`

Narrower port called only when a session is marked **completed** (not abandoned). Rankings
implements this to enqueue rating-update work. Like `ISessionLifecycleObserver`, runs inside
the ambient transaction. Optional — Core completes sessions without it.

### `IGdprDeleteExtension`

Port for packages that have their own tables with player PII. Implement this to participate in
the `DELETE /api/players/{id}` GDPR-delete pipeline. Core calls every registered extension in
order; if any extension throws the whole pipeline rolls back.

```csharp
public class MyGdprDeleteExtension : IGdprDeleteExtension
{
    public Task DeleteAsync(Guid playerId, CancellationToken ct) { ... }
}
services.AddScoped<IGdprDeleteExtension, MyGdprDeleteExtension>();
```

### `IPlayerRatingProvider`

Read-only port that exposes a player's current Elo/Glicko rating for features that need it
(e.g. presence panels, match history UI). The default implementation is `NullPlayerRatingProvider`
— no rating is returned unless `GameKit.Rankings` is installed. Install Rankings and the
concrete implementation is registered automatically.

### `IPlayerDisplayNameResolver`

Port for custom display-name resolution. The default falls back to the player's `username`
column. Implement and register this interface to resolve display names from a custom source
(e.g. a social graph, a cache, or a third-party profile service).

### `IGameKitRateLimitPolicies`

Seam for overriding the default rate-limit policies that GameKit applies to its HTTP endpoints.
Implement and register before calling `AddGameKit(...)` to replace any or all policies. The
default policies are fixed-window (per-player, by JWT subject claim).

### `IModelBuilderExtension`

Port for packages that need to add EF Core entity configurations to the shared `DbContext`.
Each GameKit package implements this to register its own tables without touching Core's table
definitions (the migration-boundary contract from `CLAUDE.md`).

### `ILeaderLease`

Core-level distributed-lease abstraction backed by Redis `SET NX PX`. Used internally by
matchmaking and rankings to elect a single ticker leader across replicas. Consumers who need
their own distributed locks can implement custom lease logic using the same abstraction.

## Observability and Health — Core Extension Methods

Observability and health are **not** separate NuGet packages. They are opt-in extension
methods shipped in `GameKit.Core`:

```csharp
// Opt-in OpenTelemetry traces + metrics (registers ActivitySource + Meter; no exporter forced):
gk.AddGameKitObservability(otel =>
{
    otel.OtlpEndpoint = configuration["GameKit:Observability:OtlpEndpoint"]; // null = no export
});

// Health checks (Postgres + Redis liveness probes + migration-readiness check):
gk.AddGameKitHealthChecks();

// Mount /health/live + /health/ready endpoints:
app.MapGameKitHealth();
```

Calling `AddGameKitObservability` does not force any exporter on the host — it only registers
`ActivitySource("GameKit.*")` and `Meter("GameKit.*")` sources so the host's own OTel SDK can
pick them up if configured.

## Library-vs-Consumer Responsibility Line

| GameKit.Core owns | Consumer owns |
|-------------------|---------------|
| Player schema + CRUD endpoints | Custom player metadata (via JSONB `metadata` column) |
| Session lifecycle (start / complete / abandon) | Session-complete business logic (`IPostSessionCompleteHandler`) |
| GDPR delete pipeline orchestration | Per-package data deletion (`IGdprDeleteExtension`) |
| Rate-limit enforcement | Rate-limit policy configuration (`IGameKitRateLimitPolicies`) |
| DB migrations for Core tables | Adding columns or new tables (NOT Core tables) in sibling packages |
| Distributed lease primitive | Custom lock use-cases (`ILeaderLease`) |

## Minimal Wire-Up

```csharp
// Program.cs
var gk = builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString          = config.GetConnectionString("GameKit")!;
    opts.MigrationConnectionString = config.GetConnectionString("GameKitMigrations")!;
});
gk.AddGameKitObservability();   // optional
gk.AddGameKitHealthChecks();    // optional

var app = builder.Build();
app.UseGameKit();
app.MapGameKit();
app.MapGameKitHealth();         // /health/live + /health/ready
await app.RunAsync();
```

## See Also

- [API reference](../../api/GameKit.Core.yml) — full member-level docs generated from XML comments.
- [docs/ops/migrations-runbook.md](../ops/migrations-runbook.md) — migration operations.
- [docs/security-checklist.md](../security-checklist.md) — rate-limit and JWT hardening.
