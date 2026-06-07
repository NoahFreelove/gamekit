# Phase 12: Admin Multi-Replica + Distribution Close-Out — Pattern Map

**Mapped:** 2026-06-06
**Files analyzed:** 12 new/modified files
**Analogs found:** 12 / 12

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Services/IRedisErrorRateCounter.cs` | service (interface) | event-driven | `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` | role-match |
| `src/GameKit.Admin.UI/Services/RedisErrorRateCounter.cs` | service | event-driven | `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` + `IConnectionMultiplexer` usage in `HealthProbeService` | role-match |
| `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` (MODIFIED) | middleware/provider | event-driven | `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` (lines 1-52, existing file) | exact |
| `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (MODIFIED) | service | request-response | `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (lines 1-113, existing file) | exact |
| `src/GameKit.Admin.UI/Hubs/AdminEventHub.cs` | hub | event-driven | `src/GameKit.Lobby/Hubs/LobbyHub.cs` | role-match (auth scheme differs) |
| `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs` | service (BackgroundService) | pub-sub | `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs` + `RankDecayBackgroundService` pattern | role-match |
| `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` (MODIFIED) | config/builder | request-response | `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` (lines 49-103) | role-match |
| `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` (MODIFIED) | config/builder | request-response | `src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs` (lines 38-51) | role-match |
| `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor` (MODIFIED) | component | request-response | `src/GameKit.Admin.UI/Components/Pages/Players.razor` + `Components/Shared/PlayerDetailPane.razor` | exact |
| `docs/ops/multi-replica.md` | documentation | — | `docs/ops/bare-metal.md` (structure/header pattern) | role-match |
| `tests/GameKit.Admin.Integration.Tests/RedisErrorCounterTests.cs` | test | event-driven | `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` + `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` | role-match |
| `tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs` | test | event-driven | `tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs` + `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` | exact |
| `tests/GameKit.Admin.Integration.Tests/RankAdjustServiceTests.cs` | test | CRUD | `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` (AdminTestHost pattern) | role-match |
| `tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs` (MODIFIED) | test | transform | `tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs` (lines 1-111, existing file) | exact |

---

## Pattern Assignments

### `src/GameKit.Admin.UI/Services/IRedisErrorRateCounter.cs` (service interface, event-driven)

**Analog:** `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs`

**SPDX + namespace pattern** (lines 1-7):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Admin.UI.Services;
```

**Interface shape** — mirror `ErrorRateRingBuffer` public API surface with async read side:
```csharp
/// <summary>
/// Cross-replica error-rate counter (ADMIN-14). When <see cref="RedisErrorRateCounter"/>
/// is registered, <see cref="HealthProbeService"/> reads from this interface instead of
/// <see cref="ErrorRateRingBuffer"/> so the health panel is correct across all replicas.
/// Implementations MUST NOT throw — fire-and-forget contract on writes.
/// </summary>
public interface IRedisErrorRateCounter
{
    /// <summary>Increments the current time bucket. Fire-and-forget — must not throw.</summary>
    void IncrementError();

    /// <summary>
    /// Returns the aggregate error count across all replicas for the current window.
    /// Returns <c>-1</c> when Redis is unavailable (caller falls back to in-memory buffer).
    /// </summary>
    Task<long> RecentErrorCountAsync(CancellationToken ct = default);
}
```

---

### `src/GameKit.Admin.UI/Services/RedisErrorRateCounter.cs` (service, event-driven)

**Analog:** `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` (lines 1-87) for constructor shape; `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (lines 22-47) for `IConnectionMultiplexer?` injection pattern.

**Imports pattern** — combine ErrorRateRingBuffer + HealthProbeService imports:
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace GameKit.Admin.UI.Services;
```

**Constructor pattern** — mirrors `ErrorRateRingBuffer` ctor signature (lines 28-44):
```csharp
internal sealed class RedisErrorRateCounter : IRedisErrorRateCounter
{
    private readonly IConnectionMultiplexer _mux;
    private readonly long _bucketWidthSeconds;
    private readonly int _bucketCount;
    private readonly TimeSpan _keyTtl;

    public RedisErrorRateCounter(IConnectionMultiplexer mux, GameKitAdminOptions opts)
    {
        ArgumentNullException.ThrowIfNull(mux);
        ArgumentNullException.ThrowIfNull(opts);
        // Derive bucket/window from the same AdminPanelOptions that ErrorRateRingBuffer uses
        _bucketWidthSeconds = (long)Math.Max(1, opts.Panel.HealthErrorRateBucketSize.TotalSeconds);
        _bucketCount = (int)Math.Ceiling(
            opts.Panel.HealthErrorRateWindow.TotalSeconds / _bucketWidthSeconds);
        _keyTtl = opts.Panel.HealthErrorRateWindow + opts.Panel.HealthErrorRateBucketSize;
        _mux = mux;
    }
```

**Fire-and-forget pattern** — per `IRedisErrorRateCounter` contract, never throws:
```csharp
    public void IncrementError()
    {
        _ = IncrementInternalAsync();  // discard Task — fire-and-forget
    }

    private async Task IncrementInternalAsync()
    {
        try
        {
            var db = _mux.GetDatabase();
            var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _bucketWidthSeconds;
            var key = (RedisKey)$"gamekit:admin:errors:{bucket}";
            await db.StringIncrementAsync(key).ConfigureAwait(false);
            await db.KeyExpireAsync(key, _keyTtl).ConfigureAwait(false);
        }
        catch { /* swallow — Redis unavailable degrades to in-memory counter only */ }
    }
```

**Read pattern** — MGET over sliding window, mirrors `ErrorRateRingBuffer.RecentErrorCount()` logic (lines 53-61):
```csharp
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
        catch { return -1; }  // sentinel: Redis unavailable
    }
```

---

### `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` (MODIFIED — dual-write)

**Analog:** `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` (lines 1-52) — this IS the file being modified.

**Current pattern** (lines 15-52): inject `ErrorRateRingBuffer buf` only; `CountingLogger.Log` calls `_buf.IncrementError()`.

**Change: add optional `IRedisErrorRateCounter?`** — follow the optional-injection pattern from `HealthProbeService` (line 38: `IConnectionMultiplexer? redis = null`):

```csharp
// MODIFIED: Add optional IRedisErrorRateCounter to constructor (line ~21)
public LogErrorCounter(ErrorRateRingBuffer buf, IRedisErrorRateCounter? redis = null)
{
    ArgumentNullException.ThrowIfNull(buf);
    _buf = buf;
    _redis = redis;   // null when no Redis is registered
}

// MODIFIED: CreateLogger passes redis to CountingLogger (line ~28)
public ILogger CreateLogger(string categoryName) => new CountingLogger(_buf, _redis);

// MODIFIED: CountingLogger.Log dual-writes (line ~48)
public void Log<TState>(LogLevel level, EventId id, TState state,
    Exception? ex, Func<TState, Exception?, string> fmt)
{
    if (level < LogLevel.Error) return;
    _buf.IncrementError();
    _redis?.IncrementError();  // fire-and-forget per IRedisErrorRateCounter contract
}
```

---

### `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (MODIFIED — async error rate probe)

**Analog:** `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (lines 1-113) — this IS the file being modified.

**Current `ProbeAsync` pattern** (lines 50-56):
```csharp
public async Task<HealthReport> ProbeAsync(CancellationToken cancellationToken)
{
    var pg = await ProbePostgresAsync(cancellationToken).ConfigureAwait(false);
    var redis = await ProbeRedisAsync(cancellationToken).ConfigureAwait(false);
    var err = ProbeErrorRate();   // CURRENTLY SYNCHRONOUS — must become async
    return new HealthReport(pg, redis, err, _clock.UtcNow);
}
```

**Change: add optional `IRedisErrorRateCounter?` to constructor** (mirrors `IConnectionMultiplexer? redis = null` at line 38):
```csharp
// MODIFIED constructor — add optional redisErrors parameter last
public HealthProbeService(
    GameKitOptions gameKitOpts,
    ErrorRateRingBuffer errors,
    IClock clock,
    IConnectionMultiplexer? redis = null,
    IRedisErrorRateCounter? redisErrors = null)
```

**Change: `ProbeErrorRate()` → `ProbeErrorRateAsync()`** — follow existing `ProbeRedisAsync` async pattern (lines 81-99):
```csharp
private async Task<HealthTile> ProbeErrorRateAsync(CancellationToken ct)
{
    long count;
    if (_redisErrors is not null)
    {
        count = await _redisErrors.RecentErrorCountAsync(ct).ConfigureAwait(false);
        if (count < 0)  // Redis unavailable — fall back to in-memory
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

---

### `src/GameKit.Admin.UI/Hubs/AdminEventHub.cs` (hub, event-driven)

**Analog:** `src/GameKit.Lobby/Hubs/LobbyHub.cs` (lines 1-221)

**CRITICAL DIFFERENCE from LobbyHub:** `LobbyHub` uses `[Authorize]` (player JWT Bearer). `AdminEventHub` MUST use `[Authorize(Policy = AdminPolicies.Admin)]` to pin the `GameKitAdmin` cookie scheme. See `AdminBuilderExtensions.cs` lines 141-144 for the policy definition.

**Imports pattern** (mirrors LobbyHub lines 1-14, simplified — no ILobbyService needed):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using GameKit.Admin.UI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameKit.Admin.UI.Hubs;
```

**Hub class pattern** (mirrors LobbyHub lines 46-65 structure):
```csharp
/// <summary>
/// Admin live-event SignalR hub (ADMIN-13 / ADMIN-15).
/// Gated by the <c>GameKitAdmin</c> COOKIE scheme via
/// <see cref="AdminPolicies.Admin"/> — NOT the player JWT Bearer scheme.
/// <c>AdminLiveBroadcastService</c> injects <see cref="IHubContext{AdminEventHub}"/>
/// to broadcast; this hub is receive-only for connected admin clients.
/// </summary>
/// <remarks>
/// The hub is mapped under <see cref="GameKitAdminOptions.MountPath"/> at
/// <c>{MountPath}/hubs/events</c> so the path-based default scheme selector in
/// <c>AddGameKitAdmin</c> routes WebSocket upgrade requests to the <c>GameKitAdmin</c>
/// cookie scheme (Pitfall 2 mitigation).
/// </remarks>
[Authorize(Policy = AdminPolicies.Admin)]
public sealed class AdminEventHub : Hub
{
    // Receive-only: AdminLiveBroadcastService broadcasts via IHubContext<AdminEventHub>.
    // No ILobbyService equivalent needed — the hub has no server-callable methods.
}
```

---

### `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs` (BackgroundService, pub-sub)

**Analog:** Lobby-pattern `BackgroundService` (e.g., `RankDecayBackgroundService` structure) + `IConnectionMultiplexer` subscriber usage from `HealthProbeService.cs` (lines 81-99 for the `IConnectionMultiplexer? redis` optional injection pattern).

**Imports pattern:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace GameKit.Admin.UI.Services;
```

**BackgroundService core pattern** — `IConnectionMultiplexer` injected as nullable (Pitfall 4 mitigation) with short-circuit when null:
```csharp
internal sealed class AdminLiveBroadcastService : BackgroundService
{
    private const string Channel = "gamekit:admin:events";
    private readonly IConnectionMultiplexer? _mux;
    private readonly IHubContext<AdminEventHub> _hub;

    /// <summary>Constructs the broadcast relay.</summary>
    /// <param name="mux">Redis multiplexer. When <see langword="null"/>, the service
    /// is a no-op (single-instance deployments without Redis).</param>
    /// <param name="hub">Hub context for broadcasting to admin sessions.</param>
    public AdminLiveBroadcastService(IHubContext<AdminEventHub> hub,
        IConnectionMultiplexer? mux = null)
    {
        _hub = hub;
        _mux = mux;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_mux is null) return;  // no Redis — Pitfall 4 short-circuit

        var sub = _mux.GetSubscriber();
        var queue = await sub.SubscribeAsync(RedisChannel.Literal(Channel))
            .ConfigureAwait(false);
        stoppingToken.Register(() => queue.Unsubscribe());

        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _hub.Clients.All
                    .SendAsync("ReceiveAdminEvent", message.Message.ToString(), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch { /* swallow — individual relay failure must not kill the service */ }
        }
    }
}
```

---

### `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` (MODIFIED)

**Analog:** `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` (lines 1-241, existing file) for overall structure; `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` (lines 71-84) for the `AddSignalR().AddStackExchangeRedis` + `IPostConfigureOptions<RedisOptions>` pattern.

**Insertion point:** After comment block `// 7. Error-rate ring buffer + log provider` (line 159-163) and before `// 8. Rate limiter` (line 165). New registrations at position 8+ (shift existing numbering):

**SC#1 Redis counter registration** — conditional on `IConnectionMultiplexer` presence; mirrors `HealthProbeService`'s optional-redis pattern:
```csharp
// 8. ADMIN-14: opt-in Redis error counter. TryAddSingleton returns null when no
//    IConnectionMultiplexer is registered — HealthProbeService falls back to ErrorRateRingBuffer.
builder.Services.TryAddSingleton<IRedisErrorRateCounter>(sp =>
{
    var mux = sp.GetService<IConnectionMultiplexer>();
    if (mux is null) return null!;  // single-instance install — in-memory only
    return new RedisErrorRateCounter(mux, sp.GetRequiredService<GameKitAdminOptions>());
});
```

**SC#2 SignalR backplane + AdminEventHub** — copy from `LobbyBuilderExtensions.cs` lines 73-84, rename `LobbyRedisBackplanePostConfigure` → `AdminBackplanePostConfigure`:
```csharp
// 9. ADMIN-13: SignalR Redis backplane (ChannelPrefix "GameKit" matches Lobby —
//    same prefix; hub-type isolation ensures no cross-delivery, see A4).
//    TryAddEnumerable: if AddLobby() already registered one PostConfigure, the Admin
//    one stacks on top (both set ConnectionFactory to the same multiplexer — idempotent).
builder.Services.AddSignalR()
    .AddStackExchangeRedis(options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
    });
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IPostConfigureOptions<RedisOptions>,
        AdminBackplanePostConfigure>());

// 10. ADMIN-13: background relay service — only when Redis is available (Pitfall 4).
//     IConnectionMultiplexer? injection short-circuits ExecuteAsync when null.
builder.Services.AddHostedService<AdminLiveBroadcastService>();
```

---

### `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` (MODIFIED)

**Analog:** `src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs` (lines 38-44) for `MapHub<T>` pattern; `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` (lines 45-74, existing `MapGameKitAdmin`) for `mount` variable and `routes.ServiceProvider` resolution.

**MapHub insertion** — after `routes.MapAdminFormEndpoints(mount)` (line 62) and before `routes.MapRazorComponents<Components.App>()` (line 69):
```csharp
// ADMIN-13: AdminEventHub at {mount}/hubs/events.
// MUST be under MountPath so the path-based default scheme selector in
// AddGameKitAdmin routes /admin/* to the GameKitAdmin cookie scheme (Pitfall 2).
routes.MapHub<GameKit.Admin.UI.Hubs.AdminEventHub>($"{mount}/hubs/events");
```

---

### `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor` (MODIFIED — stub replacement)

**Analog:** `src/GameKit.Admin.UI/Components/Pages/Players.razor` (lines 1-222) for search-field + result-list pattern; `src/GameKit.Admin.UI/Components/Shared/PlayerDetailPane.razor` (lines 302-317) for `IDialogService.ShowAsync<TDialog>` invocation pattern.

**Keep unchanged from existing stub** (lines 10-12):
```razor
@page "/admin/rankings/adjust"
@attribute [Authorize(Policy = AdminPolicies.Superadmin)]
@implements IDisposable
```

**Replace `@inject IServiceProvider Sp` section** — add `IDialogService` and `IPlayerSearchService` (mirrors `PlayerDetailPane.razor` line 29: `@inject IDialogService Dialogs`):
```razor
@inject IServiceProvider Sp
@inject IDialogService DialogService
@inject IPlayerSearchService SearchSvc
```

**Page head pattern** — matches existing stub line 16:
```razor
<div class="page-head">
    <h1>Rank adjust</h1>
</div>
```

**MissingPackageAlert guard** — keep exact existing pattern (lines 20-23) but fix DI check (replace `Type.GetType` reflection):
```razor
@if (!_rankingsInstalled)
{
    <MissingPackageAlert PackageName="Rankings" Feature="manual rank adjustments" />
}
```

**Search + dialog launch** — copy `MudTextField` from `Players.razor` (lines 35-46) then loop rows with "Adjust" button that calls `ShowAsync<RankAdjustDialog>`. Mirror dialog launch from `PlayerDetailPane.razor` (lines 302-316):
```razor
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
        <a class="master-row" @onclick="@(() => OpenRankAdjustAsync(row.Id, row.DisplayName))"
           @onclick:preventDefault="true">
            <span>@row.DisplayName</span>
            <MudButton Variant="Variant.Outlined" Color="Color.Primary">Adjust Rating</MudButton>
        </a>
    }
}
```

**`@code` block** — replace `OnInitialized` guard with direct DI check (research §SC#3 anti-pattern):
```csharp
@code {
    private bool _rankingsInstalled;
    private string _query = string.Empty;
    private readonly List<PlayerRow> _rows = new();
    private CancellationTokenSource? _cts;

    protected override void OnInitialized()
    {
        // Direct DI check — avoids fragile Type.GetType string-based reflection (anti-pattern)
        _rankingsInstalled = Sp.GetService<GameKit.Rankings.Services.IRankAdjustService>() is not null;
    }
```

**Dialog launch** — exact mirror of `PlayerDetailPane.OpenBanDialog` (lines 302-316), substitute `RankAdjustDialog`:
```csharp
    private async Task OpenRankAdjustAsync(Guid playerId, string displayName)
    {
        var parameters = new DialogParameters
        {
            ["PlayerId"] = playerId,
            ["DisplayName"] = displayName,
        };
        await DialogService.ShowAsync<Dialogs.RankAdjustDialog>(
            $"Adjust rating for {displayName}",
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
    }

    public void Dispose() => _cts?.Cancel();
}
```

---

### `docs/ops/multi-replica.md` (new ops documentation)

**Analog:** `docs/ops/bare-metal.md` for SPDX comment header and section structure.

**Header pattern** (mirrors all existing ops docs):
```markdown
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Multi-replica deployment
```

**Content sections** (from RESEARCH.md §SC#2 Blazor Multi-Replica):
1. Requirements (sticky sessions + Redis backplane + shared Data Protection key ring)
2. Data Protection key sharing (critical — `AddDataProtection().PersistKeysTo*` options)
3. SignalR backplane (auto-provided via `AddGameKitAdmin()`)
4. Sticky sessions recommendation (STRONGLY RECOMMENDED even with backplane)

---

## Test File Patterns

### `tests/GameKit.Admin.Integration.Tests/RedisErrorCounterTests.cs` (SC#1 test)

**Analog:** `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` (lines 1-99) for `AdminTestHost.StartAsync` + `host.Resolve<T>()` pattern; `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` (lines 22-162) for two-instance shared-Redis setup.

**Collection + trait pattern** (exact copy from HealthProbeTests.cs lines 21-23):
```csharp
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class RedisErrorCounterTests : IAsyncLifetime
```

**Two-host setup** — copy `BackplaneTests.cs` lines 28-55 structure (two `AdminTestHost` instead of two `LobbyTestApp`):
```csharp
private readonly PostgresFixture _pg;
private readonly RedisFixture _redis;
private AdminTestHost _hostA = default!;
private AdminTestHost _hostB = default!;

public RedisErrorCounterTests(PostgresFixture pg, RedisFixture redis)
{
    _pg = pg;
    _redis = redis;
}

public async Task InitializeAsync()
{
    _hostA = await AdminTestHost.StartAsync(_pg, _redis, env: "Production",
        seed: h => h.SeedAdminAsync("root", "hunter2", AdminRoles.Superadmin));
    _hostB = await AdminTestHost.StartAsync(_pg, _redis, env: "Production",
        seed: h => h.SeedAdminAsync("root", "hunter2", AdminRoles.Superadmin));
}

public async Task DisposeAsync()
{
    await _hostA.DisposeAsync();
    await _hostB.DisposeAsync();
}
```

**SC#1 assertion** — resolve `IRedisErrorRateCounter` from host A, `IHealthProbeService` from host B (mirrors `HealthProbeTests.cs` lines 38-49):
```csharp
[Fact(DisplayName = "SC#1: 15 errors on host A visible as Degraded on host B via Redis counter")]
public async Task CrossReplica_ErrorRate_Visible_Across_Hosts()
{
    var (scopeA, counterA) = _hostA.Resolve<IRedisErrorRateCounter>();
    using (scopeA)
    {
        for (var i = 0; i < 15; i++) counterA.IncrementError();
        await Task.Delay(100);  // allow fire-and-forget Redis writes to land
    }

    var (scopeB, probeB) = _hostB.Resolve<IHealthProbeService>();
    using (scopeB)
    {
        var report = await probeB.ProbeAsync(default);
        Assert.Equal("Degraded", report.ErrorRate.Status);
    }
}
```

---

### `tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs` (SC#2 test)

**Analog:** `tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs` (lines 1-90) for unauthenticated 401 pattern; `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` (lines 64-121) for `HubConnectionBuilder` + `TaskCompletionSource` cross-instance backplane assertion.

**Hub connection factory pattern** (mirrors `LobbyTestApp.ConnectLobbyHubAsync` lines 254-263, adapted for admin cookie auth):
```csharp
// Admin hub uses COOKIE auth, not JWT Bearer query-string.
// The HubConnection must carry the admin session cookie (from POST /admin/api/login).
private HubConnection ConnectAdminHub(AdminTestHost host, HttpClient authenticatedClient)
{
    return new HubConnectionBuilder()
        .WithUrl($"http://localhost{host.MountPath}/hubs/events", o =>
        {
            o.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
            // Cookie is already in the CookieContainer of authenticatedClient —
            // use the same handler so the cookie is forwarded to the WebSocket upgrade.
        })
        .Build();
}
```

**Unauthenticated 401 pattern** (exact copy of `HubAuthTests.cs` lines 44-68, adjust URL to `/admin/hubs/events`):
```csharp
[Fact(DisplayName = "SC#2: unauthenticated WebSocket upgrade to /admin/hubs/events returns 401")]
public async Task Unauthenticated_Upgrade_Returns_401()
{
    var conn = new HubConnectionBuilder()
        .WithUrl("http://localhost/admin/hubs/events", o =>
        {
            o.HttpMessageHandlerFactory = _ => _hostA.Server.CreateHandler();
        })
        .Build();

    var ex = await Assert.ThrowsAsync<HttpRequestException>(() => conn.StartAsync());
    Assert.True(ex.StatusCode == HttpStatusCode.Unauthorized
                || ex.Message.Contains("401"));
    await conn.DisposeAsync();
}
```

**Cross-instance backplane pattern** — mirrors `BackplaneTests.cs` lines 82-120 (`TaskCompletionSource` + timeout):
```csharp
var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
connB.On<string>("ReceiveAdminEvent", payload => tcs.TrySetResult(payload));

// Publish via host A's IConnectionMultiplexer directly
var (scope, mux) = _hostA.Resolve<IConnectionMultiplexer>();
using (scope) { await mux.GetSubscriber().PublishAsync("gamekit:admin:events", "ping"); }

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var received = await tcs.Task.WaitAsync(cts.Token);
Assert.Equal("ping", received);
```

---

### `tests/GameKit.Admin.Integration.Tests/RankAdjustServiceTests.cs` (SC#3 test)

**Analog:** `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` (lines 1-99) for `AdminTestHost.StartAsync` + `host.Resolve<T>()` + `CreateDbScope()` patterns.

**AdminTestHost with Rankings migrations** — extend `AdminTestHost.MigrateAsync` OR use `configureExtraServices` to add Rankings (per RESEARCH §Pitfall 5):
```csharp
// Use configureExtraServices hook (AdminTestHost.cs line 187) to register Rankings
await using var host = await AdminTestHost.StartAsync(
    _pg, _redis, env: "Production",
    seed: h => h.SeedAdminAsync("superadmin", "P@ss1234", AdminRoles.Superadmin),
    configureExtraServices: services =>
    {
        // Register Rankings after the standard chain
        // NOTE: Rankings migrations must be run separately (Pitfall 5)
    });
```

**Audit log assertion** — mirrors `PlayerBanServiceTests` pattern for `admin_audit_log` row check via `CreateDbScope()`:
```csharp
var (scope, ctx) = host.CreateDbScope();
await using (scope)
{
    var auditRow = await ctx.AdminAuditLog
        .FirstOrDefaultAsync(r => r.Action == "admin.player.rank_adjust"
                                  && r.TargetId == playerId);
    Assert.NotNull(auditRow);
    Assert.Equal(actorId, auditRow.ActorId);
}
```

---

### `tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs` (MODIFIED — SC#4)

**Analog:** `tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs` (lines 1-111) — this IS the file being modified.

**Existing pattern** (lines 39-48) — extend `AllSevenGameKitPackages` array to include the five new packages:
```csharp
// MODIFIED: extend AllSevenGameKitPackages to AllTwelveGameKitPackages
private static readonly string[] AllTwelveGameKitPackages =
{
    // Original 7 (unchanged)
    "GameKit.Core",
    "GameKit.Auth",
    "GameKit.Rankings",
    "GameKit.Matchmaking",
    "GameKit.Admin.UI",
    "GameKit.Presence",
    "GameKit.OpenApi",
    // Phase 12 additions (DIST-07)
    "GameKit.Auth.Argon2",
    "GameKit.Auth.Google",
    "GameKit.Auth.Apple",
    "GameKit.Auth.Epic",
    "GameKit.Lobby",
};
```

**Reflection pattern** (lines 62-73) — keep identical, just use the new array name. The `Assembly.Load(packageName)` + `GetType($"{packageName}.Internal.GameKitMarker")` + `BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static` pattern is unchanged.

**csproj ProjectReference additions required** (mirrors existing lines 32-41 of Distribution.Integration.Tests.csproj):
```xml
<ProjectReference Include="..\..\src\GameKit.Auth.Argon2\GameKit.Auth.Argon2.csproj" />
<ProjectReference Include="..\..\src\GameKit.Auth.Google\GameKit.Auth.Google.csproj" />
<ProjectReference Include="..\..\src\GameKit.Auth.Apple\GameKit.Auth.Apple.csproj" />
<ProjectReference Include="..\..\src\GameKit.Auth.Epic\GameKit.Auth.Epic.csproj" />
<!-- GameKit.Lobby already a dep via the standard chain, verify it's present -->
<ProjectReference Include="..\..\src\GameKit.Lobby\GameKit.Lobby.csproj" />
```

---

## Shared Patterns

### SPDX + GPL Header
**Source:** All `src/GameKit.*` files
**Apply to:** Every new `.cs` and `.razor` file
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```
For `.razor`:
```razor
@* SPDX-License-Identifier: GPL-3.0-or-later *@
@* Copyright (c) 2026 GameKit contributors *@
```
For `.md`:
```markdown
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->
```

### XML Doc on Every Public API Member (CS1591-as-error)
**Source:** `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (lines 9-47)
**Apply to:** `IRedisErrorRateCounter`, `RedisErrorRateCounter`, `AdminEventHub`, `AdminLiveBroadcastService` — every public member needs `<summary>`.
```csharp
/// <summary>Summary here. Omit and the build fails with CS1591.</summary>
```

### Admin Authorization Policy Binding (Cookie-Only)
**Source:** `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` (lines 139-149)
**Apply to:** `AdminEventHub.cs`
```csharp
// Policy pins GameKitAdmin COOKIE scheme. JWT Bearer CANNOT satisfy this.
[Authorize(Policy = AdminPolicies.Admin)]
```
The `AdminPolicies.Admin` policy is defined at lines 141-144:
```csharp
ao.AddPolicy(AdminPolicies.Admin, p => p
    .AddAuthenticationSchemes(AdminAuthenticationSchemeConstants.Scheme)  // "GameKitAdmin"
    .RequireAuthenticatedUser()
    .RequireRole(AdminRoles.Admin, AdminRoles.Superadmin));
```

### Optional IConnectionMultiplexer (Never Required)
**Source:** `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (lines 34-38)
**Apply to:** `AdminLiveBroadcastService` constructor, `IRedisErrorRateCounter` registration
```csharp
// Pattern: optional last parameter, null = single-instance with no Redis
public HealthProbeService(..., IConnectionMultiplexer? redis = null)
```

### IPostConfigureOptions<RedisOptions> Backplane Pattern
**Source:** `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs` (lines 1-48)
**Apply to:** New `AdminBackplanePostConfigure.cs` — copy verbatim, change class name and namespace:
```csharp
// Exact copy of LobbyRedisBackplanePostConfigure — change class name only
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

### TryAddEnumerable for IPostConfigureOptions
**Source:** `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` (lines 83-84)
**Apply to:** `AdminBuilderExtensions.cs` SC#2 backplane registration
```csharp
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IPostConfigureOptions<RedisOptions>, AdminBackplanePostConfigure>());
```

### AdminTestHost Two-Instance Pattern
**Source:** `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` (lines 28-56) + `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` (lines 96-107)
**Apply to:** `RedisErrorCounterTests.cs`, `AdminEventHubTests.cs`
```csharp
// Both hosts share the same RedisFixture → same Redis container → shared backplane
_hostA = await AdminTestHost.StartAsync(_pg, _redis, ...);
_hostB = await AdminTestHost.StartAsync(_pg, _redis, ...);
```

### HubConnection with Server.CreateHandler()
**Source:** `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` (lines 254-263)
**Apply to:** `AdminEventHubTests.cs` hub connection construction
```csharp
new HubConnectionBuilder()
    .WithUrl("http://localhost/admin/hubs/events", o =>
    {
        o.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
        // Admin uses cookie auth, not JWT query-string; cookie forwarded via handler
    })
    .Build();
```

### UseWebSockets Before UseRouting (TestServer)
**Source:** `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` (lines 177-179)
**Apply to:** Any new test host that needs SignalR WebSocket transport
```csharp
app.UseWebSockets();  // MUST precede UseRouting for TestServer WebSocket
app.UseRouting();
```

---

## No Analog Found

All files have analogs in the codebase. No entries in this section.

---

## Metadata

**Analog search scope:** `src/GameKit.Admin.UI/`, `src/GameKit.Lobby/`, `src/GameKit.Core/`, `tests/GameKit.Admin.Integration.Tests/`, `tests/GameKit.Lobby.Integration.Tests/`, `tests/GameKit.Distribution.Integration.Tests/`, `docs/ops/`
**Files scanned:** 34
**Pattern extraction date:** 2026-06-06

---

## Key Patterns Summary

1. **Optional-Redis pattern:** `IConnectionMultiplexer? redis = null` on constructors — copied verbatim from `HealthProbeService` line 38. Used in `AdminLiveBroadcastService` and `IRedisErrorRateCounter` registration.
2. **Backplane pattern:** `IPostConfigureOptions<RedisOptions>` + `TryAddEnumerable` — copied verbatim from `LobbyRedisBackplanePostConfigure` (35 lines total). The Admin variant is a rename-only copy.
3. **Admin hub auth:** `[Authorize(Policy = AdminPolicies.Admin)]` only — policy pins `"GameKitAdmin"` cookie scheme. Never `[Authorize]` alone (that would fall back to JWT Bearer).
4. **Two-TestServer backplane test:** Copy `BackplaneTests.cs` structure with `AdminTestHost` instances sharing `RedisFixture`. `TaskCompletionSource<T>` + `WaitAsync(CancellationToken)` for cross-replica assertion.
5. **Stub replacement (RankAdjust.razor):** Replace `OnInitialized` `Type.GetType` check with `Sp.GetService<IRankAdjustService>() is not null`. Replace alert placeholder with `MudTextField` search + `IDialogService.ShowAsync<RankAdjustDialog>` — mirrors `PlayerDetailPane.OpenBanDialog` exactly.
6. **Version-train extension:** Add 5 package names to existing array in `OPS04_VersionStampedAcrossPackagesTests.cs`. Add 5 `ProjectReference` items to Distribution.Integration.Tests.csproj. No production code changes.
