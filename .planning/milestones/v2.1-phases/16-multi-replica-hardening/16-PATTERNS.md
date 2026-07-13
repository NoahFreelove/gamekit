# Phase 16: Multi-Replica Hardening — Pattern Map

**Mapped:** 2026-06-22
**Files analyzed:** 13 (6 new, 7 modified)
**Analogs found:** 13 / 13

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Core/Services/ILeaderLease.cs` | interface | request-response | `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` | exact |
| `src/GameKit.Core/Services/LeaseStatus.cs` | model | — | `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` (record at bottom) | exact |
| `src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs` | migration | CRUD | `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` | exact |
| `src/GameKit.Core/Entities/GameSession.cs` (modify) | model | CRUD | `src/GameKit.Core/Entities/GameSession.cs` (self) | self |
| `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` (modify) | service | request-response | self + `IMatchmakerLease.cs` | self |
| `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` (modify) | service | request-response | `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` | exact |
| `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` (modify) | service | request-response | `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` | exact |
| `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` (modify) | service | request-response | `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` | exact |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` (modify) | service | event-driven | self | self |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` (modify) | service | event-driven | `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | role-match |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs` | test | event-driven | `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs` | test | request-response | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` + `BackplaneTests.cs` | role-match |
| `tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs` | test | event-driven | `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` | exact |

---

## Pattern Assignments

### `src/GameKit.Core/Services/ILeaderLease.cs` (interface)

**Analog:** `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs`

This is the new unified interface. Copy the full structure of `IMatchmakerLease` — license header, namespace declaration style, XML doc format — and extend the surface with `RenewLeaseAsync` (present on all helpers but missing from `IMatchmakerLease`) and a `QueryLeaseAsync` returning the moved `LeaseStatus`.

**License + namespace pattern** (lines 1–8 of `IMatchmakerLease.cs`):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;
```

**Interface surface to replicate** (lines 34–61 of `IMatchmakerLease.cs`):
```csharp
public interface ILeaderLease
{
    /// <summary>Fencing-token-grade unique id for this process instance (<c>MachineName:Guid</c>).</summary>
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

**Conventions:**
- Single file per interface (no bundled records; `LeaseStatus` moves to its own file)
- XML doc on every member — no exceptions (CLAUDE.md constraint)
- Namespace `GameKit.Core.Services` (not `GameKit.Core` root)
- `IMatchmakerLease` in Matchmaking should become `public interface IMatchmakerLease : ILeaderLease { }` so existing health check + DI references compile without change

---

### `src/GameKit.Core/Services/LeaseStatus.cs` (moved record)

**Analog:** `IMatchmakerLease.cs` lines 63–66 (the record currently co-located with the interface):
```csharp
/// <summary>Snapshot of a distributed leader lock: current holder + TTL.</summary>
/// <param name="HolderInstanceId">The holder's <c>InstanceId</c>, or <c>null</c> when unheld.</param>
/// <param name="Ttl">Remaining lease duration, or <c>null</c> when the key has no TTL.</param>
public sealed record LeaseStatus(string? HolderInstanceId, TimeSpan? Ttl);
```

**Conventions:**
- `sealed record` with positional parameters
- XML doc on the record and each parameter
- Same namespace as `ILeaderLease`: `GameKit.Core.Services`
- After the move, add `using GameKit.Core.Services;` to `IMatchmakerLease.cs` and delete the old definition

---

### `src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs` (new migration)

**Analog:** `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs`

This is the canonical pattern for a non-destructive additive Core migration.

**Full analog** (lines 1–37 of `AddSessionParticipationFraction.cs`):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <summary>
    /// Adds the <c>ParticipationFraction</c> column to <c>gamekit.session_participants</c>
    /// (MATCH-19 rating guard). Core is the sole owner of this Core-table column per
    /// CLAUDE.md per-package boundary rule (packages never modify Core tables in their migrations).
    /// </summary>
    public partial class AddSessionParticipationFraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ParticipationFraction",
                schema: "gamekit",
                table: "session_participants",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParticipationFraction",
                schema: "gamekit",
                table: "session_participants");
        }
    }
}
```

**New migration shape** — replicate the above structure with:
```csharp
/// <summary>
/// Adds the <c>IdempotencyKey</c> column to <c>gamekit.game_sessions</c> with a partial
/// unique index (SCALE-03). Core is the sole owner of Core-table schema per CLAUDE.md
/// per-package boundary rule. Nullable with partial index allows non-destructive addition
/// to existing rows. The unique constraint is the Postgres-level secondary guard against
/// split-brain double-write (RESEARCH §Idempotency Design).
/// </summary>
public partial class AddGameSessionIdempotencyKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKey",
            schema: "gamekit",
            table: "game_sessions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX "uq_game_sessions_idempotency_key"
                ON gamekit.game_sessions ("IdempotencyKey")
                WHERE "IdempotencyKey" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """DROP INDEX IF EXISTS gamekit."uq_game_sessions_idempotency_key";""");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            schema: "gamekit",
            table: "game_sessions");
    }
}
```

**Conventions:**
- Class wrapped in `namespace GameKit.Core.Migrations { }` block (not file-scoped) — matches existing migrations
- `#nullable disable` above namespace
- `partial class` — EF generates the `.Designer.cs` counterpart
- Schema is always `"gamekit"` (all GameKit tables live in this schema, per `CoreInitial.cs` line 14)
- `migrationBuilder.Sql(...)` for raw DDL that EF's fluent API cannot express (partial indexes)
- XML doc on the class referencing the CLAUDE.md boundary rule

---

### `src/GameKit.Core/Entities/GameSession.cs` (modify — add `IdempotencyKey`)

**Analog:** `src/GameKit.Core/Entities/GameSession.cs` (self)

Add after the `Metadata` property (line 34), following the existing property XML doc style:
```csharp
/// <summary>
/// Idempotency key set at match-formation time to the proposal id (SCALE-03).
/// A partial unique index on this column prevents duplicate <c>game_sessions</c> rows
/// when split-brain replicas both attempt to create the same session.
/// Null for sessions created outside the matchmaking path (manual API calls).
/// </summary>
public string? IdempotencyKey { get; set; }
```

**Conventions:** nullable `string?` with XML doc; no behavior method added (it is set externally by the matchmaking service)

---

### `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` (modify — add `: ILeaderLease`)

**Change:** class declaration line 54:
```csharp
// Before:
public sealed class MatchmakerLeaseHelper : IMatchmakerLease

// After:
public sealed class MatchmakerLeaseHelper : IMatchmakerLease, ILeaderLease
```

`ILeaderLease` requires `RenewLeaseAsync` — already present on this class (lines 155–177). `QueryLeaseAsync` — already present (lines 208–226). `InstanceId` — already present (line 68). No new method bodies needed; the class already satisfies `ILeaderLease` fully.

Also add `using GameKit.Core.Services;` to the import block (lines 1–13).

---

### `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` (modify — add `: ILeaderLease`)

**Analog:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` for the stub methods

`RedisMatchmakerLease` is the minimal implementation. It lacks `RenewLeaseAsync`. Add a stub that returns `false` (this implementation does not support renewal):
```csharp
/// <inheritdoc />
/// <remarks>
/// This minimal implementation does not support lease renewal. Returns <c>false</c>
/// unconditionally — callers must treat this as lease lost and stop processing.
/// </remarks>
public Task<bool> RenewLeaseAsync(CancellationToken ct) => Task.FromResult(false);
```

It also lacks `QueryLeaseAsync` unless added in Phase 14. If missing, add the same Lua pattern from `MatchmakerLeaseHelper` lines 204–226.

---

### `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` (modify — add `: ILeaderLease` + `QueryLeaseAsync`)

**Analog:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` lines 199–247 for `QueryLeaseAsync`

Current class (lines 1–173) has `TryAcquireLeaseAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync`, and `InstanceId` but no `QueryLeaseAsync`. Adding `ILeaderLease` requires it.

**Imports to add** (follow `MatchmakerLeaseHelper.cs` import block):
```csharp
using GameKit.Core.Services;  // ILeaderLease, LeaseStatus
```

**Class declaration change:**
```csharp
// Before:
public sealed class RankDecayLeaseHelper

// After:
public sealed class RankDecayLeaseHelper : ILeaderLease
```

**`QueryLeaseAsync` to add** — copy exactly from `MatchmakerLeaseHelper.cs` lines 199–226, substituting `_opts.Decay.LockKey` for `_opts.Ticker.LockKey` and `RankDecayLeaseHelper` for `MatchmakerLeaseHelper` in log messages:
```csharp
private const string QueryLeaseScript =
    "return { redis.call('GET', KEYS[1]), redis.call('PTTL', KEYS[1]) }";

public async Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)
{
    try
    {
        var db = _redis.GetDatabase();
        var result = (RedisResult[]?)await db
            .ScriptEvaluateAsync(QueryLeaseScript, new RedisKey[] { _opts.Decay.LockKey })
            .ConfigureAwait(false);
        return ParseLeaseStatus(result);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex,
            "RankDecayLeaseHelper: QueryLeaseAsync — Redis unavailable.");
        return new LeaseStatus(null, null);
    }
}

private static LeaseStatus ParseLeaseStatus(RedisResult[]? result)
{
    if (result is null || result.Length < 2)
        return new LeaseStatus(null, null);

    var holderRaw = (RedisValue)result[0];
    string? holder = holderRaw.HasValue && holderRaw.Length() > 0
        ? (string?)holderRaw
        : null;

    var pttlMs = (long)result[1];
    TimeSpan? ttl = pttlMs > 0 ? TimeSpan.FromMilliseconds(pttlMs) : null;

    return new LeaseStatus(holder, ttl);
}
```

**Same pattern applies to `RankingsTickerLeaseHelper.cs`** — substitute `_opts.Ticker.LockKey` and `RankingsTickerLeaseHelper` in log messages.

---

### `CancellationToken.None` fixes — 5 `finally` paths

**Analog:** `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` lines 287–293 (the bug site, then the fixed pattern from RESEARCH §Code Examples)

The bug is on line 292 (and four other `finally` blocks):
```csharp
// BEFORE (bug — ct is the stoppingToken, already cancelled on SIGTERM):
finally
{
    MatchmakingMeter.TickerLag.Record(tickSw.Elapsed.TotalMilliseconds);
    await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false);
}

// AFTER (fix — CancellationToken.None is correct in finally paths):
finally
{
    MatchmakingMeter.TickerLag.Record(tickSw.Elapsed.TotalMilliseconds);
    await _lease.ReleaseLeaseAsync(CancellationToken.None).ConfigureAwait(false);
}
```

**All five locations:**
| File | Approx. Line | Notes |
|------|-------------|-------|
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | 292 | After `MatchmakingMeter.TickerLag.Record(...)` |
| `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs` | 180 | Reconciler finally |
| `src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs` | 171 | Retention finally |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` | 189 | Decay finally |
| `src/GameKit.Rankings/Services/RankingsTickerService.cs` | 199 | Rankings ticker finally |

No other code changes in these files — one token substitution per file.

---

### `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs` (new test class)

**Analog:** `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` (entire file)

**Test class skeleton — copy this structure exactly:**

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// SCALE-04 — CI gate: two-replica split-brain test. Two <see cref="MatchmakingTestApp"/>
/// instances share a single Testcontainers Postgres database and Redis, simulating two
/// replicas competing for the leader lease. The <see cref="IChaosInterceptor"/> seam
/// pauses Replica A's ticker mid-claim past the lock TTL, allowing Replica B to acquire
/// the lease and form the match — asserts zero duplicate <c>game_sessions</c> rows.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "SplitBrain")]
public sealed class MatchmakerSplitBrainTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp _appA = default!;
    private MatchmakingTestApp _appB = default!;

    public MatchmakerSplitBrainTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        // Apply migrations ONCE before both apps start (Pitfall 3 from RESEARCH).
        // Both apps use AutoMigrate = false and the same connection string.
        _appA = new MatchmakingTestApp(lockTtlSeconds: 2);
        await _appA.StartAsync(_pg, _redis);

        // AppB shares the SAME Postgres database (same connection string) —
        // required so game_sessions rows are visible to both replicas.
        _appB = new MatchmakingTestApp(lockTtlSeconds: 2,
            connectionString: _appA.ConnectionString);
        await _appB.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        await _appA.DisposeAsync();
        await _appB.DisposeAsync();
    }

    [Fact(DisplayName = "SCALE-04: zero duplicate game_sessions rows under leader churn")]
    public async Task SplitBrain_NoDuplicateSessions()
    {
        // ...
    }

    [Fact(DisplayName = "SCALE-03: concurrent SessionCompleteAsync for same key → exactly one row")]
    [Trait("Category", "Idempotency")]
    public async Task ConcurrentSessionCreate_IdempotencyKey_ExactlyOneRow()
    {
        // ...
    }
}
```

**Key conventions from `BackplaneTests.cs`:**
- `[Collection("Matchmaking")]` — injected `PostgresFixture` + `RedisFixture` via constructor (lines 31–35)
- `IAsyncLifetime` with `InitializeAsync` / `DisposeAsync` pair (lines 38–62)
- Two app fields declared as `default!`, constructed in `InitializeAsync` (lines 28–29, 42–55)
- `await appA.DisposeAsync(); await appB.DisposeAsync();` in `DisposeAsync` (lines 59–62)
- `[Trait("Category", "SplitBrain")]` on the class — matches CI gate filter `--filter "Category=SplitBrain"`

**`IChaosInterceptor` usage pattern** — from `src/GameKit.Matchmaking/Services/IChaosInterceptor.cs`:
The seam's `BeforeLuaClaim` fires immediately before the Lua atomic-claim script. For split-brain simulation, replace the `NullChaosInterceptor` via `MatchmakingTestApp`'s `serviceOverrides` callback:
```csharp
// In InitializeAsync — inject a DelayingChaosInterceptor that sleeps past TTL:
_appA = new MatchmakingTestApp(lockTtlSeconds: 2);
await _appA.StartAsync(_pg, _redis, serviceOverrides: services =>
{
    services.RemoveAll<IChaosInterceptor>();
    services.AddSingleton<IChaosInterceptor>(
        new DelayingChaosInterceptor(delayMs: 3000)); // > 2 s TTL
});
```

The `serviceOverrides` parameter on `MatchmakingTestApp.StartAsync` is already present (matches `LobbyTestApp.StartAsync` at line 114–116).

---

### `tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs` (new test class)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` (for host construction) + `BackplaneTests.cs` (for `IAsyncLifetime` skeleton)

**Drain test skeleton:**
```csharp
[Collection("Matchmaking")]
[Trait("Category", "GracefulDrain")]
public sealed class GracefulDrainTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp _app = default!;

    public GracefulDrainTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new MatchmakingTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact(DisplayName = "SCALE-05: 100 concurrent requests + host stop → zero 5xx, lease released")]
    public async Task GracefulDrain_NoFiveXx_LeaseReleased()
    {
        // Fire 100 concurrent HTTP requests
        // Call _app's host StopAsync mid-flight
        // Assert all responses are < 500
        // Assert Redis lock key is absent (lease was released — not waiting for TTL)
    }
}
```

**Verifying lease released** — query Redis directly after stop using the `RedisFixture.ConnectionString`:
```csharp
var mux = ConnectionMultiplexer.Connect(_redis.ConnectionString);
var db = mux.GetDatabase();
var lockValue = await db.StringGetAsync(_app.MatcherLockKey);
Assert.True(lockValue.IsNullOrEmpty, "Lease was not released on shutdown");
```

`MatchmakingTestApp` may need a `MatcherLockKey` property exposing the configured lock key for this assertion.

---

### `tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs` (new test class)

**Analog:** `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` (entire file — exact copy of structure)

**Full skeleton:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// SCALE-06 — Multi-replica SignalR correctness. Two <see cref="LobbyTestApp"/> instances
/// share the same Testcontainers Redis backplane. Tests cover: (1) replica restart — Replica
/// A disposed and restarted while clientB on Replica B receives events from the new AppA;
/// (2) Redis reconnect — Redis container stopped and restarted, events resume.
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Replica")]
public sealed class SignalRReplicaTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _appA = default!;
    private LobbyTestApp _appB = default!;

    public SignalRReplicaTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _appA = new LobbyTestApp();
        _appB = new LobbyTestApp();
        await _appA.StartAsync(_pg, _redis);
        await _appB.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        await _appA.DisposeAsync();
        await _appB.DisposeAsync();
    }

    [Fact(DisplayName = "SCALE-06: hub events reach clients after Replica A restart")]
    public async Task HubEvents_AfterReplicaRestart()
    {
        // Connect clientB to appB, join lobby
        // Dispose _appA; reconstruct new LobbyTestApp; StartAsync against same _redis
        // Connect clientA to new appA, join same lobby
        // clientA sends message → clientB on appB must receive via backplane
    }
}
```

**Pattern to copy from `BackplaneTests.cs`:**
- `ConnectLobbyHubAsync` (line 254 of `LobbyTestApp.cs`) — builds `HubConnection` through `Server.CreateHandler()`
- `SeedSharedLobbyAsync` helper (lines 129–161 of `BackplaneTests.cs`) — raw Npgsql INSERT into both apps' databases
- `TaskCompletionSource<T>` + `connB.On<...>` + `tcs.Task.WaitAsync(cts.Token)` pattern (lines 85–111 of `BackplaneTests.cs`)
- `try/finally { await connA.StopAsync(); await connB.StopAsync(); }` cleanup (lines 114–120)

**Replica-restart pattern** — from RESEARCH §Code Examples:
```csharp
await _appA.DisposeAsync();
_appA = new LobbyTestApp();
await _appA.StartAsync(_pg, _redis);
// connA must be rebuilt against the new app's Server.CreateHandler()
```

**Channel prefix collision prevention** (RESEARCH Pitfall 4): in test host `StartAsync`, override the SignalR Redis options with a per-test-run prefix:
```csharp
serviceOverrides: services =>
{
    services.Configure<Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisOptions>(
        opts => opts.Configuration.ChannelPrefix =
            StackExchange.Redis.RedisChannel.Literal($"GameKit:{Guid.NewGuid():N}"));
}
```

---

## Shared Patterns

### GPL License Header
**Source:** Every existing `.cs` file in `src/` and `tests/`
**Apply to:** All new files
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### Polly v8 Resilience Pipeline (lease helpers)
**Source:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` lines 87–110
**Apply to:** Any new lease helper class; do not alter in existing helpers
```csharp
_polly = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<RedisConnectionException>()
            .Handle<RedisTimeoutException>(),
        OnRetry = args =>
        {
            _logger.LogWarning(
                args.Outcome.Exception,
                "{HelperName}: Redis retry {Attempt} after {Delay}ms.",
                args.AttemptNumber + 1,
                args.RetryDelay.TotalMilliseconds);
            return ValueTask.CompletedTask;
        },
    })
    .Build();
```

### `InstanceId` fencing token
**Source:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` line 68
**Apply to:** Every lease helper class and the `ILeaderLease` interface doc
```csharp
public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";
```

### `CancellationToken.None` on `finally` lease release
**Source:** RESEARCH §ILeaderLease Design — correct pattern (not existing code, which is the bug)
**Apply to:** All five `finally` blocks listed above and any future `BackgroundService` that calls `ReleaseLeaseAsync`
```csharp
finally
{
    await _lease.ReleaseLeaseAsync(CancellationToken.None).ConfigureAwait(false);
}
```

### xUnit Collection Fixture Injection
**Source:** `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` lines 23–35
**Apply to:** `MatchmakerSplitBrainTests`, `GracefulDrainTests`, `SignalRReplicaTests`
```csharp
[Collection("Matchmaking")] // or "Lobby" for Lobby tests
public sealed class MyTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public MyTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }
}
```

### Testcontainers App `serviceOverrides` Injection
**Source:** `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` lines 114–116 + 172–173
**Apply to:** All new tests that need to override services (chaos interceptor, short TTL)
```csharp
await _app.StartAsync(_pg, _redis, serviceOverrides: services =>
{
    services.RemoveAll<IFoo>();
    services.AddSingleton<IFoo>(new TestFooImpl());
});
```

### Migration: `#nullable disable` + namespace block (not file-scoped)
**Source:** `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` lines 6–7
**Apply to:** `20260622000000_AddGameSessionIdempotencyKey.cs`
```csharp
#nullable disable

namespace GameKit.Core.Migrations
{
    public partial class MigrationName : Migration
    {
        // ...
    }
}
```

---

## No Analog Found

All phase 16 artifacts have strong analogs in the existing codebase. No file requires falling back to RESEARCH.md patterns only.

---

## Metadata

**Analog search scope:** `src/GameKit.Core/`, `src/GameKit.Matchmaking/`, `src/GameKit.Rankings/`, `tests/GameKit.Lobby.Integration.Tests/`, `tests/GameKit.Matchmaking.Integration.Tests/`, `tests/GameKit.TestFixtures/`
**Files read:** 15
**Pattern extraction date:** 2026-06-22

---

## PATTERN MAPPING COMPLETE

**Phase:** 16 — Multi-Replica Hardening
**Files classified:** 13
**Analogs found:** 13 / 13

### Coverage
- Files with exact analog: 8
- Files with role-match analog: 3
- Files that are self-modifications: 2

### Key Patterns Identified
- `ILeaderLease` in `GameKit.Core.Services` is a direct structural clone of `IMatchmakerLease` with `RenewLeaseAsync` added; `LeaseStatus` record moves to Core alongside it
- All five `CancellationToken.None` fixes are one-token substitutions in existing `finally` blocks — no structural change to the surrounding code
- Two-replica integration tests (`MatchmakerSplitBrainTests`, `SignalRReplicaTests`) copy the `BackplaneTests.cs` skeleton exactly: `[Collection]` + `IAsyncLifetime` + two app fields + shared `RedisFixture`
- Core migration for `IdempotencyKey` follows the `AddSessionParticipationFraction` pattern: `partial class`, `#nullable disable`, namespace block, `AddColumn` + raw `Sql` for the partial unique index
- `IChaosInterceptor` seam injected via `MatchmakingTestApp`'s `serviceOverrides` callback — no new seam needed for split-brain simulation

### File Created
`/home/noah/Desktop/projects/gamekit/.planning/phases/16-multi-replica-hardening/16-PATTERNS.md`

### Ready for Planning
Pattern mapping complete. Planner can now reference analog patterns in PLAN.md files.
