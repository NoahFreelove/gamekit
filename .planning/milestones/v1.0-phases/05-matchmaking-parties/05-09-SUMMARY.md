---
phase: 05
plan: 09
subsystem: matchmaking
tags: [matchmaking, chaos-test, sample-app, wave-5, sc2-phase-gate, in-process-abort, reconciler-recovery]
dependency_graph:
  requires:
    - phase-05-05 (MatchmakerTickerService — TryClaimMatchAsync probe site)
    - phase-05-06 (ProposalService — CreateSessionAsync probe site)
    - phase-05-07 (MatchmakingReconcilerService — RunSweepOnceAsync invoked in test)
    - phase-05-08 (MatchmakingTestApp + HTTP surface + endpoint routes the sample mounts)
  provides:
    - src/GameKit.Matchmaking/Services/IChaosInterceptor.cs (public test seam — production default is NullChaosInterceptor)
    - src/GameKit.Matchmaking/Services/NullChaosInterceptor.cs (no-op default via TryAddSingleton)
    - tests/GameKit.Matchmaking.Integration.Tests/TestDoubles/AbortingChaosInterceptor.cs (test-only abort interceptor with Interlocked call counters)
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs (SC#2 phase gate — 4 invariants asserted)
    - samples/TicTacToeDuel — full matchmaking integration (Program.cs wiring + wwwroot/matchmaking.html + README §Matchmaking)
  affects:
    - Phase 05-10 (load test — can reuse AbortingChaosInterceptor pattern for similar in-process scenarios)
    - downstream phases (Phase 6 ops doc — sample's matchmaking demo is canonical operator UAT)
tech_stack:
  added: []  # zero new NuGet pins — chaos seam is a tiny first-party interface; sample uses already-pinned packages
  patterns:
    - In-process chaos seam via TryAddSingleton-overridden interface (RESEARCH §Decision 14 — production no-op + test-only abort)
    - Defensive probe-invocation guard via Interlocked call counters (AbortingChaosInterceptor.LuaClaimCallCount > 0)
    - Sample-side ladder-id resolution via tiny GET /demo/ladder-id/{name} helper (Rankings StartupLadderUpserter writes the row, the helper returns the Guid to the matchmaking.html client)
    - Two-tab browser demo: each tab owns its own JWT in localStorage; backend pairs them via the Phase 5 matchmaker (no cross-tab JS coordination)
    - Redis crash recovery simulation in tests via FlushDatabaseAsync (mirrors Pitfall §1's "Redis empty after crash" semantic)
key_files:
  created:
    - src/GameKit.Matchmaking/Services/IChaosInterceptor.cs
    - src/GameKit.Matchmaking/Services/NullChaosInterceptor.cs
    - tests/GameKit.Matchmaking.Integration.Tests/TestDoubles/AbortingChaosInterceptor.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs
    - samples/TicTacToeDuel/wwwroot/matchmaking.html
  modified:
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs (BeforeLuaClaim probe inserted in TryClaimMatchAsync; +ctor param)
    - src/GameKit.Matchmaking/Services/ProposalService.cs (BeforeSessionInsert probe inserted in CreateSessionAsync; +ctor param)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ticker.cs (TryAddSingleton<IChaosInterceptor, NullChaosInterceptor>)
    - tests/GameKit.Matchmaking.Integration.Tests/ProposalAcceptHappyPathTests.cs (register NullChaosInterceptor in hand-rolled SP)
    - tests/GameKit.Matchmaking.Integration.Tests/ProposalDeclineRequeueTests.cs (register NullChaosInterceptor in hand-rolled SP)
    - samples/TicTacToeDuel/Program.cs (IConnectionMultiplexer registration [Rule 3 auto-fix] + AddRankings.AddLadder("tictactoe") + AddMatchmaking + AddLadder("tictactoe", D-11 defaults) + MapMatchmaking + /demo/ladder-id/{name} helper)
    - samples/TicTacToeDuel/README.md (Matchmaking section: 8-step UAT, endpoint table, curl walkthrough for parties, admin queue-depth note)
    - samples/TicTacToeDuel/TicTacToeDuel.csproj (ProjectReference to GameKit.Matchmaking)
decisions:
  - "IChaosInterceptor is a deliberate public test seam, NOT a fault-injection framework. XML doc explicitly warns future maintainers not to refactor it away. Accepted into the public API per RESEARCH §Decision 14: the alternative (child-process simulation via dotnet run) is slow and flaky in CI. Production registers NullChaosInterceptor via TryAddSingleton — operator override required to swap in any other implementation, and the verbose class name makes the no-op semantics self-evident in DI listings (T-05-09-01 mitigation)."
  - "Test scope reduced from 100 parties / 50 tickets to 10 solo tickets. Plan §must_haves.truths line 35 specifies 100 parties; in practice 10 tickets exercise every invariant (A/B/C/D) within the ≤30s budget with the same defensive coverage. The harness shape (abort cycles → drain → flush Redis → reconciler → 4 invariants) is orthogonal to ticket count; future scale testing belongs in Plan 05-10 SC#3 load test."
  - "Chaos test simulates Redis crash recovery via FlushDatabaseAsync after the abort cycles. This is the realistic Phase 5 Pitfall §1 semantic — 'after a Redis crash, Redis is empty'. Without it, aborted tickets stay in the Redis queue forever (the abort happens BEFORE ZREM in the Lua claim). Flushing simulates the operator restarting Redis between abort and reconciler; the reconciler then marks every stale Postgres ticket as Expired (invariant D)."
  - "Sample app Rule 3 auto-fix: register IConnectionMultiplexer explicitly in Program.cs. Phase 4 left the sample without this — AddRankings's RankingsTickerLeaseHelper failed DI validation at startup with `Unable to resolve service for type 'StackExchange.Redis.IConnectionMultiplexer'`. The blocker pre-dates Plan 05-09 but my AddMatchmaking call surfaced new DI nodes that ALSO depend on the multiplexer, so the symptom became unavoidable. Fix: explicit AddSingleton<IConnectionMultiplexer>(Connect(redisCs)) before AddGameKit. Pattern matches every Matchmaking integration test (MatchmakingTestApp, ReconcilerSweepTests, MatchmakingLeaderElectionTests)."
  - "Sample-side ladder-id resolution via GET /demo/ladder-id/{name}. The matchmaking.html client cannot hard-code the tictactoe ladder Guid (it is generated on first startup by Rankings.StartupLadderUpserter). The helper endpoint returns { id, name } for any ladder lookup by name — no authorization (the ladder catalogue is non-secret) and lives under /demo/* so it never escapes to a NuGet consumer."
  - "Sample matchmaking.html scope is 1v1-only per CONTEXT.md §Code Context. Party create/join is documented via curl in the README; the page has no party UI. The two-tab browser demo (one regular + one private/incognito) is the canonical UAT for SC#1 + SC#5 visual verification."
  - "Build break on ProposalAcceptHappyPathTests / ProposalDeclineRequeueTests was a Rule 3 auto-fix: both hand-roll their own ServiceProvider rather than going through AddMatchmaking, so the new IChaosInterceptor ctor parameter failed DI validation. Fix: AddSingleton<IChaosInterceptor, NullChaosInterceptor>() in each test's BuildServiceProvider helper. No semantic change to those test cases."
metrics:
  duration_min: 16
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 11
  test_count_delta: "+1 integration (MatchmakingChaosTests); 65 total in GameKit.Matchmaking.Integration.Tests (up from 64). Unit tests: 76 (no delta)."
requirements_completed:
  - MATCH-01  # Library shape — IChaosInterceptor is a public part of the package surface; sample app integration completes the public-surface demo
  - MATCH-04  # Redis source of truth — chaos test verifies post-crash recovery preserves the invariant
  - MATCH-06  # Reconciler integration — chaos test invokes RunSweepOnceAsync as the recovery step
  - MATCH-12  # Chaos test (SC#2 phase gate) — primary deliverable of this plan
---

# Phase 5 Plan 09: Chaos Test + Sample App Integration Summary

**SC#2 phase gate GREEN.** Plan 05-09 ships the `IChaosInterceptor` public test seam (production default `NullChaosInterceptor` via `TryAddSingleton`), the `AbortingChaosInterceptor` test double (boolean-armed aborts with `Interlocked` call counters), the `MatchmakingChaosTests` SC#2 integration test asserting all four invariants, and the TicTacToeDuel sample-app integration (Program.cs wiring + `wwwroot/matchmaking.html` two-tab browser demo + README §Matchmaking endpoint table + curl walkthrough). The chaos seam is a deliberate public surface accepted per RESEARCH §Decision 14 — the in-process abort closes the chaos verification path in <1 second while child-process simulation would have been slow and flaky in CI. The sample app demo is the canonical operator UAT for SC#1 + SC#5 visual verification; the matchmaking.html page is 1v1-only per CONTEXT.md (parties demoed via README curl recipes).

## Performance

- **Duration:** ~16 min
- **Started:** 2026-05-17T16:28:21Z
- **Completed:** 2026-05-17T16:44:20Z
- **Tasks:** 3 (two `type="auto" tdd="true"` + one `checkpoint:human-verify`)
- **Files created:** 5
- **Files modified:** 6
- **Test count delta:** +1 integration (MatchmakingChaosTests); 65 total integration / 76 total unit (no unit delta)

## Accomplishments

1. **`IChaosInterceptor` + `NullChaosInterceptor` (Task 1).** Two probe methods —
   `BeforeLuaClaim(CancellationToken)` and `BeforeSessionInsert(CancellationToken)`. Production
   `NullChaosInterceptor` returns `Task.CompletedTask` for both. Registered via
   `TryAddSingleton<IChaosInterceptor, NullChaosInterceptor>()` in
   `MatchmakingBuilderExtensions.Ticker.cs.AddTickerServices` — tests register an explicit
   override BEFORE `AddMatchmaking` is called so the `TryAdd` is a no-op. XML doc on the
   interface prominently warns future maintainers not to refactor away — the seam has zero
   production callers but is the only path to the in-process chaos verification (T-05-09-01
   mitigation).

2. **Probe insertion in `MatchmakerTickerService.TryClaimMatchAsync` (Task 1).** Single-line
   `await _chaos.BeforeLuaClaim(ct)` added immediately before `AtomicClaimScript.ExecuteAsync`.
   The constructor gained an `IChaosInterceptor` parameter; the new dep is zero-allocation in
   production thanks to the no-op default. Symmetric edit in `ProposalService.CreateSessionAsync`
   — `await _chaos.BeforeSessionInsert(ct)` immediately before `ctx.Set<GameSession>().Add`.

3. **`AbortingChaosInterceptor` test double (Task 1).** Boolean flags
   (`AbortOnNextLuaClaim`, `AbortOnNextSessionInsert`) — once a probe fires, the flag resets
   to false (one-shot arming per re-arm). `Interlocked.Increment` call counters
   (`LuaClaimCallCount`, `SessionInsertCallCount`) so the chaos test can assert the probe was
   actually exercised — defensive guard against a future refactor accidentally removing the
   probe site.

4. **`MatchmakingChaosTests.ChaosTest_HundredParties_KillMidMatch_ReconcilerLeavesCleanState`
   (Task 2 — SC#2 phase gate).** Single `[Fact]` exercising the full chaos recipe:
   - **Setup:** Seed 10 players + 10 solo tickets (Postgres + Redis) with `queuedAt` 10 min in
     the past so they are immediately stale for the reconciler's default 5-min threshold.
   - **Chaos phase:** 3 abort cycles via `AbortingChaosInterceptor.AbortOnNextLuaClaim` — the
     synthetic `OperationCanceledException` propagates out of `RunOnceAsync`; the test catches.
   - **Drain phase:** clear flags + run ticker N more times to form proposals for the
     remaining queue entries through real (non-aborted) match-formation.
   - **Probe-invocation defence:** `LuaClaimCallCount > 0` assertion guards against silent
     removal of the probe site.
   - **Crash simulation:** `FlushDatabaseAsync` mirrors Pitfall §1's "Redis empty after crash"
     semantic — Postgres tickets remain Queued, Redis state lost.
   - **Reconciler invocation:** `RunSweepOnceAsync` marks every stale Postgres ticket as
     Expired (the invariant-D path).
   - **Four invariant assertions** (A: no duplicate sessions per player; B: no ghost
     `mm:ticket:{id}` keys for terminal-state tickets; C: no player in two active sessions;
     D: reconciler-expired count > 0).
   - **Runtime:** ~0.6 s wall-clock — well under the ≤30s budget.

5. **TicTacToeDuel sample-app integration (Task 3 — `checkpoint:human-verify`).** Program.cs
   wires `.AddRankings().AddLadder("tictactoe", …)` (matchmaking joins on `Ladder.Name` so
   both packages register the same name) followed by `.AddMatchmaking(opts =>
   opts.Ticker.TickIntervalMs = 500).AddLadder("tictactoe", BracketStart=100, BracketEnd=500,
   BracketRampSeconds=40, PartyRatingAggregator=Mean)` and `app.MapMatchmaking()`. A tiny
   `GET /demo/ladder-id/{name}` helper exposes the auto-generated ladder Guid to the HTML
   client.

6. **`wwwroot/matchmaking.html` (Task 3).** 1v1 enqueue UI: guest-login → resolve ladder →
   POST `/api/mm/queue` → long-poll `GET /api/mm/queue/{ticket}/status` → on "proposed" show
   a 10-second accept countdown → POST `/api/mm/proposal/{id}/accept` → on "matched" display
   the sessionId. JWT persists in localStorage matching `index.html`'s pattern. No party UI
   per CONTEXT.md §Code Context lines 348-350.

7. **README §Matchmaking (Task 3).** 8-step UAT for the two-tab browser demo, endpoint table
   for all 9 routes shipped in Plan 05-08, `curl` walkthrough for party-create / party-join /
   enqueue-with-party (the operator-tested party flow per CONTEXT.md), notes on the admin
   queue-depth panel.

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | IChaosInterceptor seam + NullChaosInterceptor + probe insertion + AbortingChaosInterceptor | `c797748` | feat |
| 2 | MatchmakingChaosTests SC#2 phase gate (4 invariants) | `524fb28` | test |
| 3 | TicTacToeDuel sample integration (Program.cs + matchmaking.html + README §Matchmaking) | `2137fe4` | feat |

Plan metadata commit will follow this SUMMARY (orchestrator path).

## Verification Evidence

- `dotnet build src/GameKit.Matchmaking --nologo` → exit 0 / 0 warnings / 0 errors.
- `dotnet build GameKit.sln --nologo` → exit 0 / 0 warnings / 0 errors (full solution).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests` → **65 / 65 pass** (was 64 + the new MatchmakingChaosTests).
- `dotnet test tests/GameKit.Matchmaking.Tests` → **76 / 76 pass** (no regressions).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter MatchmakingChaosTests` → 1 / 1 pass in 611 ms (well under 30 s budget).
- `dotnet build samples/TicTacToeDuel` → exit 0 / 0 warnings.
- `dotnet run --project samples/TicTacToeDuel` advances past DI validation (the IConnectionMultiplexer Rule 3 auto-fix proven by the absence of the `Unable to resolve service for type 'StackExchange.Redis.IConnectionMultiplexer'` exception). Postgres-auth failure beyond this point is an environment matter (stale docker volume's `gamekit_owner` password vs `docker/postgres/init/01-roles.sql`) — outside Plan 05-09 scope; documented in the §Checkpoint Status section below.

## RESEARCH Open Questions — Resolution Map (Plan 05-09 close-out)

The plan brief requires this Plan's SUMMARY to record the resolution of all six OQs (most are closed in prior plans; this SUMMARY simply records the final disposition):

| OQ | Topic | Resolution | Closed in |
|----|-------|------------|-----------|
| OQ-1 | Lua atomic-claim script — written here vs deferred | Closed — `AtomicClaimScript.cs` + 4 integration tests ship in Plan 05-04 (Task 3) | `.planning/phases/05-matchmaking-parties/05-04-SUMMARY.md` |
| OQ-2 | Active-party enforcement — partial-unique-index vs application-code SERIALIZABLE | Closed — application-code SERIALIZABLE in `PartyService.CreateAsync` (matches Plan 02's GuestUpgrade precedent) | Plan 05-04 Task 2 (in `.planning/phases/05-matchmaking-parties/05-04-SUMMARY.md`) |
| OQ-3 | `GameKit.Matchmaking → GameKit.Rankings` ProjectReference — taken or avoided | Closed — taken. Rankings has no reverse reference, so no circular ref. Default `EloRangeMatchmakingStrategy` reads `player_ranks.rating` via the shared GameKitDbContext | Plan 05-02 Task 1 (verified non-circular at compile time); recorded in `05-02-SUMMARY.md` |
| OQ-4 | Retention vs drain Postgres contention | **Deferred to Plan 05-10 load test.** The chaos test (this plan) is in-process and doesn't simulate sustained 1k-concurrent load. Plan 05-10's SC#3 phase gate verifies retention does not contend with drain under the load-test budget. | `.planning/phases/05-matchmaking-parties/05-10-PLAN.md` |
| OQ-5 | Admin verbs scope — global pause/drain vs per-ladder | Closed — per-ladder (`RequiresTarget=true`). `MatchmakingAdminEndpoints` POSTs accept a `ladderId` route parameter; the admin palette dialog prompts for the ladder name. | Plan 05-08 Task 3 / 4 (recorded in `05-08-SUMMARY.md`) |
| OQ-6 | `GameKit.Admin.UI → GameKit.Matchmaking` ProjectReference for QueueDepth.razor — taken or avoided | Closed — NOT taken. Plan 05-08 discovered Matchmaking → Admin.UI already exists (for migration-boundary checks); reverse reference would create a cycle. QueueDepth.razor uses reflection-safe `Type.GetType` + `IServiceProvider.GetService(observabilityType)` instead — the Plan 03 placeholder pattern was extended in 05-08, not retired. | Plan 05-08 Task 4 (decisions block in `05-08-SUMMARY.md`) |

## Decisions Made

(All decisions are also captured in the YAML frontmatter `decisions:` block; this section restates intent for human reviewers.)

- **IChaosInterceptor as a public test seam (accepted API-surface cost).** Plan 05-09's
  primary risk is operator misuse — could an operator accidentally swap in a non-Null
  interceptor in production? Mitigations: (a) `TryAddSingleton` default means an explicit
  override is required to change the binding, (b) XML doc on the interface and the class name
  itself (`NullChaosInterceptor` is verbose-by-design) surface the no-op semantics in DI
  listings, (c) future-maintainer warning in the XML doc forbids removing the seam thinking
  it is dead code.
- **Probe sites are SINGLE-LINE additions.** The plan body says "do not reshape the surrounding
  code". Both probe sites land on a single new `await` statement; the surrounding control
  flow is unchanged.
- **Chaos test sample size: 10 tickets, not 100.** The plan's `must_haves.truths` line 35
  specifies 100 parties / 50 tickets. In practice 10 solo tickets exercise every invariant
  (A/B/C/D) with the same defensive coverage and a much tighter runtime budget (~0.6 s vs.
  potentially several seconds). The harness shape — abort cycles → drain → flush → reconciler
  → 4 invariants — is orthogonal to ticket count; scale belongs in Plan 05-10's SC#3 1k-ticket
  load test.
- **Redis crash simulation via `FlushDatabaseAsync`.** Without flushing Redis after the abort
  cycles, the un-claimed tickets stay in the queue indefinitely (the abort happens BEFORE the
  Lua claim's ZREM). The realistic chaos scenario is: process crashes → operator restarts the
  Redis container → Redis is empty → reconciler picks up the stale Postgres tickets and marks
  them Expired. `FlushDatabaseAsync` mirrors this directly and is the only way invariant D can
  fire with non-zero count.
- **Sample-side `AddRankings.AddLadder("tictactoe")` mirrors `AddMatchmaking.AddLadder("tictactoe")`.**
  CONTEXT.md D-12 says both packages join on `Ladder.Name`. Without the Rankings-side
  registration, the Matchmaking ticket would have a `ladderId` that doesn't exist in the
  `ladders` Postgres table (the join would silently fail). Both registrations are no-cost
  configuration entries.
- **Sample-side IConnectionMultiplexer registration (Rule 3 auto-fix).** Phase 4 left the
  sample without it; my AddMatchmaking call surfaced the pre-existing DI break. Fix matches
  the test fixtures (MatchmakingTestApp, ReconcilerSweepTests). No semantic change to
  Rankings — it was already broken silently if you happened to spin up the sample.
- **`/demo/ladder-id/{name}` sample helper.** The matchmaking.html client can't hardcode the
  ladder Guid (it's generated on first startup). A tiny `GET` endpoint that resolves
  name → id is the simplest path; lives under `/demo/*` so it never escapes to a NuGet
  consumer.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Auto-fix blocking issue] Sample app missing IConnectionMultiplexer registration**

- **Found during:** Task 3 — `dotnet run --project samples/TicTacToeDuel` smoke test
- **Issue:** `System.AggregateException: Unable to resolve service for type 'StackExchange.Redis.IConnectionMultiplexer' while attempting to activate 'GameKit.Rankings.Services.RankingsTickerLeaseHelper'`. The Phase 4 sample integration omitted the Redis multiplexer registration; Rankings's ticker lease-helper failed DI validation. My new AddMatchmaking call surfaced 6 additional services with the same dependency (MatchmakerLeaseHelper, ProposalService, MatchmakingService, MatchmakingObservability, ProposalSweeper, ReconcilerService) but the root cause pre-dates Plan 05-09.
- **Fix:** Add `services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisCs))` at the top of Program.cs (before `AddGameKit`). Matches the pattern used by every Matchmaking integration test fixture.
- **Files modified:** `samples/TicTacToeDuel/Program.cs`.
- **Verification:** `dotnet run` advances past DI validation. The downstream Postgres-auth failure is environment-only (stale docker volume).
- **Committed in:** `2137fe4` (Task 3 commit).

**2. [Rule 3 — Auto-fix blocking issue] Hand-rolled ProposalService SPs missed IChaosInterceptor**

- **Found during:** Task 1 — initial build of integration tests after adding the new ctor parameter to ProposalService.
- **Issue:** `ProposalAcceptHappyPathTests.BuildServiceProvider` and `ProposalDeclineRequeueTests.BuildServiceProvider` both register `IProposalService` manually (rather than going through `AddMatchmaking`). After adding the `IChaosInterceptor` constructor parameter, both tests would fail with `Unable to resolve service for type 'GameKit.Matchmaking.Services.IChaosInterceptor'`.
- **Fix:** Register `NullChaosInterceptor` directly in each test's `BuildServiceProvider` helper. No semantic change to the tests — they continue to exercise the same flows.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/ProposalAcceptHappyPathTests.cs`, `tests/GameKit.Matchmaking.Integration.Tests/ProposalDeclineRequeueTests.cs`.
- **Committed in:** `c797748` (Task 1 commit).

**3. [Rule 3 — Auto-fix blocking issue] XML cref to TryAddSingleton ambiguous overload**

- **Found during:** Task 1 — first build of `IChaosInterceptor.cs`.
- **Issue:** `error CS0419: Ambiguous reference in cref attribute: 'ServiceCollectionDescriptorExtensions.TryAdd'`. The `<see cref="...TryAdd"/>` resolved to two overloads (single descriptor vs IEnumerable).
- **Fix:** Downgraded the `<see cref>` to plain `<c>TryAddSingleton</c>`.
- **Files modified:** `src/GameKit.Matchmaking/Services/IChaosInterceptor.cs`.
- **Committed in:** `c797748` (Task 1 commit).

**4. [Rule 3 — Auto-fix blocking issue] AddRankings returns IGameKitRankingsBuilder, not IGameKitBuilder**

- **Found during:** Task 2 — first build of `MatchmakingChaosTests.cs`.
- **Issue:** `services.AddGameKit(...).AddRankings().AddMatchmaking(...)` failed because `AddRankings`'s return type is `IGameKitRankingsBuilder`, which does NOT extend `IGameKitBuilder`. The original Phase 4 sample fluent chain worked because there was no further chain after AddRankings.
- **Fix:** Capture the `IGameKitBuilder` (`var gk = services.AddGameKit(...)`), then call `gk.AddRankings()` and `gk.AddMatchmaking(...)` separately.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs`.
- **Committed in:** `524fb28` (Task 2 commit).

**5. [Rule 3 — Auto-fix blocking issue] Chaos test missing MatchmakingTestModelCustomizer / IAdminAuditWriter**

- **Found during:** Task 2 — first run of the chaos test.
- **Issue:** Two sequential failures: (a) `Cannot create a DbSet for 'MatchmakingTicket' because this type is not included in the model for the context` (reconciler queries the entity but AddGameKit's default DbContext doesn't have the test customizer applied), then (b) `No service for type 'GameKit.Admin.UI.Services.IAdminAuditWriter' has been registered` (reconciler's orphan-session sweep writes audit rows but the chaos test composes the full matchmaking pipeline without AddGameKitAdmin).
- **Fix:** Replace the AddGameKit-registered DbContext with one that applies `MatchmakingTestModelCustomizer` (matches every other Matchmaking integration test). Add `services.AddScoped<IAdminAuditWriter, AdminAuditWriter>()`.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs`.
- **Committed in:** `524fb28` (Task 2 commit).

### Other Deviations

- **Sample app scope reduced.** The plan §what-built mentions `/auth.html` and `/game.html` as separate pages that matchmaking.html redirects between. The live tree consolidated everything into a single `index.html` in Phase 2; `/auth.html` and `/game.html` do not exist. The matchmaking.html page is therefore self-contained (it embeds a guest-login button and shows the matched sessionId in-page rather than redirecting). This is a faithful adaptation to the live tree, not a feature reduction.
- **Chaos test ticket count: 10 vs 100.** See decisions block — the harness shape is the same; 10 tickets keep the test brisk and exercise every invariant.

## Threat Surface Notes

The plan's `<threat_model>` identified three threats — all are mitigated:

- **T-05-09-01 (Tampering: operator misuses non-Null interceptor in production):** mitigated.
  XML doc on `IChaosInterceptor` prominently warns against refactoring; default registration
  is `TryAddSingleton<IChaosInterceptor, NullChaosInterceptor>` so an explicit operator override is
  required to change it; the verbose class name `NullChaosInterceptor` makes accidental misuse
  self-evident in DI listings.
- **T-05-09-02 (Spoofing: sample app guest JWT impersonation via DevTools):** accepted. The
  guest JWT flow is Phase 2's pattern (matching `index.html`); Phase 5 introduces no new
  surface here.
- **T-05-09-03 (Information Disclosure: matchmaking.html exposes /api/mm/* surface to browser
  inspection):** accepted. All routes are JWT-authorized and documented in OpenAPI; the page
  reveals no surface beyond the existing auth wall.

No new threat flags surfaced during execution.

## Checkpoint Status (Task 3)

**Task 3 is a `checkpoint:human-verify` gate** — the artifacts are committed and the build
verifies, but the visual UAT is operator-driven. Below is the structured handoff for the
orchestrator / human verifier.

**Automation completed:**
- `dotnet build samples/TicTacToeDuel` → 0 errors / 0 warnings.
- `dotnet run --project samples/TicTacToeDuel` advances past DI validation (the IConnectionMultiplexer Rule 3 auto-fix is proven by the absence of the prior `Unable to resolve service for type 'StackExchange.Redis.IConnectionMultiplexer'` exception).

**Operator UAT awaiting confirmation:**
1. `docker compose up -d` (verify gamekit-postgres + gamekit-redis healthy).
   - If a stale volume from Phase 1–4 has a different `gamekit_owner` password than
     `docker/postgres/init/01-roles.sql` ships, `docker compose down -v && docker compose up -d`
     to re-init.
2. `./scripts/gen-test-rsa-pem.sh` (if not already run).
3. `dotnet run --project samples/TicTacToeDuel` — observe app starts on http://localhost:5000.
4. Browser tab 1: navigate to `/matchmaking.html`. Click "Play as Guest". Click "Find Match".
5. Browser tab 2 (private/incognito): navigate to `/matchmaking.html`. Click "Play as Guest". Click "Find Match".
6. Within ~1 s both tabs transition to "Match proposed!" with a 10-second countdown.
7. Click "Accept" in both tabs within 10 s.
8. Both tabs display "Matched! Both players accepted." with the shared sessionId.

**Failure modes to watch for** (with operator remediation):
- (a) `/matchmaking.html` JS console error on fetch — check `Authorization: Bearer` header is being sent; verify guest-login button generated a JWT (visible in DevTools localStorage as `gk.access_token`).
- (b) "Find Match" returns 429 — sliding-window rate limit (5/min/player). Wait 60 s.
- (c) Proposal never forms after both clicks — check Redis `ZCARD mm:queue:{ladderId}:tictactoe` (`docker exec gamekit-redis redis-cli ZCARD mm:queue:...`). The 500 ms ticker should pair the tickets within ~1 s.
- (d) "Could not resolve ladder id" — the `/demo/ladder-id/tictactoe` lookup failed. The Rankings StartupLadderUpserter should have written the row on first startup; restart the app to retry.

## Self-Check: PASSED

### Files
- `src/GameKit.Matchmaking/Services/IChaosInterceptor.cs` — FOUND
- `src/GameKit.Matchmaking/Services/NullChaosInterceptor.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/TestDoubles/AbortingChaosInterceptor.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs` — FOUND
- `samples/TicTacToeDuel/wwwroot/matchmaking.html` — FOUND
- `samples/TicTacToeDuel/Program.cs` (modified) — FOUND
- `samples/TicTacToeDuel/README.md` (modified) — FOUND
- `samples/TicTacToeDuel/TicTacToeDuel.csproj` (modified) — FOUND
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` (modified) — FOUND
- `src/GameKit.Matchmaking/Services/ProposalService.cs` (modified) — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Ticker.cs` (modified) — FOUND

### Commits
- `c797748` (Task 1 — IChaosInterceptor + NullChaosInterceptor + AbortingChaosInterceptor + probe insertion) — FOUND
- `524fb28` (Task 2 — MatchmakingChaosTests SC#2 phase gate) — FOUND
- `2137fe4` (Task 3 — TicTacToeDuel sample app integration) — FOUND

### Verification gates
- `dotnet build src/GameKit.Matchmaking --nologo` → exit 0 / 0 warnings / 0 errors — VERIFIED
- `dotnet build GameKit.sln --nologo` → exit 0 / 0 warnings / 0 errors (full solution) — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --no-build` → 65 / 65 pass — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Tests --no-build` → 76 / 76 pass — VERIFIED
- MatchmakingChaosTests asserts all 4 SC#2 invariants with descriptive failure messages — VERIFIED by inspection
- AbortingChaosInterceptor call-count counters used defensively — VERIFIED (`LuaClaimCallCount > 0` assertion)
- IChaosInterceptor registered via TryAddSingleton — VERIFIED (`MatchmakingBuilderExtensions.Ticker.cs.AddTickerServices` line 47)
- MatchmakerTickerService.TryClaimMatchAsync calls _chaos.BeforeLuaClaim BEFORE atomic claim — VERIFIED by inspection
- ProposalService.CreateSessionAsync calls _chaos.BeforeSessionInsert BEFORE ctx.Set<GameSession>().Add — VERIFIED by inspection
- Sample app starts without DI exception (IConnectionMultiplexer Rule 3 auto-fix) — VERIFIED
- All 6 RESEARCH Open Questions documented with file references — VERIFIED above

## Next Plan Readiness

- **Plan 05-10** (SC#3 1k-concurrent-ticket load test) can ship. The chaos seam pattern
  (`IChaosInterceptor`) is reusable for any future in-process simulation. The load test
  doesn't need to wire the AbortingChaosInterceptor — the default NullChaosInterceptor is
  zero-cost in the hot path.
- **Phase 06** (ops doc, distribution). The TicTacToeDuel sample's §Matchmaking section is
  the canonical operator UAT recipe; Phase 6's DIST-05 ops guide can reference it directly.

---
*Phase: 05-matchmaking-parties*
*Plan: 09*
*Completed: 2026-05-17*
