---
phase: 16-multi-replica-hardening
plan: "03"
subsystem: matchmaking-rankings-background-services
status: complete
tags: [scale-02, graceful-shutdown, lease-release, cancellation-token, static-gate]
dependency_graph:
  requires: [16-01-ILeaderLease, 16-02-idempotency]
  provides: [SCALE-02-fix, check-lease-release-token-gate]
  affects: [MatchmakerTickerService, MatchmakingReconcilerService, MatchmakingRetentionCleanupService, RankDecayBackgroundService, RankingsTickerService]
tech_stack:
  added: []
  patterns: [CancellationToken.None-on-finally-paths, static-grep-gate]
key_files:
  modified:
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
    - src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs
    - src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs
    - src/GameKit.Rankings/Services/RankDecayBackgroundService.cs
    - src/GameKit.Rankings/Services/RankingsTickerService.cs
  created:
    - scripts/check-lease-release-token.sh
decisions:
  - "CancellationToken.None is the correct token for finally-path lease releases; the stopping token is already cancelled when finally runs on SIGTERM"
  - "Static grep gate excludes comment lines to prevent self-invalidation"
  - "Gate uses 'violations' variable with || true to handle grep exit 1 on empty match under set -e"
metrics:
  duration: ~3m
  completed: 2026-06-23T01:17:18Z
  tasks_completed: 3
  tasks_total: 3
  files_changed: 6
---

# Phase 16 Plan 03: Graceful-Shutdown Lease-Release Fix (SCALE-02) Summary

**One-liner:** Five `finally`-path `ReleaseLeaseAsync(ct)` calls replaced with `CancellationToken.None` across all Matchmaking and Rankings background services, plus a static grep gate that prevents regression.

## What Was Built

SCALE-02 fixes the graceful-shutdown lease-release bug. Before this plan, every background service `finally` block called `ReleaseLeaseAsync(ct)` where `ct` is the `stoppingToken` — already cancelled when SIGTERM arrives. StackExchange.Redis cancels the release command before it is sent, so the Redis lock hangs until TTL expiry (90 s default), stalling leader re-election on the surviving replica by up to 90 s.

The fix is a one-token substitution at each of five sites: `ct` → `CancellationToken.None`. The `LockReleaseAsync` Lua script is a single atomic Redis command (~1 ms RTT) and does not need a cancellation budget, making `CancellationToken.None` safe and correct.

A shell script gate (`scripts/check-lease-release-token.sh`) prevents the pattern from regressing: it greps `src/**/*.cs` for the forbidden literal, excludes comment lines, and exits 1 on any match.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Replace stopping-token release in Matchmaking (3 services) | cf3ca13 | MatchmakerTickerService.cs, MatchmakingReconcilerService.cs, MatchmakingRetentionCleanupService.cs |
| 2 | Replace stopping-token release in Rankings (2 services) | d9d6a2a | RankDecayBackgroundService.cs, RankingsTickerService.cs |
| 3 | Add SCALE-02 static grep gate | faaeae1 | scripts/check-lease-release-token.sh |

## Acceptance Criteria Verification

- `grep -c "ReleaseLeaseAsync(CancellationToken.None)" MatchmakerTickerService.cs` → 1 ✓
- `grep -c "ReleaseLeaseAsync(CancellationToken.None)" MatchmakingReconcilerService.cs` → 1 ✓
- `grep -c "ReleaseLeaseAsync(CancellationToken.None)" MatchmakingRetentionCleanupService.cs` → 1 ✓
- `grep -c "ReleaseLeaseAsync(CancellationToken.None)" RankDecayBackgroundService.cs` → 1 ✓
- `grep -c "ReleaseLeaseAsync(CancellationToken.None)" RankingsTickerService.cs` → 1 ✓
- `grep -rc "ReleaseLeaseAsync(ct)" src/GameKit.Matchmaking/Services/` (3 files) → 0 ✓
- `grep -rc "ReleaseLeaseAsync(ct)" src/GameKit.Rankings/Services/` (2 files) → 0 ✓
- `dotnet build GameKit.Matchmaking.csproj -p:NuGetAudit=false` → Build succeeded, 0 errors ✓
- `dotnet build GameKit.Rankings.csproj -p:NuGetAudit=false` → Build succeeded, 0 errors ✓
- `bash scripts/check-lease-release-token.sh` → exits 0, "SCALE-02 OK" ✓

## Decisions Made

1. **CancellationToken.None is correct on finally paths.** The `LockReleaseAsync` Lua script is a single atomic Redis command (~1 ms RTT) with no long-running work to cancel. Passing `CancellationToken.None` ensures the release command is sent even during SIGTERM shutdown.

2. **Comments retained and expanded.** Existing comments at each site were retained and supplemented with a SCALE-02 annotation explaining why `CancellationToken.None` is correct, so future contributors understand the intent without reading the research doc.

3. **Shell gate uses `|| true` pattern** to handle `grep` returning exit code 1 when no matches are found, which would otherwise abort the script under `set -euo pipefail`. The violations are captured in a variable first, then counted via `grep -c .`.

## Deviations from Plan

None — plan executed exactly as written. All five sites were at the expected line numbers (within ±2 lines of documented approximations). Both packages compiled without warnings after the one-token substitutions.

## Known Stubs

None. This plan is a pure behavioral fix — no stubs, no placeholder text.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes were introduced. This plan removes a DoS-class bug (stale lock stalls re-election) without adding any new trust boundaries.

## Self-Check: PASSED

Files exist:
- scripts/check-lease-release-token.sh ✓
- src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs (modified) ✓
- src/GameKit.Rankings/Services/RankDecayBackgroundService.cs (modified) ✓

Commits exist:
- cf3ca13 ✓ (fix(16-03): Matchmaking)
- d9d6a2a ✓ (fix(16-03): Rankings)
- faaeae1 ✓ (chore(16-03): gate)

Gate: `bash scripts/check-lease-release-token.sh` → exit 0, "SCALE-02 OK" ✓
