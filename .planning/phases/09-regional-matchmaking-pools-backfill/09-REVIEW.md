---
phase: 09-regional-matchmaking-pools-backfill
reviewed: 2026-06-06T00:00:00Z
depth: standard
files_reviewed: 23
files_reviewed_list:
  - src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs
  - src/GameKit.Core/Entities/SessionParticipant.cs
  - src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs
  - src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Http.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs
  - src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs
  - src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs
  - src/GameKit.Matchmaking/Entities/MatchmakingTicketType.cs
  - src/GameKit.Matchmaking/Http/Contracts/BackfillRequest.cs
  - src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs
  - src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs
  - src/GameKit.Matchmaking/Http/Validators/BackfillRequestValidator.cs
  - src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs
  - src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs
  - src/GameKit.Matchmaking/Services/BackfillService.cs
  - src/GameKit.Matchmaking/Services/IBackfillService.cs
  - src/GameKit.Matchmaking/Services/IMatchmakingService.cs
  - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
  - src/GameKit.Matchmaking/Services/MatchmakingService.cs
  - src/GameKit.Rankings/Builder/LadderConfig.cs
  - src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs
  - src/GameKit.Rankings/Services/StartupLadderUpserter.cs
findings:
  critical: 2
  warning: 3
  info: 2
  total: 7
status: issues_found
---

# Phase 9: Code Review Report

**Reviewed:** 2026-06-06T00:00:00Z
**Depth:** standard
**Files Reviewed:** 23
**Status:** issues_found

## Summary

Phase 9 adds regional matchmaking pools, a new `POST /api/matchmaking/backfill` endpoint, and the
participation-fraction rating guard (MATCH-19). The migration boundary rule is correctly honoured:
Core owns `ParticipationFraction` (migration 20260519000000) and Matchmaking owns `TicketType`
(migration 20260520000000). Authorization on the backfill endpoint is correct (`RequireAuthorization`).
The JSONB property name is consistent between writer (`StartupLadderUpserter`) and reader
(`PendingRatingUpdatesAdapter`) — both use PascalCase `"MinParticipationFractionForRating"` with the
default `JsonSerializer` (no camelCase policy applied). The per-pool lease renewal before each
ladder/pool iteration in `MatchmakerTickerService` preserves the single-drainer invariant. The
Redis score-0 invariant for backfill tickets is correctly implemented.

Two critical issues are present: an incomplete character-class check on `EnqueueRequest.PoolName`
that allows arbitrary characters to flow into a Redis sorted-set key, and a multi-ladder scenario in
`BackfillService` where `AllowedRegions` validation is performed against the wrong ladder config.
Three warnings cover a missing duplicate-ticket guard in `BackfillService`, the absence of
character-class constraints on `AllowedRegions` entries in the builder, and a stale comment that
mis-attributes the `ParticipationFraction` migration.

## Narrative Findings (AI reviewer)

## Critical Issues

### CR-01: Redis key injection via `EnqueueRequest.PoolName` — no character-class restriction

**File:** `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs:22-23`

**Issue:** `EnqueueRequestValidator` validates `PoolName` only for maximum length (64 chars) — it
applies no character-class `Matches()` rule equivalent to the one on `RegionName`. The endpoint
handler at `MatchmakingEndpoints.cs:90` resolves the effective pool as
`req.RegionName ?? req.PoolName`. When `RegionName` is `null` (the common case for callers that have
not yet migrated to the regional API), `PoolName` is forwarded verbatim as the pool component of the
Redis sorted-set key `mm:queue:{ladderId}:{poolName}`. An attacker can supply a `PoolName` containing
`:`—for example `"default:mm:control:paused"`—producing a key of
`mm:queue:{ladderId}:default:mm:control:paused`. The SCAN glob used in
`MatchmakerTickerService.ProcessPoolAsync` (`mm:queue:*:{poolName}`) would match this key because the
glob wildcard covers the ladderId segment. More critically, a sufficiently crafted `PoolName` could
cause the ticker's `ExtractLadderId` helper to parse the wrong segment, returning `Guid.Empty` and
silently corrupting the per-ladder pause-flag check. `RegionName` has the correct restriction
(`^[a-zA-Z0-9\-]+$`); `PoolName` does not.

**Fix:** Add a `Matches` rule to `PoolName` in `EnqueueRequestValidator` with the same pattern used
for `RegionName`:

```csharp
RuleFor(x => x.PoolName)
    .MaximumLength(64).WithMessage("PoolName must be at most 64 characters.")
    .Matches(@"^[a-zA-Z0-9\-]+$").When(x => x.PoolName is not null)
    .WithMessage("PoolName may only contain alphanumeric characters and hyphens (security: used as Redis key component).");
```

---

### CR-02: `BackfillService` validates `AllowedRegions` against the wrong ladder config in multi-ladder deployments

**File:** `src/GameKit.Matchmaking/Services/BackfillService.cs:98-114`

**Issue:** `BackfillService.BackfillAsync` resolves the ladder config (`cfg`) by matching the
_computed pool name_ against `l.Name`, falling back to `_ladders.FirstOrDefault()`. The `ladderId`
parameter supplied by the caller (sourced from the HTTP request) is used unconditionally for the
Postgres ticket row (`ticketRow.LadderId = ladderId`) and the Redis queue key
(`MatchmakingRedisKeys.Queue(ladderId, pool)`) — it is never matched against `cfg`. In a deployment
with more than one registered ladder, an attacker can send a `BackfillRequest` with the `LadderId` of
ladder B while the resolved `cfg` belongs to ladder A (because the pool/region name matches ladder
A's `Name`). The `AllowedRegions` check at line 111 is then performed against ladder A's
`AllowedRegions`, not ladder B's. The attacker can enqueue a backfill ticket into ladder B's Redis
queue for a region that ladder B explicitly prohibits, bypassing the regional access gate entirely.

Note: the identical structural pattern exists in `MatchmakingService.EnqueueAsync` (lines 184-190),
where `cfg` is also resolved by pool name without verifying that `ladderId` corresponds to the
resolved config. An implicitly resolved mismatch causes the AllowedRegions guard to operate against
the wrong ladder in both services.

**Fix:** Resolve `cfg` by `ladderId` first (look up by the `LadderId` passed in), not by pool name.
The `MatchmakingLadderConfig` does not currently carry a `Guid` identifier (configs are keyed by
name). The cleanest fix is to look up the matching ladder from the DB (or a registered name→id map)
to confirm the config. At minimum, validate that `ladderId` corresponds to a ladder whose `Name`
matches one of the registered configs before using that config's `AllowedRegions`. Example pattern:

```csharp
// Resolve cfg by the ladderId from DB or a pre-built name→id registration map.
var ladderName = await _db.Set<Ladder>()
    .AsNoTracking()
    .Where(l => l.Id == ladderId)
    .Select(l => l.Name)
    .FirstOrDefaultAsync(ct);

if (ladderName is null)
    return new BackfillResult(BackfillOutcome.UnknownLadder, Detail: "ladder_not_found");

var cfg = _ladders.FirstOrDefault(l => l.Name.Equals(ladderName, StringComparison.OrdinalIgnoreCase));
if (cfg is null)
    return new BackfillResult(BackfillOutcome.UnknownLadder, Detail: "ladder_not_configured");
```

This also applies to `MatchmakingService.EnqueueAsync` (lines 184–189).

---

## Warnings

### WR-01: `AllowedRegions` entries in `GameKitMatchmakingBuilder` are not restricted by character class

**File:** `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs:100-125`

**Issue:** `ValidateLadderConfig` validates `AllowedRegions` entries for null/empty, length ≤ 64, the
reserved value `"default"`, and duplicates, but applies no character-class restriction. An operator
who configures `AllowedRegions = ["eu*west"]` produces a ticker SCAN glob of
`mm:queue:*:eu*west` (inside `MatchmakerTickerService.ProcessPoolAsync:321`), where the embedded `*`
causes Redis `SCAN` to return queue keys for any pool matching `eu*west` — including pools belonging
to completely unrelated ladders. Similarly, a colon in a region name (e.g. `"eu:west"`) produces the
key `mm:queue:{id}:eu:west`, which has five colon-delimited segments instead of four, potentially
confusing `ExtractLadderId` (which expects `parts[2]` to be the UUID). This is an operator
misconfiguration rather than an inbound attacker, but the builder is the correct place to enforce
the invariant that protects the Redis key layout.

**Fix:** Add a character-class check to `ValidateLadderConfig` mirroring the HTTP-layer rule:

```csharp
if (!System.Text.RegularExpressions.Regex.IsMatch(region, @"^[a-zA-Z0-9\-]+$"))
    throw new ArgumentException(
        $"{nameof(config.AllowedRegions)} entry '{region}' may only contain alphanumeric characters and hyphens (Redis key safety).",
        nameof(config));
```

---

### WR-02: `BackfillService` has no existing-ticket dedup guard — a player can queue multiple backfill tickets simultaneously

**File:** `src/GameKit.Matchmaking/Services/BackfillService.cs:130-145`

**Issue:** `MatchmakingService.EnqueueAsync` performs a Postgres dedup check (lines 259–265) that
returns `AlreadyEnqueued` when the party already has a non-terminal ticket. `BackfillService` has no
equivalent guard. An authenticated player who fires the backfill endpoint in rapid succession (or
before the first ticket reaches a terminal state) will accumulate multiple `Backfill`-typed tickets
in the Redis sorted set at score 0. The rate limiter (`RequireRateLimiting(names.MmEnqueue)`) is the
only protection. Because the rate-limit policy is shared with the normal enqueue path, a highly
favourable rate limit configuration could allow a burst of backfill tickets, causing the matcher to
attempt multiple matches for the same player in a single tick and potentially producing duplicated
proposal events that confuse the accept/decline state machine.

**Fix:** Add a dedup check before the Postgres INSERT in `BackfillService.BackfillAsync`:

```csharp
// Dedup: reject if the player already has a non-terminal ticket.
var existingActive = await _db.Set<MatchmakingTicket>()
    .AsNoTracking()
    .AnyAsync(t => t.LadderId == ladderId
                && (t.Status == TicketStatus.Queued || t.Status == TicketStatus.Proposed),
              ct)
    .ConfigureAwait(false);

if (existingActive)
    return new BackfillResult(BackfillOutcome.AlreadyEnqueued,
        Detail: "active_ticket_exists");
```

(Add `AlreadyEnqueued = 5` to `BackfillOutcome` and an `HTTP 409` case in the endpoint handler.)

---

### WR-03: `SessionParticipantConfiguration` comment incorrectly attributes `ParticipationFraction` to the wrong migration

**File:** `src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs:37`

**Issue:** The inline comment reads:

```
// nullable double — column added by GameKit.Matchmaking migration 20260520000000
```

The `ParticipationFraction` column is actually added by `GameKit.Core.Migrations.AddSessionParticipationFraction`
(timestamp `20260519000000`), which lives in the `GameKit.Core.Migrations` namespace — not by a
Matchmaking migration. The comment is wrong on both the owning package (`GameKit.Matchmaking`) and
the migration timestamp (`20260520000000` vs `20260519000000`). This inverts the per-package
migration boundary rule documented in `CLAUDE.md`: Core is the correct owner, but the comment
implies Matchmaking is the owner. A developer reading this comment when triaging a missing-column
failure will look in the wrong migration history.

**Fix:**

```csharp
b.Property(p => p.ParticipationFraction); // nullable double — column added by GameKit.Core migration 20260519000000_AddSessionParticipationFraction
```

---

## Info

### IN-01: `EnqueueRequestValidator` accepts `PoolName = ""` (empty string) without character-class error, inconsistent with service behaviour

**File:** `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs:22-23`

**Issue:** `MatchmakingService.EnqueueAsync` treats an empty or whitespace `poolName` as `"default"`
(line 131). `EnqueueRequestValidator` does not apply a `Matches` rule to `PoolName` (only
`MaximumLength`), so an empty `PoolName` passes validation and reaches the service where it is
silently redirected to `"default"`. This is not a security risk (after CR-01 is fixed) but is a UX
inconsistency — callers who pass `PoolName = ""` receive a `queued` response routed to `"default"`
with no indication that their explicit empty string was rewritten. Either the validator should reject
empty `PoolName` explicitly, or the API contract should document the implicit rewrite.

**Fix:** Either add `.NotEmpty().When(x => x.PoolName is not null)` to the `PoolName` rule, or
document the rewrite in the `EnqueueRequest` XML doc.

---

### IN-02: `SessionParticipant.cs` entity doc comment cites the wrong migration for `ParticipationFraction`

**File:** `src/GameKit.Core/Entities/SessionParticipant.cs:52-53`

**Issue:** The XML doc comment on `ParticipationFraction` states:

> Column added by `GameKit.Matchmaking` migration `20260520000000` per the per-package migration
> boundary rule.

This contradicts the migration boundary rule it cites: the column is added by the `GameKit.Core`
migration `20260519000000_AddSessionParticipationFraction`. The Matchmaking migration
`20260520000000_MatchmakingBackfillRegions` only adds `TicketType` to `matchmaking_tickets`. The
comment is doubly wrong (wrong package, wrong timestamp) in an entity file that is itself in
`GameKit.Core` — readers will be confused.

**Fix:**

```csharp
/// Column added by <c>GameKit.Core</c> migration <c>20260519000000_AddSessionParticipationFraction</c>
/// per the per-package migration boundary rule (packages never modify Core tables).
```

---

_Reviewed: 2026-06-06T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
