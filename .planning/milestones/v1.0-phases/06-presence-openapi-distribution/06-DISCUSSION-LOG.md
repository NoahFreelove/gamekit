# Phase 6: Presence + OpenAPI + Distribution - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-25
**Phase:** 6-Presence + OpenAPI + Distribution
**Areas discussed:** Presence shape, OpenAPI doc structure, Template + SampleGame, Release train + ops guide

---

## Presence shape

### Heartbeat TTL + client cadence

| Option | Description | Selected |
|--------|-------------|----------|
| TTL=30s, ping every 10s | Tight default. 3× safety factor. Higher Redis write rate (~0.1 writes/sec/player). Arena-style games. | ✓ |
| TTL=60s, ping every 20s | Balanced. 3× safety factor. Half the Redis writes. Typical session-based games. | |
| TTL=120s, ping every 45s | Lazy. ~2.7× safety. Light on Redis. Asynchronous / lobby-heavy games. | |
| Consumer picks via options | Ship default + expose `GameKitPresenceOptions.{TtlSeconds, HeartbeatIntervalSeconds}` with validator. | |

**User's choice:** TTL=30s, ping every 10s
**Notes:** Tight 'offline within 30s' detection. Validator-backed options still added as the consumer escape hatch — the choice fixes only the default.

---

### Route prefix + in-match trigger

| Option | Description | Selected |
|--------|-------------|----------|
| `/api/presence/heartbeat` + `/start` sets in-match | Consistent with `/api/sessions/`, `/api/mm/`, `/api/parties/`. Treat ROADMAP SC#1 wording as typo. | ✓ |
| `/presence/heartbeat` + `/start` sets in-match | Bare `/presence` prefix per ROADMAP literal. 3 prefix conventions: `/auth`, `/presence`, `/api/*`. | |
| `/api/presence/heartbeat` + `/abandon`-also sets in-match | Take ROADMAP SC#1 at face value. Unintended reading. | |

**User's choice:** `/api/presence/heartbeat` + `/start` sets in-match (Recommended)
**Notes:** ROADMAP SC#1 wording flagged as typo in CONTEXT.md `<specifics>`. Plan-01 to either correct ROADMAP wording or revert the Core XML doc — DOC and CODE must agree.

---

### Admin presence panel shape

| Option | Description | Selected |
|--------|-------------|----------|
| Top-25, 10s refresh, table | Reuses Panel.RefreshInterval=10s. MudDataGrid PlayerId/DisplayName/Status/LastSeen, sortable. Fits without scroll. | ✓ |
| Top-50, 10s refresh, table | Same UX, denser. May scroll on smaller viewports. | |
| Top-100, 30s refresh, virtualized | MudVirtualize. Slower refresh to keep Redis traffic low. High-pop servers. | |
| Top-10, 5s refresh, card grid | MudCard per player. 'Who's playing right now' glance, not analytical. | |

**User's choice:** Top-25, 10s refresh, table layout (Recommended)
**Notes:** No new options — reuses existing `GameKitAdminOptions.Panel.RefreshInterval`. Missing-package uses the existing `MissingPackageAlert.razor` pattern.

---

### Multi-device semantics + heartbeat rate-limit

| Option | Description | Selected |
|--------|-------------|----------|
| Player-keyed LWW, no rate limit | Single `presence:{playerId}` Redis key. Heartbeat cheap (one SETEX), no rate limit. Simplest contract. | ✓ |
| Per-device aggregated, no rate limit | Per-device key with X-GameKit-Device fingerprint. Online if ANY device fresh. Better fidelity. | |
| Player-keyed + 20/min/player rate limit | Add `gamekit:presence:heartbeat` policy. Defends against runaway client DoS. | |

**User's choice:** Player-keyed last-write-wins, no rate limit (Recommended)
**Notes:** Per-device aggregation deferred to v2 (not a v1 stakeholder ask). Runaway clients hit the same Kestrel queue as legitimate traffic.

---

## OpenAPI doc structure

### Document shape

| Option | Description | Selected |
|--------|-------------|----------|
| Single combined `/openapi/v1.json` | One doc. Endpoints tagged by package. Matches 'install only what you need'. | ✓ |
| Per-package docs | `/openapi/{pkg}/v1.json`. Cleaner boundary but doubles wiring. | |
| Combined + per-package both | Maximum flexibility, doubles maintenance. | |

**User's choice:** Single combined `/openapi/v1.json` (Recommended)
**Notes:** Doc reflects whatever packages the consumer registered.

---

### Security schemes + admin endpoint visibility

| Option | Description | Selected |
|--------|-------------|----------|
| JWT only, admin excluded | Public doc shows only JWT bearer. `/admin/api/*` filtered out. Matches Phase 3 D-04 '404 in Production'. | ✓ |
| Both schemes, all endpoints public | bearerAuth + adminCookieAuth+csrfHeader. Most accurate but exposes admin route shapes. | |
| JWT only, admin filtered when env != Development | Env-conditional. In Dev includes admin routes; in Staging/Prod filtered. | |

**User's choice:** JWT bearer only; admin endpoints excluded from public doc (Recommended)
**Notes:** Whether to ship `/openapi/admin/v1.json` gated behind admin cookie scheme is a Plan-time open option — v1 default is NO.

---

### Contract test mechanism

| Option | Description | Selected |
|--------|-------------|----------|
| EndpointDataSource enumeration | First-party ASP.NET Core API. Survives endpoint refactors. | ✓ |
| Reflection scan of Map* methods | String-matches route literals; fragile under refactor. | |
| Hand-maintained route allowlist | Catches accidental missing-OpenApi but adds review friction. | |

**User's choice:** EndpointDataSource enumeration (Recommended)
**Notes:** Lives in `tests/GameKit.OpenApi.Integration.Tests/`.

---

### Route-prefix normalization + versioning

| Option | Description | Selected |
|--------|-------------|----------|
| Document as-is, no version prefix | Reflect what shipped. info.version = MinVer-derived. /v1/ deferred to v2. | ✓ |
| Normalize to `/api/v1/*` + 308-redirects | Cleanest spec but hidden migration cost. | |
| Document as-is + deprecation note | No code change; info.description warns v2 will normalize. | |

**User's choice:** Document as-is, no version prefix (Recommended)
**Notes:** `info.version` field encodes the GameKit package MinVer-derived version.

---

## Template + SampleGame

### Template body

| Option | Description | Selected |
|--------|-------------|----------|
| Full TicTacToeDuel clone | Working game end-to-end. Highest 'wow'. Template heavy (~30 files). | ✓ |
| Leaner 'hello GameKit' skeleton | Minimal scaffold with one Razor page. ~6 files. Faster to grok. | |
| Two templates: gamekit + gamekit-sample | Both. Maximum flexibility, double maintenance. | |

**User's choice:** Full TicTacToeDuel clone (Recommended)
**Notes:** Template lives at `templates/GameKit.Templates/content/GameKit.SampleGame/`.

---

### Template parameters

| Option | Description | Selected |
|--------|-------------|----------|
| Minimal: --name | Standard -n only. Lowest cognitive load. | |
| Minimal + --skip-auth/--skip-rankings/--skip-matchmaking/--skip-presence | Boolean opt-outs per non-Core package. | ✓ |
| Minimal + --port + --postgres-host + --redis-host | Three connection-string knobs. Useful if non-default. | |

**User's choice:** Minimal + --skip-* per package
**Notes:** Lets developers scaffold 'just Core+Auth' for a leaderless game. `--port` / `--postgres-host` / `--redis-host` deferred (consumers edit `appsettings.json` if non-default).

---

### DIST-02 test home

| Option | Description | Selected |
|--------|-------------|----------|
| New `tests/GameKit.Distribution.Integration.Tests/` project | Dedicated project for distribution/ops invariants. Mirrors existing per-package pattern. | ✓ |
| Add to existing `GameKit.Core.Integration.Tests` | Keeps test count down but muddies Core's scope. | |
| Single-file loose test in `tests/` root | Lightest but breaks per-project collection-fixture pattern. | |

**User's choice:** New `tests/GameKit.Distribution.Integration.Tests/` project (Recommended)
**Notes:** Also houses DIST-03 SampleGame smoke test, OPS-04 GameKitVersion mismatch test, OPS-06 clean-install migration test.

---

### DIST-03 game-server side scope

| Option | Description | Selected |
|--------|-------------|----------|
| Add second-process game-server console app | `samples/TicTacToeDuel.GameServer/` using `gamekit_reader`. Real production topology. | ✓ |
| Reader-role demo inside existing web app | Lighter; one class. Doesn't feel like 'game-server' to operators. | |
| Document-only demo | README diagram + grant table. No extra code. Weakest 'show me'. | |

**User's choice:** Add second-process game-server console app (Recommended)
**Notes:** Template clones both web + GameServer projects.

---

## Release train + ops guide

> **Auto-picked by Claude per user instruction "proceed with auto for this discuss phase".** Picks favor the lowest-risk option consistent with prior phase patterns (hosted-service migration model, MinVer build target patterns, multi-page docs structure).

### GameKitVersion stamping mechanism

| Option | Description | Selected |
|--------|-------------|----------|
| Roslyn source generator (new src/GameKit.Build/) | Compile-time const emission from MinVer $(Version). ProjectRef as Analyzer (no shipped assembly). | ✓ |
| AssemblyInformationalVersion + runtime reflection | No new project but runtime string parsing on every call. | |
| Hand-maintained shared GameKitVersion.cs partial | Manual sync per release; high drift risk. | |

**Claude's auto-pick:** Roslyn source generator
**Notes:** Const intern-pooled at JIT. Generator runs at compile-time only; output items typed `Analyzer` with `ReferenceOutputAssembly=false` so the generator does NOT ship.

---

### Mismatch detection

| Option | Description | Selected |
|--------|-------------|----------|
| `GameKitVersionAssertionHostedService` in IHost.StartAsync | Fail-fast before Kestrel. Same pattern as existing Auth/Admin/Mm migration hosted services. | ✓ |
| Static-ctor assertion in AddGameKit | Earliest possible — but harder to surface to logs. | |
| Module initializer per assembly | Most aggressive — fires at JIT load of EACH GameKit.* assembly. Hard to suppress in tests. | |

**Claude's auto-pick:** Hosted service in IHost.StartAsync
**Notes:** Iterates `AppDomain.CurrentDomain.GetAssemblies()` filtered to GameKit.*; throws `GameKitVersionMismatchException` listing divergent (assembly, version) tuples. Registered automatically by `AddGameKit()` in Core.

---

### Exact-pin enforcement

| Option | Description | Selected |
|--------|-------------|----------|
| Custom MSBuild target (GameKit.targets) during Pack | Emits exact-pin sibling PackageReferences from ProjectReferences during pack. Single source of truth = the build. | ✓ |
| Pre-commit hook validation | Lightweight but only catches at commit time, not at `dotnet pack` time. | |
| Manual Directory.Packages.props maintenance + CI grep | Highest drift risk; manual per-release update. | |

**Claude's auto-pick:** MSBuild target during Pack
**Notes:** Imported into Directory.Build.props. CI asserts every produced `.nuspec` contains `Version="[X.Y.Z]"` literal square brackets for every sibling ref. Wildcard CI guard `! grep -rE 'Version="(\*|\^)' src/**/*.csproj`.

---

### Ops guide structure

| Option | Description | Selected |
|--------|-------------|----------|
| Multi-page `docs/ops/` | File-per-recipe. README index. Deep-linkable, low single-file maintenance cost. | ✓ |
| Single `docs/OPS.md` (3000+ lines) | One scroll-or-search target. Hard to deep-link, brittle to maintain. | |
| `docs/ops/` + topic clusters (longer files) | 3-4 mid-size files. Compromise. | |

**Claude's auto-pick:** Multi-page docs/ops/
**Notes:** README.md (index), bare-metal.md, container.md, air-gapped.md, postgres-roles.md, redis-aof.md, jwt-keys.md, disaster-recovery.md, migrations-runbook.md. Repo-root README.md gets a "Production Deployment" section linking to `docs/ops/README.md`.

---

## Claude's Discretion

Per user's "proceed with auto" instruction the entire **Release train + ops guide** area was auto-picked (see above). Within the user-decided areas, the following sub-decisions were also delegated to Claude:

- HTTP request body for `/api/presence/heartbeat` (default: empty body since JWT already identifies the player).
- `LastSeen` column format in the Admin panel (default: relative "3 seconds ago").
- OPS-06 clean-install migration test scope (default: single-shot against fresh empty Postgres).
- Whether `GameKit.Build` source generator emits a `[GameKitInfo]` attribute alongside the const (default: const-only).

## Deferred Ideas

- Per-device aggregated presence with X-GameKit-Device fingerprint — v2.
- Presence-driven analytics (player-session-time histograms) — v2 admin feature.
- `/api/v2/*` prefix normalization + `/auth/*` legacy 308-redirect plumbing — v2 migration.
- `/openapi/admin/v1.json` separate admin-cookie-gated doc — Plan-time open option.
- Second `gamekit-skeleton` minimal template — v1.1 backlog.
- Argon2 password hasher sibling package (`GameKit.Auth.Argon2`) — v2 per AUTH-V2-01.
- `[GameKitInfo]` assembly attribute alongside source-gen-emitted const — Plan-time deferred (default const-only).
