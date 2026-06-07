# Phase 12: Admin Multi-Replica + Distribution Close-Out — Research

**Researched:** 2026-06-06
**Domain:** ASP.NET Core SignalR backplane, Redis-backed error counter, Blazor Server multi-replica, rank-adjust UI wiring, MinVer release-train extension
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None explicitly locked — discuss phase was skipped. All implementation choices are at Claude's discretion guided by the ROADMAP, success criteria, and codebase conventions.

### Claude's Discretion
All implementation choices. The AdminEventHub is a NEW hub, NOT the Blazor Server circuit transport. SC#3 rank-adjust page must follow existing Admin UI patterns (MudBlazor, violet-600 accent, density tokens, BanPlayerDialog/PlayerDetailPane patterns). SC#4 is wiring work only — the 5 packages' csproj structures already have the GameKit.Build analyzer reference; they need no new NuGet packages added.

### Deferred Ideas (OUT OF SCOPE)
None — discuss phase skipped. Redis (not Azure SignalR) is locked by GPL zero-cloud constraint. Data Protection key sharing is a DOCUMENTATION deliverable only — no code feature.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| ADMIN-13 | Multi-replica Admin UI — SignalR + Redis backplane (never Azure SignalR); sticky-session requirement documented | §SignalR Wiring, §Blazor Server Multi-Replica, §Ops Docs |
| ADMIN-14 | Replace in-memory `ErrorRateRingBuffer` with Redis-backed counter (`INCRBY` on time-bucketed keys) so health panel is correct across replicas | §Redis Error Counter Design, §Coexistence Strategy |
| ADMIN-15 | Replace dead "Rank adjust" stub nav page with working flow wiring existing `IRankAdjustService`; Admin hub uses distinct hub + `[Authorize]` policy from Lobby hub | §RankAdjust Page, §AdminEventHub Auth |
| DIST-07 | Five new packages (`GameKit.Auth.Argon2`, `.Google`, `.Apple`, `.Epic`, `GameKit.Lobby`) join coordinated MinVer release train — same version, exact-pinned `[X.Y.Z]` sibling refs; `GameKitVersionAssertionHostedService` covers them | §Version Train Audit |
</phase_requirements>

---

## Summary

Phase 12 is four independent work streams that share no code dependencies on each other: (1) Redis-backed error counter replacing the in-memory ring buffer, (2) a new `AdminEventHub` SignalR hub on the Redis backplane, (3) fixing the dead `RankAdjust.razor` stub page, and (4) confirming the five new packages are on the release train. All four streams operate within `GameKit.Admin.UI`, and streams 1–3 also touch `tests/GameKit.Admin.Integration.Tests`.

The central insight for SC#1 is that `ErrorRateRingBuffer` and `LogErrorCounter` exist and work today — the Redis counter is an **opt-in extension** for multi-replica deployments, not a full replacement. `HealthProbeService` must be taught to prefer `IRedisErrorRateCounter` when available, but fall back to `ErrorRateRingBuffer` for single-instance installs. This preserves the zero-dependency default for operators who don't need cross-replica aggregation.

SC#2 (AdminEventHub) is the most complex stream. Blazor Server already uses SignalR internally for its interactive circuit — the `/_blazor` endpoint. `AddRazorComponents().AddInteractiveServerComponents()` calls `AddSignalR()` under the hood. Phase 12 adds a SECOND SignalR surface: a programmatic `AdminEventHub` at `/admin/hubs/events` that relays Redis Pub/Sub into connected admin browser sessions. The two surfaces share one `AddSignalR()` registration (idempotent in ASP.NET Core) and one `AddStackExchangeRedis()` backplane. The `AdminEventHub` is gated by the `GameKitAdmin` COOKIE scheme — NOT the player JWT Bearer scheme — using `[Authorize(Policy = AdminPolicies.Admin)]` or `[Authorize(Policy = AdminPolicies.Superadmin)]`. The `AdminLiveBroadcastService` is a `BackgroundService` that subscribes to Redis channel `"gamekit:admin:events"` via `IConnectionMultiplexer.GetSubscriber().Subscribe(...)` and calls `IHubContext<AdminEventHub>.Clients.All.SendAsync(...)` to relay each message.

SC#3 is simpler than it looks: `RankAdjust.razor` is a stub page with a `Type.GetType` reflection check, but `RankAdjustDialog.razor` already exists and is fully wired to `IRankAdjustService` via direct DI injection. The fix is to replace the stub `RankAdjust.razor` body with a player-search form + `IDialogService.ShowAsync<RankAdjustDialog>(...)` flow — following the same pattern as `PlayerDetailPane.razor` launching `BanPlayerDialog.razor`. The dialog already handles ladder selection, validation, service call, and audit row.

SC#4 is confirmed: all five new packages already have `GameKit.Build` analyzer references in their csproj files. `GameKitVersionAssertionHostedService` works by reflection — it discovers any loaded assembly whose name starts with `GameKit.` and reads `{AssemblyName}.Internal.GameKitMarker.GameKitVersion`. No code changes are needed in the assertion service. The only SC#4 work is verifying each package's `PackageId`, `AssemblyName`, and the `GameKit.Build` analyzer reference are correctly wired (they are) and writing a test that asserts all five appear in the version assertion's output.

**Primary recommendation:** Plan four independent waves — SC#4 first (pure verification, low risk), then SC#3 (UI-only, no new NuGet), then SC#1 (Redis counter, additive), then SC#2 (AdminEventHub, most complex). No new NuGet packages are needed — `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 is already in `Directory.Packages.props` (added in Phase 11) and `StackExchange.Redis` is already pinned.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Cross-replica error-rate aggregation | Redis (INCRBY on time-bucketed keys) | Admin server (HealthProbeService reads aggregate) | In-memory ring buffer is per-process; Redis provides a shared counter all replicas write to |
| Admin live-event relay (SC#2) | Redis Pub/Sub (publisher) + SignalR hub (relay to browser) | `BackgroundService` (subscriber bridge) | Redis decouples producers from consumers across replicas; SignalR delivers to browser connections |
| Admin browser session transport | Blazor Server circuit (SignalR `/_blazor`) | — | Blazor's interactive circuit is already SignalR; the AdminEventHub is an ADDITIONAL hub endpoint |
| AdminEventHub authorization | Admin cookie scheme (`GameKitAdmin`) | ASP.NET Core authz policy | Must NOT accept player JWT — `[Authorize(Policy=AdminPolicies.Admin)]` with the cookie scheme pinned |
| Rank-adjust UI flow | Blazor Server (RankAdjust.razor + RankAdjustDialog.razor) | Rankings service layer (IRankAdjustService) | Blazor circuit calls IRankAdjustService via direct DI injection (same pattern as BanPlayerDialog) |
| Rank-adjust data persistence | Database (PlayerRank + AdminAuditLog via RankAdjustService) | — | RankAdjustService already does SERIALIZABLE tx + audit row write |
| Version train coherence | Build-time (GameKit.targets exact-pin) + Runtime (GameKitVersionAssertionHostedService) | — | GameKit.targets already rewrites sibling refs to `[X.Y.Z]` at Pack time; assertion service reflects at host start |
| Data Protection key sharing (multi-replica Blazor) | Ops documentation | Consumer infra (Postgres/Redis/file share) | Not a code feature — operator configures `AddDataProtection()` in their host; Admin UI documents the requirement |

---

## Standard Stack

### Core (no new packages — all already in Directory.Packages.props)

| Library | Version | Purpose | Status |
|---------|---------|---------|--------|
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | **10.0.8** | Redis backplane for `AdminEventHub` | Already pinned in `Directory.Packages.props` (added Phase 11) [VERIFIED: codebase] |
| `StackExchange.Redis` | **2.8.41** | `IConnectionMultiplexer` for Redis error counter + backplane | Already pinned [VERIFIED: codebase] |
| `Microsoft.AspNetCore.App` (shared framework) | net10.0 | `Hub<T>`, `IHubContext<T>`, `BackgroundService`, `IHubContext` | Already in all packages [VERIFIED: codebase] |

### No new NuGet dependencies for Phase 12

`Microsoft.AspNetCore.SignalR.Client` was added to `Directory.Packages.props` in Phase 11 for Lobby integration tests. The Admin integration tests also need it for AdminEventHub testing (two-TestServer backplane test). It is already available in CPM [VERIFIED: codebase Phase 11 research].

**Installation:** None required. All packages already in `Directory.Packages.props`.

---

## Package Legitimacy Audit

No new packages. This phase introduces zero new NuGet dependencies. The packages used (`Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8, `StackExchange.Redis` 2.8.41) were both verified as legitimate in Phase 11 research [VERIFIED: Phase 11 research + nuget.org].

**Packages removed due to slopcheck [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

---

## Architecture Patterns

### System Architecture Diagram

```
Admin Browser (Blazor circuit)                Admin Server Instance A           Admin Server Instance B
        │                                               │                                    │
        │  wss://.../admin/hubs/events                  │                                    │
        │  Cookie: gk_admin_session                     │                                    │
        │ ────────────────────────────────────────────>│                                    │
        │                                               │ [Authorize(Policy=Admin)]           │
        │                                               │ GameKitAdmin cookie scheme          │
        │                                               │ [FAIL → 401]  [PASS → upgrade]     │
        │                                               │                                    │
        │  Connected to hub group "admin"               │                                    │
        │                                               │                                    │
        ├── Health probe request ────────────────────>  │                                    │
        │                                               │ HealthProbeService.ProbeAsync       │
        │                                               │   ProbeRedisErrorRateAsync:         │
        │                                               │     Redis GETSET gamekit:admin:     │
        │                                               │       errors:BUCKET → count         │
        │                                               │   (cross-replica aggregate read)    │
        │ <── HealthReport.ErrorRate ─────────────────  │                                    │
        │                                               │                                    │
        │                                               │  AdminLiveBroadcastService          │
        │                                               │  (BackgroundService)                │
        │                                               │   ISubscriber.Subscribe(            │
        │                                               │     "gamekit:admin:events")         │
        │                                               │            │                        │
        │                           Redis Pub/Sub channel "gamekit:admin:events"              │
        │                                               │            │                        │
        │                                               │            ▼                        │
        │  ReceiveEvent(payload) ─────────────────────  │  IHubContext<AdminEventHub>         │
        │                                               │    .Clients.All.SendAsync(...)      │
        │                                               │                                    │
        │                           Redis backplane (SignalR channels GameKit:*)              │
        │                                         ──────────────────────────────────────────>│
        │                                                                      relay to other │
        │                                                                      admin sessions │
```

### SC#1: RedisErrorRateCounter Design

**Architecture:** Additive opt-in, not a replacement. When `AddGameKitAdmin()` detects a registered `IConnectionMultiplexer`, it additionally registers `RedisErrorRateCounter` and teaches `HealthProbeService` to read from it. The in-memory `ErrorRateRingBuffer` + `LogErrorCounter` remain registered and continue to receive increments via the `ILoggerProvider` path — they serve as the local write side. The Redis counter is a SEPARATE write destination that `LogErrorCounter` also writes to when configured.

**Design decision: dual-write vs. Redis-only.** The simplest design is: `LogErrorCounter` continues to call `_buf.IncrementError()` AND, when `IRedisErrorRateCounter` is registered, also calls `_redisCounter.IncrementAsync()` (fire-and-forget, never throws). `HealthProbeService` reads from `IRedisErrorRateCounter` when available, otherwise falls back to `ErrorRateRingBuffer.RecentErrorCount()`.

**Redis key schema:** Time-bucketed counters. Bucket width = `GameKitAdminOptions.Panel.HealthErrorRateBucketSize` (default 1s). Key = `gamekit:admin:errors:{epoch_bucket}` where `epoch_bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / bucketWidthSeconds`. Expiry = window width + 1 extra bucket (defensive TTL). Read side: sum all keys for the current window range = `MGET gamekit:admin:errors:{now - windowBuckets} ... {now}`.

**Concrete implementation:**

```csharp
// Source: [ASSUMED] — Redis INCRBY pattern is standard, no official docs ref needed

/// <summary>
/// Redis-backed error-rate counter for multi-replica deployments (ADMIN-14).
/// Writes are fire-and-forget (never throws on Redis failure — degrades to in-memory only).
/// Reads sum the current sliding window using MGET on per-second bucket keys.
/// </summary>
public interface IRedisErrorRateCounter
{
    /// <summary>Increments the current time bucket. Fire-and-forget — must not throw.</summary>
    void IncrementError();

    /// <summary>Returns the aggregate error count across all replicas for the current window.</summary>
    Task<long> RecentErrorCountAsync(CancellationToken ct = default);
}

internal sealed class RedisErrorRateCounter : IRedisErrorRateCounter
{
    private readonly IConnectionMultiplexer _mux;
    private readonly long _bucketWidthSeconds;
    private readonly int _bucketCount;
    private readonly TimeSpan _keyTtl;

    public RedisErrorRateCounter(IConnectionMultiplexer mux, GameKitAdminOptions opts)
    {
        _mux = mux;
        _bucketWidthSeconds = (long)opts.Panel.HealthErrorRateBucketSize.TotalSeconds;
        if (_bucketWidthSeconds < 1) _bucketWidthSeconds = 1;
        _bucketCount = (int)Math.Ceiling(
            opts.Panel.HealthErrorRateWindow.TotalSeconds / _bucketWidthSeconds);
        _keyTtl = opts.Panel.HealthErrorRateWindow + opts.Panel.HealthErrorRateBucketSize;
    }

    public void IncrementError()
    {
        // Fire-and-forget: never let Redis failure propagate to the logger
        _ = IncrementInternalAsync();
    }

    private async Task IncrementInternalAsync()
    {
        try
        {
            var db = _mux.GetDatabase();
            var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _bucketWidthSeconds;
            var key = (RedisKey)$"gamekit:admin:errors:{bucket}";
            await db.StringIncrementAsync(key, 1, CommandFlags.FireAndForget)
                .ConfigureAwait(false);
            // Set TTL on first write (EXPIRE NX) — avoid a second round trip on hot path
            await db.KeyExpireAsync(key, _keyTtl, ExpireWhen.HasNoExpiry,
                CommandFlags.FireAndForget).ConfigureAwait(false);
        }
        catch { /* swallow — Redis unavailable degrades to in-memory counter only */ }
    }

    public async Task<long> RecentErrorCountAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _mux.GetDatabase();
            var nowBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _bucketWidthSeconds;
            var keys = new RedisKey[_bucketCount];
            for (var i = 0; i < _bucketCount; i++)
                keys[i] = $"gamekit:admin:errors:{nowBucket - (_bucketCount - 1 - i)}";
            var values = await db.StringGetAsync(keys).ConfigureAwait(false);
            var sum = 0L;
            foreach (var v in values)
                if (v.TryParse(out long n)) sum += n;
            return sum;
        }
        catch
        {
            return -1; // sentinel: Redis unavailable
        }
    }
}
```

**HealthProbeService change:** The constructor gains an optional `IRedisErrorRateCounter? redisCounter = null` parameter. `ProbeErrorRate()` becomes `async Task<HealthTile> ProbeErrorRateAsync()`. The count is `redisCounter != null ? await redisCounter.RecentErrorCountAsync() : _errors.RecentErrorCount()`. A negative return from Redis (unavailable) falls back to the local buffer.

**Registration change in `AddGameKitAdmin()`:** After the existing `ErrorRateRingBuffer` + `LogErrorCounter` registration:

```csharp
// ADMIN-14: opt-in Redis counter if IConnectionMultiplexer is already registered.
// Uses TryAddSingleton so a consumer who does NOT register Redis skips this path.
// The consumer registers IConnectionMultiplexer before calling AddGameKitAdmin().
builder.Services.TryAddSingleton<IRedisErrorRateCounter>(sp =>
{
    var mux = sp.GetService<IConnectionMultiplexer>();
    return mux is not null
        ? new RedisErrorRateCounter(mux, sp.GetRequiredService<GameKitAdminOptions>())
        : null!;  // returns null — HealthProbeService falls back to ErrorRateRingBuffer
});
```

**LogErrorCounter change:** Inject optional `IRedisErrorRateCounter?`. In `CountingLogger.Log`, after `_buf.IncrementError()`, also call `_redisCounter?.IncrementError()` (never throws per interface contract).

**SC#1 test:** Two `AdminTestHost` instances sharing the same `RedisFixture`. Resolve `IRedisErrorRateCounter` from host A, call `IncrementError()` 15 times. Resolve `IHealthProbeService` from host B, call `ProbeAsync()`. Assert `report.ErrorRate.Status == "Degraded"` (15 errors >= 10-99 threshold). [VERIFIED: codebase — thresholds are 0-9 OK, 10-99 Degraded, 100+ Down in `HealthProbeService.ProbeErrorRate()`]

### SC#2: AdminEventHub + AdminLiveBroadcastService

**Key architectural fact confirmed from source:** `AddRazorComponents().AddInteractiveServerComponents()` (registered in `AdminBuilderExtensions` line 183) internally calls `AddSignalR()` for the Blazor Server circuit. ASP.NET Core's `AddSignalR()` is idempotent — calling it again from `AddGameKitAdmin()` to add the backplane is safe. The `AddStackExchangeRedis()` backplane extension, however, must only be registered ONCE — use `TryAddEnumerable` or check-before-register pattern. [ASSUMED — idempotency claim confirmed by Microsoft docs for `AddSignalR`, but the "only once" backplane warning needs care]

**Correct backplane registration approach:** Use the same `IPostConfigureOptions<RedisOptions>` pattern proven in Phase 11 (`LobbyRedisBackplanePostConfigure`). Name it `AdminBackplanePostConfigure`. Since `AddStackExchangeRedis` in Lobby already registered one `IPostConfigureOptions<RedisOptions>`, the Admin one stacks on top — they both run and both target the same `RedisOptions`. This is correct behavior — the options chain is additive.

**Hub signature:**

```csharp
// Source: mirrors LobbyHub pattern (Phase 11 research, codebase verified)
// CRITICAL DIFFERENCE from LobbyHub: auth scheme is GameKitAdmin COOKIE, not JWT Bearer

/// <summary>
/// Admin live-event SignalR hub (ADMIN-13 / ADMIN-15).
/// Gated by the <c>GameKitAdmin</c> cookie authentication scheme via
/// <see cref="AdminPolicies.Admin"/> (NOT the player Bearer scheme).
/// </summary>
[Authorize(Policy = AdminPolicies.Admin)]
public sealed class AdminEventHub : Hub
{
    // No IHubContext needed — hub is receive-only for clients.
    // AdminLiveBroadcastService injects IHubContext<AdminEventHub> to broadcast.
}
```

**Why `[Authorize(Policy = AdminPolicies.Admin)]` works:** `AdminPolicies.Admin` is registered in `AddGameKitAdmin()` with `.AddAuthenticationSchemes(AdminAuthenticationSchemeConstants.Scheme)` (line 141-145 of `AdminBuilderExtensions`), where `AdminAuthenticationSchemeConstants.Scheme = "GameKitAdmin"`. The policy explicitly pins the authentication scheme to the admin cookie. A player JWT Bearer token cannot satisfy this policy — the auth pipeline tries the GameKitAdmin cookie scheme and returns 401 if the cookie is absent.

**Hub mapping position:** Under `MountPath`, at `/admin/hubs/events`. Added to `MapGameKitAdmin()` in `AdminApplicationBuilderExtensions`:

```csharp
routes.MapHub<AdminEventHub>($"{mount}/hubs/events");
```

**AdminLiveBroadcastService:**

```csharp
// Source: [ASSUMED] — BackgroundService + Redis Subscribe pattern, standard ASP.NET Core

internal sealed class AdminLiveBroadcastService : BackgroundService
{
    private const string Channel = "gamekit:admin:events";
    private readonly IConnectionMultiplexer _mux;
    private readonly IHubContext<AdminEventHub> _hub;

    public AdminLiveBroadcastService(IConnectionMultiplexer mux, IHubContext<AdminEventHub> hub)
    {
        _mux = mux;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _mux.GetSubscriber();
        var queue = await sub.SubscribeAsync(RedisChannel.Literal(Channel))
            .ConfigureAwait(false);
        stoppingToken.Register(() => queue.Unsubscribe());

        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _hub.Clients.All.SendAsync(
                    "ReceiveAdminEvent", message.Message.ToString(), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { /* swallow — individual relay failure must not kill the service */ }
        }
    }
}
```

**Registration in `AddGameKitAdmin()`:**

```csharp
// SC#2: AdminEventHub + backplane (only if IConnectionMultiplexer is registered)
builder.Services.AddSignalR()
    .AddStackExchangeRedis(options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
    });
builder.Services.AddSingleton<IPostConfigureOptions<RedisOptions>, AdminBackplanePostConfigure>();
builder.Services.AddHostedService<AdminLiveBroadcastService>();
```

`AdminBackplanePostConfigure` mirrors `LobbyRedisBackplanePostConfigure` exactly:

```csharp
// Source: mirrors LobbyRedisBackplanePostConfigure (Phase 11, codebase verified pattern)
internal sealed class AdminBackplanePostConfigure : IPostConfigureOptions<RedisOptions>
{
    private readonly IServiceProvider _sp;
    public AdminBackplanePostConfigure(IServiceProvider sp) => _sp = sp;

    public void PostConfigure(string? name, RedisOptions options)
    {
        var mux = _sp.GetRequiredService<IConnectionMultiplexer>();
        options.ConnectionFactory = _ => Task.FromResult(mux);
    }
}
```

**SC#2 test:** Two `AdminTestHost` instances sharing the same `RedisFixture`. Connect a `HubConnection` (from `Microsoft.AspNetCore.SignalR.Client`) to host B's `/admin/hubs/events` with a valid admin cookie (use `AdminTestHost.LoginAsAdminAsync`). Publish to `"gamekit:admin:events"` via host A's `IConnectionMultiplexer`. Assert the hub connection on host B receives `"ReceiveAdminEvent"` within a `TaskCompletionSource` timeout.

**Blazor Server multi-replica circuit concern (ADMIN-13):** Blazor Server uses SignalR for the interactive circuit. When running multi-replica WITHOUT sticky sessions, a browser's SignalR reconnect may land on a different replica — losing the circuit state. This is NOT a new problem introduced by Phase 12; it exists today for any multi-replica Blazor Server. The fix is either:
1. Sticky sessions at the load balancer (the standard recommendation, required for Blazor Server).
2. Redis backplane for the Blazor circuit (which the `AddStackExchangeRedis` backplane already provides).

Option 2 is what Phase 12 provides at the code level via `AddStackExchangeRedis`. Option 1 is what the ops doc must document. Both should be documented in `docs/ops/multi-replica.md`.

**Data Protection key-sharing:** Blazor Server anti-forgery tokens and cookies use ASP.NET Core Data Protection. In a multi-replica deployment, all replicas MUST share the same Data Protection keyring. This is an operator responsibility — the operator calls `AddDataProtection().PersistKeysToDbContext<GameKitDbContext>()` or `.PersistKeysToFileSystem(...)` or `.PersistKeysToStackExchangeRedis(...)` in their own host. GameKit does NOT force a Data Protection provider — but the ops doc must explain the requirement. This is a DOCUMENTATION deliverable, not a code feature (confirmed by CONTEXT.md).

### SC#3: RankAdjust.razor — Replacing the Dead Stub

**Confirmed from source:** `RankAdjust.razor` (line 1-42) is a stub page that shows `MissingPackageAlert` when Rankings is not installed, or a dead alert "Rank adjust flow will render when GameKit.Rankings ships." The page uses `Type.GetType("GameKit.Rankings.IRankingAlgorithm, GameKit.Rankings", throwOnError: false)` reflection to check for Rankings installation.

**Confirmed from source:** `RankAdjustDialog.razor` already exists and is FULLY IMPLEMENTED (242 lines). It:
- Injects `IRankAdjustService` directly via DI (line 25)
- Injects `IValidator<RankAdjustRequest>` (line 26)
- Loads active ladders from `GameKitDbContext.Set<Ladder>()` (line 149)
- Calls `IRankAdjustService.AdjustAsync(PlayerId, ladderId, newRating, reason, actorId, ct)` (line 199)
- Gets actor ID from `AuthenticationStateProvider.GetAuthenticationStateAsync()` (line 225)
- Returns `DialogResult.Ok(result)` on success

**What SC#3 actually needs:** The `RankAdjust.razor` page needs a player search input + "Adjust" button that opens `RankAdjustDialog`. The page already has:
- `@page "/admin/rankings/adjust"` [VERIFIED: codebase]
- `@attribute [Authorize(Policy = AdminPolicies.Superadmin)]` [VERIFIED: codebase]
- `@implements IDisposable` [VERIFIED: codebase]
- `@inject IServiceProvider Sp` [VERIFIED: codebase]

The page needs to be extended with:
1. `@inject IPlayerSearchService SearchSvc` (reuses existing service)
2. `@inject IDialogService DialogService` (MudBlazor dialog service, already in the DI container via `AddMudServices()`)
3. A MudTextField for player lookup (player ID or display name)
4. A MudDataGrid or simple list of matching players
5. An "Adjust" button per player row that calls `DialogService.ShowAsync<RankAdjustDialog>(...)`

**Pattern model:** `PlayerDetailPane.razor` + `BanPlayerDialog.razor` — the detail pane has a ban button that opens the dialog. The rank-adjust page follows the same "search → result → open dialog" pattern.

**Note:** `IRankAdjustService` is registered in `RankingsBuilderExtensions.Export.cs` (line 34: `services.AddScoped<IRankAdjustService, RankAdjustService>()`). It is ONLY available when `AddRankings()` is called. The `RankAdjust.razor` page must retain the `_rankingsInstalled` guard using a direct DI check (`Sp.GetService<IRankAdjustService>() is not null`) rather than the fragile `Type.GetType` reflection.

**RankAdjustRequest record** (confirmed from codebase xml docs):
```csharp
// Source: GameKit.Rankings.Http.Contracts.RankAdjustRequest
public sealed record RankAdjustRequest(Guid LadderId, double NewRating, string Reason);
```

**SC#3 test options:**
- **Integration test** (preferred): `AdminTestHost.StartAsync` + Rankings registration → call `IRankAdjustService.AdjustAsync(...)` → assert `admin_audit_log` row exists with `action = "admin.player.rank_adjust"`. This does NOT require bUnit (tests the service, not the Razor component). Pattern: extend `GameKit.Admin.Integration.Tests`.
- **bUnit test** (for the page itself): Add to `GameKit.Admin.Tests/Components/` — mock `IRankAdjustService` via Moq, render `RankAdjust.razor`, assert dialog opens when button clicked. Note: `GameKit.Admin.Tests` already has `bunit` dependency [VERIFIED: codebase csproj].

**For the plan:** the integration test (service end-to-end with audit row) is the SC#3 acceptance criterion. The bUnit test is advisory. Both can be written.

**AdminTestHost needs Rankings migration:** The `AdminTestHost.MigrateAsync` currently applies Core + Auth + Admin migrations. For SC#3 integration tests, a fourth Rankings migration pass is needed — OR a simpler approach: create a dedicated `RankAdjustServiceTests.cs` in `GameKit.Admin.Integration.Tests` that bootstraps its own minimal service provider with Core + Auth + Admin + Rankings. This follows the `TestHelpers.ApplyMigrations` pattern from prior integration test projects.

### SC#4: Version Train Audit

**`GameKitVersionAssertionHostedService` mechanism (read from source):**
- Calls `Assembly.GetEntryAssembly().GetReferencedAssemblies()` to eager-load all `GameKit.*` assemblies (line 103-120)
- Iterates `AppDomain.CurrentDomain.GetAssemblies()` for names starting with `"GameKit."` (excluding `"GameKit.Build"`) (line 127-135)
- For each, reflects on `{AssemblyName}.Internal.GameKitMarker` type and reads `GameKitVersion` field (line 143-148)
- Throws `GameKitVersionMismatchException` if distinct versions > 1

**No code change needed in the service.** The service already works by reflection — any new `GameKit.*` assembly loaded with a `GameKitMarker` constant is automatically covered.

**What each package needs to be "on the train":**
1. `PackageId` set (all five have it — confirmed from csprojfiles)
2. `AssemblyName` set (all five have it — confirmed)
3. `GameKit.Build` analyzer reference present (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) — all five have it confirmed from csproj files
4. `GameKit.targets` auto-applies the `[X.Y.Z]` exact-pin to sibling `ProjectReference`s at Pack time — this is repo-wide, no per-csproj action needed

**Train readiness audit (confirmed from csproj files):**

| Package | PackageId | AssemblyName | GameKit.Build Analyzer | Status |
|---------|-----------|--------------|----------------------|--------|
| `GameKit.Auth.Argon2` | ✓ GameKit.Auth.Argon2 | ✓ GameKit.Auth.Argon2 | ✓ | Train-ready |
| `GameKit.Auth.Google` | ✓ GameKit.Auth.Google | ✓ GameKit.Auth.Google | ✓ | Train-ready |
| `GameKit.Auth.Apple` | ✓ GameKit.Auth.Apple | ✓ GameKit.Auth.Apple | ✓ | Train-ready |
| `GameKit.Auth.Epic` | ✓ GameKit.Auth.Epic | ✓ GameKit.Auth.Epic | ✓ | Train-ready |
| `GameKit.Lobby` | ✓ GameKit.Lobby | ✓ GameKit.Lobby | ✓ | Train-ready |

[VERIFIED: codebase — all five csproj files read]

**SC#4 test:** A version coherence test in `tests/GameKit.Core.Tests/` (or a new thin test project) that:
1. Loads all five new assemblies explicitly
2. Asserts `GameKitVersionAssertionHostedService` does NOT throw when started with a DI container that includes all packages
3. Asserts that `GameKit.Auth.Argon2.Internal.GameKitMarker.GameKitVersion` is non-null and non-"0.0.0" (proves the source generator ran correctly)

The simplest approach: add 5 assertions to the existing `VersionAssertionTests` (if it exists) or extend the Phase 6 OPS-04/OPS-05 integration test that already covers the train.

**Regarding exact-pinned sibling refs at runtime:** The `[X.Y.Z]` exact-pin is a NuGet PACK-time concern enforced by `GameKit.targets`. At integration test time (using `ProjectReference`s, not packed NuGet packages), the version coherence is enforced by the source-generator constants — which is what the assertion service tests. No additional wiring is needed.

### Recommended Project Structure (changes only)

```
src/GameKit.Admin.UI/
├── Hubs/
│   └── AdminEventHub.cs            # NEW: [Authorize(Policy=AdminPolicies.Admin)] Hub
├── Services/
│   ├── IRedisErrorRateCounter.cs   # NEW: interface for cross-replica error counter
│   ├── RedisErrorRateCounter.cs    # NEW: Redis INCRBY implementation
│   ├── AdminLiveBroadcastService.cs # NEW: BackgroundService subscriber → IHubContext relay
│   ├── ErrorRateRingBuffer.cs      # UNCHANGED
│   ├── LogErrorCounter.cs          # CHANGED: add optional IRedisErrorRateCounter dual-write
│   └── HealthProbeService.cs       # CHANGED: ProbeErrorRate becomes async, prefers Redis counter
├── Builder/
│   ├── AdminBuilderExtensions.cs   # CHANGED: register hub, backplane PostConfigure, broadcast service, Redis counter
│   └── AdminApplicationBuilderExtensions.cs  # CHANGED: MapHub<AdminEventHub>
└── Components/
    └── Pages/
        └── RankAdjust.razor        # CHANGED: replace stub with player-search + dialog launch

docs/ops/
└── multi-replica.md                # NEW: sticky-session req, Data Protection key-sharing, Redis backplane

tests/GameKit.Admin.Integration.Tests/
├── RedisErrorCounterTests.cs       # NEW: SC#1 two-host cross-replica assertion
├── AdminEventHubTests.cs           # NEW: SC#2 hub auth (cookie-only) + two-host backplane relay
└── RankAdjustServiceTests.cs       # NEW: SC#3 IRankAdjustService → admin_audit_log integration
```

### Anti-Patterns to Avoid

- **Using player JWT to authenticate the AdminEventHub:** `[Authorize(Policy = AdminPolicies.Admin)]` is already pinned to `"GameKitAdmin"` cookie scheme. Never add `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` to the admin hub.
- **Registering `AddStackExchangeRedis()` twice without the PostConfigure pattern:** Two calls to `AddStackExchangeRedis()` on the same `ISignalRServerBuilder` may register duplicate backplane implementations. Use `IPostConfigureOptions<RedisOptions>` (the proven Phase 11 pattern) so only one backplane is active regardless of registration order.
- **Calling `services.BuildServiceProvider()` inside `AddGameKitAdmin()`:** `AddLobby()` Phase 11 research documents this as the wrong pattern. Use `IPostConfigureOptions<RedisOptions>` for deferred multiplexer resolution.
- **Replacing `ErrorRateRingBuffer` with `RedisErrorRateCounter` entirely:** This breaks single-instance deployments that have no Redis. The Redis counter is opt-in via the presence of `IConnectionMultiplexer` in DI.
- **Calling `ICurrentPlayer` inside the AdminEventHub:** `IHttpContextAccessor.HttpContext` is null in SignalR hub methods. The admin hub doesn't need player identity — admins are authenticated via cookie and the hub is receive-only.
- **Type.GetType reflection for Rankings detection in RankAdjust.razor:** Use `Sp.GetService<IRankAdjustService>() is not null` instead — direct DI check avoids fragile string-based assembly names.
- **Running `AdminLiveBroadcastService` without a registered `IConnectionMultiplexer`:** The service ctor-injects `IConnectionMultiplexer` (required). If no Redis is configured, the consumer calling `AddGameKitAdmin()` without Redis will get a DI resolution failure at host start. Gate the hosted service registration on multiplexer availability (same conditional pattern used for `IRedisErrorRateCounter`).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-replica SignalR message delivery | Custom Redis pub/sub relay per-hub | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` backplane | Already proven in Phase 11; handles serialization, channel naming, reconnection |
| Cross-instance Redis subscription for admin events | Two separate subscription mechanisms | Single `IConnectionMultiplexer.GetSubscriber().SubscribeAsync()` in `AdminLiveBroadcastService` + `IHubContext<AdminEventHub>` broadcast | Standard BackgroundService + IHubContext pattern; no custom relay infrastructure |
| Cookie-based WebSocket auth | Custom cookie extraction in hub | `[Authorize(Policy = AdminPolicies.Admin)]` — the path-based policy scheme in `AddGameKitAdmin()` already routes `/_blazor/*` AND `/admin/*` to the GameKitAdmin cookie scheme | Blazor's circuit auth is already solved by the path-based scheme; hub inherits it |
| Admin-level Redis INCRBY counters | Custom time-series storage | Redis `INCRBY` with `KeyExpireAsync(..., ExpireWhen.HasNoExpiry)` for TTL + `MGET` for sum | Standard Redis pattern; 2 round-trips per read is fine for a 10s health refresh interval |
| Rank-adjust form + validation | Custom form controls | Extend the existing `RankAdjustDialog.razor` (already complete) + `IPlayerSearchService` (already registered) | The dialog is already 242 lines of complete Blazor + FluentValidation + IRankAdjustService integration |

**Key insight:** Three of the four SCs are primarily WIRING work on already-implemented services (IRankAdjustService, SignalR backplane, GameKitVersionAssertionHostedService). The only genuinely new code is `RedisErrorRateCounter`, `AdminEventHub`, and `AdminLiveBroadcastService` — all under 100 lines each.

---

## Common Pitfalls

### Pitfall 1: `AddStackExchangeRedis()` registering the backplane twice
**What goes wrong:** Both `AddLobby()` (Phase 11) and `AddGameKitAdmin()` (Phase 12) call `AddSignalR().AddStackExchangeRedis(...)`. If the consumer calls both, two backplane registrations exist. In practice ASP.NET Core's DI will use the last-registered `IRedisServerMessageSerializer` / backplane services — but this is fragile.
**Why it happens:** `AddStackExchangeRedis()` is an extension on `ISignalRServerBuilder` that calls `services.AddSingleton<IMessageSerializer, ...>()` etc. — not idempotent.
**How to avoid:** In `AddGameKitAdmin()`, use `services.TryAddSingleton<IPostConfigureOptions<RedisOptions>, AdminBackplanePostConfigure>()` (note `TryAdd` not `Add`) so a second registration from Lobby doesn't conflict. The `ChannelPrefix = "GameKit"` is the same across both registrations — no conflict there. The `ConnectionFactory` PostConfigure runs for all `RedisOptions` instances and idempotently sets the same multiplexer.
**Warning signs:** SignalR backplane startup log shows two "Connected" messages; admin events not delivered across instances.

### Pitfall 2: Admin Hub WebSocket upgrade without the path-based auth scheme
**What goes wrong:** A hub at `/admin/hubs/events` with `[Authorize(Policy=AdminPolicies.Admin)]` returns 401 even for a valid admin session.
**Why it happens:** The path-based default scheme in `AddGameKitAdmin()` forwards `/admin/*` paths to the `GameKitAdmin` cookie scheme. BUT the hub negotiate endpoint (`/admin/hubs/events/negotiate`) and the WebSocket upgrade must also match this path prefix. The current scheme-selector uses `path.StartsWithSegments(opts.MountPath, ...)` which covers `/admin/hubs/events` when `MountPath="/admin"`.
**How to avoid:** Confirm the hub is mapped under `$"{mount}/hubs/events"` (not `/hubs/events`) so it falls under `MountPath` and the path-based policy scheme selects `GameKitAdmin` correctly.
**Warning signs:** Hub returns 401 with `WWW-Authenticate: Bearer` header (indicates JWT scheme was selected instead of cookie scheme).

### Pitfall 3: `KeyExpireAsync(..., ExpireWhen.HasNoExpiry)` not available in SE.Redis 2.8.x
**What goes wrong:** `ExpireWhen` enum may not exist in StackExchange.Redis 2.8.41 (it was added in a later version).
**Why it happens:** The `ExpireWhen` enum was added in SE.Redis 2.6.x; 2.8.41 should have it, but the exact API shape needs verification.
**How to avoid:** The planner should verify `ExpireWhen.HasNoExpiry` is available in SE.Redis 2.8.41 before writing the implementation. Fallback: use a separate `KeyExistsAsync` + `KeyExpireAsync` pair, or simply always set the TTL (slight overhead but correct).
**Warning signs:** CS0246 compilation error on `ExpireWhen`.

### Pitfall 4: `AdminLiveBroadcastService` registered unconditionally when no Redis is configured
**What goes wrong:** A consumer calling `AddGameKitAdmin()` without registering `IConnectionMultiplexer` gets a `DI resolution failure` at host start because `AdminLiveBroadcastService` ctor-injects `IConnectionMultiplexer` (required, not optional).
**Why it happens:** Phase 3 registered `IConnectionMultiplexer` as optional in `HealthProbeService` (`IConnectionMultiplexer? redis = null`) — but `AdminLiveBroadcastService` needs it to subscribe.
**How to avoid:** Either (a) make `AdminLiveBroadcastService` register only when `IConnectionMultiplexer` is detected in the service collection (check `builder.Services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))` at `AddGameKitAdmin()` time), or (b) inject `IConnectionMultiplexer?` as nullable and short-circuit `ExecuteAsync` when null. Option (b) is simpler and consistent with `HealthProbeService`'s pattern.
**Warning signs:** `InvalidOperationException: Unable to resolve service for type 'StackExchange.Redis.IConnectionMultiplexer'` at host start.

### Pitfall 5: `RankAdjust.razor` page requires Rankings + Admin + Core migrations
**What goes wrong:** The SC#3 integration test calls `IRankAdjustService.AdjustAsync` but `Rankings` tables (`player_ranks`, `ladders`) don't exist in the Testcontainers database.
**Why it happens:** `AdminTestHost.MigrateAsync` only applies Core + Auth + Admin migrations.
**How to avoid:** For SC#3 tests, extend the migration sequence to include Rankings. Create a helper `RankAdjustTestHost` or extend `AdminTestHost.MigrateAsync` with an optional `includeRankings: bool` parameter. The Rankings migration context follows the same pattern as Admin.
**Warning signs:** `42P01 (relation "gamekit.player_ranks" does not exist)` in test output.

### Pitfall 6: `ProbeErrorRate()` is synchronous but `IRedisErrorRateCounter.RecentErrorCountAsync()` is async
**What goes wrong:** `HealthProbeService.ProbeAsync()` currently calls synchronous `ProbeErrorRate()`. Converting to async requires changing the method signature and the callers.
**Why it happens:** `ErrorRateRingBuffer.RecentErrorCount()` is synchronous (in-memory, O(n)). Adding Redis requires a `Task`-returning method.
**How to avoid:** Change `ProbeErrorRate()` to `ProbeErrorRateAsync()` returning `Task<HealthTile>`. Update `ProbeAsync()` accordingly (it already `await`s the other probes). This is a non-breaking change to the public `HealthProbeService` API (the interface `IHealthProbeService` exposes `Task<HealthReport> ProbeAsync(CancellationToken)` — no signature change needed on the public API).
**Warning signs:** Deadlock in sync-over-async pattern; `.Result` on an async task in an ASP.NET Core synchronization context.

---

## Code Examples

### Dual-Write in LogErrorCounter

```csharp
// Source: extension of codebase-verified LogErrorCounter.cs pattern
// CHANGE: inject optional IRedisErrorRateCounter

public sealed class LogErrorCounter : ILoggerProvider
{
    private readonly ErrorRateRingBuffer _buf;
    private readonly IRedisErrorRateCounter? _redis;

    public LogErrorCounter(ErrorRateRingBuffer buf, IRedisErrorRateCounter? redis = null)
    {
        _buf = buf;
        _redis = redis;
    }

    public ILogger CreateLogger(string categoryName) =>
        new CountingLogger(_buf, _redis);

    public void Dispose() { }

    private sealed class CountingLogger : ILogger
    {
        private readonly ErrorRateRingBuffer _buf;
        private readonly IRedisErrorRateCounter? _redis;

        public CountingLogger(ErrorRateRingBuffer buf, IRedisErrorRateCounter? redis)
        {
            _buf = buf;
            _redis = redis;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;

        public void Log<TState>(LogLevel level, EventId id, TState state,
            Exception? ex, Func<TState, Exception?, string> fmt)
        {
            if (level < LogLevel.Error) return;
            _buf.IncrementError();
            _redis?.IncrementError(); // fire-and-forget per IRedisErrorRateCounter contract
        }
    }
}
```

### HealthProbeService Error-Rate Probe (updated)

```csharp
// Source: extension of codebase-verified HealthProbeService.cs

// Constructor:
public HealthProbeService(
    GameKitOptions gameKitOpts,
    ErrorRateRingBuffer errors,
    IClock clock,
    IConnectionMultiplexer? redis = null,
    IRedisErrorRateCounter? redisErrors = null) { ... }

// Updated probe (async):
private async Task<HealthTile> ProbeErrorRateAsync(CancellationToken ct)
{
    long count;
    if (_redisErrors is not null)
    {
        count = await _redisErrors.RecentErrorCountAsync(ct).ConfigureAwait(false);
        if (count < 0) // Redis unavailable — fall back
            count = _errors.RecentErrorCount();
    }
    else
    {
        count = _errors.RecentErrorCount();
    }

    var status = count switch
    {
        < 10 => "OK",
        < 100 => "Degraded",
        _ => "Down",
    };
    return new HealthTile(status, $"{count} errors in window", null);
}
```

### RankAdjust.razor — Replacement Body (skeleton)

```razor
@* Source: mirrors BanPlayerDialog + PlayerDetailPane launch pattern (codebase verified) *@
@page "/admin/rankings/adjust"
@attribute [Authorize(Policy = AdminPolicies.Superadmin)]
@implements IDisposable
@inject IServiceProvider Sp
@inject IDialogService DialogService
@inject IPlayerSearchService SearchSvc

<div class="page-head">
    <h1>Rank adjust</h1>
</div>

@if (!_rankingsInstalled)
{
    <MissingPackageAlert PackageName="Rankings" Feature="manual rank adjustments" />
}
else
{
    <MudTextField T="string"
                  @bind-Value="_query"
                  @bind-Value:after="OnQueryChanged"
                  Placeholder="Search player by ID or name…"
                  Adornment="Adornment.Start"
                  AdornmentIcon="@Icons.Material.Filled.Search"
                  Immediate="true"
                  DebounceInterval="250"
                  Clearable="true"
                  Variant="Variant.Outlined"
                  FullWidth="true" />

    @foreach (var row in _rows)
    {
        <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                   OnClick="@(() => OpenRankAdjustAsync(row.Id, row.DisplayName))">
            Adjust @row.DisplayName
        </MudButton>
    }
}

@code {
    private bool _rankingsInstalled;
    private string _query = string.Empty;
    private List<PlayerSearchRow> _rows = new();
    private readonly CancellationTokenSource _cts = new();

    protected override void OnInitialized()
    {
        // Direct DI check: avoids fragile Type.GetType string-based reflection
        _rankingsInstalled = Sp.GetService(
            typeof(GameKit.Rankings.Services.IRankAdjustService)) is not null;
    }

    private async Task OnQueryChanged() { /* search via SearchSvc */ }

    private async Task OpenRankAdjustAsync(Guid playerId, string displayName)
    {
        var parameters = new DialogParameters<RankAdjustDialog>
        {
            { x => x.PlayerId, playerId },
            { x => x.DisplayName, displayName },
        };
        await DialogService.ShowAsync<RankAdjustDialog>("Adjust Rating", parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
    }

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
```

### Ops Doc: multi-replica.md (outline)

```markdown
# Multi-Replica Deployment

## Requirements
1. Sticky sessions at the load balancer (required for Blazor Server circuit continuity)
2. Redis backplane for SignalR (provided automatically by AddGameKitAdmin + AddLobby)
3. Shared Data Protection keyring (operator responsibility)

## Data Protection Key Sharing (CRITICAL)

Blazor Server uses ASP.NET Core Data Protection for anti-forgery tokens and session cookies.
In a multi-replica deployment, all replicas MUST share the same Data Protection key ring.

Without this, admin cookies issued by replica A will be rejected by replica B.

Add to your host Program.cs:
    builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(connectionMultiplexer, "gamekit:dp:keys")
        // OR:
        .PersistKeysToDbContext<YourDbContext>()  // requires Microsoft.AspNetCore.DataProtection.EntityFrameworkCore
        // OR (file share for on-premise):
        .PersistKeysToFileSystem(new DirectoryInfo("/shared/dp-keys"))

## SignalR Backplane
GameKit Admin UI automatically configures the Redis backplane for both the Blazor Server
interactive circuit (/_blazor) and the AdminEventHub (/admin/hubs/events).
ChannelPrefix = "GameKit" isolates GameKit channels from any consumer-level SignalR.

## Sticky Sessions
Even with the Redis backplane, sticky sessions (IP hash or session-cookie affinity) are
STRONGLY RECOMMENDED for Blazor Server. The backplane handles cross-instance message
delivery but reconnection to a different instance requires a full circuit re-initialization,
which resets all component state.
```

---

## Runtime State Inventory

This is not a rename/refactor phase. Omitted per instructions.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | Testcontainers (Postgres + Redis) | ✓ | 29.5.3 (from Phase 11) | — |
| .NET SDK | All compilation | ✓ | 10.0.108 | — |
| `Microsoft.AspNetCore.SignalR.Client` | Hub tests (two-TestServer backplane) | ✓ (pinned 10.0.8 in CPM, added Phase 11) | 10.0.8 | — |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | AdminEventHub backplane | ✓ (pinned 10.0.8 in CPM, added Phase 11) | 10.0.8 | — |

**Missing dependencies with no fallback:** None.

**Missing dependencies with fallback:** None.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Testcontainers 4.11.0 + bunit (for Razor component tests) |
| Config file | `tests/GameKit.Admin.Integration.Tests/` (existing) + `tests/GameKit.Admin.Tests/` (existing) |
| Quick run command | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter "Category!=LoadTest" -x` |
| Full suite command | `dotnet test tests/GameKit.Admin.Integration.Tests/ tests/GameKit.Admin.Tests/ -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| ADMIN-14 / SC#1 | Write `RedisErrorRateCounter` on host A; read aggregate from host B; assert `Degraded` status | Integration (two-TestServer, shared Redis) | `dotnet test ... --filter "FullyQualifiedName~RedisErrorCounterTests.SC1"` | ❌ Wave 1 |
| ADMIN-14 | Single-instance fallback: no Redis configured → `ErrorRateRingBuffer` still works | Integration (existing `HealthProbeTests` extension) | `dotnet test ... --filter "FullyQualifiedName~HealthProbeTests"` | ✅ (extend) |
| ADMIN-13 / SC#2 | Unauthenticated WebSocket upgrade to `/admin/hubs/events` → 401 | Integration | `dotnet test ... --filter "FullyQualifiedName~AdminEventHubTests.Unauthenticated_Returns_401"` | ❌ Wave 2 |
| ADMIN-13 / SC#2 | Player JWT cannot authenticate admin hub (`[Authorize(Policy=Admin)]` cookie-only) | Integration | `dotnet test ... --filter "FullyQualifiedName~AdminEventHubTests.PlayerJwt_Cannot_Access_AdminHub"` | ❌ Wave 2 |
| ADMIN-13 / SC#2 | Redis Pub/Sub message on host A → admin session on host B receives `ReceiveAdminEvent` | Integration (two-TestServer, shared Redis) | `dotnet test ... --filter "FullyQualifiedName~AdminEventHubTests.SC2"` | ❌ Wave 2 |
| ADMIN-15 / SC#3 | `IRankAdjustService.AdjustAsync` produces `admin_audit_log` row with `admin.player.rank_adjust` action | Integration | `dotnet test ... --filter "FullyQualifiedName~RankAdjustServiceTests.SC3"` | ❌ Wave 3 |
| ADMIN-15 | `RankAdjust.razor` renders `MissingPackageAlert` when Rankings not installed | bUnit | `dotnet test tests/GameKit.Admin.Tests/ --filter "FullyQualifiedName~RankAdjustPageTests"` | ❌ Wave 3 |
| DIST-07 / SC#4 | All 5 new packages have non-"0.0.0" `GameKitMarker.GameKitVersion` constants | Unit | `dotnet test tests/GameKit.Core.Tests/ --filter "FullyQualifiedName~VersionTrainTests"` | ❌ Wave 0 |
| DIST-07 | `GameKitVersionAssertionHostedService` does not throw when all 5 packages loaded | Integration | `dotnet test ... --filter "FullyQualifiedName~VersionTrainTests.AssertionService_Does_Not_Throw"` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter "Category!=LoadTest" -x`
- **Per wave merge:** `dotnet test tests/GameKit.Admin.Integration.Tests/ tests/GameKit.Admin.Tests/ -x`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/GameKit.Core.Tests/VersionTrainTests.cs` — SC#4 version coherence test asserting all 5 packages have non-"0.0.0" GameKitVersion constant and the assertion service doesn't throw

*(All other test files are Wave 1–3 additions to existing test projects, not new projects.)*

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | Yes — AdminEventHub must refuse non-admin connections | `[Authorize(Policy = AdminPolicies.Admin)]` with `GameKitAdmin` cookie scheme |
| V3 Session Management | Yes — multi-replica Blazor requires shared Data Protection keys | Documented in ops guide; operator configures `PersistKeysTo*` |
| V4 Access Control | Yes — rank-adjust is superadmin-only; hub is admin-only | `[Authorize(Policy = AdminPolicies.Superadmin)]` on RankAdjust.razor; `Admin` policy on hub |
| V5 Input Validation | Yes — rank-adjust bounds + reason length | `RankAdjustRequestValidator` (already registered by `AddRankings()`) + server-side bounds check in `RankAdjustService.AdjustAsync` |
| V6 Cryptography | No — no new cryptographic operations | Data Protection crypto owned by ASP.NET Core; no hand-rolled crypto |

### Known Threat Patterns for this Phase

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Player JWT authenticating AdminEventHub | Spoofing | `AdminPolicies.Admin` explicitly pins `GameKitAdmin` cookie scheme — JWT Bearer cannot satisfy this policy (SC#2 cross-scheme test from Phase 3 already proves this boundary) |
| Admin event flooding via Redis Pub/Sub injection | DoS | `AdminLiveBroadcastService` swallows per-message errors; no rate-limit on the internal Redis channel (admin-only publish path; not consumer-accessible). Low risk. |
| Rank-adjust by non-superadmin | Elevation of Privilege | `@attribute [Authorize(Policy = AdminPolicies.Superadmin)]` on `RankAdjust.razor` + service-level audit trail; `IRankAdjustService` writes `admin_audit_log` row with `actorId` |
| Redis error counter integer overflow | Tampering | `INCRBY` on int64; at 1 error/ns continuously for ~292 years — not a realistic risk for a per-second bucket. Bucket TTL = window width + 1 bucket ensures automatic cleanup. |
| PII leak via admin live events | Information Disclosure | `AdminLiveBroadcastService` relays raw Redis message bytes — the publisher (future feature) controls payload. For v2, no PII is published to `"gamekit:admin:events"` (the channel has no producers in Phase 12 — SC#2 test is the only publisher). |
| Stale admin session on replica after logout | Spoofing | Cookie revocation on logout invalidates the session server-side; the path-based scheme applies consistently across replicas since it reads the `gk_admin_session` cookie which is issued by the shared Data Protection keyring |

---

## Project Constraints (from CLAUDE.md)

1. **GPL license:** SPDX header on every new `.cs` file.
2. **net10.0 TFM:** No TFM changes — all work is in existing `GameKit.Admin.UI` package.
3. **XML doc on every public API:** `IRedisErrorRateCounter`, `RedisErrorRateCounter`, `AdminEventHub`, `AdminLiveBroadcastService` must all have `<summary>` on every public member. `CS1591` is error.
4. **Zero cloud deps:** Redis backplane only (not Azure SignalR Service).
5. **SignalR backplane:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 already pinned.
6. **MinVer release train:** No version changes needed — all 5 packages confirmed train-ready from csproj inspection.
7. **Migration boundaries:** Phase 12 adds no migrations — it is code/config/docs only.
8. **Admin cookie scheme:** `AdminAuthenticationSchemeConstants.Scheme = "GameKitAdmin"` — all admin hub authorization must use this scheme, never `JwtBearerDefaults.AuthenticationScheme`.
9. **IConnectionMultiplexer optional:** `AddGameKitAdmin()` already tolerates no Redis (health probe). `AdminLiveBroadcastService` and `RedisErrorRateCounter` must conditionally register (only when IConnectionMultiplexer is available).
10. **`AddGameKit.targets` exact-pin:** Automatic — no per-csproj action needed for SC#4.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `services.AddSignalR().AddStackExchangeRedis(...)` called twice (once in AddLobby, once in AddGameKitAdmin) does not duplicate or conflict the backplane registration when using the `IPostConfigureOptions<RedisOptions>` pattern | §SC#2 Pitfall 1 | If it duplicates registrations, the backplane may deliver messages twice or fail silently; mitigate by using `TryAddEnumerable` for the PostConfigure |
| A2 | `ExpireWhen.HasNoExpiry` enum value exists in StackExchange.Redis 2.8.41 | §SC#1 Redis counter | If absent, use `KeyExistsAsync` + conditional `KeyExpireAsync` instead; no functional impact |
| A3 | `bunit` in `GameKit.Admin.Tests.csproj` supports Razor component testing without a real Blazor Server runtime (test-renderer mode) | §SC#3 bUnit test | If bUnit can't render `RankAdjust.razor` without Rankings DI, mock `IServiceProvider.GetService<IRankAdjustService>()` to return non-null |
| A4 | `ChannelPrefix = RedisChannel.Literal("GameKit")` used in both Lobby (Phase 11) and Admin (Phase 12) does not cause Lobby messages to be delivered to the Admin hub or vice versa | §SC#2 backplane | SignalR backplane namespaces by hub type within the prefix — different hub types use different internal channels. The prefix isolates GameKit from consumer, not hub-from-hub. This is expected behavior per Microsoft docs. |

**If this table is empty of blocking assumptions:** A1 is the only one that could affect the execution plan — the planner should note it as a test-verify item in Wave 2.

---

## Open Questions (RESOLVED)

> RESOLVED in planning: Q1 (SE.Redis ExpireWhen.HasNoExpiry) -> always-set-TTL fallback in 12-03. Q2 (conditional AdminLiveBroadcastService) -> nullable IConnectionMultiplexer short-circuit in 12-04.

1. **`ExpireWhen.HasNoExpiry` API availability in SE.Redis 2.8.41**
   - What we know: `ExpireWhen` was added in SE.Redis 2.6.0. 2.8.41 should have it.
   - What's unclear: the exact overload shape (`KeyExpireAsync(key, expiry, when, flags)`) vs. just `KeyExpireAsync(key, expiry, flags)`.
   - Recommendation: Planner reads `StackExchange.Redis` 2.8.41 API (via `dotnet package inspect` or IDE) before writing `RedisErrorRateCounter`. Fallback is always-set TTL.

2. **Should `AdminEventHub` be conditional on IConnectionMultiplexer being registered?**
   - What we know: Without Redis, the backplane doesn't work and `AdminLiveBroadcastService` needs `IConnectionMultiplexer`.
   - What's unclear: Is a single-instance deployment with `AddGameKitAdmin()` but no Redis a supported configuration?
   - Recommendation: Yes — single-instance with no Redis IS a valid configuration (the existing health probe supports it). Gate `AdminLiveBroadcastService` registration on Redis availability. The hub itself can still be mapped (it will just never receive events). Document in XML docs.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| In-memory `ErrorRateRingBuffer` (per-process) | Redis `INCRBY` bucketed counters (cross-replica) | Phase 12 (opt-in, single-instance default unchanged) | Health panel shows correct aggregate across replicas |
| Dead `RankAdjust.razor` stub (alert placeholder) | Full player-search + `RankAdjustDialog` flow | Phase 12 | SC#3 requirement satisfied; `IRankAdjustService` + audit trail active |
| Azure SignalR Service (forbidden) | Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) | GameKit v1 requirement from day one | Zero cloud dependency; self-hostable |

**Deprecated/outdated:**
- The `Type.GetType("GameKit.Rankings.IRankingAlgorithm, GameKit.Rankings", ...)` reflection guard in `RankAdjust.razor` — replace with `Sp.GetService<IRankAdjustService>() is not null` (direct DI check, no string-based assembly name).

---

## Sources

### Primary (HIGH confidence)
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` — exact constructor, bucket/window logic, `IncrementError()`, `RecentErrorCount()`
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` — exact `ILoggerProvider` pattern, `CountingLogger.Log` entry point
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Services/HealthProbeService.cs` — exact constructor, `ProbeErrorRate()` thresholds (0-9 OK, 10-99 Degraded, 100+ Down)
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` — exact DI registrations, path-based policy scheme selector, `AddRazorComponents().AddInteractiveServerComponents()` at line 183
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor` — confirmed stub with dead alert body
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor` — confirmed fully implemented (242 lines), exact `IRankAdjustService.AdjustAsync` call signature
- [VERIFIED: codebase] `src/GameKit.Rankings/Services/IRankAdjustService.cs` — exact interface: `AdjustAsync(Guid playerId, Guid ladderId, double newRating, string reason, Guid actorId, CancellationToken ct) → Task<RankAdjustResult>`
- [VERIFIED: codebase] `src/GameKit.Rankings/Services/RankAdjustService.cs` — confirmed writes `admin_audit_log` row with action `"admin.player.rank_adjust"` inside SERIALIZABLE tx
- [VERIFIED: codebase] `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Export.cs` — `IRankAdjustService` and `IValidator<RankAdjustRequest>` registered via `AddRankings()`
- [VERIFIED: codebase] `src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs` — reflection-based, discovers any `GameKit.*.Internal.GameKitMarker.GameKitVersion` field; no code change needed
- [VERIFIED: codebase] `src/GameKit.Build/GameKitVersionGenerator.cs` — emits per-assembly `GameKitMarker` constant; runs on `GameKit.*` assemblies only
- [VERIFIED: codebase] `GameKit.targets` — `_ApplyExactPinToSiblingGameKitReferences` target confirms exact-pin is automatic for all `ProjectReference`s with `GameKit.*` filename
- [VERIFIED: codebase] `src/GameKit.Auth.Argon2/GameKit.Auth.Argon2.csproj` + `GameKit.Auth.Google.csproj` + `GameKit.Auth.Apple.csproj` + `GameKit.Auth.Epic.csproj` + `GameKit.Lobby/GameKit.Lobby.csproj` — all five packages confirmed with `PackageId`, `AssemblyName`, and `GameKit.Build` analyzer reference
- [VERIFIED: codebase] `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` — `StartAsync` signature, existing migration pattern (Core+Auth+Admin only), `configureExtraServices` hook
- [VERIFIED: codebase] `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` — existing test structure and test collection `"Admin"`
- [VERIFIED: codebase Phase 11] `LobbyRedisBackplanePostConfigure` pattern — `IPostConfigureOptions<RedisOptions>` deferred multiplexer resolution
- [VERIFIED: codebase] `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` + `AdminAuthenticationSchemeConstants.cs` — `AdminPolicies.Admin = "gamekit.admin.admin"`, `Scheme = "GameKitAdmin"`

### Secondary (MEDIUM confidence)
- [CITED: .planning/phases/11-gamekit-lobby/11-RESEARCH.md §SignalR Wiring] — `AddStackExchangeRedis`, `ConnectionFactory`, `IPostConfigureOptions<RedisOptions>` pattern verified and documented in Phase 11
- [CITED: learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0] — Redis backplane setup for ASP.NET Core SignalR
- [CITED: learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0] — SignalR hub authentication and authorization

---

## Metadata

**Confidence breakdown:**
- SC#1 Redis error counter: HIGH — based on actual `ErrorRateRingBuffer`, `LogErrorCounter`, `HealthProbeService` source; Redis INCRBY pattern is standard
- SC#2 AdminEventHub: HIGH — based on actual `AdminBuilderExtensions`, `AdminPolicies`, Phase 11 backplane pattern; one ASSUMED item (double-registration safety)
- SC#3 RankAdjust page: HIGH — RankAdjustDialog.razor is fully implemented and read; IRankAdjustService interface and implementation verified from source
- SC#4 version train: HIGH — all five csproj files read; assertion service code read; GameKit.targets read

**Research date:** 2026-06-06
**Valid until:** 2026-07-06 (30 days; stable .NET 10 stack)

---

## RESEARCH COMPLETE

**Phase:** 12 — Admin Multi-Replica + Distribution Close-Out
**Confidence:** HIGH

### Key Findings

1. **No new NuGet packages required.** All dependencies (`Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8, `Microsoft.AspNetCore.SignalR.Client` 10.0.8) are already in `Directory.Packages.props` from Phase 11. Phase 12 is pure code work.

2. **SC#1 is additive, not a replacement.** `ErrorRateRingBuffer` and `LogErrorCounter` stay in place. `RedisErrorRateCounter` is an opt-in extension registered only when `IConnectionMultiplexer` is available — dual-write from `LogErrorCounter`, aggregate read in `HealthProbeService`. Single-instance installs continue working unchanged.

3. **SC#2 auth boundary confirmed critical.** `AdminEventHub` must be mapped under `MountPath` (`/admin/hubs/events`) so the path-based policy scheme in `AddGameKitAdmin()` routes it to the `GameKitAdmin` cookie scheme. Player JWT Bearer cannot satisfy `AdminPolicies.Admin`. The Phase 3 cross-scheme isolation test (`CrossSchemeIsolationTests`) already proves this boundary — the SC#2 test extends it to the new hub endpoint.

4. **SC#3 is mostly done — `RankAdjustDialog.razor` is already complete (242 lines).** Only `RankAdjust.razor` needs replacing: swap the dead stub body with player search + `IDialogService.ShowAsync<RankAdjustDialog>(...)`. Replace fragile `Type.GetType` reflection guard with `Sp.GetService<IRankAdjustService>() is not null`.

5. **SC#4 requires zero code changes to production code.** All five packages already have `GameKit.Build` analyzer references, `PackageId`, and `AssemblyName` set. `GameKitVersionAssertionHostedService` discovers them automatically. The only SC#4 deliverable is a new version-train coherence test and verification that no package reports "0.0.0".

### File Created

`.planning/phases/12-admin-multi-replica-distribution-close-out/12-RESEARCH.md`

### Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| SC#1 Redis error counter | HIGH | All source files read; Redis INCRBY pattern is well-established |
| SC#2 AdminEventHub auth | HIGH | AdminBuilderExtensions.cs + AdminPolicies confirmed; Phase 11 backplane pattern proven |
| SC#3 RankAdjust page | HIGH | RankAdjustDialog.razor and IRankAdjustService read from source — dialog is complete |
| SC#4 version train | HIGH | All 5 csproj files read; assertion service code read |
| Double-registration safety (A1) | MEDIUM | `IPostConfigureOptions` pattern mitigates; exact SE.Redis behavior not tested |

### Ready for Planning

Research complete. Planner can now create PLAN.md files.
