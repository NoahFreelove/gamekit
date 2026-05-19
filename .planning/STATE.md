---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: ready_to_plan
stopped_at: Phase 04 complete (8/8) — ready to discuss Phase 5
last_updated: 2026-05-16T17:03:02.183Z
progress:
  total_phases: 7
  completed_phases: 4
  total_plans: 40
  completed_plans: 42
  percent: 57
---

# STATE: GameKit

## Project Reference

**Core Value:** A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.

**License:** GPL
**Runtime:** .NET 10 LTS (released 2026-04-14)
**Mode:** YOLO / Quality model profile / parallel execution enabled
**Current Focus:** Phase 5 — matchmaking + parties

## Current Position

Phase: 04 (rankings-sessions-gdpr) — EXECUTING
Plan: 8 of 8
Next: `/gsd-plan-phase 04` to produce the plan set
**Milestone:** v1 (initial 6-phase build-out)
**Phase:** 5
**Plan:** Not started
**Status:** Ready to plan

**Progress:** [███████████████] 100% (32 / 32 plans; Phase 03.1 verified after `quick/20260515-phase-031-verification-gaps` closed BLOCKER-GAP-01 + INFO-GAP-03)

**Pre-Flight Gate (Phase 1):**

- [x] Verify `Npgsql.EntityFrameworkCore.PostgreSQL` `net10.0` TFM GA on NuGet — 10.0.1 verified GA
- [x] Verify `AspNet.Security.OAuth.Discord` 10.0.x `net10.0` TFM — 10.0.0 verified GA 2026-04-18 (nuspec explicit `net10.0`)
- [N/A] `AspNet.Security.OpenId.Steam` — intentionally NOT pinned per D-09 (in-house SteamOpenIdVerifier replaces contrib package)
- [x] Verify `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Polly`, `FluentValidation` 12, `Scrutor`, `MinVer` 7, `Microsoft.SourceLink.GitHub` all resolve on `net10.0` — all GA, pinned in Directory.Packages.props
- [x] Record workarounds (preview pins, compatibility shims) in STATE before first migration is authored — NO workarounds needed, all packages GA

## Performance Metrics

| Metric | Value |
|--------|-------|
| Phases complete | 0 / 6 |
| v1 requirements mapped | 92 / 92 |
| v1 requirements validated | 0 / 92 |
| Packages released | 0 / 6 |
| Phase 01 P01 | 4min | 3 tasks | 9 files |
| Phase 01 P02 | 2min | 3 tasks | 4 files |
| Phase 01 P03 | 9min | 3 tasks | 22 files |
| Phase 01 P04 | 11min | 3 tasks | 16 files |
| Phase 01 P05 | 14min | 2 tasks | 21 files |
| Phase 01 P06 | 5min | 3 tasks | 20 files |
| Phase 01 P07 | 23min | 5 tasks | 37 files |
| Phase 02 P01 | 6min | 3 tasks | 15 files |
| Phase 02 P02 | 14min | 3 tasks | 17 files |
| Phase 02 P03 | 32min | 3 tasks | 13 files |
| Phase 02 P04 | 12min | 3 tasks | 23 files |
| Phase 02 P05 | 12min | 3 tasks | 14 files |
| Phase 02 P06 | 10min | 3 tasks | 14 files |
| Phase 02 P07 | 35min | 3 tasks | 21 files |
| Phase 02 P08 | 210min | 3 tasks | 20 files |
| Phase 03 P01 | 8min | 3 tasks | 11 files |
| Phase 03 P02 | 22min | 3 tasks | 15 files |
| Phase 03 P03 | 14min | 2 tasks | 8 files |
| Phase 03 P04 | 6min | 2 tasks | 5 files |
| Phase 03 P05 | 20min | 2 tasks | 6 files |
| Phase 03 P06 | 27min | 3 tasks | 33 files |
| Phase 03 P07 | 10min | 3 tasks | 14 files |
| Phase 03 P08 | 32min | 2 tasks | 18 files |
| Phase 03 P13 | 18min | 2 tasks | 8 files |
| Phase 04 P01 | 15min | 3 tasks | 8 files |
| Phase 04 P02 | — | — | — |
| Phase 04 P03 | — | — | — |
| Phase 04 P04 | — | — | — |
| Phase 04 P05 | 32min | 3 tasks | 16 files |
| Phase 04 P07 | 45min | 3 tasks | 18 files |

## Accumulated Context

### Roadmap Evolution

- 2026-04-26 — Phase 03.1 inserted after Phase 03: Admin UI redesign v2 — re-skin per Claude Design hi-fi prototype (violet-600 accent, density tokens, master-detail Players, two-column Audit, ⌘K palette, Tweaks panel, medium ban banner). Source prototype preserved at `.planning/sketches/admin-ui-redesign-v2/`. Re-skins existing UI without changing functional contract. (URGENT-INSERT)

### Decisions Locked (from research)

| Decision | Source |
|----------|--------|
| Single fully-owned `GameKitDbContext` in DI (not a base class) | PROJECT.md Key Decisions |
| Per-package migrations assembly + per-package `__ef_migrations_<pkg>` history table + per-package `IDesignTimeDbContextFactory` | ARCHITECTURE.md + PITFALLS.md #3 |
| `BackgroundService` + `PeriodicTimer` + Polly (NOT Hangfire/Quartz) | STACK.md + PROJECT.md |
| MinVer coordinated release train, all 6 packages stamped to same version, sibling refs exact-pinned `[X.Y.Z]` | STACK.md + PITFALLS.md #11 |
| Reject MediatR / AutoMapper (RPL v13+ commercial license) | STACK.md |
| Presence in its own package (`GameKit.Presence`), Core defines `IPresenceProvider` | PROJECT.md Key Decisions |
| Parties live in `GameKit.Matchmaking` for v1 (ticket model 1-N from day one) | PROJECT.md Key Decisions |
| Blazor Server in RCL for Admin UI | PROJECT.md Key Decisions |
| Rating columns stored as `double precision`, not `NUMERIC(8,2)` | PITFALLS.md #13 |
| `IRankingAlgorithm.Apply(state, batch)` — batched, not per-match | PITFALLS.md #1 |
| Glicko-2 vendored from MaartenStaa/glicko2-csharp (**BSD-3-Clause** — NOT MIT; CLAUDE.md/04-CONTEXT.md incorrect; verified by git clone commit 59033eec) | STACK.md + 04-01 execution |
| Steam provider implemented in-house against xPaw reference with server-side `check_authentication` roundtrip | PITFALLS.md #12 |
| Redis with `--appendonly yes --appendfsync everysec` in shipped `docker-compose.yml` | PITFALLS.md #17 |
| Three Postgres roles: `gamekit_owner`, `gamekit_app`, `gamekit_reader`; SampleGame game-server uses reader | PITFALLS.md #7 |
| GPL LICENSE + per-file headers + CI check from Phase 1 | Task prompt |
| Runtime guard asserts zero outbound HTTP from Core (except configured providers) | PROJECT.md + task prompt |
| Used legacy .sln format (not .slnx) for broad IDE compatibility | 01-01 execution — .NET 10 defaults to .slnx |
| MinVer 7.0.0 and SourceLink 10.0.202 (updated from CLAUDE.md stale 6.0.0/8.0.0) | 01-01 execution — verified GA on nuget.org |
| POSTGRES_USER=postgres (not gamekit_owner) as bootstrap superuser for init scripts | 01-02 execution — superuser needed for CREATE EXTENSION + REVOKE |
| Redis --maxmemory-policy noeviction for loud failures over silent key eviction | 01-02 execution — matchmaking/presence prefer errors over data loss |
| Npgsql transitive pin bumped 10.0.1 -> 10.0.2 (required by Npgsql.EFCore.PG 10.0.1) | 01-03 execution — NuGet restore error |
| Microsoft.Extensions.Caching.Memory bumped 10.0.0 -> 10.0.6 (required by EF Core 10.0.6) | 01-03 execution — transitive downgrade error |
| GameSessionState stored as string (HasConversion<string>) not integer | 01-03 execution — stable across enum reorderings |
| All entity Ids use ValueGeneratedNever (UUIDv7 from IIdGenerator, not DB) | 01-03 execution — per threat T-03-05 |
| Explicit snake_case table names in EF configs (defensive, not relying on naming convention) | 01-03 execution — Plan 04 may add UseSnakeCaseNamingConvention |
| Advisory lock key corrected to 1800940027 (live Postgres 17.9 verified via Testcontainers) | 01-07 execution — RESEARCH.md value was wrong |
| Migration timestamp renamed to 20260415000000 for deterministic cross-package ordering | 01-04 execution — EF CLI generated current timestamp |
| EF Core InMemory provider added to test project (Npgsql with fake conn string used for model tests) | 01-04 execution — InMemory can't handle jsonb column types |
| FrameworkReference Microsoft.AspNetCore.App replaces explicit Caching.Memory PackageReference | 01-05 execution — NU1510 warning: transitive dep redundant |
| PlayerDisplayNameResolver registered as Scoped (not Singleton per plan) | 01-05 execution — depends on scoped GameKitDbContext |
| GDPR ExecuteDeleteAsync round-trip test deferred to Plan 07 Testcontainers integration tests | 01-05 execution — InMemory provider does not support bulk operations |
| InMemory test factory with custom ModelCustomizer for JsonDocument value converters | 01-05 execution — InMemory can't handle jsonb/JsonDocument natively |
| BCrypt.Net-Next pin bumped 4.0.3 -> 4.1.0 (RESEARCH §4 verified net10.0 TFM) | 02-01 execution |
| Microsoft.Extensions.Http.Resilience 10.5.0 pinned for named-HttpClient resilience pipelines | 02-01 execution |
| AspNet.Security.OpenId.Steam intentionally NOT pinned — in-house SteamOpenIdVerifier replaces contrib package (D-09) | 02-01 execution |
| AuthMigrationConstants.AdvisoryLockKey = -298890956L (live Postgres 17.9 hashtext('gamekit.auth.migrations')::bigint) — distinct from Core's 1800940027L | 02-02 execution |
| Negative advisory-lock keys acceptable — hashtext returns int4; ::bigint preserves sign; Postgres advisory locks accept any bigint | 02-02 execution |
| AuthMigrationModelCustomizer (top-level public) — reused by design-time EF CLI + runtime test Auth migration contexts; applies Auth configs directly + ExcludeFromMigrations on Core entities | 02-02 execution |
| EF internal service provider does NOT forward IEnumerable<IModelBuilderExtension> to ReplaceService customizer constructor injection — use AuthMigrationModelCustomizer for migration-time, test-local AuthRuntimeQueryCustomizer for query-time (flagged for 02-03 audit) | 02-02 execution |
| Auth migration timestamp 20260418000000 (Phase 1 deterministic-timestamp convention) | 02-02 execution |
| Explicit migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext") duplicates Npgsql:PostgresExtension annotation for defensive auditability in Auth migration | 02-02 execution |
| GameKitAuthOptions.AllowedProviderHosts is a mutable `List<string>` initialized from the public literal constant `DefaultAllowedHosts.All` (4 hosts) — a misconfigured appsettings.json cannot silently clear the list (CONTEXT `<specifics>` hard requirement) | 02-03 execution |
| Two named HttpClients `gamekit.auth.provider.steam` + `gamekit.auth.provider.discord` — both pipe through `EgressAllowListHandler` (transient DelegatingHandler) and `AddStandardResilienceHandler()` (Polly v8 via Microsoft.Extensions.Http.Resilience) | 02-03 execution |
| `SkipAuthenticationSchemeRegistration` feature-flag on `GameKitAuthOptions` — gates `services.AddAuthentication("Bearer")` inside AddAuth so unit tests can build the DI container without real RSA PEM files. Plans 02-04/02-05 flip it off | 02-03 execution |
| `UseGameKitAuth()` lives in GameKit.Auth (not Core) — Option B from RESEARCH Open Q #1; keeps Core free of authentication-scheme awareness. Strict consumer ordering: UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → MapGameKit → MapAuth | 02-03 execution |
| `ValidateAuthOptions` fail-fast validator runs inside AddAuth (not IValidateOptions) — surfaces missing Issuer/Audience/PEM files at registration time, not first request | 02-03 execution |
| DI audit outcome (02-02 deviation #4 follow-up): workaround stays. Three fix attempts reached fix-limit; root cause is that Core's migration path and runtime query path share one model view. Full fix requires a CoreMigrationModelCustomizer + split query/migration model views across MigrationRunner/UseGameKit — dedicated Phase 2 gap plan needed (see 02-03-SUMMARY FOLLOW-UP-02-03-01) | 02-03 execution |
| Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6 pinned as a standalone NuGet package (NOT shared framework — handler was split out of Microsoft.AspNetCore.App in .NET 8). CLAUDE.md stack-table stale on this row; Directory.Packages.props corrected | 02-04 execution |
| JwtIssuer lifetime = Scoped (not Singleton) because it depends on the scoped IIsGuestResolver; RsaSecurityKey + SigningCredentials ctor-captured so per-call JWT-sign cost is constant within a scope | 02-04 execution |
| AuthAuditWriter lifetime = Scoped (same DbContext lifetime as caller) — audit writes ride the caller's transaction, so a rollback also rolls back the audit row (matches GdprDeleteService precedent) | 02-04 execution |
| Refresh-token rotation uses IsolationLevel.ReadCommitted (not Serializable) — lookup is on UNIQUE TokenHash + no phantom-read semantics needed; Serializable is reserved for Phase 2-06 GuestUpgrade + IdentityLink where D-14 race depends on phantom-read protection | 02-04 execution |
| Fingerprint stored as-is in refresh_tokens.device_fingerprint (not hashed); comparisons use string.Equals(StringComparison.Ordinal). Mismatch inside grace window STILL fires family revoke (D-05/D-06 invariant) | 02-04 execution |
| D-03 claim names preserved literally on ingress via MapInboundClaims=false on AddJwtBearer — 'sub'/'provider'/'sid' reach ICurrentPlayer without Microsoft's default claim-type remapping | 02-04 execution |
| IOAuthProvider registered via Scrutor with `publicOnly: false` — built-in providers are internal sealed; default publicOnly:true would silently skip them. Customer-authored providers work regardless of access modifier | 02-05 execution |
| Discord authentication scheme registered conditionally (only when ClientId+ClientSecret both supplied) — prevents aspnet-contrib handler from throwing in test harnesses that don't exercise Discord | 02-05 execution |
| Steam scheme deliberately NOT registered — SteamOpenIdVerifier invoked directly by /auth/callback/steam endpoint (D-09 in-house verifier; no AspNet.Security.OpenId.Steam contrib dep) | 02-05 execution |
| DiscordBackchannelPostConfigure is Singleton IPostConfigureOptions<DiscordAuthenticationOptions> scoped narrowly to the Discord options type — NOT a global handler. Defends against T-02-19 (cross-scheme backchannel override) | 02-05 execution |
| T-02-20 (OpenID assertion replay) accepted as residual risk for v1 — Steam's OP tracks nonce reuse on its side; OpenID 2.0 §11.4.2 says check_authentication is single-response per assertion | 02-05 execution |
| OnCreatingTicket resolves Discord IOAuthProvider via IEnumerable<IOAuthProvider> filter by Provider=="discord" (not GetRequiredService<DiscordOAuthProvider>) — Scrutor registers only under interface; avoids duplicate concrete-type registration | 02-05 execution |
| Npgsql default execution strategy wraps transient failures (incl. 40001) in InvalidOperationException; EF further wraps in DbUpdateException. TryFindPostgresException(Exception) walks InnerException chain (bounded depth 8) rather than disabling retry strategy — keeps consumer-facing EF behavior intact. Used in IdentityLinker, GuestUpgradeService, PasswordOAuthProvider | 02-06 execution |
| PasswordOAuthProvider.DummyHash — canned BCrypt-format literal used on user-not-found so BCrypt.Verify runs the full work-factor-12 comparison and wall-clock time parity-matches the hit path (T-02-16, closes plan 02-04 follow-up) | 02-06 execution |
| IdentityLinker uses SERIALIZABLE transaction with 3-attempt retry on 40001; 23505 surfaces as LinkResult.AlreadyLinkedToOtherPlayer with SHA-256 hash (never raw external_id — T-02-10) | 02-06 execution |
| GuestUpgradeService.UpgradeToPasswordAsync opens SERIALIZABLE tx; 23505 on UNIQUE(Username) throws UsernameAlreadyTakenException (resolves RESEARCH §15 open q #3) | 02-06 execution |
| Guest + Password providers are internal sealed, auto-discovered by plan 02-05 Scrutor scan with publicOnly:false — no additional DI registration needed beyond IIdentityLinker + IGuestUpgradeService | 02-06 execution |
| TestHelpers.cs extracts ApplyMigrations + BuildProvider (returns TestContext with IAsyncDisposable-managed RSA PEM directory) for plan 02-06's 4 integration test classes — reused FOLLOW-UP-02-03-01 AuthRuntimeQueryCustomizer pattern | 02-06 execution |
| AddRateLimiter extension method lives in namespace Microsoft.AspNetCore.Builder (not Microsoft.AspNetCore.RateLimiting as the enclosing types would suggest); library projects must `using Microsoft.AspNetCore.Builder;` to bring it into scope | 02-07 execution |
| Auth rate-limit partition key = $"{RemoteIp}:{X-GameKit-Device}" composite; missing fingerprint falls back to IP-only (RESEARCH §8.7; defends against fingerprint-spray DoS while allowing single-NAT bursts) | 02-07 execution |
| PasswordOAuthProvider has a concrete-type DI factory forwarder that resolves the existing IOAuthProvider Scrutor-scoped instance — endpoint layer uses RegisterAsync (not on the IOAuthProvider interface) without a duplicate scoped registration per request | 02-07 execution |
| AuthTestHost.Now initialized to real DateTimeOffset.UtcNow (NOT UnixEpoch+56y) so JwtIssuer-signed tokens pass JwtBearer handler's real-clock lifetime validation; mock clock still mutable for refresh-grace advancement | 02-07 execution |
| /auth/logout returns 204 No Content (Bearer-protected endpoint; RevokeFamilyAsync silently no-ops for unknown tokens — no enum-oracle concern because unauthenticated callers can't reach the endpoint) | 02-07 execution |
| FOLLOW-UP-02-03-01 CLOSED in 02-08: GameKitDbContext.OnModelCreating resolves IEnumerable<IModelBuilderExtension> lazily via CoreOptionsExtension.ApplicationServiceProvider; AddGameKit uses AddDbContext((sp, opts) => ...) overload + UseApplicationServiceProvider(sp) so runtime context carries the app provider. Direct-construction migration contexts (design-time factories + BuildMigrationContext) intentionally do NOT attach a provider, preserving per-package migration boundary. Fix uncovered when sample's first /auth/login/guest failed because test-local AuthRuntimeQueryCustomizer shim was unavailable at runtime | 02-08 execution |
| AuthMigrationHostedService (new, 85 LOC) applies __ef_migrations_auth under Auth-specific advisory lock (-298890956) in IHost.StartAsync — runs after Core migrations via UseGameKit but BEFORE Kestrel accepts traffic. UseGameKitAuth reduced to pure app.UseAuthentication(); migration concern migrated to the hosted service. Sibling packages (Rankings, Matchmaking, Presence) mirror this pattern | 02-08 execution |
| /auth/logout no longer requires Bearer JWT (reverses 02-07 decision after human-verify surfaced security hole): refresh token IS the revocation capability (RFC 7009 semantics). Expired access token previously 401'd logout and left refresh family live after "logout". RevokeFamilyAsync is silent no-op for unknown tokens, so endpoint cannot enum-oracle | 02-08 execution |
| OAuth callbacks (/auth/callback/steam + /auth/callback/discord) return HTML bridge via AuthEndpoints.BrowserTokenBridge (not JSON) — Steam/Discord redirect the browser, which rendered JSON as text. Bridge writes tokens to localStorage via JsonEncodedText-escaped literals (defense-in-depth) + location.replace("/"). Pattern reusable for any future provider | 02-08 execution |
| Sample appsettings.Development.json PEM paths are project-relative (keys/dev-priv.pem), NOT repo-root-relative. `dotnet run --project` sets CWD to project dir; repo-root paths resolved to nonexistent locations and failed ValidateAuthOptions at startup | 02-08 execution |
| MudBlazor 9.3.0 pinned in Directory.Packages.props as a CPM-only entry (no csproj PackageReferences it yet; plan 03-03 wires it into src/GameKit.Admin.UI) — MIT, net10.0 GA 2026-04-18 | 03-01 execution |
| tests/GameKit.Admin.Integration.Tests deliberately does NOT depend on WireMock.Net — admin surface makes no outbound HTTP (health probe uses in-process Npgsql + StackExchange.Redis clients) | 03-01 execution |
| AdminCollection xUnit collection bundles PostgresFixture + RedisFixture + AdminIntegrationFixture; re-declared per assembly (xUnit1041) mirroring the Phase 2 AuthCollection pattern | 03-01 execution |
| FakePlayerJwtIssuer emits D-03 shaped JWTs (sub/provider="guest"/sid) under throwaway RSA 2048 keypair; IDisposable scrubs the keypair — used ONLY for SC#6 cross-scheme isolation in plan 03-13 | 03-01 execution |
| WebApplicationFactoryExtensions.LoginAsAdminAsync + HarvestAntiforgeryTokenAsync signatures locked — plans 03-04/03-07/03-13 call them verbatim | 03-01 execution |
| AdminMigrationConstants.AdvisoryLockKey = -2101739634L (live Postgres 17.9 hashtext('gamekit.admin.migrations')::bigint via Testcontainers) — distinct from Core (1800940027L) and Auth (-298890956L); placeholder-then-live-verify pattern follows 02-02 precedent | 03-02 execution |
| GameKit.Admin.UI csproj gains GameKit.Auth ProjectReference per W5 (CLAUDE.md plan-01 entry) — required because AdminMigrationConstants XML doc cref AuthMigrationConstants and AdminMigrationModelCustomizer ExcludeFromMigrations Auth's three entity types | 03-02 execution |
| AdminMigrationModelCustomizer is a separate file (not collocated with AdminDesignTimeDbContextFactory like Auth) — readability win for the 7-entity exclusion list (4 Core + 3 Auth, vs Auth's 4 Core only); ExcludeEntity helper avoids duplication | 03-02 execution |
| Admin migration timestamp 20260419000000 (Phase-1/Phase-2 deterministic-timestamp convention; Auth = 20260418000000, Admin one day later for cross-package ordering) | 03-02 execution |
| Postgres CHECK-constraint expressions referencing PascalCase columns MUST quote the identifier ("Role" IN (...)) — Postgres folds unquoted identifiers to lowercase; AuthInitial had no CHECK constraints so this gotcha didn't surface in Phase 2. Plan literal "role IN (...)" was wrong; corrected as Rule-1 deviation | 03-02 execution |
| ICollectionFixture<AdminIntegrationFixture> dropped from BOTH AdminCollection re-declarations — xUnit 2.9 ICollectionFixture<T> requires T to have a parameterless constructor; AdminIntegrationFixture's PostgresFixture+RedisFixture ctor cannot be satisfied at collection scope. Composite preserved for plans 03-04+/03-07/03-13 to construct manually (matches AuthIntegrationFixture usage today) | 03-02 execution |
| dotnet-ef CLI tool upgraded 10.0.5 -> 10.0.6 to match runtime EF Core (Directory.Packages.props) — design assemblies must align with runtime to avoid silent codegen drift | 03-02 execution |
| AdminSchemaTests does NOT register AuthModelBuilderExtension via DI (Auth's marker is internal sealed and not friend to GameKit.Admin.Integration.Tests) — equivalent because AuthMigrationModelCustomizer applies Auth configs directly during the migration pass | 03-02 execution |
| GameKit.Admin.UI promoted from Microsoft.NET.Sdk to Microsoft.NET.Sdk.Razor SDK + AddRazorSupportForMvc=true property — required so plan 03-08 can compile .razor pages; FrameworkReference Microsoft.AspNetCore.App added to bring Cookies/Antiforgery shared-framework types into scope | 03-03 execution |
| AdminUiMarker is internal static (mirrors GameKit.Auth.AuthMarker) — paired with [assembly: InternalsVisibleTo("GameKit.Admin.Tests")] + InternalsVisibleTo("GameKit.Admin.Integration.Tests") so SmokeTests.TestProject_Loads asserts typeof(AdminUiMarker) is non-null (proves InternalsVisibleTo grant resolves at compile-time + assembly loads at test-time) | 03-03 execution |
| GameKitAdminOptions defaults pinned: MountPath="/admin", Cookie.{Name="gk_admin_session", ExpireTimeSpan=8h, SlidingExpiration=true, RememberMeDuration=30d}, Panel.{RefreshInterval=10s, HealthErrorRateWindow=5m, HealthErrorRateBucketSize=1s}, Csp.ReportOnly=false (no phone-home — matches "install only what you need") | 03-03 execution |
| AdminRoles values lowercase ("admin", "superadmin") — must match plan 03-02 ck_admin_users_role CHECK constraint values; AdminPolicies dotted lower-case ("gamekit.admin.admin"/"gamekit.admin.superadmin") namespaced under gamekit.admin.* to avoid collision with consumer-defined ASP.NET authorization policies | 03-03 execution |
| AdminAuthenticationSchemeConstants.Scheme = "GameKitAdmin" deliberately distinct from JwtBearerDefaults.AuthenticationScheme ("Bearer") — satisfies ROADMAP SC#6 (player JWT cannot authenticate into admin endpoint); plan 03-13 CrossSchemeIsolationTests will assert empirically. CookieName ("gk_admin_session") + CsrfHeaderName ("X-GameKit-Admin-CSRF") + CsrfCookieName ("gk_admin_csrf") pinned at the constant layer; AdminCookieOptions.Name default tracks CookieName but consumer can override on host cookie collision (T-03-03-02 mitigation) | 03-03 execution |
| MountPath option scope clarified in XML doc: prefixes ONLY admin HTTP API endpoints (/admin/api/*); Blazor @page routes + MudBlazor _content/* static assets remain root-relative for v1 (B1 step 4 from CLAUDE.md GameKit.Admin.UI block) | 03-03 execution |
| `AdminCookieEvents` status-code matrix: Production/Staging unauthenticated → 404 (SC#2); Development unauthenticated → 302 to MountPath+`/login`; access-denied (authenticated, wrong role) → 403 across all envs. Single class routed at `CookieAuthenticationOptions.Events`; no separate handler per environment | 03-04 execution |
| `gamekit:admin:login` rate-limit policy = sliding-window, 5/min/IP, 4 segments — partitioned by `RemoteIp`; policy name locked in `AdminAuthenticationSchemeConstants` and referenced by plan 03-07 login endpoint via `.RequireRateLimiting("gamekit:admin:login")` | 03-04 execution |
| `AdminCspNonceMiddleware.NonceKey = "gamekit.admin.csp-nonce"` — 128-bit nonce generated per request via `RandomNumberGenerator.GetBytes(16)` + Base64 encode; stored on `HttpContext.Items` for `App.razor` to read into `<script nonce="...">` and injected into CSP policy `script-src 'self' 'nonce-{value}'`; co-headers `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` (redundant to `frame-ancestors 'none'` for legacy-browser compat) | 03-05 execution |
| `AntiforgeryValidationFilter` + `ValidationEndpointFilter<T>` are `public sealed` (not internal sealed as PATTERNS.md §endpoint-filters suggested) — plan 03-05 reference code + UseMiddleware wiring in plan 03-06 require public constructors; PATTERNS.md guidance overridden by plan-supremacy | 03-05 execution |
| `AntiforgeryValidationFilter` throws `AntiforgeryValidationException` → maps to 400 Bad Request with problem-detail `csrf_validation_failed` code; endpoints opt-in via `.AddEndpointFilter<AntiforgeryValidationFilter>()` rather than global middleware so GET/read endpoints skip the token check | 03-05 execution |
| Admin CSP test harness: `TestResponseFeature.FireOnStartingAsync()` helper replaces `Response.StartAsync()` (which no-ops on `DefaultHttpContext`) — needed because `AdminCspNonceMiddleware` writes response headers in a `Response.OnStarting(...)` callback and tests need to trigger it synchronously | 03-05 execution |
| AdminAuthService.DummyHash is a real BCrypt.Net-Next 4.1.0 hash (`$2a$12$IqEI8DJ7RlcRdaL03LoJo.JbZ1kR.Ao4S3xPGk7XQdhaPfwmAyv2q`) for password "admin-dummy-never-matches" at work factor 12 — paste-verbatim literal ensures BCrypt.Verify runs the full work-factor comparison (timing parity T-03-06-03). Distinct from PasswordOAuthProvider.DummyHash so a leak of one does not compromise the other | 03-06 execution |
| AdminUserService.DeleteAsync counts remaining superadmins INSIDE the SERIALIZABLE tx before removing the target — throws LastSuperadminException when `superadminCount <= 1 && target.Role == superadmin` (T-03-06-02). No retry loop — the read-then-check pattern is serializable-safe | 03-06 execution |
| PlayerBanService uses tracked-mutation + SaveChanges + audit-write + Commit (not ExecuteUpdate) — single-row ban does not benefit from bypassing the change tracker; keeps mutation + audit row in one SaveChanges atomicity window inside the SERIALIZABLE tx (T-03-06-01) | 03-06 execution |
| PlayerSearchService.ClassifyInput is `public static` — enables unit tests to exercise the 4-mode branch (None/Id/Identity/DisplayName) without a DbContext; also exposes the classification for consumer extension points | 03-06 execution |
| ErrorRateRingBuffer takes IClock via constructor (not DateTimeOffset.UtcNow) — enables FakeClock deterministic decay tests per W6 acceptance criterion; default 300 buckets of 1-second width covering a 5-minute rolling window | 03-06 execution |
| LogErrorCounter registered as ILoggerProvider Singleton in AddGameKitAdmin — every ILogger<T> in the host now feeds LogLevel.Error+ events into the ring buffer; no opt-in required from the consumer | 03-06 execution |
| HealthProbeService status thresholds: 0-9 errors = OK, 10-99 = Degraded, 100+ = Down per RESEARCH §Health Panel; Redis probe returns "Degraded: not configured" when IConnectionMultiplexer is null (test-host supplies one; consumer apps may not) | 03-06 execution |
| AddGameKitAdmin's AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddCookie(GameKitAdmin, ...) — W4 requirement: preserve Phase 2 JWT Bearer as the DEFAULT auth scheme so /auth/me and other Bearer endpoints continue to authenticate. Admin cookie is a NAMED scheme only. Authorization policies call AddAuthenticationSchemes(GameKitAdmin) explicitly to pin scheme | 03-06 execution |
| AdminRuntimeQueryCustomizer test-host workaround: under Host.CreateDefaultBuilder + ConfigureWebHostDefaults the DbContext factory is invoked TWICE with DIFFERENT service providers — the first (generic-host) sees 0 IModelBuilderExtension registrations, the second (web-host) sees all registrations. EF caches the model from the first call, so AdminUser never lands in the runtime model. Bypass via ReplaceService<IModelCustomizer, AdminRuntimeQueryCustomizer> in AdminTestHost. Phase 2 AuthTestHost has the same issue and uses an analogous AuthRuntimeQueryCustomizer | 03-06 execution |
| GameKit.Auth grants InternalsVisibleTo("GameKit.Admin.Integration.Tests") — enables AdminRuntimeQueryCustomizer to apply internal Auth configurations (PlayerIdentityConfiguration, PlayerCredentialConfiguration, RefreshTokenConfiguration) directly without reflection; grant is documented with a forward-reference to plan 03-06 in GameKit.Auth/AssemblyInfo.cs | 03-06 execution |
| TestDbContextFactory in GameKit.Admin.Tests applies JsonDocument ValueConverter for Before/After columns so AdminAuditWriter unit test roundtrips through the InMemory provider; mirrors Phase-1 GameKit.Core.Tests factory pattern | 03-06 execution |
| AdminCspNonceMiddleware writes its strict CSP unconditionally (NOT ContainsKey-guarded) — ASP.NET Core's static-SSR Blazor antiforgery pipeline pre-sets a less-strict default with `frame-ancestors 'self'`; the prior guard silently honored it. Override is safe because the middleware is path-prefix gated to /admin/* | 03-13 execution |
| AdminTestHost.StartAsync gains `Action<GameKitAdminOptions>? configureAdmin` overload — backwards-compatible (default null); enables MountPathTests to override MountPath without a parallel test-host class | 03-13 execution |
| One [Fact(DisplayName="SC#N: …")] per ROADMAP success criterion in dedicated SC-anchor test classes (RoadmapScenarioTests / ProductionGateTests / CrossSchemeIsolationTests / CspAndAntiforgeryTests / PanelRenderTests / MountPathTests) — `grep 'SC#1'` locates the regression test for any roadmap claim | 03-13 execution |
| Direct-Npgsql seeding (players, identities, sessions, participants) instead of EF DbContext in integration tests — avoids the FOLLOW-UP-02-03-01 two-service-provider quirk that would otherwise require AdminRuntimeQueryCustomizer plumbing per test | 03-13 execution |
| GameSessionState stored as text (HasConversion<string>()) — raw SQL in seeds/fixtures MUST use enum name strings ('Active', 'Cancelled', 'Completed') NOT integer cast values; EF WHERE predicates generate 'Active' string comparison | 04-05 execution |
| Optional port injection via factory lambda (GetService<T>) for IPostSessionCompleteHandler, IIdempotencyStore, ICanonicalRequestHasher — Core operates in degraded mode (session state transition only) when Rankings not installed | 04-05 execution |
| IIdempotencyStore.StoreAsync + IPostSessionCompleteHandler.OnCompletedAsync run inside the caller's ambient transaction (ReadCommitted) — SaveChanges called internally, Commit is the SessionCompleteService's responsibility | 04-05 execution |
| EndSeasonService writes audit rows directly via _ctx.Set<AdminAuditLog>() (NOT IAdminAuditWriter) — IAdminAuditWriter lives in Admin.UI; Admin.UI declares a ProjectReference to Rankings (for EndSeasonDialog DI injection). Using IAdminAuditWriter would create a circular dependency. AdminAuditLog is a Core entity; the action literal "admin.ladder.end_season" is duplicated as a private const in EndSeasonService with a sync-comment pointing to AdminAuditActions.LadderEndSeason | 04-07 execution |
| LeaderboardService assigns ranks in-memory after ORDER BY Rating DESC rather than using EF Core ROW_NUMBER() OVER() window functions — EF Core 10 / Npgsql translation of window functions is inconsistent across query shapes; 500-row cap makes the in-memory sort cost negligible | 04-07 execution |
| AntiforgeryValidationFilter DRY-cloned into GameKit.Rankings/Http/EndpointFilters/ (not shared) — Open Q4 pattern; sharing would require a fourth package or polluting Core with admin filter concerns | 04-07 execution |

### Open Questions

None. All open questions from PROJECT.md were resolved before research completed. Research confirmed the resolutions.

### Todos

(none yet — accumulated during plan execution)

### Blockers

None.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 260416-tlm | Build Tic-Tac-Toe Duel sample app demonstrating Phase 1 GameKit | 2026-04-17 | 677260e | [260416-tlm-build-tic-tac-toe-duel-sample-app-demons](./quick/260416-tlm-build-tic-tac-toe-duel-sample-app-demons/) |
| 20260515 | Phase 03.1 verification-gap closure: BLOCKER-GAP-01 (PlayerDetailPane admin lookup), Blazor Server ConfigureAwait regression, INFO-GAP-03 (Tweaks panel aria-checked timing); +2 bUnit regression tests; VERIFICATION.md flipped to 6/6 | 2026-05-15 | ded277d | [20260515-phase-031-verification-gaps](./quick/20260515-phase-031-verification-gaps/) |

## Session Continuity

**Last action:** 2026-05-16T (execute-phase) — Plan 04-05 complete. POST /api/sessions/{id}/complete endpoint with idempotency dedup + pending_rating_updates enqueue. Bug fixed: raw SQL seed used integer cast of GameSessionState.Active but column stores text ('Active'). All 6 SessionCompleteIdempotencyTests pass; 20/20 Rankings integration tests green. Commits: 4975588 + 445b3f7 + 998297f.

**Previous action:** 2026-05-15T (quick task) — `.planning/quick/20260515-phase-031-verification-gaps/` complete. Closed the three open Phase 03.1 verification items: (1) BLOCKER-GAP-01 — `PlayerDetailPane.LoadAsync` now resolves the banning admin's username via `Db.Set<AdminUser>().AsNoTracking()` against `gamekit.admin_users` (the prior `IPlayerDisplayNameResolver` path queried `players`, which by design never holds admin IDs, so every human-issued ban rendered the deleted-player tombstone); (2) Blazor Server anti-pattern — removed both `ConfigureAwait(false)` calls in `PlayerDetailPane.LoadAsync` (continuations subsequently call `StateHasChanged()`); (3) INFO-GAP-03 — `openTweaks()` in `gamekit-admin.js` now invokes `applyAttrs(loadTweaks())` so aria-checked reflects the persisted selection before the Tweaks panel becomes visible (the deferred-script bundle-init call ran before Blazor mounted the panel). Removed the stale `@inject IPlayerDisplayNameResolver` directive from `PlayerDetailPane.razor` and the now-unused `NoopDisplayNameResolver` registration from `PlayersWorkspaceTests`. New regression test `PlayerDetailPaneBanAttributionTests` (2 facts) seeds an admin row + admin_audit_log ban row and asserts BanBanner ActorName renders the admin's username, with a paired test for the no-audit-row fallback to "unknown actor". Uses a local `BunitContext` disposed via `await using` to avoid the MudBlazor `KeyInterceptorService` IAsyncDisposable-only teardown trap that fires when the full pane (with MudTabs) renders. Admin.Tests 90 → 92; Admin.Integration.Tests 15/15 in isolation (no functional change). VERIFICATION.md re-verified at 6/6.

**Previous action:** 2026-04-26T (sequential executor) — Plan 03-13 complete (Wave 6): ROADMAP SC#1–SC#6 integration test matrix. Six new test files in `tests/GameKit.Admin.Integration.Tests/`: RoadmapScenarioTests (SC#1 mount + bootstrap + login + 3-mode search), ProductionGateTests (SC#2 — 4 facts: Production 404 / Development 302 / startup throw / login reachable), CrossSchemeIsolationTests (SC#6 — player JWT 404 in Production + ≠200 in Development via FakePlayerJwtIssuer + Bearer header), CspAndAntiforgeryTests (SC#5 — 7-directive mandatory CSP + unique nonces + scoped to /admin/* + 400 csrf_validation_failed on POST /ban without antiforgery), PanelRenderTests (SC#4 — `Install GameKit.Matchmaking` + `Install GameKit.Rankings` placeholders + 3-probe HealthReport + match-history join), MountPathTests (ADMIN-02 — custom prefix relocates API only; Blazor shell stays at /admin). 1 deviation auto-fixed: AdminCspNonceMiddleware ContainsKey-guard removed so the strict GameKit policy takes precedence over ASP.NET Core's static-SSR default `frame-ancestors 'self'` (Rule 1 bug; the override is safe — middleware is already path-prefix gated). 1 modification: AdminTestHost.StartAsync gains `configureAdmin` callback for per-test options. Commits: `9a862da` (t1: 6 facts) + `954c35e` (t2: 7 facts + middleware fix). Admin.Integration.Tests 23 → 53 (+30 from this plan + sibling Wave 5/6 plans); Admin.Tests unchanged at 54/0/0. Phase 3 SC matrix is now anchored end-to-end. Pre-existing Auth integration `PendingModelChangesWarning` failures (38/44) remain out of scope (deferred-items.md, captured by 03-10 executor). Phase 3 close-out depends only on plan 03-12's pending operator walkthrough.

**Previous action:** 2026-04-19T14:02:29Z — Plan 03-06 complete (Wave 3): the largest Phase 3 plan. Shipped 6 interface/implementation pairs (IAdminAuditWriter/AdminAuditWriter + 9 namespaced actions in AdminAuditActions; IAdminAuthService/AdminAuthService with real BCrypt 4.1.0 dummy-hash literal for T-03-06-03 timing parity; IPlayerSearchService/PlayerSearchService with public-static ClassifyInput; IPlayerBanService/PlayerBanService with SERIALIZABLE tx + snapshot-before + audit-write; IAdminUserService/AdminUserService with SERIALIZABLE create + 40001-retry + 23505-collision + last-superadmin guard; IHealthProbeService/HealthProbeService with Postgres SELECT 1 + Redis PING + ring-buffer read) + ErrorRateRingBuffer (lock-free, IClock-driven) + LogErrorCounter ILoggerProvider + SuperadminGateHostedService (D-04 Production throw / D-05 Development warn) + 6 DTOs (PaginatedResult<T>, PlayerRow, PlayerSearchResult, HealthReport, HealthTile, SearchMode enum + PlayerSearchClassification record struct) + AdminBuilderExtensions.AddGameKitAdmin (SP-5 wire-up preserving Bearer as default auth scheme per W4) + AdminApplicationBuilderExtensions.UseGameKitAdmin + MapGameKitAdmin + AdminEndpoints placeholder (plan 03-07 replaces body) + AdminTestHost (in-process TestServer with AdminRuntimeQueryCustomizer bypass for FOLLOW-UP-02-03-01 two-service-provider issue under Host.CreateDefaultBuilder + ConfigureWebHostDefaults). 13 new unit tests (audit writer + search classification + auth dummy-hash + ring-buffer decay using FakeClock) + 11 new integration tests (SuperadminGateTests 3 + PlayerBanServiceTests 2 + HealthProbeTests 3 + AuthSchemeIsolationSmokeTests 3). Commits: 3049a3c (t1 services + DTOs + unit tests) + 3aa02bd (t2 health + ring buffer + gate + AdminTestHost + integration tests + GameKit.Auth InternalsVisibleTo grant to GameKit.Admin.Integration.Tests) + fc6abcc (t3 fluent builder). Admin.Tests 35/0/0 (+16 from plan start); Admin.Integration.Tests 14/0/0 (+11). Full solution 17 projects / 0 warnings / 0 errors. Requirements satisfied: ADMIN-03/05/06/10. Ready for Wave 4 plan 03-07 (/admin/api/* 12-endpoint surface).

**Previous action:** 2026-04-19T05:10:00Z — Phase 3 Wave 2 complete. Two parallel worktree executors landed 03-04 (AdminCookieEvents + `gamekit:admin:login` sliding-window rate-limit; commits fbc73f4 + c662d09 + 2658428 pre-merge → squashed into merge commit) and 03-05 (AdminCspNonceMiddleware + AntiforgeryValidationFilter + ValidationEndpointFilter<T>; 1c0d2a2 + d5a1d7a + be45453 pre-merge → squashed into merge commit). Post-merge test gate: Admin.Tests 19/0/0 (up from 11/0/0 pre-wave), full-solution build clean (17 projects, 0 warnings, 0 errors). No cross-plan file conflicts — Wave 2 intra-wave overlap check confirmed disjoint file sets; merges clean with no conflicts. Status-code matrix and CSP/antiforgery primitives now ready for Wave 3 (03-06 services + fluent builder that wires both in).

**Previous action:** 2026-04-19T04:29:17Z — Plan 03-03 complete: GameKit.Admin.UI Razor Class Library promotion + options/constants surface for plans 03-04 through 03-09. Task 1 (a614f3e) rewrote src/GameKit.Admin.UI/GameKit.Admin.UI.csproj: SDK Microsoft.NET.Sdk -> Microsoft.NET.Sdk.Razor + AddRazorSupportForMvc=true (required so plan 03-08 .razor pages compile), added FrameworkReference Microsoft.AspNetCore.App (Cookies/Antiforgery shared-framework types), added MudBlazor / FluentValidation / FluentValidation.DependencyInjectionExtensions / StackExchange.Redis PackageReferences (CPM-resolved from Directory.Packages.props), preserved EF Core + Relational + Design + Npgsql + Core/Auth ProjectReferences from plan 03-02 W5. Replaced AssemblyInfo.cs SPDX-only stub with [assembly: InternalsVisibleTo("GameKit.Admin.Tests")] + InternalsVisibleTo("GameKit.Admin.Integration.Tests") + internal static class AdminUiMarker (mirrors GameKit.Auth.AuthMarker). Strengthened SmokeTests.TestProject_Loads from Assert.True(true) placeholder to Assert.NotNull(typeof(GameKit.Admin.UI.AdminUiMarker)) — proves InternalsVisibleTo resolves at compile-time and assembly loads at test-time. Task 2 (cc2cf49, TDD: RED CS0234 -> GREEN 4/4) added GameKitAdminOptions.cs (root + nested AdminCookieOptions/AdminPanelOptions/AdminCspOptions; 11 documented public properties with production-safe defaults: MountPath="/admin", Cookie.{Name="gk_admin_session", ExpireTimeSpan=8h, SlidingExpiration=true, RememberMeDuration=30d}, Panel.{RefreshInterval=10s, HealthErrorRateWindow=5m, HealthErrorRateBucketSize=1s}, Csp.ReportOnly=false), Authorization/AdminRoles.cs (Admin="admin"/Superadmin="superadmin" — must match ck_admin_users_role values), Authorization/AdminPolicies.cs (Admin="gamekit.admin.admin"/Superadmin="gamekit.admin.superadmin" — namespaced under gamekit.admin.*), Authentication/AdminAuthenticationSchemeConstants.cs (Scheme="GameKitAdmin" distinct from JwtBearerDefaults.AuthenticationScheme satisfying ROADMAP SC#6, CookieName="gk_admin_session", CsrfHeaderName="X-GameKit-Admin-CSRF", CsrfCookieName="gk_admin_csrf"), and tests/GameKit.Admin.Tests/GameKitAdminOptionsValidationTests.cs (4 [Fact]s: defaults table + AdminRoles + AdminAuthenticationSchemeConstants + AdminPolicies). No deviations — plan executed exactly as written; only formatting variance was dropping the W5/EF-Core block comments from the prior csproj header (the rewrite verb was REWRITE not EDIT; carry-over decisions documented in 03-03-SUMMARY decisions list). RED phase confirmed CS0234 compile-fail on the GameKit.Admin.UI.Authentication + GameKit.Admin.UI.Authorization namespaces before GREEN drop. Final: 5/0/0 GameKit.Admin.Tests (1 smoke + 4 validation), 3/0/0 GameKit.Admin.Integration.Tests unchanged from 03-02, 17 projects build clean (0 warnings / 0 errors). No new requirements satisfied this plan — ADMIN-01 already complete from 03-01; ADMIN-02 (mountable at configurable path via app.MapGameKitAdmin) needs the actual MapGameKitAdmin extension shipping in plan 03-06+ before it can flip.

**Previous action:** 2026-04-19T04:19:13Z — Plan 03-02 complete: Admin data layer + AdminInitial migration + 3 integration tests. Task 1 (5dfe081) added AdminUser entity (Id/Username/PasswordHash/Role/CreatedAt/LastLoginAt/FailedLoginCount/LockedUntil), AdminUserConfiguration (citext Username, ck_admin_users_role CHECK, UNIQUE ix_admin_users_username, no FK to players per D-06), AdminModelBuilderExtension (lazy app-provider resolution per closed FOLLOW-UP-02-03-01), AdminMigrationConstants (__ef_migrations_admin + placeholder AdvisoryLockKey 0L), and added GameKit.Auth ProjectReference to GameKit.Admin.UI csproj per W5. Task 2 (cd223ab) added AdminMigrationModelCustomizer (ExcludeFromMigrations on 4 Core + 3 Auth entities via ExcludeEntity helper), AdminDesignTimeDbContextFactory, AdminMigrationHostedService, generated 20260419000000_AdminInitial migration; also added EF Core PackageReferences and upgraded dotnet-ef CLI 10.0.5 -> 10.0.6. Task 3 (a5c75ed) added AdminAdvisoryLockKeyTests + AdminSchemaTests; live-verified AdvisoryLockKey = -2101739634L. ADMIN-04 requirement marked complete.

**Earlier action:** 2026-04-19T12:00:00Z — Plan 03-01 complete: Phase 3 Wave-0 test scaffolding. Task 1 (02b1028) added tests/GameKit.Admin.Tests (xUnit + Moq + EF InMemory) and tests/GameKit.Admin.Integration.Tests (Testcontainers + WebApplicationFactory; no WireMock — admin surface has zero outbound HTTP), registered both in GameKit.sln, pinned MudBlazor 9.3.0 (MIT, net10.0 GA 2026-04-18) in Directory.Packages.props. Task 2 (878a372) added tests/GameKit.TestFixtures/AdminIntegrationFixture.cs (Postgres + Redis composite), AdminCollection xUnit definition (declared in both TestFixtures and Admin.Integration.Tests assemblies for xUnit1041), WebApplicationFactoryExtensions.LoginAsAdminAsync + HarvestAntiforgeryTokenAsync helpers (signatures locked for 03-04/03-07/03-13), and Mocks/FakePlayerJwtIssuer (throwaway RSA 2048 minting D-03 claim-shaped player JWTs for SC#6 isolation). Task 3 (2889eb3) added a 5-row `### GameKit.Admin.UI` per-package table to CLAUDE.md (MudBlazor / Redis / FluentValidation / Antiforgery / Cookies) + Out-of-scope + Dependency-direction note (W5: Admin.UI -> Auth ProjectReference) + MountPath-scope note (B1 step 4: /admin/api/* only) + one new MudBlazor row in the Core Technologies table. Full solution builds green (17 projects, 0 warnings, 0 errors); Admin.Tests smoke test passes 1/0/0. No deviations. ADMIN-01 requirement marked complete. Wave 1 (plans 03-02 and 03-03) now unblocked.

**Older action:** 2026-04-18T00:36:06Z — Plan 02-08 complete: TicTacToeDuel sample shipped end-to-end + human-verify approved + three follow-up fixes landed + FOLLOW-UP-02-03-01 CLOSED. Task 1 (994671b) rewrote Program.cs with AddAuth + strict middleware order, added appsettings.Development.json GameKit:Auth section (JWT + Steam realm + Discord placeholder creds), removed Phase-1 /demo/players/register + RegisterPlayerRequest/Response, added keys/{README,.gitignore}, scripts/gen-test-rsa-pem.sh (RSA 2048, 0600/0644), and full README auth section (localStorage/XSS disclaimer, PEM rotation via Kid, AllowedProviderHosts customization). Task 2 (10c0de1) shipped 488-LOC auth-aware SPA: auth panel (guest/register/login/Steam/Discord challenge), session panel (JWT decode + logout + /auth/me probe), gkFetch wrapper with X-GameKit-Device + Bearer + 401-refresh-retry-once. Task 3 human-verify walked all 15 steps in a real browser — approved. **Three follow-up fixes after walkthrough:** (a) 6c73630 fix(core,auth) — FOLLOW-UP-02-03-01 RESOLUTION: GameKitDbContext.OnModelCreating resolves IEnumerable<IModelBuilderExtension> lazily via CoreOptionsExtension.ApplicationServiceProvider; AddGameKit switches to (sp,opts) AddDbContext overload + UseApplicationServiceProvider(sp); new AuthMigrationHostedService applies __ef_migrations_auth under Auth advisory lock (-298890956) in IHost.StartAsync; UseGameKitAuth reduced to pure UseAuthentication. Also fixed: Auth migrations never applied at runtime pre-fix (tables missing on first Auth call). (b) 1f8d4f3 fix(auth) — /auth/logout no longer requires Bearer (refresh token IS the revocation capability; prior RequireAuthorization left refresh family un-revoked if access expired = security hole); OAuth callbacks return HTML BrowserTokenBridge (JSON rendered as text because Steam/Discord redirect the browser). (c) 7e96b00 fix(sample) — PEM paths changed to project-relative (dotnet run --project sets CWD to project dir, repo-root paths broke startup); dedicated upgrade-username/password inputs in session panel (auth-panel inputs were hidden → upgrade silently no-opped); formatAuthError helper parses ProblemDetails + AuthErrorResponse shapes (prior code rendered ProblemDetails as "Bad Request"). Phase 2 success criteria coverage: #1 (4-provider login — Guest/Password/Steam e2e in browser; Discord WireMock + service-layer), #2 (forged Steam — E2E + spot-checked in browser), #3 (refresh rotation UX proven in browser), #4 (concurrent guest-upgrade via plan 02-06), #5 (cross-player link 409), #6 (rate-limit 429). Full unit suite 166/166 green post-fix. AUTH-01 requirement closed.

**Next action:** Plan 04-02 (Rankings package skeleton + EF migration). Plan 04-01 complete — license attribution, test csprojs, RankingsFixture, and Glickman fixture all committed.
**Resume file:** None
**Stopped at:** Phase 04 Plan 01 complete
**Blockers:** None

**Follow-up RESOLVED:** FOLLOW-UP-02-03-01 closed in plan 02-08 via commit 6c73630. GameKitDbContext.OnModelCreating now resolves IModelBuilderExtension lazily from CoreOptionsExtension.ApplicationServiceProvider. AddGameKit uses (sp, opts) AddDbContext overload + UseApplicationServiceProvider(sp). Direct-construction migration contexts (design-time factories + BuildMigrationContext) intentionally do NOT attach a provider, preserving the per-package migration boundary. AuthMigrationHostedService owns __ef_migrations_auth application under Auth advisory lock (-298890956). The 02-02 test-local AuthRuntimeQueryCustomizer shim can be removed by future cleanup — the runtime path now works without it. Cross-cutting: Rankings, Matchmaking, Presence can now ship sibling IModelBuilderExtensions + per-package migration hosted services mirroring this pattern.

**Context preserved:**

- PROJECT.md, REQUIREMENTS.md, research/{SUMMARY,STACK,FEATURES,ARCHITECTURE,PITFALLS}.md, config.json
- ROADMAP.md (6 phases, 92/92 coverage)
- 01-01-SUMMARY.md (repo chassis complete, 7 requirements marked complete)
- 01-02-SUMMARY.md (docker-compose + init scripts complete, DIST-01 + OPS-08 requirements)
- 01-03-SUMMARY.md (Core entities + EF configs, 8 requirements: CORE-01/03/04/06/07/08/09/17)
- 01-04-SUMMARY.md (DbContext + ModelCustomizer + MigrationRunner + CoreInitial migration, 5 requirements: CORE-02/04/11/13/14)
- 01-05-SUMMARY.md (Core runtime services + fluent builder, 6 requirements: CORE-05/10/11/12/13/16)
- 01-06-SUMMARY.md (5 sibling csprojs + CLI + SampleGame, 3 requirements: CORE-05/CORE-13/DIST-01)
- 01-07-SUMMARY.md (Test suite + CI + license-check, 18 requirements verified)
- 02-01-SUMMARY.md (Wave-0 test scaffolding — 2 test projects + WireMock fixture + AuthCollection; Directory.Packages.props Auth pins; AssemblyInfo InternalsVisibleTo + AuthMarker)
- 02-02-SUMMARY.md (Auth entities + EF configs + AuthInitial migration + three integration tests; AUTH-02/03/04/11 requirements satisfied)
- 02-03-SUMMARY.md (GameKitAuthOptions tree + DefaultAllowedHosts + EgressAllowListHandler + AddAuth + UseGameKitAuth/MapAuth; 15 unit tests; AUTH-05/AUTH-10 skeleton only — concrete issuance/interface land in 02-04/02-05)
- 02-04-SUMMARY.md (leaf Auth services + RefreshTokenService Pattern 3 rotation; 22 new tests; AUTH-09/10/11/12/16 satisfied)
- 02-05-SUMMARY.md (IOAuthProvider + in-house SteamOpenIdVerifier + Steam/Discord providers + Scrutor scan + Discord scheme; 10 new tests including forgery rejection for Success Criterion #2; AUTH-05/06/07 satisfied)
- 02-06-SUMMARY.md (Guest + Password providers + IdentityLinker + GuestUpgradeService + LinkResult + UsernameAlreadyTakenException; 9 new integration tests including Success Criterion #4 + #5 proven at service layer; AUTH-08/09/13/14 satisfied; T-02-16 timing mitigation closes plan 02-04 follow-up)
- 02-07-SUMMARY.md (/auth/* HTTP surface — 10 minimal-API endpoints + 7 DTOs + 5 FluentValidation validators + ValidationEndpointFilter<T> + AuthRateLimitRegistrations + AuthTestHost WebApplicationFactory harness; 14 new tests covering ROADMAP success #1/#2/#3/#5/#6 at e2e level; AUTH-14/15/16 satisfied)
- 02-08-SUMMARY.md (TicTacToeDuel sample + human-verify approved + 3 follow-up fixes; FOLLOW-UP-02-03-01 CLOSED via GameKitDbContext app-provider wiring + AuthMigrationHostedService; /auth/logout no longer requires Bearer (security fix); OAuth callbacks return HTML BrowserTokenBridge; AUTH-01 satisfied)
- 03-01-SUMMARY.md (Wave-0 Admin UI test scaffolding — tests/GameKit.Admin.Tests + tests/GameKit.Admin.Integration.Tests + AdminIntegrationFixture + AdminCollection + WebApplicationFactoryExtensions cookie/CSRF helpers + FakePlayerJwtIssuer; MudBlazor 9.3.0 CPM pin; CLAUDE.md GameKit.Admin.UI per-package section + MudBlazor stack row; ADMIN-01 satisfied)
- 03-02-SUMMARY.md (Admin data layer — AdminUser + AdminUserConfiguration + AdminModelBuilderExtension + AdminMigrationConstants + AdminMigrationModelCustomizer + AdminDesignTimeDbContextFactory + AdminMigrationHostedService + 20260419000000_AdminInitial migration + AdminAdvisoryLockKeyTests + AdminSchemaTests; AdvisoryLockKey -2101739634L live-verified; ADMIN-04 satisfied)
- 03-03-SUMMARY.md (GameKit.Admin.UI promoted to Razor Class Library — Microsoft.NET.Sdk.Razor + MudBlazor/FluentValidation/StackExchange.Redis PackageReferences + FrameworkReference Microsoft.AspNetCore.App + AdminUiMarker + GameKitAdminOptions root + AdminCookieOptions/AdminPanelOptions/AdminCspOptions + AdminRoles + AdminPolicies + AdminAuthenticationSchemeConstants + 4 validation tests; Admin.Tests now 5/0/0; ROADMAP SC#6 cross-scheme isolation foundation laid; no requirements newly satisfied — ADMIN-02 awaits MapGameKitAdmin extension in plan 03-06+)
- 03-04-SUMMARY.md (AdminCookieEvents — 404-in-Production / 302-in-Development / 403-on-access-denied status-code matrix satisfying ROADMAP SC#2; gamekit:admin:login sliding-window 5/min/IP rate-limit policy; 8 new Admin.Tests (7 cookie events + 1 rate-limit); Admin.Tests = 13/0/0 post-plan)
- 03-05-SUMMARY.md (AdminCspNonceMiddleware — 128-bit per-request nonce at HttpContext.Items["gamekit.admin.csp-nonce"] + strict CSP string with script-src 'self' 'nonce-...' + frame-ancestors 'none' satisfying ROADMAP SC#5 + X-Content-Type-Options/Referrer-Policy/X-Frame-Options co-headers; AntiforgeryValidationFilter endpoint filter (csrf_validation_failed ProblemDetails on 400) + ValidationEndpointFilter<T> copy from GameKit.Auth; 6 new Admin.Tests; Admin.Tests = 19/0/0 post-Wave-2)
- 03-06-SUMMARY.md (Admin services + fluent builder — 6 iface/impl pairs + AdminAuditActions + ErrorRateRingBuffer + LogErrorCounter + SuperadminGateHostedService + AdminBuilderExtensions.AddGameKitAdmin + UseGameKitAdmin + MapGameKitAdmin + AdminEndpoints placeholder + AdminTestHost + AdminRuntimeQueryCustomizer bypass for two-service-provider Host.CreateDefaultBuilder pattern; InternalsVisibleTo grant from GameKit.Auth to GameKit.Admin.Integration.Tests; 13 new unit tests + 11 integration tests; Admin.Tests 35/0/0 + Admin.Integration.Tests 14/0/0; ADMIN-03/05/06/10 satisfied)
- 03-13-SUMMARY.md (ROADMAP SC#1–SC#6 integration test matrix — 6 SC-anchored test files / 13 facts: RoadmapScenarioTests + ProductionGateTests + MountPathTests + CrossSchemeIsolationTests + CspAndAntiforgeryTests + PanelRenderTests; AdminCspNonceMiddleware override-CSP fix; AdminTestHost.StartAsync configureAdmin overload; Admin.Integration.Tests = 53/0/0 post-plan; ADMIN-02/03/04/09/10/12 anchored)
- All NuGet versions verified GA on net10.0 — Npgsql bumped to 10.0.2, Caching.Memory to 10.0.6, Microsoft.AspNetCore.Authentication.JwtBearer pinned 10.0.6
- CLAUDE.md updated from stale .NET 9 to verified .NET 10 LTS pins; CLAUDE.md JwtBearer-in-shared-framework row confirmed stale (handler split out in .NET 8)
- 219 tests green: 165 unit (130 Core + 35 Auth) + 53 integration (9 Core + 44 Auth) + 1 CLI — CI pipeline ready
- AdvisoryLockKey values: Core = 1800940027 (positive), Auth = -298890956 (negative), Admin = -2101739634 (negative); all three verified against live Postgres 17.9 via Testcontainers; pairwise distinct per AdminAdvisoryLockKeyTests + AuthAdvisoryLockKeyTests

---
*Initialized: 2026-04-15 at roadmap creation.*
