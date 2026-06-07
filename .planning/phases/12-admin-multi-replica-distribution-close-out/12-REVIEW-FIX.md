---
phase: 12-admin-multi-replica-distribution-close-out
fixed_at: 2026-06-06T00:00:00Z
review_path: .planning/phases/12-admin-multi-replica-distribution-close-out/12-REVIEW.md
iteration: 1
findings_in_scope: 4
fixed: 4
skipped: 0
status: all_fixed
---

# Phase 12: Code Review Fix Report

**Fixed at:** 2026-06-06T00:00:00Z
**Source review:** .planning/phases/12-admin-multi-replica-distribution-close-out/12-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 4
- Fixed: 4
- Skipped: 0

## Fixed Issues

### CR-01: `AdminBackplanePostConfigure` crashes single-instance installs on first hub connection

**Files modified:** `src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs`, `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`, `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`, `tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs`
**Commit:** d392b33
**Applied fix:** Two-layer fix for defense in depth:

1. Made `AddStackExchangeRedis` conditional in `AddGameKitAdmin`: checks `builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer))` at registration time. When no multiplexer is registered (single-instance install), `AddStackExchangeRedis` and `AdminBackplanePostConfigure` registration are both skipped — SignalR uses its default in-process backplane. This is the root fix; simply null-guarding `PostConfigure` alone was insufficient because `AddStackExchangeRedis` would still register the Redis backplane provider, which attempts a default-localhost connection on first hub use.

2. Updated `AdminBackplanePostConfigure.PostConfigure` to use `GetService<IConnectionMultiplexer>()` with a null guard (returns without setting `ConnectionFactory` when null) as defense-in-depth for scenarios where the registration check may not cover edge cases (e.g., factory-registered multiplexers).

3. Added `AdminTestHost.StartNoRedisAsync` overload that omits `IConnectionMultiplexer` registration entirely.

4. Added `AdminEventHubNoRedisTests.CR-01` regression test: starts a no-Redis host, confirms it comes up cleanly, then attempts an unauthenticated hub connection and asserts the response is 401/404 (not 500, which would indicate a crash in `PostConfigure`).

### WR-01: `queue.Unsubscribe()` is a blocking network call on the shutdown cancellation path

**Files modified:** `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs`
**Commit:** a1ad959
**Applied fix:** Removed `stoppingToken.Register(() => queue.Unsubscribe())`. Wrapped the `await foreach` in a `try/catch (OperationCanceledException) / finally` block. The `finally` block calls `await queue.UnsubscribeAsync().ConfigureAwait(false)` after the loop exits (whether by cancellation, normal completion, or exception). This eliminates the blocking synchronous unsubscribe on the shutdown thread and also removes the discarded `CancellationTokenRegistration` handle (resolving IN-01 simultaneously).

### WR-02: `CancellationTokenSource` instances in `RankAdjust.razor` are cancelled but never disposed

**Files modified:** `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor`
**Commit:** f652f0a
**Applied fix:** In `OnQueryChanged`, added `_cts?.Dispose()` immediately after `_cts?.Cancel()` before reassigning `_cts`. In `Dispose()`, expanded the expression body to a block and added `_cts?.Dispose()` after `_cts?.Cancel()`. Prevents kernel handle accumulation from undisposed `WaitHandle` instances in Blazor Server circuits under heavy search-debounce load.

### IN-01: `CancellationTokenRegistration` from `stoppingToken.Register` is not disposed

**Files modified:** `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs`
**Commit:** a1ad959 (same commit as WR-01)
**Applied fix:** Resolved by the WR-01 fix — removing `stoppingToken.Register` entirely eliminates the discarded registration.

---

_Fixed: 2026-06-06T00:00:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
