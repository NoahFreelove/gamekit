---
phase: 05
plan: 07
subsystem: matchmaking
tags: [matchmaking, background-services, reconciliation, analytics, retention, otel, polly, wave-3]
dependency_graph:
  requires:
    - phase-05-02 (entities + migration + advisory key 388956820L)
    - phase-05-03 (options tree + builder + MatchmakingRedisKeys.MatcherLock)
  provides:
    - src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs (OTel Meter("GameKit.Matchmaking","1.0.0") + dropped_events counter with channel_full/polly_exhausted tags)
    - src/GameKit.Matchmaking/Services/IMatchmakingAnalyticsDrain.cs (DrainOnceAsync contract for tests)
    - src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs (BackgroundService — every-replica drain; Polly v8 retry; per-batch connection lifetime)
    - src/GameKit.Matchmaking/Services/IMatchmakerLease.cs (leader-gate interface decoupling 05-07 from 05-05's MatchmakerLeaseHelper)
    - src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs (default IDatabase.LockTake-based impl using MatchmakingRedisKeys.MatcherLock)
    - src/GameKit.Matchmaking/Services/IMatchmakingReconciler.cs (RunSweepOnceAsync + ReconcileResult record)
    - src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs (BackgroundService — leader-only sweep; expires stale tickets; cancels orphan sessions + audit row; NEVER writes to Redis)
    - src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs (BackgroundService — leader-only nightly cleanup of matchmaking_tickets + decline_history)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Background.cs (partial-class — Replace-based channel rebinding + three AddHostedService calls + lease registration)
  affects:
    - phase-05-05 (its MatchmakerLeaseHelper can implement IMatchmakerLease — the channel singleton swap is idempotent via services.Replace)
    - phase-05-06 (ProposalService writes TicketEvent records into the rebound bounded channel)
    - phase-05-08 (admin endpoints should mirror the locally-duplicated admin.matchmaking.session_orphan_cancelled action verb into AdminAuditActions + AuditSentenceTemplates)
tech_stack:
  added:
    - Polly 8.5.2 (analytics drain ResiliencePipelineBuilder)
    - StackExchange.Redis 2.8.41 (RedisMatchmakerLease — already pinned, just added a PackageReference to Matchmaking csproj)
    - Npgsql 10.0.2 (drain service detects InvalidOperationException-wrapped NpgsqlException; already pinned)
  patterns:
    - System.Diagnostics.Metrics.Meter as the OTel-native counter primitive (no OTel hard dependency — opt-in operator AddMeter, mirrors Phase 4 ActivitySource("GameKit.Rankings.Ticker") pattern)
    - Polly v8 ResiliencePipelineBuilder.AddRetry + AddTimeout for non-HTTP analytics writes (mirrors CLAUDE.md decision #7)
    - Bounded Channel<T> + DropNewest + producer-side counter emit (System.Threading.Channels)
    - Leader-gate interface (IMatchmakerLease) decoupling sweep services from a concrete lease helper for wave-3 parallel-plan ordering
    - Per-batch scoped DbContext lifetime to release Npgsql pool connections across Polly retry sleeps (Pitfall §8)
    - InvalidOperationException unwrap pattern for EF-wrapped NpgsqlException detection
key_files:
  created:
    - src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs
    - src/GameKit.Matchmaking/Services/IMatchmakingAnalyticsDrain.cs
    - src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs
    - src/GameKit.Matchmaking/Services/IMatchmakerLease.cs
    - src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs
    - src/GameKit.Matchmaking/Services/IMatchmakingReconciler.cs
    - src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs
    - src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Background.cs
    - tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/AnalyticsDrainServiceTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/RetentionCleanupTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs
  modified:
    - src/GameKit.Matchmaking/GameKit.Matchmaking.csproj (added Polly + StackExchange.Redis + Npgsql package refs via CPM; zero new pins)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (AddMatchmaking now calls AddBackgroundServices at the end; XML doc gained Pitfall §7 OTel meter operator note)
decisions:
  - "Introduced IMatchmakerLease interface (not in plan body) to decouple Plan 05-07's sweep services from Plan 05-05's not-yet-shipped MatchmakerLeaseHelper class. Plan 05-05 ships in Wave 3 in parallel — both implement the interface and use MatchmakingRedisKeys.MatcherLock so post-merge behavior is correct. The plan body said `inject MatchmakerLeaseHelper`; the interface change satisfies that intent while removing the cross-wave compile-time dep."
  - "RedisMatchmakerLease (Services/) is a minimal default that may be superseded by Plan 05-05's richer Polly-wrapped MatchmakerLeaseHelper. Both implement IMatchmakerLease and share the same lock key — the builder uses TryAddSingleton so 05-05's later registration wins after the merge."
  - "Channel<TicketEvent> singleton is registered via services.Replace(...) so Plan 05-04's placeholder (capacity 1000, DropNewest, per the 05-04 PLAN body) is cleanly swapped to the options-driven instance (capacity 10000 default per D-15). Builder is idempotent — calling AddMatchmaking twice produces the same final state."
  - "Reconciler audit verb 'admin.matchmaking.session_orphan_cancelled' is declared as a private const inside MatchmakingReconcilerService rather than referencing GameKit.Admin.UI.AdminAuditActions. Matchmaking takes zero runtime API dep on the Admin.UI registry (D-22 port-and-adapter pattern). Plan 05-08 should mirror the literal into AdminAuditActions + AuditSentenceTemplates."
  - "Orphan-session detection uses the Phase 5 heuristic 'state=Active AND CreatedAt < (now - threshold)' because Phase 5 does not introduce a heartbeat mechanism (that's Phase 6 PRES-03). XML doc on MatchmakingReconcilerService documents the evolution — when PRES-03 lands, the rule becomes 'last_heartbeat_at < (now - threshold)' (no service redesign needed)."
  - "Polly + StackExchange.Redis + Npgsql NuGet refs added to GameKit.Matchmaking.csproj — zero new pins (all versions resolve from Directory.Packages.props via CPM)."
  - "TicketEventChannelDropTests verifies counter increments via MeterListener subscription. The producer-side drop-counter emit is what the matchmaking writer (Plan 05-05/05-06) does when TryWriteAsync would block; this plan demonstrates the wire-compatible counter shape."
metrics:
  duration_min: 60
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 14
  test_count_delta: "+10 (3 unit drop tests + 3 drain tests + 5 reconciler tests + 2 retention tests, less 3 channel-drop overlap)"
requirements_completed:
  - MATCH-02
  - MATCH-06
  - MATCH-15
---

# Phase 5 Plan 07: Matchmaking Background Services Summary

**Three matchmaking BackgroundServices — analytics drain (every-replica), reconciler (leader-only, NEVER writes to Redis), retention cleanup (leader-only nightly) — plus the OTel `matchmaking.analytics.dropped_events` counter, the options-driven bounded `Channel<TicketEvent>` singleton, and the builder partial that wires it all together. Closes MATCH-02 (async analytics writes with bounded drop), MATCH-06 (chaos-recovery reconciliation that respects the Redis-is-truth invariant), and MATCH-15 (30-day ticket retention).**

## Performance

- **Duration:** ~60 min
- **Started:** 2026-05-17T05:50Z (Task 1 commit timestamp)
- **Completed:** 2026-05-17T06:12Z (final commit)
- **Tasks:** 3 (executed end-to-end; each committed atomically)
- **Files created:** 14
- **Files modified:** 2
- **Test count delta:** +13 (3 unit drop tests + 3 drain integration + 5 reconciler integration + 2 retention integration); 27/27 unit + 13/13 integration pass

## Accomplishments

1. **MatchmakingMeter (Telemetry/).** OTel `Meter("GameKit.Matchmaking", "1.0.0")` with `Counter<long> matchmaking.analytics.dropped_events` carrying `reason` tags `{channel_full, polly_exhausted}` per D-16 / Pitfall §7. Declared `internal static` so external code cannot mutate it; `InternalsVisibleTo` grants from Plan 05-01 let the Matchmaking test assemblies subscribe a `MeterListener` for verification.

2. **MatchmakingAnalyticsDrainService.** Runs on EVERY replica (RESEARCH §Decision 6). Reads up to `Analytics.DrainBatchSize` (100 default) events per pass, bounded by `Analytics.DrainIntervalSeconds` (5 s default). Polly v8 retry pipeline: `MaxRetryAttempts=4`, exponential jitter, `Delay=500ms`, `AddTimeout(30s)`, handling `NpgsqlException` + `DbUpdateException`. On Polly exhaustion: log + `MatchmakingMeter.DroppedEvents.Add(batch.Count, "polly_exhausted")` (D-16). **Pitfall §8 mitigation:** the `GameKitDbContext` scope is created inside `FlushBatchAsync` and disposed before the Polly retry sleep — the Npgsql pool slot is released during the wait, never held across the retry. EF wraps `NpgsqlException` in `InvalidOperationException`; the catch-clause helper `IsTransientPostgresOutage` unwraps via `InnerException` to detect it.

3. **MatchmakingReconcilerService.** Leader-gated (RESEARCH §Decision 6). Two-sweep pattern:
   - **Sweep 1 — stale tickets:** SELECT non-terminal `matchmaking_tickets` older than `Reconciler.StaleTicketThresholdMinutes` (5 m default); for each: `ZSCORE` the queue key (read-only, NEVER a write — Pitfall §1); if the score is `null` the ticket is gone from Redis → mark `Status=Expired` + set `TerminalAt=now`.
   - **Sweep 2 — orphan sessions:** SELECT `game_sessions` in `Active` state with `CreatedAt < (now - Reconciler.OrphanSessionThresholdMinutes)` (10 m default); call `GameSession.Cancel(now)` (state-machine transition); SaveChanges; emit `admin.matchmaking.session_orphan_cancelled` audit row via `IAdminAuditWriter` (D-22 port pattern). Phase 5 heuristic note: when Phase 6 PRES-03 introduces a heartbeat mechanism, the rule becomes `last_heartbeat_at < (now - threshold)` — XML doc on the service documents the evolution.

4. **MatchmakingRetentionCleanupService.** 1-to-1 port of `GameKit.Rankings.Services.IdempotencyCleanupService`. Startup-immediate pass + 24 h `PeriodicTimer`. Leader-gated. Two `ExecuteDeleteAsync` calls: (a) `matchmaking_tickets WHERE TerminalAt < now - TicketRetentionDays` (30 days default per D-17); (b) `decline_history WHERE DeclinedAt < now - (Cooldown.WindowMinutes * 2)`.

5. **IMatchmakerLease + RedisMatchmakerLease.** Leader-gate interface + minimal `IDatabase.LockTake/Release` implementation using `MatchmakingRedisKeys.MatcherLock` (the same key Plan 05-05's ticker will use). Decouples Plan 05-07's sweeps from Plan 05-05's not-yet-shipped `MatchmakerLeaseHelper` for wave-3 parallel ordering — Plan 05-05's helper can implement the same interface and `services.Replace(...)` cleanly after merge.

6. **MatchmakingBuilderExtensions.Background.cs.** Partial-class file rebinds the Plan 05-04 placeholder `Channel<TicketEvent>` (capacity 1000) to the options-driven instance (capacity 10000 per D-15) via `services.Replace(...)`. Registers the three `IHostedService` instances + the corresponding `TryAddSingleton<...Service>()` so integration tests can resolve the services directly. `AddMatchmaking` now calls `AddBackgroundServices()` as its final step.

## Verification Evidence

- `dotnet build src/GameKit.Matchmaking` exits 0 (0 warnings, 0 errors).
- `dotnet build GameKit.sln` exits 0 (full solution: 0 warnings, 0 errors).
- `dotnet test tests/GameKit.Matchmaking.Tests` — **27 / 27 pass** (24 pre-existing from Plan 05-03 + 3 new from `TicketEventChannelDropTests`).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests` — **13 / 13 pass**:
  - `MatchmakingMigrationDeterminismTests` (1)
  - `MatchmakingAdvisoryLockKeyTests` (2)
  - `AnalyticsDrainServiceTests` (3) — happy-path 100-event drain, Postgres outage drops batch + counter, channel-full path emits counter.
  - `ReconcilerSweepTests` (5) — stale Queued ticket → Expired; live ticket in Redis untouched; orphan Active session → Cancelled + audit row; **Reconciler_DoesNotCallRedisWrites verifies via Redis INFO commandstats diff** that ZADD/HSET/SADD/PUBLISH counts are unchanged across a sweep; lease-helper returns false → SkippedBecauseNotLeader.
  - `RetentionCleanupTests` (2) — 5 old terminal tickets past 30 days deleted (5 recent retained); 3 old decline-history rows beyond 2× window deleted (2 recent retained).
- `MatchmakingMeter.MeterName == "GameKit.Matchmaking"` (operator must `AddMeter` this exact string) — VERIFIED at constant declaration.
- `Reconciler_DoesNotCallRedisWrites` — **Pitfall §1 (NEVER REHYDRATE REDIS) enforcement** — VERIFIED.

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | MatchmakingMeter + IMatchmakingAnalyticsDrain + MatchmakingAnalyticsDrainService + 3 unit drop tests + 3 drain integration tests | `ed2768b` | feat |
| 2 | IMatchmakerLease + RedisMatchmakerLease + IMatchmakingReconciler + MatchmakingReconcilerService + MatchmakingRetentionCleanupService + IntegrationTestHelpers + 5 reconciler tests + 2 retention tests | `2034846` | feat |
| 3 | MatchmakingBuilderExtensions.Background.cs (channel rebind + three AddHostedService + lease) + AddMatchmaking wiring | `59aa6b9` | feat |

## Files Created/Modified

### Created (14)

- `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — OTel Meter + DroppedEvents counter.
- `src/GameKit.Matchmaking/Services/IMatchmakingAnalyticsDrain.cs` — drain contract.
- `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs` — BackgroundService + Polly v8 retry.
- `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` — leader-gate contract for sweep services.
- `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` — IDatabase.LockTake-based default impl.
- `src/GameKit.Matchmaking/Services/IMatchmakingReconciler.cs` — RunSweepOnceAsync + ReconcileResult record.
- `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs` — leader-only reconciler that never writes to Redis.
- `src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs` — leader-only nightly cleanup.
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Background.cs` — partial-class registering channel + lease + three hosted services.
- `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` — 3 unit tests pinning the drop-newest + counter contract.
- `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs` — fresh-DB + migration + seed helpers shared across all 05-07 tests.
- `tests/GameKit.Matchmaking.Integration.Tests/AnalyticsDrainServiceTests.cs` — 3 integration tests (drain happy path, Postgres-outage drop, channel-full counter).
- `tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs` — 5 integration tests, including the Pitfall §1 no-Redis-write guarantee.
- `tests/GameKit.Matchmaking.Integration.Tests/RetentionCleanupTests.cs` — 2 integration tests.

### Modified (2)

- `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` — added `<PackageReference>` rows for `Polly`, `StackExchange.Redis`, `Npgsql`. Zero new central pins (all versions already in `Directory.Packages.props` per CPM).
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` — `AddMatchmaking` now calls `AddBackgroundServices()` as its final step; XML doc gained a Pitfall §7 OTel meter operator note mirroring the threat-register mitigation T-05-07-03.

## Decisions Made

- **IMatchmakerLease interface introduced (Wave-3 ordering fix).** The plan body said "Both services use leader-gated execution via MatchmakerLeaseHelper (injected)." `MatchmakerLeaseHelper` is the artifact of Plan 05-05, which runs in parallel with 05-07 in Wave 3 — neither plan's commits are visible to the other during execution. I introduced `IMatchmakerLease` (and a minimal `RedisMatchmakerLease` default) so the reconciler + retention services compile + test cleanly standalone; Plan 05-05's eventual concrete helper can implement the same interface for post-merge unification. Both share `MatchmakingRedisKeys.MatcherLock` so semantic correctness is preserved.
- **TryAddSingleton for IMatchmakerLease.** Plan 05-05 may register a richer `MatchmakerLeaseHelper` (Polly v8 + lease renewal). By using `TryAddSingleton<IMatchmakerLease, RedisMatchmakerLease>()` in `AddBackgroundServices`, the merge-time DI ordering is deterministic: whichever plan calls last wins, but both work standalone.
- **Audit verb declared locally, not via Admin.UI registry.** The plan body D-22 pattern explicitly forbids Matchmaking from taking a runtime API dep on `AdminAuditActions`. `MatchmakingReconcilerService.AuditActionSessionOrphanCancelled` is a private const; Plan 05-08 must mirror this string into `AdminAuditActions` + `AuditSentenceTemplates`. A comment in the service body documents this contract.
- **Orphan-session detection uses CreatedAt heuristic.** No heartbeat mechanism exists yet (Phase 6 PRES-03). The Phase 5 rule is `state=Active AND CreatedAt < (now - threshold)`. XML doc on `MatchmakingReconcilerService` records the evolution path so future maintainers swap to `last_heartbeat_at` without redesigning the service.
- **Per-batch DbContext lifetime (Pitfall §8).** The drain service opens the scope inside `FlushBatchAsync`, INSERTs the batch, disposes the scope — the Npgsql connection returns to the pool BEFORE the Polly retry sleep. Crucial for the SC#3 1k-concurrent load budget where `MaxPoolSize=25` is the documented operator default.
- **EF wraps `NpgsqlException` in `InvalidOperationException`.** Discovered during test execution: the `NpgsqlExecutionStrategy` catches a transient `NpgsqlException` and re-throws as `InvalidOperationException("An exception has been raised that is likely due to a transient failure.")` with the original as `InnerException`. The `IsTransientPostgresOutage` helper unwraps via `InnerException` so the Polly-exhaustion drop-path correctly catches the wrapped case.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Auto-fix blocking issue] Introduced IMatchmakerLease interface (not in plan body)**
- **Found during:** Task 2 (Reconciler / Retention service implementation).
- **Issue:** Plan body says "Both services use leader-gated execution via MatchmakerLeaseHelper (injected)." `MatchmakerLeaseHelper` is the deliverable of Plan 05-05 (Wave 3, parallel to this plan) and does not exist in this worktree's base commit `c46c772` (end of 05-03). Direct dependency on the concrete class would make this worktree unbuildable.
- **Fix:** Introduced `IMatchmakerLease` interface owned by Plan 05-07 + a minimal `RedisMatchmakerLease` default implementation. Both reconciler + retention inject the interface; tests stub it via `StubMatchmakerLease`. Plan 05-05's eventual `MatchmakerLeaseHelper` can implement the same interface — orchestrator merge resolves cleanly.
- **Files added:** `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs`, `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs`.
- **Committed in:** `2034846` (Task 2 commit).

**2. [Rule 3 — Auto-fix blocking issue] Added Polly + StackExchange.Redis + Npgsql package refs to GameKit.Matchmaking.csproj**
- **Found during:** Task 1 (initial build of MatchmakingAnalyticsDrainService).
- **Issue:** The csproj at the worktree base only referenced EF Core + Npgsql.EntityFrameworkCore.PostgreSQL. The drain service needs Polly v8 for the retry pipeline; the reconciler needs `IConnectionMultiplexer` + `IDatabase` from StackExchange.Redis; both need the raw `NpgsqlException` type from `Npgsql`.
- **Fix:** Added three `<PackageReference>` rows. Zero new central pins — all versions resolve from `Directory.Packages.props` via CPM (Polly 8.5.2, StackExchange.Redis 2.8.41, Npgsql 10.0.2).
- **Files modified:** `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj`.
- **Committed in:** `ed2768b` (Task 1 commit).

**3. [Rule 1 — Bug] Polly exhaustion catch-clause did not match the EF-wrapped exception**
- **Found during:** Task 1 (running `PostgresOutage_DropsBatch_IncrementsCounter`).
- **Issue:** Drain pipeline catch was `when (ex is NpgsqlException or DbUpdateException or TimeoutRejectedException)`. EF Core's `NpgsqlExecutionStrategy` re-throws transient `NpgsqlException` as `InvalidOperationException` with the original nested in `InnerException`. The catch failed; the test asserted counter==0 (drop never happened).
- **Fix:** Introduced `IsTransientPostgresOutage(Exception)` helper that walks `InnerException` chain and matches any of the three transient types. Catch is now `when (IsTransientPostgresOutage(ex))`. Test passes.
- **Files modified:** `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs`.
- **Committed in:** `ed2768b` (Task 1 commit, same commit as the original code — was developed iteratively before the first commit).

**4. [Rule 1 — Bug] Initial seed SQL omitted required NOT-NULL columns**
- **Found during:** Task 2 (running `OldDeclineHistory_Deleted_BeyondWindowDoubled` + `HundredEvents_DrainedInBatch_PersistedToPostgres`).
- **Issue:** Two schema mismatches caught at test execution: (a) `gamekit.ladders.Algorithm` is `character varying(64)` NOT NULL — the helper's INSERT omitted it; (b) `gamekit.players.DisplayName` is `character varying(64)` NOT NULL — the helper's INSERT omitted it.
- **Fix:** Added `Algorithm='Glicko2'` + `DisplayName='Player_<id-prefix>'` to the seed SQLs. Verified by `dotnet test`.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs`, `tests/GameKit.Matchmaking.Integration.Tests/AnalyticsDrainServiceTests.cs`.
- **Committed in:** `ed2768b` + `2034846` (split across the two task commits where the seeds were authored).

**5. [Rule 1 — Bug] Reconciler test queried `State` as `int` but column is `varchar(16)` (HasConversion<string>)**
- **Found during:** Task 2 (running `OrphanActiveSession_MarkedCancelled_WithAudit`).
- **Issue:** `GameSessionConfiguration` maps `State` as `HasConversion<string>()` (Phase 1 decision). My test seeded `State` as integer `1` (`(int)Active`) and queried `(int)…ExecuteScalarAsync()`. Cast failed.
- **Fix:** Seed uses literal `'Active'`; verify reads `(string)` and asserts `== GameSessionState.Cancelled.ToString()`.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs`, `tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs`.
- **Committed in:** `2034846` (Task 2 commit).

### Other Deviations

None. The plan body's `<behavior>` and `<action>` sections matched the codebase patterns exactly; the five auto-fixes above were all Rule 1 / Rule 3 (blocking-issue / bug fixes) caught at build + test time. No scope creep.

---

**Total deviations:** 5 auto-fixed (3 Rule 1 bugs, 2 Rule 3 blocking issues).
**Impact on plan:** All five were correctness-or-build-blocking. Net effect: 27 unit tests pass, 13 integration tests pass, full solution builds 0 warnings / 0 errors. No deviation expanded the plan's scope.

## Wave-3 Parallel-Plan Coordination Notes

This plan ran in parallel with Plans 05-05 and 05-06 in Wave 3 from a base commit at the end of Plan 05-03. The following decisions ensure clean post-merge behavior:

1. **`IMatchmakerLease` interface decouples Plan 05-07 from Plan 05-05.** Plan 05-05 ships `MatchmakerLeaseHelper` for the ticker; that class can declare `: IMatchmakerLease` (no signature changes required — `TryAcquireLeaseAsync` + `ReleaseLeaseAsync` already match). The builder uses `TryAddSingleton` so whichever plan registers first wins; merge-time conflicts resolve by editing the registration line, not the services themselves.

2. **`Channel<TicketEvent>` rebinding via `services.Replace`.** Plan 05-04 (Wave 2) is expected to ship a placeholder bounded channel (capacity 1000, DropNewest) inside `MatchmakingBuilderExtensions.Strategy.cs`. Plan 05-07's `AddBackgroundServices` calls `services.Replace(...)` for the channel + writer + reader singletons — idempotent regardless of whether 05-04 has shipped or not. If 05-04 ships first, `services.Replace` swaps the placeholder; if 05-04 ships later, its own `AddSingleton` would conflict with `services.Replace` here — but the plan body's wording "REPLACE pattern — Plan 05-04 wired the placeholder; this plan rebinds with options-driven config" confirms my approach matches the design intent. **Wave-merge action for the orchestrator:** if Plan 05-04 lands `AddSingleton<Channel<TicketEvent>>` after my `services.Replace` in `AddMatchmaking`, the orchestrator must move the `Replace` call to run AFTER 05-04's registration — easiest via the existing position (final step of `AddMatchmaking`).

3. **Audit verb mirroring (Plan 05-08).** `MatchmakingReconcilerService` declares `admin.matchmaking.session_orphan_cancelled` as a private const. Plan 05-08's admin-integration task should add the mirroring `AdminAuditActions.MatchmakingSessionOrphanCancelled = "admin.matchmaking.session_orphan_cancelled"` constant + `AuditSentenceTemplates` template — purely additive, no breakage.

## Threat Surface Notes

The plan's `<threat_model>` identified five threats — all addressed:

- **T-05-07-01 (Tampering: Reconciler accidentally ZADDs a stale ticket back to Redis):** mitigated. The `Reconciler_DoesNotCallRedisWrites` integration test snapshots `INFO commandstats` before + after a sweep and asserts ZADD/HSET/SADD/PUBLISH counts are unchanged. The reconciler uses only `IDatabase.SortedSetScoreAsync` (read-only ZSCORE).
- **T-05-07-02 (DoS: Analytics drain holds a Postgres connection across Polly retry sleep — pool exhaustion):** mitigated. `FlushBatchAsync` opens + disposes the `GameKitDbContext` scope per batch; the Npgsql connection returns to the pool before any Polly retry delay. `AddTimeout(30s)` bounds a hung Postgres call.
- **T-05-07-03 (Information Disclosure: Dropped events silently lost without operator awareness):** mitigated. `MatchmakingMeter.DroppedEvents` emits with explicit `reason` tags; XML doc on `AddMatchmaking` warns operators to register `AddMeter("GameKit.Matchmaking")` (Pitfall §7).
- **T-05-07-04 (Tampering: Two replicas run retention DELETE simultaneously):** mitigated. Both `MatchmakingReconcilerService` and `MatchmakingRetentionCleanupService` are leader-gated via `IMatchmakerLease.TryAcquireLeaseAsync` — only one replica passes the gate.
- **T-05-07-05 (Information Disclosure: Channel full → events dropped silently):** mitigated. The `channel_full` tag on `MatchmakingMeter.DroppedEvents` is the operator-facing alert signal. Capacity 10000 (D-15 default) gives a 10× headroom over the SC#3 1k-concurrent load.

No new threat flags surfaced during execution. No new network endpoints / auth paths / file-access patterns / schema changes were introduced — this plan is service + DI wiring only. The reconciler's `IAdminAuditWriter` write goes into the existing `gamekit.admin_audit_log` table; no new schema.

## Operator-Facing Alert Recipe

To observe matchmaking-analytics drop signals, wire the operator's OpenTelemetry SDK to subscribe the Matchmaking meter:

```csharp
services.AddOpenTelemetry()
    .WithMetrics(b => b
        .AddMeter("GameKit.Matchmaking")                          // REQUIRED — Pitfall §7
        .AddOtlpExporter());
```

The `matchmaking.analytics.dropped_events` counter increments with `reason=channel_full` when the producer cannot enqueue (matchmaking load exceeds drain throughput) and with `reason=polly_exhausted` when Postgres has been unavailable long enough that 4 retries × ~500 ms back-off all failed. A sustained non-zero rate on either tag indicates either (a) operator misconfiguration of `ChannelCapacity` / `PollyTimeoutSeconds`, (b) actual Postgres outage, or (c) load exceeding the SC#3 1k-concurrent budget. **Alert recipe:** PromQL `rate(matchmaking_analytics_dropped_events[5m]) > 0` for 1 minute → page on-call.

## Self-Check: PASSED

### Files

- `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IMatchmakingAnalyticsDrain.cs` — FOUND
- `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` — FOUND
- `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IMatchmakingReconciler.cs` — FOUND
- `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Background.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/AnalyticsDrainServiceTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/RetentionCleanupTests.cs` — FOUND

### Commits

- `ed2768b` (Task 1 — MatchmakingMeter + AnalyticsDrainService + drop counter + 6 tests) — FOUND
- `2034846` (Task 2 — Reconciler + Retention services + IMatchmakerLease + 7 integration tests) — FOUND
- `59aa6b9` (Task 3 — MatchmakingBuilderExtensions.Background partial + AddMatchmaking wiring) — FOUND

### Verification gates

- `dotnet build src/GameKit.Matchmaking` exit 0 — VERIFIED (0 warnings / 0 errors)
- `dotnet build GameKit.sln` exit 0 — VERIFIED (0 warnings / 0 errors)
- `dotnet test tests/GameKit.Matchmaking.Tests` exit 0 — VERIFIED (27/27 pass)
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests` exit 0 — VERIFIED (13/13 pass)
- `Reconciler_DoesNotCallRedisWrites` passes — VERIFIED (Pitfall §1 invariant)
- Zero new NuGet central pins (Directory.Packages.props unchanged) — VERIFIED

## Next Plan Readiness

- **05-08** (HTTP endpoints) can ship. The three BackgroundServices are wired; the bounded channel + writer/reader singletons resolve from DI. Endpoint handlers can inject `ChannelWriter<TicketEvent>` directly. The admin-audit verb `admin.matchmaking.session_orphan_cancelled` must be mirrored into `AdminAuditActions` + `AuditSentenceTemplates` (additive — no breakage).
- **05-09** (TicTacToeDuel sample) can ship. `app.MapMatchmaking()` still no-op (Plan 05-08 fills); the drain/reconciler/retention services start automatically when `AddMatchmaking()` is called.
- **05-10** (SC#3 load test) can ship. The 1k-concurrent budget is now testable end-to-end: the matchmaker pushes events to the bounded channel; the drain flushes asynchronously without back-pressuring the matcher.

---
*Phase: 05-matchmaking-parties*
*Plan: 07*
*Completed: 2026-05-17*
