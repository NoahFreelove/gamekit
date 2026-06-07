---
phase: 12-admin-multi-replica-distribution-close-out
reviewed: 2026-06-06T00:00:00Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - src/GameKit.Admin.UI/Hubs/AdminEventHub.cs
  - src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs
  - src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs
  - src/GameKit.Admin.UI/Services/RedisErrorRateCounter.cs
  - src/GameKit.Admin.UI/Services/IRedisErrorRateCounter.cs
  - src/GameKit.Admin.UI/Services/LogErrorCounter.cs
  - src/GameKit.Admin.UI/Services/HealthProbeService.cs
  - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
  - src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs
  - src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor
findings:
  critical: 1
  warning: 2
  info: 1
  total: 4
status: issues_found
---

# Phase 12: Code Review Report

**Reviewed:** 2026-06-06T00:00:00Z
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

Phase 12 adds SignalR Redis backplane support for multi-replica admin event broadcast (`AdminEventHub` + `AdminLiveBroadcastService`), cross-replica error rate counting (`RedisErrorRateCounter`), and the `RankAdjust.razor` close-out page. The security posture of `AdminEventHub` is sound: `[Authorize(Policy = AdminPolicies.Admin)]` correctly pins the `GameKitAdmin` cookie scheme via `AddAuthenticationSchemes`, the hub declares no server-callable methods (receive-only), and the Redis channel name is a compile-time constant. `RedisErrorRateCounter` implements the sliding-window bucket logic correctly and returns `-1` as a safe sentinel on Redis failure. `RankAdjust.razor` is properly gated by `[Authorize(Policy = AdminPolicies.Superadmin)]`, follows the established `IDialogService.ShowAsync<T>` pattern, and uses the existing `RankAdjustDialog` without re-implementing it.

One blocker was found: `AdminBackplanePostConfigure.PostConfigure` calls `GetRequiredService<IConnectionMultiplexer>` unconditionally, but `AddStackExchangeRedis` (and the post-configure itself) is registered without any Redis guard in `AddGameKitAdmin`. On a single-instance install that has no `IConnectionMultiplexer` in DI, the first WebSocket upgrade to `AdminEventHub` causes the options system to resolve `RedisOptions`, which fires `PostConfigure`, which throws `InvalidOperationException`. The documented claim that "single-instance installs without Redis start cleanly" is only true for `AdminLiveBroadcastService` (which has a proper null-mux guard); the SignalR backplane path is not guarded.

Two warnings were found: a blocking synchronous `queue.Unsubscribe()` call inside a `CancellationToken.Register` callback on the shutdown path, and undisposed `CancellationTokenSource` instances in `RankAdjust.razor`.

## Critical Issues

### CR-01: `AdminBackplanePostConfigure` crashes single-instance installs on first hub connection

**File:** `src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs:51`

**Issue:** `PostConfigure` calls `_sp.GetRequiredService<IConnectionMultiplexer>()` unconditionally. `AddGameKitAdmin` unconditionally calls `AddStackExchangeRedis` (line 188–192 of `AdminBuilderExtensions.cs`) and unconditionally registers `AdminBackplanePostConfigure` via `TryAddEnumerable` (lines 193–195). On a single-instance install with no `IConnectionMultiplexer` registered, everything appears healthy until the first WebSocket upgrade to `AdminEventHub`: at that point ASP.NET Core resolves `IOptions<RedisOptions>`, which fires `PostConfigure`, which throws `InvalidOperationException: No service for type 'StackExchange.Redis.IConnectionMultiplexer'`. The test host always registers a real `IConnectionMultiplexer`, so this path is not exercised by tests.

`AdminLiveBroadcastService` is correctly guarded (line 66: `if (_mux is null) return;`), but that guard does not protect the SignalR backplane.

**Fix:** Use `GetService` with a null guard in `PostConfigure`. When no multiplexer is available, leave `options.ConnectionFactory` at its default so SignalR falls back to the in-process (non-Redis) backplane — which is exactly the correct behaviour for a single-instance install:

```csharp
// AdminBackplanePostConfigure.cs
public void PostConfigure(string? name, RedisOptions options)
{
    var mux = _sp.GetService<IConnectionMultiplexer>();
    if (mux is null) return;  // single-instance install — in-process backplane only
    options.ConnectionFactory = _ => Task.FromResult(mux);
}
```

## Warnings

### WR-01: `queue.Unsubscribe()` is a blocking network call on the shutdown cancellation path

**File:** `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs:73`

**Issue:** `stoppingToken.Register(() => queue.Unsubscribe())` runs synchronously on whichever thread cancels the token (the host's shutdown thread). `ChannelMessageQueue.Unsubscribe()` is a synchronous method that sends the Redis `UNSUBSCRIBE` command and waits for acknowledgement. Blocking the shutdown thread for an in-flight Redis round-trip risks exceeding the default 5-second `StopAsync` grace period in degraded-network scenarios. SE.Redis 2.8.41 exposes `UnsubscribeAsync` on `ChannelMessageQueue`.

**Fix:** Register the callback asynchronously. Because `CT.Register` does not accept an async delegate, fire-and-forget inside the registration and let the `await foreach` exit via the cancellation token:

```csharp
stoppingToken.Register(() => _ = queue.UnsubscribeAsync());
```

Or, if a cleaner shutdown guarantee is needed, move the unsubscribe call to after the foreach exits:

```csharp
try
{
    await foreach (var message in queue.WithCancellation(stoppingToken))
    { /* ... relay ... */ }
}
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
finally
{
    await queue.UnsubscribeAsync().ConfigureAwait(false);
}
```

The `finally` form also eliminates the need for `CT.Register` entirely.

### WR-02: `CancellationTokenSource` instances in `RankAdjust.razor` are cancelled but never disposed

**File:** `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor:70-71, 111`

**Issue:** In `OnQueryChanged`, the old `_cts` is cancelled and immediately overwritten with a new instance. The old instance is never disposed. In `Dispose()`, the current `_cts` is cancelled but not disposed. `CancellationTokenSource` implements `IDisposable`; undisposed instances hold a `WaitHandle` (a kernel object on Windows, a file descriptor on Linux) until the GC finalizer runs. In a Blazor Server circuit with a busy search field, many query-debounce events can accumulate unreleased handles before GC pressure triggers finalization.

**Fix:** Dispose each superseded `CancellationTokenSource` before abandoning it, and dispose in `Dispose()`:

```csharp
private async Task OnQueryChanged()
{
    _cts?.Cancel();
    _cts?.Dispose();
    _cts = new CancellationTokenSource();
    // ...
}

public void Dispose()
{
    _cts?.Cancel();
    _cts?.Dispose();
}
```

## Info

### IN-01: `CancellationTokenRegistration` from `stoppingToken.Register` is not disposed

**File:** `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs:73`

**Issue:** `CancellationToken.Register` returns a `CancellationTokenRegistration` that implements `IDisposable`. The returned registration is discarded. Microsoft's guidance is to dispose registrations when the registrant's lifetime ends before the token's. Here both the `BackgroundService` and `stoppingToken` share the host lifetime, so the practical leak is zero; however the pattern is inconsistent with the recommended disposal idiom and would become a real leak if the implementation is ever refactored (e.g., subscribe/unsubscribe in a loop).

**Fix:** If WR-01 is resolved by moving to a `finally` block, this registration is eliminated entirely. If `CT.Register` is retained, capture and dispose the registration:

```csharp
using var reg = stoppingToken.Register(() => _ = queue.UnsubscribeAsync());
await foreach (var message in queue.WithCancellation(stoppingToken))
{ /* ... */ }
```

---

_Reviewed: 2026-06-06T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
