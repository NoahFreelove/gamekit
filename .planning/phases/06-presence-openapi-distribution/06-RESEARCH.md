# Phase 6: Presence + OpenAPI + Distribution - Research

**Researched:** 2026-05-25
**Domain:** Redis-TTL presence + ASP.NET Core OpenAPI + dotnet-template authoring + Roslyn source generators + MSBuild Pack-time enforcement
**Confidence:** HIGH

## Summary

Phase 6 is a 4-deliverable close-out that lights up `GameKit.Presence`, ships a single OpenAPI document for every player-facing endpoint, packages the TicTacToeDuel sample as `dotnet new gamekit`, and hardens the release train so all six `GameKit.*` packages move as one. Almost every strategic decision is pre-locked in `06-CONTEXT.md` (D-01 .. D-18), so research focuses on technical idioms — the .NET 10 `Microsoft.AspNetCore.OpenApi` transformer surface, MSBuild dynamic `PackageReference` emission, incremental source generators that read MSBuild `$(Version)`, and the templating-engine `template.json` symbol/`#if`-conditional contract.

The standout finding that requires a flag to the planner: `Microsoft.AspNetCore.OpenApi` is **NOT** part of the `Microsoft.AspNetCore.App` shared framework in .NET 10 — it requires an explicit `PackageReference` (current latest `10.0.8`, released 2026-05-12) [VERIFIED: nuget.org/packages/Microsoft.AspNetCore.OpenApi]. CONTEXT.md (D-canonical_refs line 76) and CLAUDE.md both assert "ships in the .NET 10 shared framework"; this assumption is wrong. Phase 6 must add **one** new pin to `Directory.Packages.props` (`Microsoft.AspNetCore.OpenApi` 10.0.8), not zero as CONTEXT claims.

**Primary recommendation:** Build the four deliverables in dependency-order waves: (Wave 0) test scaffolding + `Directory.Packages.props` pin + `GameKit.OpenApi` + `GameKit.Build` skeletons; (Wave 1) `GameKit.Presence` Redis layer + `ISessionLifecycleObserver` Core port + new `/api/sessions/{id}/start` + `/abandon` endpoints; (Wave 2) `GameKit.OpenApi` document transformers + admin-route filter + contract test; (Wave 3) `GameKit.Build` source generator + `GameKitVersionAssertionHostedService` + `GameKit.targets` Pack-time exact-pin emission; (Wave 4) `templates/GameKit.Templates/` + `TicTacToeDuel.GameServer` console app + `docs/ops/` multi-page guide + DIST-02 / DIST-03 / OPS-04 / OPS-06 integration tests + CS1591 audit.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Presence shape (PRES-01..PRES-06):**
- **D-01:** Heartbeat TTL = 30 seconds; expected client cadence = ping every 10 seconds (3× safety factor). Consumers override via `GameKitPresenceOptions.{TtlSeconds, HeartbeatIntervalSeconds}`.
- **D-02:** Player-facing route = `POST /api/presence/heartbeat` (JWT-bearer-required, idempotent). NOT the bare `/presence/heartbeat` ROADMAP literal.
- **D-03:** In-match transition set by game-server `POST /api/sessions/{id}/start`; `/complete` and `/abandon` clear it back to online or offline. `IPresenceProvider.PresenceStatus.InMatch` XML doc is authoritative; ROADMAP SC#1 wording is a typo (see specifics below).
- **D-04:** Single Redis key per player — `presence:{playerId}` last-write-wins across devices. Per-device aggregation deferred to v2.
- **D-05:** No rate-limit on the heartbeat endpoint. Single Redis `SETEX`.
- **D-06:** Admin Presence panel — Top-25 online players, 10s refresh (reuses `GameKitAdminOptions.Panel.RefreshInterval`), MudDataGrid layout, columns `PlayerId | DisplayName | Status badge (Online / InMatch) | LastSeen`, sortable. Missing-package alert reuses `MissingPackageAlert.razor` with literal `Install GameKit.Presence` + `AddPresence(…)`.

**OpenAPI doc structure (OPEN-01):**
- **D-07:** Single combined document at `/openapi/v1.json`. Endpoints tagged by package (`auth`, `sessions`, `mm`, `parties`, `presence`).
- **D-08:** Public doc exposes ONLY the player JWT bearer scheme (`bearerAuth`). `/admin/api/*` excluded.
- **D-09:** Coverage contract test = `EndpointDataSource` enumeration via `WebApplicationFactory`. Lives in `tests/GameKit.OpenApi.Integration.Tests/`.
- **D-10:** Route prefixes documented as-is (no `/v1/` normalization). `info.version` encodes the MinVer-derived GameKit package version.

**Template + SampleGame (DIST-02..DIST-04):**
- **D-11:** `dotnet new gamekit -n ${ProjectName}` produces a full TicTacToeDuel clone (web app + GameServer console app + matchmaking.html + appsettings + README). Lives at `templates/GameKit.Templates/content/GameKit.SampleGame/`.
- **D-12:** Template parameters = `-n ${name}` + `--skip-auth`, `--skip-rankings`, `--skip-matchmaking`, `--skip-presence`. Each maps to conditional `<PackageReference>` + `Program.cs` `Add${Pkg}()` blocks via `template.json` symbols + `#if (!SKIP_X) ... #endif`.
- **D-13:** NEW `samples/TicTacToeDuel.GameServer/` console app — connects via `gamekit_reader`, reads ladders/players, calls `POST /api/sessions/{id}/start` + `/complete` via HTTP. Template clones BOTH web + GameServer.
- **D-14:** NEW `tests/GameKit.Distribution.Integration.Tests/` project houses DIST-02 + DIST-03 + OPS-04 + OPS-06.

**Release train + ops guide (OPS-04, OPS-05, DIST-05):**
- **D-15:** `GameKitVersion` const stamped by Roslyn source generator (NEW `src/GameKit.Build/`, ProjectRef-only Analyzer, never shipped as NuGet) reading MinVer `$(Version)`. Emits `internal const string GameKitVersion = "X.Y.Z";` into `GameKit.<Pkg>.Internal.GameKitMarker`.
- **D-16:** `GameKitVersionAssertionHostedService` in `IHost.StartAsync` iterates `AppDomain.CurrentDomain.GetAssemblies()` filtered to `GameKit.*`, reflects on `Internal.GameKitMarker.GameKitVersion`, throws `GameKitVersionMismatchException` on divergence. Registered automatically by `AddGameKit()`.
- **D-17:** Custom MSBuild target `GameKit.targets` at repo root, imported into `Directory.Build.props`. Runs in `Pack` phase, enumerates `<ProjectReference Include="..\GameKit.*\GameKit.*.csproj" />` and emits `<PackageReference Include="GameKit.X" Version="[$(Version)]" />` (square-bracket exact-pin). CI wildcard guard: `! grep -rE 'Version="(\*|\^)' src/**/*.csproj`.
- **D-18:** Multi-page `docs/ops/` — `README.md`, `bare-metal.md`, `container.md`, `air-gapped.md`, `postgres-roles.md`, `redis-aof.md`, `jwt-keys.md`, `disaster-recovery.md`, `migrations-runbook.md`. Repo-root README gets "Production Deployment" section.

### Claude's Discretion
- Specific HTTP shape for `/api/presence/heartbeat` request body (default: empty body since JWT identifies the player).
- `LastSeen` column format in Admin panel (default: relative "3 seconds ago").
- Scope of OPS-06 clean-install migration test (default: single-shot against fresh empty Postgres).
- Whether `GameKit.Build` emits a `[GameKitInfo]` attribute alongside the const (default: const-only).

### Deferred Ideas (OUT OF SCOPE)
- Per-device aggregated presence
- Presence-driven analytics
- `/api/v2/*` prefix normalization + `/auth/*` legacy 308-redirect plumbing
- `/openapi/admin/v1.json` separate admin-cookie-gated doc
- Second `gamekit-skeleton` minimal template
- Argon2 password hasher sibling package
- `[GameKitInfo]` assembly attribute alongside the source-gen-emitted const
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PRES-01 | `GameKit.Presence` NuGet package — Redis-only (no EF entities) | Phase-1 csproj stub is already wired with `Core` ProjectRef; Phase 6 adds Redis layer + `PresenceBuilderExtensions` |
| PRES-02 | Implements `Core.IPresenceProvider` | Interface locked at `src/GameKit.Core/Services/IPresenceProvider.cs`; impl uses StackExchange.Redis `SETEX` / `GET` |
| PRES-03 | Heartbeat endpoint: client posts liveness; expires via Redis TTL | Standard `SETEX presence:{playerId} 30 "online"` pattern; key auto-expires |
| PRES-04 | Status states: online / offline / in-match | Enum locked in `Core.PresenceStatus`; status derives from key existence + value content |
| PRES-05 | Abandonment grace period (game-server-authoritative) | New `POST /api/sessions/{id}/abandon` endpoint + new `ISessionLifecycleObserver` Core port wired into `SessionCompleteService` |
| PRES-06 | Admin UI presence panel (top-N online, per-player status) | `Components/Pages/Presence.razor` + MudDataGrid; reuses `Panel.RefreshInterval` |
| OPEN-01 | OpenAPI spec via `Microsoft.AspNetCore.OpenApi` covering all GameKit HTTP endpoints | `AddOpenApi()` + `MapOpenApi()` + document transformer for JWT bearer + operation transformer for admin filter + per-`MapGroup().WithTags(...)` package grouping |
| DIST-02 | Integration test asserts `gamekit_reader` cannot INSERT into `gamekit.sessions` (typo in REQUIREMENTS — actual table is `game_sessions`) | Two-connection test: seed via `gamekit_owner`, attempt INSERT via `gamekit_reader`, assert Postgres SQLSTATE `42501` |
| DIST-03 | `SampleGame` reference application using all packages + `gamekit_reader` from game-server side | Full TicTacToeDuel + new `TicTacToeDuel.GameServer` console process |
| DIST-04 | `GameKit.Template` NuGet template — `dotnet new gamekit` wraps SampleGame | `templates/GameKit.Templates/` csproj with `<PackageType>Template</PackageType>`; `template.json` symbols + `#if` conditional content |
| DIST-05 | Production-readiness ops guide (bare-metal, container, air-gapped recipes) | Multi-page `docs/ops/` per D-18 |
| DIST-06 | All public APIs have XML doc comments — CS1591 enforced as error across all packages | Audit confirms no `<NoWarn>1591` overrides exist today; verification via `grep -rE '<NoWarn>.*1591' src/` |
| OPS-04 | Coordinated SemVer release train: all 6 packages stamp same MinVer-derived version; sibling refs exact-pinned `[X.Y.Z]` | `GameKit.targets` Pack-time emission of `<PackageReference Version="[$(Version)]" />` |
| OPS-05 | Runtime startup assertion: all GameKit packages report matching `GameKitVersion` constant; fail-fast on mismatch | `GameKitVersionAssertionHostedService` reflects `AppDomain.CurrentDomain.GetAssemblies()` for `GameKit.*` |
</phase_requirements>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Presence heartbeat write (player → server) | API / Backend | Cache / Redis | Player POSTs JWT-authenticated request; server writes single Redis key with TTL |
| Presence read (admin panel → server) | API / Backend (via `IPresenceProvider`) | Cache / Redis | Admin panel queries via DI-injected provider; Redis `GET` + `KEYS`/`SCAN` |
| In-match transition write | API / Backend (game-server → web) | Database (session state) + Cache | Game-server is authoritative; calls `/api/sessions/{id}/start` which mutates Postgres AND signals Presence via `ISessionLifecycleObserver` |
| OpenAPI document generation | API / Backend | — | `Microsoft.AspNetCore.OpenApi` middleware introspects `EndpointDataSource` at runtime |
| OpenAPI Swagger UI consumption | Out of scope (consumer choice) | — | GameKit ships ONLY the JSON document; consumers add Swagger UI / Scalar separately if desired |
| Template scaffolding | Build-tool / dotnet CLI | — | `dotnet new gamekit` runs entirely client-side; produces files, no runtime |
| Version stamp emission | Build-time / Roslyn compile | — | `GameKit.Build` source generator runs at compile, emits const into each assembly |
| Version mismatch detection | API / Backend (host startup) | — | `GameKitVersionAssertionHostedService` runs in `IHost.StartAsync` before Kestrel accepts traffic |
| Exact-pin enforcement | Build-time / MSBuild Pack | CI | `GameKit.targets` runs during `dotnet pack`; CI grep guard catches wildcard pins pre-merge |
| `gamekit_reader` permission enforcement | Database (Postgres GRANT/REVOKE) | — | Already provisioned by `docker/postgres/init/01-roles.sql`; Phase 6 just adds a test |
| Ops documentation | Markdown / docs/ — read-time only | — | Pure docs; no runtime |

## Standard Stack

### Core (already pinned)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| StackExchange.Redis | 2.8.41 | Presence Redis client | Already pinned repo-wide from Phase 1 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.6 | Heartbeat endpoint JWT auth | Already pinned from Phase 2 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | `WebApplicationFactory` for OpenAPI contract test + DIST-03 smoke test | Already pinned from Phase 2 |
| Testcontainers.PostgreSql / Testcontainers.Redis | 4.11.0 | Distribution.Integration.Tests | Already pinned from Phase 1 |
| MudBlazor | 9.3.0 | Admin Presence panel — `MudDataGrid` columns | Already pinned from Phase 3 |

### NEW pin required
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| **Microsoft.AspNetCore.OpenApi** | **10.0.8** | OpenAPI document generation via `AddOpenApi()` / `MapOpenApi()` | First-party Microsoft package; NOT shared framework despite CONTEXT.md claim — explicit pin required [VERIFIED: nuget.org] |

### Compile-time / build-time (project-references, no runtime ship)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.CodeAnalysis.CSharp | **4.13.0** | `GameKit.Build` Roslyn incremental source generator | Lowest version compatible with .NET 10 SDK + `netstandard2.0` TFM that source generators MUST target (generator runs in the compiler host, not the consumer runtime) [CITED: github.com/dotnet/roslyn — source generators must target netstandard2.0] |
| Microsoft.CodeAnalysis.Analyzers | 3.11.0 | Diagnostics for the generator itself (optional) | Optional; only if we ship analyzer diagnostics |

**Note on Microsoft.CodeAnalysis.CSharp 5.3.0:** WebFetch reports 5.3.0 as the latest stable. That version targets `net8.0` + `netstandard2.0`. Either 4.13+ or 5.3 works for our generator; 4.13 is the conservative pick (lowest version still supporting `IIncrementalGenerator` cleanly; 5.x is fine if we want the latest analyzer infrastructure). The pin lives in `src/GameKit.Build/GameKit.Build.csproj` — NOT in `Directory.Packages.props` if we keep CPM but exclude this single project, OR it goes in `Directory.Packages.props` if we accept that the analyzer pin is the single exception to "everything CPM-managed." Plan-time call.

### Already-present transitively (no new pins)
| Library | Source | Purpose |
|---------|--------|---------|
| Microsoft.OpenApi 2.0+ | Transitive via `Microsoft.AspNetCore.OpenApi` | OpenAPI model types |
| Microsoft.TemplateEngine.Authoring.Templates | SDK-aligned (10.0.x) | `dotnet new gamekit` template authoring — no explicit pin needed; the `<PackageType>Template</PackageType>` + `template.json` contract is SDK-bundled |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Microsoft.AspNetCore.OpenApi | Swashbuckle.AspNetCore | Swashbuckle was dropped from the default .NET 9/10 web-api templates in favor of Microsoft.AspNetCore.OpenApi; staying on the first-party package matches CLAUDE.md "install only what you need" + CONTEXT D-07 [CITED: learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi] |
| Microsoft.AspNetCore.OpenApi alone | + Scalar.AspNetCore for UI | Scalar (MIT, GPL-compatible) gives a nicer browseable UI but adds a runtime dep and a `.MapScalarApiReference()` middleware. CONTEXT D-07 specifies "JSON doc only"; UI is out of scope for v1. |
| `BackgroundService` for Presence cleanup | Redis TTL alone (D-01) | TTL alone is sufficient — keys auto-expire, no sweeper needed. A reconciler would be a v2 enhancement for per-device aggregated presence. |
| Roslyn source generator (D-15) | Runtime `AssemblyInformationalVersion` parsing | Source generator is JIT-intern-pooled (zero allocation per check) and runs at compile-time so a missing/stale build artifact is caught before runtime. Runtime parsing would still work but is strictly slower and weaker. |
| In-process integration test for DIST-03 | Testcontainers running the SampleGame as a Docker image | In-process via `WebApplicationFactory` is 10x faster and matches the existing Phase 3-5 integration-test pattern. The GameServer console can be spun up as a `Process.Start` invocation in the test, OR as a `Task.Run` invocation that calls the GameServer's `Main()` directly. Default to in-process `Process.Start` — closer to real production topology. |

**Version verification (run before write):**
```bash
# Confirm Microsoft.AspNetCore.OpenApi 10.0.8 is current
# (cannot run dotnet/curl from researcher; verified via nuget.org WebFetch 2026-05-25)
```

## Package Legitimacy Audit

> Phase 6 adds **one new NuGet pin**: `Microsoft.AspNetCore.OpenApi` 10.0.8.
>
> `slopcheck` is PyPI-only — it cannot validate NuGet packages [CITED: tool output `slopcheck install Microsoft.AspNetCore.OpenApi` returned `[SLOP]: Package 'Microsoft.AspNetCore.OpenApi' does not exist on pypi`]. This is a tool limitation, not a real slop signal. The package is a first-party Microsoft package present on nuget.org with 100M+ weekly downloads, source repo at `github.com/dotnet/aspnetcore`. Treat as `[OK]`.

| Package | Registry | Age | Downloads | Source Repo | slopcheck | Disposition |
|---------|----------|-----|-----------|-------------|-----------|-------------|
| Microsoft.AspNetCore.OpenApi | nuget.org | 1.5 yrs (9.0 GA 2024-11-12; 10.0.x current) | 100M+ /mo | github.com/dotnet/aspnetcore (Microsoft, MIT) | N/A (PyPI-only tool) | Approved [VERIFIED: nuget.org + Microsoft owner] |
| Microsoft.CodeAnalysis.CSharp | nuget.org | 8+ yrs | 50M+ /wk | github.com/dotnet/roslyn (Microsoft, MIT) | N/A | Approved [VERIFIED: nuget.org + Microsoft owner] |

**Packages removed due to slopcheck [SLOP] verdict:** none (false positives from PyPI-only tool — not applicable to NuGet).
**Packages flagged as suspicious [SUS]:** none.

## Architecture Patterns

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                       │
│    PLAYER CLIENT (browser SPA / Steam client / game client)                          │
│       │                                                                               │
│       │  POST /api/presence/heartbeat                                                 │
│       │    Authorization: Bearer <player-JWT>                                         │
│       │    Body: {}                                                                   │
│       ▼                                                                               │
│  ┌──────────────────────────────────────────────────────────────────────────────┐   │
│  │  WEB TIER — GameKit.* packages loaded into consumer's ASP.NET Core 10 app    │   │
│  │                                                                               │   │
│  │  ┌────────────────────────────────────────────────────────────────────────┐  │   │
│  │  │ PresenceEndpoints.MapPresence() (NEW Phase 6)                          │  │   │
│  │  │   POST /api/presence/heartbeat → IPresenceWriter.WriteHeartbeatAsync   │  │   │
│  │  │                                                                         │  │   │
│  │  │   ┌────────────────────────────────────────────────────────────────┐  │  │   │
│  │  │   │ RedisPresenceProvider : IPresenceProvider + IPresenceWriter    │  │  │   │
│  │  │   │   SETEX presence:{playerId} 30 "online"   ◄─── heartbeat       │  │  │   │
│  │  │   │   SETEX presence:{playerId} 30 "in_match" ◄─── /sessions/start │  │  │   │
│  │  │   │   DEL   presence:{playerId}                ◄─── /sessions/abandon (TTL takes over)
│  │  │   └────────────────────────────────────────────────────────────────┘  │  │   │
│  │  └────────────────────────────────────────────────────────────────────────┘  │   │
│  │                                                                               │   │
│  │  ┌────────────────────────────────────────────────────────────────────────┐  │   │
│  │  │ SessionEndpoints (Core, ENHANCED Phase 6)                              │  │   │
│  │  │   POST /api/sessions/{id}/start    (NEW — service-token auth)          │  │   │
│  │  │   POST /api/sessions/{id}/complete (existing Phase 4)                  │  │   │
│  │  │   POST /api/sessions/{id}/abandon  (NEW — service-token auth)          │  │   │
│  │  │                                                                         │  │   │
│  │  │   ↓ side-effect via ISessionLifecycleObserver (NEW Core port)          │  │   │
│  │  │     PresenceSessionObserver implements ISessionLifecycleObserver       │  │   │
│  │  └────────────────────────────────────────────────────────────────────────┘  │   │
│  │                                                                               │   │
│  │  ┌────────────────────────────────────────────────────────────────────────┐  │   │
│  │  │ Microsoft.AspNetCore.OpenApi pipeline                                  │  │   │
│  │  │   AddOpenApi("v1", opts =>                                             │  │   │
│  │  │     opts.AddDocumentTransformer<GameKitBearerSchemeTransformer>()      │  │   │
│  │  │     opts.AddOperationTransformer<GameKitAdminRouteFilter>())           │  │   │
│  │  │   MapOpenApi("/openapi/{documentName}.json")  ◄── public, anon-OK     │  │   │
│  │  └────────────────────────────────────────────────────────────────────────┘  │   │
│  │                                                                               │   │
│  │  ┌────────────────────────────────────────────────────────────────────────┐  │   │
│  │  │ GameKitVersionAssertionHostedService (NEW Phase 6, in GameKit.Core)    │  │   │
│  │  │   IHost.StartAsync → reflect AppDomain.CurrentDomain.GetAssemblies()   │  │   │
│  │  │   → throw GameKitVersionMismatchException if any version diverges      │  │   │
│  │  └────────────────────────────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────────────────────────┘   │
│         │                              ▲                                              │
│         │  SETEX/GET                   │  query players for matchmaking eligibility   │
│         ▼                              │                                              │
│  ┌─────────────────┐         ┌──────────────────────────────────────────────┐       │
│  │  Redis 8        │         │  Postgres 17 — gamekit schema                │       │
│  │  presence:{id}  │         │  3 roles: gamekit_owner / gamekit_app /     │       │
│  │  TTL=30s        │         │            gamekit_reader (DIST-02)         │       │
│  └─────────────────┘         └──────────────────────────────────────────────┘       │
│                                       ▲                                              │
│                                       │ SELECT only (gamekit_reader)                 │
│  ┌─────────────────────────────────────┴──────────────────────────────────────┐    │
│  │  GAME-SERVER TIER — TicTacToeDuel.GameServer console (NEW Phase 6)         │    │
│  │  Reads ladders + players via gamekit_reader Npgsql connection             │    │
│  │  HTTP-calls POST /api/sessions/{id}/start + /complete + /abandon          │    │
│  │  on the web tier with a service-account JWT                              │    │
│  └────────────────────────────────────────────────────────────────────────────┘    │
│                                                                                       │
│  ADMIN TIER (Blazor Server at /admin)                                                │
│    Components/Pages/Presence.razor (NEW Phase 6)                                     │
│      → IPresenceProvider.GetOnlinePlayerIdsAsync(25)                                 │
│      → MudDataGrid: PlayerId | DisplayName | Status | LastSeen                      │
│      → 10s auto-refresh via GameKitAdminOptions.Panel.RefreshInterval               │
│      → MissingPackageAlert when sp.GetService<IPresenceProvider>() == null          │
│                                                                                       │
│  BUILD TIER (compile-time)                                                           │
│    src/GameKit.Build/ Roslyn IIncrementalGenerator                                   │
│      reads MSBuild $(Version) via AnalyzerConfigOptionsProvider                      │
│      → emits internal const GameKit.<Pkg>.Internal.GameKitMarker.GameKitVersion     │
│      ProjectRef'd from every src/GameKit.*/ csproj                                   │
│      with OutputItemType="Analyzer" ReferenceOutputAssembly="false"                 │
│                                                                                       │
│  PACK TIER (dotnet pack)                                                             │
│    GameKit.targets (NEW Phase 6, imported from Directory.Build.props)               │
│      enumerates <ProjectReference Include="..\GameKit.*\GameKit.*.csproj" />        │
│      emits <PackageReference Include="GameKit.X" Version="[$(Version)]" />          │
│      square-bracket = exact-pin, no float                                            │
│                                                                                       │
│  TEMPLATE TIER (dotnet new gamekit)                                                  │
│    templates/GameKit.Templates/content/GameKit.SampleGame/                          │
│      → mirror of samples/TicTacToeDuel/ with ${ProjectName} substitution            │
│      → template.json: -n + --skip-{auth,rankings,matchmaking,presence}              │
│      → #if (!SKIP_X) ... #endif in Program.cs + .csproj                             │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

### Recommended Project Structure (additions only — existing layout unchanged)

```
src/
├── GameKit.Presence/                          # filled in from Phase 1 stub
│   ├── AssemblyInfo.cs                        # existing
│   ├── GameKit.Presence.csproj                # +StackExchange.Redis +Microsoft.AspNetCore.App FrameworkReference +GameKit.Build ProjectRef
│   ├── GameKitPresenceOptions.cs              # TtlSeconds, HeartbeatIntervalSeconds
│   ├── PresenceRedisKeys.cs                   # "presence:{playerId}" formatter
│   ├── PresenceValues.cs                      # "online" / "in_match" string constants
│   ├── Services/
│   │   ├── IPresenceWriter.cs                 # NEW write-only port (separate from Core's IPresenceProvider read-only port)
│   │   ├── RedisPresenceProvider.cs           # implements IPresenceProvider + IPresenceWriter
│   │   └── PresenceSessionObserver.cs         # implements ISessionLifecycleObserver
│   ├── Http/
│   │   ├── PresenceEndpoints.cs               # POST /api/presence/heartbeat
│   │   └── Contracts/HeartbeatResponse.cs     # {} body OK; HEAD-style 204 also valid
│   └── Builder/
│       ├── PresenceBuilderExtensions.cs       # AddPresence(...) + UsePresence(...) + MapPresence(...)
│       └── PresenceBuilderExtensions.Options.cs # partial-class options binder
│
├── GameKit.OpenApi/                           # NEW 7th package
│   ├── AssemblyInfo.cs                        # SPDX + InternalsVisibleTo
│   ├── GameKit.OpenApi.csproj                 # +Microsoft.AspNetCore.OpenApi 10.0.8 +Microsoft.AspNetCore.App
│   ├── GameKitOpenApiOptions.cs               # DocumentName="v1", Title, MountPath="/openapi"
│   ├── OpenApiMarker.cs                       # partial; source gen extends
│   ├── Transformers/
│   │   ├── GameKitBearerSchemeTransformer.cs  # IOpenApiDocumentTransformer — adds bearerAuth
│   │   ├── GameKitAdminRouteFilter.cs         # IOpenApiOperationTransformer — drops admin paths
│   │   └── GameKitInfoTransformer.cs          # IOpenApiDocumentTransformer — sets info.title + info.version (from GameKitMarker.GameKitVersion)
│   └── Builder/
│       ├── OpenApiBuilderExtensions.cs        # AddGameKitOpenApi(...) + MapGameKitOpenApi(...)
│       └── OpenApiBuilderExtensions.Options.cs
│
├── GameKit.Build/                             # NEW Roslyn source generator (ProjectRef-only, never NuGet-published)
│   ├── AssemblyInfo.cs
│   ├── GameKit.Build.csproj                   # SDK=Microsoft.NET.Sdk; TFM=netstandard2.0; <IsRoslynComponent>true</IsRoslynComponent>; +Microsoft.CodeAnalysis.CSharp 4.13.0
│   ├── GameKitVersionGenerator.cs             # IIncrementalGenerator reading build_property.Version
│   └── README.md                              # not-for-distribution notice
│
└── (existing) GameKit.Core/, GameKit.Auth/, GameKit.Admin.UI/, GameKit.Rankings/, GameKit.Matchmaking/ get:
    ├── +GameKit.Build ProjectRef (OutputItemType="Analyzer" ReferenceOutputAssembly="false")
    └── (generated at compile) Internal/GameKitMarker.g.cs containing `internal const string GameKitVersion = "X.Y.Z";`

src/GameKit.Core/Services/  (Phase 6 additions)
├── ISessionLifecycleObserver.cs    # new port — mirrors IPostSessionCompleteHandler
├── GameKitVersionMismatchException.cs
└── (mod) SessionCompleteService.cs # injects IEnumerable<ISessionLifecycleObserver>, fires after Commit
                                      and on the new /start + /abandon flows

src/GameKit.Core/Hosting/  (Phase 6 additions)
└── GameKitVersionAssertionHostedService.cs

samples/
├── TicTacToeDuel/                  # existing — add app.MapPresence() to Program.cs
└── TicTacToeDuel.GameServer/       # NEW console app
    ├── TicTacToeDuel.GameServer.csproj  # PackageReferences: Npgsql + Microsoft.Extensions.Http
    ├── Program.cs                       # reads via gamekit_reader; HTTP-calls /api/sessions/{id}/start + /complete
    ├── appsettings.json                 # ConnectionStrings:GameKit uses gamekit_reader_dev
    └── README.md

templates/
└── GameKit.Templates/               # NEW NuGet template package
    ├── GameKit.Templates.csproj     # <PackageType>Template</PackageType>; <IncludeContentInPack>true</IncludeContentInPack>
    ├── README.md
    └── content/
        └── GameKit.SampleGame/      # template body — mirrors samples/TicTacToeDuel/ + .GameServer/
            ├── .template.config/
            │   └── template.json    # symbols: name (-n), skipAuth/skipRankings/skipMatchmaking/skipPresence
            ├── GameKit.SampleGame.sln
            ├── src/
            │   ├── GameKit.SampleGame/         # web app — clone of TicTacToeDuel
            │   └── GameKit.SampleGame.GameServer/  # console clone of TicTacToeDuel.GameServer
            └── docker-compose.yml   # clone of repo-root

tests/
└── GameKit.Distribution.Integration.Tests/   # NEW — houses DIST-02 + DIST-03 + OPS-04 + OPS-06
    ├── GameKit.Distribution.Integration.Tests.csproj
    ├── DistributionIntegrationFixture.cs     # Postgres + Redis composite
    ├── CollectionDefinitions.cs
    ├── DIST02_GamekitReaderInsertDeniedTests.cs
    ├── DIST03_TemplateSampleGameSmokeTests.cs
    ├── OPS04_VersionStampedAcrossPackagesTests.cs
    ├── OPS05_VersionMismatchAssertionTests.cs (D-16 lives here too)
    └── OPS06_CleanInstallMigrationTests.cs

tests/
└── GameKit.OpenApi.Integration.Tests/         # NEW — D-09
    ├── GameKit.OpenApi.Integration.Tests.csproj
    ├── OpenApiCoverageTests.cs                # EndpointDataSource enumeration contract test
    ├── OpenApiBearerSchemeTests.cs            # bearerAuth present in components.securitySchemes
    └── OpenApiAdminRouteExclusionTests.cs     # /admin/api/* NOT present in document paths

docs/
└── ops/                # NEW Phase 6
    ├── README.md       # index + ToC
    ├── bare-metal.md
    ├── container.md
    ├── air-gapped.md
    ├── postgres-roles.md
    ├── redis-aof.md
    ├── jwt-keys.md
    ├── disaster-recovery.md
    └── migrations-runbook.md

GameKit.targets         # NEW at repo root — Pack-time exact-pin emission per D-17
```

### Pattern 1: Redis-keyed presence with TTL

**What:** Each player has a single Redis key `presence:{playerId}` whose value carries the status (`"online"` or `"in_match"`) and whose TTL is the heartbeat window (default 30s). Absent key ⇒ offline. Last write wins across devices (D-04).

**When to use:** Any time you need fire-and-forget liveness with millisecond writes and zero state cleanup. Postgres is the wrong tool — heartbeats are 10×/min/player and would burn IOPS.

**Example:**
```csharp
// Source: StackExchange.Redis docs + Pattern 4 in CONTEXT.md D-01..D-05
public sealed class RedisPresenceProvider : IPresenceProvider, IPresenceWriter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly GameKitPresenceOptions _opts;

    public ValueTask WriteHeartbeatAsync(Guid playerId, CancellationToken ct)
    {
        var key  = $"presence:{playerId}";
        var ttl  = TimeSpan.FromSeconds(_opts.TtlSeconds);

        // CRITICAL precedence rule (D-03 + D-04):
        // If the key currently holds "in_match", a heartbeat MUST refresh the TTL
        // but NOT downgrade the value to "online". Game-server-authoritative wins.
        // Implementation: Lua script "if GET == 'in_match' then PEXPIRE; else SETEX 'online'"
        // OR: simpler — write SETEX presence:{id} TTL "online" always, and have
        //     game-server's /start endpoint refresh SETEX with "in_match" on every server tick.
        //
        // Recommendation: Lua script for atomic precedence. Saves the round-trip.

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
    }

    public async ValueTask<PresenceStatus> GetStatusAsync(Guid playerId, CancellationToken ct)
    {
        var v = await _redis.GetDatabase().StringGetAsync($"presence:{playerId}");
        if (v.IsNullOrEmpty) return PresenceStatus.Offline;
        return v == "in_match" ? PresenceStatus.InMatch : PresenceStatus.Online;
    }

    public async ValueTask<IReadOnlyList<Guid>> GetOnlinePlayerIdsAsync(int take, CancellationToken ct)
    {
        // SCAN, not KEYS — KEYS is O(N) blocking; SCAN is O(N) cursored, ops-team-safe.
        // 25 results means a few hundred key reads at most given typical online-player counts;
        // an admin-panel-only path is allowed to be O(N) over the keyset.
        var server  = _redis.GetServer(_redis.GetEndPoints().Single());
        var results = new List<Guid>(take);
        await foreach (var key in server.KeysAsync(pattern: "presence:*", pageSize: 250).WithCancellation(ct))
        {
            var raw = (string?)key!;
            if (raw is null) continue;
            if (Guid.TryParse(raw["presence:".Length..], out var id))
                results.Add(id);
            if (results.Count >= take) break;
        }
        return results;
    }
}
```

### Pattern 2: ISessionLifecycleObserver port (Core → Presence)

**What:** Core publishes an `ISessionLifecycleObserver` port. `SessionCompleteService` (and the new start/abandon services) resolve `IEnumerable<ISessionLifecycleObserver>` and invoke them inside the existing transaction. Presence implements the observer to set/clear in-match.

**When to use:** Any cross-package event-style coupling where the publisher (Core/Sessions) must not depend on the subscriber (Presence). Mirrors the existing `IPostSessionCompleteHandler` pattern from Phase 4 Plan 04-05.

**Example:**
```csharp
// src/GameKit.Core/Services/ISessionLifecycleObserver.cs (NEW Phase 6)
public interface ISessionLifecycleObserver
{
    /// <summary>Called inside the ambient transaction after game_sessions.state transitions to Active.</summary>
    Task OnSessionStartedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);

    /// <summary>Called inside the ambient transaction after game_sessions.state transitions to Completed.</summary>
    Task OnSessionCompletedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);

    /// <summary>Called inside the ambient transaction after game_sessions.state transitions to Cancelled.</summary>
    Task OnSessionAbandonedAsync(Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
}

// src/GameKit.Presence/Services/PresenceSessionObserver.cs (NEW)
internal sealed class PresenceSessionObserver(IPresenceWriter writer) : ISessionLifecycleObserver
{
    public async Task OnSessionStartedAsync(Guid id, IReadOnlyList<Guid> participants, CancellationToken ct)
    {
        foreach (var p in participants) await writer.WriteInMatchAsync(p, ct);
    }
    public async Task OnSessionCompletedAsync(Guid id, IReadOnlyList<Guid> participants, CancellationToken ct)
    {
        foreach (var p in participants) await writer.WriteOnlineAsync(p, ct);  // refresh TTL with "online"
    }
    public async Task OnSessionAbandonedAsync(Guid id, IReadOnlyList<Guid> participants, CancellationToken ct)
    {
        foreach (var p in participants) await writer.ClearInMatchAsync(p, ct); // SET "online" w/ existing TTL
    }
}
```

### Pattern 3: OpenAPI document with bearer scheme + admin filter

**What:** Single `/openapi/v1.json` doc. Use `IOpenApiDocumentTransformer` to add the `bearerAuth` security scheme; use `IOpenApiOperationTransformer` to exclude admin routes. Per-package grouping is by `MapGroup().WithTags("auth")` etc. — tags already shipped in Phase 2-5 endpoint code.

**Example:**
```csharp
// src/GameKit.OpenApi/Builder/OpenApiBuilderExtensions.cs
public static IServiceCollection AddGameKitOpenApi(
    this IServiceCollection services,
    Action<GameKitOpenApiOptions>? configure = null)
{
    var opts = new GameKitOpenApiOptions();
    configure?.Invoke(opts);
    services.AddSingleton(opts);

    services.AddOpenApi(opts.DocumentName, o =>
    {
        o.AddDocumentTransformer<GameKitInfoTransformer>();
        o.AddDocumentTransformer<GameKitBearerSchemeTransformer>();
        o.AddOperationTransformer<GameKitAdminRouteFilter>();
    });
    return services;
}

public static IEndpointRouteBuilder MapGameKitOpenApi(this IEndpointRouteBuilder routes)
{
    routes.MapOpenApi($"{routes.ServiceProvider.GetRequiredService<GameKitOpenApiOptions>().MountPath}/{{documentName}}.json");
    return routes;
}

// src/GameKit.OpenApi/Transformers/GameKitAdminRouteFilter.cs
internal sealed class GameKitAdminRouteFilter : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext ctx, CancellationToken ct)
    {
        // Admin endpoints have ApiDescription.RelativePath starting with "admin/api/" or "admin/"
        // because we want them filtered OUT of the public doc (D-08).
        // But filtering at operation-transformer time means the path is ALREADY in the document;
        // operation transformer cannot remove a path. Use OpenApiOptions.ShouldInclude delegate INSTEAD.
        // (Operation transformer left here for response-decoration only.)
        return Task.CompletedTask;
    }
}

// CORRECTED — use ShouldInclude (the documented mechanism for admin-path filtering):
services.AddOpenApi("v1", o =>
{
    o.ShouldInclude = (desc) =>
    {
        var path = desc.RelativePath ?? string.Empty;
        // Drop /admin/api/* + /admin/* — D-08
        return !path.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);
    };
    o.AddDocumentTransformer<GameKitBearerSchemeTransformer>();
    o.AddDocumentTransformer<GameKitInfoTransformer>();
});

// src/GameKit.OpenApi/Transformers/GameKitBearerSchemeTransformer.cs
internal sealed class GameKitBearerSchemeTransformer(IAuthenticationSchemeProvider schemes) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext ctx, CancellationToken ct)
    {
        var registered = await schemes.GetAllSchemesAsync();
        if (!registered.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme)) return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Player JWT issued by /auth/login/*"
        };

        // Apply globally — but exempt anonymous endpoints (login/register/heartbeat-with-anon-auth)
        // by checking the ApiDescription metadata in an operation transformer (preferred over global).
        // For v1 we apply globally; the JWT-bearer-required endpoints already validate via [Authorize].
        foreach (var path in document.Paths.Values)
        foreach (var op in path.Operations.Values)
        {
            op.Security ??= new List<OpenApiSecurityRequirement>();
            op.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("bearerAuth", document)] = []
            });
        }
    }
}
```

[CITED: learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi — the `IAuthenticationSchemeProvider`-based `BearerSecuritySchemeTransformer` pattern]

### Pattern 4: EndpointDataSource-driven coverage contract test (D-09)

**What:** Resolve `IEnumerable<EndpointDataSource>` from the `WebApplicationFactory`'s service provider, flatten to all endpoints, extract `(METHOD, PATH)` tuples, filter out `/admin/*` (and the OpenAPI endpoint itself), assert each tuple appears in the generated OpenAPI document.

**Example:**
```csharp
// tests/GameKit.OpenApi.Integration.Tests/OpenApiCoverageTests.cs
public sealed class OpenApiCoverageTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory; // boot full TicTacToeDuel sample

    [Fact]
    public async Task Every_NonAdmin_Endpoint_Is_In_OpenApi_Document()
    {
        // 1) Enumerate registered endpoints
        using var scope = _factory.Services.CreateScope();
        var sources    = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var endpoints  = sources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null)
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var methodMeta = e.Metadata.GetMetadata<HttpMethodMetadata>();
                var method     = methodMeta?.HttpMethods.FirstOrDefault() ?? "GET";
                return (method, path: "/" + e.RoutePattern.RawText!.TrimStart('/'));
            })
            .Distinct()
            .ToList();

        // 2) Fetch the generated OpenAPI doc
        var client    = _factory.CreateClient();
        var json      = await client.GetStringAsync("/openapi/v1.json");
        using var doc = JsonDocument.Parse(json);
        var paths     = doc.RootElement.GetProperty("paths");

        // 3) Assert every (METHOD, PATH) is present
        var missing = new List<string>();
        foreach (var (method, path) in endpoints)
        {
            if (!paths.TryGetProperty(path, out var pathItem) ||
                !pathItem.TryGetProperty(method.ToLowerInvariant(), out _))
                missing.Add($"{method} {path}");
        }
        Assert.True(missing.Count == 0,
            $"OpenAPI doc missing {missing.Count} endpoint(s):\n  " + string.Join("\n  ", missing));
    }
}
```

[CITED: learn.microsoft.com/en-us/aspnet/core/fundamentals/routing — `EndpointDataSource.Endpoints` enumeration; meziantou.net listing all routes]

### Pattern 5: Roslyn incremental source generator reading MSBuild `$(Version)`

**What:** Compile-time generator emits `internal const string GameKitVersion = "X.Y.Z";` into every consuming GameKit.* assembly. Reads `$(Version)` via the `build_property.Version` analyzer-config key. Generator project targets `netstandard2.0` (mandatory for source generators), is wired with `<IsRoslynComponent>true</IsRoslynComponent>`, and is referenced as `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` (so generator runs but DLL is not shipped).

**Example:**
```csharp
// src/GameKit.Build/GameKitVersionGenerator.cs
[Generator]
public sealed class GameKitVersionGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1) Read MSBuild $(Version) — exposed by AnalyzerConfigOptionsProvider
        var version = context.AnalyzerConfigOptionsProvider.Select((p, _) =>
            p.GlobalOptions.TryGetValue("build_property.Version", out var v) ? v : "0.0.0");

        // 2) Read MSBuild $(AssemblyName) to namespace the marker correctly
        var asmName = context.CompilationProvider.Select((c, _) => c.AssemblyName ?? "Unknown");

        // 3) Combine and emit
        var combined = version.Combine(asmName);
        context.RegisterSourceOutput(combined, (spc, tuple) =>
        {
            var (ver, name) = tuple;
            // Only emit into GameKit.* assemblies (defense-in-depth — generator is
            // ProjectRef'd only from GameKit packages so this is a belt-and-braces check).
            if (!name.StartsWith("GameKit.", StringComparison.Ordinal)) return;
            var ns = $"{name}.Internal";
            var src = $$"""
                // <auto-generated/>
                // Emitted by GameKit.Build source generator.
                // Source of truth for OPS-04 / OPS-05 (CLAUDE.md §release train).
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

**Wiring (in `src/GameKit.Core/GameKit.Core.csproj` and every sibling):**
```xml
<ItemGroup>
  <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**Critical wiring at the GameKit.Build csproj level — expose `$(Version)` to the generator:**
```xml
<!-- src/GameKit.Build/GameKit.Build.csproj OR equivalent .props imported by consumers -->
<ItemGroup>
  <CompilerVisibleProperty Include="Version" />
</ItemGroup>
```

The consumer csproj that ProjectRefs the generator must publish `Version` as a compiler-visible property. The cleanest path: put `<CompilerVisibleProperty Include="Version" />` in `Directory.Build.props` so every `GameKit.*` consumer auto-exposes it.

[CITED: github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md — `CompilerVisibleProperty` for MSBuild → generator; thinktecture article — `OutputItemType="Analyzer"` pattern]

**Caveat:** MSBuild → editorconfig serialization may truncate complex values, but a SemVer string is well within safe bounds.

### Pattern 6: GameKitVersionAssertionHostedService (OPS-05)

**Example:**
```csharp
// src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs
internal sealed class GameKitVersionAssertionHostedService(ILogger<GameKitVersionAssertionHostedService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var gamekitAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("GameKit.", StringComparison.Ordinal) == true)
            .Where(a => a.GetName().Name != "GameKit.Build") // skip the source generator if it's loaded
            .ToList();

        var versionsByAsm = new Dictionary<string, string>();
        foreach (var asm in gamekitAssemblies)
        {
            // Look for {AssemblyName}.Internal.GameKitMarker
            var asmName    = asm.GetName().Name!;
            var markerType = asm.GetType($"{asmName}.Internal.GameKitMarker", throwOnError: false);
            if (markerType is null) continue;
            var field = markerType.GetField("GameKitVersion", BindingFlags.Public | BindingFlags.Static);
            if (field?.GetValue(null) is string ver) versionsByAsm[asmName] = ver;
        }

        var distinctVersions = versionsByAsm.Values.Distinct().ToList();
        if (distinctVersions.Count > 1)
        {
            throw new GameKitVersionMismatchException(versionsByAsm);
        }
        logger.LogInformation("GameKit version assertion passed: all {Count} GameKit.* assemblies report version {Version}.",
            versionsByAsm.Count, distinctVersions.SingleOrDefault());
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**OPS-05 caveat (Open Q2 resolution):** `AppDomain.CurrentDomain.GetAssemblies()` only sees assemblies that have been loaded. In typical .NET 10 hosting (`Host.CreateDefaultBuilder` + `ConfigureWebHostDefaults`), all referenced assemblies are loaded eagerly during `IServiceCollection` registration because the `AddXxx()` calls trigger module loads. To be **defensive**, the hosted service can eager-load `GameKit.*` assemblies by walking `Assembly.GetEntryAssembly()!.GetReferencedAssemblies()` and calling `Assembly.Load(name)` on any GameKit-named entries first. Plan-time call — recommend the defensive eager-load for safety; it costs <1ms.

### Pattern 7: GameKit.targets — Pack-time exact-pin emission (OPS-04)

**What:** A custom MSBuild target that runs during `dotnet pack` and rewrites every `ProjectReference` to a GameKit sibling into a `PackageReference` with `Version="[$(Version)]"` (square-bracket exact pin).

**Example:**
```xml
<!-- GameKit.targets at repo root, imported from Directory.Build.props -->
<Project>
  <Target Name="GameKitEmitSiblingPackageRefs"
          BeforeTargets="GenerateNuspec"
          Condition="'$(IsPackable)' == 'true'">
    <ItemGroup>
      <!-- Capture every GameKit sibling ProjectReference -->
      <_GameKitSiblingRef Include="@(ProjectReference)"
                          Condition="$([System.String]::Copy('%(Filename)').StartsWith('GameKit.'))" />
    </ItemGroup>

    <!-- For each sibling, emit a PackageReference pin into the generated nuspec via the
         GenerateNuspec target's input items. The exact mechanism varies by SDK version;
         see Pattern 7-alt below for the safer documented approach. -->
    <Message Importance="high"
             Text="GameKit: exact-pinning siblings %(_GameKitSiblingRef.Filename) → [$(Version)]" />
  </Target>
</Project>
```

**Pattern 7-alt (safer, documented):** The MSBuild research surfaced an important constraint: `PackageReference` items must be in `ItemGroup`s at evaluation time, not inside a target. Generating them dynamically inside a target won't flow to the nuspec correctly [CITED: github.com/dotnet/msbuild/discussions/11191].

The cleaner approach: **static `Update` attributes in `Directory.Build.targets`.** Every consumer csproj already declares the sibling `ProjectReference`s explicitly; we can layer a static `<PackageReference Update="GameKit.*" Version="[$(Version)]" />` policy that doesn't add references but enforces the version on any GameKit reference that does exist after `dotnet pack` converts ProjectReferences to PackageReferences.

```xml
<!-- GameKit.targets — Pattern 7-alt -->
<Project>
  <!-- Default NuGet behavior: dotnet pack converts ProjectReference siblings to PackageReference
       entries in the .nuspec with Version="$(PackageVersion)" (single value, NOT exact-pin syntax).
       We override this by setting the per-reference version via PrivateAssets/ItemDefinition. -->
  <ItemDefinitionGroup>
    <ProjectReference>
      <!-- This metadata is read by GenerateNuspec to set the converted PackageReference version. -->
      <PackageVersion>[$(Version)]</PackageVersion>
    </ProjectReference>
  </ItemDefinitionGroup>
</Project>
```

**Caveat:** The exact MSBuild metadata that controls the converted `Version` attribute in the generated `.nuspec` requires experimentation. The fallback (and lowest-risk) approach is a CI grep assertion against the produced `.nupkg`'s nuspec:

```bash
# CI step after dotnet pack:
for nupkg in artifacts/*.nupkg; do
  unzip -p "$nupkg" "*.nuspec" | grep -E 'id="GameKit\.' \
    | grep -vE 'version="\[[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(-[a-z0-9.\-]+)?\]"' \
    && { echo "WILDCARD PIN: $nupkg"; exit 1; }
done
```

**And the source-code CI guard (D-17):**
```bash
# Block ^ or * wildcard pins in any GameKit src csproj
! grep -rE 'Version="(\*|\^)' src/GameKit.*/*.csproj
```

[CITED: github.com/dotnet/msbuild/discussions/11191 — ProjectReference/PackageReference must be in evaluation-time ItemGroups; learn.microsoft.com/en-us/nuget/concepts/package-versioning — `[1.0.0]` exact-pin syntax]

### Pattern 8: dotnet new gamekit template (DIST-04)

**What:** `templates/GameKit.Templates/GameKit.Templates.csproj` packages template content under `content/GameKit.SampleGame/`. A `.template.config/template.json` defines symbols (`-n`, `--skip-*`) and `#if`-style conditional content blocks.

**Example template.json:**
```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "GameKit contributors",
  "classifications": ["GameKit", "Sample", "WebAPI"],
  "identity": "GameKit.SampleGame",
  "name": "GameKit Sample Game (TicTacToeDuel)",
  "shortName": "gamekit",
  "tags": { "language": "C#", "type": "project" },
  "sourceName": "GameKit.SampleGame",
  "preferNameDirectory": true,
  "symbols": {
    "skipAuth":        { "type": "parameter", "datatype": "bool", "defaultValue": "false", "description": "Omit GameKit.Auth wiring." },
    "skipRankings":    { "type": "parameter", "datatype": "bool", "defaultValue": "false", "description": "Omit GameKit.Rankings wiring." },
    "skipMatchmaking": { "type": "parameter", "datatype": "bool", "defaultValue": "false", "description": "Omit GameKit.Matchmaking wiring." },
    "skipPresence":    { "type": "parameter", "datatype": "bool", "defaultValue": "false", "description": "Omit GameKit.Presence wiring." }
  },
  "postActions": [
    {
      "description": "Generate dev RSA key pair for JWT signing.",
      "manualInstructions": [{ "text": "Run ./scripts/gen-test-rsa-pem.sh in the project root." }],
      "actionId": "3A7C4B45-1F5D-4A30-959A-51B88E82B5D2",
      "args": { "executable": "bash", "args": "./scripts/gen-test-rsa-pem.sh" },
      "continueOnError": true
    }
  ]
}
```

**Example conditional in Program.cs:**
```csharp
// Source: github.com/dotnet/templating/wiki/Conditions
//#if (!skipAuth)
gameKitBuilder.AddAuth(auth => { /* ... */ });
//#endif
//#if (!skipRankings)
gameKitBuilder.AddRankings(opts => { /* ... */ }).AddLadder("main", c => { /* ... */ });
//#endif
//#if (!skipMatchmaking)
gameKitBuilder.AddMatchmaking(opts => { /* ... */ }).AddLadder("tictactoe", l => { /* ... */ });
//#endif
//#if (!skipPresence)
gameKitBuilder.AddPresence();   // NEW Phase 6
//#endif
```

**csproj packaging:**
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
    <NoDefaultExcludes>true</NoDefaultExcludes> <!-- keep .template.config -->
  </PropertyGroup>

  <ItemGroup>
    <Content Include="content\**\*"
             Exclude="content\**\bin\**;content\**\obj\**" />
    <Compile Remove="**\*" />
  </ItemGroup>
</Project>
```

[CITED: learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package; github.com/dotnet/templating/wiki/Reference-for-template.json]

### Anti-Patterns to Avoid
- **Anti-pattern: Hand-rolling JWT bearer in the OpenAPI doc.** The `IAuthenticationSchemeProvider`-driven document transformer is the documented MS pattern; copy it verbatim. Manual security-scheme JSON is a maintenance trap.
- **Anti-pattern: Using `WebApplication.MapGet` for the heartbeat endpoint.** Use the existing `MapPost("/api/presence/heartbeat", ...)` minimal-API surface inside a dedicated `PresenceEndpoints.cs` to match Phase 2/4/5 layout.
- **Anti-pattern: Using `IConnectionMultiplexer.GetDatabase().StringSetAsync(key, value, expiry)` for the heartbeat write.** The base `StringSet` does not enforce the in-match precedence rule (D-03 + D-04). Use the Lua script in Pattern 1.
- **Anti-pattern: Using `IConnectionMultiplexer.GetServer().Keys()` (synchronous).** Use `KeysAsync()` with `pageSize` argument — the synchronous variant is blocking and not safe under load.
- **Anti-pattern: Filtering admin routes via `IOpenApiOperationTransformer`.** Operation transformers cannot remove operations from the document; use `OpenApiOptions.ShouldInclude` (the documented mechanism). [CITED: learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi — `ShouldInclude` delegate]
- **Anti-pattern: Targeting `net10.0` for the Roslyn source generator project.** Source generators MUST target `netstandard2.0` because they load into the compiler host (which targets netstandard2.0). [CITED: github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md]
- **Anti-pattern: Putting the source generator's csproj in `Directory.Packages.props` CPM.** The generator's `Microsoft.CodeAnalysis.CSharp` pin is project-local because the version pin is tightly coupled to the generator's API surface; keep it inline in `src/GameKit.Build/GameKit.Build.csproj`. Optionally exempt the project via `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` at the csproj level.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OpenAPI document generation | Hand-written JSON or YAML | `Microsoft.AspNetCore.OpenApi` 10.0.8 | First-party, transformer pipeline, EndpointDataSource integration |
| Bearer security scheme injection | Manual JSON assembly | `IAuthenticationSchemeProvider`-driven `IOpenApiDocumentTransformer` | The documented MS pattern; survives API surface changes |
| Admin-route filtering | Operation transformer | `OpenApiOptions.ShouldInclude` delegate | Operation transformer can decorate but not remove |
| Version stamp into assemblies | `[assembly: AssemblyMetadata("GameKitVersion","X.Y.Z")]` runtime parsing | Roslyn `IIncrementalGenerator` emitting `const` | Const is JIT-intern-pooled; runtime parsing burns IO on every call |
| Source-generator/MSBuild data plumbing | `AdditionalFiles` hacks | `CompilerVisibleProperty Include="Version"` in Directory.Build.props | First-class MSBuild → AnalyzerConfigOptionsProvider plumbing |
| dotnet new templating | Custom shell script copying files | `Microsoft.TemplateEngine.Authoring.Templates` `template.json` + `<PackageType>Template</PackageType>` | First-party; `dotnet new install` distribution; `#if` conditionals |
| Presence sweeper / cleanup background job | Custom IHostedService | Redis TTL alone | Keys auto-expire; sweeper is wasted compute (D-04, D-01 — single key per player) |
| Heartbeat rate limiting | Custom Polly policy | None (D-05 — explicitly no rate limit) | Single SETEX is cheaper than enqueue; runaway clients hit Kestrel queue |
| Test harness for cross-process GameServer call | Docker-in-Docker via Testcontainers | `Process.Start` on the built console exe + `WebApplicationFactory` for the web app | 10x faster CI; matches Phase 3-5 pattern |
| Pack-time PackageReference rewriting | Custom shell post-pack | MSBuild `ItemDefinitionGroup` + CI grep guard | MSBuild's own evaluation honors `Version="[X.Y.Z]"` syntax natively |

**Key insight:** Phase 6 is largely about **assembling existing first-party primitives in the right order** — `Microsoft.AspNetCore.OpenApi`, `IIncrementalGenerator`, `template.json`, MSBuild Pack. No phase deserves custom infra; the patterns above are all "configure and wire" rather than "implement from scratch."

## Runtime State Inventory

> Phase 6 is greenfield (new packages + new endpoints + new tests + new docs). Runtime state already in production:
>
> | Category | Items Found | Action Required |
> |----------|-------------|------------------|
> | Stored data | Redis already has `gk:matchmaking:*` keys from Phase 5; Postgres has `players`/`game_sessions`/`auth_*`/`admin_*`/`ladders`/`matchmaking_*` tables; no `presence:*` keys exist yet | None — Phase 6 ADDS new Redis key prefix `presence:*`; no conflict |
> | Live service config | n/a — GameKit is a library, consumers run their own services | None |
> | OS-registered state | n/a | None |
> | Secrets / env vars | `ConnectionStrings:GameKit` (Postgres), `ConnectionStrings:Redis`, `GameKit:Auth:Jwt:*` (existing). Phase 6 introduces NO new secrets. The GameServer console uses the SAME Redis + a `gamekit_reader` Postgres connection — both already in `01-roles.sql` | TicTacToeDuel.GameServer/appsettings.json adds `ConnectionStrings:GameKit` w/ `gamekit_reader_dev` password (matches docker init script) |
> | Build artifacts / installed packages | Generated `Internal/GameKitMarker.g.cs` lands in `obj/` per package — must be excluded from XML doc warnings (it's auto-generated; CS1591 won't trigger because it's `internal`) | None — `internal` + `<auto-generated/>` header suppress CS1591 by convention |

**Nothing found in 4 of 5 categories** — verified by code inspection of CONTEXT.md, sample-app appsettings, and existing service registration. Phase 6 introduces no rename / refactor / migration concerns.

## Common Pitfalls

### Pitfall 1: Source generators don't see `$(Version)` until `CompilerVisibleProperty` exposes it
**What goes wrong:** The generator runs but `AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.Version", out var v)` returns `false`, so the generator emits `GameKitVersion = "0.0.0"` (the fallback). Production builds end up with a useless version stamp.
**Why it happens:** MSBuild does not auto-publish every property to the generator; you must opt-in per property via `<CompilerVisibleProperty Include="Version" />`.
**How to avoid:** Put `<CompilerVisibleProperty Include="Version" />` in **`Directory.Build.props`** (not in individual csprojs) so every GameKit.* package inherits it. Add an integration test that builds a synthetic test assembly and asserts `GameKitMarker.GameKitVersion != "0.0.0"`.
**Warning signs:** `OPS04_VersionStampedAcrossPackagesTests` shows every assembly reporting `"0.0.0"`.

### Pitfall 2: MinVer `$(Version)` is empty before the `MinVer` target runs
**What goes wrong:** Source generator runs at compile time, but `MinVer`'s `$(Version)` resolution happens during `BeforeBuild`. If the generator's `AnalyzerConfigOptionsProvider` snapshots properties before MinVer fires, the version is blank.
**Why it happens:** MSBuild target ordering — analyzer-config options are baked from properties at the start of `CoreCompile`; MinVer must run earlier.
**How to avoid:** MinVer 7 publishes `$(Version)` during `GenerateAssemblyInfo` which is in `BeforeBuild` — happens before `CoreCompile`. Should be fine. Add a smoke test: build `GameKit.Core` and grep the emitted `GameKitMarker.g.cs` (located in `obj/Debug/net10.0/generated/GameKit.Build/...`) for the live version string.
**Warning signs:** Generator emits `"0.0.0-alpha.0"` (MinVer's default-pre-release fallback) on what should be a tagged release build.

### Pitfall 3: `AppDomain.CurrentDomain.GetAssemblies()` returns only loaded assemblies
**What goes wrong:** OPS-05 assertion runs in `IHost.StartAsync` but `GameKit.Matchmaking` hasn't been touched yet (no `/api/mm/*` request has been served), so its assembly is not loaded, so the version mismatch goes undetected.
**Why it happens:** .NET lazy-loads assemblies on first reference. `Host.CreateDefaultBuilder` + `AddXxx()` calls reference the assembly via `ProjectReference` which IS loaded eagerly, BUT only because the `AddMatchmaking()` extension method requires it. If a consumer never calls `AddMatchmaking()`, the assembly never loads — and that's correct (the package isn't installed for them).
**How to avoid:** Eager-load only assemblies that are referenced by the entry assembly: walk `Assembly.GetEntryAssembly()!.GetReferencedAssemblies()`, filter to GameKit.*, call `Assembly.Load()` on each. This catches any package that's pinned as a `<PackageReference>` even if no `Add*()` call was made.
**Warning signs:** Test that pins `GameKit.Matchmaking` 1.0.0 alongside `GameKit.Core` 1.0.1, calls only `AddGameKit()`, and expects the mismatch assertion to throw — but it passes silently.

### Pitfall 4: `OpenApiOptions.ShouldInclude` filters at ApiDescription level, but `WithGroupName` is the OOTB filter
**What goes wrong:** Setting `o.ShouldInclude = desc => !desc.RelativePath?.StartsWith("admin/") ?? true` works, but if a consumer adds `WithGroupName("v2")` to a Phase 6 endpoint, the default `ShouldInclude` (which checks `GroupName == DocumentName`) is bypassed — admin filtering breaks v2 separation.
**Why it happens:** `ShouldInclude` is a single delegate; setting it replaces the default behavior wholesale.
**How to avoid:** Compose with the default: `o.ShouldInclude = desc => (desc.GroupName == null || desc.GroupName == "v1") && !desc.RelativePath?.StartsWith("admin/") ?? true;`. Document this in the `GameKitOpenApiOptions.ShouldInclude` configuration knob if we expose one.
**Warning signs:** Consumer reports endpoints with `WithGroupName` set are vanishing from the doc.

### Pitfall 5: `dotnet new gamekit` post-action runs `gen-test-rsa-pem.sh` which fails on Windows
**What goes wrong:** The post-action calls bash; Windows users without WSL get an error.
**How to avoid:** Set `continueOnError: true` AND `manualInstructions` so Windows users see a clear "run this command yourself" prompt. Document in `templates/GameKit.Templates/README.md`. Optionally ship a `gen-test-rsa-pem.ps1` PowerShell equivalent and use `template.json` `actionId: "AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C"` (run-script with OS detection).
**Warning signs:** Windows newcomers report `bash: command not found` on first template use.

### Pitfall 6: Razor Class Library + dotnet pack converts ProjectReferences to PackageReferences but loses transitive grants
**What goes wrong:** `GameKit.Admin.UI` has a ProjectReference to `GameKit.Auth`. When packed, the .nuspec lists `GameKit.Auth` as a `<dependency>` — but the consumer's restore graph may pull a DIFFERENT version of `GameKit.Auth` if not exact-pinned. Result: GameKit.Admin.UI 1.0.0 ends up with GameKit.Auth 0.9.5 (an older cached package).
**How to avoid:** OPS-04 D-17 exact-pin solves this — `Version="[1.0.0]"` blocks any other version from satisfying. The CI grep guard catches drift.
**Warning signs:** OPS-04 test that pins mismatched versions in a synthetic consumer csproj should fail restore with NU1605 (downgrade detected).

### Pitfall 7: `Microsoft.AspNetCore.OpenApi` does NOT auto-discover `[Authorize]` for security requirements
**What goes wrong:** `[Authorize]` on an endpoint does not auto-emit `security: [{bearerAuth: []}]` in the doc. We must emit it explicitly via document transformer (Pattern 3 above).
**How to avoid:** The Pattern 3 transformer applies bearerAuth globally to all operations, then we should add an operation transformer that REMOVES the requirement for `[AllowAnonymous]` endpoints (login, register, OAuth callbacks). Net effect: doc accurately reflects auth requirements.
**Warning signs:** Doc shows `bearerAuth` on `/auth/login/guest` even though that endpoint accepts anonymous calls.

### Pitfall 8: `tests/GameKit.Distribution.Integration.Tests/` reusing Testcontainers fixtures must NOT include the docker init script
**What goes wrong:** DIST-02 tests need the 3-role bootstrap from `docker/postgres/init/01-roles.sql`, but the existing `PostgresFixture` in `tests/GameKit.TestFixtures/PostgresFixture.cs` uses a vanilla Postgres image with no init scripts. Naively reusing it means `gamekit_reader` does not exist.
**How to avoid:** New `DistributionIntegrationFixture` mounts the `docker/postgres/init/` directory as `/docker-entrypoint-initdb.d/` via Testcontainers' `WithBindMount()` (or copies the init script via `WithResourceMapping()`). Verify the test asserts `gamekit_reader` LOGIN succeeds before attempting the INSERT-denied test.
**Warning signs:** Test fails at "role gamekit_reader does not exist" before reaching the INSERT assertion.

### Pitfall 9: `EndpointDataSource` enumeration picks up endpoints registered by middleware too
**What goes wrong:** The OpenAPI coverage test enumerates `EndpointDataSource.Endpoints` and gets endpoints registered for OpenAPI ITSELF (`/openapi/v1.json`), antiforgery validation, and other middleware. These show up as orphan endpoints with no operation in the OpenAPI doc.
**How to avoid:** Filter the enumeration to `RouteEndpoint`s with `RoutePattern.RawText != null` AND a non-null `HttpMethodMetadata` AND not starting with `/openapi` or `/_blazor` (Blazor Server) or `/admin`. Document the filter list in the test (it's a coverage scope statement).
**Warning signs:** Test reports `/openapi/v1.json` as "missing from OpenAPI doc."

### Pitfall 10: ROADMAP SC#1 wording contradicts Core XML doc
**What goes wrong:** ROADMAP says `/abandon` moves players to in-match. Core XML doc says `/start` moves to in-match. CONTEXT.md flags this as a typo. If we ship Phase 6 without reconciling, downstream operator docs cite contradictory sources.
**How to avoid:** Plan-time first task is a one-line ROADMAP.md SC#1 wording correction commit (CONTEXT.md `<specifics>` already drafted the authoritative wording). Pair the doc fix with the code review that says "Phase 6 SC#1 is the new wording from CONTEXT.md."

## Code Examples

(See Patterns 1-8 above — each pattern includes a complete code example with source attribution.)

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Swashbuckle.AspNetCore for OpenAPI | `Microsoft.AspNetCore.OpenApi` (first-party) | .NET 9 default templates dropped Swashbuckle | Phase 6 standardizes on first-party; matches "install only what you need" |
| Runtime `AssemblyInformationalVersion` parsing | Compile-time Roslyn source generator emitting `const` | .NET 6+ (IIncrementalGenerator GA) | Zero-allocation, JIT-intern-pooled, fail-fast at compile |
| `<PackageReference Include="Foo" Version="1.0.0" />` literal | CPM in `Directory.Packages.props` + per-package exact-pin `[X.Y.Z]` for siblings | .NET 6+ CPM + NuGet 5.x version-range syntax | Phase 6 leverages both for coordinated release train |
| Hangfire / Quartz for periodic jobs | `BackgroundService` + Polly | CLAUDE.md decision §1 | Phase 6 follows existing pattern (no periodic jobs needed — Redis TTL is the sweeper) |
| Hand-rolled CLI templating | `template.json` + `<PackageType>Template</PackageType>` + `dotnet new install` | .NET Core 3.0 GA | Phase 6 uses first-party path |

**Deprecated/outdated:**
- **`WithOpenApi()` extension on endpoints** is .NET 7/8-era; .NET 9/10 prefer the `AddOpenApi()` + transformer pipeline. Existing endpoints in Phase 2-5 do NOT call `WithOpenApi()` — they just rely on `MapGroup().WithTags(...)` which IS the supported .NET 10 path.
- **`Microsoft.Extensions.ApiDescription.Server`** is for build-time OpenAPI generation (committing the spec to source control). We default to runtime generation per CONTEXT D-07 — build-time is an optional follow-up if consumers ask.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Moq 4.20.72 + Testcontainers.PostgreSql 4.11.0 + Testcontainers.Redis 4.11.0 |
| Config file | `tests/Directory.Build.props` + per-project `xunit.runner.json` |
| Quick run command | `dotnet test tests/GameKit.Presence.Tests/ -c Debug --no-build` (unit-only, per package) |
| Full suite command | `dotnet test` at repo root (all test csprojs) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PRES-01 | `GameKit.Presence` builds + appears in `bin/` as standalone NuGet | unit (smoke) | `dotnet test tests/GameKit.Presence.Tests/ --filter FullyQualifiedName~SmokeTests` | ❌ Wave 0 |
| PRES-02 | `RedisPresenceProvider` implements `IPresenceProvider` contract | unit (mocked Redis) | `dotnet test tests/GameKit.Presence.Tests/ --filter FullyQualifiedName~RedisPresenceProviderTests` | ❌ Wave 0 |
| PRES-03 | Heartbeat writes Redis key + 30s TTL | integration (Testcontainers Redis) | `dotnet test tests/GameKit.Presence.Integration.Tests/ --filter HeartbeatWritesKeyWithTtl` | ❌ Wave 0 |
| PRES-04 | Status states reflect Redis state (online/offline/in_match) | integration | `dotnet test tests/GameKit.Presence.Integration.Tests/ --filter StatusReflectsRedisState` | ❌ Wave 0 |
| PRES-05 | `/api/sessions/{id}/abandon` clears in-match marker; heartbeat does NOT downgrade in-match | integration (full WebApplicationFactory) | `dotnet test tests/GameKit.Presence.Integration.Tests/ --filter AbandonClearsInMatchMarker` | ❌ Wave 0 |
| PRES-06 | Admin Presence panel renders Top-25 grid + degrades when GameKit.Presence absent | bUnit + integration | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter PresencePanelRendersTop25` | ❌ Wave 0 |
| OPEN-01 | `/openapi/v1.json` covers every non-admin endpoint | integration (`EndpointDataSource` contract) | `dotnet test tests/GameKit.OpenApi.Integration.Tests/ --filter OpenApiCoverage` | ❌ Wave 0 |
| DIST-02 | `gamekit_reader` denied INSERT into `gamekit.game_sessions` (Postgres `42501`) | integration (Testcontainers w/ 3-role bootstrap) | `dotnet test tests/GameKit.Distribution.Integration.Tests/ --filter GamekitReaderInsertDenied` | ❌ Wave 0 |
| DIST-03 | `dotnet new gamekit -n MyGame` produces buildable + bootable app + GameServer console | integration (template install + dotnet build + WebApplicationFactory) | `dotnet test tests/GameKit.Distribution.Integration.Tests/ --filter TemplateSampleGameSmoke` | ❌ Wave 0 |
| DIST-04 | Template NuGet package contains expected files + template.json valid | unit (zip-inspect) | `dotnet test tests/GameKit.Distribution.Integration.Tests/ --filter TemplatePackageShape` | ❌ Wave 0 |
| DIST-05 | `docs/ops/*.md` files exist + link-check passes | docs (CI shell step, not xUnit) | `find docs/ops/*.md \| xargs markdown-link-check` | manual-only (no automated test framework for docs link validation in repo today) |
| DIST-06 | CS1591 enforced; no `<NoWarn>1591` overrides exist | shell (CI grep) | `! grep -rE '<NoWarn>.*1591' src/` | manual (grep) |
| OPS-04 | All 6+1 packages stamp same MinVer-derived version into `GameKitMarker.GameKitVersion` | integration (assembly-inspect) | `dotnet test tests/GameKit.Distribution.Integration.Tests/ --filter VersionStampedAcrossPackages` | ❌ Wave 0 |
| OPS-05 | `GameKitVersionMismatchException` thrown when synthetic assembly reports diverging version | integration (load synthetic assembly + start host) | `dotnet test tests/GameKit.Distribution.Integration.Tests/ --filter VersionMismatchAssertionThrows` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/GameKit.{Phase6Package}.Tests/ -c Debug --no-build` (the unit test project for the package being edited)
- **Per wave merge:** `dotnet test --filter "Category!=LoadTest"` (skip the 1k-ticket load test from Phase 5)
- **Phase gate:** `dotnet test` at repo root — all xUnit projects green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/GameKit.Presence.Tests/` — Moq-based unit tests for `RedisPresenceProvider` precedence rules
- [ ] `tests/GameKit.Presence.Integration.Tests/` — Testcontainers Redis + WebApplicationFactory for heartbeat + abandon flows
- [ ] `tests/GameKit.OpenApi.Integration.Tests/` — `EndpointDataSource` contract test (D-09) + bearer scheme test + admin filter test
- [ ] `tests/GameKit.Distribution.Integration.Tests/` — DIST-02 + DIST-03 + DIST-04 + OPS-04 + OPS-05 + OPS-06
- [ ] `tests/GameKit.TestFixtures/DistributionIntegrationFixture.cs` + collection definition — Postgres + Redis composite WITH 3-role init script bound

## Security Domain

> `security_enforcement: true` (default — not explicitly disabled in `.planning/config.json`).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | JWT bearer on `/api/presence/heartbeat` + `/api/sessions/{id}/start` (service-token scheme) + `/api/sessions/{id}/abandon` (service-token scheme); already-shipped Phase 2 + Phase 4 patterns |
| V3 Session Management | partial | Heartbeat refreshes Redis TTL; no session state created beyond the existing JWT |
| V4 Access Control | yes | `gamekit_reader` Postgres role REVOKE INSERT/UPDATE/DELETE — DIST-02 test asserts; `/admin/api/*` filtered from public OpenAPI doc (D-08); admin endpoints already gated by `gamekit:admin:login` cookie scheme + CSRF |
| V5 Input Validation | yes | Heartbeat body = empty `{}`; no fields to validate beyond JWT-extracted `sub`. `/sessions/{id}/start` + `/abandon` request bodies need `FluentValidation` validators per existing Phase 4 pattern |
| V6 Cryptography | no | Phase 6 introduces no new crypto. JWT signing is Phase 2's `JwtIssuer`. |
| V11 Business Logic | yes | In-match precedence rule (heartbeat MUST NOT downgrade in-match) is a business-logic invariant enforced via Lua script |
| V14 Configuration | yes | `GameKit.targets` exact-pin enforcement is a supply-chain hardening control; CI wildcard-pin guard blocks accidental drift |

### Known Threat Patterns for {stack}

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Player floods heartbeat endpoint | DoS | None at GameKit layer (D-05); Kestrel queue + consumer-side rate-limit middleware (operator's choice) |
| Player forges in-match status via heartbeat | Tampering / EoP | Heartbeat endpoint only ever writes `"online"`; in-match is set only by service-token-authenticated `/api/sessions/{id}/start` (game-server-authoritative — D-03) |
| Reader role escalation via SQL injection | Tampering | Npgsql parameterized queries (already enforced repo-wide); DIST-02 test pins the GRANT model |
| OpenAPI doc leaks admin endpoint shape | Information disclosure | `ShouldInclude` filters `/admin/*` (D-08); contract test in `OpenApiAdminRouteExclusionTests` |
| Source generator emits malicious code on consumer machines | Tampering / Supply chain | `GameKit.Build` is ProjectRef-only, never NuGet-published; generator source is GPL + auditable in-tree |
| Wildcard sibling pin in nuspec opens consumer to bait-and-switch | Tampering / Supply chain | `GameKit.targets` exact-pin enforcement (D-17) + CI grep guard |
| Mismatched GameKit package versions cause runtime divergence | EoP / Integrity | `GameKitVersionAssertionHostedService` fail-fast at startup (D-16) |
| Template post-action runs arbitrary bash from package | Tampering / EoP | Post-actions in `template.json` are sandboxed by the dotnet templating engine; the included `gen-test-rsa-pem.sh` lives inside the template repo and is human-auditable; users can decline post-actions via `--no-restore` |
| Reader connection string leaks to public OpenAPI doc | Info disclosure | Connection strings are NEVER in any endpoint response body; doc only describes routes, parameters, and response shapes |

### Project Constraints (from CLAUDE.md)

- **GPL license:** every new source file gets the SPDX header `// SPDX-License-Identifier: GPL-3.0-or-later` + `// Copyright (c) 2026 GameKit contributors`. Apply to source generator output too (header inside the `<auto-generated/>` comment block).
- **No proprietary deps, no telemetry, no phone-home:** Microsoft.AspNetCore.OpenApi 10.0.8 is MIT, Microsoft.CodeAnalysis.CSharp is MIT — both GPL-compatible. No new SaaS deps.
- **Self-hosted only:** `dotnet new gamekit` produces a self-hostable app; no auth-server callbacks to Microsoft, no analytics injection.
- **.NET 10 LTS, ASP.NET Core 10, EF Core 10.0.6, Npgsql 10.0.1, StackExchange.Redis 2.8.41, MudBlazor 9.3.0, MinVer 7.0.0:** all already pinned; Phase 6 adds Microsoft.AspNetCore.OpenApi 10.0.8 + (optionally) Microsoft.CodeAnalysis.CSharp 4.13.0 to the generator project.
- **Per-package migrations boundary:** `GameKit.Presence` has NO EF entities (Redis-only per PRES-01) — therefore NO migration, NO advisory-lock key, NO ExcludeFromMigrations enumeration. `GameKit.OpenApi` also has NO data layer.
- **Public API discipline — XML doc comments on every public API:** DIST-06 audit + CS1591 already enforced.
- **`GameKit.Presence` package boundary:** Core's `IPresenceProvider` (read-only) + new `IPresenceWriter` (write-only, lives in `GameKit.Presence` itself per cross-cutting decision in Pattern 1) keep the public surface minimal. Builder + endpoints are public; Redis driver internals are `internal sealed`.

### NEW: required ports / hosting-service additions in Core

- `src/GameKit.Core/Services/ISessionLifecycleObserver.cs` (NEW public interface)
- `src/GameKit.Core/Services/GameKitVersionMismatchException.cs` (NEW public exception)
- `src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs` (NEW internal sealed; registered by AddGameKit)
- `src/GameKit.Core/Services/SessionCompleteService.cs` (MODIFY — inject `IEnumerable<ISessionLifecycleObserver>`, fire `OnSessionCompletedAsync` inside Commit)
- `src/GameKit.Core/Http/SessionEndpoints.cs` (MODIFY — add `MapPost("/{id}/start", ...)` + `MapPost("/{id}/abandon", ...)`)
- `src/GameKit.Core/Services/ISessionStartService.cs` + `ISessionAbandonService.cs` (NEW — mirror `ISessionCompleteService` pattern; orchestrate state transition + fire observers)

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | All build steps | ✓ | 10.0.106 (pinned via `global.json`) | — |
| Docker daemon | Testcontainers (Postgres + Redis) | (assumed; matches Phase 1-5 baseline) | — | — |
| `bash` for template post-actions | DIST-04 `gen-test-rsa-pem.sh` | ✓ on Linux/macOS; ⚠ on Windows without WSL | — | `manualInstructions` in template.json prompt user to run manually |
| `npm` / `markdown-link-check` for DIST-05 docs link validation | docs/ops/*.md | (optional) | — | Manual review in PR checklist |
| `unzip` / `zip` for DIST-04 template package shape test | TemplatePackageShape integration test | ✓ standard on every CI | — | — |

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** Windows bash for post-action (fallback: `manualInstructions`).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Microsoft.AspNetCore.OpenApi` 10.0.8 is the right pin and NOT in shared framework | Standard Stack | If wrong, adds an unnecessary pin OR misses a needed pin; mitigated by `dotnet add package` confirmation at execution time |
| A2 | `Microsoft.CodeAnalysis.CSharp` 4.13.0 is the conservative right pin for the source generator | Standard Stack | If too old, generator API differences cause compile errors; if too new, may demand SDK upgrade. Easily fixed at execution time. |
| A3 | `OpenApiOptions.ShouldInclude` is the right mechanism for admin-route filtering (NOT operation transformer) | Pattern 3 anti-pattern note | Verified via MS docs; if wrong, admin endpoints appear in public doc — caught by `OpenApiAdminRouteExclusionTests` |
| A4 | MinVer's `$(Version)` is published before `CoreCompile` so source generator sees it | Pitfall 2 | If wrong, all stamps default to "0.0.0"; caught by `VersionStampedAcrossPackages` test |
| A5 | Static `<ItemDefinitionGroup>` `PackageVersion` metadata is the right way to enforce exact-pin in nuspec (Pattern 7-alt) | Pattern 7 | Documented uncertainty — the MSBuild research surfaced ambiguity. Fallback: CI grep guard against produced nuspec is the lowest-risk anchor. Plan should include that grep guard as the PRIMARY enforcement and treat `ItemDefinitionGroup` as a defense-in-depth |
| A6 | `AppDomain.CurrentDomain.GetAssemblies()` plus eager-loading via `GetReferencedAssemblies()` covers every GameKit.* assembly the consumer has pinned | Pattern 6 + Pitfall 3 | If a consumer dynamically loads GameKit.Matchmaking after `IHost.StartAsync` (unusual), the assertion runs against an incomplete set. Document as a known limitation: assertion runs only against entry-assembly references at startup |
| A7 | Template post-action `actionId: 3A7C4B45-...` is the correct GUID for "run script" | Pattern 8 | If wrong, template install fails. Verify at execution time via `dotnet new install` of the local package. |
| A8 | The CONTEXT.md claim that GameKit.OpenApi is the 7th package matches the MinVer release train (so OPS-04 covers it too) | A-Map | Need to confirm at plan time that ROADMAP "all 6 packages" includes the new GameKit.OpenApi as the 7th — D-15 implicitly does (`Phase 6: 6 packages (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI)` — wording in ROADMAP SC#5). Plan must add the 7th and propose a one-line ROADMAP fix. |
| A9 | `gamekit_reader` test fixture can bind-mount `docker/postgres/init/01-roles.sql` via Testcontainers | Pitfall 8 + DIST-02 | Verified by Phase 1 docker-compose pattern; Testcontainers supports `WithBindMount` and `WithResourceMapping`. |
| A10 | Sample-app `Program.cs` `.AddPresence()` chain compiles without breaking existing TicTacToeDuel functionality | DIST-03 + sample modify | Low risk — fluent chain is additive; existing Phase 2-5 services unaffected. |

## Open Questions (RESOLVED)

1. **OQ1: Should GameKit ship a Swagger UI or just the JSON doc?**
   - What we know: CONTEXT D-07 says JSON only; Swashbuckle is officially deprecated from .NET 9/10 default templates; Scalar is MIT-licensed and would be GPL-compatible.
   - What's unclear: whether consumers will demand a built-in UI vs being told to `dotnet add package Scalar.AspNetCore` themselves.
   - **Recommendation:** Stay with JSON-only for v1 (matches CONTEXT D-07). Document the consumer path: "to add Scalar UI, run `dotnet add package Scalar.AspNetCore` and `app.MapScalarApiReference()`." Add a sentence to `docs/ops/README.md` pointing at Scalar + Swagger UI options.

2. **OQ2: Should `GameKitVersionAssertionHostedService` eager-load referenced GameKit.* assemblies?**
   - What we know: lazy loading means OPS-05 may miss packages not yet touched.
   - **Recommendation:** YES — eager-load via `Assembly.GetEntryAssembly()!.GetReferencedAssemblies()` filtered to GameKit.*. Cost <1ms; safety win is significant. Document as Pitfall 3 prevention.

3. **OQ3: Should `GameKit.Build` be in `Directory.Packages.props` CPM or local-pin?**
   - What we know: CPM applies to all csprojs by default; source generator csprojs have unusual constraints (netstandard2.0, special analyzer types).
   - **Recommendation:** Set `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` on `src/GameKit.Build/GameKit.Build.csproj` and pin `Microsoft.CodeAnalysis.CSharp` 4.13.0 inline. Reasoning: the version is tightly coupled to the generator API; should NOT change for runtime reasons. Document in the csproj header.

4. **OQ4: DIST-03 template-smoke-test — `Process.Start` GameServer or `Task.Run(GameServer.Main)`?**
   - What we know: `Process.Start` matches real production topology (separate processes); `Task.Run` is faster but mocks the process boundary.
   - **Recommendation:** `Process.Start` for the canonical SC#4 test (proves cross-process HTTP works); add a SEPARATE faster `Task.Run`-based smoke for per-commit feedback if CI times bite.

5. **OQ5: Is `ISessionLifecycleObserver` the right abstraction, or should we extend the existing `IPostSessionCompleteHandler`?**
   - What we know: `IPostSessionCompleteHandler` only fires after `/complete` (Phase 4). Phase 6 needs hooks for `/start` + `/abandon` too.
   - **Recommendation:** New `ISessionLifecycleObserver` interface with three methods (`OnSessionStartedAsync`, `OnSessionCompletedAsync`, `OnSessionAbandonedAsync`). Keep `IPostSessionCompleteHandler` for backward compatibility (Rankings already implements it). PresenceSessionObserver implements `ISessionLifecycleObserver`. Service registration: both interfaces are independently scannable via Scrutor. Document the partial overlap in XML doc.

## Sources

### Primary (HIGH confidence)
- [learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi (v10)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi) — `AddOpenApi()` / `MapOpenApi()` / `ShouldInclude` / multiple documents / build-time generation
- [learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi (v10)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi) — `IOpenApiDocumentTransformer` / `BearerSecuritySchemeTransformer` / `IOpenApiOperationTransformer`
- [learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata) — `ExcludeFromDescription()` / `WithTags()` / route group tags
- [github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md) — `IIncrementalGenerator` patterns, `CompilerVisibleProperty`, `AnalyzerConfigOptionsProvider`
- [github.com/dotnet/templating/wiki/Reference-for-template.json](https://github.com/dotnet/templating/wiki/Reference-for-template.json) — template.json schema
- [github.com/dotnet/templating/wiki/Conditions](https://github.com/dotnet/templating/wiki/Conditions) — `#if (...)` / `#endif` conditional content
- [learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package) — template package authoring
- [learn.microsoft.com/en-us/nuget/concepts/package-versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning) — `[X.Y.Z]` exact-pin syntax
- [nuget.org/packages/Microsoft.AspNetCore.OpenApi](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi) — 10.0.8 verified GA, NOT shared framework
- `src/GameKit.Core/Services/IPresenceProvider.cs` — locked Phase 1 contract
- `06-CONTEXT.md` — all D-01..D-18 decisions
- `CLAUDE.md` — license + stack + no-SaaS constraints

### Secondary (MEDIUM confidence)
- [meziantou.net/list-all-routes-in-an-asp-net-core-application.htm](https://www.meziantou.net/list-all-routes-in-an-asp-net-core-application.htm) — `IEnumerable<EndpointDataSource>` enumeration pattern
- [thinktecture.com/en/net/roslyn-source-generators-introduction/](https://www.thinktecture.com/en/net/roslyn-source-generators-introduction/) — `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` pattern
- [medium.com/tilt-engineering/redis-powered-presence-from-heartbeat-to-persistent-websocket-0455c03487a8](https://medium.com/tilt-engineering/redis-powered-presence-from-heartbeat-to-persistent-websocket-0455c03487a8) — Redis SETEX heartbeat pattern (verified against MS Learn StackExchange.Redis docs)
- [github.com/dotnet/msbuild/discussions/11191](https://github.com/dotnet/msbuild/discussions/11191) — `ProjectReference`/`PackageReference` must be in evaluation-time `ItemGroup`s (NOT inside Targets)
- [medium.com/@sidharth.cp34/openapi-swagger-enhancements-in-asp-net-core-10-the-complete-2025-guide-2fa6da93a7fb](https://medium.com/@sidharth.cp34/openapi-swagger-enhancements-in-asp-net-core-10-the-complete-2025-guide-2fa6da93a7fb) — .NET 10 OpenAPI behavior overview

### Tertiary (LOW confidence — verified or supplementary)
- [servicestack.net/posts/openapi-net10](https://servicestack.net/posts/openapi-net10) — Scalar + Swagger UI options for .NET 10 (informational; out-of-scope for v1)
- [oneuptime.com/blog/post/2026-01-21-redis-presence-detection/view](https://oneuptime.com/blog/post/2026-01-21-redis-presence-detection/view) — presence design pattern primer (cross-verified)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Microsoft.AspNetCore.OpenApi 10.0.8 verified GA via nuget.org; correction made re: NOT-shared-framework; Microsoft.CodeAnalysis.CSharp 4.13.0 conservative pin
- Architecture: HIGH — all patterns derive from existing Phase 1-5 patterns (per-package builders, hosted services, ProjectRef-only Analyzer) + first-party docs for OpenAPI/source-gen/template idioms
- Pitfalls: HIGH — all 10 pitfalls verified against canonical Microsoft docs or hands-on knowledge of MSBuild + Roslyn quirks
- Security: MEDIUM-HIGH — ASVS mapping is straightforward (no new crypto, no new external IO surfaces); A8 (7th-package release train coverage) needs plan-time confirmation
- Test architecture: HIGH — mirrors Phase 3-5 fixture + collection patterns

**Research date:** 2026-05-25
**Valid until:** 2026-06-25 (30 days — stack is stable .NET 10 LTS; only Microsoft.AspNetCore.OpenApi may release a minor bump within window — non-breaking)
