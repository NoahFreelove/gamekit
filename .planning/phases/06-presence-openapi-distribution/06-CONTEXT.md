# Phase 6: Presence + OpenAPI + Distribution - Context

**Gathered:** 2026-05-25
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 6 closes out v1 by shipping the four remaining surfaces that turn the v1 codebase into a self-hosted backend a newcomer can install:

1. **`GameKit.Presence`** — fills the Phase-1 stub package: Redis-TTL heartbeat (`/api/presence/heartbeat`), `IPresenceProvider` implementation, Admin UI top-25 panel, game-server-authoritative in-match transitions (`POST /api/sessions/{id}/start` sets in-match; `/complete`/`/abandon` clear it back to online or offline).
2. **OpenAPI doc + contract test** — single combined `/openapi/v1.json` covering every player-facing GameKit HTTP endpoint (auth + sessions + matchmaking + parties + presence); admin endpoints intentionally excluded; `EndpointDataSource`-driven contract test enforces no-endpoint-missing.
3. **`dotnet new gamekit` template + SampleGame topology** — `templates/GameKit.Templates/` NuGet template wrapping the full TicTacToeDuel sample (web app + new `TicTacToeDuel.GameServer` console process using `gamekit_reader`); `--skip-auth/--skip-rankings/--skip-matchmaking/--skip-presence` opt-out flags.
4. **Release-train + production-ops hardening** — Roslyn source generator stamps `GameKitVersion` const into every `GameKit.*` assembly from MinVer `$(Version)`; `GameKitVersionAssertionHostedService` fails fast on mismatch in `IHost.StartAsync`; MSBuild target enforces exact-pin `[X.Y.Z]` sibling `PackageReference`s during `Pack`; multi-page `docs/ops/` production-readiness guide (bare-metal / container / air-gapped recipes + JWT key management + disaster-recovery runbook); CS1591-as-error verified across all 6 shipped packages.

**Out of scope (deferred to v2 or backlog):** route-prefix normalization to `/api/v1/*`, multi-device aggregated presence, Argon2 password hasher (`GameKit.Auth.Argon2`), `/openapi/admin/v1.json` separate admin-cookie-gated doc.

</domain>

<decisions>
## Implementation Decisions

### Presence shape (PRES-01..PRES-06)
- **D-01:** Heartbeat TTL = 30 seconds; expected client cadence = ping every 10 seconds (3× safety factor — lose 3 pings before going offline). Tight default for arena-style games; consumers can override via `GameKitPresenceOptions.{TtlSeconds, HeartbeatIntervalSeconds}` if they need a slower contract.
- **D-02:** Player-facing route = `/api/presence/heartbeat` (POST, JWT-bearer-required, idempotent). Consistent with the `/api/sessions/`, `/api/mm/`, `/api/parties/` prefixes from Phase 4-5 (NOT the bare `/presence/heartbeat` literal from ROADMAP — see ROADMAP TYPO note in `<specifics>`).
- **D-03:** In-match transition is set by the game-server's `POST /api/sessions/{id}/start` (a Phase-4 session-lifecycle endpoint that Phase 6 wires the Presence side-effect into); `POST /api/sessions/{id}/complete` and `POST /api/sessions/{id}/abandon` clear the in-match marker back to online (heartbeat fresh) or offline (heartbeat expired). The XML doc on `Core.Services.IPresenceProvider.PresenceStatus.InMatch` is authoritative; ROADMAP SC#1 wording about "/abandon moves to in-match" is a documented typo, NOT new behavior.
- **D-04:** Single Redis key per player — `presence:{playerId}` last-write-wins across multiple devices. Any device's heartbeat keeps the player Online; whichever heartbeats most recently wins. Per-device aggregation deferred to v2 (multi-device fidelity is not a v1 stakeholder ask).
- **D-05:** No rate-limit on the heartbeat endpoint. Heartbeat is a single Redis `SETEX` — cheaper than queue enqueue. Runaway clients hit Kestrel's queue same as other traffic.
- **D-06:** Admin Presence panel — **Top-25 online players**, **10 second refresh** (reuses existing `GameKitAdminOptions.Panel.RefreshInterval`), **MudDataGrid layout** with columns `PlayerId | DisplayName | Status badge (Online / InMatch) | LastSeen`, sortable. Missing-package path reuses the existing `MissingPackageAlert.razor` pattern with literal substrings `Install GameKit.Presence` + `AddPresence(…)`.

### OpenAPI doc structure (OPEN-01)
- **D-07:** Single combined document published at `/openapi/v1.json`. Endpoints tagged by package (`auth`, `sessions`, `mm`, `parties`, `presence`) so Swagger/Stoplight renderers group naturally. Matches CLAUDE.md "install only what you need" — the doc reflects exactly whatever Map* extensions the consumer registered.
- **D-08:** Public doc exposes ONLY the player JWT bearer security scheme (`bearerAuth` in `components.securitySchemes`). Admin endpoints (`/admin/api/*`) are intentionally excluded from the public doc — matches Phase 3 D-04 "404-in-Production" admin philosophy. Whether to ship a separate `/openapi/admin/v1.json` gated behind admin cookie scheme is a Plan-time open option; v1 default is NO.
- **D-09:** Coverage contract test = `EndpointDataSource` enumeration via `WebApplicationFactory`: resolve `IEndpointSourceProvider`, filter routes whose pattern starts with `/admin`, assert every remaining `(METHOD, PATH)` tuple appears in the generated OpenAPI doc. First-party ASP.NET Core API; survives endpoint refactors. Lives in `tests/GameKit.OpenApi.Integration.Tests/`.
- **D-10:** Route prefixes documented **as-is** — no `/v1/` path normalization in v1. Mixed prefixes (`/auth/*` + `/api/*`) reflect what already shipped Phase 2+; renaming would be a breaking change. `info.version` field in the OpenAPI doc encodes the MinVer-derived GameKit package version. `/api/v1/*` normalization + 308-redirect plumbing deferred to v2.

### Template + SampleGame (DIST-02..DIST-04)
- **D-11:** `dotnet new gamekit -n ${ProjectName}` produces a **full TicTacToeDuel clone** with name replacements: includes web app (`${ProjectName}.csproj`), `${ProjectName}.GameServer` console app (see D-13), `matchmaking.html` SPA, `index.html`, `appsettings.json`, full README pointing at `docker-compose.yml`. Template lives at `templates/GameKit.Templates/content/GameKit.SampleGame/`. Newcomer gets a working game end-to-end (auth + matchmaking + rankings + admin + presence).
- **D-12:** Template parameters = `-n ${name}` (standard) + four boolean opt-outs: `--skip-auth`, `--skip-rankings`, `--skip-matchmaking`, `--skip-presence`. Each maps to conditional `<PackageReference>` and `Program.cs` `Add${Pkg}()` blocks. Template engine handles via `template.json` symbols + `#if (!SKIP_X) ... #endif` blocks in source files. Connection-string flags (`--port`, `--postgres-host`, `--redis-host`) NOT exposed — defaults match the shipped `docker-compose.yml`; consumers edit `appsettings.json` if they diverge.
- **D-13:** **`samples/TicTacToeDuel.GameServer/`** is a NEW console app (Phase 6 deliverable) that demonstrates the game-server-side topology: connects to Postgres via `gamekit_reader` connection string, reads ladders/players for matchmaking eligibility, calls `POST /api/sessions/{id}/start` + `/complete` against the web app via HTTP. Real production topology proof (web tier writes, game-server reads). Template clones BOTH `${ProjectName}` (web) AND `${ProjectName}.GameServer` (console).
- **D-14:** DIST-02 ("`gamekit_reader` cannot INSERT") lives in a new **`tests/GameKit.Distribution.Integration.Tests/`** project. Also houses: DIST-03 SampleGame smoke test (boot the template output against Testcontainers Postgres+Redis, assert guest auth + session-complete + leaderboard query work), OPS-04 GameKitVersion mismatch test, OPS-06 clean-install migration test. Mirrors the `GameKit.Admin.Integration.Tests` / `GameKit.Matchmaking.Integration.Tests` per-package pattern.

### Release train + ops guide (OPS-04, OPS-05, DIST-05)
- **D-15:** GameKitVersion is stamped by a **Roslyn source generator** (new `src/GameKit.Build/` project, MIT-licensed, ProjectRef-only never published as a NuGet package): reads MSBuild `$(Version)` from MinVer at compile time, emits `internal const string GameKitVersion = "X.Y.Z";` into each `GameKit.*` assembly under namespace `GameKit.<Pkg>.Internal` (class `GameKitMarker`). Referenced via `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` in every `src/GameKit.*/*.csproj`. Cleaner than runtime `AssemblyInformationalVersion` parsing; const intern-pooled at JIT time.
- **D-16:** Mismatch detection = **`GameKitVersionAssertionHostedService`** in `IHost.StartAsync` (before Kestrel accepts traffic) — iterates `AppDomain.CurrentDomain.GetAssemblies()` filtered to assemblies whose name matches `GameKit.*`, reflects on the `Internal.GameKitMarker.GameKitVersion` const, throws `GameKitVersionMismatchException` listing the divergent `(assembly, version)` tuples. Same pattern as existing `AuthMigrationHostedService` / `AdminMigrationHostedService` / `MatchmakingMigrationHostedService` / `RankingsMigrationHostedService`. Registered automatically by `AddGameKit()` in Core.
- **D-17:** Exact-pin enforcement via **custom MSBuild target** (`GameKit.targets` at repo root, imported into `Directory.Build.props`). Target runs in the `Pack` phase: enumerates `<ProjectReference Include="..\GameKit.*\GameKit.*.csproj" />` items in the current csproj and emits a `<PackageReference Include="GameKit.X" Version="[$(Version)]" />` (square-bracket exact-pin syntax) for each. CI then runs `dotnet pack` against a tagged release and asserts via grep that every produced `.nuspec` contains `Version="[X.Y.Z]"` (literal square brackets) for every sibling ref. CI wildcard guard: `! grep -rE 'Version="(\*|\^)' src/**/*.csproj` blocks PR if any wildcard pin sneaks in.
- **D-18:** Production-readiness ops guide = **multi-page `docs/ops/`** layout — `README.md` (index + table of contents), `bare-metal.md`, `container.md`, `air-gapped.md`, `postgres-roles.md`, `redis-aof.md`, `jwt-keys.md`, `disaster-recovery.md`, `migrations-runbook.md`. Each runbook is single-purpose and deep-linkable (operators can share one URL); easier to maintain than a 3000-line `OPS.md`. Repo-root `README.md` gets a "Production Deployment" section linking to `docs/ops/README.md`.

### Post-research addendum (locked by RESEARCH.md findings, 2026-05-25)
- **D-19:** **Admin-route exclusion uses `OpenApiOptions.ShouldInclude`**, NOT operation transformers (RESEARCH.md §Architecture pitfall #2 — transformers can decorate but cannot REMOVE paths from the document). Filter pattern: `options.ShouldInclude = (description) => !description.RelativePath?.StartsWith("admin", StringComparison.OrdinalIgnoreCase) == true;`.
- **D-20:** **Phase 6 SHIPS the `/api/sessions/{id}/start` and `/api/sessions/{id}/abandon` endpoints** — they do NOT exist today (only `/complete` from Phase 4). The new endpoints land in `src/GameKit.Rankings/Http/SessionsEndpoints.cs` (or a new partial extending Phase-4's existing surface) and fire `ISessionLifecycleObserver` for the Presence in-match transition. ROADMAP SC#1 wording typo + Core XML doc both reference these endpoints assuming they exist; Phase 6 must materialize them.
- **D-21:** **NEW `ISessionLifecycleObserver` Core port** (in `src/GameKit.Core/Services/`, sibling to existing `IPostSessionCompleteHandler` from Plan 04-05). `GameKit.Presence.Services.PresenceLifecycleObserver` implements it. `SessionCompleteService` + the new `/start` + `/abandon` endpoints resolve `IEnumerable<ISessionLifecycleObserver>` and invoke them inside the existing transaction. Keep `IPostSessionCompleteHandler` for backwards-compat (Rankings already uses it).
- **D-22:** **The release train covers 7 packages, not 6.** D-15 introduced `GameKit.OpenApi` as a new src project. ROADMAP SC#5 wording ("all 6 packages: Core, Auth, Rankings, Matchmaking, Presence, Admin.UI") is now stale; Plan 06-01 must ship a one-line ROADMAP correction to read "all 7 packages: Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi". The source generator + version-assertion hosted service iterate all 7.
- **D-23:** **Source generator visibility into `$(Version)` requires `<CompilerVisibleProperty Include="Version" />`** in `Directory.Build.props`. Without it the generator cannot read MinVer's MSBuild property (Roslyn analyzer host hides MSBuild properties by default). Add the `CompilerVisibleProperty` element next to the existing `<MinVerTagPrefix>` line.
- **D-24:** **OPS-05 mismatch detection MUST eager-load referenced assemblies** before iterating `AppDomain.CurrentDomain.GetAssemblies()`. Pattern: `Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Where(n => n.Name?.StartsWith("GameKit.", StringComparison.Ordinal) == true).Select(Assembly.Load).ToList();` — without this, packages whose endpoints haven't been hit yet (Matchmaking, Presence) are silently missed.
- **D-25:** **Source-generator project lives at `src/GameKit.Build/`** with TFM `netstandard2.0` (Roslyn analyzer requirement), `<IsRoslynComponent>true</IsRoslynComponent>`, `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` (local-pin Microsoft.CodeAnalysis.CSharp 4.13 to dodge CPM constraints on analyzer assemblies — RESEARCH.md §OQ3). NOT published as its own NuGet package — every GameKit.* csproj references it via `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
- **D-26:** **Pack-time exact-pin enforcement primary line of defense = CI grep on produced `.nuspec`**, not the MSBuild target alone. The MSBuild target's `ItemDefinitionGroup` metadata approach (RESEARCH.md §Architecture pitfall #4) emits the exact-pin sibling refs at evaluation time; CI then runs `grep -E 'Version="[\^\*]' artifacts/**/*.nuspec` and fails the build on any wildcard. Defense in depth.

### Claude's Discretion
- Specific HTTP shape for `/api/presence/heartbeat` request body (empty `{}` body vs ProblemDetails error vs no body — Plan-time decision; default is empty body since JWT already identifies the player).
- `LastSeen` column format in the Admin panel ("3 seconds ago" relative vs ISO timestamp — Plan-time UX decision, default is relative).
- Specific scope of OPS-06 clean-install migration test (full migration matrix vs single-shot — Plan-time decision; default is single-shot against fresh empty Postgres).
- Whether `GameKit.Build` source generator emits a `[GameKitInfo]` attribute alongside the const (for future tooling discovery — Plan-time decision; default is const-only).

### Folded Todos
None — no pending todos matched Phase 6 scope (no `gsd-sdk query todo.match-phase` hits).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Existing GameKit code (must read for boundary preservation)
- `src/GameKit.Core/Services/IPresenceProvider.cs` — locked Phase 1 contract. `PresenceStatus { Offline=0, Online=1, InMatch=2 }` + `GetStatusAsync(Guid playerId, CT)` + `GetOnlinePlayerIdsAsync(int take, CT)`. Phase 6 implements this interface in `GameKit.Presence`; Phase 3 Admin UI already calls into it conditionally (gracefully degrades when no `IPresenceProvider` registered).
- `src/GameKit.Presence/GameKit.Presence.csproj` — Phase 1 stub package (ProjectRef → Core only; AssemblyInfo.cs is the only source file). Phase 6 fills in.
- `src/GameKit.Admin.UI/Components/Shared/MissingPackageAlert.razor` — graceful-degrade pattern. Comments lines 8-10 document the EXACT rendered substrings (`Install GameKit.Matchmaking`, `Install GameKit.Rankings`) that Phase 3 SC#4 integration tests assert. Phase 6 adds the `Presence` variant: must render `Install GameKit.Presence and add .AddPresence(…) to your service registration to enable Presence telemetry.`
- `src/GameKit.Admin.UI/Configuration/GameKitAdminOptions.cs` — `Panel.RefreshInterval` default (10s) which the new Presence panel reuses. Do NOT introduce a separate refresh-cadence option for Presence.
- `src/GameKit.Admin.UI/Components/Pages/Dashboard.razor` line 150 + line 69 — reflection-only `Type.GetType("GameKit.X.Y, GameKit.X")` lookup pattern for the missing-package detection (see Phase 5 UAT-1 D1 fix on Dashboard.razor:150). Phase 6's `IPresenceProvider` detection uses the SAME pattern via Core's interface (`typeof(IPresenceProvider)` from Core is always loaded, so `sp.GetService<IPresenceProvider>() == null` is enough — no reflection lookup needed).
- `docker-compose.yml` + `docker/postgres/init/01-roles.sql` — 3-role Postgres bootstrap (`gamekit_owner` / `gamekit_app` / `gamekit_reader`) with default-privileges grants. DIST-02 asserts `gamekit_reader` denied INSERT on `gamekit.game_sessions`.
- `samples/TicTacToeDuel/` — full sample app to be cloned. Phase 6 produces `templates/GameKit.Templates/content/GameKit.SampleGame/` as a template-engine-aware copy with `${ProjectName}` substitutions.
- `samples/TicTacToeDuel/Program.cs` — `Host.CreateDefaultBuilder` + `AddGameKit().AddAuth().AddRankings().AddMatchmaking()` fluent chain. Phase 6 adds `.AddPresence()` here AND inside the template.
- `samples/TicTacToeDuel/scripts/run-sample.sh` — gamekit_owner connection string template; Phase 6's `TicTacToeDuel.GameServer` console app needs an analogous `run-game-server.sh` using `gamekit_reader`.
- `Directory.Build.props` — MinVer 7.0.0 + SourceLink 10.0.202 pinned (Phase 1). Phase 6 imports the new `GameKit.targets` from this file.
- `Directory.Packages.props` — CPM-only entries; Phase 6 adds **ONE** new pin: `Microsoft.AspNetCore.OpenApi` 10.0.8 (verified GA on nuget.org 2026-05-12 per RESEARCH.md). Earlier CONTEXT.md draft incorrectly claimed this package shipped in the .NET 10 shared framework — RESEARCH.md §Standard Stack corrected this. The new `GameKit.Build` SourceGenerator references `Microsoft.CodeAnalysis.CSharp` 4.13+ which IS already transitively present via .NET 10 SDK (no second pin needed).
- `CLAUDE.md` per-package tables + Key Decisions — locked decisions on MinVer coordinated train, exact-pin sibling refs, CS1591-as-error, no SaaS deps.

### Phase 5 patterns to mirror (per-package boundary)
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` — `AdvisoryLockKey = 388956820L` live-verified pattern. Presence does NOT need a migration constant (Redis-only, no EF entities per PRES-01).
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` partial split (base + .Ladder) — Phase 6 mirrors as `PresenceBuilderExtensions.cs` partial split into base + .Options.
- `tests/GameKit.Matchmaking.Integration.Tests/` xUnit collection-fixture pattern — `tests/GameKit.Distribution.Integration.Tests/` mirrors it (own `CollectionDefinitions.cs` + `DistributionIntegrationFixture.cs`).

### Planning documents
- `.planning/REQUIREMENTS.md` lines 99-114 — Phase 6 requirements (PRES-01..06, OPEN-01, DIST-02..06, OPS-04, OPS-05).
- `.planning/ROADMAP.md` lines 220-256 — Phase 6 goal + 6 success criteria. **NOTE:** SC#1 wording typo on in-match trigger — see `<specifics>` below for the authoritative wording.
- `.planning/STATE.md` — Phase 5 close-out summary + decision log (Sessions = source of truth for in-match trigger; reused PartyService SERIALIZABLE pattern; etc.).

### External libraries (already pinned or shared framework)
- `Microsoft.AspNetCore.OpenApi` 10.0.8 (explicit NuGet pin — NOT in shared framework; verified GA 2026-05-12) — `AddOpenApi()` + `MapOpenApi()` API + `OpenApiOptions.ShouldInclude` for admin-route filtering (operation transformers can decorate but cannot REMOVE paths — RESEARCH.md §Architecture pitfall #2). See https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi for the filter and transformer APIs.
- `Microsoft.CodeAnalysis.CSharp` 4.13+ (transitively present in .NET 10 SDK) — incremental source generator API for `GameKit.Build`.
- `Microsoft.TemplateEngine.Authoring.Templates` 10.0.x (SDK-aligned, no NuGet pin needed) — `template.json` schema for `dotnet new gamekit`.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`IPresenceProvider` interface** (Core): already locked; Phase 6 implements only.
- **`MissingPackageAlert.razor`**: exact pattern for the Admin Presence panel's empty state. Test contract is the rendered substring (`Install GameKit.Presence`).
- **`GameKitAdminOptions.Panel.RefreshInterval`**: 10s default reused by the new Presence panel — no new option.
- **`AdminTestHost`** in `tests/GameKit.TestFixtures/`: WebApplicationFactory pattern reused by `GameKit.OpenApi.Integration.Tests` for the contract test (host needs admin endpoints registered too to confirm they're filtered OUT of the public OpenAPI doc).
- **`AuthMigrationHostedService` / `MatchmakingMigrationHostedService`**: structural pattern for the new `GameKitVersionAssertionHostedService` (run-once at `IHost.StartAsync` before Kestrel).
- **`docker-compose.yml` + `docker/postgres/init/01-roles.sql`**: 3-role bootstrap; DIST-02 reuses verbatim against Testcontainers.

### Established Patterns
- **Per-package partial `*BuilderExtensions.cs`**: `AddXxx()` base + `.Ladder` / `.Options` partial follows Rankings/Matchmaking. Phase 6 produces `PresenceBuilderExtensions.cs` (base + .Options partial).
- **Per-package `*Marker` internal-static class** (Phase 1+2+3 pattern via `AuthMarker`, `AdminUiMarker`, etc.): Phase 6's `GameKit.<Pkg>.Internal.GameKitMarker.GameKitVersion` source-gen-emitted const reuses the namespace shape; existing Marker classes can be folded into the generated `GameKitMarker` partial-class output (one source-of-truth per package).
- **Reflection-safe missing-package detection**: `Type.GetType("X.Y, X")` on Dashboard.razor — Phase 6 adds the `IPresenceProvider`-check variant (cheaper: just `sp.GetService<IPresenceProvider>() != null` since the interface is in always-loaded Core).
- **HostedService for run-once startup work**: pattern reused for `GameKitVersionAssertionHostedService` — run order in DI registration determines startup order (version assertion registered FIRST in `AddGameKit()` so it gates everything else).
- **`tests/GameKit.X.Integration.Tests/CollectionDefinitions.cs` + per-fixture `BuildServiceProvider(suffix)`**: pattern reused by `tests/GameKit.Distribution.Integration.Tests/`.

### Integration Points
- **Sessions side-effect for in-match transitions**: `src/GameKit.Rankings/.../SessionCompleteService.cs` (Phase 4) needs ONE callsite-aware extension point so `GameKit.Presence` can subscribe to "/start fired → mark in-match" + "/complete or /abandon fired → clear in-match" without `GameKit.Rankings` taking a hard ref on `GameKit.Presence`. Phase 6 adds an `ISessionLifecycleObserver` interface in `GameKit.Core/Services/` (mirrors the existing `IPostSessionCompleteHandler` pattern from Plan 04-05) that Presence implements; `SessionCompleteService` resolves `IEnumerable<ISessionLifecycleObserver>` and invokes them inside the existing transaction.
- **OpenAPI service registration**: ONE consumer-visible call `services.AddGameKitOpenApi()` registers `Microsoft.AspNetCore.OpenApi`'s `AddOpenApi()` with GameKit-specific document-transformer (tag groupings, JWT-bearer security scheme, admin-route filter). Lives in a new `src/GameKit.OpenApi/` package (NEW package, 7th GameKit assembly — listed in ROADMAP MinVer release train).
- **GameKit.Build source generator**: ProjectRef'd from EVERY `src/GameKit.*/*.csproj` (Core, Auth, Admin.UI, Rankings, Matchmaking, Presence, OpenApi). Output items typed `Analyzer` with `ReferenceOutputAssembly=false` so the generator runs at compile but its assembly does NOT ship.
- **CS1591 audit**: Phase 1 OPS-02 enabled `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` repo-wide. Phase 6 verifies CS1591 specifically is not suppressed anywhere; spot-fix gaps surfaced by `dotnet build -warnaserror` on a fresh `<NoWarn>` purge.

</code_context>

<specifics>
## Specific Ideas

### ROADMAP TYPO (must be reconciled at plan-execute time, or documented as won't-fix)
Phase 6 SC#1 in ROADMAP.md reads:

> "...the game server calling `POST /api/sessions/{id}/abandon` (game-server-authoritative) is what moves them to `in-match` or triggers abandonment, never presence inference alone."

This wording is internally contradictory: `/abandon` cannot simultaneously move a player TO in-match AND trigger abandonment. The authoritative wording (matching `IPresenceProvider.PresenceStatus.InMatch` XML doc at `src/GameKit.Core/Services/IPresenceProvider.cs:20`) is:

> "...the game server calling `POST /api/sessions/{id}/start` is what moves them to `in-match`; `POST /api/sessions/{id}/complete` or `/abandon` clears in-match back to online (heartbeat fresh) or offline. Presence inference is never the trigger."

**Action item for Plan-01:** propose a one-line ROADMAP.md SC#1 wording correction commit alongside the Presence implementation. Either the ROADMAP gets updated or the Core XML doc gets reverted — DOC and CODE must agree.

### Connection-string pattern for the GameServer console
`samples/TicTacToeDuel.GameServer/appsettings.json` should ship with `ConnectionStrings:GameKit` pointing at the SAME Postgres host as the web app but using user `gamekit_reader` + password `gamekit_reader_dev` (matching `docker/postgres/init/01-roles.sql`). This MUST be different from `samples/TicTacToeDuel/scripts/run-sample.sh` which uses `gamekit_owner`. The DIST-02 test seeds a row via `gamekit_owner`, then opens a SECOND Npgsql connection as `gamekit_reader` and asserts an `INSERT` raises Postgres error code `42501` ("permission denied for table game_sessions").

### Template post-action: regenerate keys + license header
`dotnet new gamekit -n MyGame` post-action should run `scripts/gen-test-rsa-pem.sh` (already exists in TicTacToeDuel) AND prepend the GPL-3.0-or-later SPDX header to every source file generated. The post-action is declared in `template.json` `postActions`; users can opt-out with `--no-restore` if they intend to script it differently.

### CS1591 audit scope
Per CLAUDE.md OPS-02, CS1591-as-error is already on at the repo root. Phase 6 DIST-06 plan verifies no `<NoWarn>1591</NoWarn>` overrides snuck into individual csprojs (a quick `grep -rE '<NoWarn>.*1591' src/` should return empty); if any are found, they get fixed (add the missing XML doc comments) rather than suppressed.

</specifics>

<deferred>
## Deferred Ideas

- **Per-device aggregated presence** (using existing `X-GameKit-Device` fingerprint from Phase 2): v2 if multi-device fidelity becomes a stakeholder ask.
- **Presence-driven analytics** (player-session-time histograms, online-now sparklines): v2 admin feature.
- **`/api/v2/*` prefix normalization** + `/auth/*` legacy 308-redirect plumbing: v2 migration scope.
- **`/openapi/admin/v1.json` separate admin-cookie-gated doc**: Plan-time open option for OpenAPI plan; v1 default is "admin endpoints excluded".
- **Second `gamekit-skeleton` minimal template** (no rankings/matchmaking, just Core+Auth): v1.1 if "full TicTacToeDuel clone" proves too heavy in practice.
- **Argon2 password hasher sibling package** (`GameKit.Auth.Argon2` using Isopoh): v2 per AUTH-V2-01.
- **`[GameKitInfo]` assembly attribute** alongside the source-gen-emitted const (for future tooling discovery): Plan-time decision deferred — v1 default is const-only.

### Reviewed Todos (not folded)
None — no pending todos matched Phase 6 scope.

</deferred>

---

*Phase: 6-Presence + OpenAPI + Distribution*
*Context gathered: 2026-05-25*
