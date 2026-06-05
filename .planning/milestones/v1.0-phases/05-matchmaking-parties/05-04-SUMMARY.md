---
phase: 05
plan: 04
subsystem: matchmaking
tags: [matchmaking, strategy, party-service, lua, atomic-claim, scrutor, serializable, wave-2]
dependency_graph:
  requires:
    - phase-05-01 (Wave-0 test scaffolding)
    - phase-05-02 (data layer + Party/PartyMember entities + citext party_code)
    - phase-05-03 (options + builder + Redis-key constants + Scrutor TODO marker)
  provides:
    - src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs (pluggable strategy contract; MATCH-09)
    - src/GameKit.Matchmaking/Strategy/QueuedParty.cs (per-tick strategy input record)
    - src/GameKit.Matchmaking/Strategy/MatchResult.cs (proposal id + matched tickets + team map)
    - src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs (default; MATCH-10)
    - src/GameKit.Matchmaking/Strategy/PartyRatingAggregatorService.cs (Mean/Max/GlickoWeighted)
    - src/GameKit.Matchmaking/Services/IPartyCodeGenerator.cs + PartyCodeGenerator.cs (Crockford base32, CSPRNG)
    - src/GameKit.Matchmaking/Services/IPartyService.cs + PartyService.cs (SERIALIZABLE; MATCH-03)
    - src/GameKit.Matchmaking/Services/PartyServiceExceptions.cs (Conflict / InvalidState / Authorization)
    - src/GameKit.Matchmaking/Services/SerializationFailureRetry.cs (Polly v8 40001 retry)
    - src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs (Lua + EVALSHA; MATCH-04 / MATCH-05)
    - src/GameKit.Matchmaking/Redis/AtomicClaimResult.cs (Success / LeaseLost / TicketGone / RedisError)
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs (Scrutor scan + channel placeholder)
  affects:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (closes Plan 05-03's Scrutor TODO)
    - src/GameKit.Matchmaking/GameKit.Matchmaking.csproj (adds Polly + StackExchange.Redis package refs)
    - 05-05..05-08 (downstream plans now resolve strategy + IPartyService + ChannelWriter<TicketEvent> from DI)
tech_stack:
  added: []  # zero new NuGet pins — Polly 8.5.2 + StackExchange.Redis 2.8.41 already in Directory.Packages.props
  patterns:
    - IMatchmakingStrategy mirrors IRankingAlgorithm (Phase 4) — pluggable strategy registered by Scrutor as singleton
    - SERIALIZABLE transaction + Polly 40001 retry pattern (Phase 4 EndSeasonService precedent)
    - Crockford base32 + per-char CSPRNG GetInt32 (bias-free random codes)
    - Lua atomic-claim with fencing-token check FIRST (Pitfall §2; RESEARCH §Decision 3)
    - StackExchange.Redis ScriptEvaluateAsync auto-caches SHA1 + handles NOSCRIPT fallback to EVAL
    - Placeholder Channel<TicketEvent> + writer/reader singletons (Plan 05-07 will services.Replace())
    - Partial-class extension pattern (MatchmakingBuilderExtensions + .Strategy)
key_files:
  created:
    - src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs
    - src/GameKit.Matchmaking/Strategy/QueuedParty.cs
    - src/GameKit.Matchmaking/Strategy/MatchResult.cs
    - src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs
    - src/GameKit.Matchmaking/Strategy/PartyRatingAggregatorService.cs
    - src/GameKit.Matchmaking/Services/IPartyCodeGenerator.cs
    - src/GameKit.Matchmaking/Services/PartyCodeGenerator.cs
    - src/GameKit.Matchmaking/Services/IPartyService.cs
    - src/GameKit.Matchmaking/Services/PartyService.cs
    - src/GameKit.Matchmaking/Services/PartyServiceExceptions.cs
    - src/GameKit.Matchmaking/Services/SerializationFailureRetry.cs
    - src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs
    - src/GameKit.Matchmaking/Redis/AtomicClaimResult.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs
    - tests/GameKit.Matchmaking.Tests/Strategy/BracketFlexMathTests.cs
    - tests/GameKit.Matchmaking.Tests/Strategy/GlickoWeightedAggregatorTests.cs
    - tests/GameKit.Matchmaking.Tests/Strategy/EloRangeStrategyTests.cs
    - tests/GameKit.Matchmaking.Tests/Services/PartyCodeGenerationTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/PartyServiceTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/AtomicClaimScriptTests.cs
  modified:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (Scrutor TODO closed; calls AddStrategyServices())
    - src/GameKit.Matchmaking/GameKit.Matchmaking.csproj (added Polly + StackExchange.Redis package refs)
    - tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs (added Redis-only collection)
decisions:
  - "EloRangeMatchmakingStrategy.Bracket is a public static method (not private) so unit tests (BracketFlexMathTests) can exercise the formula directly without DI. Returns double — the formula yields fractional rating-point widths between integer seconds; rounding for display is the caller's concern."
  - "Per-char RandomNumberGenerator.GetInt32(0, 30) chosen over byte-buffer-modulo for PartyCodeGenerator (eliminates the 16/30 modulo bias entirely at the cost of ~6 RNG calls per code). Documented in PartyCodeGenerator XML doc; T-05-04-04 mitigation."
  - "PartyService catches DbUpdateException with PostgresException SqlState 23505 + ConstraintName containing 'party_code' (case-insensitive) for code-collision retry. Composite-UNIQUE membership race uses ConstraintName containing 'party_member' OR 'PartyId_PlayerId' so the test database's actual constraint name (whichever Plan 05-02 generated) matches."
  - "Three typed exceptions for PartyService — PartyConflictException (409), PartyInvalidStateException (400/409), PartyAuthorizationException (403). All carry a stable string Code for the endpoint layer (Plan 05-08) to map directly to response bodies. Mirrors Auth's UnauthorizedException shape but lives in Matchmaking namespace to avoid an Auth runtime dependency."
  - "AtomicClaimScript Lua source is 18 non-blank lines / 15 non-comment lines — well under the 30-line cap from Plan 05-04 must_haves. Fencing-token check IS the first non-comment line, guarding Pitfall §2 (stale-leader race). Return values are literal bulk strings ('OK' / 'LEASE_LOST' / 'TICKET_GONE') rather than Lua tables — simpler to parse, harder to corrupt."
  - "AtomicClaimScript exposes Sha1Hex as a public static readonly string for diagnostics. The EVALSHA fast-path test verifies SCRIPT EXISTS via raw ExecuteAsync('SCRIPT', 'EXISTS', sha) — StackExchange.Redis's IServer.ScriptExistsAsync method had an interaction with our SHA format that produced false negatives, so we use raw command execution instead. The precomputed C# SHA1 matches Redis's server-side SCRIPT LOAD return exactly (asserted)."
  - "Channel<TicketEvent> registration is a deliberate PLACEHOLDER (capacity 1000, DropNewest, SingleReader=true, SingleWriter=false) per Plan 05-04 must_haves line 47. Plan 05-07's AddBackgroundServices() will services.Replace() the singleton + writer + reader with the options-driven instance (capacity from GameKitMatchmakingOptions.Analytics.ChannelCapacity, default 10000 per CONTEXT D-15). This pre-registration lets Wave 3 plans 05-05 (ticker) + 05-06 (proposal service) consume ChannelWriter<TicketEvent> from DI without a compile-order dependency on 05-07."
  - "MatchmakingBuilderExtensions.Strategy.cs uses `publicOnly: false` on the Scrutor scan — EloRangeMatchmakingStrategy is public sealed today, but a future internal sealed strategy in this assembly must also be picked up. Customer-authored strategies in a separate assembly register manually via services.AddSingleton<IMatchmakingStrategy, MyStrategy>() BEFORE AddMatchmaking(); Scrutor dedups by service+impl pair."
metrics:
  duration_min: 21
  completed_date: "2026-05-17"
  task_count: 3
  file_count: 23
requirements_completed:
  - MATCH-03  # party_members 1-N enforced application-side via SERIALIZABLE tx (RESEARCH §OQ-2-RESOLVED)
  - MATCH-04  # Redis source of truth (Lua atomic-claim serializes write-set within a tick)
  - MATCH-05  # Atomic claim via Lua + fencing-token check (Pitfall §2 closure)
  - MATCH-09  # Pluggable IMatchmakingStrategy contract + Scrutor discovery (Plan 05-03 TODO closed)
  - MATCH-10  # Default EloRangeMatchmakingStrategy with bracket flex + per-aggregator switch
---

# Phase 5 Plan 04: Matchmaking Strategy + Party CRUD + Lua Atomic-Claim Summary

**The correctness core of Phase 5 is landed.** This plan ships the pluggable `IMatchmakingStrategy` contract and its default `EloRangeMatchmakingStrategy` (linear bracket flex + symmetric-overlap rule + oldest-waiter-first), the `PartyRatingAggregatorService` Mean / Max / GlickoWeighted switch, the SERIALIZABLE-transaction-backed `PartyService` (case-insensitive citext join, single-active-party enforcement, Polly 40001 retry), the `PartyCodeGenerator` (Crockford base32 + per-char CSPRNG), and the atomic-claim `AtomicClaimScript` whose 18-line Lua source carries the fencing-token check as its first non-comment statement. Plan 05-03's deferred Scrutor scan TODO is closed inside `MatchmakingBuilderExtensions.Strategy.cs`, which also pre-registers the placeholder bounded `Channel<TicketEvent>` + writer + reader singletons so Wave 3 plans 05-05 (ticker) and 05-06 (proposal service) can resolve `ChannelWriter<TicketEvent>` from DI without depending on Plan 05-07 having shipped first.

## Performance

- **Duration:** ~21 min
- **Started:** 2026-05-17T05:56:49Z (post-worktree-base-reset)
- **Completed:** 2026-05-17T06:17:35Z
- **Tasks:** 3 (3 executed; all `type="auto" tdd="true"`)
- **Files created:** 20
- **Files modified:** 3 (MatchmakingBuilderExtensions.cs / GameKit.Matchmaking.csproj / CollectionDefinitions.cs)
- **Test count delta:** +37 unit (61 total in `GameKit.Matchmaking.Tests`, up from 24) and +18 integration (20 total in `GameKit.Matchmaking.Integration.Tests`, up from 2)

## Accomplishments

1. **IMatchmakingStrategy + QueuedParty + MatchResult contract (Task 1).** Pluggable strategy interface modelled on `IRankingAlgorithm` (Phase 4): a single `Name` property + `Match(candidate, pool, now)` method. The class-level XML doc enforces statelessness, thread-safety, and determinism with one documented exception (CSPRNG-sourced random team assignment). The records carry the cached `AggregateRating` (computed at enqueue per RESEARCH §Decision 5) so the matcher reads it instead of recomputing each tick.

2. **EloRangeMatchmakingStrategy default impl (Task 1).** Implements the RESEARCH §Decision 4 formula `Bracket(cfg, t) = min(BracketStart + (BracketEnd − BracketStart) · t / BracketRampSeconds, BracketEnd)` exactly, with the symmetric-overlap (conjunctive) match rule `|rA − rB| ≤ bA AND |rA − rB| ≤ bB`. The pool iteration is **oldest-waiter-first** (re-sorted defensively by `QueuedAt`) — Pitfall §6 closed. Random team assignment uses `RandomNumberGenerator.GetInt32(0, 2)` per player. The `MaxPartyRatingSpread` cap is checked defensively for both candidate and pool entries (CONTEXT D-14).

3. **PartyRatingAggregatorService (Task 1).** Pure stateless helper with the three CONTEXT D-13 modes: `Mean` (arithmetic mean), `Max` (highest rating), `GlickoWeighted` (`Σ rating · (1/RD²) / Σ (1/RD²)`). On all-zero RD inputs the GlickoWeighted path falls back to arithmetic mean.

4. **PartyCodeGenerator + IPartyCodeGenerator (Task 2).** 30-character Crockford base32 alphabet `ABCDEFGHJKMNPQRSTVWXYZ23456789` (no `I/L/O/0/1` per RESEARCH §Don't Hand-Roll). Per-character `RandomNumberGenerator.GetInt32(0, 30)` — bias-free (CSPRNG with rejection sampling). 6-char default, supports 4..16. Mitigates T-05-04-04 (predictable code via `System.Random`).

5. **PartyService SERIALIZABLE party CRUD (Task 2).** Three operations under `IsolationLevel.Serializable` + Polly retry on Postgres 40001 serialization failure (3 attempts, exponential backoff + jitter):
   - **CreateAsync** — active-membership guard, code-collision retry loop (up to 5 attempts on 23505 with party_code constraint), atomic Party + PartyMember insert. Throws `PartyConflictException` on duplicate active membership or code-exhaustion.
   - **JoinAsync** — citext-aware lookup (NO `ToUpperInvariant()` — column type does the work, Pitfall §9 closed), state guard (must be `Open`), active-membership guard for the joiner, idempotent on composite-UNIQUE race.
   - **DissolveAsync** — owner check (throws `PartyAuthorizationException` on mismatch), state transition `Open/Queueing/InMatch → Dissolved`, member rows retained (audit).
   - **GetByCodeAsync** — case-insensitive citext lookup, no state filter (callers check `State` themselves).

6. **Three typed exceptions (Task 2).** `PartyConflictException` (409), `PartyInvalidStateException` (400/409), `PartyAuthorizationException` (403). Each carries a stable `Code` string for endpoint mapping in Plan 05-08.

7. **AtomicClaimScript (Task 3).** **18-line Lua source** (under the 30-line cap), fencing-token check IS the first non-comment line, three literal-string return values (`OK` / `LEASE_LOST` / `TICKET_GONE`). KEYS layout `[leaseKey, queueKey, proposalKey, ticket1, ticket2, ...]`, ARGV layout `[leaseValue, proposalId, ttlSeconds, ticketCount, tid1, tid2, ..., fieldsJson]`. StackExchange.Redis's `ScriptEvaluateAsync` handles SHA1 caching + NOSCRIPT fallback automatically. Precomputed `Sha1Hex = "b31a7825cbbe43420f357f8d3ebaf81bf1fd0d56"` exposed for diagnostics; matches Redis's server-side SCRIPT LOAD output.

8. **AtomicClaimResult enum (Task 3).** Four-state result: `Success` / `LeaseLost` (fencing-token mismatch — Pitfall §2 guard) / `TicketGone` (another claim won the race) / `RedisError` (connection/timeout — caller retries at the call-site).

9. **MatchmakingBuilderExtensions.Strategy.cs (Task 3).** Partial-class file that:
   - Runs the Scrutor scan `services.Scan(s => s.FromAssemblyOf<EloRangeMatchmakingStrategy>().AddClasses(c => c.AssignableTo<IMatchmakingStrategy>(), publicOnly: false).AsImplementedInterfaces().WithSingletonLifetime())` — picks up `EloRangeMatchmakingStrategy` today plus any operator-authored strategies registered before `AddMatchmaking()`.
   - Registers `AtomicClaimScript`, `PartyRatingAggregatorService`, `PartyCodeGenerator` (singletons) and `IPartyService → PartyService` (scoped).
   - Pre-registers a **placeholder** bounded `Channel<TicketEvent>` (capacity 1000, `DropNewest`, `SingleReader=true`, `SingleWriter=false`) + derived `ChannelWriter<TicketEvent>` + `ChannelReader<TicketEvent>` singletons. Plan 05-07's `AddBackgroundServices()` will `services.Replace(...)` these with the options-driven instance (capacity 10000 default per CONTEXT D-15).

   `MatchmakingBuilderExtensions.AddMatchmaking` now calls `builder.Services.AddStrategyServices()` — closes the `TODO(05-04)` comment Plan 05-03 left behind.

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | IMatchmakingStrategy + EloRangeMatchmakingStrategy + PartyRatingAggregator + 25 unit tests | `734436b` | feat |
| 2 | PartyCodeGenerator + IPartyService + PartyService (SERIALIZABLE, citext) + 12 unit + 10 integration tests | `66cda73` | feat |
| 3 | AtomicClaimScript (Lua + EVALSHA) + Scrutor scan + Channel placeholder + 7 integration tests | `b5722ef` | feat |

**Plan metadata commit:** will be made by the executor after this SUMMARY is written (worktree mode — SUMMARY commit only).

## Verification Evidence

- `dotnet build src/GameKit.Matchmaking --nologo` → **0 warnings, 0 errors**.
- `dotnet test tests/GameKit.Matchmaking.Tests --nologo` → **61 passed / 0 failed** (24 from prior plans + 25 strategy + 12 party-code).
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --nologo` → **20 passed / 0 failed** (advisory + migration determinism + 7 AtomicClaimScript + 10 PartyService + 1 builder Channel smoke).
- Lua source line count: 18 non-blank / 15 non-comment — asserted programmatically by `LuaSource_Is_Under_30_Lines` test.
- First Lua step is the fencing-token check — asserted programmatically by `LuaSource_First_Step_Is_Fencing_Token_Check` test.
- EVALSHA fast-path: `SCRIPT EXISTS sha` returns 1 after the first `ScriptEvaluateAsync` call (verified via raw `ExecuteAsync("SCRIPT", "EXISTS", sha)`).
- Channel placeholder resolves from DI: `sp.GetRequiredService<ChannelWriter<TicketEvent>>()` returns the channel's writer; `sp.GetRequiredService<ChannelReader<TicketEvent>>()` returns the channel's reader (verified by `Channel_Placeholder_Resolves_From_DI_After_AddMatchmaking` test).
- Case-insensitive party-code join: code `"K7Q3M2"` is joinable via `"k7q3m2"` (verified by `JoinAsync_Is_Case_Insensitive_Via_Citext` integration test).
- Concurrent-join race: 2 parallel `JoinAsync` calls by 2 different players → both succeed, 3 final members; 2 parallel `CreateAsync` calls by 1 player → exactly 1 succeeds (verified by the two concurrent-race integration tests).

## Decisions Made

(All decisions are also captured in the YAML frontmatter `decisions:` block; this section restates the design intent for human reviewers.)

- **`Bracket` is a public static method** on `EloRangeMatchmakingStrategy` so the 8 `BracketFlexMathTests` can exercise it directly without DI. Returns `double` (the formula yields fractional rating-point widths between integer seconds; rounding for display is the caller's concern).
- **Per-char `GetInt32`** chosen over byte-buffer-modulo for party codes — eliminates the 256 % 30 = 16 modulo bias entirely at the cost of ~6 RNG calls per code. Documented in `PartyCodeGenerator` XML doc; T-05-04-04 (predictable code) mitigated.
- **Three typed exceptions for `PartyService`**, each carrying a stable string `Code` for endpoint mapping (Plan 05-08). Mirrors Auth's `UnauthorizedException` shape but lives in Matchmaking namespace to avoid an Auth runtime dependency.
- **Lua script returns literal bulk strings**, not Lua tables. Simpler to parse, harder to corrupt; the `AtomicClaimResult` enum has exactly three real states (plus `RedisError` for transient failures).
- **`AtomicClaimScript.Sha1Hex` is public** for diagnostics — and matches Redis's server-side SCRIPT LOAD output exactly. The integration test asserts this equality so a future refactor of the script source (e.g. Plan 05-05 / 05-07) automatically surfaces SHA changes via a failing test.
- **`Channel<TicketEvent>` registration is a documented placeholder** — capacity 1000, `DropNewest`, `SingleReader=true`. Plan 05-07's `AddBackgroundServices()` will `services.Replace(...)` it with options-driven capacity (10000 default per CONTEXT D-15). Pre-registering it here means Wave 3 plans 05-05 + 05-06 resolve `ChannelWriter<TicketEvent>` from DI cleanly with no compile-order dependency on 05-07.
- **Scrutor `publicOnly: false`** — `EloRangeMatchmakingStrategy` is `public sealed` today, but a future internal sealed strategy in this assembly must still be discoverable. Customer-authored strategies in a separate assembly register manually before `AddMatchmaking()` to be picked up.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Auto-fix blocking issue] Added Polly + StackExchange.Redis package refs to `GameKit.Matchmaking.csproj`**

- **Found during:** Task 2 build verification + Task 3 build verification.
- **Issue:** `SerializationFailureRetry` (Task 2 helper) requires Polly v8; `AtomicClaimScript` (Task 3) requires StackExchange.Redis. The csproj started Plan 05-04 with only the Phase 5 Plan 05-02 EF Core + Npgsql refs. The two packages were already pinned centrally in `Directory.Packages.props` (Polly 8.5.2; StackExchange.Redis 2.8.41) so the fix was a versionless `<PackageReference Include="…" />` row.
- **Fix:** Added the two `PackageReference` rows with explanatory comment block under "Plan 05-04 runtime deps". No new pins added — both versions resolve from CPM.
- **Files modified:** `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` (lines 46-56).
- **Verification:** `dotnet build src/GameKit.Matchmaking --nologo` exits 0 after the additions.
- **Committed in:** `66cda73` (Task 2 commit) for Polly; same commit covers both because they were both needed for Task 2/3 build success — atomic with `PartyService` so the package's runtime surface is internally consistent.

**2. [Rule 3 — Auto-fix blocking issue] Added Redis-only `[CollectionDefinition("Redis")]` to `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs`**

- **Found during:** Task 3 build verification of `AtomicClaimScriptTests`.
- **Issue:** `AtomicClaimScriptTests` only needs Redis (not Postgres). It declares `[Collection("Redis")]` — but Wave-0's `CollectionDefinitions.cs` defined only `"Matchmaking"` (Postgres + Redis) and `"Postgres"` (Postgres only). Without a Redis-only declaration the test would either fall through (xUnit warns + skips) or spin up Postgres unnecessarily. The Core integration tests already follow this pattern (`tests/GameKit.Core.Integration.Tests/CollectionDefinitions.cs` line 12).
- **Fix:** Added `[CollectionDefinition("Redis")] public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }` to the Matchmaking integration tests' `CollectionDefinitions.cs`. xUnit1041 requires the attribute to live in the same assembly as the consuming tests.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` (lines 23-26).
- **Verification:** `AtomicClaimScriptTests` discovers the `RedisFixture` correctly; the 7 integration tests pass in ~75 ms total against the running Testcontainer Redis.
- **Committed in:** `b5722ef` (Task 3 commit).

**3. [Rule 1 — Bug] EVALSHA verification path: switched from `IServer.ScriptExistsAsync(sha)` to raw `ExecuteAsync("SCRIPT", "EXISTS", sha)`**

- **Found during:** Task 3 initial test run of `EVALSHA_Fast_Path_Uses_Cached_Hash_After_First_Call`.
- **Issue:** The first version of the test asserted `Server.ScriptExistsAsync(AtomicClaimScript.Sha1Hex)` returned `true` after the first `ScriptEvaluateAsync` call. The assertion failed with "Redis SCRIPT EXISTS returned false for SHA b31a7825…" — but a follow-up raw `SCRIPT LOAD` returned that exact same SHA, AND a raw `SCRIPT EXISTS sha` returned `[1]`. The `IServer.ScriptExistsAsync` helper on the same multiplexer was reporting false. Likely cause: a subtle interaction between StackExchange.Redis's typed wrapper and how it formats the SHA argument (hex string vs binary 20-byte token).
- **Fix:** Use `_server.ExecuteAsync("SCRIPT", "EXISTS", sha)` directly and parse the `RedisResult[]` reply. This matches the raw Redis protocol exactly and is unambiguous. Documented the workaround inline in the test.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/AtomicClaimScriptTests.cs` (the EVALSHA fast-path test body).
- **Verification:** Test now passes; the precomputed `AtomicClaimScript.Sha1Hex` is asserted equal to Redis's server-side SCRIPT LOAD return, AND `SCRIPT EXISTS sha` returns `[1]` after the first `ScriptEvaluateAsync` call.
- **Committed in:** `b5722ef` (Task 3 commit).

### Worktree Path Hygiene Incident (non-code-impacting)

During Task 1's initial Write-tool calls, the absolute paths I supplied resolved to the **main repository** (`/home/noah/Desktop/projects/gamekit/...`) rather than the **worktree** (`/home/noah/Desktop/projects/gamekit/.claude/worktrees/agent-a4a749c867d5a88fc/...`). The worktree's CWD was correct at the bash level, but the Write tool calls used main-repo paths. I detected this when the test runner reported `No test matches the given testcase filter` (the test files weren't visible to the worktree's csproj). I then moved the 8 files from `main` into the worktree via `mv` and re-ran the build/tests successfully — no orphan files remain in the main repo (`git status` on main is clean except for `.claude/`).

Subsequent Write/Edit calls have all targeted explicit worktree-absolute paths (`/home/noah/.../worktrees/agent-a4a749c867d5a88fc/...`). This is documented here for future executors as a known hazard in the worktree mode of Claude Code's `Write` tool: prefer worktree-prefixed paths or relative paths for Edit/Write operations.

### Other Deviations

None. The plan body's `<action>` and `<behavior>` sections matched the codebase patterns exactly; the only unplanned work was the three auto-fixes above.

## Threat Surface Notes

The plan's `<threat_model>` identified 6 STRIDE threats — all are now mitigated by the implementation:

- **T-05-04-01 (Tampering: stale leader writes a proposal):** mitigated. The Lua script's FIRST non-comment line is `if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 'LEASE_LOST' end`. Asserted programmatically by `LuaSource_First_Step_Is_Fencing_Token_Check`. The atomic execution semantics of EVAL guarantee no partial state on mismatch — verified by `LeaseLost_When_Fencing_Value_Does_Not_Match` (queue + proposal hash untouched after a fencing-mismatch return).
- **T-05-04-02 (Tampering: double-claim race):** mitigated. Step 2 of the Lua script verifies every candidate ticket is still in the queue sorted set; on any miss it returns `TICKET_GONE` before any `ZREM` — verified by `TicketGone_When_One_Ticket_Already_Claimed` (the surviving ticket is NOT removed on a `TicketGone` return). Plan 05-05 will exercise the full leader-election race against two replicas.
- **T-05-04-03 (Information Disclosure: party-code brute force):** acknowledged and deferred to Plan 05-08's rate-limit policy. The 30^6 ≈ 7.3·10⁸ code space + Plan 05-08's per-IP rate limit on `POST /api/parties/join` together make brute force infeasible.
- **T-05-04-04 (Tampering: predictable party code via `System.Random`):** mitigated. `PartyCodeGenerator` uses `RandomNumberGenerator.GetInt32(0, 30)` per char (CSPRNG with rejection sampling — bias-free). Documented in the class XML doc; verified by `GenerateCode_Uses_Only_Crockford_Alphabet` + `GenerateCode_Never_Contains_Forbidden_Characters`.
- **T-05-04-05 (DoS: concurrent CreateAsync from one player):** mitigated. `PartyService.CreateAsync` runs under `IsolationLevel.Serializable` with a Polly v8 retry on 40001 — verified by `CreateAsync_Concurrent_Calls_For_Same_Owner_Exactly_One_Succeeds` (2 parallel calls; exactly 1 succeeds, the other throws `player_already_in_party`).
- **T-05-04-06 (Tampering: non-owner dissolve):** mitigated. `DissolveAsync` checks `party.OwnerPlayerId == actorPlayerId` and throws `PartyAuthorizationException` on mismatch — verified by `DissolveAsync_Requires_Owner`.

No new threat flags surfaced during execution. No new network endpoints / auth paths / file access patterns / schema changes were introduced beyond those already cleared by Plan 05-02.

## Self-Check: PASSED

### Files
- `src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs` — FOUND
- `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` — FOUND
- `src/GameKit.Matchmaking/Strategy/MatchResult.cs` — FOUND
- `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` — FOUND
- `src/GameKit.Matchmaking/Strategy/PartyRatingAggregatorService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IPartyCodeGenerator.cs` — FOUND
- `src/GameKit.Matchmaking/Services/PartyCodeGenerator.cs` — FOUND
- `src/GameKit.Matchmaking/Services/IPartyService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/PartyService.cs` — FOUND
- `src/GameKit.Matchmaking/Services/PartyServiceExceptions.cs` — FOUND
- `src/GameKit.Matchmaking/Services/SerializationFailureRetry.cs` — FOUND
- `src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs` — FOUND
- `src/GameKit.Matchmaking/Redis/AtomicClaimResult.cs` — FOUND
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Strategy/BracketFlexMathTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Strategy/GlickoWeightedAggregatorTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeStrategyTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Tests/Services/PartyCodeGenerationTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/PartyServiceTests.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/AtomicClaimScriptTests.cs` — FOUND

### Commits
- `734436b` (Task 1 — IMatchmakingStrategy + EloRangeMatchmakingStrategy + aggregator + 25 unit tests) — FOUND
- `66cda73` (Task 2 — PartyCodeGenerator + PartyService SERIALIZABLE + 12 unit + 10 integration tests) — FOUND
- `b5722ef` (Task 3 — AtomicClaimScript + Scrutor scan + Channel placeholder + 7 integration tests) — FOUND

### Verification gates
- `dotnet build src/GameKit.Matchmaking --nologo` → exit 0 / 0 warnings / 0 errors — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Tests --nologo` → 61 passed / 0 failed — VERIFIED
- `dotnet test tests/GameKit.Matchmaking.Integration.Tests --nologo` → 20 passed / 0 failed — VERIFIED
- AtomicClaimScript Lua source ≤30 lines — VERIFIED (18 non-blank / 15 non-comment via programmatic assertion)
- AtomicClaimScript first step IS the fencing-token check — VERIFIED programmatically
- EVALSHA fast-path verified via raw `SCRIPT EXISTS sha` — VERIFIED
- PartyService uses `IsolationLevel.Serializable` — VERIFIED (grep'd the source; 3 `BeginTransactionAsync(IsolationLevel.Serializable` matches)
- PartyService uses citext-aware `WHERE party_code = @code` (no `ToUpperInvariant`) — VERIFIED (grep'd; zero `ToUpperInvariant` matches in PartyService.cs)
- PartyCodeGenerator uses `RandomNumberGenerator` (not `System.Random`) — VERIFIED (grep'd; zero `System.Random` references)
- Scrutor scan picks up `EloRangeMatchmakingStrategy` — VERIFIED (the `Channel_Placeholder_Resolves_From_DI_After_AddMatchmaking` test brings up the whole `AddMatchmaking()` chain without throwing; the Scrutor scan runs as part of `AddStrategyServices`)
- Plan 05-03's `// TODO(05-04): add Scrutor scan…` comment in `MatchmakingBuilderExtensions.AddMatchmaking` is gone — VERIFIED (grep'd; zero matches for the TODO marker)
- Channel `TicketEvent` placeholder + writer + reader resolve from DI — VERIFIED (`Channel_Placeholder_Resolves_From_DI_After_AddMatchmaking` test).

## Next Plan Readiness

- **05-05** (MatchmakerLeaseHelper + MatchmakerTickerService): can ship. `IMatchmakingStrategy` resolves from DI by Scrutor; `AtomicClaimScript` resolves as a singleton; `ChannelWriter<TicketEvent>` resolves cleanly against the placeholder (Plan 05-07 will `Replace()` it later). The ticker's match-formation loop now has every primitive it needs.
- **05-06** (ProposalService): can ship. `ChannelWriter<TicketEvent>` resolves from the placeholder (replaceable in 05-07); the Lua script's atomic claim is the correctness anchor for the accept-flow racing the TTL.
- **05-07** (AnalyticsDrainService + reconciler + retention): can ship. The placeholder `Channel<TicketEvent>` is the explicit `services.Replace()` target — the file map + comment in `MatchmakingBuilderExtensions.Strategy.cs` documents the replace semantics for the future maintainer.
- **05-08** (HTTP endpoints): can ship. `IPartyService` + the three typed exceptions are wired through DI; the endpoint layer maps `PartyConflictException` → 409, `PartyInvalidStateException` → 400/409, `PartyAuthorizationException` → 403 directly off the `Code` string. Rate-limit policy lives at the endpoint layer (mitigating T-05-04-03).

---
*Phase: 05-matchmaking-parties*
*Plan: 04*
*Completed: 2026-05-17*
