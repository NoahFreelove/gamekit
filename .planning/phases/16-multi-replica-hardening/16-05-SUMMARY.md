---
phase: 16-multi-replica-hardening
plan: "05"
subsystem: tests/GameKit.Matchmaking.Integration.Tests
tags: [graceful-drain, matchmaking, SCALE-05, SCALE-02-proof]
dependency_graph:
  requires: [16-02, 16-03, 16-04]
  provides: [SCALE-05-ci-gate, SCALE-02-end-to-end-proof]
  affects: [tests/GameKit.Matchmaking.Integration.Tests]
tech_stack:
  added: []
  patterns:
    - "GracefulDrainTests: 100 concurrent HTTP requests + StopHostAsync mid-flight + zero 5xx assertion"
    - "Redis StringGetAsync(_app.MatcherLockKey) + IsNullOrEmpty — end-to-end SCALE-02 proof"
    - "Duplicate game_sessions guard via IdempotencyKey GROUP BY HAVING COUNT > 1"
    - "StopHostAsync() public passthrough on MatchmakingTestApp for host-stop primitive"
key_files:
  created:
    - tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs
  modified:
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs
decisions:
  - "Use _app.Client (shared TestServer HttpClient) with per-request Authorization header rather than 100 CreateClient calls — avoids connection pool flooding"
  - "Catch HttpRequestException/TaskCanceledException/OperationCanceledException per request and return null — these are client-side connection-race outcomes, not server 5xx errors"
  - "Assert lock absent after stop using a fresh ConnectionMultiplexer (host's mux is disposed) connecting to RedisFixture.ConnectionString"
  - "Duplicate check queries IdempotencyKey GROUP BY HAVING COUNT > 1 — catches duplicates even if the key was set after partial drain"
  - "StopHostAsync() added as thin public passthrough on MatchmakingTestApp — only call in DisposeAsync was already there; needed a public surface for test body to trigger shutdown mid-flight"
metrics:
  duration: "~10 minutes"
  completed: "2026-06-23"
  tasks_completed: 1
  files_changed: 2
status: complete
requirements: [SCALE-05]
---

# Phase 16 Plan 05: GracefulDrainTests Summary

SCALE-05 CI gate: 100 concurrent in-flight matchmaking HTTP requests + host stop proves zero 5xx responses, the leader lock is released proactively (not TTL-expired), and no duplicate game_sessions result.

## What Was Built

### Task 1 — GracefulDrainTests (commit 734aef2)

`GracefulDrainTests` (`[Collection("Matchmaking")]`, `[Trait("Category","GracefulDrain")]`, `IAsyncLifetime`) with one `MatchmakingTestApp` instance (default lock TTL — the point is proactive release, not TTL expiry).

**Fact: `GracefulDrain_NoFiveXx_LeaseReleased_NoDuplicateSessions`**

1. **Arrange:** 100 unique player ids created (`EnsurePlayerRow` to satisfy FKs). Each player sends one `POST /api/mm/queue` with no `PoolName` (routes to `default` pool per memory note).

2. **Act:** Build 100 request tasks (`SendEnqueueRequestAsync` per player — uses shared `_app.Client` with per-request `Authorization` header). Fire without awaiting. Call `_app.StopHostAsync()` while requests are in flight. Await `Task.WhenAll` outcomes.

3. **Assert 1 — zero 5xx:** Collect all non-null response objects; assert none has `StatusCode >= 500`. `HttpRequestException`/`TaskCanceledException`/`OperationCanceledException` are caught and returned as `null` — connection-race outcomes, not server errors.

4. **Assert 2 — lock absent:** Open a fresh `ConnectionMultiplexer` to `_redis.ConnectionString`. `db.StringGetAsync(_app.MatcherLockKey)`. Assert `IsNullOrEmpty` — proves the 16-03 `CancellationToken.None` finally-path fix released the lease proactively, not via TTL expiry.

5. **Assert 3 — no duplicates:** Raw Npgsql query `GROUP BY "IdempotencyKey" HAVING COUNT(*) > 1`. Count = 0.

**StopHostAsync passthrough on MatchmakingTestApp:**

Added minimal `public Task StopHostAsync() => _host!.StopAsync();` after `GetTicker()`. `DisposeAsync` already calls `_host.StopAsync()` internally; `IHost.StopAsync` is idempotent.

## CI Gate Results

| Filter | Tests | Result |
|--------|-------|--------|
| `--filter "Category=GracefulDrain"` | 1/1 | PASSED |
| Full Matchmaking integration suite | 84/84 | PASSED |

## Drain Test Result

```
Passed SCALE-05: 100 concurrent requests + host stop → zero 5xx, lease released, zero duplicate matches [449 ms]
```

All three assertions passed:
- Zero 5xx responses (ASP.NET Core drained in-flight requests before stop)
- Matcher lock key absent in Redis after stop (CancellationToken.None fix confirmed end-to-end)
- Zero duplicate game_sessions rows

## Deviations from Plan

None — plan executed exactly as written. The `StopHostAsync()` passthrough was anticipated in the plan's context notes ("add only a thin passthrough — allowed incidental change in THIS plan").

## Known Stubs

None. The test exercises live infrastructure via Testcontainers (Postgres + Redis) with 100 real HTTP requests to the in-process test server.

## Threat Flags

No new network endpoints, auth paths, file access patterns, or trust boundary changes. This plan is test-infrastructure-only.

## Self-Check: PASSED

- `tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` (StopHostAsync added) — FOUND
- Commit 734aef2 — present in git log
- `--filter "Category=GracefulDrain"` → 1/1 PASSED
- Full suite → 84/84 PASSED
