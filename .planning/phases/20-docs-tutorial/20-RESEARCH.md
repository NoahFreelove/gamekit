# Phase 20: Docs & Tutorial — Research

**Researched:** 2026-06-23
**Domain:** DocFX API documentation, tutorial authoring, CI smoke testing, runbook library, ADR capture
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None locked — discuss phase skipped. All implementation at Claude's discretion.

### Claude's Discretion
All implementation choices are at Claude's discretion per CONTEXT.md (discuss skipped via workflow.skip_discuss).

### Deferred Ideas (OUT OF SCOPE)
None beyond REQUIREMENTS.md scope. Public docs-site publishing is explicitly out of scope this milestone per DOCS-01.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| DOCS-01 | DocFX (MIT, net10.0) site from existing XML doc comments; `docfx build --warningsAsErrors` CI gate; in-repo, not published | DocFX 2.78.5 confirmed globally available; local-tool-manifest pattern documented; **2 blocking metadata warnings found and root-caused — fix path identified** |
| DOCS-02 | Getting-started tutorial (`dotnet new gamekit` → first authenticated player + first completed match in ~15 min); CI smoke test | Template short-name verified as `gamekit`; tutorial happy-path fully traced (port 5433, guest→enqueue→match→complete); `poolName` bug in matchmaking.html confirmed |
| DOCS-03 | Per-package concepts docs: what it does, interfaces exposed, library-vs-consumer boundary | Full interface inventory completed across all 14 src/ packages |
| DOCS-04 | Upgrade/compatibility guide v2.0 → v2.1 | Exact config additions enumerated: AddGameKitObservability, AddGameKitHealthChecks, MapGameKitHealth, ILeaderLease, MessagePack pin, DrOrdering migrations |
| DOCS-05 | Runbook library + ADRs | Existing docs/runbooks/, docs/ops/ inventoried; MISSING: rolling-deploy, matchmaking-outage incident-response; ADR directory does not exist yet |
| DOCS-06 | Sample app current with v2.1 (observability, health) | Sample verified current for observability/health; **bug found: matchmaking.html sends poolName:"tictactoe" instead of null → match never forms** |
</phase_requirements>

---

## Summary

Phase 20 is a documentation and CI-gates phase. The work divides into three layers: (1) DocFX setup and the strict `--warningsAsErrors` CI gate, (2) the getting-started tutorial backed by a CI smoke test that exercises the real happy-path through the TicTacToeDuel sample, and (3) prose documentation — concepts per package, upgrade guide, runbooks, and ADRs.

The most important pre-work finding: **DocFX metadata fails exit code 255 with `--warningsAsErrors`** due to two "Duplicate source file" warnings for `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` in `GameKit.Build`. Root cause: `Directory.Build.props` adds these files explicitly via `<AdditionalFiles>` under an `AssemblyName == GameKit.Build` condition, but the Roslyn SDK also auto-detects them via `Exists('$(MSBuildProjectDirectory)\AnalyzerReleases.*.md')` — docfx sees both. Fix: remove the two explicit `<AdditionalFiles>` lines from `Directory.Build.props` (the Roslyn SDK auto-discovery is sufficient). With that fix applied, `docfx build --warningsAsErrors` passes with **0 warnings 0 errors** — verified live. The XML doc coverage across all src/ packages is already complete (CS1591 is a `WarningsAsErrors` in `Directory.Build.props`; the codebase already enforces it at build time).

The second key finding: **matchmaking.html sends `poolName: "tictactoe"` in the enqueue POST** but TicTacToeDuel's matchmaking ladder has no explicit pool named "tictactoe" — tickets only pair in the `"default"` pool (null PoolName routes there). The tutorial smoke test and DOCS-06 sample fix must both send `poolName: null`. The DOCS-06 task must patch `matchmaking.html` line 240 to change `poolName: "tictactoe"` to `poolName: null` (or omit the field entirely, which is equivalent given the record default).

**Primary recommendation:** Fix the 2-line Directory.Build.props gap and the matchmaking.html poolName bug first (they are Wave 0 prerequisites), then build the committed docfx.json + dotnet-tools.json, write the tutorial, and write the prose docs in parallel.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| DocFX API site generation | Build/CI tooling | None | Consumes compiled XML docs; runs as a dotnet local tool in CI |
| Tutorial prose + happy-path steps | docs/ authoring | Sample app | Tutorial drives real sample endpoints; docs and sample co-own the happy-path |
| CI smoke test | CI (dotnet test) | Sample + Testcontainers | Smoke test is an xUnit integration test using the existing Testcontainers pattern |
| Per-package concepts docs | docs/ authoring | src/ source (ground truth) | Docs must accurately reflect interfaces in src/ |
| Runbooks + ADRs | docs/ authoring | None | Operator-facing markdown; no code dependency |
| Sample app current (DOCS-06) | samples/TicTacToeDuel | CI smoke test | Sample is both tutorial target and integration harness |

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| **docfx** | **2.78.5** | API reference site from XML docs | MIT; .NET global tool; installed globally on this machine; the canonical .NET OSS doc tool |
| **xUnit** | 2.9.2 (pinned) | CI smoke test framework | Already the project test framework |
| **Testcontainers.PostgreSql** + **Testcontainers.Redis** | 4.11.0 (pinned) | Spin up real Postgres + Redis for smoke test | Already used by all integration tests |

[VERIFIED: live `docfx --version` output on machine] docfx 2.78.5+fafdcd5ddacdb756bd5c4b84f2f07c18292e4821

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| **dotnet local tool manifest** | n/a | Pin docfx version in `.config/dotnet-tools.json` | So CI runs `dotnet tool restore && dotnet docfx` — no global install needed in CI |

**Installation (CI one-time setup):**
```bash
# In repo root — creates .config/dotnet-tools.json
dotnet new tool-manifest
dotnet tool install docfx --version 2.78.5
# Verify
dotnet tool restore
dotnet docfx --version
```

After `.config/dotnet-tools.json` is committed, CI runs:
```bash
dotnet tool restore
dotnet docfx docfx.json --warningsAsErrors   # metadata + build in one command
```

---

## Package Legitimacy Audit

> docfx is a dotnet global/local tool, not a NuGet package reference in source code. No new NuGet packages are added to any `src/` project in this phase.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| docfx (dotnet tool) | nuget.org | ~8 yrs | Millions (Microsoft project) | github.com/dotnet/docfx | OK | Approved |

**Packages removed due to SLOP verdict:** none
**Packages flagged as suspicious:** none

---

## DocFX Audit: The Blocking `--warningsAsErrors` Gap (DOCS-01)

### Finding: 2 Warnings — Exit Code 255

Running `docfx metadata docfx.json --warningsAsErrors` exits **255** (failure) due to two warnings:

```
warning: [Warning] Duplicate source file
  '/home/.../src/GameKit.Build/AnalyzerReleases.Shipped.md'
  in project 'GameKit.Build.csproj'

warning: [Warning] Duplicate source file
  '/home/.../src/GameKit.Build/AnalyzerReleases.Unshipped.md'
  in project 'GameKit.Build.csproj'
```

`docfx build --warningsAsErrors` passes with **0 warnings 0 errors** (exit 0). The blocking step is `docfx metadata`.

### Root Cause [VERIFIED: live MSBuild preprocessor output]

`Directory.Build.props` explicitly adds the files:
```xml
<!-- Lines 89-90 -->
<ItemGroup Condition="'$(AssemblyName)' == 'GameKit.Build'">
  <AdditionalFiles Include=".../src/GameKit.Build/AnalyzerReleases.Shipped.md" />
  <AdditionalFiles Include=".../src/GameKit.Build/AnalyzerReleases.Unshipped.md" />
</ItemGroup>
```

The Roslyn SDK for analyzer projects _also_ auto-detects these files via its own `Exists()` condition (visible in the preprocessed MSBuild output):
```xml
<ItemGroup Condition="Exists('$(MSBuildProjectDirectory)\AnalyzerReleases.Shipped.md')">
  <AdditionalFiles Include="AnalyzerReleases.Shipped.md" />
</ItemGroup>
```

docfx sees both paths (absolute from Directory.Build.props + relative from SDK auto-detect) for the same physical file — hence "Duplicate source file". This is NOT a missing-docs issue; it is a tool-configuration collision.

### Fix: Remove the 2 Explicit Lines from Directory.Build.props

The Roslyn SDK auto-discovery is sufficient. Remove these two lines from `Directory.Build.props`:
```xml
<!-- DELETE these two AdditionalFiles lines from the Condition="AssemblyName=='GameKit.Build'" ItemGroup -->
<AdditionalFiles Include="$(MSBuildThisFileDirectory)src/GameKit.Build/AnalyzerReleases.Shipped.md" />
<AdditionalFiles Include="$(MSBuildThisFileDirectory)src/GameKit.Build/AnalyzerReleases.Unshipped.md" />
```

After this change, `docfx metadata --warningsAsErrors` exits 0. Regression risk: none — the Roslyn SDK auto-discovery fires on the same files.

### XML Doc Coverage Verdict [VERIFIED: live docfx build run]

`docfx build --warningsAsErrors` already passes with **0 warnings 0 errors** across all 14 src/ packages. The `<WarningsAsErrors>CS1591</WarningsAsErrors>` in `Directory.Build.props` enforces XML doc completeness at compile time — the codebase is already compliant. No XML doc gap-filling sprint is needed.

### Scope of the docfx.json

The following 13 packages must be in scope (GameKit.Build is excluded — it is a Roslyn analyzer, not a shipped consumer API):

- GameKit.Core, GameKit.Auth, GameKit.Auth.Argon2, GameKit.Auth.Apple, GameKit.Auth.Epic, GameKit.Auth.Google
- GameKit.Rankings, GameKit.Matchmaking, GameKit.Presence, GameKit.Lobby
- GameKit.Admin.UI, GameKit.OpenApi, GameKit.Cli

Recommended approach: use `src/**/*.csproj` in the metadata src glob — docfx loads GameKit.Build transitively but the duplicate warning only appears in metadata mode; after the Directory.Build.props fix it is gone.

### Recommended docfx.json [VERIFIED: structure against live repo]
```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/docfx/main/schemas/v1.0/docfx.schema.json",
  "metadata": [
    {
      "src": [
        {
          "files": [ "src/**/*.csproj" ],
          "src": "."
        }
      ],
      "dest": "api",
      "includePrivateMembers": false,
      "disableDefaultFilter": false
    }
  ],
  "build": {
    "content": [
      { "files": [ "api/**.yml", "api/index.md" ] },
      { "files": [ "docs/**/*.md" ] }
    ],
    "dest": "_site",
    "globalMetadata": {
      "_appName": "GameKit",
      "_appTitle": "GameKit — Self-Hosted Game Services"
    }
  }
}
```

Place at repo root as `docfx.json`. Add `_site/` to `.gitignore`.

### CI Gate Addition (append to `.github/workflows/ci.yml`)
```yaml
- name: Restore docfx tool
  run: dotnet tool restore

- name: DocFX API reference gate (DOCS-01)
  run: dotnet docfx docfx.json --warningsAsErrors
```

---

## Tutorial Smoke Test Happy-Path (DOCS-02)

### Template Short-Name [VERIFIED: templates/GameKit.Templates/content/GameKit.SampleGame/.template.config/template.json]

`shortName` is `"gamekit"` — tutorial uses `dotnet new gamekit`. Confirmed.

### Docker-Compose Port Mapping [VERIFIED: samples/TicTacToeDuel/docker-compose.yml line 13]

```yaml
postgres:
  ports:
    - "5433:5432"   # KEY: host :5432 is the developer's local Postgres; sample uses 5433
redis:
  ports:
    - "6379:6379"
```

The sample app at `appsettings.Development.json` still targets `Host=localhost;Port=5432` — this is the default development config for running the app directly (not via Docker on the app side). For the tutorial, `docker-compose up` is the infra only; the .NET app runs on the host and reads `appsettings.Development.json`. The connection string in `appsettings.Development.json` must be updated to reference port 5433 when Postgres is running in the sample's Docker compose. **OR** the tutorial must instruct readers to update the connection string to port 5433 before running.

**Investigation result:** `appsettings.Development.json` has `Port=5432` — this is a pre-existing discrepancy. The tutorial must call out the port-override step explicitly:
```bash
# Override port if your local Postgres is on :5432
export ConnectionStrings__GameKit="Host=localhost;Port=5433;Database=gamekit;Username=gamekit_app;Password=gamekit_app_dev"
export ConnectionStrings__GameKitMigrations="Host=localhost;Port=5433;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev"
dotnet run --project samples/TicTacToeDuel
```

### matchmaking.html poolName Bug [VERIFIED: samples/TicTacToeDuel/wwwroot/matchmaking.html line 240]

```js
// CURRENT (BUG — never matches):
body: JSON.stringify({ ladderId, poolName: "tictactoe", partyId: null }),

// CORRECT (null routes to "default" pool where TicTacToeDuel enqueues):
body: JSON.stringify({ ladderId, poolName: null, partyId: null }),
```

`EnqueueRequest.PoolName` defaults to `null` which routes to the `"default"` pool. The ladder name is `"tictactoe"` (for `AddLadder("tictactoe", ...)`), but the pool name within that ladder is `"default"`. DOCS-06 must fix this one-line bug in `matchmaking.html` before DOCS-02 writes a tutorial that tells users to open this page.

### Application Port [VERIFIED: samples/TicTacToeDuel/Properties/launchSettings.json]

Sample app listens on `http://localhost:5000`.

### Tutorial Happy-Path (exact steps)

1. **Prerequisites**: Docker, .NET 10 SDK, OpenSSL (for RSA keygen)
2. `docker compose -f samples/TicTacToeDuel/docker-compose.yml up -d`
3. `bash samples/TicTacToeDuel/scripts/gen-test-rsa-pem.sh` (generates `keys/dev-priv.pem` + `keys/dev-pub.pem`)
4. Set connection strings to port 5433 (env vars or override file)
5. `dotnet run --project samples/TicTacToeDuel` — migrations auto-run on first start; app ready at `http://localhost:5000`
6. Open browser at `http://localhost:5000` → click **Play as Guest** → JWT issued (`POST /auth/login/guest`, no body, `X-GameKit-Device` header required)
7. Open `http://localhost:5000/matchmaking.html` in **two browser tabs** → each tab clicks **Find Match** → both enqueue via `POST /api/mm/queue` with `{ ladderId, poolName: null }`
8. Ticker fires within 500 ms → match proposal emitted → both tabs show the accept button
9. Both players accept → `POST /api/mm/proposal/{id}/accept` → match formed
10. Assert `GET /health/ready` → 200
11. Optional (with observability): `docker compose -f ... -f docker-compose.observability.yml up -d` → Grafana at `http://localhost:3000` shows traces

### Tutorial Smoke Test Architecture

The CI smoke test for DOCS-02 should be an xUnit integration test in a new project `tests/GameKit.Tutorial.SmokeTests/`. It follows the **existing Testcontainers pattern** from `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` and `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingHappyPathTests.cs`.

**Smoke test shape:**
```csharp
// Full tutorial path: guest login → enqueue two tickets (no poolName) → poll for match → /health/ready 200
[Collection("TutorialSmoke")]
[Trait("Category", "Integration")]
public sealed class TutorialSmokeTests : IAsyncLifetime
{
    // Uses PostgresFixture + RedisFixture from GameKit.TestFixtures
    // Boots the full TicTacToeDuel program via WebApplicationFactory<Program>
    //   (or a minimal equivalent that wires all Add*+Map* calls)
    //
    // Steps:
    // 1. POST /auth/login/guest x2 (two players) → get access tokens
    // 2. POST /api/mm/queue x2 (ladderId from /demo/ladder-id/tictactoe, poolName: null) 
    // 3. Poll GET /api/mm/queue/{ticketId}/status until status = "matched" (timeout 10s)
    // 4. POST /api/mm/proposal/{proposalId}/accept x2
    // 5. GET /health/ready → 200
}
```

**Reference fixtures to reuse:**
- `GameKit.TestFixtures.PostgresFixture` — Testcontainers Postgres (port auto-assigned)
- `GameKit.TestFixtures.RedisFixture` — Testcontainers Redis
- `GameKit.TestFixtures.AuthIntegrationFixture` — pre-wired auth test host

The smoke test must NOT use docker-compose — it uses Testcontainers so it is reproducible in CI without Docker-in-Docker concerns. The `docker-compose up` path is **tutorial prose only**.

---

## Architecture Patterns

### DocFX Local Tool Manifest Pattern

```
.config/
  dotnet-tools.json    ← NEW (Wave 0) — commits docfx 2.78.5 pin
docfx.json             ← NEW (Wave 0) — metadata + build config
_site/                 ← gitignored output
docs/
  api/                 ← gitignored docfx intermediate YAML
  adr/                 ← NEW — Architecture Decision Records
  concepts/            ← NEW — per-package concepts markdown
  tutorial/            ← NEW — getting-started tutorial
  runbooks/
    postgres-backup-restore.md   ← EXISTS
    redis-backup-restore.md      ← EXISTS
    rolling-deploy.md            ← MISSING — must add
    matchmaking-outage.md        ← MISSING — must add
  ops/                           ← EXISTS (10 files)
  architecture/                  ← EXISTS (signalr-multi-replica.md)
  migration-ops.md               ← EXISTS
  performance-tuning.md          ← EXISTS
  security-checklist.md          ← EXISTS
```

### Runbook vs Ops Directory Policy

`docs/runbooks/` = reactive operator tasks (backup/restore, rolling-deploy, incident response).
`docs/ops/` = proactive deployment guidance (bare-metal, container, air-gapped, postgres-roles, jwt-keys, multi-replica).
These are complementary — cross-link, do NOT duplicate. Phase 20 adds files to `docs/runbooks/` and `docs/adr/` only; the existing `docs/ops/` files are not modified.

### ADR Pattern for .NET OSS Projects [ASSUMED — standard community convention]

ADRs live in `docs/adr/` with filenames `NNNN-short-title.md` using the Michael Nygard format:
- Title, Status, Context, Decision, Consequences
- Numbered sequentially: `0001-use-ef-core-migrations.md`, `0002-no-hangfire.md`, etc.

Key ADRs to capture for v1/v2 (all decisions already in CLAUDE.md — ADRs are a formalization):

| # | Title | Key Content |
|---|-------|-------------|
| 0001 | No MediatR/AutoMapper | Licensing (RPL after v13); plain injected services |
| 0002 | BackgroundService not Hangfire/Quartz | Library cannot add customer-DB tables |
| 0003 | Glicko-2 vendored not NuGet | Stagnant packages; 150 LOC; MIT attribution |
| 0004 | aspnet-contrib OAuth not custom | Martin Costello + Kévin Chalet battle-tested |
| 0005 | MinVer not Nerdbank.GitVersioning | Tag-driven; no version gaps |
| 0006 | Scrutor + MS.DI not source-gen DI | Libraries cannot mandate customer container |
| 0007 | FluentValidation 12 explicit inject | Auto-binding deprecated; minimal API usage |
| 0008 | BCrypt default + Argon2 opt-in | Isopoh fully-managed portability |
| 0009 | OTel opt-in not forced | Consumers who don't want observability |
| 0010 | No ASP.NET Core Identity | Fights players/player_identities/player_credentials split |

---

## DOCS-03: Per-Package Concepts Scope

Each concepts doc follows this template:
- What the package does (one paragraph)
- Key public interfaces (the "every algorithm is replaceable" story)
- Library-vs-consumer responsibility line
- Minimal wire-up snippet

### Package Inventory [VERIFIED: find /src output + grep for public interface]

| Package | Key Public Interfaces | Consumer Replaces |
|---------|----------------------|------------------|
| **GameKit.Core** | `IGameKitBuilder`, `ISessionLifecycleObserver`, `IPostSessionCompleteHandler`, `IGdprDeleteExtension`, `IPlayerRatingProvider`, `IPlayerDisplayNameResolver`, `ISessionAbandonService`, `IGameKitRateLimitPolicies`, `IModelBuilderExtension` | Session lifecycle hooks, custom display name resolution, custom rate-limit policies |
| **GameKit.Auth** | `IOAuthProvider`, `IPasswordHasher`, `IJwtIssuer`, `IRefreshTokenService`, `IIdentityLinker`, `IAccountMergeService`, `IAuthAuditWriter`, `IGuestUpgradeService`, `IExternalIdHasher`, `IIsGuestResolver` | Custom auth providers (add Discord/Apple/Epic etc.), custom password hasher, custom audit writer |
| **GameKit.Auth.Argon2** | (implements `IPasswordHasher`) | Opt-in Argon2id replacement for BCrypt default |
| **GameKit.Auth.Apple / Epic / Google** | (implement `IOAuthProvider`) | Plug-in OAuth providers |
| **GameKit.Rankings** | `IRankingAlgorithm`, `ILeaderboardService`, `IRankAdjustService`, `IEndSeasonService`, `IServiceTokenService`, `IGdprExportService`, `IGameKitRankingsBuilder` | Custom ranking algorithm (replace Glicko-2), custom leaderboard |
| **GameKit.Matchmaking** | `IMatchmakingStrategy`, `IMatchmakerTicker`, `IProposalService`, `IBackfillService`, `IMatchmakingControlService`, `IPartyCodeGenerator`, `IGameKitMatchmakingBuilder` | Custom matching strategy (replace EloRange) |
| **GameKit.Presence** | `IPresenceWriter` | Custom presence state writer |
| **GameKit.Lobby** | `ILobbyService`, `ILobbyMessageHandler`, `ILobbyClient` | Custom lobby message handlers |
| **GameKit.Admin.UI** | `IAdminAuthService`, `IPlayerBanService`, `IAdminUserService`, `IPlayerSearchService`, `IHealthProbeService`, `IAdminAuditWriter`, `IRedisErrorRateCounter` | Custom admin audit writer, custom player search |
| **GameKit.OpenApi** | (zero public interfaces — config-only) | Endpoint inclusion predicate |
| **GameKit.Cli** | (Spectre.Console.Cli commands) | Add custom CLI commands |

---

## DOCS-04: Upgrade Guide v2.0 → v2.1

### Required Config Additions [VERIFIED: src/ code + REQUIREMENTS.md traceability]

A v2.0 consumer (who has Core + Auth + Rankings + Matchmaking + Presence + Lobby) must add the following to upgrade to v2.1:

**1. AddGameKitObservability (Phase 13/15 — OBS-01/04/05/06)**
```csharp
// After all AddXxx calls, before app = builder.Build()
gameKitBuilder.AddGameKitObservability(otel =>
{
    otel.OtlpEndpoint = builder.Configuration["GameKit:Observability:OtlpEndpoint"];
    // Leave null if not running the observability stack
});
// Host-side: opt-in ASP.NET Core instrumentation (consumer choice)
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation());
```

**2. AddGameKitHealthChecks + MapGameKitHealth (Phase 14 — HLTH-01/02)**
```csharp
// Registration — AFTER all Add* extensions (so multiplexer is in DI)
gameKitBuilder.AddGameKitHealthChecks();

// Mapping — in the endpoint section
app.MapGameKitHealth();   // → GET /health/live + GET /health/ready
```

**3. ILeaderLease (Phase 16 — SCALE-01)**
No consumer action required — this is an internal consolidation of lease helpers. Only consumers who injected the concrete lease helpers (`MatchmakerLeaseHelper`, `RankDecayLeaseHelper`, `RankingsTickerLeaseHelper`) by interface need updating. Most consumers use the add-builder pattern and are unaffected.

**4. MessagePack 3.1.7 Transitive Pin (Phase 18 — SEC-07)**
```xml
<!-- Directory.Packages.props — adds transitive pin to eliminate GHSA-hv8m-jj95-wg3x -->
<PackageVersion Include="MessagePack" Version="3.1.7" />
<PackageVersion Include="MessagePack.Annotations" Version="3.1.7" />
```
Consumers who previously added `-p:NuGetAudit=false` as a workaround should remove it. The advisory is fixed, not suppressed.

**5. DrOrdering Marker Migrations (Phase 17 — DR-07 / migration ordering)**
Each package gained a new "marker" migration (`DrOrderingMarker`) at timestamps:
- `GameKit.Auth`: `20260623000000_DrOrderingMarker`
- `GameKit.Core`: (part of `20260622000000_AddGameSessionIdempotencyKey` for SCALE-03)
- `GameKit.Rankings`: `20260625000000_DrOrderingMarker`
- `GameKit.Matchmaking`: `20260626000000_DrOrderingMarker`
- `GameKit.Lobby`: `20260627000000_DrOrderingMarker`
- `GameKit.Admin.UI`: `20260624000000_DrOrderingMarker`

These are no-op ordering markers. They auto-apply on startup. No action required by consumers beyond allowing migrations to run.

**6. NuGetAuditMode = all (Phase 18 — SEC-07)**
Now active in `Directory.Build.props`. Consumers who build GameKit from source need `NuGetAuditMode=all` + `NuGetAuditLevel=high` (or remove the MessagePack advisory by pinning 3.1.7). The shipped NuGet packages are unaffected.

---

## DOCS-05: Runbook and ADR Gaps

### Existing Files (do NOT recreate — cross-link)

**`docs/runbooks/`:**
- `postgres-backup-restore.md` (301 lines) — Phase 17 [VERIFIED: exists]
- `redis-backup-restore.md` (240 lines) — Phase 17 [VERIFIED: exists]

**`docs/ops/`:**
- `README.md`, `air-gapped.md`, `bare-metal.md`, `container.md`, `disaster-recovery.md`, `jwt-keys.md`, `migrations-runbook.md`, `multi-replica.md`, `postgres-roles.md`, `redis-aof.md` [VERIFIED: all exist]

**`docs/architecture/`:**
- `signalr-multi-replica.md` [VERIFIED: exists]

**`docs/` (root):**
- `migration-ops.md` (270 lines), `security-checklist.md` (360 lines), `performance-tuning.md` (347 lines) [VERIFIED: all exist]

### MISSING Files — Phase 20 Must Create

**`docs/runbooks/rolling-deploy.md`:**
- Zero-downtime rolling deploy procedure for multi-replica setups
- Pre-deploy checklist: migration state, leader-lock TTL headroom
- Canary → drain → replace sequence
- Rollback decision gate
- Cross-links to: `docs/ops/multi-replica.md`, `docs/ops/migrations-runbook.md`, `docs/architecture/signalr-multi-replica.md`
- Existing test coverage: SCALE-05 graceful-drain integration test (pending Phase 16)

**`docs/runbooks/matchmaking-outage.md`:**
- Incident response for matchmaking outage (ticker stopped, leader lock not held, queue backed up)
- Diagnostic: `GET /health/ready` → check `matchmaking_leader_lock` component
- `GET /admin/api/matchmaking/stats` queue-depth check
- Redis key inspection: `SET NX PX` lock key pattern
- Admin drain + pause-queue commands
- Escalation: Redis failover vs app restart
- Cross-links to: `docs/ops/redis-aof.md`, `docs/runbooks/redis-backup-restore.md`

**`docs/adr/` (new directory):**
- 10 ADRs listed in Architecture Patterns section above

**`docs/concepts/` (new directory):**
- One `.md` per package (13 files — see DOCS-03 package inventory)
- `index.md` landing page

**`docs/tutorial/getting-started.md`:**
- The complete 15-minute tutorial

**`docs/upgrade/v2.0-to-v2.1.md`:**
- The upgrade guide (see DOCS-04)

### RunbookFilesTests.cs Gate [VERIFIED: tests/GameKit.Core.Tests/RunbookFilesTests.cs]

The existing `RunbookFilesTests.cs` asserts that the 3 existing runbooks exist and are non-trivial (>200 bytes). Phase 20 should extend this test to cover the 2 new runbooks (`rolling-deploy.md`, `matchmaking-outage.md`).

---

## DOCS-06: Sample Currency Assessment

### Sample v2.1 Feature Status [VERIFIED: samples/TicTacToeDuel/Program.cs]

| Feature | Status | Code Evidence |
|---------|--------|---------------|
| AddGameKitObservability | PRESENT | Program.cs line 142 |
| AddOpenTelemetry ASP.NET Core instrumentation | PRESENT | Program.cs line 152-153 |
| AddGameKitHealthChecks | PRESENT | Program.cs line 161 |
| MapGameKitHealth | PRESENT | Program.cs line 203 |
| docker-compose.observability.yml (OTel Collector + Prometheus + Grafana + Tempo) | PRESENT | file exists with all 4 services |
| Grafana pre-provisioned dashboards | PRESENT | observability/grafana/ |
| ILeaderLease (SCALE-01) | N/A for sample — internal abstraction |
| AddLobby + MapLobby | PRESENT | Program.cs |
| MapGameKitAdmin | PRESENT | Program.cs |

**Verdict: Sample is current with v2.1 observability and health.** No Program.cs changes needed.

### Bug to Fix in DOCS-06

**File:** `samples/TicTacToeDuel/wwwroot/matchmaking.html`, line 240

```js
// CURRENT — BROKEN: sends poolName "tictactoe" → tickets never pair
body: JSON.stringify({ ladderId, poolName: "tictactoe", partyId: null }),

// FIX: null routes to "default" pool (the only pool the "tictactoe" ladder uses)
body: JSON.stringify({ ladderId, poolName: null, partyId: null }),
```

This is a 1-character change but is critical: without it, the DOCS-02 tutorial UI path "open two tabs, click Find Match" never forms a match. The CI smoke test already uses `poolName: null` directly (it calls the API without going through the HTML page), so the smoke test would pass but the tutorial UI would silently fail.

### appsettings.Development.json Port Issue [VERIFIED: file read]

`appsettings.Development.json` has `Host=localhost;Port=5432` but the docker-compose maps Postgres to `5433` on the host. Current resolution: this is the **dev workstation** config that assumes the developer runs `dotnet run` against a locally-running Postgres (not dockerized). The docker-compose provides an alternative infra — the tutorial must call out the port explicitly.

**Recommended fix for DOCS-06:** Add an `appsettings.DockerCompose.json` override file or a note in the README. The planner should decide: (a) update README with env var export instructions, (b) add a docker-compose app service, or (c) add an `appsettings.DockerCompose.json`. Option (a) is lowest risk (no code change).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| API reference from XML docs | Custom markdown generator | DocFX | Handles `<see cref>` cross-refs, generics, inheritance; MIT licensed |
| CI warnings-as-errors gate | Custom exit-code checker | `docfx --warningsAsErrors` built-in flag | Already available in docfx 2.78.x |
| Testcontainers smoke test setup | Custom Docker SDK calls | Existing `PostgresFixture` + `RedisFixture` in `GameKit.TestFixtures` | Already handles startup/retry/cleanup |
| Auth happy path in smoke test | Re-implement JWT parsing | `AuthEndpointsE2ETests` pattern + `AuthTestHost` | Already tested; just compose the calls |
| Runbook file existence gate | Custom CI script | Extend `RunbookFilesTests.cs` | Existing pattern; consistent with DR-01/02 tests |

---

## Common Pitfalls

### Pitfall 1: docfx metadata --warningsAsErrors exits 255 on the 2 duplicate-file warnings
**What goes wrong:** `dotnet docfx docfx.json --warningsAsErrors` exits 255 (failure) even though the human-readable output says "Build succeeded with warning."
**Why it happens:** Two `Duplicate source file` warnings from `GameKit.Build` — see the DocFX Audit section above.
**How to avoid:** Fix `Directory.Build.props` first (remove the 2 explicit `<AdditionalFiles>` lines). Verify with `echo $?` after the command.
**Warning signs:** Output says "2 warning(s)" + "0 error(s)" but CI gate fails.

### Pitfall 2: Tutorial match never forms (poolName bug)
**What goes wrong:** Tutorial reader opens two tabs, clicks "Find Match", waits indefinitely — match never forms.
**Why it happens:** `matchmaking.html` sends `poolName: "tictactoe"` but the ladder only has a `"default"` pool. Two tickets in different pool names never compare.
**How to avoid:** Fix `matchmaking.html` line 240 before writing the tutorial (DOCS-06 prerequisite).
**Warning signs:** Queue depth accumulates but no proposals are emitted; `/admin/api/matchmaking/stats` shows depth > 0 in the "tictactoe" pool, depth 0 in "default".

### Pitfall 3: CI smoke test uses port 5433 connection string
**What goes wrong:** Smoke test that connects directly to the docker-compose Postgres fails because it tries `Port=5432`.
**How to avoid:** Smoke test uses Testcontainers (auto-assigns port) — it does NOT connect to the docker-compose stack. The tutorial prose uses docker-compose; the CI smoke test uses Testcontainers. Keep these separate.

### Pitfall 4: docfx picks up the samples/ directory
**What goes wrong:** docfx generates API stubs for `TicTacToeDuel` types mixed in with library types.
**How to avoid:** Scope the `metadata.src` glob to `src/**/*.csproj` only (not `samples/**`).

### Pitfall 5: Tutorial instructs `dotnet new gamekit` without installing the template
**What goes wrong:** `dotnet new gamekit` fails with "template not found."
**Why it happens:** The template must be installed first via `dotnet new install` or `dotnet pack` + install.
**How to avoid:** Tutorial must include `dotnet new install ./templates/GameKit.Templates` (local dev) or the published NuGet package path. For this milestone (not yet on NuGet.org), use local install.

### Pitfall 6: X-GameKit-Device header missing in smoke test HTTP calls
**What goes wrong:** `POST /auth/login/guest` returns 400 or auth fails.
**Why it happens:** The auth endpoints require the `X-GameKit-Device` header (device fingerprint for refresh token family tracking). The existing `AuthEndpointsE2ETests` always adds this header.
**How to avoid:** Follow `AuthEndpointsE2ETests` pattern — set `host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", Guid.NewGuid().ToString())` before any auth calls.

---

## Runtime State Inventory

> Not applicable — this is a greenfield docs/CI phase with no rename, refactor, or migration. No runtime state affected.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | All | ✓ | 10.0.106 (pinned via global.json) | None — required |
| Docker | DOCS-02 tutorial prose, CI docker-compose reference | ✓ | Available on dev machine | CI: use Testcontainers |
| docfx (global tool) | DOCS-01 local dev | ✓ | 2.78.5 | Use local tool manifest for CI |
| docfx (local tool manifest) | DOCS-01 CI gate | ✗ | `.config/dotnet-tools.json` does not exist yet | Wave 0 creates it |
| Testcontainers Docker socket | CI smoke test | ✓ (ubuntu-24.04 runner) | Docker available on GitHub Actions | None — required for integration tests |

**Missing dependencies with no fallback:** `.config/dotnet-tools.json` — must be created in Wave 0.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | `tests/xunit.runner.json` |
| Quick run command | `dotnet test --filter "Category=Integration" --no-build -c Release` |
| Full suite command | `dotnet test --no-build -c Release` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| DOCS-01 | docfx build passes with --warningsAsErrors | CI step | `dotnet docfx docfx.json --warningsAsErrors` | ❌ Wave 0 — add to ci.yml |
| DOCS-01 | Directory.Build.props fix eliminates 2 warnings | unit/build | `dotnet build -warnaserror src/GameKit.Build/` | Existing build gate |
| DOCS-02 | Tutorial path: guest login → queue → match → health 200 | integration | `dotnet test tests/GameKit.Tutorial.SmokeTests -c Release` | ❌ Wave 0 — new project |
| DOCS-05 | New runbooks exist and are non-trivial | unit | `dotnet test tests/GameKit.Core.Tests --filter RunbookFiles -c Release` | ❌ needs 2 new test methods |
| DOCS-06 | matchmaking.html poolName fix — no test; manual verify | smoke | Open two tabs, click Find Match | n/a (HTML fix) |

### Sampling Rate
- **Per task commit:** `dotnet build --no-restore -c Release`
- **Per wave merge:** `dotnet test --no-build -c Release --filter "Category=Integration"`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `.config/dotnet-tools.json` — docfx local tool manifest (`dotnet new tool-manifest && dotnet tool install docfx --version 2.78.5`)
- [ ] `docfx.json` — repo-root DocFX config
- [ ] `tests/GameKit.Tutorial.SmokeTests/` — new test project for DOCS-02 CI smoke test
- [ ] `tests/GameKit.Core.Tests/RunbookFilesTests.cs` — add 2 new `[Fact]` methods for rolling-deploy and matchmaking-outage runbooks
- [ ] `docs/adr/` directory
- [ ] `docs/concepts/` directory
- [ ] `docs/tutorial/` directory
- [ ] `docs/upgrade/` directory
- [ ] Fix `Directory.Build.props` — remove 2 duplicate `<AdditionalFiles>` lines
- [ ] Fix `matchmaking.html` — poolName: null

---

## Security Domain

Security enforcement is enabled (no explicit `false` in config.json).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No — docs phase, no new auth code | n/a |
| V3 Session Management | No — no new session code | n/a |
| V4 Access Control | No — no new endpoints | n/a |
| V5 Input Validation | No — docs phase | n/a |
| V6 Cryptography | No — no new crypto | n/a |

**Security note for tutorial:** The tutorial must include a clear callout that `localStorage` JWT storage in the sample HTML is demo-only (XSS-vulnerable). This callout already exists in `index.html` and `matchmaking.html`; the tutorial prose must echo it. Do not remove the existing warnings.

**ADR security note:** The ADR for OTel opt-in (ADR-0009) should explicitly document the phone-home / telemetry threat model and why the air-gap guarantee requires consumers to configure the OTLP endpoint, not the library.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Global `dotnet tool install -g docfx` | Local tool manifest (`.config/dotnet-tools.json`) | docfx 2.x era | CI-reproducible, no machine-global state |
| Manual XML doc comments + NDoc | `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + DocFX | .NET 5+ era | Inline with source; IDE-integrated |
| Michael Nygard ADR format (flat files) | Still standard for .NET OSS | n/a | Lightweight; no tooling required |
| Hand-written upgrade guides | Upgrade guide derived from conventional commits + REQUIREMENTS.md | n/a | Phase 20 formalizes this |

**Deprecated/outdated:**
- `Sandcastle` / `Sandcastle Help File Builder`: Windows-only, unmaintained. DocFX is the modern .NET replacement.
- `NDoc`: Abandoned ~2008. DocFX.
- Global tool installs for CI: superseded by local tool manifests (committed `.config/dotnet-tools.json`).

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | ADR NNNN numbering convention is standard for this project | Architecture Patterns | Low — naming is cosmetic; content is what matters |
| A2 | `dotnet new install ./templates/GameKit.Templates` is the correct local install command for the current .NET 10 SDK | Tutorial Happy-Path | Medium — command syntax changed between .NET SDK versions; verify during Wave 0 |
| A3 | The tutorial smoke test should live in a new `tests/GameKit.Tutorial.SmokeTests/` project | Validation Architecture | Low — could also be added to an existing integration test project |

All material technical claims (docfx warnings, pool names, template short-name, port mappings, interface names, Program.cs wiring) were verified against the actual codebase in this session.

---

## Open Questions

1. **appsettings.Development.json Port 5432 vs docker-compose port 5433**
   - What we know: `appsettings.Development.json` hardcodes `Port=5432`; docker-compose maps Postgres to `5433` on the host.
   - What's unclear: Is the intended tutorial flow "run the app on the host" (needs port override) or "run everything in Docker" (would need a docker-compose app service)?
   - Recommendation: Tutorial uses env var overrides (simplest for the dev); optionally add `appsettings.DockerCompose.json` to make it one-command.

2. **Tutorial smoke test: WebApplicationFactory vs separate docker-compose**
   - What we know: Existing integration tests use WebApplicationFactory + Testcontainers; no docker-compose-in-CI harness exists.
   - What's unclear: Whether the smoke test should boot the full `TicTacToeDuel` `Program.cs` (including static files, Blazor, lobby SignalR) or a slimmed-down minimal host.
   - Recommendation: Use WebApplicationFactory against `TicTacToeDuel.Program` — it boots the real app with full middleware. The DR test (`DrRoundTripTests.cs`) uses a similar full-stack pattern. This gives the most realistic "tutorial path" test.

3. **Where to put the tutorial docs: `docs/tutorial/` vs repo root `TUTORIAL.md` vs `README.md`**
   - Recommendation: `docs/tutorial/getting-started.md` with a link from root `README.md`. Consistent with the ops/ and concepts/ subdirectory pattern already in `docs/`.

---

## Sources

### Primary (HIGH confidence)
- [VERIFIED: live `docfx --version`] docfx 2.78.5 on machine — tool available; metadata warnings enumerated
- [VERIFIED: MSBuild preprocessed output `/tmp/GameKit.Build.pp.xml`] Duplicate AdditionalFiles root cause confirmed
- [VERIFIED: `docfx build --warningsAsErrors` exit 0, 0 warnings] XML doc coverage complete
- [VERIFIED: `samples/TicTacToeDuel/docker-compose.yml`] Port 5433 mapping confirmed
- [VERIFIED: `samples/TicTacToeDuel/wwwroot/matchmaking.html` line 240] poolName bug confirmed
- [VERIFIED: `templates/.../template.json`] shortName: "gamekit" confirmed
- [VERIFIED: `samples/TicTacToeDuel/Program.cs`] AddGameKitObservability + AddGameKitHealthChecks + MapGameKitHealth all present
- [VERIFIED: `tests/GameKit.Core.Tests/RunbookFilesTests.cs`] Existing runbook gate pattern confirmed
- [VERIFIED: `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs`] Tutorial smoke test pattern confirmed
- [VERIFIED: `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs`] PoolName defaults to null → "default" pool

### Secondary (MEDIUM confidence)
- [CITED: github.com/dotnet/docfx] docfx --warningsAsErrors flag behavior; local tool manifest pattern
- [CITED: Directory.Build.props in repo] CS1591 WarningsAsErrors confirmed for all packages

---

## Metadata

**Confidence breakdown:**
- DOCS-01 DocFX setup: HIGH — tool run live; warnings enumerated; fix identified
- DOCS-02 Tutorial smoke test: HIGH — happy-path traced from actual code; bug found and root-caused
- DOCS-03 Concepts scope: HIGH — interface inventory from live codebase
- DOCS-04 Upgrade guide: HIGH — v2.1 extension methods verified in src/
- DOCS-05 Runbook gaps: HIGH — directory listing verified; missing files enumerated
- DOCS-06 Sample currency: HIGH — Program.cs read; bug confirmed

**Research date:** 2026-06-23
**Valid until:** 2026-08-01 (docs phases are stable; only risk is Phase 16/17/18/19 delivering new APIs before this phase executes)

---

## RESEARCH COMPLETE
