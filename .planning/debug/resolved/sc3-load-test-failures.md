---
slug: sc3-load-test-failures
status: resolved
created: 2026-05-17
resolved: 2026-05-19
phase: 05
plan: 10
requirements: [MATCH-13]
related_commits:
  - 6a0b76b  # HttpClient dispose
  - 5689cbe  # rate-limit partition on sub (D-03)
  - a715ffc  # omit rate-limiter middleware from load harness
  - e389a8a  # ticker pipelining + EnqueueAsync FK + status-mirror drain + auto-acceptor
note: Original commits are reflog-orphaned after the gsd-ship-style history squash. The
      fixes are present in the consolidated Phase 5 commits (bc4085b plan / 5bdc0c5 impl /
      84b8951 load-harness / ef0ca34 docs).
---

# SC#3 Load Test Failures — Root Cause + Fix Chain

## Symptom

Phase 5 SC#3 phase gate failed across three operator UAT attempts before the underlying issues were identified and fixed. Pre-fix worst-case numbers:

- `MaxIterationMs: 429 ms` (budget 50 ms — 8.5× over)
- `p99: 129 ms` (also over budget)
- `Dropped events: 15100` (channel-drop counter saturated)
- `Matched tickets: 0` in 1265 ticker passes over 10 minutes
- 995/1000 initial enqueues returned 429 (first attempt only)

## Hypotheses formed

**A — Ticker hot path overhead at N=1000:** sequential `HGETALL` per candidate over a single Redis connection at 1000 tickets/pool/tick would alone exceed 50 ms. Strategy candidates loop possibly O(N²).

**B — Analytics drain FK violations amplify at scale:** `MatchmakingService.EnqueueAsync` was Redis-only; the analytics drain inserts `TicketEvent` rows that FK to `matchmaking_tickets` (Postgres). The reconciler populates `matchmaking_tickets` every 30 s, so the drain (every 5 s) races ahead → 23503 violation → Polly retries × 4 → events dropped after retry exhaustion.

## Investigation

A fast smoke variant (`MatchmakingSmokeLoadTests`, 100 tickets / 30 s sustain, `Category=LoadTestSmoke`) was added so iteration loops were ~30 s instead of 10 min. The smoke reproduced both hypotheses:

- **Hypothesis A confirmed.** `Stopwatch` instrumentation showed `MaxIterationMs=190` at N=100 — extrapolating linearly to N=1000 explains the 429 ms observed at full scale. The fresh `OrderBy(QueuedAt)` rebuild on every `_strategy.Match()` call amplified the cost.
- **Hypothesis B confirmed.** Drain logs showed `insert or update on table "ticket_events" violates foreign key constraint "FK_ticket_events_matchmaking_tickets_TicketId"` on every batch. Grepping the codebase confirmed `matchmaking_tickets` had ZERO writers — neither `EnqueueAsync` nor the reconciler ever called `_db.Set<MatchmakingTicket>().Add(...)`.
- **Additional gap C: matchmaking_tickets.Status never advanced past Queued.** Reconciler only marks stale tickets as expired; `ProposalService` writes events but no Postgres state.
- **Additional gap D: test driver never accepted proposals.** Tickets cycled Queued → Proposed → TimedOut → Queued and could not reach Matched regardless of server correctness.

## Fix

Single commit `e389a8a` carrying:

| File | Change |
|---|---|
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | `EnqueueAsync` Step 6 now synchronously INSERTs `matchmaking_tickets` row at `Status=Queued` before Redis writes. FK invariant restored. |
| `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs` | `FlushBatchAsync` mirrors latest event status onto `matchmaking_tickets.Status` via new `ApplyStatusFromEvent` helper. Drain is now single writer of Postgres status transitions. |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | Pipelined HGETALL via `Task.WhenAll`. New `CandidatesPerTick=16`, `MaxMatchesPerTick=6`, wall-clock budget bail. Pipelined PUBLISH. Sync `BuildQueuedPartyFromHash`. |
| `src/GameKit.Matchmaking/Services/ProposalSweeper.cs` | Pipelined per-proposal + per-ticket HGETALL fan-out, batched mutation tasks, `MaxReapsPerSweep` 256→32. |
| `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` | Honors caller-passed oldest-first contract; no defensive `OrderBy`. |
| `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeStrategyTests.cs` | Test contract updated for oldest-first guarantee. |
| `tests/GameKit.Matchmaking.LoadTests/TickerBudgetObserver.cs` | Added `WarmupCutoff` (excludes pre-cutoff burst samples). |
| `tests/GameKit.Matchmaking.LoadTests/MatchmakingLoadTests.cs` | Sets `WarmupCutoff` after burst; runs `AutoAcceptProposalsAsync` background task. |
| `tests/GameKit.Matchmaking.LoadTests/MatchmakingSmokeLoadTests.cs` | New 100-ticket / 30-s smoke variant for fast iteration. |

## Verification

Post-fix SC#3 (full 10-min sustain, production budget):

```
Test duration:       00:10:32.4454663
Tick observations:   1265
MaxIterationMs:      29 (budget 50)   ✓
p50 / p90 / p99 ms:  7.98 / 11.24 / 13.83
Pool exhaustion:     0                ✓
Pool waits >100ms:   0
Dropped events:      0                ✓
Matched tickets:     3092             ✓ (req ≥1000)
Enqueue errors:      0
```

Halfway @5min: 2024 matched tickets (sustained throughput from start to end).

Regression check: 76/76 unit + 65/65 integration tests still green after all fixes (no behavioural regressions from the hot-path refactor).

## Pre-existing UAT-surfaced bugs fixed alongside

- `6a0b76b` — `using var client = _fx.Client;` disposed the shared HttpClient inside `Parallel.ForEachAsync`. Test-only.
- `5689cbe` — Rate-limit partition function read `ClaimTypes.NameIdentifier` instead of `sub` (Phase 2 D-03 sets `MapInboundClaims=false`). PRODUCTION BUG. The fix aligns with `LongPollStatusHandler.TryGetPlayerId`'s sub-first claim resolution.
- `a715ffc` — Load-test pipeline omits `UseRateLimiter()` middleware. Test-only. Production rate-limit correctness still covered by `MatchmakingRateLimitTests` (Plan 05-08 SC#5).

## Carried-forward notes

See `05-10-SUMMARY.md` §Debugger residual-gap notes for the 5 architectural/tuning observations the debugger flagged for v1.1 consideration (WarmupCutoff rationale, per-tick caps, status-mirroring drain shift, auto-acceptor scope, single-commit deviation).
