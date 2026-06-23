---
phase: 16
slug: multi-replica-hardening
reviewed: 2026-06-22
depth: standard
status: findings
critical: 0
warnings: 6
info: 2
---

# Phase 16 — Code Review

Production correctness is sound: the `ON CONFLICT ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL DO NOTHING` predicate exactly mirrors the partial index; `CancellationToken.None` is applied at all five finally-path `ReleaseLeaseAsync` sites; the `ILeaderLease` hierarchy compiles cleanly and DI ordering resolves `IMatchmakerLease` → `MatchmakerLeaseHelper` (with real renewal) in production. The notable risk is **test vacuity** on the two new CI gates.

## Findings & Triage

| ID | Sev | File | Issue | Decision |
|----|-----|------|-------|----------|
| WR-01 | Warning | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs` | SCALE-04 split-brain test can pass **vacuously**: if AppB returns `LockNotAcquired` before AppA's TTL expires, `sessionCount=0 & matchedCount=0` and the `<=1` assertion proves nothing. | **FIX** — stage AppB's tick to run after TTL expiry; assert `matchedCount>=1` precondition. Re-run, must still pass non-vacuously. |
| WR-02 | Warning | `tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs` | SCALE-05 drain test's "lock absent after stop" is **vacuous** if no ticker tick ran before shutdown (500ms first-tick delay). | **FIX** — assert the ticker actually held the lock before stop (heartbeat/immediate-tick), so absence proves proactive release. Re-run. |
| WR-04 | Warning | `scripts/check-lease-release-token.sh` | SCALE-02 gate only matches literal `ReleaseLeaseAsync(ct)`; misses other token var names; no CI wiring. | **FIX** — negative-match any `ReleaseLeaseAsync(...)` not passing `CancellationToken.None`. |
| WR-05 | Warning | 4 Rankings service ctors (`RankDecayLeaseHelper`, `RankingsTickerLeaseHelper`, `RankDecayBackgroundService`, `RankingsTickerService`) | Missing `ArgumentNullException.ThrowIfNull` guards — inconsistent with the Matchmaking services' convention. | **FIX** — add guards (cheap, improves DI-misconfig diagnostics). |
| IN-02 | Info | `src/GameKit.Matchmaking/Services/ProposalService.cs` | On the (logically impossible) ON CONFLICT-fired-but-no-row path, returns an orphaned `sessionId`. | **FIX** — log + throw instead of returning an un-inserted id. |
| IN-01 | Info | `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` | `RenewLeaseAsync` returns `false` unconditionally — footgun if `AddTickerServices` is skipped. | **FIX (doc)** — clarify the fallback-only behavior in XML doc. |
| WR-03 | Warning | `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs:293` (`SweepOrphanSessionsAsync`) | `orphans.Take(cancelled)` positional audit slice could misattribute if `Cancel()` throws. | **DEFER** — pre-existing code (NOT touched by Phase 16); currently unreachable (`Active→Cancelled` always permitted). Logged as tech-debt. |
| WR-06 | Warning | 4 lease helpers | `ParseLeaseStatus` + `QueryLeaseScript` duplicated verbatim. | **DEFER** — deliberate per plan/research ("copy verbatim"); maintainability-only. Extract to a Core helper in a future cleanup. |

## REVIEW COMPLETE
status: findings — 6 fixes applied below, 2 deferred (WR-03 pre-existing, WR-06 deliberate).
