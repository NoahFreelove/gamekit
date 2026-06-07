---
phase: 09-regional-matchmaking-pools-backfill
fixed_at: 2026-06-06T20:22:00Z
review_path: .planning/phases/09-regional-matchmaking-pools-backfill/09-REVIEW.md
iteration: 1
findings_in_scope: 7
fixed: 7
skipped: 0
status: all_fixed
---

# Phase 9: Code Review Fix Report

**Fixed at:** 2026-06-06T20:22:00Z
**Source review:** .planning/phases/09-regional-matchmaking-pools-backfill/09-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 7 (2 Critical, 3 Warning, 2 Info)
- Fixed: 7
- Skipped: 0

## Fixed Issues

### CR-01 + IN-01: Redis key injection via PoolName + empty-string inconsistency

**Files modified:** `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs`
**Commit:** 91a39ae
**Applied fix:** Added `.NotEmpty().When(x => x.PoolName is not null)` guard (IN-01) and
`.Matches(@"^[a-zA-Z0-9\-]+$").When(x => !string.IsNullOrEmpty(x.PoolName))` character-class
rule (CR-01) to the PoolName validator, mirroring the existing RegionName rule. Empty PoolName
is now explicitly rejected when supplied (rather than silently rewritten to "default").

### CR-02: Wrong-ladder AllowedRegions resolution in BackfillService + MatchmakingService

**Files modified:** `src/GameKit.Matchmaking/Services/BackfillService.cs`,
`src/GameKit.Matchmaking/Services/MatchmakingService.cs`,
`src/GameKit.Matchmaking/Services/IBackfillService.cs`,
`src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs`
**Commit:** 5b17dae (core) + 85e6fc7 (dedup guard correction)
**Applied fix:** Both services now look up `Ladder.Name` from the DB using `_db.Set<Ladder>().AsNoTracking().Where(l => l.Id == ladderId).Select(l => l.Name).FirstOrDefaultAsync()` before resolving `MatchmakingLadderConfig`. Returns `UnknownLadder` if the DB has no matching row (detail: `"unknown_ladder:{ladderId}"`) or if no config is registered for that ladder name (detail: `"ladder_not_configured_for_matchmaking"`). The old pool-name-match fallback and `FirstOrDefault()` fallback are removed from both services. `using GameKit.Rankings.Entities;` added to both. Updated XML doc on `BackfillService` class-level remarks.

### WR-01: AllowedRegions char-class validation missing in builder

**Files modified:** `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs`
**Commit:** 153b2df
**Applied fix:** Added `Regex.IsMatch(region, @"^[a-zA-Z0-9\-]+$")` check inside
`ValidateLadderConfig`'s AllowedRegions loop, throwing `ArgumentException` for entries
containing colons (which break the 4-segment `mm:queue:{id}:{region}` key format) or
glob characters (which corrupt MatchmakerTickerService SCAN patterns). `using System.Text.RegularExpressions;` added.

### WR-02: BackfillService dedup guard missing

**Files modified:** `src/GameKit.Matchmaking/Services/BackfillService.cs`,
`src/GameKit.Matchmaking/Services/IBackfillService.cs`,
`src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs`
**Commit:** 5b17dae (combined with CR-02 commit), 85e6fc7 (dedup narrowing)
**Applied fix:** Added `BackfillOutcome.AlreadyEnqueued = 5` to the enum with XML doc.
Added a dedup query before the Postgres INSERT that checks for existing non-terminal
`Backfill`-typed tickets in the same `ladderId + pool`. Narrowed to `MatchmakingTicketType.Backfill`
to avoid blocking normal-ticket players from initiating backfill in the same pool. The
endpoint handler returns HTTP 409 for `AlreadyEnqueued`.

### WR-03 + IN-02: Stale migration attribution in SessionParticipantConfiguration and SessionParticipant

**Files modified:** `src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs`,
`src/GameKit.Core/Entities/SessionParticipant.cs`
**Commit:** 85e6fc7
**Applied fix:** Corrected the inline comment in `SessionParticipantConfiguration.cs` from
`GameKit.Matchmaking migration 20260520000000` to `GameKit.Core migration
20260519000000_AddSessionParticipationFraction`. Corrected the XML doc on
`SessionParticipant.ParticipationFraction` from `GameKit.Matchmaking / 20260520000000` to
`GameKit.Core / 20260519000000_AddSessionParticipationFraction`, consistent with the
per-package migration boundary rule.

## Skipped Issues

None — all findings were fixed.

---

## Verification Results

1. `dotnet build GameKit.sln -warnaserror` → Build succeeded. **0 Warning(s), 0 Error(s)**
2. `dotnet test GameKit.Matchmaking.Integration.Tests` → **Passed! Failed: 0, Passed: 76, Skipped: 0, Total: 76**
3. `dotnet test GameKit.Rankings.Integration.Tests` → **Passed! Failed: 0, Passed: 74, Skipped: 0, Total: 74**
4. `dotnet test GameKit.Matchmaking.Tests` (unit) → **Passed! Failed: 0, Passed: 91, Skipped: 0, Total: 91**

---

_Fixed: 2026-06-06T20:22:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
