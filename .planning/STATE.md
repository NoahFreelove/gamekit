---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
last_updated: "2026-04-18T00:36:06Z"
progress:
  total_phases: 6
  completed_phases: 1
  total_plans: 15
  completed_plans: 15
  percent: 100
---

# STATE: GameKit

## Project Reference

**Core Value:** A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.

**License:** GPL
**Runtime:** .NET 10 LTS (released 2026-04-14)
**Mode:** YOLO / Quality model profile / parallel execution enabled
**Current Focus:** Phase 02 — Authentication — all 8 plans complete; awaiting verifier

## Current Position

Phase: 02 (Authentication) — AWAITING VERIFIER
Plan: 8 of 8 complete
**Milestone:** v1 (initial 6-phase build-out)
**Phase:** 2
**Plan:** 02-08 complete (human-verify approved + 3 follow-up fix commits landed)
**Status:** Phase 2 code + docs complete; orchestrator runs gsd-verifier next. Do not mark Phase 2 checkbox in ROADMAP.md "Phases" list done until the verifier passes.

**Progress:** [███████████████] 100% (plan count; phase checkbox still pending verifier)

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

## Accumulated Context

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
| Glicko-2 vendored from MaartenStaa/glicko2-csharp (MIT) | STACK.md |
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

## Session Continuity

**Last action:** 2026-04-18T00:36:06Z — Plan 02-08 complete: TicTacToeDuel sample shipped end-to-end + human-verify approved + three follow-up fixes landed + FOLLOW-UP-02-03-01 CLOSED. Task 1 (994671b) rewrote Program.cs with AddAuth + strict middleware order, added appsettings.Development.json GameKit:Auth section (JWT + Steam realm + Discord placeholder creds), removed Phase-1 /demo/players/register + RegisterPlayerRequest/Response, added keys/{README,.gitignore}, scripts/gen-test-rsa-pem.sh (RSA 2048, 0600/0644), and full README auth section (localStorage/XSS disclaimer, PEM rotation via Kid, AllowedProviderHosts customization). Task 2 (10c0de1) shipped 488-LOC auth-aware SPA: auth panel (guest/register/login/Steam/Discord challenge), session panel (JWT decode + logout + /auth/me probe), gkFetch wrapper with X-GameKit-Device + Bearer + 401-refresh-retry-once. Task 3 human-verify walked all 15 steps in a real browser — approved. **Three follow-up fixes after walkthrough:** (a) 6c73630 fix(core,auth) — FOLLOW-UP-02-03-01 RESOLUTION: GameKitDbContext.OnModelCreating resolves IEnumerable<IModelBuilderExtension> lazily via CoreOptionsExtension.ApplicationServiceProvider; AddGameKit switches to (sp,opts) AddDbContext overload + UseApplicationServiceProvider(sp); new AuthMigrationHostedService applies __ef_migrations_auth under Auth advisory lock (-298890956) in IHost.StartAsync; UseGameKitAuth reduced to pure UseAuthentication. Also fixed: Auth migrations never applied at runtime pre-fix (tables missing on first Auth call). (b) 1f8d4f3 fix(auth) — /auth/logout no longer requires Bearer (refresh token IS the revocation capability; prior RequireAuthorization left refresh family un-revoked if access expired = security hole); OAuth callbacks return HTML BrowserTokenBridge (JSON rendered as text because Steam/Discord redirect the browser). (c) 7e96b00 fix(sample) — PEM paths changed to project-relative (dotnet run --project sets CWD to project dir, repo-root paths broke startup); dedicated upgrade-username/password inputs in session panel (auth-panel inputs were hidden → upgrade silently no-opped); formatAuthError helper parses ProblemDetails + AuthErrorResponse shapes (prior code rendered ProblemDetails as "Bad Request"). Phase 2 success criteria coverage: #1 (4-provider login — Guest/Password/Steam e2e in browser; Discord WireMock + service-layer), #2 (forged Steam — E2E + spot-checked in browser), #3 (refresh rotation UX proven in browser), #4 (concurrent guest-upgrade via plan 02-06), #5 (cross-player link 409), #6 (rate-limit 429). Full unit suite 166/166 green post-fix. AUTH-01 requirement closed.

**Next action:** Run gsd-verifier for Phase 2 (orchestrator-driven; do not advance phase 2 "Phases" checkbox until verifier passes)
**Resume file:** none (Phase 2 execution complete)
**Stopped at:** Completed 02-08-PLAN.md + human-verify + 3 follow-up commits
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
- All NuGet versions verified GA on net10.0 — Npgsql bumped to 10.0.2, Caching.Memory to 10.0.6, Microsoft.AspNetCore.Authentication.JwtBearer pinned 10.0.6
- CLAUDE.md updated from stale .NET 9 to verified .NET 10 LTS pins; CLAUDE.md JwtBearer-in-shared-framework row confirmed stale (handler split out in .NET 8)
- 219 tests green: 165 unit (130 Core + 35 Auth) + 53 integration (9 Core + 44 Auth) + 1 CLI — CI pipeline ready
- AdvisoryLockKey values: Core = 1800940027 (positive), Auth = -298890956 (negative); both verified against live Postgres 17.9

---
*Initialized: 2026-04-15 at roadmap creation.*
