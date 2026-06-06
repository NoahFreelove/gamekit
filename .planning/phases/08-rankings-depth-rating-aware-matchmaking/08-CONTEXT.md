# Phase 8: Rankings Depth + Rating-Aware Matchmaking - Context

**Gathered:** 2026-06-05
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss; enriched from .planning/research/ + Phase 7 outcomes)

<domain>
## Phase Boundary

Finalize the `player_ranks` schema (decay + placement columns) and make matchmaking rating-aware by consuming the Phase 7 `IPlayerRatingProvider` seam — WITH anti-feedback-loop guardrails shipped in the same unit. This phase FREEZES `player_ranks` (Phase 10 account-merge reads it; no later structural change). Requirements: RANK-15, RANK-16, RANK-17, MATCH-16, MATCH-17.
</domain>

<decisions>
## Implementation Decisions

### Schema — player_ranks migration (RANK-15/16) [Rankings package]
- A single new Rankings migration adds: `last_decay_at` (timestamptz null), `placement_matches_remaining` (int, default = configured N), `is_in_placement` (bool, default true for new ranks). Timestamp follows the deterministic convention AFTER the latest existing Rankings migration. Uses the EXISTING Rankings advisory lock (-156812172) — NO new lock key. Update the Rankings model snapshot + entity config together. Rankings owns player_ranks (does not touch Core/Auth tables).
- This is the LAST structural change to player_ranks (SC#5). Phase 10 will read these columns, not alter them.

### RANK-15 — Rank decay (RD inflation, not point loss)
- Decay = Glicko-2's native "no games played" period update (RD grows toward the default via the `φ' = √(φ² + σ²)` inactivity step) — rating stays constant, RD inflates. Applied only to players above a configurable rating threshold whose inactivity exceeds a configurable window; stamps `last_decay_at`.
- Runs in a leader-elected `BackgroundService` mirroring the v1 `MatchmakerTickerService` pattern (Redis `SET NX PX` distributed lock + Polly backoff) so multi-replica hosts run exactly one decay runner. Configurable interval. Batched updates (reuse the batched `IRankingAlgorithm.Apply` precedent — no per-player round trips).

### RANK-16 — Placement matches
- New ranks start `is_in_placement = true`, `placement_matches_remaining = N` (configurable). Session-complete decrements `placement_matches_remaining` ATOMICALLY (inside the existing session-complete transaction / SaveChanges); when it reaches 0, set `is_in_placement = false`.
- Visible rank is HIDDEN in API/DTO responses while `is_in_placement` (return null/"unranked" rating in the rank read DTOs), but the underlying Glicko-2 state still updates each game.

### RANK-17 — RankingsRatingSource (the seam implementation)
- `RankingsRatingSource : IPlayerRatingProvider` in GameKit.Rankings maps `player_ranks` rows → Core `PlayerRatingValue` (Rating/RatingDeviation/Volatility) for the requested ladder, omitting players with no rank row.
- Opt-in via `.WithRatingsFrom<RankingsRatingSource>()` on the Rankings builder. MUST register with `services.RemoveAll<IPlayerRatingProvider>(); services.AddSingleton<IPlayerRatingProvider, RankingsRatingSource>();` — NOT TryAdd (Core already registered the null-object via TryAddSingleton, so TryAdd would be a no-op; see Phase 7 07-review IN-02). Omitting the call leaves the v1 zero-rating null-object → SC#3 fallback.
- Lifetime: the source reads the scoped GameKitDbContext, so if it must be Singleton it has to resolve a scope per call (mirror how other singletons touch the DbContext) — planner to choose Scoped vs Singleton+IServiceScopeFactory consistent with v1 precedent and the IPlayerRatingProvider registration above.

### MATCH-16 — Rating-aware EloRange (consume the seam)
- `MatchmakingService.EnqueueAsync` resolves `IPlayerRatingProvider.GetRatingsAsync(playerIds, ladderId)` and writes each member's real Rating (and RD if used) into the Redis ticket hash, REPLACING the hardcoded `Rating: 0` (research cited ~MatchmakingService.cs:203). The ticker (`BuildQueuedPartyFromHash`) + `EloRangeMatchmakingStrategy` already consume real ratings — minimal/no change there.
- Matchmaking consumes the CORE `IPlayerRatingProvider` interface ONLY — NO hard ProjectReference to GameKit.Rankings (preserves package independence; null-object default when Rankings absent).

### MATCH-17 — Anti-feedback-loop guardrails (ship WITH MATCH-16)
- Add `MaxBracketWidth` (hard cap on EloRange bracket expansion) and `MinPoolDepthBeforeBracketExpansion` (don't widen until the pool has ≥ N candidates) to the EloRange / matchmaking options. Enforced in the same phase/wave as the rating wire-up — NOT a follow-up (prevents new high-RD players funnelling into top-rated matches on sparse pools). Integration test: bracket expansion stops at MaxBracketWidth regardless of pool depth.

### Claude's Discretion
Exact option-class field names, DTO shaping for hidden placement rank, decay batch size/interval defaults, and test structure are at Claude's discretion — follow v1 patterns (MatchmakerTickerService leader election, batched IRankingAlgorithm, EloRange options, session-complete transaction). Research basis: .planning/research/ARCHITECTURE.md, FEATURES.md, PITFALLS.md, SUMMARY.md.
</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets / Patterns to mirror
- src/GameKit.Core/Services/IPlayerRatingProvider.cs + NullPlayerRatingProvider (Phase 7 seam — RankingsRatingSource implements it; returns `PlayerRatingValue`).
- src/GameKit.Matchmaking/ MatchmakingService.EnqueueAsync (hardcoded rating=0 — the injection point) + MatchmakerTickerService (leader election SET NX PX — copy for the decay runner) + EloRangeMatchmakingStrategy + the Redis ticket hash schema.
- src/GameKit.Rankings/ IRankingAlgorithm (batched Apply), PlayerRank entity + config + existing Rankings migration + advisory-lock constant (-156812172), session-complete service (RANK-16 decrement site), seasonal reset precedent.
- Per-package migration pattern (advisory lock + __ef_migrations_rankings + design-time factory + ExcludeFromMigrations prior packages).

### Integration Points
- RankingsRatingSource registration overrides the Core null-object via RemoveAll+AddSingleton.
- MATCH-16 reads the Core seam at enqueue; caches into Redis ticket hash.
- Placement decrement rides the existing session-complete transaction.

### Pitfalls (from research)
- Feedback loop: guardrails (MaxBracketWidth, MinPoolDepthBeforeBracketExpansion) MUST ship with the rating wire-up, not after.
- Decay must inflate RD, not subtract rating (fairness + Glicko-2 correctness).
- Do NOT add a hard Matchmaking→Rankings dependency.
- player_ranks migration must not touch Core tables; reuse the existing Rankings advisory lock.
</code_context>

<specifics>
## Specific Ideas
- player_ranks is FROZEN after this phase (SC#5) — Phase 10 reads, never alters.
- Decay + matchmaking-consumption integration tests should use Testcontainers Postgres (+ Redis for the ticket-hash rating assertion). Glickman inactivity formula unit test for RD inflation.
</specifics>

<deferred>
## Deferred Ideas
- Regional pools + backfill → Phase 9.
- Account merge reading player_ranks → Phase 10.
</deferred>
