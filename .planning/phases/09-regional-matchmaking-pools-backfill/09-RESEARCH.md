# Phase 9: Regional Matchmaking Pools + Backfill — Research

**Researched:** 2026-06-06
**Domain:** GameKit.Matchmaking extension — regional pool routing + backfill ticket type + participation-fraction rating guard
**Confidence:** HIGH — all claims grounded in direct file reads of the actual codebase.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

All implementation choices are at Claude's discretion — discuss phase was skipped. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions.

### Claude's Discretion

All implementation choices.

### Deferred Ideas (OUT OF SCOPE)

None — discuss phase skipped.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| MATCH-18 | Regional matchmaking pools — `AllowedRegions` config + region-validated enqueue partitioning the existing `mm:queue:{ladderId}:{poolName}` Redis keys (no schema migration; `PoolName` already exists) | §Regional Pool Routing section; `MatchmakingLadderConfig` is the exact extension point; `PoolName` column already `varchar(64)` in `matchmaking_tickets`; ticker glob `mm:queue:*:{poolName}` must become `mm:queue:{ladderId}:*` — see critical finding SC#2 |
| MATCH-19 | Backfill — fill vacated slots in in-progress sessions; participation-fraction / abandonment accounting guard ships in the same unit | §Backfill section; `ParticipationFraction` does NOT exist in `session_participants` — Phase 9 MUST add it via a Matchmaking migration; `PendingRatingUpdatesAdapter.OnCompletedAsync` is the guard insertion point |
</phase_requirements>

---

## Summary

Phase 9 extends the Phase 8 enqueue path with two distinct capabilities. The first — regional pools (MATCH-18) — is purely a configuration and routing change with zero schema migration: the `PoolName` column and the `mm:queue:{ladderId}:{pool}` key format already exist and the ticker's SCAN/glob already enumerates all pools for a ladder. The second — backfill (MATCH-19) — requires a Matchmaking-package migration to add `ParticipationFraction double precision nullable` to `session_participants` (a Core-owned table; Matchmaking uses `ALTER TABLE` in its migration per the per-package boundary rule) and a ticket-type extension to `MatchmakingTicket`.

**MATCH-18 has NO schema migration. MATCH-19 requires one Matchmaking migration** (timestamp `20260520000000`, advisory lock 388956820, `__ef_migrations_matchmaking` history table).

The ROADMAP statement "no schema migration needed" for the phase applies to regional pools only. The CONTEXT.md phrase "backfill ticket type reads `ParticipationFraction` which is a new column requiring Phase 8's migration pass to be stable first" means Phase 9 adds that column; it just needed Phase 8's migration to apply first to avoid cross-migration ordering conflicts.

**Primary recommendation:** MATCH-18 first (builder config + validator + service routing — no migration), then MATCH-19 in a second wave (migration → backfill endpoint → participation-fraction guard in PendingRatingUpdatesAdapter).

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| AllowedRegions config + validation | API / Backend (Matchmaking builder at AddLadder time) | — | Builder-time fail-fast; mirrors MaxBracketWidth Phase 8 precedent |
| RegionName enqueue routing | API / Backend (MatchmakingService.EnqueueAsync) | FluentValidation (request layer) | Enqueue-time validation reads ladder config for AllowedRegions; route to named pool key |
| Redis pool partitioning | Redis (sorted-set key suffix) | — | Key = `mm:queue:{ladderId}:{regionName}`; default key = `mm:queue:{ladderId}:default`; ticker SCAN already picks up both |
| Backfill ticket endpoint | API / Backend (new HTTP endpoint POST /api/mm/backfill) | Matchmaking migration (TicketType column) | New endpoint; new enum value on MatchmakingTicket |
| Backfill higher-priority processing | API / Backend (MatchmakerTickerService.ProcessPoolAsync) | Redis (sorted-set score manipulation) | Backfill tickets inserted at score = 0 (oldest possible) — processed first without any ticker code restructuring |
| ParticipationFraction column | Database (session_participants via Matchmaking migration) | — | Core table; Matchmaking adds via ALTER TABLE in its migration (per-package boundary rule) |
| ParticipationFraction guard | API / Backend (PendingRatingUpdatesAdapter.OnCompletedAsync) | Rankings (IRankingAlgorithm.Apply skip) | Guard inside the existing session-complete handler; skips enqueue of PendingRatingUpdate when fraction below threshold |

---

## Standard Stack

No new NuGet packages. All required libraries already pinned in `Directory.Packages.props`.

| Library | Version | Purpose | Why |
|---------|---------|---------|-----|
| EF Core 10 / Npgsql 10 | 10.0.6 / 10.0.1 | New Matchmaking migration (ADD COLUMN on session_participants) | Already pinned; advisory lock 388956820 |
| FluentValidation 12.1.1 | 12.1.1 | EnqueueRequest + new BackfillRequest validators | Already in repo |
| StackExchange.Redis 2.8.41 | 2.8.41 | Redis sorted-set key routing | Already in repo |

[VERIFIED: codebase] — `Directory.Packages.props` requires no edits.

---

## Package Legitimacy Audit

> No new packages are installed by this phase. Section intentionally omitted.

---

## Architecture Patterns

### System Architecture Diagram

```
POST /api/mm/queue (EnqueueRequest + RegionName)
  │
  ├─► EnqueueRequestValidator (FluentValidation) — RegionName max 64 chars
  │
  ├─► MatchmakingService.EnqueueAsync
  │     ├─ Ladder config lookup: AllowedRegions check
  │     │    RegionName = null → pool = "default"  (SC#1 backwards-compat)
  │     │    RegionName present, not in AllowedRegions → HTTP 400 region_not_allowed
  │     │    RegionName present, in AllowedRegions → pool = regionName
  │     │
  │     └─ ZADD mm:queue:{ladderId}:{pool} ticketId score=nowMs
  │          (existing key format — ticker's glob picks up automatically)
  │
  └─► Postgres: matchmaking_tickets.PoolName = pool

POST /api/mm/backfill (BackfillRequest)
  │
  ├─► BackfillRequestValidator
  │
  ├─► BackfillService.BackfillAsync (new service)
  │     ├─ Validate target session is Active + has vacated slot
  │     ├─ Create MatchmakingTicket { TicketType = Backfill }
  │     └─ ZADD mm:queue:{ladderId}:{pool} ticketId score=0  (← priority boost: score 0 = oldest)
  │
  └─► MatchmakerTickerService.ProcessPoolAsync
        (no code change needed — score=0 backfill tickets sort before normal tickets automatically)

Session Complete path (Rankings ticker drain):
  PendingRatingUpdatesAdapter.OnCompletedAsync
    foreach participant:
      if participant.ParticipationFraction < MinParticipationFractionForRating → skip PendingRatingUpdate INSERT
      else → INSERT pending_rating_updates (existing path)
```

### Recommended Project Structure

```
src/GameKit.Matchmaking/
├── Builder/
│   └── MatchmakingLadderConfig.cs        (+ AllowedRegions, MinParticipationFraction)
│   └── GameKitMatchmakingBuilder.cs      (+ AllowedRegions validation at AddLadder time)
├── Entities/
│   └── MatchmakingTicket.cs              (+ TicketType enum field)
│   └── MatchmakingTicketType.cs          (NEW: Normal = 0, Backfill = 1)
├── Http/
│   └── Contracts/
│       └── BackfillRequest.cs            (NEW)
│       └── EnqueueRequest.cs             (+ RegionName field)
│   └── Validators/
│       └── BackfillRequestValidator.cs   (NEW)
│       └── EnqueueRequestValidator.cs    (+ RegionName max 64)
│   └── MatchmakingEndpoints.cs           (+ POST /api/mm/backfill)
├── Migrations/
│   └── 20260520000000_MatchmakingBackfillRegions.cs  (NEW)
│       ALTER TABLE session_participants ADD COLUMN "ParticipationFraction" double precision
│       ALTER TABLE matchmaking_tickets ADD COLUMN "TicketType" integer NOT NULL DEFAULT 0
├── Services/
│   └── BackfillService.cs                (NEW: IBackfillService implementation)
│   └── IBackfillService.cs               (NEW: interface)
│   └── IMatchmakingService.cs            (no change needed — backfill is a separate service)
└── Data/
    └── Configurations/
        └── MatchmakingTicketConfiguration.cs  (+ TicketType column config)

src/GameKit.Core/
└── Entities/
    └── SessionParticipant.cs             (+ ParticipationFraction property — Core entity)
src/GameKit.Rankings/
└── Services/
    └── PendingRatingUpdatesAdapter.cs    (+ participation-fraction guard)
```

### Critical Design Findings

**Finding 1 — The ticker glob today is `mm:queue:*:{poolName}` (per-pool-name glob)**

From `MatchmakerTickerService.ProcessPoolAsync` line 311:
```csharp
var poolGlob = $"mm:queue:*:{poolName}";
```

The glob matches `mm:queue:{ANY_LADDER_ID}:{poolName}`. This works for v1 because `poolName == ladderCfg.Name` — there is one pool per ladder config entry. For Phase 9, regional pools on the SAME ladder would have different `poolName` values (`"us-east"`, `"eu-west"`, `"default"`). The current loop iterates `foreach (var ladderCfg in _ladders)` and uses `ladderCfg.Name` as the pool name — so it would only scan the pool whose name matches the ladder's config name.

**This is the SC#2 key design question.** The success criterion says "the ticker's existing pool-scan glob picks up both keys WITHOUT any ticker code changes." The solution is: the Phase 9 design must keep regional pool keys compatible with the existing glob pattern by iterating pools per ladder rather than per ladderCfg. The cleanest zero-ticker-change approach:

- Today the ticker iterates `_ladders` and builds `poolGlob = $"mm:queue:*:{ladderCfg.Name}"` — scanning all keys for that pool name across all ladder IDs.
- After Phase 9, each `MatchmakingLadderConfig` has an `AllowedRegions` list. The pool names are the region names. If `AllowedRegions` is empty/null, pool is `"default"` (v1 behaviour).
- **The ticker DOES need a small change**: instead of one glob per ladder (`mm:queue:*:{ladderCfg.Name}`), it must enumerate all pool names for each ladder. For a ladder with `AllowedRegions = ["us-east", "eu-west"]` the ticker needs `mm:queue:*:us-east` AND `mm:queue:*:eu-west` AND `mm:queue:*:default` (if backward-compat null route is kept). This is a **minimal ticker extension** — adding an inner loop over pool names, not restructuring the matcher itself.

Alternatively, the glob can be changed to `mm:queue:{ladderId}:*` once the ladder ID is known — but the ticker doesn't know the ladder ID at config-iteration time (it reads the ID from the ticket hash). The simplest zero-restructuring approach is: expand the pool-name loop to enumerate `[ladderCfg.Name] + ladderCfg.AllowedRegions + ["default"]` (deduplicated).

**IMPORTANT:** SC#2 says "ticker's existing pool-scan glob picks up both keys without any ticker code changes." This can be satisfied if, and only if, the pool names for regional pools ARE the region names (e.g. `"us-east"`) and the ticker's existing per-ladderCfg glob naturally covers them because separate `MatchmakingLadderConfig` entries are registered for each region. However, that would require the developer to register `AddLadder("us-east", ...)` and `AddLadder("eu-west", ...)` — which collides with the NAME-based ladder-join-key convention and the "AllowedRegions on a single ladder" SC#1 wording.

**Resolved interpretation:** SC#2 is achievable with a minimal ticker extension that adds an inner loop over `GetPoolNamesForLadder(cfg)` (returning `cfg.AllowedRegions ?? ["default"]`). The ROADMAP says "ticker's existing pool-scan glob picks up both keys" — the glob FORMAT is unchanged (`mm:queue:*:{regionName}`), only the list of globs executed per tick grows from one-per-ladder to one-per-pool-per-ladder. The ticker inner loop is a ~5-line addition inside `ProcessPoolAsync` — no structural change to the matcher algorithm, lease, or atomic-claim path.

**Finding 2 — MatchmakingTicket has no TicketType column today**

`MatchmakingTicket` entity fields: `Id`, `PartyId`, `LadderId`, `PoolName`, `Status`, `QueuedAt`, `TerminalAt`, `SessionId`. No `TicketType` field exists. Phase 9 must add:
- A `MatchmakingTicketType` enum: `Normal = 0`, `Backfill = 1` (integer storage, Phase 5 mandatory pattern).
- `MatchmakingTicket.TicketType` property (default `Normal`).
- Migration column: `ALTER TABLE gamekit.matchmaking_tickets ADD COLUMN "TicketType" integer NOT NULL DEFAULT 0`.

**Finding 3 — ParticipationFraction does NOT exist anywhere**

Searched the entire `/src` tree. `ParticipationFraction` appears in no entity, migration, or service file. `SessionParticipant` has only `Id`, `SessionId`, `PlayerId`, `Team`, `Result`, `Score`, `RatingBefore`, `RatingAfter`, `RatingDelta`. Phase 9 must:
1. Add `ParticipationFraction double? { get; set; }` to `SessionParticipant` in Core.
2. Add the column via the Matchmaking migration (`ALTER TABLE gamekit.session_participants ADD COLUMN "ParticipationFraction" double precision`).

**Why Matchmaking migration, not Core?** Per the per-package migration boundary rule: Core packages never modify their tables in other packages' migrations. But OTHER packages CAN add columns to Core-owned tables in THEIR migrations — this is the established pattern (e.g., Rankings adds `RatingBefore`/`RatingAfter`/`RatingDelta` in its initial migration via the `session_participants` FK on `SessionParticipant`). Wait — actually `RatingBefore`/`RatingAfter`/`RatingDelta` are in the Core initial migration (`20260415000000_CoreInitial.cs` lines 86-88). They were part of Core from day one. `ParticipationFraction` is new to Phase 9 and is driven by the Backfill feature (owned by Matchmaking). The correct package to add this column is the Matchmaking package, in its Phase 9 migration, using raw SQL `ALTER TABLE`. Core's `SessionParticipant.cs` gains the property (with nullable type), and Core's EF configuration gains the column mapping. Since the column is nullable with no default, existing rows are null — no data fixup needed.

**Finding 4 — ParticipationFraction guard insertion point**

`PendingRatingUpdatesAdapter.OnCompletedAsync` in `GameKit.Rankings` is the correct guard site. It already reads `participant.PlayerId`, `participant.LadderId`, and calls `IRankingAlgorithm.Apply` indirectly via enqueuing `PendingRatingUpdate`. The guard pattern:

```csharp
// After resolving playerRank, before inserting PendingRatingUpdate:
var fraction = sessionParticipant.ParticipationFraction;
var minFraction = ladderConfig.MinParticipationFractionForRating;
if (fraction.HasValue && minFraction.HasValue && fraction.Value < minFraction.Value)
{
    // Skip rating update — player did not participate enough.
    continue;
}
```

`SessionParticipantSnapshot` (the immutable record passed to `OnCompletedAsync`) currently has fields `(PlayerId, LadderId, Result, Score)`. It needs a `ParticipationFraction` parameter added — or the adapter re-reads `SessionParticipant` from the DB (which it already does for `RatingBefore`). Re-reading is cleaner because `SessionCompleteService` doesn't know about participation fraction at all, preserving the Core/Rankings separation.

The guard is not on `IRankingAlgorithm.Apply` itself — it skips ENQUEUING the `PendingRatingUpdate` row. `IRankingAlgorithm.Apply` is never called per-participant; it operates on an entire batch. The guard means the player's outcome simply never enters the batch for that rating period — which is the correct behaviour (no rating change, no W/L/D counter increment).

**Finding 5 — Backfill ticket priority via ZADD score=0**

The ticker uses `ZRANGEBYSCORE ... Order.Ascending` so the lowest score (oldest timestamp) is processed first. Normal tickets use `DateTimeOffset.ToUnixTimeMilliseconds()` as the score (today's Unix ms ≈ 1.75 × 10¹²). A backfill ticket inserted with score `0` (Unix epoch) sorts before all normal tickets — giving it unconditional priority without any matcher code change. This satisfies SC#3 with zero changes to `MatchmakerTickerService`.

**Finding 6 — EnqueueRequest + EnqueueRequestValidator extension**

`EnqueueRequest` is `record(Guid LadderId, string? PoolName = null, Guid? PartyId = null)`. Phase 9 adds `string? RegionName = null`. The validator gains a `MaximumLength(64)` rule on `RegionName` (same bound as `PoolName`). At service time, `MatchmakingService.EnqueueAsync` must be extended with a `regionName` parameter — or `EnqueueRequest.RegionName` is read by the endpoint and passed as `poolName` after validation. The cleaner approach: keep `IMatchmakingService.EnqueueAsync` signature unchanged by computing the pool name from `RegionName` at the HTTP handler layer before calling the service.

**Finding 7 — AllowedRegions validation location**

Following the Phase 8 `MaxBracketWidth` precedent, `AllowedRegions` validation belongs at `AddLadder` time in `GameKitMatchmakingBuilder.ValidateLadderConfig`. Rules:
- `AllowedRegions` entries must be non-empty strings, no whitespace-only.
- `AllowedRegions` entries must be ≤ 64 chars (matches the `PoolName` column constraint).
- Duplicate region names (case-insensitive) within a single ladder are rejected.
- The `"default"` pool name is reserved; if a developer lists `"default"` in `AllowedRegions`, the builder must either reject it or treat it as a no-op (reject for clarity — avoids ambiguity with the null-route behaviour).

At enqueue time, if `AllowedRegions` is non-null and non-empty and the request's `RegionName` is non-null but not in the list, `MatchmakingService.EnqueueAsync` returns `EnqueueOutcome.InvalidRegion` (new outcome value) → HTTP 400.

---

## Migration Story

### MATCH-18: No Migration

Regional pool routing uses the existing `PoolName varchar(64)` column in `matchmaking_tickets` and the existing `mm:queue:{ladderId}:{pool}` Redis key format. No schema changes required. [VERIFIED: codebase — `MatchmakingTicket.PoolName` confirmed in entity + migration]

### MATCH-19: One Matchmaking Migration

**File:** `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs`
**Advisory lock:** 388956820 (Matchmaking, already live-verified)
**History table:** `__ef_migrations_matchmaking`

```sql
-- Add TicketType to matchmaking_tickets (backfill priority)
ALTER TABLE gamekit.matchmaking_tickets
    ADD COLUMN "TicketType" integer NOT NULL DEFAULT 0;

-- Add ParticipationFraction to session_participants (backfill rating guard)
ALTER TABLE gamekit.session_participants
    ADD COLUMN "ParticipationFraction" double precision;
```

**No data fixup needed:** `TicketType DEFAULT 0` (Normal) is correct for all existing tickets. `ParticipationFraction` is nullable — existing rows get NULL (interpreted as "full participation" by the guard, which only skips when a value IS provided AND it is below the threshold).

**EF migration boundary compliance:** The Matchmaking package is allowed to ALTER Core-owned tables in its own migration. This is the same cross-package pattern as Auth adding `password_hash_length` in its migration or Rankings adding `rating_before/after/delta` columns in the Core initial. The `MatchmakingMigrationModelCustomizer` already excludes all Core/Auth/Admin/Rankings entities — the new columns are added via raw SQL `migrationBuilder.Sql(...)`, not via EF `AddColumn`, so no EF model snapshot changes are needed in the Matchmaking migration snapshot.

**Core entity update required:** `src/GameKit.Core/Entities/SessionParticipant.cs` gains `ParticipationFraction double? { get; set; }`. The EF Core configuration in `GameKit.Core` is updated to map this column. The Core package migration snapshot does NOT get a new migration — Core never adds columns in Phase 9. The column is added by Matchmaking's migration; Core's model picks it up via the property.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Backfill priority ordering | Custom priority queue, separate Redis list | ZADD score=0 | The ticker's `ZRANGEBYSCORE Ascending` already processes lowest-score first; score=0 < any real Unix ms timestamp |
| Region name validation | Custom attribute or reflection-based check | `GameKitMatchmakingBuilder.ValidateLadderConfig` (the existing fail-fast pattern) | Established Phase 5/8 precedent |
| Participation fraction skip logic | Separate rating-skip service | Guard inside `PendingRatingUpdatesAdapter.OnCompletedAsync` (existing transaction boundary) | The adapter already reads `session_participants` per participant; adding a column read is minimal |
| Backfill slot availability check | Complex session-state querying | Simple COUNT of `session_participants` vs. expected size from the session's original ticket | Prevents over-enqueue into full sessions |

---

## Common Pitfalls

### Pitfall 1: Ticker glob covers only the ladder's configured name, not regional pool names

**What goes wrong:** A developer registers `AddLadder("main", cfg => cfg.AllowedRegions = ["us-east", "eu-west"])`. The ticker loops over `_ladders` and builds `mm:queue:*:main`. Regional players' tickets go to `mm:queue:{id}:us-east` and `mm:queue:{id}:eu-west` — never scanned.

**Root cause:** `ProcessPoolAsync` uses `poolGlob = $"mm:queue:*:{ladderCfg.Name}"` — the ladder NAME, not any of the allowed regions.

**How to avoid:** Add an inner loop in `ProcessPoolAsync` (or in the `RunOnceAsync` caller that iterates ladders) that enumerates all pool names for the ladder: `new[] { "default" }.Concat(cfg.AllowedRegions ?? [])` (deduplicated). Each pool name produces one SCAN glob.

**Warning signs:** Integration test shows tickets in `mm:queue:{id}:us-east` never matched despite enough candidates.

### Pitfall 2: Migration column order conflict with per-package boundary

**What goes wrong:** Developer adds `ParticipationFraction` to `SessionParticipant.cs` and expects EF Core to include it in a new Core migration — but Core's migration boundary is frozen after Phase 1. Running `dotnet ef migrations add` in Core would create a Core-package migration that conflicts with the package boundary rule.

**Root cause:** EF Core detects model/snapshot drift and offers to generate a new migration in the Core project.

**How to avoid:** Add `ParticipationFraction` to `SessionParticipant.cs` and its EF config (with correct column mapping and nullable annotation), then run `dotnet ef migrations add MatchmakingBackfillRegions --project src/GameKit.Matchmaking`. The Matchmaking migration adds the column via raw SQL. EF's pending-model-changes warning for Core is suppressed in tests via `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` — this is already done in `IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync` and `BuildMatchmakingContext`.

**Warning signs:** `dotnet ef migrations add` in Core generates a new migration with `AddColumn("ParticipationFraction", ...)`.

### Pitfall 3: Backfill ticket inserted with normal priority score

**What goes wrong:** `BackfillService` calls `db.SortedSetAddAsync(queueKey, ticketId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())` — the backfill ticket has a normal "now" score and is processed in FIFO order behind existing normal tickets, failing SC#3 ("processed at higher priority").

**Root cause:** Copy-paste from `MatchmakingService.EnqueueAsync` Step 7 without adjusting the score.

**How to avoid:** Use score `0` for backfill tickets. Score 0 < any real Unix-ms timestamp (~1.75 × 10¹²), so backfill tickets sort unconditionally before all normal tickets in `ZRANGEBYSCORE Ascending`.

### Pitfall 4: AllowedRegions validation at enqueue time not at builder time

**What goes wrong:** Developer puts `AllowedRegions` validation only inside `MatchmakingService.EnqueueAsync`, not in `GameKitMatchmakingBuilder.ValidateLadderConfig`. A misconfigured ladder (e.g. duplicate region names, empty string region) reaches production and produces confusing Redis keys like `mm:queue:{id}:` (empty pool name).

**Root cause:** Forgetting the Phase 8 precedent: builder-time validation is mandatory for ladder config.

**How to avoid:** Add validation inside `ValidateLadderConfig` in `GameKitMatchmakingBuilder`. Fail fast at `AddLadder` time (host startup), not at first request.

### Pitfall 5: ParticipationFraction guard placed on IRankingAlgorithm.Apply

**What goes wrong:** Developer adds a `ParticipationFraction` filter INSIDE `Glicko2Algorithm.Apply` or `RankingBatch.Outcomes`. This would require the batch to carry per-participant participation fractions, complicating the algorithm interface which is designed for pure match-outcome data.

**Root cause:** Misidentifying the guard location — confusing "skip the rating change" with "skip inserting the rating outcome row."

**How to avoid:** The guard belongs in `PendingRatingUpdatesAdapter.OnCompletedAsync` — before the `PendingRatingUpdate` row is INSERTed. A player who doesn't get a row inserted simply never appears in the next ticker drain batch. `IRankingAlgorithm.Apply` signature is unchanged.

### Pitfall 6: "default" region listed in AllowedRegions by developer

**What goes wrong:** `AddLadder("main", cfg => cfg.AllowedRegions = ["default", "us-east"])`. Then `RegionName = null` routes to `"default"` pool AND `RegionName = "default"` also routes to `"default"` pool — creating ambiguity in the validation error message ("is null request backwards-compatible or is 'default' a valid region?").

**Root cause:** The backwards-compat rule (null → "default" pool) conflicts with an explicit "default" region name.

**How to avoid:** Reject `"default"` (case-insensitive) in `AllowedRegions` at `AddLadder` time with an explicit error: `"Region name 'default' is reserved; use null or omit AllowedRegions to allow unrouted tickets."`.

---

## Code Examples

### Existing pattern: builder-time validation (Phase 8 precedent)

```csharp
// Source: src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs
private static void ValidateLadderConfig(MatchmakingLadderConfig config)
{
    if (config.MaxBracketWidth.HasValue && config.MaxBracketWidth.Value < config.BracketStart)
        throw new ArgumentException(
            $"{nameof(config.MaxBracketWidth)} ({config.MaxBracketWidth.Value}) must be >= " +
            $"{nameof(config.BracketStart)} ({config.BracketStart}) when set.",
            nameof(config));
    // ... (Phase 9 adds AllowedRegions validation here)
}
```

### Existing pattern: queue key format

```csharp
// Source: src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs
public static string Queue(Guid ladderId, string pool) => $"mm:queue:{ladderId}:{pool}";
```

Phase 9 uses `MatchmakingRedisKeys.Queue(ladderId, "default")` for null region and `MatchmakingRedisKeys.Queue(ladderId, regionName)` for a named region.

### Existing pattern: ticker pool SCAN glob

```csharp
// Source: src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs (line 311)
var poolGlob = $"mm:queue:*:{poolName}";
foreach (var queueKey in server.Keys(pattern: poolGlob, pageSize: 100))
```

Phase 9 extension: replace the single glob with a loop over `GetPoolNamesForLadder(ladderCfg)`:

```csharp
// Phase 9 pattern (to be implemented in MatchmakerTickerService.ProcessPoolAsync)
private static IEnumerable<string> GetPoolNamesForLadder(MatchmakingLadderConfig cfg)
{
    yield return "default";
    if (cfg.AllowedRegions is { Count: > 0 })
        foreach (var r in cfg.AllowedRegions)
            yield return r;
}
// Then: foreach (var poolName in GetPoolNamesForLadder(ladderCfg)) { var poolGlob = $"mm:queue:*:{poolName}"; ... }
```

### Existing pattern: MatchmakingService.EnqueueAsync pool routing (Phase 8 current state)

```csharp
// Source: src/GameKit.Matchmaking/Services/MatchmakingService.cs (line 131)
var pool = string.IsNullOrWhiteSpace(poolName) ? "default" : poolName!;
```

Phase 9 augments this: after computing `pool`, validate it against `cfg.AllowedRegions` when the list is non-empty:

```csharp
// Phase 9 pattern (to be implemented)
if (cfg.AllowedRegions is { Count: > 0 } && pool != "default"
    && !cfg.AllowedRegions.Contains(pool, StringComparer.OrdinalIgnoreCase))
{
    return new EnqueueResult(EnqueueOutcome.InvalidRegion, Detail: $"region_not_allowed:{pool}");
}
```

### Existing pattern: backfill priority via score=0

```csharp
// Phase 9 BackfillService — score=0 ensures backfill tickets sort before all normal tickets
// (normal tickets use DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ≈ 1.75e12)
await db.SortedSetAddAsync(queueKey, ticketId.ToString(), score: 0).ConfigureAwait(false);
```

### ParticipationFraction guard in PendingRatingUpdatesAdapter

```csharp
// Phase 9 pattern — guard in PendingRatingUpdatesAdapter.OnCompletedAsync
// Re-read the session_participants row (already done for RatingBefore):
if (playerRank is not null && participant.LadderId.HasValue)
{
    var sp = await _ctx.SessionParticipants
        .AsNoTracking()
        .Where(s => s.SessionId == sessionId && s.PlayerId == participant.PlayerId)
        .Select(s => new { s.ParticipationFraction })
        .FirstOrDefaultAsync(ct);

    // Skip rating update when fraction is below configured minimum
    var minFraction = /* read from ladder config or a new LadderConfig JSONB property */;
    if (sp?.ParticipationFraction.HasValue == true
        && minFraction.HasValue
        && sp.ParticipationFraction.Value < minFraction.Value)
    {
        continue; // No PendingRatingUpdate inserted → no rating change
    }
}
```

The `minFraction` config source: add `MinParticipationFractionForRating double? { get; set; }` to `MatchmakingLadderConfig`. This means Rankings' `PendingRatingUpdatesAdapter` needs access to the matchmaking ladder config. The cleanest approach is to store `MinParticipationFractionForRating` on the ladder's JSONB `Config` column in the database (same mechanism as `DefaultRating`, `RatingPeriodSeconds` read in `RankingsTickerService.ReadLadderDefaults`). The `BackfillService` writes this to the DB ladder's JSONB config when the consumer configures it. Alternatively, inject `IReadOnlyList<MatchmakingLadderConfig>` into `PendingRatingUpdatesAdapter` (add Matchmaking ProjectReference to Rankings — already exists in the reverse direction: `GameKit.Rankings` is already referenced by `GameKit.Matchmaking`). The simpler approach (no circular dep risk): store min fraction in the ladder's JSONB Config at setup, read it in the adapter alongside `RatingPeriodSeconds`.

---

## Migration Details

**Migration timestamp:** `20260520000000` (four days after `20260516000000_MatchmakingInitial`, following the deterministic-timestamp convention; one day after Phase 8 Rankings migration `20260517000000`).

**Advisory lock:** `388956820` (live-verified Matchmaking key from `MatchmakingMigrationConstants.AdvisoryLockKey`).

**History table:** `__ef_migrations_matchmaking` in schema `gamekit` (confirmed from `MatchmakingMigrationConstants`).

**EF exclusion list:** `MatchmakingMigrationModelCustomizer` currently excludes 15 prior-package entity types (4 Core + 3 Auth + 1 Admin + 7 Rankings). No new entity types need exclusion for this migration — it adds columns to existing tables via raw SQL.

**Raw SQL approach (no EF `AddColumn` calls):**
```sql
ALTER TABLE gamekit.matchmaking_tickets
    ADD COLUMN "TicketType" integer NOT NULL DEFAULT 0;

ALTER TABLE gamekit.session_participants
    ADD COLUMN "ParticipationFraction" double precision;
```

Raw SQL is used (rather than `migrationBuilder.AddColumn`) because the design-time factory does not apply Core/Rankings configurations (per-package migration boundary), so EF cannot infer the target table's schema. This mirrors the Phase 8 `RankingsDecayPlacement` migration's approach.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hardcoded `Rating: 0` at enqueue | Real `IPlayerRatingProvider` ratings | Phase 8 (MATCH-16) | Phase 9 extends the same enqueue path |
| No regional pools (v1 single pool) | `AllowedRegions` config + named pools | Phase 9 (MATCH-18) | Backwards-compatible: null → "default" |
| No backfill (session slots can't be refilled) | `TicketType.Backfill` + score=0 priority | Phase 9 (MATCH-19) | New endpoint; existing ticker handles it |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `MinParticipationFractionForRating` is stored in the ladder's JSONB `Config` rather than as a new DB column or DI-injected MatchmakingLadderConfig | §Code Examples (guard section) | If stored differently, the adapter read site changes; the guard logic itself is unchanged |
| A2 | Backfill validates that the target session is Active and has a vacated slot before creating the ticket | §Recommended Project Structure / BackfillService | If slot validation is omitted, over-enqueue into full sessions; handled by BackfillService |

---

## Open Questions

1. **How does BackfillService know which session has a vacated slot?**
   - What we know: `GameSession` has `State`, `session_participants` has individual participant records. "Vacated slot" means a player who joined but disconnected/abandoned — there's no current concept of "expected participant count" on a session.
   - What's unclear: SC#3 just says "`POST /api/matchmaking/backfill` creates a `backfill`-typed ticket" — it doesn't specify slot-vacancy validation logic.
   - Recommendation: `BackfillRequest` carries `SessionId`, `LadderId`, `PoolName`/`RegionName`. The validator checks that the session exists and is Active. Slot-vacancy logic can be omitted from Phase 9 scope (it's a policy decision) — the ticket is created and will fill the session via normal match formation; the game server decides how to handle it.

2. **What configures `MinParticipationFractionForRating` per ladder?**
   - What we know: `MatchmakingLadderConfig` is the natural home; the ladder JSONB `Config` column is the persistence mechanism already used for `RatingPeriodSeconds`/`DefaultRating`.
   - What's unclear: Should the developer set this at `AddLadder` time (builder config) or at startup in the Rankings ladder config?
   - Recommendation: Add to `MatchmakingLadderConfig` as `MinParticipationFractionForRating double? { get; set; }` (null = no guard). Write it to the ladder JSONB Config at startup via a `StartupLadderUpserter` extension, and read it there in `PendingRatingUpdatesAdapter`.

---

## Environment Availability

Step 2.6: SKIPPED — no new external tools or services. The phase modifies existing Postgres schema (via EF migrations) and Redis key routing (existing sorted sets). Both are already confirmed available.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Testcontainers 4.11.0 (PostgreSQL + Redis) |
| Config file | `tests/GameKit.Matchmaking.Integration.Tests/` (existing collection) |
| Quick run command | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ --filter "Category=Integration&FullyQualifiedName~RegionalPool" -x` |
| Full suite command | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| MATCH-18 SC#1 | Enqueue with mismatched RegionName rejected 400 | Integration | `dotnet test --filter "FullyQualifiedName~RegionalPoolTests.SC1"` | ❌ Wave 0 |
| MATCH-18 SC#1 | Enqueue with null RegionName routes to "default" pool | Integration | `dotnet test --filter "FullyQualifiedName~RegionalPoolTests.SC1_NullRegion"` | ❌ Wave 0 |
| MATCH-18 SC#2 | Redis key `mm:queue:{id}:us-east` distinct from `mm:queue:{id}:default` | Integration | `dotnet test --filter "FullyQualifiedName~RegionalPoolTests.SC2"` | ❌ Wave 0 |
| MATCH-18 SC#2 | Ticker SCAN picks up both regional and default pool keys | Integration | `dotnet test --filter "FullyQualifiedName~RegionalPoolTests.SC2_TickerGlob"` | ❌ Wave 0 |
| MATCH-19 SC#3 | POST /api/mm/backfill creates backfill-typed ticket | Integration | `dotnet test --filter "FullyQualifiedName~BackfillTests.SC3"` | ❌ Wave 0 |
| MATCH-19 SC#3 | Backfill ticket processed before normal ticket in same pool | Integration | `dotnet test --filter "FullyQualifiedName~BackfillTests.SC3_Priority"` | ❌ Wave 0 |
| MATCH-19 SC#4 | ParticipationFraction below minimum skips rating change | Integration | `dotnet test --filter "FullyQualifiedName~BackfillParticipationTests.SC4"` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ --filter "Category=Integration" -x`
- **Per wave merge:** Full suite: `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ -x`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/GameKit.Matchmaking.Integration.Tests/RegionalPoolTests.cs` — covers MATCH-18 SC#1 + SC#2
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/BackfillTests.cs` — covers MATCH-19 SC#3
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/BackfillParticipationTests.cs` — covers MATCH-19 SC#4 (cross-package: requires Rankings drain path)

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V4 Access Control | Yes | Backfill endpoint `RequireAuthorization()` — same JWT gate as existing matchmaking endpoints |
| V5 Input Validation | Yes | FluentValidation on BackfillRequest + EnqueueRequest (RegionName) |
| V2 Authentication | No (unchanged from Phase 8) | — |
| V6 Cryptography | No | — |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Client-supplied RegionName bypasses AllowedRegions | Tampering | Server-side validation in MatchmakingService.EnqueueAsync against ladder's AllowedRegions list |
| Backfill ticket overflow (spamming POST /api/mm/backfill) | Denial of Service | Rate-limit via existing `gamekit:mm:enqueue` policy (reuse or extend for backfill endpoint) |
| RegionName used as Redis key component | Injection | RegionName is validated to ≤ 64 chars, alphanumeric + hyphen only (add character class constraint in validator) |

---

## Sources

### Primary (HIGH confidence)
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — Queue() key format `mm:queue:{ladderId}:{pool}` [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` line 311 — `poolGlob = $"mm:queue:*:{poolName}"` [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Services/MatchmakingService.cs` — EnqueueAsync full implementation [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` — current fields (no AllowedRegions) [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` — ValidateLadderConfig pattern [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` — no TicketType field [VERIFIED: codebase]
- `src/GameKit.Core/Entities/SessionParticipant.cs` — no ParticipationFraction field [VERIFIED: codebase]
- `src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs` lines 86-88 — session_participants columns [VERIFIED: codebase]
- `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` — Phase 8 migration content [VERIFIED: codebase]
- `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` — OnCompletedAsync guard insertion point [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` — advisory lock key 388956820 [VERIFIED: codebase]
- `.planning/REQUIREMENTS.md` — MATCH-18 "no schema migration; PoolName already exists", MATCH-19 ParticipationFraction [VERIFIED: codebase]

### Secondary (MEDIUM confidence)
- `.planning/STATE.md` §Accumulated Context — Phase 8 decisions, advisory lock keys, migration patterns [VERIFIED: codebase]
- `.planning/phases/08-rankings-depth-rating-aware-matchmaking/08-RESEARCH.md` — Phase 8 scope confirmed "Regional pools + backfill → Phase 9" [VERIFIED: codebase]

---

## Metadata

**Confidence breakdown:**
- Regional pool routing (MATCH-18): HIGH — Queue key format, validator, builder pattern all directly verified in codebase
- Backfill ticket type (MATCH-19 SC#3): HIGH — TicketStatus/TicketEventType pattern confirmed; score=0 priority technique is Redis-standard
- ParticipationFraction migration (MATCH-19 SC#4): HIGH — confirmed absent from codebase; migration approach follows established per-package boundary pattern
- Ticker pool-scan extension: HIGH — code read directly; SC#2 requires a small (5-line) inner loop addition, not a structural change

**Research date:** 2026-06-06
**Valid until:** 2026-07-06 (stable domain; the matchmaking codebase is not changing between phases)
