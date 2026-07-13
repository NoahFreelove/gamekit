# Phase 16: Multi-Replica Hardening — Research

**Researched:** 2026-06-22
**Domain:** Distributed leader election, graceful ASP.NET Core shutdown, Postgres idempotency, SignalR Redis backplane correctness, Testcontainers multi-replica test patterns
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

All implementation choices are at Claude's discretion — discuss phase was skipped per user setting. Use the ROADMAP phase goal, success criteria, and existing codebase conventions to guide decisions.

### Claude's Discretion

All design choices: where to put ILeaderLease, migration strategy for idempotency_key column, test project location, CI gate mechanism, and every other implementation detail.

### Deferred Ideas (OUT OF SCOPE)

None — discuss phase skipped.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SCALE-01 | `ILeaderLease` abstraction in `GameKit.Core` unifies three existing lease helpers without changing the `SET NX PX` mechanism | §Lease Inventory + §ILeaderLease Design |
| SCALE-02 | Graceful shutdown — on SIGTERM, in-flight HTTP requests drain, active ticker iteration completes within stop deadline, leader lock released with `CancellationToken.None` on finally paths | §Graceful Shutdown |
| SCALE-03 | Idempotent match/session creation — Postgres `INSERT … ON CONFLICT DO NOTHING` + idempotency key; concurrent `SessionCompleteAsync` calls produce exactly one row | §Idempotency Design |
| SCALE-04 | Leader-election split-brain integration test (`MatchmakerSplitBrainTests`): two replicas, lease expiry mid-tick, zero duplicate matches, no ticker gap longer than one lock TTL — CI gate | §Test Architecture |
| SCALE-05 | Graceful-drain integration test: 100 concurrent requests + SIGTERM → zero 5xx + zero duplicate matches | §Test Architecture |
| SCALE-06 | Lobby + Admin SignalR multi-replica correctness under replica restart and Redis reconnect; sticky-session requirement documented for operators | §SignalR Backplane |
</phase_requirements>

---

## Summary

Phase 16 has three distinct work tracks that can proceed in parallel after a shared foundation wave: (1) the `ILeaderLease` unification in `GameKit.Core`; (2) graceful-drain and idempotency hardening in existing services; and (3) multi-replica integration tests for split-brain, drain, and SignalR fan-out.

The codebase already contains three structurally near-identical lease helpers that each duplicate the same Polly v8 retry pipeline, `InstanceId` property, and `ReleaseLeaseAsync` pattern. None of them share a common interface with `GameKit.Core`. `IMatchmakerLease` (in `GameKit.Matchmaking`) is the only existing interface; `RankDecayLeaseHelper` and `RankingsTickerLeaseHelper` are concrete classes with no interface at all. The `ILeaderLease` interface belongs in `GameKit.Core.Services` so all per-package implementations can implement it without creating circular dependencies.

The graceful-drain SIGTERM problem is precise: every `ReleaseLeaseAsync` call on a `finally` path currently passes `ct` (the stopping token), which is already cancelled by the time the `finally` runs. This means lease release silently no-ops in StackExchange.Redis when the token is cancelled — the lock then hangs until TTL expiry (90 s default), stalling leader re-election on the surviving replica. The fix is a one-line change per finally path: replace `ct` with `CancellationToken.None`. The Lua-script-verified `LockReleaseAsync` is itself synchronous at the Redis protocol level, so the only cancellation risk is the connection-level one handled by StackExchange.Redis internally.

For idempotent session creation (SCALE-03), the success criterion asks for `INSERT … ON CONFLICT DO NOTHING` on `game_sessions`, but the existing architecture uses a state-conditional `ExecuteUpdateAsync WHERE state = Active` for concurrent dedup. The clarification needed: SCALE-03 is about the **match formation** write path (the `MatchmakerTickerService` → `SessionStartService` path that creates the `game_sessions` row), not the `SessionCompleteAsync` path. The completion path already has idempotency via `session_complete_idempotency`. The split-brain risk is that two replicas both believe they are leader momentarily and both write a `game_sessions` row for the same match. The atomic Lua claim script (`AtomicClaimScript`) already prevents double-formation at the Redis level; SCALE-03 adds a Postgres-level safety net: an idempotency key stored on the `game_sessions` row (or a separate `game_session_formation` unique constraint on `proposal_id`) so that even if the Lua script fails to prevent the double write, the Postgres `ON CONFLICT DO NOTHING` is the last guard.

**Primary recommendation:** Add `ILeaderLease` to `GameKit.Core.Services` mirroring `IMatchmakerLease`'s surface; adapt the three helpers to implement it; fix all `finally` release paths to use `CancellationToken.None`; add a `UNIQUE(idempotency_key)` constraint to `game_sessions` with a new migration; extend the existing two-replica `BackplaneTests` pattern for the split-brain and drain test classes; add the CI gate as a required xUnit test trait.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| `ILeaderLease` interface | `GameKit.Core.Services` | — | Core owns cross-package abstractions; per-package implementations reference Core, not vice versa |
| Matchmaking ticker lease | `GameKit.Matchmaking.Services.MatchmakerLeaseHelper` | Core interface | Already implements `IMatchmakerLease`; will additionally implement `ILeaderLease` |
| Rankings decay lease | `GameKit.Rankings.Services.RankDecayLeaseHelper` | Core interface | Currently no interface; gains `ILeaderLease` |
| Rankings ticker lease | `GameKit.Rankings.Services.RankingsTickerLeaseHelper` | Core interface | Currently no interface; gains `ILeaderLease` |
| Graceful drain / SIGTERM | ASP.NET Core host (`IHostApplicationLifetime`) | `BackgroundService.StopAsync` | ASP.NET Core drains HTTP requests; `BackgroundService` `ExecuteAsync` sees `stoppingToken` cancelled |
| `game_sessions` idempotency | Postgres (`UNIQUE` constraint) + `GameKit.Core` migration | `GameKit.Matchmaking` (sets the key) | Constraint in Core schema boundary; key set by Matchmaking at match-formation time |
| SignalR multi-replica fan-out | Redis backplane (`AddStackExchangeRedis`) | `GameKit.Lobby` + `GameKit.Admin.UI` | Both already wire the backplane via `IPostConfigureOptions<RedisOptions>` |
| Split-brain CI tests | `GameKit.Matchmaking.Integration.Tests` (new test class) | `GameKit.TestFixtures` | Mirrors existing `BackplaneTests` two-host pattern |
| SignalR reconnect test | `GameKit.Lobby.Integration.Tests` (extend `BackplaneTests`) | `GameKit.TestFixtures` | `BackplaneTests` already has two-app + shared Redis; extend with restart/reconnect |

---

## Lease Inventory: Existing Code (Grounded in Src)

### Three Lease Helpers — Current State

**1. `GameKit.Matchmaking.Services.MatchmakerLeaseHelper`**
- File: `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs`
- Implements: `IMatchmakerLease` (in `GameKit.Matchmaking.Services`)
- Methods: `TryAcquireLeaseAsync(CancellationToken)`, `RenewLeaseAsync(CancellationToken)`, `ReleaseLeaseAsync(CancellationToken)`, `QueryLeaseAsync(CancellationToken)` — plus `QueryLeaseScript` Lua snippet
- Lock key: `_opts.Ticker.LockKey` (default `"gamekit:matchmaking:matcher:lock"`)
- Polly pipeline: 3 retries, exponential + jitter, handles `RedisConnectionException`/`RedisTimeoutException`
- Release in ticker finally (line 292): `await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false)` — **`ct` is the stopping token** — BUG to fix

**2. `GameKit.Matchmaking.Services.RedisMatchmakerLease`**
- File: `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs`
- Implements: `IMatchmakerLease`
- Methods: same four as above; simpler implementation (no Polly retry)
- This is the reconciler/retention default; `MatchmakerLeaseHelper` replaces it at runtime

**3. `GameKit.Rankings.Services.RankDecayLeaseHelper`**
- File: `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs`
- Implements: **no interface** — concrete class only [VERIFIED: read from src/]
- Methods: `TryAcquireLeaseAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` (all `CancellationToken`)
- Lock key: `_opts.Decay.LockKey` (default `"gamekit:rankings:decay:lease"`)
- Polly pipeline: identical structure to `MatchmakerLeaseHelper`
- Release in `RankDecayBackgroundService` finally (line 189): `await _lease.ReleaseLeaseAsync(ct)` — **`ct` is the stopping token** — BUG to fix

**4. `GameKit.Rankings.Services.RankingsTickerLeaseHelper`**
- File: `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs`
- Implements: **no interface** — concrete class only [VERIFIED: read from src/]
- Methods: `TryAcquireLeaseAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` (all `CancellationToken`)
- Lock key: `_opts.Ticker.LockKey` (default `"gamekit:rankings:ticker:lease"`)
- Polly pipeline: identical structure

**Missing from all three:** `QueryLeaseAsync` on the Rankings helpers (only Matchmaking helpers have it). The `ILeaderLease` interface should include `QueryLeaseAsync` for the health check surface.

**Missing from `RankDecayLeaseHelper` and `RankingsTickerLeaseHelper`:** `InstanceId` is exposed as a public property but the release on finally paths passes `ct` (stopping token), not `CancellationToken.None`.

### Existing Interface: `IMatchmakerLease`
- File: `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs`
- Lives in: `GameKit.Matchmaking.Services` namespace
- Surface: `InstanceId { get; }`, `TryAcquireLeaseAsync(ct)`, `ReleaseLeaseAsync(ct)`, `QueryLeaseAsync(ct)`
- **Does NOT include `RenewLeaseAsync`** — renewal is per-helper without interface contract

The SCALE-01 `ILeaderLease` interface in `GameKit.Core` should include `RenewLeaseAsync` since the Rankings helpers expose it and the health-check pattern will need it.

---

## ILeaderLease Design

### Recommended Interface (place in `GameKit.Core.Services`)

```csharp
// src/GameKit.Core/Services/ILeaderLease.cs
namespace GameKit.Core.Services;

/// <summary>
/// Common abstraction for distributed leader-election leases backed by Redis
/// SET NX PX. Implemented by per-package lease helpers; registered in DI as the
/// concrete type (not this interface) per-package — consumers resolve the concrete
/// helper, which also satisfies this interface for health checks and auditing.
/// </summary>
public interface ILeaderLease
{
    /// <summary>Fencing token — unique per process, format <c>MachineName:Guid</c>.</summary>
    string InstanceId { get; }

    /// <summary>Try to acquire the leader lock. Returns true if acquired.</summary>
    Task<bool> TryAcquireLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Extend the lock TTL mid-run. Returns false when lease expired (caller MUST stop).
    /// </summary>
    Task<bool> RenewLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Release the lock. Lua-script-verified. MUST be called with
    /// <c>CancellationToken.None</c> on all finally paths to survive shutdown.
    /// </summary>
    Task ReleaseLeaseAsync(CancellationToken ct);

    /// <summary>
    /// Non-acquiring read of current holder + TTL. Used by health checks.
    /// </summary>
    Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct);
}
```

`LeaseStatus` is already defined in `GameKit.Matchmaking.Services` — move it to `GameKit.Core.Services` so it is accessible without the Matchmaking reference. The `IMatchmakerLease` in `GameKit.Matchmaking` becomes a `[Obsolete]`-annotated alias or is removed and replaced by `ILeaderLease` directly.

### Adaptation Plan

| Helper | Change |
|--------|--------|
| `MatchmakerLeaseHelper` | Add `: ILeaderLease` (already has all four methods + `InstanceId`) |
| `RedisMatchmakerLease` | Add `: ILeaderLease`; add `RenewLeaseAsync` stub (returns `false` — this helper does not renew) |
| `RankDecayLeaseHelper` | Add `: ILeaderLease`; add `QueryLeaseAsync` |
| `RankingsTickerLeaseHelper` | Add `: ILeaderLease`; add `QueryLeaseAsync` |
| `IMatchmakerLease` in Matchmaking | Extend `ILeaderLease` or replace with type alias |
| `MatchmakingLeaderHealthCheck` | Change from `IMatchmakerLease` to `ILeaderLease` |

The SCALE-01 grep check: `grep -r "LockTakeAsync" src/` must show zero results outside a class that implements `ILeaderLease`. Currently `LockTakeAsync` appears only inside the four helpers listed above — no raw Redis calls in service layer.

---

## Graceful Shutdown

### .NET 10 / ASP.NET Core 10 Shutdown Sequence [ASSUMED]

1. SIGTERM received → `IHostApplicationLifetime.ApplicationStopping` fires
2. ASP.NET Core Kestrel stops accepting new connections; in-flight requests drain up to `HostOptions.ShutdownTimeout` (default 5 s in .NET 6+; configurable)
3. `IHostedService.StopAsync(stoppingToken)` called for each hosted service in reverse registration order
4. `BackgroundService.ExecuteAsync` observes `stoppingToken` cancellation
5. Host stops

### The Stopping-Token Bug in `finally` Paths

When `BackgroundService.StopAsync` is called, the `stoppingToken` passed to `ExecuteAsync` is cancelled. The `finally` block in `RunOnceAsync` (or the equivalent in Rankings) receives the already-cancelled `ct`. The `ReleaseLeaseAsync` implementation calls `_redis.GetDatabase().LockReleaseAsync(key, value)` — StackExchange.Redis respects the `CancellationToken` and throws `OperationCanceledException` before the Redis command is sent. The lock is NOT released; it expires after `LockTtlSeconds` (90 s default). The surviving replica cannot become leader for 90 s.

**Fix:** Replace `ct` with `CancellationToken.None` on every `ReleaseLeaseAsync` call inside a `finally` block. The `LockReleaseAsync` Lua script is a single atomic Redis command (~1 ms RTT) — it does not need a cancellation budget; passing `CancellationToken.None` is safe.

### Files That Need the Fix

| File | Line | Change |
|------|------|--------|
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | 292 | `ReleaseLeaseAsync(ct)` → `ReleaseLeaseAsync(CancellationToken.None)` |
| `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs` | 180 | same |
| `src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs` | 171 | same |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` | 189 | same |
| `src/GameKit.Rankings/Services/RankingsTickerService.cs` | 199 | same |

After the fix, a grep assertion in the drain integration test can verify: `grep -r "ReleaseLeaseAsync(ct)" src/` returns zero hits in any `finally` block.

### ShutdownTimeout Configuration

The default `HostOptions.ShutdownTimeout` is 5 s in .NET 8+. For a ticker with a 500 ms interval, the current tick will complete (at most ~50 ms per RESEARCH §MaxIterationBudgetMs), then the timer's `WaitForNextTickAsync` returns false and the loop exits — well within 5 s. No configuration change needed for normal cases. Document the operator recommendation: set `ShutdownTimeout` to at least `LockTtlSeconds / 2` if the ticker budget is ever increased significantly. [ASSUMED — ShutdownTimeout default confirmed in prior .NET 8 research but not re-verified from official .NET 10 docs this session]

---

## Idempotency Design (SCALE-03)

### Current State

`game_sessions` schema (from `20260415000000_CoreInitial.cs`):
- PK: `Id` (UUID, `ValueGeneratedNever`)
- No unique constraint on `proposal_id` or any idempotency key
- `SessionCompleteService` uses `ExecuteUpdateAsync WHERE state = Active` for concurrent dedup on completion — this does NOT protect against double-creation

`SessionCompleteIdempotency` (`src/GameKit.Rankings/Entities/SessionCompleteIdempotency.cs`) provides idempotency for `POST /sessions/{id}/complete` via composite PK `(session_id, idempotency_key)`. This is the completion path — not the creation path.

The split-brain creation risk: if two replicas both believe they hold the leader lease for a brief window (a race between lease expiry and renewal), both could run the Lua atomic claim script. The Lua script (`AtomicClaimScript`) in Matchmaking already guards against this by checking the lease value before writing the proposal — `if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 'LEASE_LOST' end`. This is the primary guard.

SCALE-03 asks for a Postgres-level secondary guard on `game_sessions` creation. The cleanest approach: add an `idempotency_key` column to `game_sessions` (or a `proposal_id` FK-equivalent) with a `UNIQUE` constraint. When `MatchmakingService.AcceptProposalAsync` creates the `game_sessions` row, it sets the `idempotency_key` to the `ProposalId`. A concurrent second write for the same proposal will hit the unique index and produce a Postgres `23505` unique-violation — handled with `ON CONFLICT DO NOTHING` or by catching the exception and returning the existing row.

### Recommended Implementation

**Migration:** New `GameKit.Core` migration `20260622000000_AddGameSessionIdempotencyKey` (timestamp follows the `AddAuditActorIdFk` migration at `20260606100000`):
```sql
ALTER TABLE gamekit.game_sessions ADD COLUMN "IdempotencyKey" varchar(128) NULL;
CREATE UNIQUE INDEX uq_game_sessions_idempotency_key
  ON gamekit.game_sessions ("IdempotencyKey")
  WHERE "IdempotencyKey" IS NOT NULL;
```
Nullable with a partial index allows the column to be added non-destructively to all existing rows.

**Entity change:** Add `public string? IdempotencyKey { get; set; }` to `GameSession`.

**Write path:** In `MatchmakingService.AcceptProposalAsync` (or `SessionStartService.StartAsync`), set `IdempotencyKey = proposal.ProposalId.ToString()` when creating the session. The Npgsql write will throw `PostgresException` code `23505` on duplicate — wrap in a try/catch and treat as success (the session already exists).

**EF Core pattern for `ON CONFLICT DO NOTHING`:** Use raw SQL via `ExecuteSqlRawAsync`:
```sql
INSERT INTO gamekit.game_sessions ("Id","State","LadderId","IdempotencyKey","CreatedAt")
VALUES (@id, 'Pending', @ladderId, @idempotencyKey, @now)
ON CONFLICT ("IdempotencyKey") DO NOTHING
RETURNING "Id"
```
Or: let EF Core do the `INSERT` and catch `DbUpdateException` → `NpgsqlException` with `SqlState == "23505"` and `ConstraintName == "uq_game_sessions_idempotency_key"`.

The raw `ON CONFLICT DO NOTHING` approach is cleaner for the test assertion (the dedicated Testcontainers test in success criterion #4 can verify the duplicate call returns 0 inserted rows).

---

## SignalR Backplane (SCALE-06)

### Current Wiring

**GameKit.Lobby:**
- `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` (line 89–93): `builder.Services.AddSignalR().AddStackExchangeRedis(options => { options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit"); })`
- `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs`: `IPostConfigureOptions<RedisOptions>` that defers `IConnectionMultiplexer` resolution until after DI build — wires `options.ConnectionFactory = _ => Task.FromResult(mux)`

**GameKit.Admin.UI:**
- `src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs`: Same pattern; null-safe (`if (mux is null) return;` — in-process backplane for single-instance admin without Redis)

**ChannelPrefix:** `RedisChannel.Literal("GameKit")` — pinned in code. Both `LobbyHub` and `AdminEventHub` use this prefix. They share the same SignalR backplane registration (one `AddSignalR()` call covers all hubs).

### Multi-Replica Fan-Out Correctness

The Redis backplane ensures that a `IHubContext<LobbyHub>.Clients.Group(...)` call on Replica A is published to Redis; Redis delivers it to Replica B, which forwards to connected clients. This is the standard SignalR scale-out pattern. [ASSUMED — SignalR Redis backplane fan-out behavior]

**Existing test:** `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` already implements a two-app backplane test (SC#5 in Phase 11). It creates `LobbyTestApp _appA` and `LobbyTestApp _appB`, both pointing at the same `RedisFixture`, and asserts `crossInstance_Broadcast_Reaches_OtherServer`.

**SCALE-06 extension needed:** The existing test does not cover:
- Replica restart (stop appA, start a new appA instance, assert appB's connected clients still receive events)
- Redis reconnect (disconnect Redis briefly, reconnect, assert SignalR hub events resume)

These two additional scenarios extend `BackplaneTests` or go into a new `SignalRReplicaTests` class.

### Reconnect Behavior

StackExchange.Redis reconnects automatically; the SignalR backplane subscription is restored on reconnect. After a brief Redis outage, in-flight messages are lost (not buffered) — document this in the operator runbook (sticky sessions help scope the loss). [ASSUMED — StackExchange.Redis reconnect behavior]

### Sticky Session Requirement

ASP.NET Core SignalR requires sticky sessions (affinity to a specific replica) for WebSocket transport when the client sends messages — the hub method invocations must reach the same hub instance. The Redis backplane handles **outbound fan-out** (hub → clients); it does NOT make hub invocations routable to arbitrary replicas. Sticky session is an operator responsibility (nginx/HAProxy/K8s ingress annotation). Document in `docs/runbooks/rolling-deploy.md` (Phase 17 creates the runbook; Phase 16 writes the sticky-session note in a `docs/architecture/signalr-multi-replica.md` stub or inline in the Phase 16 test file's XML doc).

---

## Test Architecture

### Pattern: Two-App + Shared Infrastructure

The existing `BackplaneTests` in `GameKit.Lobby.Integration.Tests` demonstrates the canonical two-replica pattern:
1. Two independent `LobbyTestApp` instances (each with its own `TestServer`)
2. Shared `RedisFixture` connection string → same Redis server → shared SignalR backplane
3. Each app gets its own fresh Postgres database

For Matchmaking split-brain tests (SCALE-04), the same pattern applies with `MatchmakingTestApp` instances.

### Proposed New Test Classes

**`MatchmakerSplitBrainTests`** (place in `tests/GameKit.Matchmaking.Integration.Tests/`):
- Two `MatchmakingTestApp` instances sharing `PostgresFixture` + `RedisFixture`
- Simulate lease expiry mid-tick: set `LockTtlSeconds = 1` (1-second TTL), sleep past TTL while tick is running
- Mechanism: `IChaosInterceptor` already exists in `MatchmakerTickerService` — use it to pause the ticker mid-claim, allowing TTL to expire and Replica B to acquire the lock
- Assert: `COUNT(game_sessions)` = expected (no duplicates), `MAX(gap between ticks)` < `LockTtlSeconds`
- CI gate marker: `[Trait("Category", "SplitBrain")]` + required in GitHub Actions matrix

**`GracefulDrainTests`** (place in `tests/GameKit.Matchmaking.Integration.Tests/`):
- One `MatchmakingTestApp`
- Fire 100 concurrent `HttpClient` requests (enqueue + status poll)
- Call `host.StopAsync()` mid-flight
- Assert zero 5xx responses (ASP.NET Core drains in-flight requests before stop)
- Assert zero duplicate `game_sessions` rows
- Assert `ReleaseLeaseAsync` was called (verify by checking Redis lock is gone after stop, not waiting for TTL)

**`SignalRReplicaTests`** (extend `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` or new class):
- Two `LobbyTestApp` instances + shared Redis
- Phase 1: cross-instance broadcast (already tested in `BackplaneTests`)
- Phase 2: restart Replica A (`await _appA.DisposeAsync(); _appA = new LobbyTestApp(); await _appA.StartAsync(...)`) — assert Replica B's clients still receive events from the new AppA
- Phase 3: (optional) stop/restart Redis container — assert reconnect resumes delivery

### Testcontainers Patterns for Two-Replica Tests

```csharp
// Pattern: shared Redis + Postgres, two app instances
public sealed class MatchmakerSplitBrainTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;   // [Collection("Matchmaking")]
    private readonly RedisFixture _redis;   // shared
    private MatchmakingTestApp _appA = default!;
    private MatchmakingTestApp _appB = default!;

    public async Task InitializeAsync()
    {
        _appA = new MatchmakingTestApp(shortLockTtlSeconds: 2);
        _appB = new MatchmakingTestApp(shortLockTtlSeconds: 2);
        // Both apps get the SAME database so game_sessions is shared
        await _appA.StartAsync(_pg, _redis, sharedDb: true);
        await _appB.StartAsync(_pg, _redis, sharedDb: true);
    }
}
```

**Key decision:** For the split-brain test, both replicas MUST share the same Postgres database (to detect duplicate rows). The existing `BackplaneTests` give each app a separate database — the split-brain test must use `AppB.StartAsync(..., connectionString: _appA.ConnectionString)`.

### Simulating Lease Expiry Mid-Tick

The `IChaosInterceptor` seam (already in `MatchmakerTickerService`) fires `BeforeLuaClaim(ct)`. The `AbortingChaosInterceptor` test implementation throws to simulate crash. For split-brain simulation:
1. Set `LockTtlSeconds = 2` (short TTL)
2. In `BeforeLuaClaim`, `await Task.Delay(3000, ct)` — sleeps past the TTL
3. After the delay, Replica B acquires the lock
4. The Lua claim script on Replica A returns `LEASE_LOST` — no write
5. Replica B runs its tick normally — one match, not two

This mechanism avoids spawning real OS processes; everything stays in-process.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Redis distributed lock | Custom `StringSetAsync(NX,PX)` | `IDatabase.LockTakeAsync/LockReleaseAsync` | SE.Redis wraps a Lua-script-verified release; custom SET NX never guarantees fencing |
| Graceful drain | Custom request-counting middleware | ASP.NET Core `IHostApplicationLifetime` + Kestrel drain | Built-in, tested, handles WebSocket upgrade too |
| SignalR scale-out | Custom message bus | `AddStackExchangeRedis()` backplane | Already wired; one backplane for all hubs |
| Idempotency check | In-memory dictionary | Postgres `UNIQUE` constraint + `ON CONFLICT DO NOTHING` | Survives replica restart; atomic; no distributed state |
| Multi-process test | OS-level process spawning | Two in-process `TestServer` instances + shared `RedisFixture` | Faster, deterministic, works in CI without elevated privileges |

---

## Common Pitfalls

### Pitfall 1: `CancellationToken` Passed to `ReleaseLeaseAsync` on Finally Path
**What goes wrong:** `finally { await _lease.ReleaseLeaseAsync(ct); }` — `ct` is the `stoppingToken`, already cancelled when the finally block runs on shutdown. StackExchange.Redis cancels the Redis command before it is sent. Lock hangs for up to 90 s.
**Why it happens:** `ct` was the right token during normal operation but is cancelled during shutdown.
**How to avoid:** Always use `CancellationToken.None` in finally blocks for lease release. This is the one place where `CancellationToken.None` is correct — the release is a short, atomic, non-cancellable Redis command.
**Warning signs:** After replica shutdown, the `MatchmakingLeaderHealthCheck` on the surviving replica reports `Degraded` for 90+ seconds instead of promoting to `Healthy` within a few seconds.

### Pitfall 2: Split-Brain Window During Lease Renewal
**What goes wrong:** Lease renewal (`LockExtendAsync`) is NOT atomic with the actual work. Between the renewal and the work, the lease can expire. A second replica acquires the lock. Both now execute the same pool scan.
**Why it happens:** 50 ms tick budget + 90 s TTL means the window is tiny, but a pause (GC, I/O stall) can cause it.
**How to avoid:** The Lua atomic-claim script (`AtomicClaimScript`) checks the lease value before writing — this is the primary guard. The Postgres `ON CONFLICT DO NOTHING` idempotency key is the secondary guard. Both are required by SCALE-03.

### Pitfall 3: Shared Postgres DB in Two-App Tests
**What goes wrong:** Test App A and App B each run `ApplyMigrationsAsync` against the same connection string — migration advisory locks serialise them, but EF migration detection may return stale results.
**Why it happens:** Both apps call `GetPendingMigrationsAsync()` on startup. The second app to check may see the first app's migration history table as already populated and skip.
**How to avoid:** Apply migrations once via `TestHelpers.ApplyMigrations` before constructing App A or App B; configure both apps with `AutoMigrate = false` and provide the already-migrated connection string.

### Pitfall 4: SignalR Backplane `ChannelPrefix` Collision
**What goes wrong:** Two test apps sharing Redis backplane but with the same `ChannelPrefix` — messages from an unrelated test run contaminate the current test.
**Why it happens:** `ChannelPrefix = RedisChannel.Literal("GameKit")` is pinned across all instances.
**How to avoid:** In integration tests, use a per-test-run unique channel prefix or use `RedisChannel.Literal($"GameKit:{Guid.NewGuid():N}")` in the test host configuration. The production value stays as `"GameKit"`.

### Pitfall 5: `TestServer` WebSocket Requires `app.UseWebSockets()`
**What goes wrong:** SignalR `HubConnection` with WebSocket transport hangs or falls back to long polling when testing via `TestServer`.
**Why it happens:** `TestServer` does not enable WebSocket support by default.
**How to avoid:** `app.UseWebSockets()` BEFORE `app.UseRouting()`. The `LobbyTestApp` already does this — copy the pattern to any new test host that exercises SignalR.

### Pitfall 6: `RenewLeaseAsync` Returns `false` and Caller Does Not Check
**What goes wrong:** Ticker continues processing pools after lease expiry — duplicate match formation possible.
**Why it happens:** `RenewLeaseAsync` returns `false` silently when `LockExtendAsync` fails; if the caller ignores the return value, it proceeds as if it still holds the lock.
**How to avoid:** Every caller of `RenewLeaseAsync` MUST `if (!renewed) return LeaseLost`. The ticker already does this (lines 233–241 of `MatchmakerTickerService.cs`) — preserve this pattern in any new code.

---

## Architecture Patterns

### System Architecture Diagram

```
                ┌─────────────────────────────────────────┐
                │           Redis (shared)                  │
                │  ┌─────────────────────────────────────┐ │
                │  │  "gamekit:matchmaking:matcher:lock"  │ │
                │  │  (SET NX PX — one holder at a time)  │ │
                │  └─────────────────────────────────────┘ │
                │  ┌─────────────────────────────────────┐ │
                │  │  SignalR backplane (ChannelPrefix     │ │
                │  │  "GameKit") — fan-out across replicas│ │
                │  └─────────────────────────────────────┘ │
                └──────────────┬──────────────┬────────────┘
                               │              │
              ┌────────────────▼───┐  ┌───────▼──────────────┐
              │   Replica A         │  │   Replica B           │
              │  MatchmakerTicker   │  │  MatchmakerTicker     │
              │  (LEADER: holds     │  │  (FOLLOWER: sees lock │
              │   lock, runs tick)  │  │   taken, skips tick)  │
              │                     │  │                       │
              │  BackgroundService  │  │  BackgroundService    │
              │  (SIGTERM → drain   │  │  (takes over lease    │
              │   finally: Release  │  │   after release)      │
              │   CancellationToken │  │                       │
              │   .None)            │  │                       │
              └────────┬────────────┘  └──────────────────────┘
                       │
                       ▼
              ┌─────────────────────────────────────────────┐
              │            Postgres (shared)                  │
              │  game_sessions  (UNIQUE idempotency_key)      │
              │  ← ON CONFLICT DO NOTHING  ← secondary guard │
              └─────────────────────────────────────────────┘
```

### Recommended Project Structure for New Test Classes

```
tests/
├── GameKit.Matchmaking.Integration.Tests/
│   ├── MatchmakerSplitBrainTests.cs      # SCALE-04 — NEW
│   ├── GracefulDrainTests.cs             # SCALE-05 — NEW
│   ├── MatchmakingTestApp.cs             # EXISTING — may need short-TTL ctor param
│   └── ...
├── GameKit.Lobby.Integration.Tests/
│   ├── BackplaneTests.cs                 # EXISTING — extend with restart/reconnect
│   ├── SignalRReplicaTests.cs            # SCALE-06 — NEW (or extend BackplaneTests)
│   └── ...
src/
├── GameKit.Core/
│   └── Services/
│       └── ILeaderLease.cs              # SCALE-01 — NEW
│       └── LeaseStatus.cs               # SCALE-01 — MOVED from Matchmaking
├── GameKit.Core/
│   └── Migrations/
│       └── 20260622000000_AddGameSessionIdempotencyKey.cs  # SCALE-03 — NEW
│       └── 20260622000000_AddGameSessionIdempotencyKey.Designer.cs
├── GameKit.Core/
│   └── Entities/
│       └── GameSession.cs               # SCALE-03 — ADD IdempotencyKey property
├── GameKit.Matchmaking/
│   └── Services/
│       └── IMatchmakerLease.cs          # SCALE-01 — keep as alias or remove
│       └── MatchmakerLeaseHelper.cs     # SCALE-01 — add `: ILeaderLease`
│       └── RedisMatchmakerLease.cs      # SCALE-01 — add `: ILeaderLease`
│       └── MatchmakerTickerService.cs   # SCALE-02 — fix ReleaseLeaseAsync(ct)
│       └── MatchmakingReconcilerService.cs  # SCALE-02 — fix
│       └── MatchmakingRetentionCleanupService.cs  # SCALE-02 — fix
├── GameKit.Rankings/
│   └── Services/
│       └── RankDecayLeaseHelper.cs      # SCALE-01/02 — add ILeaderLease + fix ct
│       └── RankingsTickerLeaseHelper.cs # SCALE-01/02 — add ILeaderLease + fix ct
│       └── RankDecayBackgroundService.cs  # SCALE-02 — fix ReleaseLeaseAsync(ct)
│       └── RankingsTickerService.cs     # SCALE-02 — fix ReleaseLeaseAsync(ct)
```

---

## Code Examples

### ILeaderLease — Correct `finally` Pattern

```csharp
// Source: project convention — every BackgroundService finally block
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
    {
        await RunOnceAsync(stoppingToken).ConfigureAwait(false);
    }
}

public async Task RunOnceAsync(CancellationToken ct)
{
    var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
    if (!acquired) return;

    try
    {
        // ... do leader work ...
    }
    finally
    {
        // CancellationToken.None — not ct — so release survives SIGTERM
        await _lease.ReleaseLeaseAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
```

### Postgres `ON CONFLICT DO NOTHING` via Raw SQL in EF Core

```csharp
// Source: Npgsql + EF Core pattern — used in LoadTestFixture.BulkInsertPlayers already
var rowsInserted = await _ctx.Database.ExecuteSqlRawAsync(
    @"INSERT INTO gamekit.game_sessions
        (""Id"", ""State"", ""LadderId"", ""IdempotencyKey"", ""CreatedAt"")
      VALUES (@id, 'Pending', @ladderId, @idempotencyKey, @now)
      ON CONFLICT (""IdempotencyKey"") DO NOTHING",
    new NpgsqlParameter("id", sessionId),
    new NpgsqlParameter("ladderId", ladderId),
    new NpgsqlParameter("idempotencyKey", proposalId.ToString()),
    new NpgsqlParameter("now", clock.UtcNow),
    cancellationToken: ct);
// rowsInserted == 0 → duplicate; return the existing session id
```

Note: EF Core 10 `ExecuteSqlRawAsync` returns the number of affected rows. `0` means `ON CONFLICT DO NOTHING` fired — safe to treat as success. [ASSUMED — EF Core ExecuteSqlRawAsync return semantics]

### Two-App Split-Brain Test Skeleton

```csharp
// Grounded in: tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs pattern
[Collection("Matchmaking")]
public sealed class MatchmakerSplitBrainTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp _appA = default!;
    private MatchmakingTestApp _appB = default!;

    public async Task InitializeAsync()
    {
        // One shared connection string — both apps write to the same game_sessions table
        _appA = new MatchmakingTestApp(lockTtlSeconds: 2);
        await _appA.StartAsync(_pg, _redis);

        _appB = new MatchmakingTestApp(lockTtlSeconds: 2,
            connectionString: _appA.ConnectionString); // SHARE the DB
        await _appB.StartAsync(_pg, _redis);
    }

    [Fact(DisplayName = "SCALE-04: zero duplicate game_sessions rows under leader churn")]
    public async Task SplitBrain_NoduplicateSessions()
    {
        // Enqueue two tickets, let the split-brain chaos interceptor run,
        // assert COUNT(game_sessions) == 1 for the matching proposal
        // ...
    }
}
```

### `HubConnection` Reconnect Test Skeleton

```csharp
// Grounded in: tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs
[Fact(DisplayName = "SCALE-06: hub events reach clients after replica restart")]
public async Task HubEvents_AfterReplicaRestart()
{
    // 1. Connect clientB to appB
    var connB = _appB.ConnectLobbyHubAsync(playerB);
    await connB.StartAsync();
    await connB.InvokeAsync("JoinLobbyAsync", lobbyId);

    // 2. Restart appA (simulate rolling deploy of Replica A)
    await _appA.DisposeAsync();
    _appA = new LobbyTestApp();
    await _appA.StartAsync(_pg, _redis);

    // 3. clientA connects to new appA, broadcasts
    var connA = _appA.ConnectLobbyHubAsync(playerA);
    await connA.StartAsync();
    await connA.InvokeAsync("JoinLobbyAsync", lobbyId);

    var tcs = new TaskCompletionSource<string>();
    connB.On<Guid, string>("ReceiveChatMessageAsync", (_, msg) => tcs.TrySetResult(msg));

    await connA.InvokeAsync("SendChatMessageAsync", lobbyId, "restart-test");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var msg = await tcs.Task.WaitAsync(cts.Token);
    Assert.Equal("restart-test", msg);
}
```

---

## Package Legitimacy Audit

> No new external packages are introduced in this phase. All capabilities use existing pinned dependencies (StackExchange.Redis 2.8.41, xUnit 2.9.2, Testcontainers 4.11.0, Npgsql 10.0.x, EF Core 10.0.6). No legitimacy check required.

| Package | Status |
|---------|--------|
| StackExchange.Redis | Existing — pinned in `Directory.Packages.props` |
| Testcontainers.PostgreSql + .Redis | Existing — pinned 4.11.0 |
| Microsoft.AspNetCore.SignalR.StackExchangeRedis | Existing — shared framework |

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | Testcontainers (all integration tests) | ✓ (pre-existing CI requirement) | — | None — integration tests require Docker |
| Redis | `RedisFixture` | ✓ (Testcontainers) | 8.6.2 in container | — |
| Postgres | `PostgresFixture` | ✓ (Testcontainers) | 17.9 in container | — |
| .NET 10 SDK 10.0.106 | Build + tests | ✓ (global.json pinned) | 10.0.106 | — |

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | `xunit.runner.json` (per project) |
| Quick run command | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ -p:NuGetAudit=false --filter "Category=SplitBrain"` |
| Full suite command | `dotnet test tests/ -p:NuGetAudit=false` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SCALE-01 | `grep -r "LockTakeAsync" src/` returns zero outside `ILeaderLease` implementors | Static lint / build | `grep -rc "LockTakeAsync" src/ --include="*.cs"` | ❌ Wave 0 — add as build gate |
| SCALE-02 | `ReleaseLeaseAsync` uses `CancellationToken.None` in all finally paths | Static lint + drain integration | `grep -rn "ReleaseLeaseAsync(ct)" src/` returns 0 in finally blocks | ❌ Wave 0 |
| SCALE-03 | Concurrent `SessionCompleteAsync` / `AcceptProposalAsync` for same key → exactly one `game_sessions` row | Integration | `dotnet test ... --filter "Category=Idempotency"` | ❌ Wave 0 |
| SCALE-04 | `MatchmakerSplitBrainTests`: zero duplicate rows, no gap > TTL | Integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ --filter "Category=SplitBrain" -p:NuGetAudit=false` | ❌ Wave 0 |
| SCALE-05 | `GracefulDrainTests`: 100 concurrent requests + SIGTERM → zero 5xx + zero duplicates | Integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ --filter "Category=GracefulDrain" -p:NuGetAudit=false` | ❌ Wave 0 |
| SCALE-06 | `SignalRReplicaTests`: hub events reach all clients under restart + reconnect | Integration | `dotnet test tests/GameKit.Lobby.Integration.Tests/ --filter "Category=Replica" -p:NuGetAudit=false` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet build GameKit.sln -p:NuGetAudit=false` — confirms compile-time contracts
- **Per wave merge:** Full affected-package test suite
- **Phase gate:** All six SCALE-* tests green before `/gsd-verify-work`

### Wave 0 Gaps (all new for this phase)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs` — covers SCALE-04
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs` — covers SCALE-05
- [ ] `tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs` — covers SCALE-06
- [ ] Idempotency test (inline in `MatchmakerSplitBrainTests` or separate) — covers SCALE-03
- [ ] `src/GameKit.Core/Services/ILeaderLease.cs` — covers SCALE-01
- [ ] `src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs` — covers SCALE-03

---

## Security Domain

> `security_enforcement` is not explicitly `false` in `.planning/config.json`, so this section is required.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | N/A — no auth changes |
| V3 Session Management | No | N/A |
| V4 Access Control | No | N/A |
| V5 Input Validation | No | No new HTTP endpoints |
| V6 Cryptography | No | Redis distributed lock uses SE.Redis built-in Lua script (not hand-rolled) |
| V10 Malicious Code | Yes (lease) | Ensure Lua script is not injectable — SE.Redis `LockTakeAsync` uses fixed Lua; no string interpolation |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Split-brain → duplicate game sessions | Tampering | Postgres `UNIQUE(IdempotencyKey)` + Lua fencing token (already in `AtomicClaimScript`) |
| Lease release after SIGTERM → stale lock | Denial of Service | `CancellationToken.None` on finally paths |
| SignalR channel prefix collision | Tampering | Pinned `RedisChannel.Literal("GameKit")` in code; test hosts use unique prefix |
| Redis FLUSH during backplane reconnect | DoS | SE.Redis auto-reconnects; in-flight messages lost (documented) |

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Three separate concrete lease classes with no shared interface | `ILeaderLease` in Core unifying all three | Phase 16 | Enables single audit point, health-check polymorphism |
| `ReleaseLeaseAsync(stoppingToken)` on finally paths | `ReleaseLeaseAsync(CancellationToken.None)` | Phase 16 | Prevents 90-s lock holdover on graceful shutdown |
| No Postgres-level guard on session creation | `UNIQUE(IdempotencyKey)` + `ON CONFLICT DO NOTHING` | Phase 16 | Secondary safeguard against split-brain double-write |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `HostOptions.ShutdownTimeout` defaults to 5 s in .NET 10 | §Graceful Shutdown | If default changed, drain window may be shorter; document `ShutdownTimeout` recommendation as >= `LockTtlSeconds / 2` regardless |
| A2 | StackExchange.Redis `LockReleaseAsync` respects `CancellationToken` and throws `OperationCanceledException` when token is cancelled | §Graceful Shutdown (Pitfall 1) | If SE.Redis ignores the token, the bug doesn't exist — but `CancellationToken.None` is still the correct defensive practice |
| A3 | SignalR Redis backplane auto-restores subscriptions on Redis reconnect (no manual re-subscribe needed) | §SignalR Backplane | If reconnect requires manual hub re-registration, `SignalRReplicaTests` will catch it |
| A4 | `EF Core 10.0.6 ExecuteSqlRawAsync` returns affected-row count of `0` when `ON CONFLICT DO NOTHING` fires | §Idempotency Design | Easily verified in a unit test before implementation |
| A5 | The `IChaosInterceptor.BeforeLuaClaim` seam can be used to simulate TTL expiry by sleeping past the TTL | §Test Architecture | If SE.Redis clocks drift in-process, the delay may not cause TTL expiry — use `KeyExpireAsync` to force expiry instead |

---

## Open Questions (RESOLVED)

*All four resolved during planning (iteration 1). #1 → `IMatchmakerLease : ILeaderLease` alias-forward (plan 16-01). #2 → `LeaseStatus` moved to `GameKit.Core.Services` (plan 16-01). #3 → migration `20260622000000_AddGameSessionIdempotencyKey` (plan 16-02). #4 → Admin hub coverage ADDED, not deferred: `AdminSignalRReplicaTests` in `tests/GameKit.Admin.Integration.Tests` proving the `gamekit:admin:events` relay survives replica restart + Redis reconnect (plan 16-06, Task 3).*

1. **`IMatchmakerLease` backward compatibility**
   - What we know: `IMatchmakerLease` is currently in `GameKit.Matchmaking.Services`; `MatchmakerLeaderHealthCheck` and `MatchmakingReconcilerService` depend on it
   - What's unclear: whether to make `IMatchmakerLease` extend `ILeaderLease` (safer) or replace it entirely
   - Recommendation: make `IMatchmakerLease` extend `ILeaderLease` (`public interface IMatchmakerLease : ILeaderLease { }`) and mark it as an alias-forward. All existing DI registrations use `MatchmakerLeaseHelper` (concrete type); health check can be changed to `ILeaderLease` directly.

2. **`LeaseStatus` move from Matchmaking to Core**
   - What we know: `LeaseStatus` record is currently defined in `GameKit.Matchmaking.Services`; `MatchmakingLeaderHealthCheck` uses it
   - What's unclear: whether `GameKit.Core` should take a dependency on the `LeaseStatus` record shape
   - Recommendation: move `LeaseStatus` to `GameKit.Core.Services`; Matchmaking uses `using GameKit.Core.Services;` — no circular dep since Core has no reference to Matchmaking.

3. **`game_sessions` migration timestamp**
   - What we know: last Core migration is `20260606100000_AddAuditActorIdFk`; next should be later
   - Recommendation: use `20260622000000_AddGameSessionIdempotencyKey` (today's date, sequence 0)

4. **Admin SignalR test coverage for SCALE-06**
   - What we know: `AdminEventHub` uses the same backplane; `BackplaneTests` covers Lobby only
   - What's unclear: whether success criterion #5 ("Lobby + Admin SignalR") requires an Admin hub test
   - Recommendation: the success criterion says "Lobby + Admin"; add a brief `AdminHubReplicaTest` in `GameKit.Admin.Integration.Tests` that asserts admin events reach clients across replicas. Lower priority than the Lobby test.

---

## Sources

### Primary (HIGH confidence)
- `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` — exact interface surface read
- `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` — concrete implementation, line 292 bug confirmed
- `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` — minimal concrete implementation
- `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` — no interface, lines 159/189 bug confirmed
- `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` — no interface confirmed
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — `IChaosInterceptor` seam confirmed
- `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs` — `ReleaseLeaseAsync(ct)` on finally line 180 confirmed
- `src/GameKit.Core/Services/SessionCompleteService.cs` — idempotency flow read
- `src/GameKit.Core/Entities/GameSession.cs` — no `IdempotencyKey` column confirmed
- `src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs` — `game_sessions` schema confirmed
- `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs` — backplane wiring pattern read
- `src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs` — null-safe admin backplane read
- `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` — `AddStackExchangeRedis` call confirmed
- `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` — two-app test pattern read
- `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` — hub connection helper read
- `tests/GameKit.TestFixtures/PostgresFixture.cs` — Testcontainers fixture pattern read
- `tests/GameKit.TestFixtures/RedisFixture.cs` — Redis fixture pattern read
- `tests/GameKit.Matchmaking.LoadTests/LoadTestFixture.cs` — two-container pattern read
- `.planning/STATE.md` — locked decisions including `CancellationToken.None` on SIGTERM
- `.planning/REQUIREMENTS.md` — SCALE-01 through SCALE-06 requirements read verbatim

### Secondary (MEDIUM confidence)
- `.planning/phases/14-health-readiness/14-RESEARCH.md` — `IMatchmakerLease.QueryLeaseAsync` design from Phase 14
- `.planning/STATE.md` — v2.1 architecture decision: `ReleaseLeaseAsync` on SIGTERM uses `CancellationToken.None`

### Tertiary (LOW confidence — tagged ASSUMED above)
- .NET 10 `HostOptions.ShutdownTimeout` default [ASSUMED]
- SE.Redis `LockReleaseAsync` cancellation behavior [ASSUMED]
- EF Core `ExecuteSqlRawAsync` return value for `ON CONFLICT DO NOTHING` [ASSUMED]
- SignalR backplane auto-resubscribe on reconnect [ASSUMED]

---

## Metadata

**Confidence breakdown:**
- Lease inventory / existing code: HIGH — all files read from src/
- ILeaderLease design: HIGH — grounded in existing interfaces
- CancellationToken bug locations: HIGH — line numbers confirmed by grep
- Graceful shutdown mechanics: MEDIUM — .NET 10 behavior assumed from .NET 8+ knowledge
- Testcontainers patterns: HIGH — copied from existing fixtures
- SignalR reconnect behavior: MEDIUM — assumed from SE.Redis documentation

**Research date:** 2026-06-22
**Valid until:** 2026-07-22 (stable framework; no fast-moving deps added)

---

## RESEARCH COMPLETE
