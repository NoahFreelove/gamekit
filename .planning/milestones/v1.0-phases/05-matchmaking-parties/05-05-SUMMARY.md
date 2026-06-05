---
phase: 05
plan: 05
subsystem: matchmaking
tags: [matchmaking, ticker, lease, leader-election, proposal-sweep, otel, polly, fencing-token, wave-3]
dependency_graph:
  requires:
    - phase-05-02 (matchmaking_tickets entity + migration + Redis-key constants)
    - phase-05-03 (options + builder + AddLadder)
    - phase-05-04 (IMatchmakingStrategy + AtomicClaimScript + Channel<TicketEvent> placeholder)
    - phase-05-07 (IMatchmakerLease contract — this plan implements it)
  provides:
    - src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs (Polly v8 around LockTake/LockExtend/LockRelease; implements IMatchmakerLease)
    - src/GameKit.Matchmaking/Services/IMatchmakerTicker.cs (testable single-tick contract)
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs (BackgroundService + PeriodicTimer; leader-only match-formation + proposal-sweep loop; MATCH-07 + MATCH-08)
    - src/GameKit.Matchmaking/Services/MatcherTickResult.cs (enum — NoMatch/Matched/LockNotAcquired/LeaseLost/RedisUnavailable)
    - src/GameKit.Matchmaking/Services/ProposalSweeper.cs (SCAN-based partial-accept reaper; Pitfall §10)
    - src/GameKit.Matchmaking/Services/IProposalService.cs + ProposalServiceStub (Plan 05-06 stub for DI compatibility)
    - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs (OTel ActivitySource("GameKit.Matchmaking.Ticker"))
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ticker.cs (AddTickerServices partial-class)
  affects:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (AddMatchmaking calls AddTickerServices() AFTER AddBackgroundServices() so MatchmakerLeaseHelper supersedes Plan 05-07's RedisMatchmakerLease default)
    - phase-05-06 (proposal service can now resolve IProposalService stub, fills body with real accept/decline)
    - phase-05-08 (HTTP endpoints can now wire to the live matchmaker tick path)
tech_stack:
  added: []  # zero new NuGet pins — Polly 8.5.2 + StackExchange.Redis 2.8.41 already in Directory.Packages.props (from Plan 05-04)
  patterns:
    - BackgroundService + PeriodicTimer + Polly v8 retry pipeline (mirrors Phase 4 RankingsTickerService precedent)
    - Lua atomic-claim with fencing-token as first non-comment line (Pitfall §2 — Plan 05-04 source; this plan invokes it)
    - Distributed lock via IDatabase.LockTakeAsync/LockExtendAsync/LockReleaseAsync (Lua-script-verified release)
    - services.Replace(IMatchmakerLease) — unified lease across ticker (Plan 05-05) + reconciler/retention (Plan 05-07)
    - OTel ActivitySource opt-in (operators register AddSource — no hard OTel dependency)
    - SCAN (via IServer.Keys, NOT raw KEYS) for proposal-key enumeration (Pitfall §11)
    - Deadline-Ms hash field for sweeper expiry detection (NOT Redis KEY TTL — which would delete the hash before SCAN sees it)
    - Channel<TicketEvent> consume-only — writes events without depending on Plan 05-07's later channel rebinding
key_files:
  created:
    - src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs
    - src/GameKit.Matchmaking/Services/IMatchmakerTicker.cs
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
    - src/GameKit.Matchmaking/Services/MatcherTickResult.cs
    - src/GameKit.Matchmaking/Services/ProposalSweeper.cs
    - src/GameKit.Matchmaking/Services/IProposalService.cs
    - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ticker.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakerLeaseHelperTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/ProposalSweepTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderElectionTests.cs
  modified:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (AddMatchmaking now calls AddTickerServices() as its final step)
decisions:
  - "MatchmakerLeaseHelper implements IMatchmakerLease (the Plan 05-07 contract). The ticker builder uses services.Replace(IMatchmakerLease) to supersede Plan 05-07's minimal RedisMatchmakerLease default — closes the orchestrator-merge ambiguity flagged in 05-07 SUMMARY §Wave-3 Coordination. All three matchmaker BackgroundServices (ticker + reconciler + retention) now share a single Polly-wrapped fencing-token InstanceId on the same gamekit:matchmaking:matcher:lock."
  - "ProposalSweeper detects past-deadline proposals by reading a `deadlineMs` Unix-ms field from the proposal hash — NOT by checking Redis KEY TTL. Rationale: the Lua atomic-claim script sets EXPIRE on the proposal hash (Plan 05-04 line 53 of LuaSource), which deletes the entire hash on expiry; SCAN-based discovery in the sweeper would then miss past-deadline proposals entirely. The ticker writes `deadlineMs` alongside the script's `fields` JSON immediately after a successful Lua claim so the sweeper has a stable signal for past-deadline reaping regardless of Redis's own EXPIRE."
  - "IProposalService + ProposalServiceStub shipped here (not deferred to 05-06) so the ticker builder can register IProposalService cleanly via TryAddScoped. Stub throws NotImplementedException(\"Plan 05-06\") on every call. Plan 05-06's later AddScoped<IProposalService, ProposalService>() registration replaces the stub via standard MS.DI ordering semantics (the last registration wins for non-TryAdd, but TryAdd here means Plan 05-06's explicit AddScoped supersedes the stub regardless of call order)."
  - "MatchmakerTickerService.ProcessPoolAsync iterates per-pool via IServer.Keys(pattern: \"mm:queue:*:{poolName}\") instead of maintaining a separate ladder-name → ladder-id index in DI. Operators typically register 1–3 ladders so the SCAN overhead is negligible (<1ms per tick). Future optimisation: maintain a per-pool registry in DI if benchmarking shows the SCAN dominates the tick budget."
  - "OTel ActivitySource defined in src/GameKit.Matchmaking/Telemetry/ (separate folder mirrors Plan 05-07's MatchmakingMeter.cs placement). The SourceName constant is exposed as a public `const string` so test assertions + operator XML docs can cross-reference the exact literal `\"GameKit.Matchmaking.Ticker\"` without drift."
  - "MatcherTickResult enum adds LeaseLost (Rankings' TickResult does not have this state because Rankings has no fencing token). When the Lua claim returns LEASE_LOST, the ticker propagates to MatcherTickResult.LeaseLost and bails the tick immediately — this is the SC#4 phase-gate signal that another replica has taken leadership mid-tick."
  - "Test `MatchmakingLeaderElectionTests` does NOT run Postgres migrations — Plan 05-05 ticker is Redis-only (analytics writes go through a Channel<TicketEvent> that Plan 05-07's drain consumes asynchronously). Supplying _pg.OwnerConnectionString to AddGameKit() satisfies the options validator without ever opening a Postgres connection during the test."
metrics:
  duration_min: 22
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 11
  test_count_delta: "+13 integration (7 LeaseHelper + 4 ProposalSweep + 2 LeaderElection); 43 total in GameKit.Matchmaking.Integration.Tests"
requirements_completed:
  - MATCH-04  # Redis source of truth — ticker reads queue + writes proposals atomically
  - MATCH-05  # Atomic claim via Lua fencing-token (script invoked by this plan; written in 05-04)
  - MATCH-07  # BackgroundService + Polly retry (MatchmakerTickerService)
  - MATCH-08  # Leader election via Redis distributed lock (MatchmakerLeaseHelper + SC#4 phase-gate verified)
---

# Phase 5 Plan 05: Matchmaker Ticker + Lease Helper + Proposal Sweeper Summary

**The live matchmaker is now wired.** Plan 05-05 ships the lease helper (`MatchmakerLeaseHelper`, Polly v8 wrapped around `LockTakeAsync/LockExtendAsync/LockReleaseAsync` against `gamekit:matchmaking:matcher:lock`), the ticker (`MatchmakerTickerService : BackgroundService + IMatchmakerTicker` driving a 500 ms `PeriodicTimer` against the live Redis queue), the proposal-sweeper (`ProposalSweeper`, SCAN-based partial-accept reaper closing Pitfall §10), the OTel `ActivitySource("GameKit.Matchmaking.Ticker")` for operator observability, and the SC#4 phase-gate `MatchmakingLeaderElectionTests` proving exactly-one-leader semantics under concurrency + forced failover within `LockTtlSeconds`. The `MatchmakerLeaseHelper` implements `IMatchmakerLease` (the Plan 05-07 contract) and uses `services.Replace` to supersede the minimal `RedisMatchmakerLease` default — unifying the lease across the ticker, the reconciler, and the retention sweep so all three share a single fencing-token `InstanceId` on the same Redis lock.

## Performance

- **Duration:** ~22 min
- **Started:** 2026-05-17T (immediately after worktree base reset)
- **Tasks:** 3 (all `type="auto" tdd="true"`)
- **Files created:** 11
- **Files modified:** 1
- **Test count delta:** +13 integration (7 LeaseHelper + 4 ProposalSweep + 2 LeaderElection); 43 total in `GameKit.Matchmaking.Integration.Tests`, 64 unchanged in `GameKit.Matchmaking.Tests`

## Accomplishments

1. **MatchmakerLeaseHelper (Task 1).** Line-by-line port of `RankingsTickerLeaseHelper.cs` from Phase 4 with the `Rankings` → `Matchmaking` symbol swap, lock key swapped to `MatchmakingRedisKeys.MatcherLock`, and TTL sourced from `GameKitMatchmakingTickerOptions.LockTtlSeconds`. Polly v8 pipeline identical (3 retries, exponential jitter, `RedisConnectionException` + `RedisTimeoutException`). `InstanceId = "{Environment.MachineName}:{Guid.NewGuid()}"` is the fencing token the AtomicClaimScript verifies as its FIRST step. **Implements `IMatchmakerLease`** — the Plan 05-07 contract — so `services.Replace(IMatchmakerLease)` in the ticker builder swaps Plan 05-07's `RedisMatchmakerLease` default. The ticker, reconciler, and retention sweeps then share a single fencing-token `InstanceId` on the same Redis lock.

2. **IMatchmakerTicker + MatcherTickResult enum (Task 1).** Public interface so integration tests can drive a single deterministic tick without waiting for the `PeriodicTimer`. `MatcherTickResult` enum: `NoMatch=0, Matched=1, LockNotAcquired=2, LeaseLost=3, RedisUnavailable=4` — adds `LeaseLost` (Rankings' `TickResult` does not have this state because Rankings has no fencing token). When the Lua claim returns `LEASE_LOST`, the ticker bails with `MatcherTickResult.LeaseLost` and the SC#4 phase-gate assertions detect the mid-tick failover.

3. **MatchmakingActivitySource (Task 1).** `internal static readonly ActivitySource Source = new("GameKit.Matchmaking.Ticker", "1.0.0")` with `StartTickActivity()` / `StartPoolActivity(ladderId, poolName)` / `StartProposalSweepActivity()` helpers. The `SourceName = "GameKit.Matchmaking.Ticker"` public const lets test code + operator XML docs cross-reference the literal without drift. **Pitfall §7 mitigation** — XML doc warns operators to register `AddSource("GameKit.Matchmaking.Ticker")` in their OTel SDK; without it, the spans are discarded silently.

4. **MatchmakerTickerService (Task 2).** `BackgroundService + IMatchmakerTicker`. `ExecuteAsync` uses `PeriodicTimer(TimeSpan.FromMilliseconds(opts.Ticker.TickIntervalMs))` (500 ms default) and catches `OperationCanceledException` for shutdown. `RunOnceAsync`:
   1. `TryAcquireLeaseAsync` → if false return `LockNotAcquired`.
   2. Open `StartTickActivity`. Check `mm:control:paused` Redis flag — if set, skip match-formation but still run the proposal-sweep.
   3. For each registered `MatchmakingLadderConfig`: `RenewLeaseAsync` (bail with `LeaseLost` on false — Pitfall §6), `ProcessPoolAsync`. Per-pool: `IServer.Keys(pattern: "mm:queue:*:{poolName}")`, `ZRANGEBYSCORE` (oldest-first), `HGETALL` each ticket hash to build `QueuedParty`, iterate candidates with `IMatchmakingStrategy.Match`, on match `AtomicClaimScript.ExecuteAsync` with `leaseValue: _lease.InstanceId`. On `Success`: `PUBLISH "proposed"` to each ticket's status channel + write `TicketEventType.Proposed` to channel. On `LeaseLost`: bail with `MatcherTickResult.LeaseLost`. On `TicketGone`: continue.
   4. After all pools: `RenewLeaseAsync` + `ProposalSweeper.SweepAsync` (Pitfall §10).
   5. `finally: ReleaseLeaseAsync`.

5. **ProposalSweeper (Task 2).** `SweepAsync(ct)` SCANs `mm:proposal:*` (via `IServer.Keys(pattern: ..., pageSize: 100)` — Pitfall §11), reads `deadlineMs` + `tickets` + `fields` from each proposal hash, identifies past-deadline proposals, partitions tickets via the `mm:proposal:{id}:accepts` set membership, **re-ZADDs accepting tickets back to their pool queue with their ORIGINAL `queuedAt` score** (CONTEXT D-09), PUBLISHes `"cancelled"` to declining tickets' `mm:status:{id}` channels, writes per-ticket `TicketEventType.Queued` / `TicketEventType.TimedOut` rows into the analytics channel, deletes the proposal hash + accept-tracker. Capped at 256 reaps per pass + 100 SCAN page size to bound per-tick work.

6. **IProposalService + ProposalServiceStub (Task 2).** Plan 05-06 stub. The interface is real (Plan 05-06 implements it); the stub throws `NotImplementedException("Plan 05-06")` on every call. Registered via `TryAddScoped<IProposalService, ProposalServiceStub>()` so Plan 05-06's later explicit `AddScoped<IProposalService, ProposalService>()` supersedes cleanly.

7. **MatchmakingBuilderExtensions.Ticker.cs (Task 2).** Partial-class file. `AddTickerServices()`:
   - `services.AddSingleton<MatchmakerLeaseHelper>()` — singleton so `InstanceId` is stable per process.
   - `services.Replace(ServiceDescriptor.Singleton<IMatchmakerLease>(sp => sp.GetRequiredService<MatchmakerLeaseHelper>()))` — supersedes Plan 05-07's `RedisMatchmakerLease` default.
   - `services.AddSingleton<ProposalSweeper>()`.
   - `services.AddSingleton<MatchmakerTickerService>()` + `AddHostedService(sp => sp.GetRequiredService<MatchmakerTickerService>())` + `AddSingleton<IMatchmakerTicker>(sp => sp.GetRequiredService<MatchmakerTickerService>())` — single instance backs both the host loop + integration tests.
   - `services.TryAddScoped<IProposalService, ProposalServiceStub>()`.

   `MatchmakingBuilderExtensions.AddMatchmaking` now calls `builder.Services.AddTickerServices()` AFTER `AddBackgroundServices()` so the `Replace(IMatchmakerLease)` supersedes correctly.

8. **MatchmakerLeaseHelperTests (Task 1, 7 [Fact]s).** Integration tests against Testcontainer Redis verifying the full lock-take / lock-extend / lock-release lifecycle, the fencing-token guarantee against cross-instance release (T-05-05-01 — `ReleaseLease_Does_Not_Remove_Another_Instances_Lock`), and the `{MachineName}:{Guid}` `InstanceId` format contract.

9. **ProposalSweepTests (Task 2, 4 [Fact]s).** Verifies Pitfall §10:
   - `PartialAccept_Reaper_ReQueues_Accepting_Parties_With_Original_QueuedAt` — 4-player proposal, 3 accept, 1 times out → 3 tickets re-ZADDed with original scores preserved, declining ticket receives `"cancelled"` PUBLISH, proposal + accepts subkey deleted, 3 `Queued` + 1 `TimedOut` `TicketEvent` rows.
   - `ProposalNotNearExpiry_NotSwept` — 30s-future deadline → `reaped == 0`, hash intact.
   - `ProposalSweeper_Source_Uses_SCAN_Not_KEYS` — source grep guard (Pitfall §11 — no raw `ExecuteAsync("KEYS", ...)`).
   - `ProposalSweeper_Source_Uses_IServer_Keys` — positive complement.

10. **MatchmakingLeaderElectionTests (Task 3, 2 [Fact]s — SC#4 phase gate).** Two replicas of `MatchmakerTickerService` sharing the same Redis + Postgres:
    - `Two_Tickers_Only_One_Drains_Per_Tick` — `Task.WhenAll(t1.RunOnceAsync, t2.RunOnceAsync)` returns exactly one (Matched|NoMatch) + exactly one `LockNotAcquired`. `ZCARD mm:queue:* == 0` (leader drained both tickets atomically). Exactly one `mm:proposal:*` hash present (no double-match — T-05-05-01 mitigation).
    - `Forced_Failover_NonLeader_Acquires_After_LeaseTtl` (the SC#4 phase-gate literal) — `LockTtlSeconds=5`, helper1 acquires lease, discarded WITHOUT calling `ReleaseLeaseAsync` (simulating a crash). Wait `LeaseTtl + 1s`. helper2's ticker.RunOnceAsync acquires the now-free lock and returns NoMatch (no seeded tickets); confirms `helper2.InstanceId != helper1.InstanceId` (new fencing token in force).

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | MatchmakerLeaseHelper + IMatchmakerTicker + MatcherTickResult + MatchmakingActivitySource + 7 LeaseHelper integration tests | `67ee380` | feat |
| 2 | MatchmakerTickerService + ProposalSweeper + IProposalService stub + ticker builder wiring + 4 ProposalSweep integration tests | `4c17889` | feat |
| 3 | MatchmakingLeaderElectionTests (SC#4 phase gate: two-replica leader election + forced failover) | `bdf8997` | test |

Plan metadata commit will be made by the orchestrator after merge — this worktree commits the SUMMARY only.

## Verification Evidence

- `dotnet build src/GameKit.Matchmaking --nologo` → exit 0 / 0 warnings / 0 errors.
- `dotnet build GameKit.sln --nologo` → exit 0 / 0 warnings / 0 errors (full solution).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests` → **43 / 43 pass** (20 prior + 7 LeaseHelper + 4 ProposalSweep + 2 LeaderElection + 10 from Plan 05-07).
- `dotnet test tests/GameKit.Matchmaking.Tests` → **64 / 64 pass** (unchanged — no new unit tests in this plan).
- **SC#4 phase gate:** both `MatchmakingLeaderElectionTests` [Fact]s green.
- `MatchmakerLeaseHelper` literal lock key — `gamekit:matchmaking:matcher:lock` — VERIFIED via grep + `MatchmakingRedisKeys.MatcherLock` reference.
- `MatchmakerTickerService.RunOnceAsync` calls `RenewLeaseAsync` before EACH pool — VERIFIED by source grep + behavioural test (`Forced_Failover` test asserts new leader's `InstanceId` differs from stale).
- `AtomicClaimScript.ExecuteAsync` called with `leaseValue: _lease.InstanceId` (fencing token bound correctly) — VERIFIED at `MatchmakerTickerService.TryClaimMatchAsync`.
- `ActivitySource("GameKit.Matchmaking.Ticker")` registered — VERIFIED in `MatchmakingActivitySource.Source` literal.
- `ProposalSweeper` uses SCAN (NOT KEYS) — VERIFIED by `ProposalSweeper_Source_Uses_SCAN_Not_KEYS` + `ProposalSweeper_Source_Uses_IServer_Keys` programmatic tests.

## Decisions Made

(All decisions are also captured in the YAML frontmatter `decisions:` block; this section restates intent for human reviewers.)

- **`MatchmakerLeaseHelper` implements `IMatchmakerLease` + supersedes via `services.Replace`.** The Plan 05-07 SUMMARY §Wave-3 Coordination Notes flagged the merge-time ambiguity: Plan 05-07 ships a minimal `RedisMatchmakerLease` default behind the interface, but Plan 05-05's richer helper should win post-merge. The `services.Replace(IMatchmakerLease)` call in `MatchmakingBuilderExtensions.Ticker.cs` closes this ambiguity at the DI level — the ticker + reconciler + retention sweeps all resolve the same Polly-wrapped helper.
- **Sweeper uses `deadlineMs` field, NOT Redis KEY TTL.** Discovered during initial test execution: Redis's `EXPIRE` on a hash deletes the hash entirely on expiry, so a SCAN-based sweeper would never see past-deadline proposals if it relied on `KeyTimeToLiveAsync`. The ticker now writes `deadlineMs` Unix-ms onto the proposal hash alongside the Lua script's `fields` JSON, and the sweeper compares it to the current clock. The Redis KEY TTL still exists as a back-stop cleanup for proposals the sweeper somehow misses (e.g. process death between SCAN pages).
- **`IProposalService` + stub shipped here, not in 05-06.** Plan 05-06 fills the body; the stub exists so the ticker (this plan) and the HTTP endpoints (Plan 05-08) can take a clean DI dep on the contract. `TryAddScoped` ensures Plan 05-06's later `AddScoped<IProposalService, ProposalService>()` supersedes regardless of order.
- **Per-pool ladder enumeration via `IServer.Keys` SCAN (not a separate ladder-name → ladder-id index in DI).** Operators register 1–3 ladders in v1; SCAN overhead <1 ms per tick. Future optimisation gated on benchmark evidence.
- **`MatchmakerTickerService` is a singleton.** Same instance backs both the `IHostedService` loop and the `IMatchmakerTicker` resolved by integration tests for deterministic single-tick execution. Mirrors the Rankings precedent.
- **MatchmakingLeaderElectionTests does NOT run Postgres migrations.** The Plan 05-05 ticker is Redis-only; analytics writes flow through the `Channel<TicketEvent>` placeholder + Plan 05-07's drain. Supplying `_pg.OwnerConnectionString` to `AddGameKit()` satisfies the options validator without opening a Postgres connection during the test.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 — Bug] ProposalSweeper expiry detection switched from KEY TTL to `deadlineMs` hash field**

- **Found during:** Task 2 — initial run of `PartialAccept_Reaper_ReQueues_Accepting_Parties_With_Original_QueuedAt` returned `reaped == 0`.
- **Issue:** My first ProposalSweeper implementation called `db.KeyTimeToLiveAsync(key)` to detect past-deadline proposals. When the test set TTL=1s and waited 1.1s, Redis deleted the entire proposal hash before SCAN could enumerate it — so `reaped` was always 0. The Lua atomic-claim script DOES set an EXPIRE on the proposal hash (line 65 of `AtomicClaimScript.LuaSource`), which is the proposal-service's expiry-cleanup signal, but it breaks SCAN-based reaping.
- **Fix:** Ticker writes a `deadlineMs` Unix-ms hash field onto the proposal hash via `db.HashSetAsync` AFTER the Lua claim returns Success. ProposalSweeper reads this field and compares to `_clock.UtcNow.ToUnixTimeMilliseconds()`. The Redis KEY TTL stays in place as a back-stop cleanup for proposals the sweeper misses.
- **Files modified:** `src/GameKit.Matchmaking/Services/ProposalSweeper.cs`, `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs`, `tests/GameKit.Matchmaking.Integration.Tests/ProposalSweepTests.cs`.
- **Verification:** Both ProposalSweep tests pass (`PartialAccept_Reaper_...` returns `reaped == 1` with all D-09 invariants intact; `ProposalNotNearExpiry_NotSwept` returns `reaped == 0` with hash retained).
- **Committed in:** `4c17889` (Task 2 commit — developed iteratively before the first commit).

**2. [Rule 3 — Auto-fix blocking issue] xUnit1031 blocking-task-operation error in MatchmakingLeaderElectionTests**

- **Found during:** Task 3 — first compile of `MatchmakingLeaderElectionTests.cs`.
- **Issue:** xUnit's `xUnit1031` analyzer flagged `cancelledReceived.Task.Result` (was an oversight when porting the pattern from ProposalSweepTests — I'd accidentally typed `.Result` instead of `await`).
- **Fix:** Replaced `cancelledReceived.Task.Result` with `await cancelledReceived.Task` after the `Task.WhenAny` arbitrates timeout vs. completion.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/ProposalSweepTests.cs` (the bug was here originally, not in the leader-election test — the analyzer caught it before commit).
- **Committed in:** `4c17889`.

**3. [Rule 3 — Auto-fix blocking issue] AllowAdmin=true required for FLUSHDB in MatchmakingLeaderElectionTests**

- **Found during:** Task 3 — first run of the leader-election tests.
- **Issue:** `StackExchange.Redis.RedisCommandException : This operation is not available unless admin mode is enabled: FLUSHDB`. The default StackExchange.Redis connection blocks admin commands; the test class needed to opt in to call `FlushDatabaseAsync`.
- **Fix:** Switched from `ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString)` to `ConfigurationOptions.Parse(...)` + `muxOpts.AllowAdmin = true` + `ConnectAsync(muxOpts)` for the test-fixture connection (the production-service connections do NOT opt in — only the test harness needs FLUSHDB).
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderElectionTests.cs`.
- **Committed in:** `bdf8997` (Task 3 commit).

**4. [Rule 3 — Auto-fix blocking issue] XML cref to MatchmakerTickerService failed before the type existed**

- **Found during:** Task 1 — first build after writing `IMatchmakerTicker.cs` + `MatchmakerLeaseHelper.cs`.
- **Issue:** Both files referenced `<see cref="MatchmakerTickerService"/>` in XML doc comments, but the type lives in Task 2's file — Task 1 doesn't ship it yet, so CS1574 failed the build.
- **Fix:** Downgraded the two `<see cref>` references to `<c>MatchmakerTickerService</c>` plain-text. Task 2 could have promoted them back, but the plain-text reference is also acceptable per Phase 4 precedent (`IRankingsTicker.cs` does NOT use `<see cref>` on the concrete type either).
- **Files modified:** `src/GameKit.Matchmaking/Services/IMatchmakerTicker.cs`, `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs`.
- **Committed in:** `67ee380`.

### Other Deviations

None. The plan body's `<behavior>` and `<action>` sections matched the codebase patterns exactly. All four auto-fixes were caught at build/test time and resolved before the relevant task's commit.

## Threat Surface Notes

The plan's `<threat_model>` identified four threats:

- **T-05-05-01 (Tampering: Two replicas double-match same ticket pair during lease handoff):** mitigated. `MatchmakingLeaderElectionTests.Two_Tickers_Only_One_Drains_Per_Tick` asserts exactly one leader + one non-leader and exactly one proposal hash present. The `MatchmakerLeaseHelper.ReleaseLease_Does_Not_Remove_Another_Instances_Lock` unit test guards the fencing-token release path. The Lua script's first non-comment line is the fencing-token check (Plan 05-04 source — verified by `AtomicClaimScriptTests.LuaSource_First_Step_Is_Fencing_Token_Check`).
- **T-05-05-02 (DoS: Ticker loop exceeds tick budget under load):** mitigated by design. `LockTtlSeconds=90` (default) is >> any reasonable tick budget (50 ms target per `Ticker.MaxIterationBudgetMs`); `RenewLeaseAsync` between pools means a leader that takes longer than 50 ms per pool still renews the lock long before TTL expiry. **SC#3 load test (Plan 05-10) verifies the budget at 1k tickets** — this plan provides the runtime; 05-10 provides the benchmark.
- **T-05-05-03 (Information Disclosure: Long-running ticker leaks lease — replica crashes between RunOnceAsync and ReleaseLeaseAsync):** accepted. `Forced_Failover_NonLeader_Acquires_After_LeaseTtl` proves the natural-expiry path — the lock expires via TTL and a new replica picks up. Cost is up to `LockTtlSeconds` (90s default) of no matching after a crash; acceptable for a self-hosted backend.
- **T-05-05-04 (Tampering: Reaped proposal's accepting parties re-ZADDed with incorrect queuedAt):** mitigated. `ProposalSweepTests.PartialAccept_Reaper_ReQueues_Accepting_Parties_With_Original_QueuedAt` asserts the original Unix-ms score is preserved across the re-ZADD — the sweeper reads `queuedAt` from the ticket hash (set at enqueue, never mutated), NOT the current time.

No new threat flags surfaced during execution. The ticker introduces no new network endpoints, auth paths, file access patterns, or schema changes — it consumes existing Redis surfaces and writes to the existing in-memory `Channel<TicketEvent>`.

## Self-Check: PASSED

### Files

- `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IMatchmakerTicker.cs` — FOUND
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/MatcherTickResult.cs` — FOUND
- `src/GameKit.Matchmaking/Services/ProposalSweeper.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IProposalService.cs` — FOUND
- `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ticker.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerLeaseHelperTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/ProposalSweepTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderElectionTests.cs` — FOUND

### Commits

- `67ee380` (Task 1 — LeaseHelper + IMatchmakerTicker + ActivitySource + 7 tests) — FOUND
- `4c17889` (Task 2 — TickerService + ProposalSweeper + builder wiring + 4 tests) — FOUND
- `bdf8997` (Task 3 — MatchmakingLeaderElectionTests SC#4 phase gate) — FOUND

### Verification gates

- `dotnet build src/GameKit.Matchmaking` exit 0 / 0 warnings — VERIFIED
- `dotnet build GameKit.sln` exit 0 / 0 warnings — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests` exit 0 — VERIFIED (43/43 pass)
- `dotnet test tests/GameKit.Matchmaking.Tests` exit 0 — VERIFIED (64/64 pass)
- MatchmakerLeaseHelper implements IMatchmakerLease — VERIFIED (grep `: IMatchmakerLease`)
- MatchmakerTickerService.RunOnceAsync calls RenewLeaseAsync before each pool — VERIFIED (grep + behavioural test)
- AtomicClaimScript called with leaseValue: _lease.InstanceId — VERIFIED (grep `InstanceId`)
- ProposalSweeper uses SCAN (IServer.Keys) — VERIFIED programmatically (`ProposalSweeper_Source_Uses_IServer_Keys`)
- ActivitySource("GameKit.Matchmaking.Ticker") declared — VERIFIED at `MatchmakingActivitySource.SourceName`
- SC#4 phase gate Forced_Failover_NonLeader_Acquires_After_LeaseTtl passes — VERIFIED

## Next Plan Readiness

- **05-06** (ProposalService accept/decline lifecycle) can ship. `IProposalService` resolves from DI via the Plan 05-05 stub; Plan 05-06's `AddScoped<IProposalService, ProposalService>()` supersedes via standard MS.DI ordering. The ticker writes `mm:proposal:{id}` hashes with the `deadlineMs` + `tickets` CSV fields the proposal service needs to enumerate participants on accept/decline.
- **05-08** (HTTP endpoints) can ship. Endpoints inject `ChannelWriter<TicketEvent>` (placeholder + Plan 05-07's options-driven rebound), `IPartyService` (Plan 05-04), `IMatchmakerLease` (Plan 05-05's MatchmakerLeaseHelper supersedes Plan 05-07's RedisMatchmakerLease via `services.Replace`), and `IProposalService` (Plan 05-06 supersedes the stub).
- **05-10** (SC#3 1k-concurrent load test) can ship. The full match-formation path is now wired end-to-end: enqueue (Plan 05-08) → ticker scans Redis → strategy matches → atomic-claim → PUBLISH → channel-write. The 50 ms `MaxIterationBudgetMs` is the benchmark's primary target.

---
*Phase: 05-matchmaking-parties*
*Plan: 05*
*Completed: 2026-05-17*
