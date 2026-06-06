---
phase: 09-regional-matchmaking-pools-backfill
plan: "03"
subsystem: matchmaking
tags: [backfill, redis, dotnet, efcore, asp-net-core, endpoint, integration-tests, wave-3]
dependency_graph:
  requires:
    - phase: 09-regional-matchmaking-pools-backfill
      plan: "01"
      provides: MatchmakingTicketType.Backfill enum, MatchmakingTicket.TicketType, Wave 0 BackfillTests scaffold
    - phase: 09-regional-matchmaking-pools-backfill
      plan: "02"
      provides: AllowedRegions guard in MatchmakingService, ValidationEndpointFilter<BackfillRequest> pattern, MatchmakingEndpoints.cs with EnqueueAsync
  provides:
    - BackfillRequest DTO (LadderId, SessionId, RegionName?)
    - BackfillRequestValidator (NotEmpty + MaxLength(64) + regex ^[a-zA-Z0-9\\-]+$ on RegionName)
    - IBackfillService + BackfillOutcome (5 values) + BackfillResult record
    - BackfillService creates MatchmakingTicketType.Backfill tickets at Redis score 0
    - POST /api/matchmaking/backfill endpoint (authorized + rate-limited + validated)
    - BackfillTests (3 facts) green
  affects:
    - 09-04-PLAN.md (participation fraction guard — BackfillTests used by 09-04 as well)
tech_stack:
  added: []
  patterns:
    - "Redis score 0 for backfill priority: ZADD score=0 sorts before all normal tickets (Unix ms timestamps ~1.75e12)"
    - "Session Active check: query GameSession.State == GameSessionState.Active before creating ticket"
    - "Route literal /api/matchmaking/backfill (not /api/mm/backfill) per SC#3 spec"
key_files:
  created:
    - src/GameKit.Matchmaking/Http/Contracts/BackfillRequest.cs
    - src/GameKit.Matchmaking/Http/Validators/BackfillRequestValidator.cs
    - src/GameKit.Matchmaking/Services/IBackfillService.cs
    - src/GameKit.Matchmaking/Services/BackfillService.cs
  modified:
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Http.cs
    - src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs
    - tests/GameKit.Matchmaking.Integration.Tests/BackfillTests.cs
decisions:
  - "Ladder config lookup in BackfillService mirrors MatchmakingService: match by pool name (from RegionName or 'default'), fallback to first registered — keeps the single-ladder-per-app v1 convention"
  - "BackfillService does NOT check that the ticket's LadderId matches the session's LadderId — SC#3 scope is 'creates a backfill-typed ticket'; T-09-03-04 accepted per threat model"
  - "IBackfillService takes ChannelWriter<TicketEvent> in ctor to mirror MatchmakingService DI shape (though backfill does not emit analytics events — included for symmetry and future extension)"
  - "ToUnixTimeMilliseconds appears only in a comment (NOT in code) — comment documents what NOT to do (MATCH-19 SC#3 Pitfall 3)"
  - "Added SC3_Backfill_MissingSession_Returns404 test beyond the two Wave 0 scaffolds to verify 404 path"
metrics:
  duration: ~4min
  completed: "2026-06-06"
  tasks: 3
  files: 7
---

# Phase 9 Plan 03: Backfill Ticketing (MATCH-19 SC#3) Summary

**One-liner:** POST /api/matchmaking/backfill endpoint with IBackfillService creating Backfill-typed tickets at Redis score 0 — unconditional higher priority over normal tickets via ZRANGEBYSCORE Ascending ordering; all BackfillTests green.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | BackfillRequest + BackfillRequestValidator + IBackfillService contract | 3c8ca27 | 3 |
| 2 | BackfillService implementation + scoped DI registration | 11ea46b | 2 |
| 3 | POST /api/matchmaking/backfill endpoint + BackfillTests green | e56fab1 | 2 |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Missing `using GameKit.Core.Services` in BackfillService.cs**
- **Found during:** Task 2 compilation
- **Issue:** `IClock` and `IIdGenerator` are in `GameKit.Core.Services` namespace, which was missing from BackfillService.cs imports (copied from MatchmakingService which already had it via `GameKit.Core.Data`)
- **Fix:** Added `using GameKit.Core.Services;` to BackfillService.cs
- **Committed in:** 11ea46b (same task commit after inline fix)

**2. [Rule 1 - Bug] CS1734 XML doc paramref 'BackfillAsync' on class-level comment**
- **Found during:** Task 2 compilation
- **Issue:** Class-level XML remarks used `<paramref name="BackfillAsync"/>` which is not a valid parameter name at class scope
- **Fix:** Changed to inline text reference
- **Committed in:** 11ea46b (same task commit after inline fix)

No scope deviations. Plan executed exactly as specified.

## Known Stubs

None — all BackfillTests facts are fully implemented (no NotImplementedException remaining).

## Threat Surface Scan

| Flag | File | Description |
|------|------|-------------|
| threat_flag: new_endpoint | src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs | POST /api/matchmaking/backfill — new authenticated endpoint creates priority-0 Redis ticket; mitigated by T-09-03-01 (.RequireAuthorization()), T-09-03-02 (FluentValidation regex guard), T-09-03-03 (.RequireRateLimiting(MmEnqueue)) as specified in plan threat model |

All threat mitigations from the plan threat register are applied:
- T-09-03-01 (EoP): `.RequireAuthorization()` — JWT required
- T-09-03-02 (Tampering): FluentValidation `^[a-zA-Z0-9\-]+$` + AllowedRegions check in BackfillService
- T-09-03-03 (DoS): `.RequireRateLimiting(names.MmEnqueue)` + session-Active gate
- T-09-03-04 (Spoofing): Accepted — session-existence + Active gate enforced; membership policy out of scope

## Self-Check: PASSED
