---
phase: 14-health-readiness
verified: 2026-06-14T00:00:00Z
status: passed
score: 14/14 must-haves verified
overrides_applied: 0
---

# Phase 14: Health & Readiness Verification Report

**Phase Goal:** Every GameKit deployment exposes separate `/health/live` and `/health/ready` endpoints with correct K8s-probe semantics; liveness never fails on a Redis blip; readiness gates on migrations + Postgres; Admin.UI delegates to Core probes.
**Verified:** 2026-06-14
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | `/health/live` returns 200 even when Postgres is stopped — no checks execute | VERIFIED | `GameKitHealthBuilderExtensions.cs` line 139: `Predicate = _ => false`. `HealthEndpointTests.Live_Returns_200_When_Postgres_Unreachable` passes with dead conn string. |
| 2 | `/health/ready` returns 503 while any `IMigrationReadinessReporter` reports pending, then 200 once all six report applied | VERIFIED | `MigrationAggregateHealthCheck` aggregates `IEnumerable<IMigrationReadinessReporter>` and returns `Unhealthy` while `pendingCount > 0`. All six reporters registered. `HealthEndpointTests.Ready_Returns_503_While_Migrations_Pending_Then_200_When_Applied` green. |
| 3 | On a Core-only install (no Redis), `/health/ready` returns 503 when Postgres unreachable, 200 when reachable; Redis ABSENCE never blocks readiness | VERIFIED | `GameKitHealthBuilderExtensions.cs` lines 83-87: Redis check registered only `if (builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer)))`. Tests `Ready_Returns_503_When_Postgres_Down_CoreOnly` and `Ready_Returns_200_When_Postgres_Up_CoreOnly_No_Redis` green. |
| 4 | Matchmaking ticker not holding leader lock reports `Degraded` (NOT `Unhealthy`) on `/health/ready`; probe surfaces holder InstanceId + TTL | VERIFIED | `MatchmakingLeaderHealthCheck.cs` line 41-44: returns `HealthCheckResult.Degraded(...)` on non-leader path; `grep -c "Unhealthy" MatchmakingLeaderHealthCheck.cs` = 0 in code paths (both occurrences are comments). `MatchmakingLeaderHealthCheckTests` (3 tests) green. |
| 5 | `Degraded` → HTTP 200 (follower stays in rotation); `Unhealthy` → HTTP 503 | VERIFIED | `GameKitHealthBuilderExtensions.cs` lines 150-152: `Healthy=200`, `Degraded=200`, `Unhealthy=503` explicitly set in `ResultStatusCodes`. |
| 6 | Health JSON contains only component name + status + human description — no connection strings, hosts, or credentials in any response body | VERIFIED | `GameKitHealthResponseWriter.cs`: `Utf8JsonWriter` emits only `status`, `checks[{name,status,description}]`. `Exception`, `Data`, `Tags` intentionally omitted (lines 60-62 comment). All checks use bare `catch` with hand-authored constant descriptions. `HealthLeakTests` (3 tests) assert no `Host=`, `Password=`, `Port=`, `Username=`, or hostname substring in body, on both healthy and Postgres-down paths. |
| 7 | All six `IMigrationReadinessReporter` implementations exist and are registered | VERIFIED | Files confirmed: `CoreMigrationReadinessReporter.cs`, `AuthMigrationReadinessReporter.cs`, `AdminMigrationReadinessReporter.cs`, `RankingsMigrationReadinessReporter.cs`, `MatchmakingMigrationReadinessReporter.cs`, `LobbyMigrationReadinessReporter.cs`. All registered via `AddSingleton<IMigrationReadinessReporter, TReporter>()` from their respective `Add*` builder. |
| 8 | Each reporter latches on first all-applied result and does not re-query Postgres thereafter | VERIFIED | All six reporters have `private volatile bool _latched;` and return `true` early when latched. Pattern confirmed in all six files. |
| 9 | Rankings, Lobby, and Matchmaking reporters suppress `PendingModelChangesWarning`; Auth and Admin do not | VERIFIED | `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` confirmed in `Rankings`, `Lobby`, `Matchmaking` reporters. Auth and Admin have no such call in code (only comments noting it is not needed). |
| 10 | `HealthProbeService` delegates to `HealthCheckService.CheckHealthAsync()` and no longer contains `ProbePostgresAsync`/`ProbeRedisAsync` | VERIFIED | `HealthProbeService.cs` line 78: `_healthCheckService.CheckHealthAsync(...)`. `grep -c "ProbePostgresAsync|ProbeRedisAsync|NpgsqlConnection|PingAsync|GameKitOptions"` returns 0. `HealthProbeServiceDelegationTests` (2 reflection tests) green. |
| 11 | `QueryLeaseAsync` reads lock via `LockQueryAsync` + `KeyTimeToLiveAsync` without acquiring or modifying it | VERIFIED | `RedisMatchmakerLease.cs` lines 104-108: only `LockQueryAsync` + `KeyTimeToLiveAsync` called in `QueryLeaseAsync` body. No `LockTakeAsync`/`LockReleaseAsync` in that method. |
| 12 | `IMatchmakerLease` exposes `InstanceId` and `QueryLeaseAsync` returning `LeaseStatus` | VERIFIED | `IMatchmakerLease.cs` lines 39, 60-61, 66: `string InstanceId { get; }`, `Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)`, `public sealed record LeaseStatus(string? HolderInstanceId, TimeSpan? Ttl)`. |
| 13 | TicTacToeDuel sample wires `AddGameKitHealthChecks()` after `AddLobby()` and `MapGameKitHealth()` as first Map* call | VERIFIED | `Program.cs` line 138: `gameKitBuilder.AddGameKitHealthChecks()` after line 130 `AddLobby()`. Line 180: `app.MapGameKitHealth()` before `app.MapGameKit()` (line 181). |
| 14 | No new NuGet central pin added (D-01); only `StackExchange.Redis` csproj reference added to `GameKit.Core` | VERIFIED | `Directory.Packages.props` shows `StackExchange.Redis` was already pinned at 2.8.41. `GameKit.Core.csproj` gained only a `PackageReference Include="StackExchange.Redis" />` (no new Version attribute = no new pin). |

**Score:** 14/14 truths verified

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/GameKit.Core/Health/IMigrationReadinessReporter.cs` | Public interface with `ValueTask<bool> IsReadyAsync(CancellationToken ct)` | VERIFIED | Substantive; latch contract documented. |
| `src/GameKit.Core/Health/MigrationAggregateHealthCheck.cs` | Aggregate IHealthCheck over IEnumerable reporters | VERIFIED | Injects `IEnumerable<IMigrationReadinessReporter>`, counts pending, hand-authored descriptions. |
| `src/GameKit.Core/Health/PostgresHealthCheck.cs` | SELECT 1, 2s timeout, infra-free description | VERIFIED | `CommandTimeout = 2`, returns `"connected"` / `"database unreachable"`, bare catch. |
| `src/GameKit.Core/Health/RedisHealthCheck.cs` | PING, registered only when IConnectionMultiplexer present | VERIFIED | Injects `IConnectionMultiplexer`, returns `"ping ok"` / `"ping failed"`, bare catch. |
| `src/GameKit.Core/Health/GameKitHealthResponseWriter.cs` | Whitelist Utf8JsonWriter — status + checks[{name,status,description}] only | VERIFIED | Exception/Data/Tags intentionally omitted. 72 lines, fully implemented. |
| `src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs` | `AddGameKitHealthChecks()` + `MapGameKitHealth()` public surface | VERIFIED | Both methods public static, XML docs, argument null checks, correct wiring. |
| `src/GameKit.Auth/Health/AuthMigrationReadinessReporter.cs` | Auth reporter, no ConfigureWarnings | VERIFIED | `volatile bool _latched`, queries `__ef_migrations_auth`, no warning suppression in code. |
| `src/GameKit.Rankings/Health/RankingsMigrationReadinessReporter.cs` | Rankings reporter, with PendingModelChangesWarning suppression | VERIFIED | `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` present. |
| `src/GameKit.Lobby/Health/LobbyMigrationReadinessReporter.cs` | Lobby reporter, with PendingModelChangesWarning suppression | VERIFIED | Same suppression pattern present. |
| `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` | InstanceId getter + QueryLeaseAsync + LeaseStatus record | VERIFIED | All three added; public, XML-documented. |
| `src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs` | Degraded-only leader-lock IHealthCheck | VERIFIED | Returns Degraded (never Unhealthy) for follower/unheld; surfaces InstanceId + TTL. |
| `src/GameKit.Matchmaking/Health/MatchmakingMigrationReadinessReporter.cs` | Sixth migration reporter, with PendingModelChangesWarning suppression | VERIFIED | Pattern consistent with Rankings/Lobby. |
| `src/GameKit.Admin.UI/Health/AdminMigrationReadinessReporter.cs` | Sixth-of-six reporter, no ConfigureWarnings | VERIFIED | `volatile bool _latched`, no warning suppression in code. |
| `src/GameKit.Admin.UI/Services/HealthProbeService.cs` | Thin adapter delegating to HealthCheckService | VERIFIED | `ProbePostgresAsync`/`ProbeRedisAsync` deleted; `CheckHealthAsync` called; `ProbeErrorRateAsync` unchanged. |
| `tests/GameKit.Core.Integration.Tests/HealthTestHost.cs` | Minimal WebApplication host wiring AddGameKitHealthChecks + MapGameKitHealth | VERIFIED | Wires both with optional Redis knob for Core-only vs Redis tests. |
| `tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs` | HLTH-01/02 live/ready + migration/Postgres/Redis gating tests | VERIFIED | 5 tests covering all HLTH-01/02 behaviors; reported 8 green tests (includes HealthLeakTests). |
| `tests/GameKit.Core.Integration.Tests/HealthLeakTests.cs` | HLTH-05 payload leak assertions | VERIFIED | 3 tests asserting no `Host=`, `Password=`, `Port=`, `Username=`, hostname substring; both healthy and Postgres-down paths covered. |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs` | HLTH-03/04 leader vs follower Degraded tests | VERIFIED | 3 tests: Healthy-leader, Degraded-follower (with holder ID), Degraded-unheld. Zero `HealthStatus.Unhealthy` assertions. |
| `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs` | HLTH-06 delegation contract structural assertion | VERIFIED | 2 reflection tests: asserts no NpgsqlConnection/GameKitOptions/IConnectionMultiplexer in ctor, asserts HealthCheckService IS present. |
| `samples/TicTacToeDuel/Program.cs` | Wires AddGameKitHealthChecks + MapGameKitHealth | VERIFIED | Line 138 after AddLobby(); line 180 as first Map* call outside auth + rate limit. |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MapGameKitHealth` live endpoint | `GameKitHealthResponseWriter.WriteAsync` | `HealthCheckOptions.ResponseWriter` | VERIFIED | Line 140: `ResponseWriter = GameKitHealthResponseWriter.WriteAsync` |
| `MapGameKitHealth` ready endpoint | `GameKitHealthResponseWriter.WriteAsync` | `HealthCheckOptions.ResponseWriter` | VERIFIED | Line 148: same pattern |
| `MapGameKitHealth` ready endpoint | `Degraded→200, Unhealthy→503` | `ResultStatusCodes` dictionary | VERIFIED | Lines 150-152: explicit three-entry mapping |
| `MigrationAggregateHealthCheck` | `IMigrationReadinessReporter` | `IEnumerable<IMigrationReadinessReporter>` ctor injection | VERIFIED | Line 31: `IEnumerable<IMigrationReadinessReporter> reporters` |
| `AddGameKitHealthChecks` | Redis conditional gate | `builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer))` | VERIFIED | Lines 83-87: conditional block |
| `HealthProbeService.ProbeAsync` | `HealthCheckService.CheckHealthAsync` | Constructor-injected `CoreHealthCheckService` | VERIFIED | Line 77-79: delegates to `_healthCheckService.CheckHealthAsync` |
| `HealthProbeService.GetTile` | `HealthReport.Entries["postgres"]/"redis"` | `report.Entries.TryGetValue(checkName, out var entry)` | VERIFIED | Line 90: `TryGetValue` pattern |
| `MatchmakingLeaderHealthCheck` | `IMatchmakerLease.QueryLeaseAsync` | Constructor-injected `IMatchmakerLease` | VERIFIED | Line 33: `_lease.QueryLeaseAsync(cancellationToken)` |
| `RedisMatchmakerLease.QueryLeaseAsync` | Redis lock (non-acquiring read) | `LockQueryAsync + KeyTimeToLiveAsync` (no LockTakeAsync) | VERIFIED | Lines 104-108: read-only; LockTakeAsync not present in this method |
| `AddMatchmaking` | `MatchmakingLeaderHealthCheck` + `MatchmakingMigrationReadinessReporter` | `builder.Services.AddHealthChecks().AddCheck<...>` + `AddSingleton<...>` | VERIFIED | Lines 89 and 96 of MatchmakingBuilderExtensions.cs |
| `AddGameKitAdmin` | `AdminMigrationReadinessReporter` + defensive `AddHealthChecks()` | `AddSingleton<IMigrationReadinessReporter, AdminMigrationReadinessReporter>()` | VERIFIED | Lines 81 and 89 of AdminBuilderExtensions.cs |
| Six `Add*` builders | Six `IMigrationReadinessReporter` registrations | `AddSingleton<IMigrationReadinessReporter, TReporter>()` | VERIFIED | Core (line 94 health extensions), Auth (line 70), Rankings (line 60), Lobby (line 80), Matchmaking (line 89), Admin (line 81). All six confirmed. |
| `TicTacToeDuel/Program.cs` | `AddGameKitHealthChecks` after Redis-using packages | Called at line 138, after `AddLobby()` (line 130) | VERIFIED | Call order ensures IConnectionMultiplexer visible when conditional Redis check registers. |

---

### Data-Flow Trace (Level 4)

Not applicable — phase delivers health check framework infrastructure, not user-facing data rendering components. The health payload data flows are verified via integration tests (HealthEndpointTests, HealthLeakTests, MatchmakingLeaderHealthCheckTests).

---

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| `HealthEndpointTests` (8 tests) | `dotnet test ... --filter HealthEndpointTests|HealthLeakTests` | 8/8 green (orchestrator-confirmed) | PASS |
| `MatchmakingLeaderHealthCheckTests` (3 tests) | `dotnet test ... --filter MatchmakingLeaderHealthCheckTests` | 3/3 green (orchestrator-confirmed) | PASS |
| `HealthProbeServiceDelegationTests` (2 tests) | `dotnet test ... --filter HealthProbeServiceDelegationTests` | 2/2 green (orchestrator-confirmed) | PASS |
| Full solution build | `dotnet build ... -p:NuGetAudit=false` | 0 errors / 0 warnings (orchestrator-confirmed) | PASS |

---

### Probe Execution

No `scripts/*/tests/probe-*.sh` files declared or detected for this phase. Step 7c: SKIPPED (no probe scripts).

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| HLTH-01 | Plans 01, 05 | Live/ready split, JSON, process-only liveness | SATISFIED | `/health/live` (Predicate=false, always 200); `/health/ready` (tag-filtered, Degraded→200, Unhealthy→503). Integration tests green. |
| HLTH-02 | Plans 01-04, 05 | Postgres SELECT 1, Redis PING conditional, migrations per-package `IMigrationReadinessReporter` | SATISFIED | Six reporters all registered and latching. Postgres always-on gate. Redis conditional. Tests cover 503→200 transition and Core-only-no-Redis 200. |
| HLTH-03 | Plans 03, 05 | Three-state: matchmaking follower reports Degraded, not Unhealthy | SATISFIED | `MatchmakingLeaderHealthCheck` never returns Unhealthy (verified by grep + 3 tests). Degraded→200 HTTP mapping enforced. |
| HLTH-04 | Plans 03, 05 | Leader-lock probe surfaces holder InstanceId + TTL | SATISFIED | Leader description: `"leader: {InstanceId}, ttl: {N}s"`. Follower description: `"not leader; holder: {HolderInstanceId}, ttl: {N}s"` or `"not leader; lock currently unheld"`. Tests assert description contains InstanceId and "ttl". |
| HLTH-05 | Plans 01, 05 | No infra details in health payloads | SATISFIED | `GameKitHealthResponseWriter` whitelist writer (Exception/Data/Tags omitted). All checks use bare catch with constant descriptions. `HealthLeakTests` (3 tests) assert no `Host=`, `Password=`, `Port=`, `Username=`, hostname fragments on both healthy and failure paths. |
| HLTH-06 | Plans 04, 05 | Admin.UI delegates to Core `HealthCheckService`, no duplicate probe logic | SATISFIED | `HealthProbeService` refactored: no `NpgsqlConnection`/`PingAsync`/`GameKitOptions` in ctor or body. `GetTile` projects Core entries. `ProbeErrorRateAsync` preserved unchanged (D-16). `HealthProbeServiceDelegationTests` structural gate green. |

---

### Anti-Patterns Found

No anti-patterns found in any health-phase files. Scan of all `src/**/Health/*.cs`, `src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs`, `src/GameKit.Admin.UI/Services/HealthProbeService.cs`, and the Matchmaking lease extension returned:

- Zero `TBD`, `FIXME`, `XXX`, `TODO`, `HACK`, `PLACEHOLDER` debt markers
- Zero `return null`, `return {}`, `return []` stub patterns in health code paths
- Zero `ex.Message`/`ex.ToString()`/`ex.GetType().Name` in health check descriptions (only in XML doc comments explaining what is forbidden)
- Zero hardcoded empty data collections passed to renderers
- Zero `Unhealthy` in `MatchmakingLeaderHealthCheck` code paths (only in comments)

**StackExchange.Redis PackageReference widening (flagged for awareness):** `GameKit.Core.csproj` gained a `PackageReference Include="StackExchange.Redis" />` (version resolved from the pre-existing central pin at 2.8.41 in `Directory.Packages.props`). This widens Core's public dependency surface so `IConnectionMultiplexer` is resolvable at compile time for the conditional Redis check guard. This is intentional per the locked design (D-09) — the check is only registered when a multiplexer is present in DI, so consumers who don't install any Redis-using package pay no runtime cost. No new central pin was added. Classification: INFO (intended architectural consequence, not a defect).

---

### Human Verification Required

None. All success criteria are verifiable programmatically and confirmed via integration tests. All 13/13 new health tests are green per orchestrator evidence. No visual UI, real-time behavior, or external service integration checks are required beyond what the integration tests exercise.

---

### Recommended Follow-Up (Out of Scope for This Phase)

**Pre-existing test debt — `MigrationDeterminismTests.Migrate_Twice_Is_Idempotent`:** This test in `GameKit.Core.Integration.Tests` asserts `Assert.Single(applied)` but Core has accumulated 4 migrations across phases 1/11/13/prior. It fails identically on the pre-phase-14 commit `c84c39c`. Phase 14 added zero migrations and did not touch this file (last modified in `test(01-07)`). This is stale Phase-1 test debt that should be corrected to `Assert.True(applied.Count >= 1)` or removed — but is explicitly out of scope for this verification.

---

## Gaps Summary

None. All 14 must-have truths are VERIFIED. All 6 requirement IDs (HLTH-01 through HLTH-06) are fully satisfied. The phase goal — "Every GameKit deployment exposes separate `/health/live` and `/health/ready` endpoints with correct K8s-probe semantics; liveness never fails on a Redis blip; readiness gates on migrations + Postgres; Admin.UI delegates to Core probes" — is achieved in the codebase.

---

_Verified: 2026-06-14_
_Verifier: Claude (gsd-verifier)_
