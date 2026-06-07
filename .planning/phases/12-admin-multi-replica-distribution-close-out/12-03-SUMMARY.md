---
phase: 12-admin-multi-replica-distribution-close-out
plan: "03"
subsystem: admin, infra
tags: [redis, stackexchange-redis, error-rate, health-probe, multi-replica, testcontainers]

# Dependency graph
requires:
  - phase: 03-admin-ui
    provides: ErrorRateRingBuffer, LogErrorCounter, HealthProbeService, AdminBuilderExtensions — in-memory error counter baseline this plan extends

provides:
  - IRedisErrorRateCounter interface (fire-and-forget write + async aggregate read)
  - RedisErrorRateCounter — INCRBY on gamekit:admin:errors:{epoch_bucket} keys with sliding-window MGET read
  - LogErrorCounter dual-writes in-memory ring buffer AND Redis when IRedisErrorRateCounter is registered
  - HealthProbeService.ProbeErrorRateAsync prefers Redis aggregate, falls back to in-memory on -1 sentinel
  - Conditional TryAddSingleton registration in AddGameKitAdmin (null factory when no IConnectionMultiplexer)
  - RedisErrorCounterTests: two-host Testcontainers integration test proving cross-replica aggregation (SC#1)

affects:
  - 12-04 (builds on AdminBuilderExtensions SC#2 block immediately after the SC#1 Redis counter registration)
  - Any future plan touching HealthProbeService or error-rate health tile

# Tech tracking
tech-stack:
  added: []  # No new packages — StackExchange.Redis already pinned from Phase 11
  patterns:
    - "TryAddSingleton factory returning null! when dependency absent — opt-in Redis services with single-instance fallback"
    - "Fire-and-forget async Redis write: void IncrementError() discards Task from private async method; swallow-all catch"
    - "MGET sliding window: build RedisKey[] for nowBucket - (bucketCount-1-i) range, sum TryParse values"
    - "Always-set-TTL: KeyExpireAsync on each increment avoids ExpireWhen.HasNoExpiry API uncertainty in SE.Redis 2.8.41"
    - "Two-host Testcontainers test: both AdminTestHost instances share the same RedisFixture for cross-replica assertion"

key-files:
  created:
    - src/GameKit.Admin.UI/Services/IRedisErrorRateCounter.cs
    - src/GameKit.Admin.UI/Services/RedisErrorRateCounter.cs
    - tests/GameKit.Admin.Integration.Tests/RedisErrorCounterTests.cs
  modified:
    - src/GameKit.Admin.UI/Services/LogErrorCounter.cs
    - src/GameKit.Admin.UI/Services/HealthProbeService.cs
    - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs

key-decisions:
  - "Always-set-TTL fallback via KeyExpireAsync on each Redis increment — avoids SE.Redis 2.8.41 ExpireWhen.HasNoExpiry API uncertainty (RESEARCH Pitfall 3)"
  - "TryAddSingleton factory returns null! when IConnectionMultiplexer absent — single-instance installs see no behavior change"
  - "Dual-write pattern: LogErrorCounter continues incrementing in-memory ErrorRateRingBuffer AND fires IRedisErrorRateCounter.IncrementError() — ErrorRateRingBuffer retained as hot-path local write side"
  - "HealthProbeService uses -1 sentinel from RecentErrorCountAsync() as fallback signal — no exception propagation from Redis failures"
  - "Test uses distinct superadmin usernames (replica-a / replica-b) per host to avoid UNIQUE constraint on shared Postgres"

patterns-established:
  - "Optional Redis service pattern: TryAddSingleton<IService>(sp => sp.GetService<IConnectionMultiplexer>() is null ? null! : new Impl(mux, opts))"
  - "Two-host cross-replica test: TRUNCATE admin_users in constructor + distinct usernames + shared RedisFixture + Resolve<IRedisErrorRateCounter> from hostA + Resolve<IHealthProbeService> from hostB"

requirements-completed: [ADMIN-14]

# Metrics
duration: 6min
completed: 2026-06-06
---

# Phase 12 Plan 03: Redis Cross-Replica Error Counter Summary

**Additive Redis-backed INCRBY error counter (gamekit:admin:errors:{epoch_bucket}) that aggregates across replicas so the health panel shows the true fleet-wide error rate, proven by a two-host Testcontainers SC#1 test.**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-06-06T01:17:18Z
- **Completed:** 2026-06-06T01:22:30Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments
- Created `IRedisErrorRateCounter` + `RedisErrorRateCounter` — INCRBY on per-second time-bucketed Redis keys with sliding-window MGET aggregate read; writes fire-and-forget (never throw); reads return -1 on Redis failure
- Wired dual-write in `LogErrorCounter` (in-memory ring buffer + Redis) and async aggregate read in `HealthProbeService.ProbeErrorRateAsync`, with -1 sentinel fallback to in-memory
- Registered `IRedisErrorRateCounter` conditionally in `AddGameKitAdmin` via `TryAddSingleton` factory returning `null!` when no `IConnectionMultiplexer` is registered — single-instance installs behave exactly as before
- SC#1 proven by `RedisErrorCounterTests`: 15 errors written via `hostA.Resolve<IRedisErrorRateCounter>()` surface as "Degraded" on `hostB.Resolve<IHealthProbeService>().ProbeAsync()` over the shared Redis fixture

## Task Commits

1. **Task 1: IRedisErrorRateCounter + RedisErrorRateCounter** - `153dbe5` (feat)
2. **Task 2: Wire dual-write + async aggregate read + conditional registration** - `7e3ae3f` (feat)
3. **Task 3: SC#1 two-host cross-replica integration test** - `ffb8661` (test)

**Plan metadata:** (this SUMMARY commit)

## Files Created/Modified
- `src/GameKit.Admin.UI/Services/IRedisErrorRateCounter.cs` - Public interface: `void IncrementError()` + `Task<long> RecentErrorCountAsync(CancellationToken)`
- `src/GameKit.Admin.UI/Services/RedisErrorRateCounter.cs` - Internal sealed implementation using `gamekit:admin:errors:{epoch_bucket}` key schema; always-set-TTL pattern
- `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` - Added optional `IRedisErrorRateCounter? redis` ctor param; `CountingLogger.Log` dual-writes both sinks
- `src/GameKit.Admin.UI/Services/HealthProbeService.cs` - Added optional `IRedisErrorRateCounter? redisErrors` ctor param; `ProbeErrorRate()` converted to `ProbeErrorRateAsync()` preferring Redis aggregate with -1 fallback
- `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` - Added `TryAddSingleton<IRedisErrorRateCounter>` conditional factory block + `using StackExchange.Redis`
- `tests/GameKit.Admin.Integration.Tests/RedisErrorCounterTests.cs` - SC#1 two-host cross-replica Testcontainers integration test

## Decisions Made
- **Always-set-TTL:** `KeyExpireAsync(key, _keyTtl)` on every increment avoids `ExpireWhen.HasNoExpiry` uncertainty in SE.Redis 2.8.41 (RESEARCH Pitfall 3). Minor overhead, unconditionally correct.
- **TryAddSingleton + null! factory:** When no `IConnectionMultiplexer` is registered, the factory returns `null!` and both `LogErrorCounter` and `HealthProbeService` receive `null` for the optional parameter — single-instance path is unchanged.
- **Distinct test usernames:** Two hosts share one Postgres DB; using `replica-a` / `replica-b` instead of duplicate `root` avoids the `UNIQUE(username)` constraint violation during `SeedAdminAsync`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing `using StackExchange.Redis` in AdminBuilderExtensions**
- **Found during:** Task 2 (AdminBuilderExtensions modification)
- **Issue:** CS0246 — `IConnectionMultiplexer` not found; the file had no `using StackExchange.Redis` directive
- **Fix:** Added `using StackExchange.Redis;` to the file's using section
- **Files modified:** `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`
- **Verification:** `dotnet build src/GameKit.Admin.UI/GameKit.Admin.UI.csproj -c Debug` clean
- **Committed in:** `7e3ae3f` (Task 2 commit)

**2. [Rule 1 - Bug] Test seed conflict — duplicate superadmin username on shared Postgres**
- **Found during:** Task 3 (initial test run)
- **Issue:** Both `AdminTestHost.StartAsync` calls seeded `"root"` to the same shared DB, hitting the `UNIQUE(username)` constraint on `admin_users`; `SaveChangesAsync` threw a `PostgresException`
- **Fix:** Used distinct usernames `"replica-a"` / `"replica-b"` for the two hosts; added `ResetAdminUsers` TRUNCATE call in the constructor (following `HealthProbeTests` pattern)
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/RedisErrorCounterTests.cs`
- **Verification:** Test passes green (`Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`)
- **Committed in:** `ffb8661` (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking build error, 1 runtime bug in test seeding)
**Impact on plan:** Both fixes necessary for correctness. No scope creep.

## Issues Encountered
- None beyond the two auto-fixed deviations above.

## Threat Surface Scan
No new network endpoints, auth paths, or schema changes. The Redis key schema (`gamekit:admin:errors:{epoch_bucket}`) is a fixed literal derived only from server clock + configured bucket width — no user input enters the key (T-12-03-TAM2 mitigated). The fire-and-forget write path never propagates exceptions into the logging pipeline (T-12-03-DOS mitigated).

## Known Stubs
None — all code paths are fully wired.

## Next Phase Readiness
- 12-03 complete: `IRedisErrorRateCounter` is registered in `AddGameKitAdmin` and available for SC#2 (AdminEventHub) plans that run immediately after in the same wave
- 12-04 can add its `SignalR + AdminEventHub` registration block immediately after the SC#1 Redis counter block in `AdminBuilderExtensions` (line ~183 after the `TryAddSingleton<IRedisErrorRateCounter>` call)
- `dotnet build GameKit.sln -warnaserror` clean; all existing `HealthProbeTests` still pass

## Self-Check: PASSED

- `src/GameKit.Admin.UI/Services/IRedisErrorRateCounter.cs` — FOUND
- `src/GameKit.Admin.UI/Services/RedisErrorRateCounter.cs` — FOUND
- `tests/GameKit.Admin.Integration.Tests/RedisErrorCounterTests.cs` — FOUND
- Commit `153dbe5` — FOUND (feat: IRedisErrorRateCounter + RedisErrorRateCounter)
- Commit `7e3ae3f` — FOUND (feat: dual-write + registration)
- Commit `ffb8661` — FOUND (test: SC#1 cross-replica test)
- `dotnet build GameKit.sln -warnaserror` — Build succeeded
- `dotnet test --filter RedisErrorCounter` — Passed! 1/1

---
*Phase: 12-admin-multi-replica-distribution-close-out*
*Completed: 2026-06-06*
