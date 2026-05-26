# Phase 6: Presence + OpenAPI + Distribution — Pattern Map

**Mapped:** 2026-05-25
**Files analyzed:** 47 new/modified files across `src/`, `samples/`, `templates/`, `tests/`, `docs/`, `Directory.Build.props`, `Directory.Packages.props`, `GameKit.targets`, `.planning/ROADMAP.md`.
**Analogs found:** 38/47 with exact or strong role-matches. 9 files (source generator, GameKit.targets, template package, dotnet new template content) have NO in-repo precedent — flagged in the "No Analog Found" section with concrete external code excerpts from RESEARCH.md.

---

## File Classification

### `GameKit.Presence` package (fills Phase-1 stub — Wave 1)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Presence/GameKit.Presence.csproj` (MODIFY) | csproj | n/a | `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` | role-match (no migrations) |
| `src/GameKit.Presence/Configuration/GameKitPresenceOptions.cs` | config | n/a | `src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs` | exact |
| `src/GameKit.Presence/Configuration/PresenceOptionsValidator.cs` | config / IValidateOptions | n/a | `src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs` | exact |
| `src/GameKit.Presence/PresenceRedisKeys.cs` | utility | n/a | `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` | exact |
| `src/GameKit.Presence/Services/IPresenceWriter.cs` | port (interface) | n/a | `src/GameKit.Core/Services/IPresenceProvider.cs` | exact |
| `src/GameKit.Presence/Services/RedisPresenceProvider.cs` | service | event-driven (Redis SETEX / GET / SCAN) | `src/GameKit.Matchmaking/Services/RedisMatchmakingObservability.cs` + `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` (constructor-injected `IConnectionMultiplexer`) | strong role-match |
| `src/GameKit.Presence/Services/PresenceSessionObserver.cs` (RESEARCH names it `PresenceSessionObserver`; CONTEXT bullets call it `PresenceLifecycleObserver` — use `PresenceSessionObserver` per RESEARCH naming) | service / observer | event-driven | `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` (implements `IPostSessionCompleteHandler`) | exact pattern |
| `src/GameKit.Presence/Http/PresenceEndpoints.cs` | controller / minimal-API | request-response | `src/GameKit.Matchmaking/Http/PartyEndpoints.cs` | exact |
| `src/GameKit.Presence/Builder/PresenceBuilderExtensions.cs` (partial base) | builder ext | n/a | `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` | exact |
| `src/GameKit.Presence/Builder/PresenceBuilderExtensions.Options.cs` (partial) | builder ext | n/a | `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` (partial-split shape) | exact |
| `src/GameKit.Presence/Builder/PresenceApplicationBuilderExtensions.cs` | builder ext / endpoint map | n/a | `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` | exact |

### `GameKit.Core` additions (Wave 1)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Core/Services/ISessionLifecycleObserver.cs` (NEW interface) | port | event-driven | `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` | exact |
| `src/GameKit.Core/Services/GameKitVersionMismatchException.cs` (NEW exception) | exception | n/a | `src/GameKit.Core/Services/PlayerNotFoundException.cs` (referenced; not read) — any Core exception type | role-match |
| `src/GameKit.Core/Services/ISessionStartService.cs` + `ISessionAbandonService.cs` (NEW services) | service interface | request-response | `src/GameKit.Core/Services/ISessionCompleteService.cs` | exact |
| `src/GameKit.Core/Services/SessionStartService.cs` + `SessionAbandonService.cs` (NEW services) | service | CRUD + transaction + observer fan-out | `src/GameKit.Core/Services/SessionCompleteService.cs` | exact |
| `src/GameKit.Core/Services/SessionCompleteService.cs` (MODIFY) | service | CRUD + transaction | self (existing) — add `IEnumerable<ISessionLifecycleObserver>` injection + invocation | self-modify |
| `src/GameKit.Core/Http/SessionEndpoints.cs` (MODIFY) | controller / minimal-API | request-response | self (existing) — extend `MapSessions` with `/{id}/start` and `/{id}/abandon` minimal-API routes mirroring the existing `/{id}/complete` registration | self-extend |
| `src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs` (NEW) | service / IHostedService | startup-once | `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` | exact lifecycle (StartAsync / StopAsync); body is reflection-based per RESEARCH Pattern 6 |
| `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` (MODIFY) | builder ext | n/a | self (existing) — register `GameKitVersionAssertionHostedService` AS THE FIRST `IHostedService` (CRITICAL: must be inserted at index 0, not appended — see D-16 + Pattern 6 below) | self-modify |

### `GameKit.OpenApi` package (NEW 7th package — Wave 2)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.OpenApi/GameKit.OpenApi.csproj` | csproj | n/a | `src/GameKit.Presence/GameKit.Presence.csproj` (skeleton — Core ProjectRef only) + ASP.NET Core FrameworkReference from `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` lines 30-31 | composite |
| `src/GameKit.OpenApi/AssemblyInfo.cs` | meta | n/a | `src/GameKit.Presence/AssemblyInfo.cs` (SPDX header only) | exact |
| `src/GameKit.OpenApi/Configuration/GameKitOpenApiOptions.cs` | config | n/a | `src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs` (much simpler — three fields: `DocumentName`, `Title`, `MountPath`) | role-match |
| `src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.cs` (partial base) | builder ext | n/a | `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` (shape) — body wraps `services.AddOpenApi(name, opts => …)` with inline `OpenApiOptions.ShouldInclude` lambda per D-19 | shape-match; OpenAPI surface novel |
| `src/GameKit.OpenApi/Builder/OpenApiApplicationBuilderExtensions.cs` (or inline `MapGameKitOpenApi` in the file above) | builder ext / endpoint map | n/a | `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` | exact |
| `src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs` | service / `IOpenApiDocumentTransformer` | transform | none in-repo (NEW component) — see RESEARCH Pattern 3 lines 527-559 for the `IAuthenticationSchemeProvider`-driven excerpt | external pattern |
| `src/GameKit.OpenApi/Transformers/GameKitInfoTransformer.cs` | service / `IOpenApiDocumentTransformer` | transform | none in-repo (NEW component) — body reads `GameKitMarker.GameKitVersion` and writes into `document.Info.Version` | external pattern |

**NOTE on D-19:** the admin-route filter is NOT a separate transformer class. It is an **inline `o.ShouldInclude` lambda** registered alongside the transformers inside `AddGameKitOpenApi`. Operation transformers cannot remove paths — they can only decorate (RESEARCH §Pitfall 4 + Anti-pattern note line 873). PATTERNS § "OpenAPI inline filter" excerpt below.

### Admin UI additions (Wave 2 frontend deliverable)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor` (NEW) | component (Blazor page) | request-response (10s polling) | `src/GameKit.Admin.UI/Components/Pages/Admins.razor` (table primitive + `MissingPackageAlert` callsite) + `src/GameKit.Admin.UI/Components/Pages/Dashboard.razor:148-176` (10s polling via `protected override async Task OnInitializedAsync` + sequential awaits + `Type.GetType` reflection for missing-package degrade) | composite exact |
| `src/GameKit.Admin.UI/Components/Layout/SideNav.razor` (MODIFY) | component | n/a | self (existing) — insert `<NavLink href="/admin/presence">Presence</NavLink>` between the `Health` row (line 31-33) and the `Queue depth` row (line 34-36) per UI-SPEC §7 | self-extend |
| `src/GameKit.Admin.UI/Components/Shared/StatusChip.razor` (MODIFY) | component | n/a | self (existing) — extend the `ChipModifierClass` switch (line 32-38) with two new arms: `"inmatch" or "in match" or "in-match" => "in-match"` and `"offline" => "offline"`. The existing `"online" => "healthy"` arm is UNCHANGED. | self-extend |
| `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css` (APPEND) | CSS | n/a | self (existing) — append two new chip-modifier rules `.chip.in-match` (amber tokens) + `.chip.offline` (neutral tokens) per UI-SPEC §5. Total delta ~10 lines (< 300 B). | self-extend |
| `src/GameKit.Admin.UI/Components/Shared/MissingPackageAlert.razor` (UNCHANGED) | component | n/a | self — DO NOT MODIFY. UI-SPEC §9 contract: only a new CALLSITE inside `PresencePanel.razor` with `PackageName="Presence" Feature="presence telemetry"`. The existing template (line 17-22) emits both required substrings naturally. | callsite-add only |

### `GameKit.Build` source generator (NEW — Wave 3)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Build/GameKit.Build.csproj` | csproj (Roslyn analyzer) | n/a | NONE in-repo (no other source generator exists). Externally-templated by RESEARCH §Standard Stack + Anti-pattern §"Putting the source generator's csproj in `Directory.Packages.props` CPM" + §Pitfall 1 | NEW |
| `src/GameKit.Build/GameKitVersionGenerator.cs` | service / `IIncrementalGenerator` | transform (compile-time) | NONE in-repo. See RESEARCH Pattern 5 lines 624-661 — `[Generator]` attribute, `IIncrementalGenerator.Initialize`, `AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.Version", out var v)`, `RegisterSourceOutput`, `spc.AddSource("GameKitMarker.g.cs", src)`. | external pattern |
| `src/GameKit.Build/AssemblyInfo.cs` + `README.md` | meta | n/a | `src/GameKit.Presence/AssemblyInfo.cs` | exact (just SPDX header) |

### MSBuild + repo-root infrastructure (Wave 3)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Directory.Build.props` (MODIFY) | config | n/a | self (existing) — add `<CompilerVisibleProperty Include="Version" />` to a NEW `<ItemGroup>` (D-23) + `<Import Project="GameKit.targets" />` (D-17). See excerpt below. | self-extend |
| `Directory.Packages.props` (MODIFY) | config | n/a | self (existing) — add ONE new `<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.8" />` line (RESEARCH §Standard Stack — NEW pin required, NOT in shared framework). | self-extend |
| `GameKit.targets` (NEW at repo root) | config / MSBuild target | n/a | NONE in-repo. RESEARCH Pattern 7-alt lines 759-771 — `<Project><ItemDefinitionGroup><ProjectReference><PackageVersion>[$(Version)]</PackageVersion></ProjectReference></ItemDefinitionGroup></Project>` (exact-pin via metadata, defense-in-depth + CI grep guard per D-26). | external pattern |

### Template + SampleGame topology (Wave 4)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `templates/GameKit.Templates/GameKit.Templates.csproj` | csproj (NuGet template package) | n/a | NONE in-repo. RESEARCH Pattern 8 lines 845-863 — `<PackageType>Template</PackageType>`, `<IncludeContentInPack>true</IncludeContentInPack>`, `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<NoDefaultExcludes>true</NoDefaultExcludes>` (keeps `.template.config`). | external pattern |
| `templates/GameKit.Templates/content/GameKit.SampleGame/.template.config/template.json` | config | n/a | NONE in-repo. RESEARCH Pattern 8 lines 797-823 — symbols for `-n` + four `--skip-*` parameters + `postActions` calling `./scripts/gen-test-rsa-pem.sh`. | external pattern |
| `templates/GameKit.Templates/content/GameKit.SampleGame/**` (verbatim file clone) | content | n/a | `samples/TicTacToeDuel/**` (every file gets `${ProjectName}` substitution where `TicTacToeDuel` appears) — files to clone: `Program.cs`, `*.csproj`, `appsettings.json`, `appsettings.Development.json`, `README.md`, `wwwroot/index.html`, `wwwroot/matchmaking.html`, `Http/DemoEndpoints.cs`, `Http/DemoContracts.cs`, `Game/TicTacToeBoard.cs`, `Game/TicTacToeBoardSerializer.cs`, `Properties/launchSettings.json` | exact mirror with name subst |
| `samples/TicTacToeDuel/Program.cs` (MODIFY) | controller / composition root | request-response | self (existing) — add `.AddPresence()` to the fluent chain after `.AddMatchmaking()` (line 101-114), and `app.MapPresence()` to the endpoint mapping after `app.MapMatchmaking()` (line 151) | self-extend |
| `samples/TicTacToeDuel.GameServer/TicTacToeDuel.GameServer.csproj` (NEW console) | csproj | n/a | NONE in-repo for "Console app with Npgsql + HttpClient" — closest is `src/GameKit.Cli/GameKit.Cli.csproj` (referenced; not opened — Spectre.Console.Cli console pattern). | partial-match |
| `samples/TicTacToeDuel.GameServer/Program.cs` (NEW) | controller / console entry | request-response (Npgsql query + HttpClient POST) | `src/GameKit.Cli/Program.cs` (referenced; not opened). Topology: connect via `gamekit_reader` (read ladders/players via Npgsql) + HTTP-call `POST /api/sessions/{id}/start` + `POST /api/sessions/{id}/complete` against the web app | partial-match |
| `samples/TicTacToeDuel.GameServer/appsettings.json` (NEW) | config | n/a | `samples/TicTacToeDuel/appsettings.json` (clone — change `ConnectionStrings:GameKit` Username/Password to `gamekit_reader` / `gamekit_reader_dev` per CONTEXT §specifics line 147) | partial-match |
| `samples/TicTacToeDuel.GameServer/README.md` (NEW) | docs | n/a | `samples/TicTacToeDuel/README.md` (referenced; not opened — structural style) | partial-match |
| `samples/TicTacToeDuel/scripts/run-game-server.sh` (NEW) | script | n/a | `samples/TicTacToeDuel/scripts/run-sample.sh` (referenced in CONTEXT line 84; not opened) — same shape but using `gamekit_reader` credentials | partial-match |

### Test infrastructure (NEW projects — Wave 0 + Wave 4)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `tests/GameKit.Presence.Tests/GameKit.Presence.Tests.csproj` (NEW) | test csproj | n/a | `tests/GameKit.Matchmaking.Tests/` (referenced; not opened — same shape as `Rankings.Tests`) — xUnit + Moq + EF InMemory | role-match |
| `tests/GameKit.Presence.Integration.Tests/GameKit.Presence.Integration.Tests.csproj` (NEW) | test csproj | n/a | `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` | exact |
| `tests/GameKit.Presence.Integration.Tests/CollectionDefinitions.cs` (NEW) | test fixture | n/a | `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` | exact |
| `tests/GameKit.Presence.Integration.Tests/Fixtures/PresenceIntegrationFixture.cs` (NEW) | test fixture | n/a | `tests/GameKit.Matchmaking.Integration.Tests/Fixtures/MatchmakingIntegrationFixture.cs` | exact |
| `tests/GameKit.OpenApi.Integration.Tests/GameKit.OpenApi.Integration.Tests.csproj` (NEW) | test csproj | n/a | `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` (referenced; not opened) — needs `Microsoft.AspNetCore.Mvc.Testing` + WebApplicationFactory pattern | role-match |
| `tests/GameKit.OpenApi.Integration.Tests/OpenApiCoverageTests.cs` (NEW) | test class | request-response | RESEARCH Pattern 4 lines 571-613 — `EndpointDataSource` enumeration via `WebApplicationFactory<Program>` + Pitfall §9 filter list (`/admin`, `/openapi`, `/_blazor`) | external pattern + `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` for the WebApplicationFactory pattern |
| `tests/GameKit.OpenApi.Integration.Tests/OpenApiBearerSchemeTests.cs` + `OpenApiAdminRouteExclusionTests.cs` (NEW) | test class | request-response | same Pattern 4 frame; just different assertions over `JsonDocument.Parse(json).RootElement` | external pattern |
| `tests/GameKit.Distribution.Integration.Tests/GameKit.Distribution.Integration.Tests.csproj` (NEW) | test csproj | n/a | `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` (mirrors the per-package CollectionDefinitions + Fixture shape) | exact |
| `tests/GameKit.Distribution.Integration.Tests/CollectionDefinitions.cs` (NEW) | test fixture | n/a | `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` | exact |
| `tests/GameKit.Distribution.Integration.Tests/DistributionIntegrationFixture.cs` (NEW) | test fixture | n/a | `tests/GameKit.TestFixtures/PostgresFixture.cs` lines 36-53 (the 3-role `WithBindMount(initDir, "/docker-entrypoint-initdb.d")` pattern is ALREADY in PostgresFixture — Phase 6 reuses it verbatim; DistributionIntegrationFixture is a thin composite over Postgres + Redis) | exact |
| `tests/GameKit.Distribution.Integration.Tests/DIST02_GamekitReaderInsertDeniedTests.cs` (NEW) | test class | CRUD (deny) | `tests/GameKit.TestFixtures/PostgresFixture.cs` (`ReaderConnectionString` is already exposed at line 53; open a second Npgsql conn as reader, attempt INSERT, assert `42501`) — no closer in-repo analog | partial-match |
| `tests/GameKit.Distribution.Integration.Tests/DIST03_TemplateSampleGameSmokeTests.cs` (NEW) | test class | request-response (cross-process) | NONE in-repo. RESEARCH OQ4 — `Process.Start` on the built template-output exe + `WebApplicationFactory` for the web app | external pattern |
| `tests/GameKit.Distribution.Integration.Tests/OPS04_VersionStampedAcrossPackagesTests.cs` (NEW) | test class | reflection | NONE in-repo. RESEARCH Pattern 6 (mirror the assertion logic) — load referenced GameKit.* assemblies, reflect `Internal.GameKitMarker.GameKitVersion`, assert all 7 report the same value | external pattern |
| `tests/GameKit.Distribution.Integration.Tests/OPS05_VersionMismatchAssertionThrowsTests.cs` (NEW) | test class | reflection + IHost.StartAsync | RESEARCH Pitfall 3 — synthetic test that registers two assemblies with diverging `GameKitVersion` const values, calls `IHost.StartAsync`, asserts `GameKitVersionMismatchException` thrown | external pattern |
| `tests/GameKit.Distribution.Integration.Tests/OPS06_CleanInstallMigrationTests.cs` (NEW) | test class | CRUD | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingMigrationDeterminismTests.cs` (referenced; not opened — same shape: spin up empty Postgres, run AddGameKit().AddAuth().AddRankings().AddMatchmaking().AddPresence(), assert all migration history tables created) | role-match |
| `tests/GameKit.Distribution.Integration.Tests/DIST04_TemplatePackageShapeTests.cs` (NEW) | test class | file-I/O | NONE in-repo. `dotnet pack templates/GameKit.Templates/` → `unzip -l` the `.nupkg` → assert it contains `content/GameKit.SampleGame/Program.cs` + `.template.config/template.json` | external pattern |
| `tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs` (NEW or extend existing `PanelRenderTests.cs`) | test class | request-response | `tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs` (referenced; not opened — already asserts the `Install GameKit.Matchmaking` substring per UI-SPEC §9). Phase 6 adds a parallel test asserting `Install GameKit.Presence` + `AddPresence(…)` | exact pattern |

### Production-ops docs (NEW — Wave 4)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `docs/ops/README.md` (NEW) | docs (index) | n/a | NONE in-repo (no multi-page docs dir today). Closest structural model: `samples/TicTacToeDuel/README.md` (referenced; not opened — Markdown prose style) | partial |
| `docs/ops/bare-metal.md`, `container.md`, `air-gapped.md`, `postgres-roles.md`, `redis-aof.md`, `jwt-keys.md`, `disaster-recovery.md`, `migrations-runbook.md` (8 NEW) | docs | n/a | same — Markdown prose; cite `docker/postgres/init/01-roles.sql` (3-role layout) + `Directory.Build.props` MinVer settings + `samples/TicTacToeDuel/keys/README.md` (referenced; not opened — JWT key bootstrap) | partial |

### ROADMAP correction (Wave 0)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `.planning/ROADMAP.md` (MODIFY — 2 one-line edits) | docs | n/a | self — SC#1 wording fix (CONTEXT `<specifics>` §ROADMAP TYPO lines 135-144) + SC#5 "all 6 packages" → "all 7 packages" (CONTEXT D-22, verified at line 233 of current ROADMAP) | self-modify |

---

## Pattern Assignments

### Block 1 — Per-package options + IValidateOptions (Presence)

**`src/GameKit.Presence/Configuration/GameKitPresenceOptions.cs`** (config, IOptions-bound POCO)

**Analog:** `src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs`

**Pattern to copy** (`GameKitMatchmakingOptions.cs:16-50`):
```csharp
public sealed class GameKitMatchmakingOptions
{
    public GameKitMatchmakingTickerOptions Ticker { get; set; } = new();
    // ...
    public int AcceptTimeoutSeconds { get; set; } = 10;
    // ...
}
```

For Presence the surface is much smaller (only `TtlSeconds` + `HeartbeatIntervalSeconds` per CONTEXT D-01). No nested option classes needed for v1. Body:
```csharp
public sealed class GameKitPresenceOptions
{
    public int TtlSeconds { get; set; } = 30;                  // D-01
    public int HeartbeatIntervalSeconds { get; set; } = 10;    // D-01 (3× safety factor)
}
```

---

**`src/GameKit.Presence/Configuration/PresenceOptionsValidator.cs`** (IValidateOptions)

**Analog:** `src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs:22-110`

**Pattern to copy** (lines 22-30 + lines 39-50 — pure-function `Validate` overload for unit testability):
```csharp
public sealed class MatchmakingOptionsValidator : IValidateOptions<GameKitMatchmakingOptions>
{
    public ValidateOptionsResult Validate(string? name, GameKitMatchmakingOptions options)
        => Validate(options, out var failures)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);

    public static bool Validate(GameKitMatchmakingOptions options, out IReadOnlyList<string> failures)
    {
        var problems = new List<string>();
        if (options.AcceptTimeoutSeconds < 1)
            problems.Add($"{nameof(GameKitMatchmakingOptions.AcceptTimeoutSeconds)} must be >= 1 second (got {options.AcceptTimeoutSeconds}).");
        // ...
        failures = problems;
        return problems.Count == 0;
    }
}
```

Presence invariants: `TtlSeconds >= 1`, `HeartbeatIntervalSeconds >= 1`, `HeartbeatIntervalSeconds * 3 <= TtlSeconds` (safety-factor check — CONTEXT D-01 expects 3× headroom).

---

### Block 2 — Redis key formatter + Redis-backed service

**`src/GameKit.Presence/PresenceRedisKeys.cs`** (utility — string constants + formatters)

**Analog:** `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs`

**Pattern to copy** (lines 33-93 — `public static class` with `public const string` + `public static string Key(Guid id) => $"prefix:{id}"`):
```csharp
public static class MatchmakingRedisKeys
{
    public const string MatcherLock = "gamekit:matchmaking:matcher:lock";
    public static string Ticket(Guid ticketId) => $"mm:ticket:{ticketId}";
    // ...
}
```

For Presence: `public const string PrefixOnline = "online"; public const string PrefixInMatch = "in_match"; public static string Player(Guid playerId) => $"presence:{playerId}";` (CONTEXT D-04 — single key per player).

---

**`src/GameKit.Presence/Services/RedisPresenceProvider.cs`** (service implementing both `IPresenceProvider` from Core AND `IPresenceWriter` from this package)

**Analog:** `src/GameKit.Matchmaking/Services/RedisMatchmakingObservability.cs` + `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` (both already use constructor-injected `IConnectionMultiplexer` + `IClock` + `IOptions<…>`)

**Constructor-injection pattern** (mirrors `MatchmakingService.cs:62-80`):
```csharp
public sealed class MatchmakingService : IMatchmakingService
{
    private readonly GameKitDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    // ...
    public MatchmakingService(
        GameKitDbContext db,
        IConnectionMultiplexer redis,
        // ... 7 other deps
        ILogger<MatchmakingService>? logger)
    { /* assign */ }
}
```

For Presence, dependencies are smaller: `IConnectionMultiplexer redis`, `IOptions<GameKitPresenceOptions> opts`, `IClock clock` (for relative-time computation if surfaced).

**Lua-script precedence rule** (CRITICAL — see RESEARCH Pattern 1 §"CRITICAL precedence rule" lines 384-402):

Heartbeat MUST NOT downgrade `in_match` to `online`. Use Lua atomic compare-and-set rather than `StringSetAsync`. Verbatim excerpt from RESEARCH Pattern 1:
```csharp
const string lua = """
  local v = redis.call('GET', KEYS[1])
  if v == 'in_match' then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
  else
    redis.call('SET', KEYS[1], 'online', 'PX', ARGV[1])
  end
  return 1
""";
return new(_redis.GetDatabase().ScriptEvaluateAsync(
    lua, new RedisKey[] { key }, new RedisValue[] { (long)ttl.TotalMilliseconds }));
```

`GetOnlinePlayerIdsAsync` MUST use `KeysAsync` (SCAN-based) not `Keys` (synchronous KEYS — RESEARCH anti-pattern line 872). Existing in-repo precedent: search Matchmaking for `KeysAsync` if needed; the Lease helpers use Redis primitives consistently.

---

### Block 3 — Core port + observer adapter

**`src/GameKit.Core/Services/ISessionLifecycleObserver.cs`** (NEW port)

**Analog:** `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` (full file, 64 lines — copy structure verbatim)

**Excerpt to mirror** (`IPostSessionCompleteHandler.cs:11-47`):
```csharp
/// <remarks>
/// <para>This port is OPTIONAL. If no implementation is registered in DI, …</para>
/// <para>Implementations run inside the same ambient transaction …</para>
/// <para>Implementations MUST be idempotent — the contract does not guarantee exactly-once delivery …</para>
/// </remarks>
public interface IPostSessionCompleteHandler
{
    Task OnCompletedAsync(
        Guid sessionId,
        IReadOnlyList<SessionParticipantSnapshot> participants,
        CancellationToken ct);
}
```

For `ISessionLifecycleObserver`, three methods — see RESEARCH Pattern 2 lines 439-450:
```csharp
public interface ISessionLifecycleObserver
{
    Task OnSessionStartedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
    Task OnSessionCompletedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
    Task OnSessionAbandonedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
}
```

Optional registration semantics + idempotency XML doc copied verbatim from `IPostSessionCompleteHandler` lines 17-30.

---

**`src/GameKit.Presence/Services/PresenceSessionObserver.cs`** (implements `ISessionLifecycleObserver`)

**Analog:** `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs:45-127` (sealed class implementing the matching Core port; runs inside ambient transaction; constructor-injects scoped DI services)

**Excerpt to mirror** (`PendingRatingUpdatesAdapter.cs:45-72`):
```csharp
public sealed class PendingRatingUpdatesAdapter : IPostSessionCompleteHandler
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public PendingRatingUpdatesAdapter(
        GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    { _ctx = ctx; _clock = clock; _ids = ids; }

    public async Task OnCompletedAsync(
        Guid sessionId,
        IReadOnlyList<SessionParticipantSnapshot> participants,
        CancellationToken ct)
    {
        // … no ITransaction work — caller owns it …
        foreach (var participant in participants)
        {
            // …
            await _ctx.SaveChangesAsync(ct);   // shares caller's tx
        }
    }
}
```

For `PresenceSessionObserver`, dependencies are smaller (just `IPresenceWriter writer`) — no DbContext (presence is Redis-only). Three methods per RESEARCH Pattern 2 lines 453-467:
```csharp
internal sealed class PresenceSessionObserver(IPresenceWriter writer) : ISessionLifecycleObserver
{
    public async Task OnSessionStartedAsync(Guid id, IReadOnlyList<Guid> ps, CancellationToken ct)
    {
        foreach (var p in ps) await writer.WriteInMatchAsync(p, ct);
    }
    // OnSessionCompletedAsync → writer.WriteOnlineAsync (refresh TTL with "online")
    // OnSessionAbandonedAsync  → writer.ClearInMatchAsync (SET "online" w/ existing TTL OR DEL — Plan-time call)
}
```

---

### Block 4 — Endpoints (Presence heartbeat + new Sessions /start + /abandon)

**`src/GameKit.Presence/Http/PresenceEndpoints.cs`** (controller / minimal-API)

**Analog:** `src/GameKit.Matchmaking/Http/PartyEndpoints.cs:28-115`

**Pattern to copy — endpoint mapping** (`PartyEndpoints.cs:41-61`):
```csharp
public static IEndpointRouteBuilder MapPartyEndpoints(this IEndpointRouteBuilder routes)
{
    ArgumentNullException.ThrowIfNull(routes);

    routes.MapPost("/api/parties", CreatePartyAsync)
        .RequireAuthorization()
        .AddEndpointFilter<ValidationEndpointFilter<CreatePartyRequest>>();
    // ...
    return routes;
}
```

For Presence — single `POST /api/presence/heartbeat`, JWT-required (D-02), no rate limit (D-05), empty body (Claude's Discretion default):
```csharp
routes.MapPost("/api/presence/heartbeat", HeartbeatAsync)
    .RequireAuthorization();   // default JWT-Bearer scheme from Phase 2
```

**Pattern to copy — handler + player-id extraction** (`PartyEndpoints.cs:65-85`):
```csharp
private static async Task<IResult> CreatePartyAsync(
    CreatePartyRequest _, HttpContext http, IPartyService svc, GameKitDbContext db, CancellationToken ct)
{
    if (!TryGetPlayerId(http, out var playerId))
        return Results.Forbid();
    // …
}
```

For Heartbeat: extract `playerId` from `ClaimTypes.NameIdentifier` (or `sub`), call `IPresenceWriter.WriteHeartbeatAsync(playerId, ct)`, return `Results.NoContent()` (204). Pattern matches existing JWT-handler extraction in `RankingsPlayerEndpoints.cs:53-56`:
```csharp
var subClaim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? http.User.FindFirst("sub")?.Value;
if (subClaim is null || !Guid.TryParse(subClaim, out var subId)) return Results.Forbid();
```

---

**`src/GameKit.Core/Http/SessionEndpoints.cs`** (MODIFY — extend `MapSessions` with `/{id}/start` and `/{id}/abandon`)

**Analog:** Self (existing) — `SessionEndpoints.cs:29-53` already shows the exact pattern for `/{id}/complete`:
```csharp
public static RouteGroupBuilder MapSessions(this IEndpointRouteBuilder routes, IGameKitRateLimitPolicies policies)
{
    var group = routes.MapGroup("/api/sessions").WithTags("GameKit.Core");

    group.MapPost("/{id}/complete", CompleteSessionAsync)
        .AddEndpointFilter<IdempotencyKeyEndpointFilter>()
        .AddEndpointFilter<ValidationEndpointFilter<SessionCompleteRequest>>()
        .RequireRateLimiting(policies.SessionsComplete)
        .RequireAuthorization("RequiresServiceToken");

    return group;
}
```

Phase 6 ADDS two parallel `group.MapPost(...)` calls for `/{id}/start` and `/{id}/abandon`, also requiring `"RequiresServiceToken"` (service-account JWT — game-server-authoritative per D-03). Handlers mirror `CompleteSessionAsync` (`SessionEndpoints.cs:55-113`) — they call new services `ISessionStartService` + `ISessionAbandonService`. The handler `switch` on result enum follows the same pattern as `CompleteSessionAsync` lines 68-112 (Completed / SessionNotFound / InvalidState / etc. → Results.Ok/NotFound/Conflict).

**Critical:** the new endpoints MUST fire `ISessionLifecycleObserver` inside the existing transaction (D-21). Mirror the pattern in `SessionCompleteService.cs:278-282`:
```csharp
if (_postCompleteHandler is not null)
{
    await _postCompleteHandler.OnCompletedAsync(sessionId, participantSnapshots, ct);
}
```

For the new SessionStartService / SessionAbandonService, replace the single nullable handler with `IEnumerable<ISessionLifecycleObserver> observers`:
```csharp
foreach (var obs in _observers)
    await obs.OnSessionStartedAsync(sessionId, participantIds, ct);
```

Also MODIFY `SessionCompleteService.cs` constructor to inject `IEnumerable<ISessionLifecycleObserver>` and fire `OnSessionCompletedAsync` after the existing `OnCompletedAsync` callback (keeping `IPostSessionCompleteHandler` for backwards-compat — D-21).

---

### Block 5 — Builder partial-split (Presence + OpenApi)

**`src/GameKit.Presence/Builder/PresenceBuilderExtensions.cs`** (partial-class base, `AddPresence`)

**Analog:** `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs:20-133`

**Pattern to copy** — `public static partial class` decoration + ValidateOnStart options pattern + builder return:
```csharp
public static partial class MatchmakingBuilderExtensions  // line 20
{
    public static IGameKitMatchmakingBuilder AddMatchmaking(
        this IGameKitBuilder builder, Action<GameKitMatchmakingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Bind + validate options
        var optsBuilder = builder.Services.AddOptions<GameKitMatchmakingOptions>();
        if (configure is not null) optsBuilder.Configure(configure);
        optsBuilder.ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GameKitMatchmakingOptions>, MatchmakingOptionsValidator>());

        // 2. Model extension via TryAddEnumerable (Presence SKIPS this — Redis-only, no entities)
        // 3. Migration runner (Presence SKIPS this — no migrations)

        // 4. Builder registration
        // 5..7. Pulled-in partial-class registrations (.Strategy, .Accept, .Background, .Ticker, .Http)
        builder.Services.AddStrategyServices();  // <- partial-class method (defined in MatchmakingBuilderExtensions.Strategy.cs)

        return matchmakingBuilder;
    }
}
```

For Presence — body is much simpler (no model extension, no migration, no extra service waves). Single `AddPresence(IGameKitBuilder, Action<GameKitPresenceOptions>?)` that registers:
- Options + validator (steps 1)
- `services.AddSingleton<IPresenceProvider, RedisPresenceProvider>()` + `services.AddSingleton<IPresenceWriter, RedisPresenceProvider>()` (same instance — implements both)
- `services.AddScoped<ISessionLifecycleObserver, PresenceSessionObserver>()` (D-21 — registered via `TryAddEnumerable` so multiple observers can coexist if a v2 package adds a second one)

Return `IGameKitBuilder` for chaining (no `.AddLadder()`-style follow-on needed — Presence has no ladder concept).

---

**`src/GameKit.Presence/Builder/PresenceApplicationBuilderExtensions.cs`** (`UsePresence` + `MapPresence`)

**Analog:** `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` (full file, 60 lines)

**Pattern to copy** (`MatchmakingApplicationBuilderExtensions.cs:15-58`):
```csharp
public static class MatchmakingApplicationBuilderExtensions
{
    public static IApplicationBuilder UseGameKitMatchmaking(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }

    public static IEndpointRouteBuilder MapMatchmaking(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes.MapPartyEndpoints();
        routes.MapMatchmakingEndpoints();
        return routes;
    }
}
```

Body: `UsePresence` is a no-op for v1; `MapPresence` calls `routes.MapPresenceEndpoints()` (the extension method from `Http/PresenceEndpoints.cs`).

---

### Block 6 — Hosted service for version assertion

**`src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs`** (NEW)

**Analog (lifecycle shape):** `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-66`

**Pattern to copy — lifecycle skeleton:**
```csharp
internal sealed class AuthMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<AuthMigrationHostedService> _logger;

    public AuthMigrationHostedService(GameKitOptions opts, ILogger<AuthMigrationHostedService> log)
    { _gameKitOpts = opts; _logger = log; }

    public async Task StartAsync(CancellationToken ct) { /* body */ }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Body (NEW logic — no in-repo analog):** see RESEARCH Pattern 6 lines 692-723 verbatim. Iterate `AppDomain.CurrentDomain.GetAssemblies()` filtered to `GameKit.*` (skip `GameKit.Build`), reflect on `{AssemblyName}.Internal.GameKitMarker.GameKitVersion`, throw `GameKitVersionMismatchException(versionsByAsm)` on divergence.

**CRITICAL (D-24 + Pitfall 3 lines 922-926):** Pre-step — eager-load referenced GameKit.* assemblies BEFORE iterating. Otherwise lazy-loaded packages are silently missed:
```csharp
Assembly.GetEntryAssembly()!
    .GetReferencedAssemblies()
    .Where(n => n.Name?.StartsWith("GameKit.", StringComparison.Ordinal) == true)
    .Select(Assembly.Load)
    .ToList();
```

**Registration order (D-16 + CONTEXT bullets):** in `AddGameKit()` (`GameKitServiceCollectionExtensions.cs:26-87`), this hosted service must be inserted AT INDEX 0 of `services` so it runs BEFORE every migration hosted service. The existing `AddGameKit()` body does NOT register any `AddHostedService` calls today (line 41-86), so a simple `services.Insert(0, ServiceDescriptor.Singleton<IHostedService, GameKitVersionAssertionHostedService>())` at the top of the body is correct. Mirror the `services.AddSingleton(opts)` pattern at line 41.

---

### Block 7 — Source generator (NEW — NO in-repo analog)

**`src/GameKit.Build/GameKit.Build.csproj`** (Roslyn analyzer csproj)

**No in-repo analog.** Verbatim shape per RESEARCH §Standard Stack lines 117-121 + anti-pattern §"Targeting `net10.0` for the Roslyn source generator project" lines 873-874:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>  <!-- MANDATORY for source generators -->
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>  <!-- D-25 / OQ3 -->
    <IncludeBuildOutput>false</IncludeBuildOutput>     <!-- NEVER ship as NuGet -->
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>  <!-- exempt CS1591 -->
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.13.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**`src/GameKit.Build/GameKitVersionGenerator.cs`** (NEW — `IIncrementalGenerator`)

**No in-repo analog.** Verbatim per RESEARCH Pattern 5 lines 624-661:

```csharp
[Generator]
public sealed class GameKitVersionGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var version = context.AnalyzerConfigOptionsProvider.Select((p, _) =>
            p.GlobalOptions.TryGetValue("build_property.Version", out var v) ? v : "0.0.0");

        var asmName = context.CompilationProvider.Select((c, _) => c.AssemblyName ?? "Unknown");

        var combined = version.Combine(asmName);
        context.RegisterSourceOutput(combined, (spc, tuple) =>
        {
            var (ver, name) = tuple;
            if (!name.StartsWith("GameKit.", StringComparison.Ordinal)) return;
            var ns = $"{name}.Internal";
            var src = $$"""
                // <auto-generated/>
                // Emitted by GameKit.Build source generator.
                namespace {{ns}};

                internal static partial class GameKitMarker
                {
                    public const string GameKitVersion = "{{ver}}";
                    public const string AssemblyName   = "{{name}}";
                }
                """;
            spc.AddSource("GameKitMarker.g.cs", src);
        });
    }
}
```

**Consumer wiring** (every `src/GameKit.*/*.csproj` except `GameKit.Build` itself) — add this `ItemGroup` per RESEARCH lines 665-671:
```xml
<ItemGroup>
  <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

### Block 8 — MSBuild + Directory.Build.props

**`Directory.Build.props`** (MODIFY)

**Self-analog** (existing file, 42 lines). Two additive edits:

1. **CompilerVisibleProperty for source-gen (D-23 + Pitfall 1).** Add a NEW `<ItemGroup>` next to the existing one at line 37:
```xml
<ItemGroup>
  <!-- Expose MinVer's $(Version) to the GameKit.Build source generator at compile time.
       Without this, AnalyzerConfigOptionsProvider.GlobalOptions cannot read build_property.Version
       and the generator emits the fallback "0.0.0" stamp. -->
  <CompilerVisibleProperty Include="Version" />
</ItemGroup>
```

2. **Import GameKit.targets (D-17).** Add at the end of the `<Project>` element (after the `</ItemGroup>`):
```xml
<Import Project="$(MSBuildThisFileDirectory)GameKit.targets" />
```

---

**`GameKit.targets`** (NEW at repo root)

**No in-repo analog.** Pattern 7-alt from RESEARCH lines 759-771. Static `<ItemDefinitionGroup>` metadata that flows into the generated `.nuspec` at Pack time. Primary defense; CI grep is the secondary defense (D-26):
```xml
<Project>
  <ItemDefinitionGroup>
    <ProjectReference>
      <!-- Read by GenerateNuspec when ProjectReference is converted to PackageReference.
           Exact-pin syntax [X.Y.Z] blocks any other version from satisfying the dependency. -->
      <PackageVersion>[$(Version)]</PackageVersion>
    </ProjectReference>
  </ItemDefinitionGroup>
</Project>
```

**CI grep guards (D-17 + D-26)** — add to CI workflow (no in-repo CI file modification asked for in CONTEXT, but plan should call this out):
```bash
# Block wildcard pins in src csprojs
! grep -rE 'Version="(\*|\^)' src/GameKit.*/*.csproj

# Post-pack assertion on produced nuspecs
for nupkg in artifacts/*.nupkg; do
  unzip -p "$nupkg" "*.nuspec" | grep -E 'id="GameKit\.' \
    | grep -vE 'version="\[[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(-[a-z0-9.\-]+)?\]"' \
    && { echo "WILDCARD PIN: $nupkg"; exit 1; }
done
```

---

**`Directory.Packages.props`** (MODIFY)

**Self-analog** (existing file, 99 lines). Single additive edit per RESEARCH §Standard Stack NEW pin (correction to prior assumption that `Microsoft.AspNetCore.OpenApi` lives in shared framework — it does NOT):
```xml
<!-- Phase 6 — Microsoft.AspNetCore.OpenApi 10.0.8 verified GA on nuget.org 2026-05-12.
     NOT in Microsoft.AspNetCore.App shared framework despite earlier CONTEXT.md claim. -->
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.8" />
```

Place anywhere alphabetically in the existing `<ItemGroup>` at line 7. NO other pin changes for Phase 6.

---

### Block 9 — Admin UI Presence panel

**`src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor`** (NEW)

**Analogs:** `src/GameKit.Admin.UI/Components/Pages/Admins.razor` (table primitive + `MissingPackageAlert` semantics) + `src/GameKit.Admin.UI/Components/Pages/Dashboard.razor:148-176` (sequential awaits + reflection-safe detect)

**Pattern to copy — page header + `[Authorize]` policy + injection** (`Admins.razor:11-27` + `Dashboard.razor:14-23`):
```razor
@page "/admin/presence"
@attribute [Authorize(Policy = AdminPolicies.Admin)]
@using GameKit.Core.Services
@inject IServiceProvider Sp

<div class="page-head">
    <h1>Presence</h1>
    <div class="actions">
        <span class="muted">Top 25 · refreshes every 10s</span>
        <button type="button" class="btn btn-sm" @onclick="RefreshAsync">Refresh</button>
    </div>
</div>
```

**Pattern to copy — `<table class="t">` primitive** (`Admins.razor:41-82`):
```razor
<div class="table-wrap">
    <table class="t">
        <thead>
            <tr>
                <th class="sortable">Player ID</th>
                <th class="sortable">Display name</th>
                <th class="sortable">Status</th>
                <th class="sortable">Last seen</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var row in _rows)
            {
                <tr>
                    <td class="mono">@row.PlayerId.ToString("N")[..8]…</td>
                    <td>@row.DisplayName</td>
                    <td><StatusChip Status="@row.Status.ToString()" /></td>
                    <td title="@row.LastSeen.ToString("yyyy-MM-dd HH:mm:ss") UTC">@RelativeTime(row.LastSeen)</td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

**Pattern to copy — `MissingPackageAlert` early-return** (`Dashboard.razor:67-70` + UI-SPEC §9):
```razor
@if (Sp.GetService<IPresenceProvider>() is null)
{
    <MissingPackageAlert PackageName="Presence" Feature="presence telemetry" />
    return;
}
```

**Pattern to copy — `OnInitializedAsync` + sequential awaits** (`Dashboard.razor:148-176`):
```csharp
protected override async Task OnInitializedAsync()
{
    var mmType = Type.GetType("GameKit.Matchmaking.Strategy.IMatchmakingStrategy, GameKit.Matchmaking", throwOnError: false);
    _matchmakingInstalled = mmType is not null && Sp.GetService(mmType) is not null;
    // … sequential awaits …
}
```

For Presence the detect is cheaper (CONTEXT line 80): `Sp.GetService<IPresenceProvider>() != null` — no `Type.GetType` reflection needed because `IPresenceProvider` is in always-loaded Core.

**10s polling timer (UI-SPEC §10)** — see RESEARCH cite "Phase 03 D-10 pattern". The class implements `IDisposable`, owns a `System.Threading.Timer` + `CancellationTokenSource`. Pattern to mirror is the existing Phase 3 Dashboard auto-refresh logic (not opened during research; planner should grep `tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs` to confirm shape).

**`RelativeTime` static helper** — UI-SPEC §6 ladder (`just now` / `{n}s ago` / `{n}m ago` / `{n}h ago` / `{n}d ago`). Single-use; co-located with the page per UI-SPEC §7.

---

**`src/GameKit.Admin.UI/Components/Layout/SideNav.razor`** (MODIFY)

**Self-analog**, file is 49 lines. Insert ONE row between line 33 (Health row) and line 34 (Queue depth row):
```razor
<NavLink href="/admin/presence" class="nav-item" ActiveClass="nav-item active">
    <span class="label">Presence</span>
</NavLink>
```

No `AuthorizeView` wrapper — Presence panel is `AdminPolicies.Admin`, not `Superadmin` (matches Health row treatment).

---

**`src/GameKit.Admin.UI/Components/Shared/StatusChip.razor`** (MODIFY)

**Self-analog**, file is 40 lines. Extend the switch at line 32-38:
```csharp
private static string ChipModifierClass(string status) => status?.Trim().ToLowerInvariant() switch
{
    "ok" or "active" or "healthy" or "online" or "up" => "healthy",
    "degraded" or "warning" => "degraded",
    "down" or "offline" or "error" or "banned" => "down",
    "inmatch" or "in match" or "in-match" => "in-match",  // NEW Phase 6
    "offline" when /* never reached due to "down" arm above — see precedence note */ => "offline",
    _ => "info",
};
```

**Precedence WARNING:** `"offline"` is currently mapped to `"down"` (red) at line 36. UI-SPEC §5 wants `"offline"` mapped to a NEW neutral `"offline"` class (gray). The planner must decide: either (a) MOVE the `"offline"` arm OUT of the `"down"` group and add the new `"offline" => "offline"` arm above the existing line 36, OR (b) accept that `Offline` chips render red (matches current `Banned` semantics — operator sees Offline as a problem state). UI-SPEC §5 explicitly states gray. **Recommend (a):** split the existing `"down" or "offline" or "error" or "banned"` arm into two — `"down" or "error" or "banned" => "down"` and new `"offline" => "offline"`. This is a self-modify; document in plan as a STATUS-CHIP-PRECEDENCE deviation.

---

**`src/GameKit.Admin.UI/wwwroot/gamekit-admin.css`** (APPEND ~10 lines)

**Self-analog** — append to the existing chip rules (UI-SPEC §7 says "below the existing chip rules at line 357" — line number not verified; planner can grep for `.chip.healthy` to locate). Add:
```css
.chip.in-match {
    background: var(--amber-bg);
    color: var(--amber);
    border-color: var(--amber-border);
}
.chip.in-match .dot { background: var(--amber); }

.chip.offline {
    background: var(--surface-2);
    color: var(--fg-3);
    border-color: var(--border);
}
.chip.offline .dot { background: var(--fg-3); }
```

All tokens (`--amber`, `--amber-bg`, `--amber-border`, `--surface-2`, `--fg-3`, `--border`) already exist per UI-SPEC §5 confirmation. ZERO new tokens.

---

### Block 10 — Sample-game additions (`samples/TicTacToeDuel/Program.cs` modify + new GameServer console)

**`samples/TicTacToeDuel/Program.cs`** (MODIFY)

**Self-analog**, file is 173 lines. Two one-line edits:
1. Add `.AddPresence()` to the fluent chain after `.AddMatchmaking(...)` at line 113 (closes the `AddMatchmaking` chain) — example: insert a new `gameKitBuilder.AddPresence();` call between line 114 (close of AddMatchmaking) and line 116 (`AddGameKitAdmin`).
2. Add `app.MapPresence();` after `app.MapMatchmaking();` at line 151.

Also add the missing `using GameKit.Presence.Builder;` at the top.

---

**`samples/TicTacToeDuel.GameServer/Program.cs`** (NEW console app)

**Partial-analog:** `src/GameKit.Cli/Program.cs` (not opened — Spectre.Console.Cli pattern for console apps). The new GameServer is simpler: a single loop that:
1. Reads from Postgres via Npgsql using the `gamekit_reader` connection string (no DbContext — direct Npgsql command per RESEARCH topology line 219-224).
2. Selects an active ladder + recent players for matchmaking eligibility.
3. POSTs `/api/sessions/{id}/start` against the web app's HTTP surface using a service-account JWT.
4. Sleeps; loop.

**HttpClient construction**: use `HttpClient` directly or `IHttpClientFactory` via minimal `Host.CreateApplicationBuilder` console host. No existing in-repo analog — RESEARCH §Decision 16 + OQ4 + topology diagram lines 219-224 cite this as the new pattern.

**`samples/TicTacToeDuel.GameServer/appsettings.json`** (NEW)

**Analog:** `samples/TicTacToeDuel/appsettings.json` (clone — open it during planning if needed). The diff is one line: `ConnectionStrings:GameKit` uses `Username=gamekit_reader;Password=gamekit_reader_dev` per CONTEXT `<specifics>` line 147 (matches `PostgresFixture.cs:53` `ReaderConnectionString` shape).

---

### Block 11 — Template (NEW — NO in-repo analog)

**`templates/GameKit.Templates/GameKit.Templates.csproj`** + `.template.config/template.json` + content tree.

**No in-repo analog.** Use RESEARCH Pattern 8 lines 795-863 verbatim:

**csproj** (lines 845-863):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageType>Template</PackageType>
    <PackageId>GameKit.Templates</PackageId>
    <Title>GameKit Project Templates</Title>
    <PackageVersion>$(Version)</PackageVersion>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <NoWarn>$(NoWarn);NU5128;NU5119</NoWarn>
    <NoDefaultExcludes>true</NoDefaultExcludes>  <!-- keep .template.config -->
  </PropertyGroup>
  <ItemGroup>
    <Content Include="content\**\*" Exclude="content\**\bin\**;content\**\obj\**" />
    <Compile Remove="**\*" />
  </ItemGroup>
</Project>
```

**template.json** (lines 797-823) — copy verbatim. Five symbols: `name` (built-in -n), `skipAuth`, `skipRankings`, `skipMatchmaking`, `skipPresence`. `postActions` array runs `./scripts/gen-test-rsa-pem.sh` with `continueOnError: true` (CONTEXT `<specifics>` lines 149-150 + Pitfall 5 Windows fallback).

**Content tree (`templates/GameKit.Templates/content/GameKit.SampleGame/**`)** — verbatim clone of `samples/TicTacToeDuel/` AND new `samples/TicTacToeDuel.GameServer/` with:
- File rename: `TicTacToeDuel` → `GameKit.SampleGame` everywhere via `sourceName` (`template.json` field).
- Conditional content blocks in `Program.cs`:
```csharp
//#if (!skipAuth)
gameKitBuilder.AddAuth(auth => { /* ... */ });
//#endif
//#if (!skipRankings)
gameKitBuilder.AddRankings(opts => { /* ... */ }).AddLadder(...);
//#endif
```
- Conditional `<PackageReference>` entries in `.csproj` using the same `#if` syntax inside XML comments.

---

### Block 12 — Tests

**`tests/GameKit.Presence.Integration.Tests/CollectionDefinitions.cs`** (NEW)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` (full file, 26 lines)

**Pattern to copy verbatim** (replace "Matchmaking" with "Presence"):
```csharp
namespace GameKit.Presence.Integration.Tests;

[CollectionDefinition("Presence")]
public sealed class PresenceCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }
```

---

**`tests/GameKit.Presence.Integration.Tests/GameKit.Presence.Integration.Tests.csproj`** (NEW)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` (full file, 37 lines).

**Pattern to copy:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <WarningsAsErrors />
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Moq" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="StackExchange.Redis" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Testcontainers.Redis" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GameKit.Presence\GameKit.Presence.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

(Drop Rankings + Matchmaking + Admin.UI ProjectRefs — Presence doesn't depend on them at runtime. Auth is required for the JWT bearer scheme on the heartbeat endpoint integration test.)

---

**`tests/GameKit.Distribution.Integration.Tests/DistributionIntegrationFixture.cs`** (NEW)

**Analog:** `tests/GameKit.TestFixtures/PostgresFixture.cs` lines 36-53 (3-role init is ALREADY done — Phase 6 reuses verbatim; no need to modify PostgresFixture).

**Pattern to copy — already-existing 3-role bind-mount** (Phase 6 just consumes `PostgresFixture.ReaderConnectionString`):
```csharp
// PostgresFixture.cs:36-53
var initDir = Path.Combine(GitRootLocator.FindRepoRoot(), "docker", "postgres", "init");
_container = new PostgreSqlBuilder("postgres:17.9")
    .WithUsername("postgres")
    .WithPassword("postgres_test")
    .WithDatabase("postgres")
    .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
    .Build();
// …
ReaderConnectionString = $"Host={host};Port={port};Database=gamekit;Username=gamekit_reader;Password=gamekit_reader_dev";
```

`DistributionIntegrationFixture` is a thin composite over `PostgresFixture` + `RedisFixture` — no new container, just expose them as properties. Pitfall 8 (lines 949-952) is ALREADY MITIGATED in the repo — no action needed.

---

**`tests/GameKit.OpenApi.Integration.Tests/OpenApiCoverageTests.cs`** (NEW)

**No in-repo analog for the contract test logic.** Use RESEARCH Pattern 4 lines 569-613 verbatim. Wraps `WebApplicationFactory<Program>` (the existing TicTacToeDuel `Program` class — already public per `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` pattern):
```csharp
public sealed class OpenApiCoverageTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    [Fact]
    public async Task Every_NonAdmin_Endpoint_Is_In_OpenApi_Document()
    {
        using var scope = _factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var endpoints = sources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null)
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase))  // Pitfall §9
            .Select(e => /* (method, path) */)
            .Distinct().ToList();

        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json");
        // assertion: every (method, path) appears in paths
    }
}
```

---

**`tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs`** (NEW or extend existing `PanelRenderTests.cs`)

**Analog:** `tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs` (referenced — not opened). UI-SPEC §9 contract: assertion must contain literal substrings `"Install GameKit.Presence"` AND `"AddPresence(…)"` in the rendered response body.

Pattern to copy from the existing Matchmaking/Rankings parallels (mirrors how PanelRenderTests asserts `Install GameKit.Matchmaking` + `Install GameKit.Rankings` per `MissingPackageAlert.razor:7-9` comment).

---

## Shared Patterns

### Shared Pattern A: SPDX header on EVERY new source file
**Source:** Every existing `.cs` / `.razor` file in `src/` (e.g. `MatchmakingBuilderExtensions.cs:1-2`, `Admins.razor:1-2`).
**Apply to:** Every new source file Phase 6 creates (Presence, OpenApi, Build, tests, samples, template content). Required by CLAUDE.md.
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```
Razor variant:
```razor
@* SPDX-License-Identifier: GPL-3.0-or-later *@
@* Copyright (c) 2026 GameKit contributors *@
```

For template content under `templates/GameKit.Templates/content/`, the SPDX header should be REMOVED or replaced with a project-level header that uses the template-engine's `${ProjectName}` substitution (CONTEXT `<specifics>` lines 149-150 — post-action prepends GPL-3.0 header per source file generated).

### Shared Pattern B: XML doc comments on every public API (CS1591 enforced)
**Source:** Every public API in `src/` files (e.g. `MatchmakingBuilderExtensions.cs:22-60`, `IPresenceProvider.cs:11-36`). `Directory.Build.props:9` enforces `<WarningsAsErrors>CS1591;nullable</WarningsAsErrors>`.
**Apply to:** Every new public type/member added in Phase 6. Generated `GameKitMarker.g.cs` is `internal` so CS1591 does not trigger (RESEARCH §Runtime State Inventory line 904).

### Shared Pattern C: ProjectReference layout — Core + per-package siblings
**Source:** `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj:9-27` (Core + Rankings + Auth + Admin.UI ProjectRefs).
**Apply to:** New csprojs:
- `GameKit.Presence` → `GameKit.Core` only (already in stub).
- `GameKit.OpenApi` → `GameKit.Core` only.
- `GameKit.Build` → no ProjectRefs (analyzer-only).
Every `src/GameKit.*/*.csproj` (all 7) gets an Analyzer ProjectRef to `GameKit.Build` per Block 7 wiring.

### Shared Pattern D: Constructor injection (primary constructor or classic ctor)
**Source:** `RedisMatchmakingObservability` / `MatchmakingService` / `PendingRatingUpdatesAdapter` (constructor with `ArgumentNullException.ThrowIfNull` guards, fields named `_xxx`).
**Apply to:** All new services. Primary constructors are acceptable for short ctors (see `PresenceSessionObserver` in RESEARCH Pattern 2 line 453). Optional deps use nullable parameter + factory registration (mirror `SessionCompleteService.cs:80-94` constructor + `GameKitServiceCollectionExtensions.cs:63-69` factory).

### Shared Pattern E: Endpoint mapping inside dedicated `*Endpoints.cs` + `MapXxx` extension
**Source:** `src/GameKit.Matchmaking/Http/PartyEndpoints.cs` (file scope = exactly one endpoint group; `MapPartyEndpoints` extension method called from `MatchmakingApplicationBuilderExtensions.MapMatchmaking`).
**Apply to:** New `PresenceEndpoints.cs` (Block 4) and the new `/start`/`/abandon` routes added to existing `SessionEndpoints.cs` (Block 4 — extend the existing `MapSessions` group, not a new file).

### Shared Pattern F: xUnit collection-fixture per integration-test project
**Source:** `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` (the file COMMENT lines 8-11 explicitly cite xUnit1041 requiring the attribute in the same assembly as consumers — important for the planner).
**Apply to:** Every new integration-test project. Each project gets its own local `CollectionDefinitions.cs` even if the fixture types are shared from `GameKit.TestFixtures`.

### Shared Pattern G: Reflection-safe optional-package detection in Admin UI
**Source:** `Dashboard.razor:148-151` (`Type.GetType` + `Sp.GetService(type)`) AND CONTEXT line 80 simplification (`sp.GetService<IPresenceProvider>() != null` is enough because `IPresenceProvider` is in always-loaded Core — no `Type.GetType` reflection needed).
**Apply to:** `PresencePanel.razor` and any future panel that consumes a Core-defined optional port.

---

## No Analog Found

These files have no close in-repo precedent. The planner should use RESEARCH.md patterns + cited external sources rather than searching for analogs:

| File | Role | Reason | RESEARCH Reference |
|------|------|--------|--------------------|
| `src/GameKit.Build/GameKit.Build.csproj` | Roslyn analyzer csproj | No source generator exists today in repo | §Standard Stack lines 117-121; Anti-pattern §"Targeting net10.0 …" line 873-874; OQ3 line 1109 |
| `src/GameKit.Build/GameKitVersionGenerator.cs` | `IIncrementalGenerator` | No Roslyn generator exists today | Pattern 5 lines 624-661 (verbatim) |
| `GameKit.targets` (repo root) | MSBuild target file | No repo-root targets file today | Pattern 7-alt lines 759-771 |
| `src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs` | `IOpenApiDocumentTransformer` | OpenAPI is new to repo | Pattern 3 lines 527-559 (verbatim) |
| `src/GameKit.OpenApi/Transformers/GameKitInfoTransformer.cs` | `IOpenApiDocumentTransformer` | OpenAPI is new to repo; reads `GameKitMarker.GameKitVersion` | Block 7 (source-gen output) + Pattern 3 frame |
| `templates/GameKit.Templates/GameKit.Templates.csproj` + `template.json` + content tree | NuGet template package | No template authoring in repo today | Pattern 8 lines 795-866 (verbatim) |
| `samples/TicTacToeDuel.GameServer/Program.cs` | Console-app entry calling Postgres-via-Npgsql + HttpClient | No console-app GameKit consumer today (GameKit.Cli is Spectre/admin-tooling, not a game-server topology) | RESEARCH §architecture diagram lines 219-224 |
| `docs/ops/*.md` (9 files) | Multi-page Markdown ops guide | No multi-page docs structure today | D-18 + RESEARCH §Test architecture inheritance |
| `src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs` (logic body only — the lifecycle skeleton has an analog in `AuthMigrationHostedService.cs`) | Reflection-based assertion service | Reflection over `AppDomain.CurrentDomain.GetAssemblies()` + `Internal.GameKitMarker.GameKitVersion` is new logic | Pattern 6 lines 692-723 + D-24 eager-load pre-step |

---

## Critical Misuse Warnings

These are patterns where the executor is most likely to copy the WRONG analog:

1. **`OpenApiOptions.ShouldInclude` (D-19) is an inline lambda, NOT a separate `IOpenApiOperationTransformer`** — operation transformers cannot remove paths (RESEARCH Anti-pattern line 873 + Pattern 3 lines 514-525). The admin-route filter is `services.AddOpenApi("v1", o => { o.ShouldInclude = desc => !(desc.RelativePath?.StartsWith("admin", StringComparison.OrdinalIgnoreCase) == true); … });` — D-19 literal uses `StartsWith("admin", ...)` with **NO trailing slash** so the bare `/admin` route (Blazor admin console root) is also filtered. A `"admin/"` literal here would leak the bare `/admin` route into the OpenAPI document.

2. **`GameKitVersionAssertionHostedService` must be the FIRST `IHostedService`** — register at index 0 via `services.Insert(0, ServiceDescriptor.Singleton<IHostedService, …>())` in `AddGameKit()`. Appending it AFTER the migration hosted services means version mismatches are detected AFTER a partial migration, leaving inconsistent state.

3. **`GameKit.Build` MUST target `netstandard2.0`** — NOT `net10.0` (RESEARCH Anti-pattern line 873-874). Source generators load into the compiler host which targets netstandard2.0.

4. **`GameKit.Build` MUST set `ManagePackageVersionsCentrally=false`** — keeps the `Microsoft.CodeAnalysis.CSharp` pin inline because the analyzer version is tightly coupled to the generator API surface (D-25 / OQ3 line 1109).

5. **Source-generator visibility into `$(Version)` requires `<CompilerVisibleProperty Include="Version" />` in `Directory.Build.props`** (D-23 / Pitfall 1). Without it the generator emits the `"0.0.0"` fallback and `OPS04_VersionStampedAcrossPackagesTests` fails silently.

6. **Heartbeat write MUST use Lua script for in-match precedence** — NOT `StringSetAsync`. The Lua script checks `if v == 'in_match' then PEXPIRE else SET 'online'`. Plain `SETEX` would let a player's heartbeat downgrade in-match → online, violating D-03 (RESEARCH Pattern 1 §"CRITICAL precedence rule" lines 384-402 + Anti-pattern line 871).

7. **Reflection assertion must EAGER-LOAD GameKit.* assemblies** before `AppDomain.CurrentDomain.GetAssemblies()` — otherwise lazy-loaded packages (Matchmaking, Presence) whose endpoints haven't been hit yet at startup are silently missed (D-24 / Pitfall 3 lines 922-926).

8. **UI panel uses `<table class="t">`, NOT `MudDataGrid`** — UI-SPEC §8 documented deviation from CONTEXT D-06 (the post-Phase-03.1 Admin UI standardized on the sketch table primitive — see `Admins.razor:41-82`).

9. **`MissingPackageAlert.razor` template is UNCHANGED** — Phase 6 only ADDS a callsite with `PackageName="Presence" Feature="presence telemetry"`. The substring contract (UI-SPEC §9: must contain `Install GameKit.Presence` AND `AddPresence(…)`) is satisfied by the existing template line 20 with these parameters.

10. **`StatusChip.razor` precedence change required** — the existing `"down" or "offline" or "error" or "banned" => "down"` arm (line 36) MUST be split so `"offline" => "offline"` (new neutral class per UI-SPEC §5) takes precedence over the old red mapping. See Block 9 "Precedence WARNING".

11. **`DistributionIntegrationFixture` does NOT need a custom Testcontainer** — `PostgresFixture` already bind-mounts the 3-role init script at line 36-53 and exposes `ReaderConnectionString` at line 53. Pitfall 8 (lines 949-952) is already mitigated.

12. **Phase 6 SHIPS the `/start` and `/abandon` endpoints** — they do NOT exist today. Only `/complete` exists (`SessionEndpoints.cs:38-50`). D-20 + ROADMAP TYPO reconciliation. Plan 06-01 includes a one-line ROADMAP SC#1 wording fix + a one-line ROADMAP SC#5 "all 6 packages" → "all 7 packages" fix.

---

## Metadata

**Analog search scope:** `src/GameKit.{Core,Auth,Rankings,Matchmaking,Admin.UI,Presence,Cli}/`, `tests/GameKit.*/`, `samples/TicTacToeDuel/`, `Directory.Build.props`, `Directory.Packages.props`, `docker/postgres/init/`, `.planning/ROADMAP.md`.

**Files scanned:** ~30 source files read in full or in targeted ranges; ~70 listed via Glob.

**Strongest in-repo analogs:**
- Per-package builder + options pattern: `GameKit.Matchmaking` (best match — same partial-split shape).
- Core port + adapter pattern: `IPostSessionCompleteHandler` + `PendingRatingUpdatesAdapter` (exact mirror for new `ISessionLifecycleObserver` + `PresenceSessionObserver`).
- Hosted-service lifecycle: `AuthMigrationHostedService` (skeleton only; body is novel for version assertion).
- Admin UI table page: `Admins.razor` (table primitive) + `Dashboard.razor` (10s polling + reflection detect + `MissingPackageAlert` callsite).
- Test fixture composition: `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` (exact verbatim for Presence + Distribution).
- `PostgresFixture` 3-role init: already in repo — Phase 6 consumes it directly, no fixture changes needed.

**Patterns with NO in-repo precedent (planner relies on RESEARCH.md external citations):**
- Roslyn `IIncrementalGenerator` (Pattern 5).
- MSBuild `ItemDefinitionGroup` Pack-time metadata (Pattern 7-alt).
- `Microsoft.AspNetCore.OpenApi` document/operation transformers (Pattern 3).
- `EndpointDataSource` enumeration coverage test (Pattern 4).
- `dotnet new` template authoring (Pattern 8).

**Pattern extraction date:** 2026-05-25.
