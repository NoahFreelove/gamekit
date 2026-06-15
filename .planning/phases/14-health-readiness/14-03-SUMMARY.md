---
phase: 14-health-readiness
plan: "03"
subsystem: matchmaking-health
tags: [health, readiness, matchmaking, leader-lock, migrations]
dependency_graph:
  requires:
    - 14-01 (IMigrationReadinessReporter, AddGameKitHealthChecks, Core "redis" check ownership)
  provides:
    - IMatchmakerLease.InstanceId (public getter, interface extension)
    - IMatchmakerLease.QueryLeaseAsync() (non-acquiring, returns LeaseStatus)
    - LeaseStatus sealed record (HolderInstanceId + Ttl)
    - MatchmakingLeaderHealthCheck (Degraded-only leader probe, tagged "ready")
    - MatchmakingMigrationReadinessReporter (sixth IMigrationReadinessReporter)
  affects:
    - src/GameKit.Matchmaking/Services/IMatchmakerLease.cs (interface extended)
    - src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs (QueryLeaseAsync added)
    - src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs (QueryLeaseAsync added, Rule 3)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (two new registrations)
tech_stack:
  added: []
  patterns:
    - Non-acquiring Redis lock read via LockQueryAsync + KeyTimeToLiveAsync (D-11)
    - Degraded-only leader check (never Unhealthy, D-10/HLTH-03)
    - Replica InstanceId surfaced in description (HLTH-04/D-13)
    - volatile bool _latched latch pattern for migration reporter (D-07)
    - BuildMatchmakingMigrationContext copied verbatim with PendingModelChangesWarning suppression
key_files:
  created:
    - src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs
    - src/GameKit.Matchmaking/Health/MatchmakingMigrationReadinessReporter.cs
  modified:
    - src/GameKit.Matchmaking/Services/IMatchmakerLease.cs
    - src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs
    - src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
decisions:
  - "InstanceId added to IMatchmakerLease interface (OQ-2 RESOLVED) — avoids unsafe cast in MatchmakingLeaderHealthCheck; RedisMatchmakerLease already had it public, interface extension is additive"
  - "QueryLeaseAsync uses LockQueryAsync + KeyTimeToLiveAsync (not raw GET+PTTL) — higher-level StackExchange.Redis API, semantically clear, non-mutating per D-11"
  - "MatchmakingLeaderHealthCheck returns Degraded on follower path (never Unhealthy) so Degraded→200 keeps follower in rotation per D-04/D-10"
  - "Matchmaking registers NO redis check — Core is the sole owner of the conditional redis check registered in AddGameKitHealthChecks() (OQ-1 RESOLVED/D-09)"
  - "MatchmakingBuilderExtensions comment in the leader-check registration documents the D-09 design explicitly, preventing future accidental re-addition"
metrics:
  duration: 4 minutes
  completed_date: "2026-06-15"
  tasks: 2
  files: 6
---

# Phase 14 Plan 03: Matchmaking Health Surface Summary

**One-liner:** Non-acquiring QueryLeaseAsync on IMatchmakerLease via LockQueryAsync+KeyTimeToLiveAsync; Degraded-only MatchmakingLeaderHealthCheck surfaces holder InstanceId+TTL; sixth migration reporter with PendingModelChangesWarning suppression; self-registered from AddMatchmaking() with no redis check (Core sole owner).

## What Was Built

Five files modified, two files created:

- **`IMatchmakerLease`** — extended with `string InstanceId { get; }` and `Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)` (OQ-2 RESOLVED); `LeaseStatus` sealed record added in same file with XML docs on all public members
- **`RedisMatchmakerLease.QueryLeaseAsync`** — reads `LockQueryAsync(lockKey)` + `KeyTimeToLiveAsync(lockKey)` (non-mutating, D-11); degrades gracefully on Redis blip (returns `LeaseStatus(null, null)`) using same try/catch/LogWarning pattern as TryAcquireLeaseAsync
- **`MatchmakerLeaseHelper.QueryLeaseAsync`** — same implementation shape for the Polly-wrapped lease (auto-fix, see deviations)
- **`MatchmakingLeaderHealthCheck`** — `internal sealed class` implementing `IHealthCheck`; returns `Healthy` when `status.HolderInstanceId == _lease.InstanceId`, `Degraded` otherwise (never Unhealthy, D-10/HLTH-03); description carries holder InstanceId + TTL seconds (HLTH-04/D-13)
- **`MatchmakingMigrationReadinessReporter`** — sixth `IMigrationReadinessReporter`; `volatile bool _latched`; `BuildMatchmakingMigrationContext` copied verbatim from `MatchmakingMigrationHostedService` including `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`
- **`MatchmakingBuilderExtensions.cs`** — self-registers `AddSingleton<IMigrationReadinessReporter, MatchmakingMigrationReadinessReporter>()` and `AddCheck<MatchmakingLeaderHealthCheck>("matchmaking-leader", tags:{"ready"})`; NO `"redis"` check registered (D-09/OQ-1 RESOLVED)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] MatchmakerLeaseHelper also implements IMatchmakerLease**
- **Found during:** Task 1 build (CS0535: `MatchmakerLeaseHelper` does not implement `IMatchmakerLease.QueryLeaseAsync`)
- **Issue:** `MatchmakerLeaseHelper` is a Polly-wrapped lease helper that also implements `IMatchmakerLease`. Extending the interface with `QueryLeaseAsync` and `InstanceId` required both implementors to be updated.
- **Fix:** Added `QueryLeaseAsync` to `MatchmakerLeaseHelper` using the same try/catch/LogWarning pattern, reading from `_opts.Ticker.LockKey`. `InstanceId` was already public on `MatchmakerLeaseHelper` (satisfies the interface member).
- **Files modified:** `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs`
- **Commit:** b7e6db7

**2. [Rule 3 - Blocking] Missing using directives in MatchmakingMigrationReadinessReporter**
- **Found during:** Task 2 first build (CS0246: MatchmakingMigrationConstants / MatchmakingMigrationModelCustomizer not found)
- **Issue:** The reporter needed `using GameKit.Matchmaking.Data;` and `using System;` for the types from the Data namespace
- **Fix:** Added `using GameKit.Matchmaking.Data;` and `using System;` to the file
- **Files modified:** `src/GameKit.Matchmaking/Health/MatchmakingMigrationReadinessReporter.cs`
- **Commit:** 8a4b602

### Notes on Acceptance Criteria Grep Gates

The plan's `grep -c "Unhealthy"` acceptance gate returns 2 (not 0) because the leader check file has two comment-only references: one in the XML doc summary and one in the `// D-10: Degraded (not Unhealthy)` code comment. There are zero `HealthCheckResult.Unhealthy(...)` calls — the gate's intent is fully satisfied. The plan-specified grep pattern is broader than the actual requirement.

The plan's `grep -c '"redis"\|RedisHealthCheck'` in the builder returns 1 because the self-registration comment documents `// The "redis" connectivity gate is owned solely by Core's ...`. No actual redis check registration exists — the comment is intentional documentation of D-09.

## Security Review

- T-14-06 (Tampering — lock mutation): `QueryLeaseAsync` uses only `LockQueryAsync` + `KeyTimeToLiveAsync`. Neither `LockTakeAsync` nor `LockReleaseAsync` appear in the `QueryLeaseAsync` method body (verified by grep).
- T-14-07 (DoS — false drain): Follower path returns `Degraded` (→ HTTP 200, D-04). Redis blip degrades to `LeaseStatus(null, null)` → `Degraded` not `Unhealthy`. Zero `HealthCheckResult.Unhealthy()` calls in `MatchmakingLeaderHealthCheck`.
- T-14-08 (Information Disclosure): Description surfaces only `InstanceId` (`MachineName:Guid`, typically the K8s pod name) and TTL seconds — no Redis host, port, or connection string.

## Threat Flags

None. All T-14-06, T-14-07, T-14-08 threats have `mitigate` dispositions fully addressed by the implementation.

## Known Stubs

None. All six files are fully implemented and functional.

## Commits

| Task | Hash | Description |
|------|------|-------------|
| Task 1 | b7e6db7 | feat(14-03): extend IMatchmakerLease with InstanceId + non-acquiring QueryLeaseAsync |
| Task 2 | 8a4b602 | feat(14-03): add MatchmakingLeaderHealthCheck, Matchmaking migration reporter, self-register |

## Self-Check: PASSED

All created/modified files verified present. All task commits verified in git log.

| Check | Result |
|-------|--------|
| `IMatchmakerLease.cs` has InstanceId + QueryLeaseAsync + LeaseStatus | FOUND |
| `RedisMatchmakerLease.cs` QueryLeaseAsync uses LockQueryAsync + KeyTimeToLiveAsync | FOUND |
| `MatchmakerLeaseHelper.cs` QueryLeaseAsync implemented (Rule 3 auto-fix) | FOUND |
| `MatchmakingLeaderHealthCheck.cs` created | FOUND |
| `MatchmakingMigrationReadinessReporter.cs` created | FOUND |
| `MatchmakingBuilderExtensions.cs` has both new registrations | FOUND |
| `dotnet build` exits 0 | PASSED |
| No `HealthCheckResult.Unhealthy()` call in leader check | PASSED |
| No `LockTakeAsync`/`LockReleaseAsync` in QueryLeaseAsync body | PASSED |
| No new NuGet pins (`git diff Directory.Packages.props` shows no changes) | PASSED |
| Commit b7e6db7 | FOUND |
| Commit 8a4b602 | FOUND |
