---
phase: 16-multi-replica-hardening
plan: "04"
subsystem: tests/GameKit.Matchmaking.Integration.Tests
tags: [split-brain, idempotency, matchmaking, chaos-testing, SCALE-03, SCALE-04]
dependency_graph:
  requires: [16-01, 16-02, 16-03]
  provides: [SCALE-04-ci-gate, SCALE-03-verification]
  affects: [tests/GameKit.Matchmaking.Integration.Tests]
tech_stack:
  added: []
  patterns:
    - "Two MatchmakingTestApp instances sharing one Postgres+Redis — lockTtlSeconds=2 + serviceOverrides hook"
    - "DelayingChaosInterceptor: IChaosInterceptor that pauses BeforeLuaClaim past lock TTL to simulate lease expiry"
    - "SemaphoreSlim(0,2) gate for max-concurrency idempotent INSERT racing"
    - "xUnit [Trait(Category,SplitBrain)] and [Trait(Category,Idempotency)] CI gate filters"
key_files:
  created:
    - tests/GameKit.Matchmaking.Integration.Tests/TestDoubles/DelayingChaosInterceptor.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs
  modified:
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs
decisions:
  - "Shared-DB path seeds a uniquely-suffixed ladder (Guid.NewGuid().ToString('N')[..8]) to avoid IX_ladders_Name collision when multiple shared-DB MatchmakingTestApp instances start"
  - "DelayingChaosInterceptor injected via serviceOverrides in InitializeAsync — avoids rebuilding AppA inside the test fact, which caused duplicate migration runs against the shared DB"
  - "SCALE-03 idempotency test uses raw Npgsql concurrent INSERT ON CONFLICT DO NOTHING rather than parallel AcceptAsync flows — directly validates the Postgres-level guard without requiring two proposals to race through the full ticker pipeline"
  - "SCALE-04 assertion uses COUNT(game_sessions WHERE LadderId = ladderId) <= 1 — catches duplicates even when the idempotency key is not yet set (timing window before AtomicClaimScript completes)"
metrics:
  duration: "~30 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  files_changed: 3
status: complete
requirements: [SCALE-04, SCALE-03]
---

# Phase 16 Plan 04: Split-Brain Test Harness Summary

SCALE-04 CI gate and SCALE-03 idempotency verification: two-replica shared-DB split-brain test that proves zero duplicate `game_sessions` rows under leader churn using a `DelayingChaosInterceptor` that forces the Redis lock TTL to expire mid-tick.

## What Was Built

### Task 1 — MatchmakingTestApp harness extension (commit ec40316 + fix 50b974b)

Extended `MatchmakingTestApp` without breaking any existing call sites:

- Optional `int? lockTtlSeconds` constructor parameter. When set, configures `o.Ticker.LockTtlSeconds = lockTtlSeconds.Value` in the `AddMatchmaking` callback so the leader lease TTL is short for the split-brain test.
- New `StartAsync(PostgresFixture, RedisFixture, string? connectionString, Action<IServiceCollection>? serviceOverrides)` overload. Existing two-arg `StartAsync(pg, redis)` delegates to the new overload with `null` params.
- Shared-DB path (`connectionString` non-null): skips `CreateFreshDatabaseAsync` and migrations entirely (RESEARCH Pitfall 3), seeds a uniquely-suffixed ladder to avoid `IX_ladders_Name` collision.
- `serviceOverrides?.Invoke(services)` invoked after all standard registrations, before host build — mirrors `LobbyTestApp` pattern.
- Public `string MatcherLockKey { get; private set; }` populated from `IOptions<GameKitMatchmakingOptions>` after host build.

### Task 2 — DelayingChaosInterceptor test double (commit bc6d641)

`DelayingChaosInterceptor : IChaosInterceptor` in `TestDoubles/` namespace:

- Constructor: `DelayingChaosInterceptor(int delayMs, bool delayLuaClaim = true, bool delaySessionInsert = false)`
- `BeforeLuaClaim`: increments `LuaClaimCallCount` (Interlocked), then `await Task.Delay(delayMs, ct)` when `delayLuaClaim`
- `BeforeSessionInsert`: increments `SessionInsertCallCount` (Interlocked), then delays when `delaySessionInsert`
- Thread-safe observable counters for defensive test assertions

### Task 3 — MatchmakerSplitBrainTests (commit d91389e + fix 50b974b)

`[Collection("Matchmaking")]` `[Trait("Category","SplitBrain")]` test class with `IAsyncLifetime`:

**`SplitBrain_NoDuplicateSessions` (SCALE-04):**
- `InitializeAsync`: `_appA` with `lockTtlSeconds:2` + `DelayingChaosInterceptor(delayMs:3000, delayLuaClaim:true)` injected via `serviceOverrides`. `_appB` shares `_appA.ConnectionString` (same DB, no second migrations run).
- Seeds two Redis ticket hashes + sorted-set entries + Postgres ticket rows for `_appA.TestLadderId`.
- Races `IMatchmakerTicker.RunOnceAsync` on both replicas concurrently (30s CancellationToken). AppA stalls 3s at `BeforeLuaClaim` > 2s TTL; AppB acquires the lock, forms the match, writes one row.
- Asserts: `COUNT(game_sessions WHERE LadderId = ladderId) <= 1` and `matchedCount <= 1`.

**`ConcurrentSessionCreate_SameIdempotencyKey_ExactlyOneRow` (SCALE-03, also tagged `[Trait("Category","Idempotency")]`):**
- `SemaphoreSlim(0,2)` gate — releases both insert tasks simultaneously to maximise the race window.
- Two concurrent raw Npgsql INSERTs: `ON CONFLICT ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL DO NOTHING`.
- Asserts: `rows1 + rows2 == 1` (exactly one insert succeeded, the other was a no-op) and `COUNT(*) WHERE IdempotencyKey = @key == 1`.

## CI Gate Results

| Filter | Tests | Result |
|--------|-------|--------|
| `--filter "Category=SplitBrain"` | 2/2 | PASSED |
| `--filter "Category=Idempotency"` | 1/1 | PASSED |
| Full Matchmaking integration suite | 83/83 | PASSED |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed IX_ladders_Name unique constraint collision for shared-DB path**

- **Found during:** Task 3 (SplitBrain_NoDuplicateSessions first run)
- **Issue:** Original shared-DB path seeded with `TestLadderName + "_b"` — a fixed suffix. The test originally rebuilt AppA inside the test fact with `connectionString: _appB.ConnectionString`, causing a second shared-DB startup that tried to seed the fixed `"default_b"` ladder again → `23505: duplicate key value violates unique constraint "IX_ladders_Name"`.
- **Fix (two-part):** (1) Changed seed to `TestLadderName + "_" + Guid.NewGuid().ToString("N")[..8]` (unique per startup). (2) Moved `DelayingChaosInterceptor` injection to `InitializeAsync` via `serviceOverrides` so the test fact never needs to rebuild AppA, eliminating the second shared-DB startup entirely.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs`, `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs`
- **Commit:** 50b974b

**2. [Rule 3 - Blocking] Missing `Microsoft.Extensions.DependencyInjection.Extensions` using directive**

- **Found during:** Task 3 (first compile attempt)
- **Issue:** `services.RemoveAll<IChaosInterceptor>()` requires `using Microsoft.Extensions.DependencyInjection.Extensions;` (CS1061).
- **Fix:** Added the using directive to `MatchmakerSplitBrainTests.cs`.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs`
- **Commit:** d91389e

## Known Stubs

None. Both test facts exercise live infrastructure via Testcontainers (Postgres + Redis) with no hardcoded or stubbed data beyond the deterministic chaos interceptor delay.

## Threat Flags

No new network endpoints, auth paths, file access patterns, or trust boundary changes. This plan is test-infrastructure-only.

## Self-Check: PASSED

- `tests/GameKit.Matchmaking.Integration.Tests/TestDoubles/DelayingChaosInterceptor.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` — FOUND (MatcherLockKey, lockTtlSeconds, serviceOverrides, connectionString)
- Commits ec40316, bc6d641, d91389e, 50b974b — present in git log
- `--filter "Category=SplitBrain"` → 2/2 PASSED
- `--filter "Category=Idempotency"` → 1/1 PASSED
- Full suite → 83/83 PASSED
