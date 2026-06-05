---
phase: 05
plan: 06
subsystem: matchmaking
tags: [matchmaking, proposal, accept-step, decline, cooldown, lua, wave-3]
dependency_graph:
  requires:
    - phase-05-04 (Strategy + Party CRUD + AtomicClaimScript + placeholder Channel<TicketEvent>)
    - phase-05-02 (DeclineHistory entity + matchmaking migration)
    - phase-05-03 (GameKitMatchmakingCooldownOptions + AcceptTimeoutSeconds)
  provides:
    - src/GameKit.Matchmaking/Services/IDeclineCooldownService.cs (D-08 cooldown contract)
    - src/GameKit.Matchmaking/Services/DeclineCooldownService.cs (escalating ladder impl)
    - src/GameKit.Matchmaking/Services/IDeclineHistoryReader.cs (storage seam — fake / EF)
    - src/GameKit.Matchmaking/Services/EfDeclineHistoryReader.cs (scoped EF-backed reader)
    - src/GameKit.Matchmaking/Services/TeamAssignmentService.cs (party-cohesive CSPRNG split)
    - src/GameKit.Matchmaking/Services/IProposalService.cs (Accept/Decline contract + result enums)
    - src/GameKit.Matchmaking/Services/ProposalService.cs (D-06 accept-step + D-09 re-queue)
    - src/GameKit.Matchmaking/Services/ProposalFields.cs (proposal hash JSON schema)
    - src/GameKit.Matchmaking/Redis/ProposalScripts.cs (Complete + DeclineAndReap Lua sources)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Accept.cs (DI registrations)
  affects:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (calls AddProposalServices())
    - 05-05 (ticker can resolve IProposalService types) / 05-08 (HTTP endpoints map AcceptResult / DeclineResult to status codes)
tech_stack:
  added: []  # no new NuGet pins — StackExchange.Redis + Polly + EF Core already in Directory.Packages.props
  patterns:
    - Atomic Lua SADD+SCARD inside ScriptEvaluateAsync (Pitfall §10 closure)
    - Postgres-first then Redis (decline_history INSERT before Redis teardown — T-05-06-03)
    - In-memory storage seam (IDeclineHistoryReader) + EF-backed default for the same surface
    - Party-cohesive Fisher–Yates shuffle (per-party assignment, members inherit their party's team)
    - ChannelWriter<TicketEvent> TryWrite drop on full (D-15 producer; drain owns the OTel counter)
key_files:
  created:
    - src/GameKit.Matchmaking/Services/IDeclineCooldownService.cs
    - src/GameKit.Matchmaking/Services/DeclineCooldownService.cs
    - src/GameKit.Matchmaking/Services/EfDeclineHistoryReader.cs
    - src/GameKit.Matchmaking/Services/TeamAssignmentService.cs
    - src/GameKit.Matchmaking/Services/IProposalService.cs
    - src/GameKit.Matchmaking/Services/ProposalService.cs
    - src/GameKit.Matchmaking/Services/ProposalFields.cs
    - src/GameKit.Matchmaking/Redis/ProposalScripts.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Accept.cs
    - tests/GameKit.Matchmaking.Tests/Services/DeclineCooldownEscalationTests.cs
    - tests/GameKit.Matchmaking.Tests/Services/TeamAssignmentTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/CooldownEnforcementTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/ProposalAcceptHappyPathTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/ProposalDeclineRequeueTests.cs
  modified:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (calls AddProposalServices())
decisions:
  - "Channel<TicketEvent> payload is the existing Entities.TicketEvent (decided in Plan 05-04). The plan envisioned a separate Services-namespace TicketEvent record; the live tree uses the entity directly because (a) Plan 05-04 registered Channel<Entities.TicketEvent>, (b) Plan 05-07 wired the drain around it, and (c) the entity fields exactly match the plan's intended record shape. ProposalService writes Entities.TicketEvent instances. Documented as Rule 3 deviation."
  - "ProposalScripts holds TWO Lua sources, not one combined script. The accept-and-complete script is invoked from AcceptAsync; the decline-and-reap script from DeclineAsync. Splitting keeps each script ≤20 lines (CompleteLuaSource = 17 / DeclineLuaSource = 16) and gives them independent KEYS layouts."
  - "ProposalFields JSON shape lives in src/GameKit.Matchmaking/Services/. The shape is a stable cross-plan contract: 05-05's ticker (producer) serializes ProposalFields into the proposal hash's 'fields' entry; 05-06's ProposalService (consumer) deserializes the same shape. Single source of truth."
  - "IDeclineHistoryReader storage seam — separates the cooldown arithmetic (deterministic, unit-testable with in-memory fake) from the EF query (integration-tested against real Postgres). The fake reader inside DeclineCooldownEscalationTests is intentionally identical-shape to EfDeclineHistoryReader so the production path goes through the same surface."
  - "TeamAssignmentService uses CSPRNG Fisher–Yates shuffle then alternates assignment 0/1/0/1. Party cohesion is automatic (all members of a single party share a team). For odd party counts (e.g. 3 single-player parties), team 0 gets one more player — this is the simplest correct v1 behaviour and matches the 'random' billing. MMR-balanced split is deferred per CONTEXT §Phase Boundary."
  - "AcceptResult includes both AlreadyAccepted (SADD returned 0 — same ticket accepted twice) AND a COMPLETED case (proposal already state=complete from a prior all-accepted close). Both map to AcceptResult.AlreadyAccepted in C# — the late-accept idempotency window (T-05-06-04) is closed without exposing it to the endpoint."
  - "ProposalService.DeclineAsync writes decline_history FIRST (Postgres durability) then runs the Lua decline-and-reap (Redis teardown + re-ZADD). On a Redis failure, the cooldown row still persists; the accepting partner's ticket is recovered by the reconciler within StaleTicketThresholdMinutes (Plan 05-07). This ordering closes T-05-06-03."
  - "Status pub/sub PUBLISH uses RedisChannel.Literal (not pattern) — Pitfall §5's connection-leak guidance applies to long-poll subscribers (Plan 05-08), not to fire-and-forget producer PUBLISH calls. ProposalService never subscribes; it only publishes."
metrics:
  duration_min: 13
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 14
requirements_completed:
  - MATCH-02  # analytics events via Entities.TicketEvent (Accepted / Declined / TimedOut / Matched / Cancelled emitted by ProposalService)
  - MATCH-04  # Redis source of truth (Lua scripts touch only Redis structures + Postgres ON-RAMP via scoped GameKitDbContext)
  - MATCH-05  # atomic accept-and-complete via Lua SADD+SCARD (closes Pitfall §10)
  - MATCH-09  # party-aware (TeamAssignmentService accepts QueuedParty list — every member of a party shares a team)
---

# Phase 5 Plan 06: Proposal Accept-Step + Decline Cooldown + Team Assignment Summary

**The D-06 proposal lifecycle is closed.** This plan lands the application-service trio that drives the accept-step proposal flow defined by CONTEXT D-06 / D-08 / D-09: `IProposalService` + `ProposalService` (Accept / Decline; atomic Lua SADD+SCARD complete check; decline-and-reap re-ZADD with original queuedAt), `IDeclineCooldownService` + `DeclineCooldownService` (escalating 3 / 15 / 30 min ladder with a 60-min rolling window), and `TeamAssignmentService` (party-cohesive Fisher–Yates split via CSPRNG). The `ProposalFields` JSON shape lives in `Services/` as the stable cross-plan contract between Plan 05-05's ticker producer and this plan's accept/decline consumer. Two Lua scripts (Complete: 17 lines; DeclineAndReap: 16 lines) hold the atomic anchors that close Pitfall §10 (partial-accept race) and T-05-06-04 (late-accept idempotency).

## Performance

- **Duration:** ~13 min
- **Started:** 2026-05-17T15:28:05Z (post-worktree-base-reset)
- **Completed:** 2026-05-17T15:41:42Z
- **Tasks:** 3 (3 executed; all `type="auto" tdd="true"`)
- **Files created:** 14
- **Files modified:** 1 (`MatchmakingBuilderExtensions.cs` — wires `AddProposalServices()`)
- **Test count delta:** +12 unit (76 total in `GameKit.Matchmaking.Tests`, up from 64) and +6 integration (36 total in `GameKit.Matchmaking.Integration.Tests`, up from 30)

## Accomplishments

1. **`IDeclineCooldownService` + `DeclineCooldownService` (Task 1).** D-08 escalating cooldown ladder: 0 declines ⇒ not locked; 1 ⇒ Step1Minutes (3); 2 ⇒ Step2Minutes (15); 3+ ⇒ Step3Minutes (30). Rolling 60-min window. `CooldownStatus(bool IsLocked, TimeSpan? RetryAfter)` record. Uses caller-supplied `now` exclusively — verified by grep (`DateTime.Now` / `DateTime.UtcNow` zero matches in cooldown code).

2. **`IDeclineHistoryReader` storage seam (Task 1).** Separates cooldown arithmetic from the EF query so unit tests use an in-memory fake (`FakeDeclineHistoryReader` inside `DeclineCooldownEscalationTests`) while the production path resolves `EfDeclineHistoryReader`. Both reach the same `GetRecentDeclinesAsync(playerId, since, take, ct)` surface with identical contract — production goes through Postgres `(PlayerId, DeclinedAt DESC)` index from Plan 05-02.

3. **`TeamAssignmentService` (Task 1).** Party-cohesive Fisher–Yates shuffle using `RandomNumberGenerator.GetInt32` (CSPRNG, bias-free). Shuffles party list, then alternates 0/1/0/1 — every member of a party shares its party's team. Stateless singleton; v1 random per CONTEXT §Phase Boundary; MMR-balanced split deferred.

4. **`IProposalService` + `ProposalService` (Task 2).** Implements the D-06 accept-step flow:
   - **AcceptAsync** — HGETALL proposal hash; verify ticket id ∈ `proposal.Tickets` (T-05-06-01); run `ProposalScripts.CompleteLuaSource` (SADD + SCARD + HSET state=complete) atomically; on `COMPLETE`, INSERT `GameSession` + `SessionParticipant` rows with team assignments and PUBLISH "matched:<sessionId>" to every status channel; on `PENDING`, emit `Accepted` ticket event; on `ALREADY` / `COMPLETED`, return `AlreadyAccepted` (T-05-06-04 late-accept idempotency).
   - **DeclineAsync** — Postgres-first: INSERT `decline_history` row BEFORE Redis teardown (T-05-06-03 durability). Then `ProposalScripts.DeclineLuaSource` (re-ZADD accepting partners with original `QueuedAtUnixMs` score; DEL acceptors + proposal hash) atomically. PUBLISH "cancelled" to decliner, "requeued" to accepting partners. Emit `Declined` event for decliner + `Cancelled` events for the others.

5. **`ProposalFields` JSON schema (Task 2).** Stable cross-plan contract — Plan 05-05's ticker is the producer (serializes into `proposal.fields`), Plan 05-06's `ProposalService` is the consumer (deserializes on Accept / Decline). Carries `Tickets[]` (each with `TicketId`, `QueuedAtUnixMs`, `PlayerIds[]`), `LadderId`, `QueueKey`, `Deadline`.

6. **`ProposalScripts` Lua sources (Task 2).** Two scripts held as `const` strings so StackExchange.Redis caches SHA1 on first call and falls back to EVAL on NOSCRIPT automatically.
   - **CompleteLuaSource (17 lines).** KEYS=`[proposalKey, acceptsSetKey]`; ARGV=`[ticketId, expectedCount, ttlSeconds]`. Returns `COMPLETED` / `ALREADY` / `PENDING` / `COMPLETE` — literal bulk strings.
   - **DeclineLuaSource (16 lines).** KEYS=`[proposalKey, acceptsSetKey, queueKey]`; ARGV=`[decliningTicketId, ticketCount, t1, score1, t2, score2, ...]`. Re-ZADDs accepting tickets with their original scores; DELs acceptors + proposal. Returns `OK`.

7. **`MatchmakingBuilderExtensions.Accept.cs` (Task 2).** Partial-class file that registers `TeamAssignmentService` (singleton), `IDeclineHistoryReader → EfDeclineHistoryReader` (scoped), `IDeclineCooldownService → DeclineCooldownService` (scoped), `IProposalService → ProposalService` (scoped) — all via `TryAdd*` for idempotency. Wired into `AddMatchmaking()` between `AddStrategyServices()` (05-04) and `AddBackgroundServices()` (05-07).

8. **3 cooldown integration tests (Task 1).** Drive `DeclineCooldownService` against real Postgres `decline_history`:
   - `Player_With_3_Declines_In_60min_Gets_30min_Cooldown` — seeds 3 rows; asserts `IsLocked=true` and `RetryAfter ≈ 29 min`.
   - `Cooldown_Expires_After_Step3_Duration` — seeds 3 rows where the most recent was 31 min ago; asserts `IsLocked=false`.
   - `Decline_Window_Rolls_Forward` — seeds 4 rows over 80 minutes; asserts only the 3 within the 60-min window count.

9. **3 proposal integration tests (Task 3).** End-to-end against Testcontainer Postgres + Redis:
   - `TwoPlayer_BothAccept_CreatesSession_With_TwoTeams_AndPublishesMatched` — drives both players through `AcceptAsync`; asserts `GameSession.State=Active`, 2 participants on teams 0 and 1, proposal `state=complete`, "matched:<sessionId>" PUBLISHed on both ticket channels, and the 3rd accept returns `AlreadyAccepted`.
   - `PlayerA_Accepts_PlayerB_Declines_Requeues_A_With_OriginalQueuedAt` — D-09 — Player A's ticket re-ZADDed with the verbatim original `QueuedAtUnixMs` score; Player B's ticket NOT in queue; 1 `decline_history` row; proposal + acceptors keys deleted; "requeued" / "cancelled" PUBLISHed appropriately.
   - `Decline_With_NotInProposal_TicketId_Returns_NotInProposal` — T-05-06-01 spoofing guard — `DeclineAsync` with a ticket id ∉ proposal returns `NotInProposal` and writes NO `decline_history` row.

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | DeclineCooldownService (D-08) + TeamAssignmentService + IDeclineHistoryReader + 12 unit + 3 integration tests | `f00f4ed` | feat |
| 2 | ProposalService (D-06 + D-09) + ProposalScripts + ProposalFields + builder wiring | `47dd284` | feat |
| 3 | ProposalAcceptHappyPath + ProposalDeclineRequeue integration tests | `83d4566` | test |

**Plan metadata commit:** will be made by the executor after this SUMMARY is written (worktree mode — SUMMARY commit only).

## Verification Evidence

- `dotnet build src/GameKit.Matchmaking --nologo` → **0 warnings, 0 errors**.
- `dotnet test tests/GameKit.Matchmaking.Tests --filter "DeclineCooldownEscalation|TeamAssignment" --no-build` → **12 passed / 0 failed** (7 cooldown + 5 team).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter "CooldownEnforcement|ProposalAcceptHappyPath|ProposalDeclineRequeue" --no-build` → **6 passed / 0 failed**.
- `dotnet test tests/GameKit.Matchmaking.Tests --no-build` → **76 passed / 0 failed** (no regressions).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --no-build` → **36 passed / 0 failed** (no regressions).
- **Plan minimum:** ≥11 unit (we have 12) + ≥5 integration (we have 6) — **exceeded**.
- **UTC discipline (Pitfall §4):** `grep -nE 'DateTime\.(Now|UtcNow)' src/GameKit.Matchmaking/Services/DeclineCooldownService.cs src/GameKit.Matchmaking/Services/IDeclineCooldownService.cs src/GameKit.Matchmaking/Services/EfDeclineHistoryReader.cs src/GameKit.Matchmaking/Services/TeamAssignmentService.cs` returns **zero matches**.
- **Lua line counts:** `CompleteLuaSource` = 17 / `DeclineLuaSource` = 16 — both well under the 20-line target (and the broader 30-line cap inherited from Plan 05-04).
- **Lua uses SADD+SCARD (not KEYS):** verified by inspection — both scripts use only O(1) `HGET` / `HSET` / `SADD` / `SCARD` / `SISMEMBER` / `ZADD` / `DEL` calls. No `KEYS` pattern walks.
- **D-09 queuedAt preservation:** verified by `ProposalDeclineRequeueTests.PlayerA_Accepts_PlayerB_Declines_Requeues_A_With_OriginalQueuedAt` — sets the original queuedAt 47 seconds in the past, then asserts `ZSCORE queue ticketA` returns the same Unix-ms score verbatim.
- **T-05-06-01 spoofing guard:** verified by `ProposalDeclineRequeueTests.Decline_With_NotInProposal_TicketId_Returns_NotInProposal` — a non-member ticket id returns `NotInProposal` and writes zero `decline_history` rows.

## Decisions Made

(All decisions are also captured in the YAML frontmatter `decisions:` block; this section restates the design intent for human reviewers.)

- **Channel payload uses `Entities.TicketEvent` (not a separate Services-namespace record).** The plan envisioned a small `TicketEvent` record in `Services/`, but Plan 05-04 registered `Channel<Entities.TicketEvent>` and Plan 05-07 wired the drain around the entity directly. The entity's fields (`Id`, `TicketId`, `EventType`, `OccurredAt`, `Payload`) match the plan's intended shape exactly — adding a parallel record would force a converter and break the existing wiring. `ProposalService` writes `Entities.TicketEvent` instances; the drain INSERTs them verbatim.
- **Two separate Lua scripts (Complete + DeclineAndReap)** rather than one combined script. Each script has a distinct KEYS layout (Complete: `[proposalKey, acceptsSetKey]`; Decline: `[proposalKey, acceptsSetKey, queueKey]`) and a distinct purpose. Splitting keeps each well under 20 lines and gives them independent EVALSHA caches.
- **`ProposalFields` JSON shape lives in `Services/`** (not `Redis/`). It's a stable cross-plan contract — the ticker (05-05, producer) serializes the same shape the proposal service (05-06, consumer) deserializes. Putting the shape in the consumer's namespace lets future producers reference it without an awkward `Redis.` qualifier.
- **`IDeclineHistoryReader` storage seam.** Separates the cooldown arithmetic (deterministic, fully unit-testable with an in-memory fake) from the EF query (integration-tested against real Postgres). The two readers reach the same surface — unit tests can pin the rolling-window arithmetic without spinning up a Postgres container, and integration tests verify the EF query against the Plan 05-02 `(PlayerId, DeclinedAt DESC)` index.
- **`TeamAssignmentService` algorithm: Fisher–Yates shuffle then alternate 0/1.** Party cohesion is automatic (all members of one party share its party-level index). For odd party counts, team 0 gets one more player — simplest correct v1 behaviour; matches "random" billing. The service is stateless and singleton-safe.
- **`AcceptResult` collapses two "already" states into one.** The Lua script can return `ALREADY` (SADD returned 0 — same ticket accepted twice) or `COMPLETED` (proposal state was already `complete`). Both map to `AcceptResult.AlreadyAccepted` in C# — the late-accept idempotency window (T-05-06-04) is closed without exposing the distinction to the endpoint.
- **Decline ordering: Postgres-first, Redis-second.** `INSERT decline_history` runs BEFORE the Lua decline-and-reap so the cooldown row persists even if Redis fails — closes T-05-06-03. On Redis failure, the accepting partner's ticket is recovered by the reconciler (Plan 05-07) within `StaleTicketThresholdMinutes`; the decline_history's effect on the decliner's cooldown is unaffected.
- **`RedisChannel.Literal` for PUBLISH calls.** Pitfall §5's connection-leak concern applies to long-poll subscribers (Plan 05-08); ProposalService never subscribes — it only publishes. Using `Literal` (not `Pattern`) is the correct choice for an exact-channel PUBLISH.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Auto-fix blocking issue] Channel<TicketEvent> binds to `Entities.TicketEvent`, not a new `Services/TicketEvent.cs` record**

- **Found during:** Task 1 design phase (initial read of `MatchmakingBuilderExtensions.Strategy.cs` and `MatchmakingAnalyticsDrainService.cs`).
- **Issue:** The plan task 1 explicitly calls for creating a `src/GameKit.Matchmaking/Services/TicketEvent.cs` record with fields `(TicketId, EventType, OccurredAt, Payload)`. However Plan 05-04 already registered `Channel<GameKit.Matchmaking.Entities.TicketEvent>` (and writer/reader singletons) inside `AddStrategyServices()`, and Plan 05-07 wired `MatchmakingAnalyticsDrainService` to read from `ChannelReader<Entities.TicketEvent>` and INSERT the entities directly. Creating a parallel `Services.TicketEvent` record would force either (a) duplicate registrations (breaking 05-07) or (b) a converter from the new record to the entity (adding allocation + cognitive overhead).
- **Fix:** Use the existing `Entities.TicketEvent` as the channel payload. `ProposalService.EmitTicketEvent` constructs entity instances with `Id = _ids.NewId()`, `OccurredAt = _clock.UtcNow`, etc. — the drain service persists them verbatim. The entity's field surface (`Id`, `TicketId`, `EventType`, `OccurredAt`, `Payload`) is a strict superset of the plan's intended record (the only addition is `Id`, which we need anyway for the Postgres write).
- **Files modified:** None (decision documented in `ProposalService` XML doc + this SUMMARY).
- **Verification:** All ChannelWriter / ChannelReader resolutions across `ProposalService`, `MatchmakingAnalyticsDrainService`, `MatchmakingBuilderExtensions.Strategy.cs`, and `MatchmakingBuilderExtensions.Background.cs` use `Entities.TicketEvent`. No type conflicts. The 3 integration tests verify the events emitted by `ProposalService` flow through the channel cleanly.
- **Committed in:** `47dd284` (Task 2 commit).

**2. [Rule 3 — Auto-fix blocking issue] Test harness writes the proposal hash directly to Redis (Plan 05-05's ticker has not shipped)**

- **Found during:** Task 3 design phase.
- **Issue:** The plan task 3 says "manually invoke MatchmakerTickerService.RunOnceAsync ... calls `IMatchmakerTicker.RunOnceAsync(ct)` (the testable inner-loop interface from Plan 05-05)". But Plan 05-05 is running in parallel in the same wave (Wave 3) — `IMatchmakerTicker` does not exist in the codebase yet. Waiting for 05-05 would block Wave 3 parallelism.
- **Fix:** The two integration tests seed the proposal hash directly via `db.HashSetAsync(proposalKey, "fields", JsonSerializer.Serialize(fields))` — simulating exactly what 05-05's ticker would write through `AtomicClaimScript`. The proposal's TTL is set via `db.KeyExpireAsync`. This isolates Plan 05-06's correctness from Plan 05-05's shipping order.
- **Files modified:** `ProposalAcceptHappyPathTests.cs` and `ProposalDeclineRequeueTests.cs` — both seed the proposal hash directly.
- **Verification:** Both tests pass against the real Redis + Postgres Testcontainer pair. The `ProposalFields` JSON shape they seed is identical to the shape 05-05's ticker will write (single source of truth via the `ProposalFields` class).
- **Committed in:** `83d4566` (Task 3 commit).

**3. [Rule 3 — Auto-fix blocking issue] xUnit1031 — replaced blocking `Task.Result` with awaited `await tcs.Task`**

- **Found during:** Task 3 build verification of `ProposalAcceptHappyPathTests` / `ProposalDeclineRequeueTests`.
- **Issue:** Initial implementation used `tcs.Task.Result` after a `Task.WhenAny` race to extract the published message. xUnit1031 analyzer flagged this as a deadlock risk (treated as error in this project's `WarningsAsErrors` config).
- **Fix:** After confirming the TCS task completed (via `WhenAny` returning the original task), the value is extracted with `await tcs.Task` — non-blocking and analyzer-compliant.
- **Files modified:** Both proposal integration test files.
- **Verification:** `dotnet build tests/GameKit.Matchmaking.Integration.Tests` exits 0 with zero warnings.
- **Committed in:** `83d4566` (Task 3 commit).

**4. [Rule 3 — Auto-fix blocking issue] `SessionParticipant.PlayerId` is `Guid?` — added null-coalesce in test assertion**

- **Found during:** Task 3 build verification of `ProposalAcceptHappyPathTests`.
- **Issue:** `SessionParticipant.PlayerId` is `Guid?` (nullable for GDPR tombstone scenarios). The original test code attempted to sort a `Guid?` list as if it were `Guid` — CS1503.
- **Fix:** Project the participants list to `p.PlayerId!.Value` before sorting. Safe in this test because the freshly-created participants have non-null player ids; for GDPR scenarios, those rows are tombstoned post-creation and not exercised by this test.
- **Files modified:** `ProposalAcceptHappyPathTests.cs`.
- **Verification:** `dotnet build` exits 0.
- **Committed in:** `83d4566` (Task 3 commit).

### Other Deviations

None. The plan's `<action>` / `<behavior>` sections matched the codebase patterns exactly; the only unplanned work was the four auto-fixes above.

## Threat Surface Notes

The plan's `<threat_model>` identified 5 STRIDE threats — all are now mitigated by the implementation:

- **T-05-06-01 (Spoofing: player accepts a proposal they are not in):** mitigated. Both `ProposalService.AcceptAsync` and `DeclineAsync` HGETALL the proposal, deserialize `ProposalFields`, and check `fields.Tickets.Any(t => t.TicketId == ticketId)` before any Redis or Postgres write. On miss, they return `NotInProposal`. Verified programmatically by `ProposalDeclineRequeueTests.Decline_With_NotInProposal_TicketId_Returns_NotInProposal`.
- **T-05-06-02 (Tampering: late accept races against proposal sweeper):** mitigated. The Lua complete-script's `SADD` + `SCARD` + `HSET state=complete` is atomic. Either (a) the accept's SADD lands before any external delete and the script transitions state to complete, or (b) the proposal hash has already been deleted (by sweeper / decline) and HGETALL returns empty — `AcceptResult.ProposalNotFound`. No partial state.
- **T-05-06-03 (Tampering: decline writes DeclineHistory but Redis re-ZADD fails):** mitigated by ordering — INSERT `decline_history` runs FIRST (durable), then the Lua decline-and-reap. On Redis failure, the cooldown row persists; the accepting partner's ticket is recovered by the reconciler (Plan 05-07) within `StaleTicketThresholdMinutes`. The decliner's cooldown takes effect regardless.
- **T-05-06-04 (Tampering: player accepts after game_session already created):** mitigated. After the Lua complete-script flips `state=complete`, a late `AcceptAsync` call observes `state=complete` in HGETALL and the script returns `COMPLETED` (or `ALREADY` if their SADD was already a member); the C# layer maps both to `AcceptResult.AlreadyAccepted`. No second `GameSession` row is created. Documented in `IProposalService` XML doc.
- **T-05-06-05 (Information Disclosure: time-based cooldown leak via DateTime.Now):** mitigated. All cooldown time math uses caller-supplied `now` (sourced from `IClock.UtcNow` in production); the production `EfDeclineHistoryReader` and unit-test `FakeDeclineHistoryReader` never touch `DateTime.Now`/`DateTime.UtcNow`. Verified by grep across all four cooldown source files — zero matches.

No new threat flags surfaced during execution. No new network endpoints / auth paths / file access patterns / schema changes were introduced beyond those already cleared by Plan 05-02 / 05-04.

## Self-Check: PASSED

### Files
- `src/GameKit.Matchmaking/Services/IDeclineCooldownService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/DeclineCooldownService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/EfDeclineHistoryReader.cs` — FOUND
- `src/GameKit.Matchmaking/Services/TeamAssignmentService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IProposalService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/ProposalService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/ProposalFields.cs` — FOUND
- `src/GameKit.Matchmaking/Redis/ProposalScripts.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Accept.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Services/DeclineCooldownEscalationTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Services/TeamAssignmentTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/CooldownEnforcementTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/ProposalAcceptHappyPathTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/ProposalDeclineRequeueTests.cs` — FOUND

### Commits
- `f00f4ed` (Task 1 — DeclineCooldownService + TeamAssignmentService + IDeclineHistoryReader + 12 unit + 3 integration tests) — FOUND
- `47dd284` (Task 2 — ProposalService + ProposalScripts + ProposalFields + builder wiring) — FOUND
- `83d4566` (Task 3 — ProposalAcceptHappyPath + ProposalDeclineRequeue integration tests) — FOUND

### Verification gates
- `dotnet build src/GameKit.Matchmaking --nologo` → exit 0 / 0 warnings / 0 errors — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Tests --no-build` → 76 passed / 0 failed — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --no-build` → 36 passed / 0 failed — VERIFIED
- 12 cooldown + team unit tests pass (plan min: 11) — VERIFIED
- 6 cooldown + accept + decline integration tests pass (plan min: 5) — VERIFIED
- CompleteLuaSource line count = 17, DeclineLuaSource = 16 — both under 20 lines — VERIFIED
- Lua scripts use only O(1) commands (SADD / SCARD / HSET / ZADD / DEL); zero `KEYS` pattern walks — VERIFIED by inspection
- ProposalService.AcceptAsync verifies `ticketId ∈ proposal.Tickets` before SADD (T-05-06-01) — VERIFIED in code + integration test
- ProposalService.DeclineAsync writes `decline_history` BEFORE Redis teardown (T-05-06-03) — VERIFIED in code (Step 3 before Step 4) + integration test
- D-09 queuedAt preservation — VERIFIED by `PlayerA_Accepts_PlayerB_Declines_Requeues_A_With_OriginalQueuedAt` ZSCORE assertion (exact-equality on the original Unix-ms value)
- Zero `DateTime.Now`/`DateTime.UtcNow` in cooldown service files — VERIFIED (grep returns no matches)

## Next Plan Readiness

- **05-05** (MatchmakerTickerService): can ship. The ticker's accept-flow types (`IProposalService`, `AcceptResult`, `DeclineResult`, `ProposalFields`) all resolve from DI. The ticker is expected to serialize `ProposalFields` JSON into the proposal hash exactly as this plan's `ProposalService` consumes it.
- **05-08** (HTTP endpoints): can ship. The endpoint layer can directly map `AcceptResult.Accepted` / `AllAccepted` / `AlreadyAccepted` / `ProposalNotFound` / `NotInProposal` to HTTP 200 / 200 / 200 / 404 / 403, and `DeclineResult.Declined` / `ProposalNotFound` / `NotInProposal` to 200 / 404 / 403. `IDeclineCooldownService.GetCurrentCooldownAsync` returns a `CooldownStatus` with `RetryAfter` — the endpoint maps `IsLocked=true` to 403 `ProhibitedDuringCooldown` with `Retry-After: {seconds}` header.

---
*Phase: 05-matchmaking-parties*
*Plan: 06*
*Completed: 2026-05-17*
