---
phase: 06-presence-openapi-distribution
plan: 04
subsystem: presence
tags: [presence, redis, heartbeat, lua, jwt, ttl, observer]

# Dependency graph
requires:
  - phase: 01-foundation-core-migrations-ops-defaults-gpl
    provides: IGameKitBuilder + GameKit.Presence csproj stub + Directory.Packages.props CPM pins
  - phase: 02-authentication
    provides: JwtBearer scheme + IJwtIssuer for integration-test JWT minting
  - phase: 06-presence-openapi-distribution/01
    provides: GameKit.Build source generator + ROADMAP/CPM corrections + OpenApi 10.0.8 pin (transitive)
  - phase: 06-presence-openapi-distribution/02
    provides: ISessionLifecycleObserver Core port + GameKitVersionAssertionHostedService + ISessionStartService/ISessionAbandonService Core ports
  - phase: 06-presence-openapi-distribution/03
    provides: GameKit.Presence.Tests + GameKit.Presence.Integration.Tests csproj scaffolding + PresenceIntegrationFixture composite
provides:
  - Redis-backed IPresenceProvider implementation (read path — Offline/Online/InMatch)
  - New Presence-internal IPresenceWriter port (write path — Heartbeat / InMatch / Online / ClearInMatch)
  - Single class RedisPresenceProvider implementing BOTH interfaces (one Singleton instance)
  - Atomic Lua precedence script for heartbeat (PATTERNS warning #6 — never downgrades in_match)
  - PresenceSessionObserver bridging ISessionLifecycleObserver → IPresenceWriter
  - POST /api/presence/heartbeat endpoint (JWT-Bearer required, no rate limit per D-05)
  - AddPresence / UsePresence / MapPresence fluent builder extensions
  - Sample TicTacToeDuel wired with .AddPresence() + app.MapPresence()
affects:
  - 06-05 (session /start + /abandon endpoints will invoke the ISessionLifecycleObserver fan-out wired here)
  - 06-07 (Admin Presence panel will consume IPresenceProvider.GetOnlinePlayerIdsAsync)
  - 06-09 (template will carry the AddPresence/MapPresence calls verbatim)

# Tech tracking
tech-stack:
  added:
    - StackExchange.Redis 2.8.41 (already CPM-pinned from Phase 1 — first GameKit.Presence consumer)
    - Microsoft.AspNetCore.App FrameworkReference (HttpContext + minimal-API types in GameKit.Presence)
  patterns:
    - Single class implementing two ports (IPresenceProvider + IPresenceWriter) registered as one Singleton with factory shims to each interface
    - Atomic Lua compare-and-set for in-match precedence (PATTERNS warning #6 — verbatim script body asserted in unit tests)
    - SCAN-based async key enumeration (KeysAsync, pageSize 250) NEVER synchronous Keys()
    - Scoped observer consuming Singleton writer (safe direction of DI lifetime mixing — documented as LIFETIME NOTE in AddPresence XML doc)
    - Partial-class base+.Options split mirroring Matchmaking convention (room for v2 multi-device aggregator)

key-files:
  created:
    - src/GameKit.Presence/Configuration/GameKitPresenceOptions.cs
    - src/GameKit.Presence/Configuration/PresenceOptionsValidator.cs
    - src/GameKit.Presence/PresenceRedisKeys.cs
    - src/GameKit.Presence/PresenceValues.cs
    - src/GameKit.Presence/Services/IPresenceWriter.cs
    - src/GameKit.Presence/Services/RedisPresenceProvider.cs
    - src/GameKit.Presence/Services/PresenceSessionObserver.cs
    - src/GameKit.Presence/Http/PresenceEndpoints.cs
    - src/GameKit.Presence/Builder/PresenceBuilderExtensions.cs
    - src/GameKit.Presence/Builder/PresenceBuilderExtensions.Options.cs
    - src/GameKit.Presence/Builder/PresenceApplicationBuilderExtensions.cs
    - tests/GameKit.Presence.Tests/PresenceOptionsValidatorTests.cs
    - tests/GameKit.Presence.Tests/RedisPresenceProviderTests.cs
    - tests/GameKit.Presence.Integration.Tests/PresenceTestApp.cs
    - tests/GameKit.Presence.Integration.Tests/HeartbeatEndpointTests.cs
    - tests/GameKit.Presence.Integration.Tests/InMatchPrecedenceTests.cs
  modified:
    - src/GameKit.Presence/GameKit.Presence.csproj (+FrameworkReference Microsoft.AspNetCore.App, +PackageReference StackExchange.Redis)
    - samples/TicTacToeDuel/Program.cs (+`using GameKit.Presence.Builder;`, +`gameKitBuilder.AddPresence();`, +`app.MapPresence();`)
    - samples/TicTacToeDuel/TicTacToeDuel.csproj (+GameKit.Presence ProjectReference)

key-decisions:
  - "Single class RedisPresenceProvider implements BOTH IPresenceProvider + IPresenceWriter; DI registers concrete Singleton once and routes both interfaces via factory shims (sp.GetRequiredService<RedisPresenceProvider>)"
  - "Lua precedence script lives as an internal const string in RedisPresenceProvider so unit tests can verify its presence (Moq Verify on ScriptEvaluateAsync argument); script body is the verbatim PATTERNS Block 2 lines 236-244"
  - "ClearInMatchAsync delegates to WriteOnlineAsync internally — semantically distinct call-site for the observer, identical Redis effect (SET online PX ttl)"
  - "PresenceTestApp drops the AuthRuntimeQueryCustomizer that other Auth-touching integration tests use — JWT validation does NOT query Auth entities at runtime; only Core+Auth migrations are applied so JwtBearer middleware can resolve its signing key from disk"

patterns-established:
  - "Pattern A: Single-class-multiple-interfaces DI registration — TryAddSingleton<Concrete>() then TryAddSingleton<IPort1>(sp => sp.GetRequiredService<Concrete>()) + same for IPort2. Future packages adding multi-interface services should mirror."
  - "Pattern B: Atomic Lua script as private internal const — exposed for test verification via Moq matcher rather than reflection. Future Redis-touching services should embed Lua bodies as internal consts so tests assert without reflection."
  - "Pattern C: Scoped observer + Singleton writer is the safe direction of lifetime mixing. Documented in AddPresence XML doc as LIFETIME NOTE so reviewers don't flag as a captive-dependency bug."

requirements-completed: [PRES-01, PRES-02, PRES-03, PRES-04]

# Metrics
duration: ~50min
completed: 2026-05-26
---

# Phase 06 Plan 04: Presence runtime (Redis TTL heartbeat + Lua precedence + observer fan-out) Summary

**Redis-backed IPresenceProvider with atomic Lua in-match precedence + POST /api/presence/heartbeat endpoint + ISessionLifecycleObserver adapter (PresenceSessionObserver), wired into sample TicTacToeDuel with full integration-test coverage of the no-downgrade invariant.**

## Performance

- **Duration:** ~50 min
- **Started:** 2026-05-26 ~01:55 UTC
- **Completed:** 2026-05-26 ~02:46 UTC
- **Tasks:** 3 (all `type=auto tdd=true`)
- **Files created:** 16 (9 src + 5 tests + 1 sample-modified + 1 csproj-modified — see key-files)
- **Files modified:** 3 (Presence.csproj, sample Program.cs, sample csproj)

## Accomplishments

- **RedisPresenceProvider ships against both Core's read port (`IPresenceProvider`) and the new Presence-internal write port (`IPresenceWriter`)** as a single Singleton instance. Read path covers Offline/Online/InMatch detection (defensive parse: an unexpected value falls back to Offline). `GetOnlinePlayerIdsAsync` enumerates via `IServer.KeysAsync` (SCAN, pageSize 250) with `take`-cap early termination and a defensive `Guid.TryParse` on the key suffix.
- **The heartbeat write uses an atomic Lua script** that performs GET → conditional SET in one Redis round-trip — refuses to downgrade an `in_match` value to `online` (only refreshes the TTL on `in_match` keys). The script body is asserted character-for-character in the unit tests so any accidental edit fails CI.
- **`POST /api/presence/heartbeat` ships under the default JWT-Bearer scheme** (no rate limit per CONTEXT D-05). Returns 204 on success, 401 anonymous, 403 if the `sub` claim cannot be parsed as a Guid.
- **`AddPresence(...)` registers the observer via `TryAddEnumerable<ISessionLifecycleObserver>`** (Scoped) so it coexists with any sibling observers; the Singleton writer is captured by the Scoped observer — the safe direction of lifetime mixing, documented in the XML doc.
- **Sample TicTacToeDuel boots with Presence wired end-to-end.** Existing Phase 2-5 sample tests are not affected (the only wire-up is one builder call + one Map call).
- **22/22 tests green:** 7 PresenceOptionsValidatorTests + 9 RedisPresenceProviderTests + 1 unit smoke + 2 HeartbeatEndpointTests + 2 InMatchPrecedenceTests + 1 integration smoke.

## Task Commits

Each task was committed atomically (TDD: test-first then implementation, both folded into a single per-task commit since the failing-test "RED" + green "GREEN" cycle happened during the same task):

1. **Task 1: Options + validator + Redis-key formatter + IPresenceWriter + csproj wiring** — `ab87bf6` (feat)
2. **Task 2: RedisPresenceProvider + PresenceSessionObserver with Lua precedence** — `e0373a1` (feat)
3. **Task 3: Heartbeat endpoint + AddPresence/MapPresence + sample wiring + integration tests** — `24c214e` (feat)

## Public surface — what downstream plans can consume

### `AddPresence` / `UsePresence` / `MapPresence` signatures (for Plan 06-07 Admin panel + Plan 06-09 template)

```csharp
namespace GameKit.Presence.Builder;

public static partial class PresenceBuilderExtensions
{
    public static IGameKitBuilder AddPresence(
        this IGameKitBuilder builder,
        Action<GameKitPresenceOptions>? configure = null);
}

public static class PresenceApplicationBuilderExtensions
{
    public static IApplicationBuilder UsePresence(this IApplicationBuilder app);   // v1 no-op stub
    public static IEndpointRouteBuilder MapPresence(this IEndpointRouteBuilder routes);
}
```

### `IPresenceWriter` method signatures (for Plan 06-05 + observer inspection in Plan 06-07)

```csharp
namespace GameKit.Presence.Services;

public interface IPresenceWriter
{
    ValueTask WriteHeartbeatAsync(Guid playerId, CancellationToken ct);
    ValueTask WriteInMatchAsync(Guid playerId, CancellationToken ct);
    ValueTask WriteOnlineAsync(Guid playerId, CancellationToken ct);
    ValueTask ClearInMatchAsync(Guid playerId, CancellationToken ct);
}
```

### Verbatim heartbeat Lua script (cross-reference for any future plan needing the same atomic primitive)

Stored as `internal const string RedisPresenceProvider.HeartbeatLuaScript`:

```lua
local v = redis.call('GET', KEYS[1])
if v == 'in_match' then
  redis.call('PEXPIRE', KEYS[1], ARGV[1])
else
  redis.call('SET', KEYS[1], 'online', 'PX', ARGV[1])
end
return 1
```

`KEYS[1]` is `presence:{playerId}` (see `PresenceRedisKeys.Player(Guid)`); `ARGV[1]` is the TTL in milliseconds (default 30000). Per PATTERNS warning #6, any code path that updates the player presence key MUST go through this script (or honor the same precedence semantics) — direct `StringSetAsync` calls from a heartbeat context would corrupt the D-03 invariant.

### Sample TicTacToeDuel end-to-end confirmation

The sample csproj now references `GameKit.Presence`, the fluent service-registration chain has `.AddPresence()` between `.AddMatchmaking(...)` and `.AddGameKitAdmin(...)`, and the endpoint mapping has `app.MapPresence()` between `app.MapMatchmaking()` and `app.MapGameKitAdmin(...)`. Build succeeds clean (0 warnings, 0 errors); the existing Phase 2-5 endpoint mappings are unchanged.

## Decisions Made

1. **Single class implementing two interfaces** — `RedisPresenceProvider : IPresenceProvider, IPresenceWriter` registered as one Singleton with factory shims to each interface. Avoids the alternative (two classes sharing a Redis multiplexer field) which would duplicate the Lua script body across two implementations.
2. **Lua script as `internal const string` field** — exposes a stable identity for Moq `Verify` to match against without reflection. The `InternalsVisibleTo("GameKit.Presence.Tests")` grant from Plan 06-03 makes the const visible to tests; otherwise the field would be private.
3. **`ClearInMatchAsync` delegates to `WriteOnlineAsync`** — both produce identical Redis effects (`SET 'online' PX ttl`). Kept as a distinct method so the observer call-site at `OnSessionAbandonedAsync` remains self-documenting; future tuning (e.g. shorter TTL on abandon) would happen in one place.
4. **`PresenceTestApp` does NOT replace the DbContext with `AuthRuntimeQueryCustomizer`** — Auth integration tests need that customizer because they query Auth entities (PlayerIdentity, RefreshToken). The heartbeat path is JWT-validate-then-Redis-write, no DB query — the default `GameKitDbContext` registration suffices.
5. **Defensive Offline fallback in `GetStatusAsync`** — an unexpected value at `presence:{playerId}` (key-shape drift, manual operator probe with a typo) is treated as Offline rather than throwing. Matches the admin UI's preferred read-path behavior (no panel-breaking exceptions).

## Deviations from Plan

**None significant** — the plan executed exactly as written, with two minor adaptations worth documenting (both safely under Rule 3 — auto-fix blocking issues):

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added GameKit.Presence ProjectReference to samples/TicTacToeDuel/TicTacToeDuel.csproj**
- **Found during:** Task 3 (sample wiring)
- **Issue:** The sample csproj never had a `GameKit.Presence` ProjectReference (the Presence package was a Phase-1 stub through Plan 06-03). Adding `gameKitBuilder.AddPresence();` to `Program.cs` would not compile without the reference.
- **Fix:** Added `<ProjectReference Include="..\..\src\GameKit.Presence\GameKit.Presence.csproj" />` to the existing ItemGroup with an explanatory comment.
- **Files modified:** `samples/TicTacToeDuel/TicTacToeDuel.csproj`
- **Verification:** `dotnet build samples/TicTacToeDuel/TicTacToeDuel.csproj` succeeds (0 warnings, 0 errors).
- **Committed in:** `24c214e` (Task 3 commit)

**2. [Rule 3 - Blocking] Reverted the `AuthRuntimeQueryCustomizer`-style DbContext swap in PresenceTestApp**
- **Found during:** Task 3 (integration test build)
- **Issue:** Initial PresenceTestApp draft copied the `services.AddDbContext(...ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>())` block from `AuthTestHost.cs`. The customizer class is defined locally inside `tests/GameKit.Auth.Integration.Tests` (internal sealed) — not visible to other test assemblies.
- **Analysis:** The customizer is needed for tests that query Auth entities (PlayerIdentity / RefreshToken) at runtime. The heartbeat path does NOT query Auth at runtime — JWT validation uses the public key from disk; the bearer scheme does not hit the DB. So the customizer is not required.
- **Fix:** Removed the `services.AddDbContext` swap entirely; `AddGameKit(...)`'s default DbContext registration is sufficient.
- **Files modified:** `tests/GameKit.Presence.Integration.Tests/PresenceTestApp.cs`
- **Verification:** Integration tests all 5 green against Testcontainers Postgres + Redis.
- **Committed in:** `24c214e` (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 3 — blocking compile/runtime issues).
**Impact on plan:** None — both fixes were natural consequences of the sample being a stub-consumer and the integration-test host being lighter than Auth's. No scope creep, no behavior change vs. the plan's intent.

## Issues Encountered

None. All three tasks completed on the first build/test pass after the two minor blocking fixes documented above.

## User Setup Required

None — no external service configuration required. The heartbeat endpoint runs against the operator's existing Redis instance (same `IConnectionMultiplexer` Singleton the sample registers in `Program.cs`).

## Next Phase Readiness

**Plan 06-04 delivers half of PRES-05.** The other half (the `/api/sessions/{id}/start` + `/abandon` endpoints that DRIVE the in-match transition via the observer registered here) lives in **Plan 06-05** — the writer-side stops at the observer being REGISTERED; Plan 06-05 wires the callers.

**Plan 06-07 (Admin Presence panel)** can now resolve `IPresenceProvider` from DI and call `GetStatusAsync` + `GetOnlinePlayerIdsAsync(take: 25)` for the top-N panel. The defensive Offline fallback in `GetStatusAsync` keeps the panel robust against key-shape drift.

**Plan 06-09 (`dotnet new gamekit` template)** can carry the `.AddPresence()` + `app.MapPresence()` calls into the template's `Program.cs` verbatim. The `--skip-presence` flag (D-12) will conditionally exclude these two lines + the `<ProjectReference>` to `GameKit.Presence`.

## Self-Check: PASSED

**Files verified:**
- FOUND: src/GameKit.Presence/Configuration/GameKitPresenceOptions.cs
- FOUND: src/GameKit.Presence/Configuration/PresenceOptionsValidator.cs
- FOUND: src/GameKit.Presence/PresenceRedisKeys.cs
- FOUND: src/GameKit.Presence/PresenceValues.cs
- FOUND: src/GameKit.Presence/Services/IPresenceWriter.cs
- FOUND: src/GameKit.Presence/Services/RedisPresenceProvider.cs
- FOUND: src/GameKit.Presence/Services/PresenceSessionObserver.cs
- FOUND: src/GameKit.Presence/Http/PresenceEndpoints.cs
- FOUND: src/GameKit.Presence/Builder/PresenceBuilderExtensions.cs
- FOUND: src/GameKit.Presence/Builder/PresenceBuilderExtensions.Options.cs
- FOUND: src/GameKit.Presence/Builder/PresenceApplicationBuilderExtensions.cs
- FOUND: tests/GameKit.Presence.Tests/PresenceOptionsValidatorTests.cs
- FOUND: tests/GameKit.Presence.Tests/RedisPresenceProviderTests.cs
- FOUND: tests/GameKit.Presence.Integration.Tests/PresenceTestApp.cs
- FOUND: tests/GameKit.Presence.Integration.Tests/HeartbeatEndpointTests.cs
- FOUND: tests/GameKit.Presence.Integration.Tests/InMatchPrecedenceTests.cs

**Commits verified:**
- FOUND: ab87bf6 (Task 1)
- FOUND: e0373a1 (Task 2)
- FOUND: 24c214e (Task 3)

---
*Phase: 06-presence-openapi-distribution*
*Completed: 2026-05-26*
