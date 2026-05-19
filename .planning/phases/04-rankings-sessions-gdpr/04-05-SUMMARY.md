---
phase: 04-rankings-sessions-gdpr
plan: "05"
subsystem: api
tags: [session-complete, idempotency, rate-limiting, endpoint-filters, rankings, postgres]

# Dependency graph
requires:
  - phase: 04-02
    provides: Rankings schema migrations (pending_rating_updates, session_complete_idempotency tables)
  - phase: 04-04
    provides: ServiceToken auth scheme, IServiceTokenService, GameKitDbContext Rankings model wiring
provides:
  - POST /api/sessions/{id}/complete endpoint (RANK-11)
  - IPostSessionCompleteHandler port — PendingRatingUpdatesAdapter enqueues pending_rating_updates rows
  - IIdempotencyStore port — RankingsIdempotencyStore persists session_complete_idempotency rows
  - ICanonicalRequestHasher port — CanonicalJsonHasher SHA-256 body fingerprinting
  - IdempotencyKeyEndpointFilter — generic Core filter enforcing Idempotency-Key header (8-128 chars)
  - ValidationEndpointFilter<T> — generic Core filter invoking IValidator<T>
  - gamekit:sessions:complete rate-limit policy (300 req/min per service token name)
  - SessionCompleteService — orchestrates state-conditional UPDATE + idempotency + post-handler
  - SessionCompleteIdempotencyTests (6 tests, all green) — proves SC#2 5x-retry → 1 delta invariant
affects: [04-06, 04-07, 04-08, rankings-ticker, gdpr-export]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Optional port injection via factory lambda (GetService<T>) — Core operates in degraded mode when Rankings not installed"
    - "State-conditional UPDATE pattern — ExecuteUpdateAsync WHERE State=Active returns affected=0 on non-active sessions"
    - "Idempotency dedup inside ambient transaction — IIdempotencyStore.StoreAsync runs in the caller's tx, no separate commit"
    - "Canonical JSON hash for idempotency body fingerprinting — camelCase serialization + SHA-256"
    - "Raw SQL in integration tests must use enum name strings not integer casts — GameSessionState is HasConversion<string>()"

key-files:
  created:
    - src/GameKit.Core/Services/IPostSessionCompleteHandler.cs
    - src/GameKit.Core/Services/IIdempotencyStore.cs
    - src/GameKit.Core/Services/ICanonicalRequestHasher.cs
    - src/GameKit.Core/Services/SessionCompleteService.cs
    - src/GameKit.Core/Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs
    - src/GameKit.Core/Http/EndpointFilters/ValidationEndpointFilter.cs
    - src/GameKit.Core/Http/SessionEndpoints.cs
    - src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs
    - src/GameKit.Rankings/Services/RankingsIdempotencyStore.cs
    - src/GameKit.Rankings/Json/CanonicalJsonHasher.cs
    - src/GameKit.Rankings/Http/Validators/SessionCompleteRequestValidator.cs
    - src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs
    - tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs
    - tests/GameKit.Rankings.Tests/Json/CanonicalJsonHasherTests.cs
  modified:
    - src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs

key-decisions:
  - "Optional ports (IPostSessionCompleteHandler, IIdempotencyStore, ICanonicalRequestHasher) use GetService<T> factory registration — Core-only installs operate in degraded mode without DI throwing"
  - "IIdempotencyStore.StoreAsync runs inside the caller's ambient transaction — no explicit commit, ensuring atomicity with the state-conditional UPDATE"
  - "GameSessionState stored as text via HasConversion<string>() — all raw SQL must use enum name strings ('Active', 'Cancelled'), never integer cast values"
  - "Rate limit policy keyed on service-token-name (from auth claims) at 300 req/min — protects against replayed token abuse"

patterns-established:
  - "Endpoint filter chain: IdempotencyKeyEndpointFilter → ValidationEndpointFilter<T> → handler"
  - "Port injection pattern: scoped service factory lambda resolves optional ports via GetService<T>"
  - "Test server override: second AddDbContext<GameKitDbContext> with ReplaceService<IModelCustomizer> to include Rankings model without UseApplicationServiceProvider"

requirements-completed: [RANK-11, RANK-07]

# Metrics
duration: 32min
completed: 2026-05-16
---

# Phase 4 Plan 05: Session-Complete Endpoint + Idempotency Summary

**POST /api/sessions/{id}/complete with 5x-retry SC#2 proof — state-conditional UPDATE, canonical JSON idempotency dedup, and pending_rating_updates enqueue**

## Performance

- **Duration:** ~32 min (across two executor agents)
- **Started:** 2026-05-15 (prior agent — tasks 1-2)
- **Completed:** 2026-05-16 (this agent — task 3 debug + fix)
- **Tasks:** 3 of 3 committed
- **Files modified:** 14 created, 2 modified

## Accomplishments

- Full session-complete endpoint (RANK-11, D-07, D-08, D-22) landed across 3 commits
- SC#2 invariant proven: 5 identical POSTs → exactly 1 idempotency row + exactly 2 pending_rating_updates rows
- Bug found and fixed: raw SQL seed used `(int)GameSessionState.Active` = `1` but the column is `HasConversion<string>()` — stores `'Active'` text; the WHERE predicate in `ExecuteUpdateAsync` never matched, returning affected=0 on every first POST

## Task Commits

1. **Task 1: Core ports + SessionCompleteService + CanonicalJsonHasher** - `4975588` (feat)
2. **Task 2: SessionEndpoints + filters + rate-limit + builder wiring** - `445b3f7` (feat)
3. **Task 3: RankingsIdempotencyStore + PendingRatingUpdatesAdapter + integration tests** - `998297f` (test + fix)

## Files Created/Modified

- `src/GameKit.Core/Services/SessionCompleteService.cs` — orchestrates state-conditional UPDATE + idempotency + post-handler inside ReadCommitted tx
- `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` — port contract for post-completion side effects
- `src/GameKit.Core/Services/IIdempotencyStore.cs` — port contract for idempotency dedup storage
- `src/GameKit.Core/Services/ICanonicalRequestHasher.cs` — port contract for request body fingerprinting
- `src/GameKit.Core/Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs` — validates Idempotency-Key header (8-128 chars)
- `src/GameKit.Core/Http/EndpointFilters/ValidationEndpointFilter.cs` — invokes IValidator<T> in endpoint pipeline
- `src/GameKit.Core/Http/SessionEndpoints.cs` — maps POST /api/sessions/{id}/complete with filter chain
- `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` — factory registration for optional ports
- `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` — IPostSessionCompleteHandler enqueuing pending_rating_updates rows + RatingBefore snapshot
- `src/GameKit.Rankings/Services/RankingsIdempotencyStore.cs` — IIdempotencyStore persisting session_complete_idempotency rows within caller's tx
- `src/GameKit.Rankings/Json/CanonicalJsonHasher.cs` — SHA-256 canonical JSON fingerprinting
- `src/GameKit.Rankings/Http/Validators/SessionCompleteRequestValidator.cs` — FluentValidation for SessionCompleteRequest
- `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs` — 300 req/min policy keyed on service token name
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs` — wires all ports + validator + rate-limit policy
- `tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs` — 6 integration tests

## Decisions Made

- Optional ports use `GetService<T>` factory lambda so Core-only installs run in degraded mode (session state transition still works, no idempotency dedup or rating enqueue)
- `IIdempotencyStore.StoreAsync` and `IPostSessionCompleteHandler.OnCompletedAsync` both run inside the caller's ambient transaction — SaveChanges is called within the tx but Commit is the caller's responsibility
- Rate-limit policy keyed on service-token-name claim (not IP) at 300 req/min — prevents replayed-token abuse without punishing legitimate retries across different game servers

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed raw SQL seed using integer enum cast instead of string enum name**
- **Found during:** Task 3 (SessionCompleteIdempotencyTests debugging)
- **Issue:** `SeedActivatedSessionAsync` inserted `"State" = {(int)GameSessionState.Active}` = `1` (integer). `GameSession.State` is configured as `HasConversion<string>()` so the column stores `'Active'` (text). The `ExecuteUpdateAsync` WHERE clause `s.State == GameSessionState.Active` generates `WHERE "State" = 'Active'` — this never matched the integer `1`, so `affected` was always 0, triggering `InvalidState` → HTTP 409 on the very first POST.
- **Fix:** Changed seed SQL to `'{nameof(GameSessionState.Active)}'` and the Cancelled update to `'{nameof(GameSessionState.Cancelled)}'`. Also changed the state assertion from `QueryScalarAsync` (returns `long`) to `QueryScalarStringAsync` comparing `nameof(GameSessionState.Completed)`.
- **Files modified:** `tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs`
- **Verification:** All 6 tests green; 20/20 Rankings integration tests pass
- **Committed in:** `998297f`

---

**Total deviations:** 1 auto-fixed (Rule 1 — Bug)
**Impact on plan:** Fix was test-only (no production code changed). Required for the SC#2 anchor test to function.

## Issues Encountered

The prior executor agent stalled diagnosing why `Retry_Five_Times_Applies_Delta_Once` returned HTTP 409 on the first POST. The agent hypothesized an EF model mismatch caused by the missing `UseApplicationServiceProvider(sp)` call in the test server override. The actual cause was simpler: the seed SQL used the integer cast of `GameSessionState.Active` (`1`) but the database column stores the enum name as text (`'Active'`) due to `HasConversion<string>()` in `GameSessionConfiguration`. The EF-generated WHERE predicate does not match the integer seed value.

## SC#2 Proof

`Retry_Five_Times_Applies_Delta_Once`:
- 5 identical POSTs with same Idempotency-Key → all 5 return HTTP 200
- Exactly 1 row in `gamekit.session_complete_idempotency`
- Exactly 2 rows in `gamekit.pending_rating_updates` (one per participant)
- Session `State = 'Completed'`, `CompletedAt` non-null

## Next Phase Readiness

- Session-complete endpoint is complete and integration-tested
- `pending_rating_updates` rows are now enqueued; Phase 4 Plan 06 (rankings ticker) can drain them
- GDPR export (Plan 07) and session history endpoints (Plan 08) build on the completed `game_sessions` + `session_participants` + `pending_rating_updates` schema

---
*Phase: 04-rankings-sessions-gdpr*
*Completed: 2026-05-16*
