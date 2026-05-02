# ROADMAP: GameKit

**Project:** GameKit — GPL, self-hostable, composable .NET 10 game-services library (6 NuGet packages)
**Created:** 2026-04-15
**Granularity:** standard (6 phases, 3-5 plans each)
**Mode:** YOLO (parallel execution, Quality model profile)
**Coverage:** 92/92 v1 requirements mapped

## Phase 1 Pre-Flight (Verify Before Committing Versions)

The .NET 10 LTS runtime was released yesterday (2026-04-14). Before Phase 1 pins its version matrix:

- [ ] Verify `Npgsql.EntityFrameworkCore.PostgreSQL` has a stable `net10.0` TFM in its NuGet package assets (fall back to RC/preview build with an explicit tracking issue if not yet GA).
- [ ] Verify `AspNet.Security.OpenId.Steam` 10.0.x and `AspNet.Security.OAuth.Discord` 10.0.x expose `net10.0` TFM (aspnet-contrib typically ships within days of a .NET LTS, but verify on NuGet before Phase 2).
- [ ] Verify `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Polly`, `FluentValidation` 12, `Scrutor`, `MinVer` 6, `Microsoft.SourceLink.GitHub` all resolve cleanly on `net10.0`.
- [ ] If any dependency is not yet GA for .NET 10, record the workaround (pinned preview, version range, compatibility shim) in Phase 1 STATE before the first migration is authored.

## Phases

- [ ] **Phase 1: Foundation (Core + Migrations + Ops Defaults + GPL)** - Ships `GameKit.Core`, per-package migrations pattern, three-role Postgres, GPL headers, and the zero-egress runtime guard.
- [x] **Phase 2: Authentication** (2026-04-18) - Ships `GameKit.Auth` with Steam/Discord/Guest/Password providers, JWT + refresh rotation with reuse-interval grace, and SERIALIZABLE guest upgrade.
- [ ] **Phase 3: Admin UI** - Ships `GameKit.Admin.UI` Blazor Server RCL with default-deny mount, player search, ban/unban, audit log, and scaffolding for later panels.
- [ ] **Phase 4: Rankings + Sessions Wiring + GDPR Export** - Ships `GameKit.Rankings` with windowed Glicko-2, seasonal reset, idempotent session-complete, and GDPR export endpoint.
- [ ] **Phase 5: Matchmaking + Parties** - Ships `GameKit.Matchmaking` with Redis-lease queue, party-aware strategy, reconciliation, leader election, chaos + load tests.
- [ ] **Phase 6: Presence + OpenAPI + Distribution** - Ships `GameKit.Presence`, OpenAPI spec, `dotnet new gamekit` template, SampleGame reference, ops guide, and coordinated release train.

## Phase Details

### Phase 1: Foundation (Core + Migrations + Ops Defaults + GPL)

**Goal**: Prove the multi-package migration pattern on `GameKit.Core` and establish the license, ops, and anti-egress defaults that govern every subsequent phase.

**Depends on**: Nothing (first phase; gated by .NET 10 ecosystem pre-flight above)

**Requirements**: CORE-01, CORE-02, CORE-03, CORE-04, CORE-05, CORE-06, CORE-07, CORE-08, CORE-09, CORE-10, CORE-11, CORE-12, CORE-13, CORE-14, CORE-15, CORE-16, CORE-17, OPS-01, OPS-02, OPS-03, OPS-06, OPS-07, OPS-08, OPS-09, OPS-10, DIST-01

**Success Criteria** (what must be TRUE):
  1. A developer can `dotnet add package GameKit.Core` onto an empty ASP.NET Core 10 app, call `AddGameKit().UseGameKit()`, point at a fresh Postgres, and observe `gamekit` schema created with `players`, `game_sessions`, `session_participants`, and `admin_audit_log` tables plus a `__ef_migrations_core` history table.
  2. The CI clean-install integration test installs `GameKit.Core` onto an empty Testcontainers Postgres, runs `Database.Migrate()` deterministically, and asserts no model-snapshot drift across repeated runs.
  3. A GDPR delete integration test creates a player with sessions, deletes the player, and confirms opponent sessions still load with the deleted name replaced by a deterministic non-PII tombstone and `deleted_at` set.
  4. The runtime-guard integration test exercises Core's HTTP surface and asserts zero outbound HTTP connections are opened by the library itself (developer-configured providers excepted); the test fails loudly if any egress occurs.
  5. The shipped `docker-compose.yml` stands up Postgres with three roles (`gamekit_owner`, `gamekit_app`, `gamekit_reader`) plus Redis with `--appendonly yes --appendfsync everysec`, and a role-isolation test asserts `gamekit_reader` cannot INSERT into `gamekit.game_sessions`.
  6. Every `.cs` file in `src/` carries a GPL header, `LICENSE` exists at the repo root, and a CI license-check job fails the build on any missing header.

**Plans**: 7 plans
- [x] 01-01-PLAN.md — Repo scaffolding: LICENSE (GPLv3), README, CONTRIBUTING (SPDX contract), Directory.Build.props (net10.0, MinVer 7, SourceLink 10, CS1591-error), Directory.Packages.props (CPM pins), global.json (SDK 10.0.106), GameKit.sln
- [x] 01-02-PLAN.md — docker-compose.yml: Postgres 17.9 with three-role init SQL (gamekit_owner/gamekit_app/gamekit_reader) + default privileges; Redis 8.6.2 with --appendonly yes --appendfsync everysec --maxmemory-policy noeviction
- [x] 01-03-PLAN.md — GameKit.Core entities: Player (CORE-06 REVISED no deleted_at per D-13), GameSession + state machine, SessionParticipant (NULLable PlayerId for GDPR), AdminAuditLog; IModelBuilderExtension interface; IEntityTypeConfiguration fluent configs (SetNull on player FK)
- [x] 01-04-PLAN.md — GameKitDbContext + GameKitModelCustomizer (IModelCustomizer replacement) + CoreDesignTimeFactory + MigrationRunner (advisory-lock wrapper) + initial migration 20260415000000_CoreInitial; MigrationsHistoryTable __ef_migrations_core in gamekit schema
- [x] 01-05-PLAN.md — Core runtime services + builder + endpoints + GameKit.Core.csproj FrameworkReference: AddGameKit/UseGameKit/MapGameKit; IClock/IIdGenerator/ICurrentPlayer/IPresenceProvider; GdprDeleteService (CORE-16 hard-delete + audit); PlayerDisplayNameResolver (Deleted Player tombstone); IGameKitRateLimitPolicies named constants; PlayerEndpoints
- [x] 01-06-PLAN.md — Siblings + CLI + SampleGame: five empty sibling csprojs (Auth/Rankings/Matchmaking/Presence/Admin.UI); GameKit.Cli (Spectre.Console.Cli; migrate functional; admin create stub; PackAsTool); SampleGame boot harness with three-role appsettings; Spectre.Console.Cli 0.49.1 pinned in Directory.Packages.props
- [x] 01-07-PLAN.md — CI + tests + CLAUDE.md fix: CI workflows (.github/workflows/ci.yml with unshare --net D-19 Layer 2 + license-check.yml); 7 Nyquist integration tests (MigrationDeterminism, HistoryIsolation, GdprDeleteTombstone, EgressGuard two-layer, RoleIsolation, RedisPersistence, AdvisoryLockKey); in-process CleanInstallMigrationTests + pack-and-install D-06 harness (skipped-by-default); unit tests; REUSE.toml; CLAUDE.md stack-table correction (.NET 10 / MinVer 7 / SourceLink 10)
**UI hint**: no

### Phase 2: Authentication

**Goal**: Players can authenticate via Steam, Discord, guest, or username/password, receive rotating JWTs with reuse-attack protection that does not force-logout legitimate mobile resumes, and upgrade guest accounts without race-induced identity corruption.

**Depends on**: Phase 1

**Requirements**: AUTH-01, AUTH-02, AUTH-03, AUTH-04, AUTH-05, AUTH-06, AUTH-07, AUTH-08, AUTH-09, AUTH-10, AUTH-11, AUTH-12, AUTH-13, AUTH-14, AUTH-15, AUTH-16

**Success Criteria** (what must be TRUE):
  1. An end-to-end integration test logs a user in via each of the four providers (Steam against a mocked OpenID 2.0 endpoint that exercises server-side `check_authentication`, Discord mocked to return `identify`-only claims, Guest anonymous, Username/Password with BCrypt), receives a JWT access token plus hashed refresh token, and completes a subsequent `/auth/refresh` that rotates via the `replaced_by` chain.
  2. A forged Steam callback (valid-looking `claimed_id`, bogus `sig`) is rejected by the Steam provider integration test.
  3. A concurrent-refresh test fires two `/auth/refresh` calls with the same token within the 30–60s reuse-interval grace window and matching client fingerprint: the user stays logged in (most recent child token returned), no family revocation fires; a separate test with a non-matching fingerprint outside the grace window correctly revokes the entire family.
  4. A concurrent guest-upgrade test spawns two simultaneous OAuth-link calls for the same guest player: exactly one succeeds inside the SERIALIZABLE transaction, the other fails loudly on the `(provider, external_id)` unique constraint, and no duplicate `players` row is created.
  5. Attempting to authenticate with an unrecognized identity while already holding a session returns a `link-or-switch` challenge rather than silently merging.
  6. Rate-limiter integration tests confirm `/auth/login`, `/auth/refresh`, and `/auth/register` return 429 under burst load from a single client.

**Plans**: 8 plans
- [x] 02-01-PLAN.md — Wave 0 test projects, WireMock Steam/Discord mocks, AuthIntegrationFixture, Directory.Packages.props pins for Auth stack
- [x] 02-02-PLAN.md — PlayerIdentity/PlayerCredential/RefreshToken entities + EF configurations + AuthInitial migration under __ef_migrations_auth; UNIQUE(provider, external_id) is the D-14 race anchor
- [x] 02-03-PLAN.md — GameKitAuthOptions/JwtOptions/SteamOptions/DiscordOptions; AddAuth fluent extension; EgressAllowListHandler + named HttpClients (D-07/D-08/D-10); UseAuthentication ordering fix
- [x] 02-04-PLAN.md — IPasswordHasher + BCryptPasswordHasher; JwtIssuer with D-03 claims; IsGuestResolver (D-13); RefreshTokenService with Pattern 3 rotation + 45s grace + fingerprint gate + family revocation + audit log
- [x] 02-05-PLAN.md — IOAuthProvider contract + SteamOAuthProvider + DiscordOAuthProvider (identify scope only); Scrutor discovery; auth scheme wiring for JwtBearer + Steam + Discord; mock-level forgery test
- [x] 02-06-PLAN.md — GuestOAuthProvider + PasswordOAuthProvider + GuestUpgradeService + IdentityLinker; concurrent-upgrade race test (success #4); cross-player collision test with ExternalIdHasher (success #5)
- [x] 02-07-PLAN.md — /auth/* minimal API endpoints + FluentValidation endpoint filter + rate-limit policies (login 10/min, refresh 60/min, register 5/min); WebApplicationFactory end-to-end tests for success #1, #3, #6 + end-to-end Steam forgery #2
- [x] 02-08-PLAN.md — TicTacToeDuel Program.cs AddAuth + startup hardening; HTML client X-GameKit-Device + JWT localStorage + 401-refresh-retry; README auth section with localStorage/XSS/signing-key disclaimers; human-verify checkpoint — 2026-04-18
**UI hint**: no

### Phase 3: Admin UI

**Goal**: An operator can mount a Blazor Server admin console at a chosen path, authenticate with a separate scheme from player JWTs, and perform audited player-management actions without ever exposing the console to unauthenticated traffic.

**Depends on**: Phase 2

**Requirements**: ADMIN-01, ADMIN-02, ADMIN-03, ADMIN-04, ADMIN-05, ADMIN-06, ADMIN-07, ADMIN-08, ADMIN-09, ADMIN-10, ADMIN-11, ADMIN-12

**Success Criteria** (what must be TRUE):
  1. An operator can `app.MapGameKitAdmin("/admin")`, bootstrap the first admin via the CLI (`dotnet gamekit admin create`), log in with a scheme distinct from the player JWT, and search for a player by id, display name, or identity provider+external_id.
  2. An unauthenticated request to `/admin` in the `Production` environment receives a `404` (not `401`), and a startup assertion fails fast if Admin UI is mounted with no admin role configured in Production.
  3. A ban action requires a reason, writes an `admin_audit_log` row with actor/action/target/before/after JSON, and the player is blocked from future auth; unban is symmetrically audited.
  4. The match-history, health (Postgres/Redis connectivity, recent error rate), rank-adjust, and queue-depth panels all render without error, with the rank-adjust and queue-depth panels displaying a clear "requires GameKit.Rankings / GameKit.Matchmaking" placeholder when those packages are absent.
  5. CSRF and CSP integration tests confirm mutations require a valid anti-CSRF token and that admin pages ship a CSP header blocking framing.
  6. A player JWT cannot authenticate into any admin endpoint (integration test asserts 404/403 regardless of valid player token).

**Plans**: 13 plans
- [x] 03-01-PLAN.md — Wave 0 test projects, AdminIntegrationFixture + cookie/CSRF helpers + FakePlayerJwtIssuer, Directory.Packages.props MudBlazor 9.3.0 pin, CLAUDE.md GameKit.Admin.UI section
- [x] 03-02-PLAN.md — AdminUser entity + EF configuration + AdminInitial migration under __ef_migrations_admin with live-verified advisory lock + AdminMigrationHostedService + design-time factory
- [x] 03-03-PLAN.md — RCL csproj rewrite (Microsoft.NET.Sdk.Razor + MudBlazor) + GameKitAdminOptions tree + AdminRoles/AdminPolicies/AdminAuthenticationSchemeConstants + marker type
- [x] 03-04-PLAN.md — AdminCookieEvents (404 in Prod / 302 in Dev / 403 on access-denied) + gamekit:admin:login sliding-window rate-limit 5/min/IP
- [x] 03-05-PLAN.md — AdminCspNonceMiddleware (128-bit per-request nonce + strict CSP policy) + AntiforgeryValidationFilter + ValidationEndpointFilter copy
- [x] 03-06-PLAN.md — IAdminAuditWriter + IAdminAuthService + IPlayerSearchService + IPlayerBanService + IAdminUserService + IHealthProbeService + ErrorRateRingBuffer + SuperadminGateHostedService + AddGameKitAdmin fluent builder + AdminTestHost
- [x] 03-07-PLAN.md — /admin/api/* minimal-API surface (12 endpoints) + 6 DTOs + 4 FluentValidation validators + login/search/antiforgery integration tests
- [x] 03-08-PLAN.md — Blazor shell: App.razor (nonce-aware) + Routes + MainLayout + LoginLayout + TopNav + SideNav + shared components + GameKitAdminTheme (UI-SPEC §Color) + global CSS
- [x] 03-09-PLAN.md — Blazor pages: Login + Dashboard + PlayerSearch + PlayerDetail + Audit + Matches + Health + QueueDepth + RankAdjust + Admins + 5 dialog components (ban/unban/gdpr/create-admin/delete-admin) per UI-SPEC §1–§13
- [x] 03-10-PLAN.md — Phase 2 ban-enforcement patches: BannedCheckHelper (SHA-256 reason hash) + 4 provider patches + RefreshTokenService family-revoke patch + BanEnforcementTests (D-03)
- [x] 03-11-PLAN.md — `dotnet gamekit admin create` CLI command (Spectre.Console flags + interactive + auto-promote first admin to superadmin + Console.ReadKey intercept)
- [ ] 03-12-PLAN.md — TicTacToeDuel sample wiring: AddGameKitAdmin + UseGameKitAdmin + MapGameKitAdmin("/admin") + README Admin UI section + human-verify 20-step walkthrough
- [x] 03-13-PLAN.md — E2E ROADMAP SC coverage: RoadmapScenarioTests (SC#1) + ProductionGateTests (SC#2) + CrossSchemeIsolationTests (SC#6) + CspAndAntiforgeryTests (SC#5) + PanelRenderTests (SC#4) + MountPathTests
**UI hint**: yes

### Phase 03.1: Admin UI redesign v2 (INSERTED)

**Goal:** Re-skin `GameKit.Admin.UI` to the Claude Design hi-fi prototype while preserving the functional contract (same routes, same `/admin/api/*` endpoints, same dialogs, same auth flow). Net result: violet-600 accent, density-aware token scale, master-detail Players page, two-column Audit row expansion, ⌘K command palette, runtime Tweaks panel, and a medium-loud ban banner — all driven by the source prototype at `.planning/sketches/admin-ui-redesign-v2/`.

**Source prototype:** `.planning/sketches/admin-ui-redesign-v2/` (HTML/CSS/JSX export from claude.ai/design)

**Scope deltas vs. Phase 03 v1:**
- Theme palette: indigo-600 → violet-600 (`#7C3AED`); add accent-50/100/700 ramps + 5 swappable accent presets (indigo / violet / teal / slate / orange)
- Density tokens: comfortable (40px row) + compact (32px row) variants exposed as `[data-density]` attribute
- Players page: split into master-detail with persistent left search list + right detail pane
- Audit page: row expansion replaces raw before/after JSON `<pre>` blocks with a two-column treatment (human-readable sentence on the left, structured key/value diff on the right)
- New global ⌘K command palette (navigate + run actions)
- New runtime Tweaks panel (accent / density / sidebar-state / ban-loudness / dashboard-direction)
- Medium ban banner across the top of the player detail page (red, 3 lines: reason / actor / timestamp)
- Restyled chip rail (filter primitive) for Audit + Players
- All 5 dialogs receive palette + density refresh
- Light-only; dark mode still deferred (token-semantic so it remains a mechanical swap)

**Out of scope (explicit):**
- No backend / endpoint / DTO / migration changes
- No new requirements (UI-only re-skin)
- No mobile reflow (desktop-only stays)
- No dark mode (deferred)
- 03-12 manual walkthrough is independent — this phase does not block on it

**Requirements**: ADMIN-02, ADMIN-03, ADMIN-05, ADMIN-06, ADMIN-09, ADMIN-12 (re-verified post-redesign — none changed semantically)
**Depends on:** Phase 03 (all admin source files this phase mutates were created there)
**Plans:** 8/9 plans executed

**Success Criteria** (what must be TRUE):
1. The shipped admin console visually matches the prototype at 1280px width: violet primary CTA, density tokens applied, master-detail Players layout, two-column Audit expansion, ban banner shape, command palette opens on ⌘K and routes to all 10 pages.
2. The Tweaks panel persists user choice across reloads (localStorage) and applies via `data-*` attributes on the root.
3. All Phase 3 integration tests still pass (no functional regression — the redesign is presentation-only).
4. New component tests cover: command palette routing, tweaks-panel state persistence, ban-banner render, master-detail synchronization between list and pane.
5. Bundle impact: no new NuGet packages; CSS payload increase ≤ 25 KB gzipped.
6. Accessibility: WCAG 2.1 AA preserved (focus rings, semantic landmarks, keyboard nav for command palette and tweaks panel).

Plans:
- [x] 03.1-01-PLAN.md — Wave 0 test scaffolding: bUnit 2.0.66 pin + 8 component test stubs (palette / tweaks / banner / workspace / sentence projector / accessibility / bundle-size)
- [x] 03.1-02-PLAN.md — Token foundation: gamekit-admin.css sketch port + GameKitAdminTheme violet-600 swap + App.razor inline tweaks-init script + 5 .razor.css migrations from --gk-color-* to sketch tokens
- [x] 03.1-03-PLAN.md — Vanilla JS bundle (window.GKAdmin IIFE) + App.razor body script tag + MainLayout DotNetObjectReference bridge for OpenDialog
- [x] 03.1-04-PLAN.md — AdminCommandRegistry + AdminCommandDto + GET /admin/api/commands + CommandPalette.razor + TopNav search-trigger + role-filtered bUnit tests
- [x] 03.1-05-PLAN.md — TweaksPanel.razor (5 radiogroups + reset) + MainLayout mount + TopNav Tune button + 4 live bUnit assertions
- [x] 03.1-06-PLAN.md — Players master-detail page + PlayerDetailPane shared + BanBanner shared + delete legacy PlayerSearch/PlayerDetail + 6 live bUnit facts
- [x] 03.1-07-PLAN.md — SentenceModel DTO + AuditSentenceTemplates registry (7 known + D-14 fallback) + AdminEndpoints AuditRow projection extension + Audit.razor 2-column row template
- [x] 03.1-08-PLAN.md — ChipRail.razor + 8 page re-skins + 5 dialog re-skins + 5 shared component re-skins + MissingPackageAlert literal substring preserved (ADMIN-09)
- [ ] 03.1-09-PLAN.md — Phase gate: AccessibilityTests live + full automated suite re-run + manual SC#1 visual walkthrough at 1280px + manual SC#6 axe DevTools sweep

### Phase 4: Rankings + Sessions Wiring + GDPR Export

**Goal**: Completed matches produce correct, idempotent rating updates via a windowed Glicko-2 default that a developer can swap out, seasonal boundaries archive ratings without data loss, and operators can satisfy GDPR export requests over the full PII surface.

**Depends on**: Phase 1 (for sessions schema + GDPR delete service); Phase 2 (for identities in export); Phase 3 (so rank-adjust audit writes into the already-wired admin panel)

**Requirements**: RANK-01, RANK-02, RANK-03, RANK-04, RANK-05, RANK-06, RANK-07, RANK-08, RANK-09, RANK-10, RANK-11, RANK-12, RANK-13, RANK-14

**Success Criteria** (what must be TRUE):
  1. A 1000-match convergence integration test simulates two populations with known true skill, runs matches through the default `Glicko2Algorithm.Apply(state, batch)` in the configured rating period, and asserts converged ratings land within Glickman's documented tolerance of true skill; the test fails if anyone swaps in a per-match update path.
  2. `POST /api/sessions/{id}/complete` is state-conditional (`WHERE state = 'active'`), caches rating deltas on the session row, accepts an `Idempotency-Key` header, and a retry integration test calls it 5 times with the same payload and observes exactly one rating delta applied per participant.
  3. Rating, rating deviation, and volatility columns are `double precision` (asserted via a schema introspection test); `rating_before`/`rating_after`/`delta` are snapshotted on `session_participants` at completion.
  4. A seasonal-reset integration test advances a ladder's season, archives the previous season's ratings into history, resets current ranks per season config, and confirms leaderboard queries against the archived season still return correct top-N and around-me results.
  5. `GET /api/players/{id}/export` returns a JSON bundle containing the player row, identities, credentials metadata (no password hash), sessions participated in, and rating history across all ladders; a contract test asserts the schema.
  6. A manual rank adjustment through the Phase 3 admin panel writes a before/after row to `admin_audit_log` and updates the player's rating in a single transaction.

**Plans**: 8 plans
- [ ] 02-01-PLAN.md — Wave 0 test projects, WireMock Steam/Discord mocks, AuthIntegrationFixture, Directory.Packages.props pins for Auth stack
- [ ] 02-02-PLAN.md — PlayerIdentity/PlayerCredential/RefreshToken entities + EF configurations + AuthInitial migration under __ef_migrations_auth; UNIQUE(provider, external_id) is the D-14 race anchor
- [ ] 02-03-PLAN.md — GameKitAuthOptions/JwtOptions/SteamOptions/DiscordOptions; AddAuth fluent extension; EgressAllowListHandler + named HttpClients (D-07/D-08/D-10); UseAuthentication ordering fix
- [ ] 02-04-PLAN.md — IPasswordHasher + BCryptPasswordHasher; JwtIssuer with D-03 claims; IsGuestResolver (D-13); RefreshTokenService with Pattern 3 rotation + 45s grace + fingerprint gate + family revocation + audit log
- [ ] 02-05-PLAN.md — IOAuthProvider contract + SteamOAuthProvider + DiscordOAuthProvider (identify scope only); Scrutor discovery; auth scheme wiring for JwtBearer + Steam + Discord; mock-level forgery test
- [ ] 02-06-PLAN.md — GuestOAuthProvider + PasswordOAuthProvider + GuestUpgradeService + IdentityLinker; concurrent-upgrade race test (success #4); cross-player collision test with ExternalIdHasher (success #5)
- [ ] 02-07-PLAN.md — /auth/* minimal API endpoints + FluentValidation endpoint filter + rate-limit policies (login 10/min, refresh 60/min, register 5/min); WebApplicationFactory end-to-end tests for success #1, #3, #6 + end-to-end Steam forgery #2
- [ ] 02-08-PLAN.md — TicTacToeDuel Program.cs AddAuth + startup hardening; HTML client X-GameKit-Device + JWT localStorage + 401-refresh-retry; README auth section with localStorage/XSS/signing-key disclaimers; human-verify checkpoint
**UI hint**: no

### Phase 5: Matchmaking + Parties

**Goal**: Players (solo or in parties of 1-N from v1) can queue into a Redis-backed live matchmaker that never double-matches, survives app-server crashes without ghost tickets, flexes rating brackets over time, and holds up under a 1k-concurrent-ticket load test.

**Depends on**: Phase 4 (default strategy needs ratings); Phase 3 (queue-depth + health panels wire through)

**Requirements**: MATCH-01, MATCH-02, MATCH-03, MATCH-04, MATCH-05, MATCH-06, MATCH-07, MATCH-08, MATCH-09, MATCH-10, MATCH-11, MATCH-12, MATCH-13, MATCH-14, MATCH-15

**Success Criteria** (what must be TRUE):
  1. A party of 1-N players can enqueue a single ticket, the default `EloRangeMatchmakingStrategy.Match(Party, candidates)` produces a match whose bracket widened from ±100 to ±500 over ~40s of queue time, and `matchmaking_tickets` rows are written asynchronously to Postgres for analytics while Redis remains the live source of truth.
  2. A chaos integration test enqueues 100 parties, runs the matcher, kills the app process mid-match, restarts, runs reconciliation, and asserts: no duplicate `game_sessions` rows, no ghost `mm:ticket:{id}` keys in Redis, expired leases returned to queue, and no player appearing in two active sessions.
  3. A load test sustains 1,000 concurrent queued tickets for 10 minutes against a single Redis + Postgres pair with no matchmaker iteration exceeding its configured budget and no Npgsql connection-pool exhaustion; the load test is a phase gate.
  4. A leader-election integration test spins up two matcher replicas sharing one Redis; exactly one holds the distributed lock at any time, and a forced failover transfers leadership within the configured lease TTL with no double-matching.
  5. Per-player enqueue rate limiting returns 429 on a client spamming `/mm/queue` and does not poison the queue with duplicate tickets.
  6. The Phase 3 Admin UI queue-depth + health panels display live Redis state (queue counts per pool, lease count, leader identity) sourced from Redis, not from Postgres reconciliation mirrors.

**Plans**: 8 plans
- [ ] 02-01-PLAN.md — Wave 0 test projects, WireMock Steam/Discord mocks, AuthIntegrationFixture, Directory.Packages.props pins for Auth stack
- [ ] 02-02-PLAN.md — PlayerIdentity/PlayerCredential/RefreshToken entities + EF configurations + AuthInitial migration under __ef_migrations_auth; UNIQUE(provider, external_id) is the D-14 race anchor
- [ ] 02-03-PLAN.md — GameKitAuthOptions/JwtOptions/SteamOptions/DiscordOptions; AddAuth fluent extension; EgressAllowListHandler + named HttpClients (D-07/D-08/D-10); UseAuthentication ordering fix
- [ ] 02-04-PLAN.md — IPasswordHasher + BCryptPasswordHasher; JwtIssuer with D-03 claims; IsGuestResolver (D-13); RefreshTokenService with Pattern 3 rotation + 45s grace + fingerprint gate + family revocation + audit log
- [ ] 02-05-PLAN.md — IOAuthProvider contract + SteamOAuthProvider + DiscordOAuthProvider (identify scope only); Scrutor discovery; auth scheme wiring for JwtBearer + Steam + Discord; mock-level forgery test
- [ ] 02-06-PLAN.md — GuestOAuthProvider + PasswordOAuthProvider + GuestUpgradeService + IdentityLinker; concurrent-upgrade race test (success #4); cross-player collision test with ExternalIdHasher (success #5)
- [ ] 02-07-PLAN.md — /auth/* minimal API endpoints + FluentValidation endpoint filter + rate-limit policies (login 10/min, refresh 60/min, register 5/min); WebApplicationFactory end-to-end tests for success #1, #3, #6 + end-to-end Steam forgery #2
- [ ] 02-08-PLAN.md — TicTacToeDuel Program.cs AddAuth + startup hardening; HTML client X-GameKit-Device + JWT localStorage + 401-refresh-retry; README auth section with localStorage/XSS/signing-key disclaimers; human-verify checkpoint
**UI hint**: no

### Phase 6: Presence + OpenAPI + Distribution

**Goal**: The presence package lights up the Admin UI and gates abandonment flows, every HTTP endpoint in the family is described by an OpenAPI document, and a newcomer can go from `dotnet new gamekit` to a running self-hosted backend against the coordinated release train.

**Depends on**: Phases 1-5 (all packages must exist for SampleGame, OpenAPI coverage, and the release-train version stamp)

**Requirements**: PRES-01, PRES-02, PRES-03, PRES-04, PRES-05, PRES-06, OPEN-01, DIST-02, DIST-03, DIST-04, DIST-05, DIST-06, OPS-04, OPS-05

**Success Criteria** (what must be TRUE):
  1. A player posts to `/presence/heartbeat`, their status appears as `online` in Redis with the configured TTL; TTL expiry transitions them to `offline`; the game server calling `POST /api/sessions/{id}/abandon` (game-server-authoritative) is what moves them to `in-match` or triggers abandonment, never presence inference alone.
  2. The Phase 3 Admin UI presence panel displays top-N online players and per-player status sourced from `GameKit.Presence` via Core's `IPresenceProvider`; the panel gracefully degrades when `GameKit.Presence` is not installed.
  3. The OpenAPI document generated by `Microsoft.AspNetCore.OpenApi` covers every GameKit HTTP endpoint (auth, session-complete, GDPR export, matchmaking, presence, admin-exposed) and a contract test asserts no endpoint is missing from the spec.
  4. `dotnet new install GameKit.Templates` + `dotnet new gamekit -n DemoGame` produces a runnable SampleGame that boots against the shipped `docker-compose.yml`, authenticates a guest, completes a session, queries a leaderboard, and demonstrates the game-server SampleGame component connecting via `gamekit_reader`; an integration test asserts `gamekit_reader` cannot INSERT into `gamekit.game_sessions`.
  5. A CI release-train job stamps all 6 packages (`Core`, `Auth`, `Rankings`, `Matchmaking`, `Presence`, `Admin.UI`) with the same MinVer-derived version, exact-pins sibling references `[X.Y.Z]`, and a runtime startup assertion fails fast on any `GameKitVersion` constant mismatch across loaded assemblies.
  6. The production-readiness ops guide documents bare-metal, container, and air-gapped deployment recipes including three-role Postgres provisioning, Redis AOF configuration, JWT key management, and disaster-recovery procedures; CS1591-as-error passes across all 6 shipped packages.

**Plans**: 8 plans
- [ ] 02-01-PLAN.md — Wave 0 test projects, WireMock Steam/Discord mocks, AuthIntegrationFixture, Directory.Packages.props pins for Auth stack
- [ ] 02-02-PLAN.md — PlayerIdentity/PlayerCredential/RefreshToken entities + EF configurations + AuthInitial migration under __ef_migrations_auth; UNIQUE(provider, external_id) is the D-14 race anchor
- [ ] 02-03-PLAN.md — GameKitAuthOptions/JwtOptions/SteamOptions/DiscordOptions; AddAuth fluent extension; EgressAllowListHandler + named HttpClients (D-07/D-08/D-10); UseAuthentication ordering fix
- [ ] 02-04-PLAN.md — IPasswordHasher + BCryptPasswordHasher; JwtIssuer with D-03 claims; IsGuestResolver (D-13); RefreshTokenService with Pattern 3 rotation + 45s grace + fingerprint gate + family revocation + audit log
- [ ] 02-05-PLAN.md — IOAuthProvider contract + SteamOAuthProvider + DiscordOAuthProvider (identify scope only); Scrutor discovery; auth scheme wiring for JwtBearer + Steam + Discord; mock-level forgery test
- [ ] 02-06-PLAN.md — GuestOAuthProvider + PasswordOAuthProvider + GuestUpgradeService + IdentityLinker; concurrent-upgrade race test (success #4); cross-player collision test with ExternalIdHasher (success #5)
- [ ] 02-07-PLAN.md — /auth/* minimal API endpoints + FluentValidation endpoint filter + rate-limit policies (login 10/min, refresh 60/min, register 5/min); WebApplicationFactory end-to-end tests for success #1, #3, #6 + end-to-end Steam forgery #2
- [ ] 02-08-PLAN.md — TicTacToeDuel Program.cs AddAuth + startup hardening; HTML client X-GameKit-Device + JWT localStorage + 401-refresh-retry; README auth section with localStorage/XSS/signing-key disclaimers; human-verify checkpoint
**UI hint**: no

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation (Core + Migrations + Ops Defaults + GPL) | 7/7 | Complete | 2026-04-18 |
| 2. Authentication | 8/8 | Complete | 2026-04-18 |
| 3. Admin UI | 6/13 | In progress | - |
| 4. Rankings + Sessions Wiring + GDPR Export | 0/? | Not started | - |
| 5. Matchmaking + Parties | 0/? | Not started | - |
| 6. Presence + OpenAPI + Distribution | 0/? | Not started | - |

## Coverage Validation

| Source | Count |
|--------|-------|
| v1 requirements in REQUIREMENTS.md | 92 |
| Mapped to phases | 92 |
| Orphaned | 0 |

Mapping by phase:
- Phase 1: CORE-01..17 (17) + OPS-01, OPS-02, OPS-03, OPS-06, OPS-07, OPS-08, OPS-09, OPS-10 (8) + DIST-01 (1) = **26**
- Phase 2: AUTH-01..16 = **16**
- Phase 3: ADMIN-01..12 = **12**
- Phase 4: RANK-01..14 = **14**
- Phase 5: MATCH-01..15 = **15**
- Phase 6: PRES-01..06 (6) + OPEN-01 (1) + DIST-02, DIST-03, DIST-04, DIST-05, DIST-06 (5) + OPS-04, OPS-05 (2) = **14**

Total: 26 + 16 + 12 + 14 + 15 + 14 = **92** ✓

---
*Created: 2026-04-15 during initial project setup.*
