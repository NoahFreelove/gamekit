---
phase: 16
fixed_at: 2026-06-22T00:00:00Z
review_path: .planning/phases/16-multi-replica-hardening/16-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 16: Code Review Fix Report

**Fixed at:** 2026-06-22
**Source review:** `.planning/phases/16-multi-replica-hardening/16-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 6
- Fixed: 6
- Skipped: 0

## Fixed Issues

### IN-01: Clarify RenewLeaseAsync fallback-only behavior

**Files modified:** `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs`
**Commit:** e2149f6
**Applied fix:** Replaced the terse `<remarks>` on `RenewLeaseAsync` with a two-paragraph XML doc explaining: (1) this is a fallback-only implementation — `MatchmakerLeaseHelper` (registered by `AddTickerServices`) is the real renewal path; (2) if `AddTickerServices` was skipped, every tick returns `LeaseLost`, which is safe but not silent.

---

### WR-05: Add ArgumentNullException.ThrowIfNull guards to 4 Rankings service constructors

**Files modified:**
- `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs`
- `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs`
- `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs`
- `src/GameKit.Rankings/Services/RankingsTickerService.cs`

**Commit:** 7f5bb85
**Applied fix:** Added `ArgumentNullException.ThrowIfNull(...)` for every constructor-injected parameter in all four services, matching the convention used in `MatchmakerLeaseHelper`, `ProposalService`, and other Matchmaking services. Guards fire at DI-graph build time with a clear parameter name rather than producing a NullReferenceException deep inside business logic.

---

### IN-02: Throw InvalidOperationException on impossible ON CONFLICT with missing row

**Files modified:** `src/GameKit.Matchmaking/Services/ProposalService.cs`
**Commit:** 0c29e32
**Applied fix:** On the code path where `rowsInserted == 0` (ON CONFLICT DO NOTHING fired) but `FirstOrDefaultAsync` returns `Guid.Empty` (no row found despite the conflict), the code now logs an error with full context (proposalId, idempotencyKey) and throws `InvalidOperationException` instead of returning the never-inserted `sessionId`. This prevents a dangling session reference being propagated to callers and makes the impossible-state loudly observable rather than silently incorrect.

---

### WR-04: Broaden SCALE-02 gate to negative-match any non-None token

**Files modified:** `scripts/check-lease-release-token.sh`
**Commit:** 333ddb5
**Applied fix:** Rewrote the gate from a single-literal positive-match (`grep 'ReleaseLeaseAsync(ct)'`) to a negative-match strategy: collect all `ReleaseLeaseAsync(` call-sites, remove the correct ones (`ReleaseLeaseAsync(CancellationToken.None)`), remove interface/abstract declaration lines, and flag everything that remains as a violation. The new gate catches any token variable name (`ct`, `stoppingToken`, `token`, etc.). Verification: gate passes on the clean tree and exits 1 when a deliberate `ReleaseLeaseAsync(stoppingToken)` injection is present (confirmed by inject-test-revert cycle).

---

### WR-01: Make split-brain test non-vacuous with staged execution

**Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs`
**Commit:** b8e0228
**Applied fix:**
- Added `PostConfigure<GameKitMatchmakingOptions>(o => o.Ticker.TickIntervalMs = 3_600_000)` in `InitializeAsync` for BOTH apps, so background `PeriodicTimer` never fires spontaneously. Only explicit `RunOnceAsync` calls drive the test.
- Restructured the race: start AppA's tick first (acquires lock, stalls 3s at `BeforeLuaClaim`), await `Task.Delay(lockTtlSeconds + 500 ms = 2500 ms)` for AppA to hold and TTL to expire, then start AppB's tick (acquires the now-free lock, forms the match).
- Added `Assert.True(matchedCount >= 1, ...)` precondition to fail loudly if neither ticker formed a match (indicates broken setup rather than vacuous pass).

The SCALE-04 scenario now genuinely executes: `TickerA=LeaseLost` (LEASE_LOST from Lua atomic-claim after TTL expiry), `TickerB=Matched` (one game_sessions row). SCALE-03 idempotency test unaffected. Both pass.

---

### WR-02: Make graceful-drain test non-vacuous with lock-held assertion

**Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/GracefulDrainTests.cs`
**Commit:** b8e0228 (same commit as WR-01)
**Applied fix:** Added a pre-stop polling phase: before calling `StopHostAsync()`, open a fresh `ConnectionMultiplexer` to the test Redis, poll `_app.MatcherLockKey` every 50ms for up to 10s until the key is present. Assert `lockHeld == true` with a descriptive message. Only then start the 100 concurrent requests and stop the host.

The post-stop absence of the lock key now non-vacuously proves proactive release via `CancellationToken.None` — the key was genuinely held before stop, then proactively released by the `finally` block, rather than "the lock was never held" (which would also produce an absent key but prove nothing about SCALE-02).

---

## Skipped Issues

None — all 6 in-scope findings were fixed.

---

_Fixed: 2026-06-22_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
