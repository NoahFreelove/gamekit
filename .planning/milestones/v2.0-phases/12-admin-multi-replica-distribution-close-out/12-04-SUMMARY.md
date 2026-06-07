---
phase: 12-admin-multi-replica-distribution-close-out
plan: "04"
subsystem: admin, infra, signalr
tags: [signalr, redis-backplane, admin-hub, cookie-auth, multi-replica, testcontainers, ops-doc]

# Dependency graph
requires:
  - phase: 12-admin-multi-replica-distribution-close-out
    plan: "03"
    provides: AdminBuilderExtensions SC#1 Redis counter block — SC#2 block inserted after it

provides:
  - AdminEventHub: receive-only SignalR hub gated by [Authorize(Policy=AdminPolicies.Admin)] — GameKitAdmin cookie scheme only
  - AdminBackplanePostConfigure: IPostConfigureOptions<RedisOptions> defers multiplexer resolution; TryAddEnumerable-safe
  - AdminLiveBroadcastService: BackgroundService subscribing to "gamekit:admin:events" Redis Pub/Sub; relays via IHubContext<AdminEventHub>.Clients.All.SendAsync("ReceiveAdminEvent", ...)
  - MapHub<AdminEventHub>({mount}/hubs/events) in MapGameKitAdmin under MountPath — path-based scheme selector routes to GameKitAdmin cookie scheme
  - docs/ops/multi-replica.md: Data Protection key-ring sharing + Redis backplane + sticky-sessions ops guide (never Azure SignalR)
  - AdminEventHubTests (3 tests): SC#2 cookie-only auth (401/404 unauthenticated) + player JWT rejection + cross-replica backplane delivery

affects:
  - Any plan adding admin event publishers (must publish to "gamekit:admin:events" Redis channel)
  - Any plan adding Blazor components that consume "ReceiveAdminEvent" hub events

# Tech tracking
tech-stack:
  added:
    - Microsoft.AspNetCore.SignalR.StackExchangeRedis (added to GameKit.Admin.UI.csproj — was transitive, now explicit)
    - Microsoft.AspNetCore.SignalR.Client (added to GameKit.Admin.Integration.Tests.csproj for HubConnectionBuilder)
  patterns:
    - "Hub cookie-scheme gate: [Authorize(Policy=AdminPolicies.Admin)] — policy pins GameKitAdmin scheme; JWT Bearer cannot satisfy"
    - "IPostConfigureOptions<RedisOptions> + TryAddEnumerable: defers multiplexer, idempotent if AddLobby() already registered"
    - "BackgroundService nullable-mux short-circuit: if (_mux is null) return; — safe unconditional registration for no-Redis installs"
    - "ChannelMessageQueue as IAsyncEnumerable: await foreach (var msg in queue.WithCancellation(ct)) — ReadAllAsync() does not exist on the type"
    - "CookieInjectingHandler: DelegatingHandler adds Cookie header to in-process TestServer requests for hub auth tests"
    - "Production AdminCookieEvents returns 404 not 401 for path-enumeration prevention — test asserts 401 OR 404"

key-files:
  created:
    - src/GameKit.Admin.UI/Hubs/AdminEventHub.cs
    - src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs
    - src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs
    - docs/ops/multi-replica.md
    - tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs
  modified:
    - src/GameKit.Admin.UI/GameKit.Admin.UI.csproj
    - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
    - src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs
    - tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs
    - tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj
    - docs/ops/README.md

key-decisions:
  - "ChannelMessageQueue.ReadAllAsync() does not exist in SE.Redis 2.8.41 — use await foreach directly (ChannelMessageQueue implements IAsyncEnumerable<ChannelMessage> via GetAsyncEnumerator; call .WithCancellation(ct))"
  - "Production AdminCookieEvents returns 404 not 401 (path enumeration prevention, already proven in CrossSchemeIsolationTests) — SC#2(a) test accepts 401 OR 404"
  - "ChannelPrefix = GameKit matches AddLobby() registration — hub-type isolation via IHubContext<T> generic parameter prevents cross-delivery between AdminEventHub and LobbyHub"
  - "CookieInjectingHandler DelegatingHandler pattern for forwarding admin session cookie to TestServer HubConnection — Server.CreateHandler() alone does not carry cookies"
  - "AdminBackplanePostConfigure registered via TryAddEnumerable so AddLobby() + AddGameKitAdmin() stack idempotently — both set ConnectionFactory to same multiplexer"

requirements-completed: [ADMIN-13]

# Metrics
duration: ~10min
completed: 2026-06-07
---

# Phase 12 Plan 04: AdminEventHub + Redis Backplane + AdminLiveBroadcastService Summary

**JWT-secure admin live-event hub on a Redis backplane: messages published to `"gamekit:admin:events"` reach all connected admin sessions regardless of which replica they hit; the hub is gated by the `GameKitAdmin` cookie scheme (player JWT refused), proven by three Testcontainers integration tests.**

## Performance

- **Duration:** ~10 min
- **Completed:** 2026-06-07T01:55:12Z
- **Tasks:** 3
- **Files created:** 5
- **Files modified:** 5

## Accomplishments

- Created `AdminEventHub` — receive-only SignalR hub with `[Authorize(Policy = AdminPolicies.Admin)]` pinning the `GameKitAdmin` cookie scheme. No JWT Bearer attribute. No ICurrentPlayer injection. Mapped at `{MountPath}/hubs/events` in `MapGameKitAdmin()`.
- Created `AdminBackplanePostConfigure` — verbatim copy of `LobbyRedisBackplanePostConfigure` with class name + namespace changed. Registered via `TryAddEnumerable` so `AddLobby()` + `AddGameKitAdmin()` stack idempotently without double-registering a multiplexer.
- Created `AdminLiveBroadcastService` — `BackgroundService` subscribing to Redis Pub/Sub literal channel `"gamekit:admin:events"`, relaying via `IHubContext<AdminEventHub>.Clients.All.SendAsync("ReceiveAdminEvent", ...)`. Short-circuits `ExecuteAsync` when `IConnectionMultiplexer` is `null` (Pitfall 4); per-message errors swallowed (T-12-04-DOS).
- Wired SC#2 registration block in `AdminBuilderExtensions` after the Plan 12-03 SC#1 Redis counter block: `AddSignalR().AddStackExchangeRedis(ChannelPrefix="GameKit")` + `TryAddEnumerable(AdminBackplanePostConfigure)` + `AddHostedService<AdminLiveBroadcastService>`.
- Added `routes.MapHub<AdminEventHub>($"{mount}/hubs/events")` in `AdminApplicationBuilderExtensions.MapGameKitAdmin` between `MapAdminFormEndpoints` and `MapRazorComponents` — under MountPath so the path-based scheme selector routes `/admin/hubs/*` to `GameKitAdmin` (Pitfall 2).
- Created `docs/ops/multi-replica.md` — comprehensive ops guide covering Data Protection key-ring sharing (3 options: Redis, file system, EF Core), SignalR Redis backplane auto-provided by `AddGameKitAdmin()`, sticky sessions strongly recommended; explicitly states Redis-only / never Azure SignalR (GPL zero-cloud constraint).
- SC#2 proven by `AdminEventHubTests` (3 Testcontainers tests, all green):
  - (a) Unauthenticated upgrade rejected (401/404 in Production via `AdminCookieEvents` — path enumeration prevention)
  - (b) Player JWT via `AccessTokenProvider` cannot connect — `AdminPolicies.Admin` pins `GameKitAdmin` scheme (cross-scheme isolation, extends Phase 3 `CrossSchemeIsolationTests` to new endpoint)
  - (c) Admin event published on host A via `IConnectionMultiplexer.GetSubscriber().PublishAsync("gamekit:admin:events", "ping-from-host-a")` reaches admin client on host B as `"ReceiveAdminEvent"` within 10s

## Task Commits

1. **Task 1: AdminEventHub + AdminBackplanePostConfigure + AdminLiveBroadcastService** — `a8747e4` (feat)
2. **Task 2: Register backplane + relay; map hub under MountPath; multi-replica ops doc** — `e754737` (feat)
3. **Task 3: SC#2 integration tests** — `e8a0f8c` (test)

**Plan metadata:** (this SUMMARY commit)

## Files Created/Modified

- `src/GameKit.Admin.UI/Hubs/AdminEventHub.cs` — `[Authorize(Policy=AdminPolicies.Admin)]` sealed hub; receive-only (no server-callable methods); XML docs warn against JWT Bearer scheme attribute
- `src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs` — `IPostConfigureOptions<RedisOptions>` defers `IConnectionMultiplexer` resolution to post-build; `TryAddEnumerable`-registered
- `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs` — `BackgroundService`; nullable mux short-circuit; channel literal `"gamekit:admin:events"`; swallows per-message errors
- `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` — added `Microsoft.AspNetCore.SignalR.StackExchangeRedis` explicit package reference
- `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` — SC#2 block: `AddSignalR().AddStackExchangeRedis` + `TryAddEnumerable(AdminBackplanePostConfigure)` + `AddHostedService<AdminLiveBroadcastService>`; added `using` for `Microsoft.AspNetCore.SignalR.StackExchangeRedis`, `Microsoft.Extensions.Options`
- `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` — `MapHub<AdminEventHub>($"{mount}/hubs/events")` insertion; added `using` for hub + SignalR namespaces
- `docs/ops/multi-replica.md` — ADMIN-13 documentation deliverable: Data Protection key sharing (3 options) + SignalR Redis backplane + sticky sessions; zero-cloud constraint documented
- `docs/ops/README.md` — added multi-replica.md to recipe index table
- `tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs` — 3 `[Collection("Admin")]` SC#2 tests with real Testcontainers Postgres + Redis
- `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` — added `Server` (TestServer) + `MountPath` properties; `UseWebSockets()` before `UseRouting()` (RESEARCH Pitfall 7)
- `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` — added `Microsoft.AspNetCore.SignalR.Client` package reference

## Decisions Made

- **`ChannelMessageQueue.ReadAllAsync()` does not exist:** `ChannelMessageQueue` in SE.Redis 2.8.41 implements `IAsyncEnumerable<ChannelMessage>` via `GetAsyncEnumerator` — use `await foreach (var msg in queue.WithCancellation(stoppingToken))`. Confirmed via SE.Redis XML docs.
- **404 not 401 in Production:** `AdminCookieEvents.RedirectToLogin` returns 404 (not 401) for unauthenticated requests to admin paths in Production to prevent path enumeration. SC#2(a) assertion updated to accept either — the security contract is "connection refused before handshake completes", not a specific status code.
- **`CookieInjectingHandler` pattern:** TestServer's `Server.CreateHandler()` returns a bare in-process handler without a `CookieContainer`. Admin hub tests carry the session cookie by wrapping the inner handler in a `DelegatingHandler` that adds the `Cookie` header to every request.
- **`TryAddEnumerable` for both Lobby and Admin post-configurators:** Both set `ConnectionFactory` to the same `IConnectionMultiplexer` singleton — composing them is idempotent and correct.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `ChannelMessageQueue.ReadAllAsync()` does not exist**
- **Found during:** Task 1 (first build)
- **Issue:** `CS1061: 'ChannelMessageQueue' does not contain a definition for 'ReadAllAsync'`. The PATTERNS.md pattern was based on `System.Threading.Channels.ChannelReader.ReadAllAsync` — `ChannelMessageQueue` (SE.Redis) implements `IAsyncEnumerable<ChannelMessage>` directly via `GetAsyncEnumerator`, not via `ReadAllAsync`.
- **Fix:** Changed `queue.ReadAllAsync(stoppingToken)` to `queue.WithCancellation(stoppingToken)` in `AdminLiveBroadcastService.ExecuteAsync`
- **Files modified:** `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs`
- **Commit:** `a8747e4` (Task 1)

**2. [Rule 1 - Bug] SC#2(a) assertion rejected valid 404 response**
- **Found during:** Task 3 (first test run)
- **Issue:** Test SC#2(a) asserted only `401 Unauthorized`, but in Production mode `AdminCookieEvents.RedirectToLogin` returns `404` to prevent path enumeration (same behavior proven by `CrossSchemeIsolationTests.PlayerJwt_InBearerHeader_CannotAccessAdminEndpoints_InProduction`). The test failed with `Expected 401 Unauthorized but got: NotFound / 404`.
- **Fix:** Updated assertion to accept `401` OR `404` with explanatory comment citing `CrossSchemeIsolationTests` precedent. Updated `DisplayName` to reflect the correct contract.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs`
- **Commit:** `e8a0f8c` (Task 3)

**Total deviations:** 2 auto-fixed (1 build error, 1 incorrect test assertion). No scope creep.

## Threat Surface Scan

New endpoints added:
- `{MountPath}/hubs/events` (SignalR negotiate + WebSocket) — T-12-04-SPOOF, T-12-04-SPOOF2 mitigated via `[Authorize(Policy=AdminPolicies.Admin)]` + MountPath placement
- `"gamekit:admin:events"` Redis Pub/Sub channel — T-12-04-TAM mitigated (literal channel, no user input); T-12-04-DOS mitigated (per-message error swallowing); T-12-04-INF mitigated (admin-only hub)

No PII flows through the channel in this plan. Future publishers must scope payloads to what the admin role may see (documented in `AdminEventHub.cs` XML remarks and `AdminLiveBroadcastService.cs` XML remarks).

## Known Stubs
None — all code paths are fully wired.

## Self-Check: PASSED

- `src/GameKit.Admin.UI/Hubs/AdminEventHub.cs` — FOUND
- `src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs` — FOUND
- `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs` — FOUND
- `docs/ops/multi-replica.md` — FOUND (6 occurrences of "Data Protection")
- `tests/GameKit.Admin.Integration.Tests/AdminEventHubTests.cs` — FOUND
- Commit `a8747e4` — FOUND (feat: AdminEventHub + AdminBackplanePostConfigure + AdminLiveBroadcastService)
- Commit `e754737` — FOUND (feat: register backplane + relay; map hub; ops doc)
- Commit `e8a0f8c` — FOUND (test: SC#2 AdminEventHub backplane tests)
- `dotnet build GameKit.sln -warnaserror` — Build succeeded
- `dotnet test --filter AdminEventHub` — Passed! 3/3

---
*Phase: 12-admin-multi-replica-distribution-close-out*
*Completed: 2026-06-07*
