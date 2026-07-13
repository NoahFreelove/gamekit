---
phase: 14-health-readiness
reviewed: 2026-06-14T00:00:00Z
depth: standard
files_reviewed: 32
files_reviewed_list:
  - samples/TicTacToeDuel/Program.cs
  - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
  - src/GameKit.Admin.UI/Health/AdminMigrationReadinessReporter.cs
  - src/GameKit.Admin.UI/Services/HealthProbeService.cs
  - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
  - src/GameKit.Auth/Health/AuthMigrationReadinessReporter.cs
  - src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs
  - src/GameKit.Core/GameKit.Core.csproj
  - src/GameKit.Core/Health/CoreMigrationReadinessReporter.cs
  - src/GameKit.Core/Health/GameKitHealthResponseWriter.cs
  - src/GameKit.Core/Health/IMigrationReadinessReporter.cs
  - src/GameKit.Core/Health/MigrationAggregateHealthCheck.cs
  - src/GameKit.Core/Health/PostgresHealthCheck.cs
  - src/GameKit.Core/Health/RedisHealthCheck.cs
  - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
  - src/GameKit.Lobby/Health/LobbyMigrationReadinessReporter.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
  - src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs
  - src/GameKit.Matchmaking/Health/MatchmakingMigrationReadinessReporter.cs
  - src/GameKit.Matchmaking/Services/IMatchmakerLease.cs
  - src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs
  - src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs
  - src/GameKit.Rankings/Health/RankingsMigrationReadinessReporter.cs
  - tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs
  - tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs
  - tests/GameKit.Core.Integration.Tests/HealthLeakTests.cs
  - tests/GameKit.Core.Integration.Tests/HealthTestHelpers.cs
  - tests/GameKit.Core.Integration.Tests/HealthTestHost.cs
  - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs
  - tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs
findings:
  critical: 1
  warning: 6
  info: 4
  total: 11
status: issues_found
---

# Phase 14: Code Review Report

**Reviewed:** 2026-06-14T00:00:00Z
**Depth:** standard
**Files Reviewed:** 32 (31 listed source files; `AdminMigrationReadinessReporter.cs` listed once)
**Status:** issues_found

## Summary

Phase 14 wires the health/readiness surface: a whitelist-only response writer, conditional Postgres/Redis checks, six per-package migration-readiness latches, and a Degraded-only matchmaker-leader check. The architecture is mostly sound — the latch contract, the conditional Redis guard, the per-package migration contexts (correctly disposed via `await using`), and the Degraded-only leader semantics are all implemented as specified.

However, the central security objective of the phase (HLTH-05 / D-12: the public health payload must emit only infra-free constants) is **violated** by the matchmaker-leader check, which surfaces `Environment.MachineName` into the anonymous `/health/ready` body. The phase's own leak test suite (`HealthLeakTests`) never exercises a host with the matchmaking-leader check registered, so the regression is undetected. This is the one BLOCKER. The remaining findings concern a non-acquiring-query race that can mislabel the leader, a `report.Status` aggregation that lets a degraded *connectivity* signal hide behind 200, incomplete cancellation propagation, and a latch race that is benign but worth noting.

## Critical Issues

### CR-01: Matchmaker-leader check leaks `Environment.MachineName` into the anonymous `/health/ready` body (HLTH-05 / D-12 violation)

**File:** `src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs:37-44`
**Also:** `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs:95-96` (registers `"matchmaking-leader"` with tag `"ready"`); `src/GameKit.Core/Health/GameKitHealthResponseWriter.cs:54-62` (emits every entry's `description` verbatim)

**Issue:**
The phase mandate states every `IHealthCheck` description must be a "hand-authored infra-free constant" and the public payload must "never emit Exception/Data/host/connection-string." The `MatchmakingLeaderHealthCheck` description is **not** a constant — it interpolates `_lease.InstanceId`, and `InstanceId` is `$"{Environment.MachineName}:{Guid.NewGuid()}"` (`RedisMatchmakerLease.cs:46`, `MatchmakerLeaseHelper.cs:68`). The leader branch emits `$"leader: {InstanceId}, ttl: ..."` and the follower branch emits `$"...holder: {status.HolderInstanceId}, ttl: ..."` — both contain the machine hostname.

`MatchmakingLeaderHealthCheck` is registered with tag `"ready"`, so it runs on `GET /health/ready`. That endpoint is `.AllowAnonymous()` (`GameKitHealthBuilderExtensions.cs:154`) and the response writer (`GameKitHealthResponseWriter.cs:54-62`) iterates **all** `report.Entries` and writes each `entry.Description` verbatim — it does not distinguish "constant" descriptions from interpolated ones. In the reference `TicTacToeDuel` sample, Matchmaking is installed (`Program.cs:104`), so an unauthenticated `curl /health/ready` returns the server hostname in the JSON body. Hostname disclosure is exactly the class of infra-detail leakage HLTH-05 / D-14 was created to prevent.

The phase's own guard does not catch this: `HealthLeakTests` (`HealthLeakTests.cs`) and `HealthTestHost` (`HealthTestHost.cs:21-26`) wire **Core only** ("no Auth, Matchmaking, Admin"), so the matchmaking-leader description is never present in any asserted payload. The leak ships untested.

D-13/HLTH-04 wanting operator visibility of the holder identity is legitimate, but that visibility belongs on the **authenticated** admin panel (`HealthProbeService`), not the anonymous k8s probe payload. The two requirements conflict and the anonymous-payload constraint must win.

**Fix:** Keep the leader/follower status in the HTTP status code (Healthy/Degraded already convey rotation state) but strip the identity from the *description* that reaches the anonymous writer. Emit an infra-free constant and move InstanceId/TTL into `HealthReportEntry.Data` (which the whitelist writer already drops from the public body but the authenticated admin `HealthProbeService` can read):

```csharp
public async Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context, CancellationToken cancellationToken = default)
{
    var status = await _lease.QueryLeaseAsync(cancellationToken).ConfigureAwait(false);

    // Identity + TTL go in Data (dropped by the whitelist writer; readable by the
    // authenticated admin panel). Description stays an infra-free constant (HLTH-05).
    var data = new Dictionary<string, object>
    {
        ["holder"] = status.HolderInstanceId ?? "(unheld)",
        ["ttlSeconds"] = status.Ttl?.TotalSeconds ?? 0,
    };

    if (status.HolderInstanceId == _lease.InstanceId)
        return HealthCheckResult.Healthy("leader", data);

    return HealthCheckResult.Degraded(
        status.HolderInstanceId is not null ? "not leader" : "not leader; lock unheld",
        data: data);
}
```

Then extend `HealthLeakTests` to spin up a host with `AddMatchmaking()` + the leader check registered and assert `MachineName` (and any guid) is absent from `/health/ready` — otherwise this regresses silently again.

## Warnings

### WR-01: `report.Status` can downgrade a real connectivity failure to Degraded→200, masking an Unhealthy Postgres/Redis behind a follower

**File:** `src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs:144-154`
**Also:** `src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs:41`

**Issue:**
`/health/ready` maps `Degraded → 200` (D-04, correct in isolation). But the aggregate `report.Status` is the **max severity** across all "ready" checks, and the matchmaking-leader check returns `Degraded` on every non-leader replica as steady-state. ASP.NET Core computes the overall status as the worst entry: if Postgres is `Healthy` and the leader check is `Degraded`, overall is `Degraded` → 200 (fine). But the concern is the inverse asymmetry already present and the precedent it sets: a permanently-Degraded steady-state check means the `/health/ready` aggregate **never reports Healthy** on follower replicas — it sits at Degraded forever. Operators reading the top-level `status` field as a binary health signal will see "Degraded" on N-1 of N replicas as normal, training them to ignore Degraded — which then masks a genuinely degraded dependency. The leader-state signal should not pollute the cluster-wide readiness aggregate this way; consider registering `matchmaking-leader` under a separate tag (e.g. `"leader"`) and exposing it on a distinct endpoint or only in the admin panel, leaving `/health/ready` to reflect dependency connectivity only.

**Fix:** Tag the leader check `"leader"` (not `"ready"`) and either (a) leave it out of `/health/ready` entirely, or (b) add a third endpoint `/health/leader`. The k8s readiness gate should track Postgres + Redis + migrations; leadership is orthogonal to "can this replica serve traffic."

### WR-02: `QueryLeaseAsync` performs two non-atomic Redis round-trips, so the leader check can report a holder with a `null` TTL (or vice-versa)

**File:** `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs:99-116`
**Also:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs:200-217`

**Issue:**
`QueryLeaseAsync` issues `LockQueryAsync` then a separate `KeyTimeToLiveAsync`. These are two independent commands with no transaction/`MULTI`. If the lock expires between the two calls, `holder` is non-null but `ttl` is null; if the lock is *acquired* between the two calls, `holder` is null but `ttl` is non-null. The mandate ("QueryLeaseAsync must be non-acquiring — `LockQueryAsync` + `KeyTimeToLiveAsync`, never `LockTake`") is satisfied for the non-acquiring requirement, but the two-call sequence produces a torn snapshot. The leader check (`MatchmakingLeaderHealthCheck.cs:36-44`) then renders `ttl: s` formatting `status.Ttl?.TotalSeconds:F0` — a null TTL with a non-null holder prints `ttl: s` (empty), and a populated TTL with a null holder routes to the `"lock currently unheld"` branch while a valid lease actually exists. The result is a momentarily wrong operator-facing report and a self-inflicted false "unheld" reading during the holder→TTL gap.

**Fix:** Read both values in a single Lua eval (or a transaction) so the holder and TTL come from the same point-in-time, e.g. a `redis.call('GET', KEYS[1])` + `redis.call('PTTL', KEYS[1])` script returning both, or at minimum document that the snapshot is best-effort and have the description tolerate the torn state (treat non-null holder + null TTL as "held, ttl unknown" rather than printing an empty value).

### WR-03: Cancellation token is dropped on the Redis ping and on the lease query

**File:** `src/GameKit.Core/Health/RedisHealthCheck.cs:47`
**Also:** `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs:104-105`, `MatchmakerLeaseHelper.cs:205-206`

**Issue:**
`db.PingAsync().ConfigureAwait(false)` ignores `cancellationToken`. `LockQueryAsync`/`KeyTimeToLiveAsync` in the lease accept the `ct` parameter into the method but never pass it to Redis. StackExchange.Redis async ops do not take a `CancellationToken` overload (they honor `SyncTimeout`/`AsyncTimeout` instead), so this is partly a library constraint — but it means a `/health/ready` probe with a client-side deadline will not actually abort a hung Redis call; the check blocks until the multiplexer's own timeout. For a readiness probe that orchestrators expect to return fast, an unbounded ping under a Redis partition can exceed the probe's `timeoutSeconds`. The Postgres check explicitly bounds itself (`CommandTimeout = 2`, `PostgresHealthCheck.cs:47`) — the Redis check has no equivalent bound and relies entirely on the consumer's multiplexer config.

**Fix:** Either pass `flags`/rely on a documented `AsyncTimeout` and state the dependency in the XML doc, or wrap the ping in `Task.WhenAny(db.PingAsync(), Task.Delay(timeout, ct))` to enforce a bound symmetric with the 2s Postgres timeout. At minimum, the unused `ct` parameter dropping should be documented so a maintainer does not assume cancellation works.

### WR-04: `PostgresHealthCheck.OpenAsync` is not bounded by the 2-second timeout the XML doc advertises

**File:** `src/GameKit.Core/Health/PostgresHealthCheck.cs:43-48`

**Issue:**
The class doc claims a "2-second command timeout (D-08)." `CommandTimeout = 2` is set on the `cmd` (line 47), which bounds only `ExecuteScalarAsync`. `conn.OpenAsync(cancellationToken)` (line 44) is governed by the connection string's `Timeout`/`Connection Idle Lifetime`, not by `CommandTimeout`. Against an unreachable host with the Npgsql default connect timeout (15s), the readiness probe blocks ~15s on `OpenAsync` before the 2s command timeout ever applies. The `HealthEndpointTests.Ready_Returns_503_When_Postgres_Down_CoreOnly` test uses `Port=9` (connection-refused, which fails fast), so the slow-open path on a *filtered/black-holed* host is never exercised. The "2-second" guarantee in the doc is misleading.

**Fix:** Set `Timeout=2` (connect timeout) on the connection — either append it to the probe connection string or build an `NpgsqlConnectionStringBuilder` with `Timeout = 2; CommandTimeout = 2`. Update the XML doc to state both the connect and command bounds.

### WR-05: Migration-readiness latch has a benign-but-undocumented double-query race; the latch is never re-validated after a connection-string change

**File:** `src/GameKit.Core/Health/CoreMigrationReadinessReporter.cs:50-69` (pattern repeated in all six reporters)

**Issue:**
`if (_latched) return true;` then later `_latched = true;` is a check-then-act on a `volatile bool` with no compare-and-swap. Two concurrent first probes can both observe `_latched == false`, both run `GetPendingMigrationsAsync`, and both open a fresh per-package `NpgsqlConnection`. This is *correct* (idempotent, both latch true) but means the "no DB round-trip after first success" guarantee in the interface doc (`IMigrationReadinessReporter.cs:21-25`) is violated under the first concurrent burst — every sibling reporter can issue N redundant connections for N simultaneous startup probes. Not a data-loss bug, but it contradicts the documented latch contract and, combined with WR-01-adjacent connection churn, multiplies startup DB connections. Worth a one-line note or an `Interlocked`/`volatile`-read-once to make the contract honest.

**Fix:** Acceptable as-is for correctness; if the "single round-trip" contract matters, gate the query behind a `SemaphoreSlim(1)` or accept the race explicitly in the XML doc ("may issue redundant queries during the concurrent first-probe window; latches thereafter").

### WR-06: `HealthProbeService` Redis-error fallback treats any negative count as "Redis down" — an off-by-one on the sentinel contract

**File:** `src/GameKit.Admin.UI/Services/HealthProbeService.cs:108-117`

**Issue:**
The contract per the constructor doc (lines 48-52) is "when Redis returns `-1`, fall back." The code checks `if (count < 0)` (line 111), which catches `-1` but also any other negative value. If `IRedisErrorRateCounter.RecentErrorCountAsync` ever returns a different negative sentinel (e.g. `-2` for a distinct failure mode), it is silently coalesced into the same fallback. More importantly, the status bucketing below (`< 10 => "OK"`) assumes a non-negative count; if the fallback path also somehow returned negative (it cannot today, but the in-memory `RecentErrorCount()` contract is not asserted non-negative), `count < 10` would report `"OK"` for a negative value, masking the failure. Tighten to the documented sentinel.

**Fix:** Compare against the explicit sentinel: `if (count == -1)` per the documented contract, and assert/clamp `count = Math.Max(0, count)` before bucketing so a stray negative never maps to `"OK"`.

## Info

### IN-01: `GameKitHealthResponseWriter` round-trips the JSON through `Encoding.UTF8.GetString(ms.ToArray())`, defeating the "low-allocation" claim

**File:** `src/GameKit.Core/Health/GameKitHealthResponseWriter.cs:46-70`

**Issue:** The XML doc advertises "efficient, low-allocation JSON serialization" via `Utf8JsonWriter` over `MemoryStream`, but line 70 does `Encoding.UTF8.GetString(ms.ToArray())` and hands a `string` to `Response.WriteAsync` — which re-encodes the string back to UTF-8 bytes. This allocates a full byte[] copy plus a string plus a re-encoded byte[], the opposite of low-allocation. (Performance is out of v1 scope, so this is Info, not Warning — but the doc comment is factually wrong.)

**Fix:** Write the `MemoryStream` bytes directly: `ms.Position = 0; return ms.CopyToAsync(ctx.Response.Body);` or `ctx.Response.Body.WriteAsync(ms.GetBuffer().AsMemory(0, (int)ms.Length))`. At minimum, correct the doc comment.

### IN-02: Six per-package migration contexts duplicate ~20 lines of `BuildXMigrationContext` each; only the customizer type and constants differ

**File:** `src/GameKit.Auth/Health/AuthMigrationReadinessReporter.cs:71-88`, `AdminMigrationReadinessReporter.cs:77-94`, `RankingsMigrationReadinessReporter.cs:74-96`, `MatchmakingMigrationReadinessReporter.cs:60-80`, `LobbyMigrationReadinessReporter.cs:74-95`

**Issue:** Five of the six reporters are near-identical (the `IsReadyAsync` latch body plus a `BuildXMigrationContext` that differs only in customizer type, migrations-assembly type, history-table constant, and whether `ConfigureWarnings` is applied). This is copy-paste with a real divergence risk: the Auth/Admin variants deliberately omit `ConfigureWarnings(PendingModelChangesWarning)` while Rankings/Matchmaking/Lobby include it — a future maintainer editing one will not necessarily update the others, and the "snapshot matches model hash" assumption is load-bearing and undocumented at the shared level. Consider a shared `MigrationReadinessReporterBase` taking the customizer factory + constants + a `suppressPendingModelChanges` flag.

**Fix:** Extract an abstract base in `GameKit.Core.Health` parameterized on `(IModelCustomizer customizer, string historyTable, Assembly migrationsAssembly, bool ignorePendingModelChanges)`. Reduces six files to thin subclasses and centralizes the latch.

### IN-03: Stale plan-numbering comments reference "Plan 05-05 / 05-07" lease-merge mechanics that no longer reflect the shipped wiring

**File:** `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs:19-33`, `MatchmakerLeaseHelper.cs:44-52`, `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs:108-136`

**Issue:** Multiple comments narrate inter-plan ordering ("Plan 05-07 ships this minimal helper so... If 05-05's richer helper lands, the builder can swap it via `services.Replace(...)`"). These are development-time scaffolding notes, not API documentation, and they describe a conditional ("if X lands") that has already resolved. They add cognitive load for a maintainer who has no access to plan numbers and cannot verify the claimed swap actually happens. Prefer documenting the *current* behavior (which lease is registered by default and how a consumer overrides it).

**Fix:** Replace plan-numbered narration with present-tense API contract: which `IMatchmakerLease` is the default registration, and the supported override seam.

### IN-04: `MatchmakerLeaseHelper.RenewLeaseAsync` is public and documented but not part of `IMatchmakerLease`; `ReleaseLeaseAsync`/`RenewLeaseAsync` ignore the `ct` parameter

**File:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs:155-197`

**Issue:** `RenewLeaseAsync` exists only on the concrete `MatchmakerLeaseHelper`, not on `IMatchmakerLease`, so any consumer holding the interface (the reconciler/retention services, the health check) cannot renew — they get the minimal `RedisMatchmakerLease` surface. That is intentional per the comments, but it means the ticker must depend on the concrete type, partially defeating the interface abstraction. Separately, `ReleaseLeaseAsync(ct)` (line 184) and `RenewLeaseAsync(ct)` accept `ct` but never pass it to Polly/Redis (`RenewLeaseAsync` passes `ct` to `_polly.ExecuteAsync` correctly; `ReleaseLeaseAsync` does not use Polly and ignores `ct` entirely). Low impact (release is fire-and-forget on shutdown) but the dropped token is inconsistent with `TryAcquireLeaseAsync`.

**Fix:** Document that `RenewLeaseAsync` is intentionally off-interface (ticker-only) and that `ReleaseLeaseAsync` is fire-and-forget so `ct` is advisory; or honor `ct` for symmetry.

---

_Reviewed: 2026-06-14T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
