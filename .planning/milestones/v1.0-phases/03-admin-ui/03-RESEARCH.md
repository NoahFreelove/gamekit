# Phase 3: Admin UI — Research

**Researched:** 2026-04-18
**Domain:** Blazor Server 10 + ASP.NET Core cookie auth + Razor Class Library packaging + CSP/CSRF hardening
**Confidence:** HIGH (stack + patterns verified against Microsoft docs + nuget.org registry + existing Phase 2 codebase)

---

<user_constraints>
## User Constraints (from 03-CONTEXT.md)

### Locked Decisions

| # | Decision | Plan impact |
|---|----------|-------------|
| **D-01** | Admin auth = **form + HttpOnly secure cookie**. `/admin/login` renders Blazor Server login page; submit issues `SameSite=Lax` HttpOnly cookie; `/admin/logout` clears it. Chose over HTTP Basic + admin-token header. | Plans DO NOT explore Basic/token auth. Implement cookie flow only. |
| **D-02** | Admin auth scheme = **`GameKitAdmin`** — distinct scheme name, cannot be confused with player JWT. Integration test asserts valid player JWT → 404/403 on `/admin/*` (ROADMAP SC #6). | Dedicated scheme registration, NOT shared with Bearer. Test required. |
| **D-03** | Player `is_banned` enforced at TWO auth checkpoints in `GameKit.Auth`: **login path** (`IOAuthProvider.CompleteLoginAsync` → 403 with banned-reason-hash) and **refresh path** (`RefreshTokenService.RotateAsync` → revoke family). **Not** enforced per-request via middleware. | This is a Phase 2 code modification triggered by Phase 3 scope. Plans must edit existing Phase 2 code. |
| **D-04** | `AddGameKitAdmin` throws **`InvalidOperationException`** at startup in Production when zero `admin_users` with `role='superadmin'` exist. Message points at `dotnet gamekit admin create`. Via `ValidateOnStart()` pipeline or equivalent startup assertion. | Must fire BEFORE Kestrel accepts traffic. Explicit test required. |
| **D-05** | In Development / Staging, startup does NOT throw — warning logged + panel inline state. | Plans wire environment-based branching in the startup check. |
| **D-06** | **Two-tier role model** — `admin` + `superadmin`. Column `role text CHECK (role IN ('admin','superadmin'))` on `admin_users`. Admin: ban/unban, view audit, view matches, view health. Superadmin: additionally create/delete admins, GDPR delete, rank adjust, signing-key rotate. | Schema check constraint required. Authorization policies per role. |
| **D-07** | Startup assertion specifically requires **≥1 superadmin** in Production. Regular admin accounts alone DO NOT satisfy the gate. | The LINQ query is `Any(a => a.Role == "superadmin")`, not `Any()`. |
| **D-08** | `dotnet gamekit admin create` = **interactive + flag-driven hybrid**. `--username`, `--password`, `--role` flags; missing flags prompt on stdin. Password via `Console.ReadKey(intercept: true)`. **Exception:** when zero `admin_users` exist, first admin auto-promoted to superadmin regardless of flag. On success, prints username + role + hashed-credential prefix. | Spectre.Console.Cli settings class + AsyncCommand handler. |
| **D-09** | Ban reason = **required**, **3–512 chars**. Validated client-side (Blazor form) + server-side (endpoint filter) via FluentValidation. Stored verbatim in `admin_audit_log.after_json->>'ban_reason'`. | Two validators (client component + server endpoint filter) sharing the same rules. |
| **D-10** | **Health + queue-depth panels refresh via polling + manual Refresh button.** Default interval: 10s, configurable via `GameKitAdminOptions.PanelRefreshInterval`. `System.Threading.Timer` bound to component lifecycle. NO SignalR push. | Plans wire `System.Threading.Timer`; `IDisposable`/`IAsyncDisposable` on component. |
| **D-11** | **Player search = unified box** with input-type auto-detect: 36-char UUID → id lookup; `provider:external_id` → `player_identities` lookup; otherwise → `display_name` prefix via `citext`. Single input, single endpoint. | One endpoint returning `PlayerSearchResult` with provenance discriminator. |
| **D-12** | **Keyset / cursor pagination** on all list views. Default page 50. "Load more" appends. Indexes: `(id DESC)` for players, `(created_at DESC, id DESC)` for audit log. NO offset/limit, NO infinite scroll. | Custom pagination — NOT MudBlazor's built-in `MudPagination`. |
| **D-13** | GameKit.Admin.UI ships **its own Blazor layout + minimal shell CSS**. CSS scoped via Blazor's `.razor.css` isolation. No theme hooks in v1. | Shell CSS goes in `*.razor.css` scoped files. |
| **D-14** | **MudBlazor** (MIT, GPL-compatible). Phase 3 MUST verify `net10.0` TFM before pinning; fall back to latest `net9.0`-compatible version under compatibility shim if not GA. CLAUDE.md stack-table gets updated in Plan 03-01. | Version verification below [VERIFIED: nuget.org 2026-04-18]. |
| **D-15** | **Strict CSP with per-request nonce.** Default policy: `default-src 'self'; script-src 'self' 'nonce-<per-request>'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`. Nonce generated per-request via middleware + threaded into `<script>` tags. Integration test asserts CSP header present on every `/admin/*` response. | Custom nonce middleware. Nonce lives in `HttpContext.Items`. |
| **D-16** | **Anti-CSRF on all mutations** via `Microsoft.AspNetCore.Antiforgery`. All POST/DELETE/PATCH admin endpoints call `IAntiforgery.ValidateRequestAsync`. Integration test: mutation without token → 400. | Endpoint filter that wraps `ValidateRequestAsync`. |
| **D-17** | Admin mutations write to `admin_audit_log` via `IAdminAuditWriter` service (Scoped, mirrors Phase 2 `IAuthAuditWriter`). Actions namespaced: `admin.player.ban`, `admin.player.unban`, `admin.admin.create`, `admin.admin.delete`, `admin.player.gdpr_delete`, `admin.player.rank_adjust`, `admin.signing_key.rotate`. Before/after JSON captures field diff. | Mirror Phase 2 `IAuthAuditWriter` wiring: Scoped, DbContext-bound, writes inside caller's transaction. |
| **D-18** | `/admin/login` uses rate-limit policy `gamekit:admin:login` — 5 attempts/min/IP, sliding window. Registers through existing `IGameKitRateLimitPolicies`. | Plans mirror `AuthRateLimitRegistrations` but with sliding-window limiter and IP-only partition. |

### Claude's Discretion

- `admin_users` schema — base columns fixed; planner may add defense-in-depth columns (failed-login counter, `locked_until`). Password hash uses Phase 2 `IPasswordHasher`.
- Migration pattern — Phase 2 precedent. History table `__ef_migrations_admin`, advisory lock from `hashtext('gamekit.admin.migrations')::bigint` (live-verified via Testcontainers), migrations assembly = `GameKit.Admin.UI`, `IModelBuilderExtension` for admin entities.
- Panel component structure — planner picks page routing + component split. MudBlazor `DataGrid` is default for tabular; plain `<table>` fine for audit log if DataGrid feels heavy.
- Health panel data sources — Postgres via `NpgsqlConnection.Ping`-equivalent; Redis via `IConnectionMultiplexer.GetStatus()`; error rate from in-memory ring buffer populated by log filter.
- Match history — direct EF query against `game_sessions` + `session_participants` + `players`. Reuse GDPR export query patterns.
- Cookie lifetime — suggested 8h sliding; remember-me extends to 30d. DataProtection-signed.
- Login form UX — include "Contact a superadmin" stub for password recovery.
- CSP reporting — planner decides. No phone-home default.
- GDPR delete panel — calls existing CORE-16 `IGdprDeleteService`. Confirmation dialog required.

### Deferred Ideas (OUT OF SCOPE)

- RBAC beyond admin/superadmin
- SSO (Entra/Okta/Keycloak) admin login
- Admin UI localization
- Audit log retention/archival policy
- Mobile-responsive admin UI
- Dark mode + theme hooks
- Admin password reset self-service flow
- CSP violation reporting endpoint
- WebAuthn / passkey admin login

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description (from REQUIREMENTS.md) | Research Support |
|----|------------------------------------|------------------|
| ADMIN-01 | Library ships as `GameKit.Admin.UI` package — Blazor Server in a Razor Class Library | § Package Composition: RCL SDK `Microsoft.NET.Sdk.Razor`; static assets under `wwwroot/` → `_content/GameKit.Admin.UI/…` |
| ADMIN-02 | Mountable at configurable path via `app.MapGameKitAdmin("/admin")` | § Package Composition: endpoint convention builder + routing pattern |
| ADMIN-03 | Default-deny route policy: 404 (not 401) on unauth in Production; startup assertion fails fast if no role | § Authentication: 404-not-401 via custom `CookieAuthenticationEvents.OnRedirectToLogin`; startup via `IHostedService.StartAsync` gate |
| ADMIN-04 | Separate auth scheme from player JWT (pluggable) | § Authentication: scheme name `"GameKitAdmin"` distinct from `JwtBearerDefaults.AuthenticationScheme` |
| ADMIN-05 | Player search (by id, display name, identity) | § Data Access: unified search box — UUID → id, `provider:external_id` → identities, else `display_name` citext prefix |
| ADMIN-06 | Player ban/unban with mandatory reason — writes to `admin_audit_log` | § Panels: ban/unban endpoint + FluentValidation reason rule (3–512); § § D-17 audit writer |
| ADMIN-07 | Manual rank adjustment UI (functional once Rankings package present) | § Panels: `MudAlert Severity=Info` placeholder when `IRankingAlgorithm` not registered; superadmin-only |
| ADMIN-08 | Match history viewer | § Panels: direct EF query against `game_sessions` + `session_participants` + `players` (reuse GDPR pattern) |
| ADMIN-09 | Live matchmaking queue depth panel (functional once Matchmaking present) | § Panels: placeholder when `IMatchmakingStrategy` not registered |
| ADMIN-10 | Health panel: Postgres + Redis connectivity + recent error rate | § Panels: `NpgsqlConnection.OpenAsync`+`SELECT 1`, `IConnectionMultiplexer.GetStatus()`, log-filter ring buffer |
| ADMIN-11 | First-admin bootstrap CLI (cannot mount in Production until one exists) | § CLI Bootstrap: Spectre.Console.Cli + `Console.ReadKey(intercept:true)` + auto-promote first admin |
| ADMIN-12 | CSP headers + anti-CSRF token enforcement on all mutations | § UI Hardening: per-request nonce middleware + `IAntiforgery.ValidateRequestAsync` endpoint filter |

</phase_requirements>

---

## Summary

Phase 3 ships **`GameKit.Admin.UI`** as a Blazor Server Razor Class Library (RCL) NuGet package that a consumer app adds via `AddGameKitAdmin(...)` + `app.MapGameKitAdmin("/admin")`. The package owns its own EF migrations (`__ef_migrations_admin` history, advisory lock `hashtext('gamekit.admin.migrations')::bigint`), adds an `admin_users` table, mounts a MudBlazor-based UI, authenticates via a distinct cookie scheme `"GameKitAdmin"` (cannot be reached by a player JWT), and hardens every response with a strict per-request-nonce CSP + antiforgery on all mutations.

**Package layout verified** against MS docs for RCL packaging: `Microsoft.NET.Sdk.Razor` SDK, static assets under `wwwroot/`, Blazor CSS isolation via `.razor.css`, routable components discovered via `Router.AdditionalAssemblies = [typeof(...).Assembly]` — but since we control mount via `MapGameKitAdmin("/admin")`, the endpoint convention builder is what stitches consumer app to our components.

**MudBlazor 9.3.0 (MIT) has a confirmed `net10.0` TFM** as of 2026-04-08 per nuget.org; dependencies land on `Microsoft.AspNetCore.Components ≥ 10.0.1`. No fallback shim needed. This is LOCKED and the UI-SPEC has already approved it.

**Strict CSP with per-request nonce** is implementable either via the MS-recommended package `NetEscapades.AspNetCore.SecurityHeaders` (which ships a `BlazorNonceService` via `CircuitHandler`) OR via a hand-rolled middleware that puts the nonce in `HttpContext.Items` and threads it to the `<script>` tags rendered in `App.razor`. The hand-rolled path keeps us zero-dependency per the GameKit "install only what you need" principle; research recommends this path (details in § UI Hardening).

**Startup fail-fast** (D-04) is best implemented as an `IHostedService.StartAsync` database probe (NOT `OptionsBuilder.ValidateOnStart()` — that validates options, not DB state). An `IHostedService` registered by `AddGameKitAdmin` runs AFTER `AuthMigrationHostedService` (so `admin_users` exists) but BEFORE Kestrel accepts traffic. An exception thrown from `StartAsync` kills the host, exactly matching D-04 expectations.

**Phase 2 code changes triggered by Phase 3 scope (D-03):** `IOAuthProvider.CompleteLoginAsync` checks `Player.IsBanned` and returns `OAuthResult.Fail("banned")`; `RefreshTokenService.RotateAsync` checks `Player.IsBanned` and revokes the family. Both paths touch EXISTING Phase 2 code and must include new tests.

**Primary recommendation:** Split into 8 plans — (1) Wave 0 test scaffolding + Directory.Packages.props pins; (2) AdminUser entity + schema + Admin migration; (3) `GameKitAdminOptions` + `AddGameKitAdmin` + `AdminMigrationHostedService` + `SuperadminGateHostedService`; (4) cookie auth scheme + `IAdminAuthService`; (5) CSP middleware + antiforgery filter + 404-not-401 handler; (6) `IAdminAuditWriter` + ban/unban/GDPR services + player search + match history; (7) Blazor components (MudBlazor layout + all panels); (8) CLI `admin create` command + Phase 2 ban enforcement patches + `TicTacToeDuel` sample wiring.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Admin login form render | Frontend Server (SSR Blazor Server) | — | `/admin/login` is a Blazor Server page; SignalR circuit established on interactive element |
| Admin session (cookie) | API / Backend | Frontend Server | HttpOnly cookie issued by `CookieAuthenticationHandler`; Blazor circuit reads via `AuthenticationStateProvider` |
| Admin password hash | API / Backend | — | `IPasswordHasher` (Phase 2 reused) — `BCrypt.Net-Next`, never run client-side |
| Player search | API / Backend | Frontend Server | Endpoint `POST /admin/players/search` backed by `GameKitDbContext` |
| Ban/unban mutation | API / Backend | — | Minimal-API endpoint + `IAdminAuditWriter` + EF write inside one transaction |
| Audit log list | API / Backend | Frontend Server | Direct EF query; Blazor Server component projects rows |
| Health check (PG/Redis/errors) | API / Backend | — | Server-side probe — cannot be done client-side (needs credentials + process memory) |
| CSP + antiforgery | API / Backend (middleware) | — | Per-request nonce middleware + `IAntiforgery.ValidateRequestAsync` endpoint filter |
| 404-not-401 on unauth | API / Backend (auth events) | — | `CookieAuthenticationEvents.OnRedirectToLogin` override |
| Migration runner | API / Backend (IHostedService) | Database | `AdminMigrationHostedService` applies `__ef_migrations_admin` under advisory lock |
| Startup superadmin gate | API / Backend (IHostedService) | Database | Runs after Admin migrations, queries `admin_users`, throws if zero superadmins in Production |
| CSS shell + MudBlazor theme | Frontend Server | — | `.razor.css` isolated styles + `MudThemeProvider` |
| Client-side interactivity | Browser / Client | Frontend Server | Blazor Server SignalR circuit — all state + rendering server-side |
| CLI `admin create` | Standalone process | Database | `GameKit.Cli` → `GameKitDbContext` via DI |

---

## Standard Stack

### Core (all `net10.0` TFM verified GA on NuGet 2026-04-18)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| **MudBlazor** | **9.3.0** | Blazor component library (DataGrid, Dialog, Snackbar, Form, Autocomplete, Nav) | UI-SPEC locked (D-14). MIT license. `net10.0` TFM confirmed on nuget.org. Depends on `Microsoft.AspNetCore.Components ≥ 10.0.1`. [VERIFIED: nuget.org/packages/MudBlazor/9.3.0] |
| **Microsoft.AspNetCore.Authentication.Cookies** | 10.0 (shared framework via `Microsoft.AspNetCore.App`) | Admin cookie auth scheme `"GameKitAdmin"` | Built into `Microsoft.AspNetCore.App`; no separate package. [CITED: learn.microsoft.com aspnetcore-10.0] |
| **Microsoft.AspNetCore.Antiforgery** | 10.0 (shared framework) | CSRF token generation + `IAntiforgery.ValidateRequestAsync` | Built in; the minimal-API `.ValidateAntiforgery()` extension lives here. [CITED: MS Learn anti-request-forgery] |
| **FluentValidation** + **FluentValidation.DependencyInjectionExtensions** | 12.1.1 (already pinned) | Admin form validators — ban reason, create-admin, rank-adjust reason | Already used in Phase 2. |
| **Scrutor** | 7.0.0 (already pinned) | Not strictly needed for Admin — no pluggable strategies in Phase 3. Potential future use for customer-authored admin pages. | Already pinned repo-wide. |
| **BCrypt.Net-Next** | 4.1.0 (already pinned) | Admin password hash — reuse `IPasswordHasher` | Already pinned; Phase 2 interface unchanged. |
| **Microsoft.AspNetCore.Components.Web** | 10.0 (shared framework via `Microsoft.AspNetCore.App`) | Blazor Server primitives | Required by MudBlazor and own components. |

### Supporting (existing pins — referenced, not re-pinned)

| Library | Version | Purpose |
|---------|---------|---------|
| EF Core + Npgsql | 10.0.6 / 10.0.1 | `admin_users` table + per-package migrations |
| StackExchange.Redis | 2.8.41 | Health panel `IConnectionMultiplexer.GetStatus()` probe |
| Spectre.Console.Cli | 0.49.1 | `dotnet gamekit admin create` command |
| Testcontainers.PostgreSQL + Redis | 4.11.0 | Integration tests (mirror `AuthIntegrationFixture`) |

### Explicitly NOT added as dependencies

| Library | Why Not | What We Do Instead |
|---------|---------|--------------------|
| **NetEscapades.AspNetCore.SecurityHeaders** | Third-party dep for a pattern we can hand-roll in ~40 LOC. Principle "install only what you need" + GPL project prefers zero external security-critical deps. [VERIFIED: GitHub juunas11/aspnetcore-security-headers — MIT-licensed, maintained; recommendation based on policy not license] | Custom `AdminCspNonceMiddleware` writing the nonce to `HttpContext.Items["gamekit.admin.csp-nonce"]`, read by `App.razor` via `IHttpContextAccessor`. |
| **NWebsec** | Similar — third-party security-headers library. Legacy; active maintenance status uncertain. | Same as above — custom middleware. |
| **MR.EntityFrameworkCore.KeysetPagination** | 50-row keyset pagination is 10–15 LOC per query in the admin surface. Wrapping that in a dependency costs more long-term (version upgrades, transitive pins) than writing the `WHERE (created_at, id) < (@lastCreated, @lastId)` by hand. [VERIFIED: github.com/mrahhal/MR.EntityFrameworkCore.KeysetPagination — library works, but we decline for same "don't add deps we don't need" principle] | Hand-coded keyset `.Where` + `.OrderBy` + `.Take(51)` (the +1 row is the "more available" sentinel). |
| **Microsoft.AspNetCore.Identity** | Drags in its own user schema, UI scaffolding, and opinions that fight our `admin_users` shape. | Hand-rolled `AdminUser` entity + cookie sign-in via `HttpContext.SignInAsync("GameKitAdmin", ...)`. |
| **MediatR / AutoMapper** | Licensing tripwire — repo-wide decision (CLAUDE.md). | Plain services. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom CSP middleware | NetEscapades.AspNetCore.SecurityHeaders | Library is well-maintained + has ready `BlazorNonceService`+`CircuitHandler`. But adds transitive dep to every consumer. Project principle says hand-roll. |
| Blazor Server components | Interactive Server components in Blazor Web App | D-01 locked Blazor Server. Blazor Web App (.NET 8+) is the "new" model but requires changes to host project shape; Blazor Server RCL is more portable across consumer apps. |
| MudBlazor | Radzen / FluentUI Blazor / Ant Design Blazor | UI-SPEC locked MudBlazor. All options have `net10.0` TFMs; MudBlazor has the largest ecosystem + matches UI-SPEC palette + is MIT. |
| Keyset pagination | Offset/limit (page number) | D-12 locked keyset. Offset/limit works for tiny datasets but breaks under insert pressure (rows shift across pages) and gets slow at deep offsets. |

### Version verification (2026-04-18)

```
MudBlazor 9.3.0 — published 2026-04-08
  TFMs: net8.0, net9.0, net10.0  [VERIFIED: nuget.org/packages/MudBlazor/9.3.0]
  Deps (net10.0):
    Microsoft.AspNetCore.Components       ≥ 10.0.1
    Microsoft.AspNetCore.Components.Web   ≥ 10.0.1
    Microsoft.Extensions.Localization     ≥ 10.0.1
  License: MIT (SPDX expression)
```

**Installation additions for `Directory.Packages.props`:**

```xml
<!-- Phase 3 Admin UI — MudBlazor 9.3.0 verified GA on net10.0 2026-04-18 -->
<PackageVersion Include="MudBlazor" Version="9.3.0" />
```

No other new pins required — everything else already lives in Directory.Packages.props from Phase 1/2.

---

## Package Composition

### Project file shape (`src/GameKit.Admin.UI/GameKit.Admin.UI.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">  <!-- KEY: Razor SDK, not plain Sdk -->
  <PropertyGroup>
    <PackageId>GameKit.Admin.UI</PackageId>
    <Description>Admin UI package for GameKit — Blazor Server Razor Class Library with player management, audit log, health dashboard. Phase 3 deliverable.</Description>
    <PackageTags>gamekit;admin;blazor;dashboard;gpl</PackageTags>
    <RootNamespace>GameKit.Admin.UI</RootNamespace>
    <AssemblyName>GameKit.Admin.UI</AssemblyName>
    <!-- Required for Blazor Server RCL: link against ASP.NET Core shared framework -->
    <AddRazorSupportForMvc>true</AddRazorSupportForMvc>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\GameKit.Core\GameKit.Core.csproj" />
    <!-- Phase 2 reused: IPasswordHasher, PlayerIdentity query, ban-reason policy source -->
    <ProjectReference Include="..\GameKit.Auth\GameKit.Auth.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="MudBlazor" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="StackExchange.Redis" />  <!-- health panel probe -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

### Recommended project structure

```
src/GameKit.Admin.UI/
├── GameKit.Admin.UI.csproj          # Razor RCL SDK
├── AssemblyInfo.cs                  # InternalsVisibleTo for tests
├── GameKitAdminOptions.cs           # options tree (PanelRefreshInterval, CookieOptions, CspOptions)
├── Builder/
│   ├── AdminBuilderExtensions.cs            # AddGameKitAdmin(this IGameKitBuilder, Action<opts>)
│   └── AdminApplicationBuilderExtensions.cs # MapGameKitAdmin(path), UseGameKitAdmin()
├── Entities/
│   └── AdminUser.cs                 # id + username (citext) + password_hash + role + created_at + last_login_at
├── Data/
│   ├── AdminMigrationConstants.cs   # history table + advisory lock key (live-verified)
│   ├── AdminModelBuilderExtension.cs
│   ├── AdminMigrationModelCustomizer.cs
│   ├── AdminMigrationHostedService.cs # mirrors AuthMigrationHostedService
│   ├── AdminDesignTimeDbContextFactory.cs
│   └── Configurations/
│       └── AdminUserConfiguration.cs
├── Authentication/
│   ├── AdminAuthenticationSchemeConstants.cs # "GameKitAdmin"
│   ├── AdminCookieEvents.cs                   # OnRedirectToLogin → 404 in Production
│   └── SuperadminGateHostedService.cs         # D-04 startup assertion
├── Authorization/
│   ├── AdminPolicies.cs                       # "admin", "superadmin" policies
│   └── AdminRoles.cs                          # constants
├── Services/
│   ├── IAdminAuditWriter.cs + AdminAuditWriter.cs
│   ├── IAdminAuthService.cs + AdminAuthService.cs  # login, check-super, create-admin
│   ├── IPlayerSearchService.cs + PlayerSearchService.cs
│   ├── IPlayerBanService.cs + PlayerBanService.cs
│   ├── IHealthProbeService.cs + HealthProbeService.cs  # PG + Redis + error-ring
│   ├── ErrorRateRingBuffer.cs + LogErrorCounter.cs     # ILoggerProvider filter
│   └── IAdminUserService.cs + AdminUserService.cs      # CRUD admins (superadmin)
├── Http/
│   ├── AdminEndpoints.cs                   # minimal-API /admin/api/*
│   ├── AdminRateLimitRegistrations.cs      # gamekit:admin:login policy
│   ├── Contracts/                           # request/response DTOs
│   │   ├── BanPlayerRequest.cs
│   │   ├── UnbanPlayerRequest.cs
│   │   ├── CreateAdminRequest.cs
│   │   ├── PlayerSearchRequest.cs
│   │   └── PlayerSearchResult.cs
│   ├── EndpointFilters/
│   │   ├── AntiforgeryValidationFilter.cs
│   │   └── ValidationEndpointFilter.cs       # mirror Phase 2 generic
│   └── Validators/
│       ├── BanPlayerRequestValidator.cs      # 3–512 char reason (D-09)
│       ├── CreateAdminRequestValidator.cs
│       └── PlayerSearchRequestValidator.cs
├── Middleware/
│   ├── AdminCspNonceMiddleware.cs            # writes nonce to HttpContext.Items + CSP header
│   └── AdminNotFoundWhenUnauthorizedMiddleware.cs  # 404-not-401 for Production
├── Components/
│   ├── _Imports.razor
│   ├── App.razor                              # root — reads nonce from HttpContextAccessor
│   ├── Routes.razor                           # Router w/ AppAssembly = typeof(App).Assembly
│   ├── Layout/
│   │   ├── MainLayout.razor + .razor.css
│   │   ├── LoginLayout.razor + .razor.css
│   │   ├── TopNav.razor + .razor.css
│   │   └── SideNav.razor + .razor.css
│   ├── Pages/
│   │   ├── Login.razor
│   │   ├── Dashboard.razor
│   │   ├── PlayerSearch.razor
│   │   ├── PlayerDetail.razor
│   │   ├── Matches.razor
│   │   ├── Audit.razor
│   │   ├── Health.razor
│   │   ├── QueueDepth.razor
│   │   ├── RankAdjust.razor
│   │   └── Admins.razor
│   ├── Dialogs/
│   │   ├── BanPlayerDialog.razor
│   │   ├── UnbanPlayerDialog.razor
│   │   ├── GdprDeleteDialog.razor
│   │   ├── CreateAdminDialog.razor
│   │   └── DeleteAdminDialog.razor
│   └── Shared/
│       ├── EnvironmentChip.razor
│       ├── StatusChip.razor
│       ├── KeysetPaginator.razor
│       └── MissingPackageAlert.razor
└── wwwroot/
    ├── gamekit-admin.css              # non-isolated global shell (minimal)
    └── gamekit-admin.razor.js         # collocated JS module for polling timer if needed
```

### Packaging notes for RCL

- **SDK must be `Microsoft.NET.Sdk.Razor`** — plain `Microsoft.NET.Sdk` cannot compile `.razor` files. [VERIFIED: MS Learn class-libraries doc]
- **Static assets** (CSS/JS/images) placed under `wwwroot/` ship automatically in the `.nupkg`. Consumer apps can reference them at `_content/GameKit.Admin.UI/{path}`. [VERIFIED: MS Learn]
- **Blazor CSS isolation** — `Foo.razor.css` auto-bundles into `_content/GameKit.Admin.UI/GameKit.Admin.UI.bundle.scp.css`. Consumer `app.css` imports it automatically via `@import '_content/...'`. [VERIFIED: MS Learn]
- **Routable components** — the consumer's `Router` needs to know about our assembly. Since we own `MapGameKitAdmin("/admin")`, we emit an endpoint convention that mounts OUR OWN router/layout; the consumer does NOT need to edit their `App.razor`. Implementation: inside `MapGameKitAdmin`, call `endpoints.MapRazorComponents<App>().AddInteractiveServerRenderMode()` rooted at the supplied prefix.

### `AddGameKitAdmin` + `MapGameKitAdmin` surface

```csharp
// Conceptual — executor owns the final signature.
public static IGameKitBuilder AddGameKitAdmin(
    this IGameKitBuilder builder,
    Action<GameKitAdminOptions>? configure = null)
{
    var opts = new GameKitAdminOptions();
    configure?.Invoke(opts);
    builder.Services.AddSingleton(opts);

    // 1. Admin model-builder extension + migrations hosted service
    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IModelBuilderExtension, AdminModelBuilderExtension>());
    builder.Services.AddHostedService<AdminMigrationHostedService>();

    // 2. Superadmin startup gate — runs after AdminMigrationHostedService (order matters)
    builder.Services.AddHostedService<SuperadminGateHostedService>();

    // 3. Cookie auth scheme
    builder.Services
        .AddAuthentication()  // additive — does not clobber Phase 2 JwtBearer default
        .AddCookie(AdminAuthenticationSchemeConstants.Scheme, options =>
        {
            options.Cookie.Name = "gk_admin_session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/admin/login";
            options.LogoutPath = "/admin/logout";
            options.EventsType = typeof(AdminCookieEvents);  // injects the events class
        });
    builder.Services.AddScoped<AdminCookieEvents>();

    // 4. Authorization policies
    builder.Services.AddAuthorization(ao =>
    {
        ao.AddPolicy(AdminPolicies.Admin, p => p
            .AddAuthenticationSchemes(AdminAuthenticationSchemeConstants.Scheme)
            .RequireAuthenticatedUser()
            .RequireRole(AdminRoles.Admin, AdminRoles.Superadmin));
        ao.AddPolicy(AdminPolicies.Superadmin, p => p
            .AddAuthenticationSchemes(AdminAuthenticationSchemeConstants.Scheme)
            .RequireAuthenticatedUser()
            .RequireRole(AdminRoles.Superadmin));
    });

    // 5. Admin services
    builder.Services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
    builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
    builder.Services.AddScoped<IPlayerSearchService, PlayerSearchService>();
    builder.Services.AddScoped<IPlayerBanService, PlayerBanService>();
    builder.Services.AddScoped<IAdminUserService, AdminUserService>();
    builder.Services.AddSingleton<ErrorRateRingBuffer>();     // shared buffer
    builder.Services.AddSingleton<ILoggerProvider, LogErrorCounter>(); // feeds buffer
    builder.Services.AddScoped<IHealthProbeService, HealthProbeService>();

    // 6. Rate limiter
    builder.Services.AddAdminRateLimits(new GameKitRateLimitPolicies());

    // 7. Antiforgery (registered once; token header name defaults to X-XSRF-TOKEN)
    builder.Services.AddAntiforgery(o => o.HeaderName = "X-GameKit-Admin-CSRF");

    // 8. Blazor Server primitives
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();

    // 9. MudBlazor wiring
    builder.Services.AddMudServices();   // extension ships with MudBlazor 9.3.0

    // 10. FluentValidation validators
    builder.Services.AddScoped<IValidator<BanPlayerRequest>, BanPlayerRequestValidator>();
    builder.Services.AddScoped<IValidator<CreateAdminRequest>, CreateAdminRequestValidator>();
    builder.Services.AddScoped<IValidator<PlayerSearchRequest>, PlayerSearchRequestValidator>();

    return builder;
}

// Endpoint routing
public static IEndpointRouteBuilder MapGameKitAdmin(
    this IEndpointRouteBuilder routes,
    string prefix = "/admin")
{
    // Blazor components under the prefix
    routes.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .WithStaticAssets()
        // Mount App at the prefix — consumer sees /admin, /admin/login, /admin/players, etc.
        // The @page routes inside App/Routes.razor use absolute paths starting with prefix.
        ;

    // Minimal-API admin endpoints under /admin/api/* (used by JavaScript fetch from Blazor components)
    var group = routes.MapGroup($"{prefix}/api");
    AdminEndpoints.Map(group);

    return routes;
}
```

**Middleware pipeline contract (consumer's Program.cs):**

```
UseRouting
  → UseRateLimiter
  → UseGameKitAuth    (UseAuthentication — registers JWT + admin cookie schemes)
  → UseGameKit        (UseAuthorization)
  → UseGameKitAdmin   (AdminCspNonceMiddleware + UseAntiforgery + 404-on-unauth middleware)
  → MapGameKit + MapAuth + MapGameKitAdmin
```

Note: `UseGameKitAuth` is the existing Phase 2 method that calls `app.UseAuthentication()`. Because cookie auth registers via `AddAuthentication().AddCookie(...)`, the same `UseAuthentication` covers both schemes. `UseGameKitAdmin()` adds the CSP + antiforgery + 404-on-unauth middleware only.

---

## Authentication

### Scheme isolation (D-02 — cannot mix with player JWT)

**Scheme name:** `"GameKitAdmin"` (constant in `AdminAuthenticationSchemeConstants.Scheme`).

**How cross-scheme leakage is prevented:**

1. **Distinct scheme name** — `"GameKitAdmin"` ≠ `JwtBearerDefaults.AuthenticationScheme` (`"Bearer"`). ASP.NET Core dispatches auth to the matching handler by scheme name only.
2. **Authorization policy pins the scheme** — `RequireAuthenticatedUser()` alone isn't enough; we MUST add `.AddAuthenticationSchemes("GameKitAdmin")` so a valid Bearer token doesn't satisfy the admin policy.
3. **Default scheme remains `"Bearer"`** — `AddAuthentication()` call inside `AddGameKitAdmin` does NOT pass a `defaultScheme` argument, so the Phase 2 default (`JwtBearerDefaults.AuthenticationScheme`) is preserved. Admin endpoints opt IN to cookie auth via their authorization policy.
4. **Distinct cookie name** — `gk_admin_session` ≠ any Phase 2 cookie (Phase 2 has none — it's bearer-only).

**Integration test (ROADMAP SC #6):**
```csharp
// Arrange: valid player JWT from /auth/login/guest
var playerJwt = await LoginAsGuestAsync();

// Act: hit /admin/players with the JWT as a Bearer header
var req = new HttpRequestMessage(HttpMethod.Get, "/admin/players");
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", playerJwt);
var resp = await client.SendAsync(req);

// Assert: NOT 200, NOT 401 (admin cookie handler ignores Bearer). In Production: 404.
// In Development/Staging: 302 to /admin/login (admin handler's challenge).
// In tests we set IHostEnvironment = Production so we see 404.
Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
```

### 404 (not 401) on unauthenticated `/admin` in Production (D-04 / ADMIN-03)

**Mechanism:** Override `CookieAuthenticationEvents.OnRedirectToLogin` to return 404 when environment = Production. [CITED: MS Learn — `CookieAuthenticationHandler.HandleChallengeAsync`]

```csharp
public sealed class AdminCookieEvents : CookieAuthenticationEvents
{
    private readonly IHostEnvironment _env;
    public AdminCookieEvents(IHostEnvironment env) => _env = env;

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> ctx)
    {
        // Only applies to /admin/* paths. Allow the built-in redirect for /admin/login itself.
        if (_env.IsProduction() && !ctx.Request.Path.StartsWithSegments("/admin/login"))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        // Non-production: standard redirect to /admin/login
        return base.RedirectToLogin(ctx);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> ctx)
    {
        // Authenticated but wrong role → 403 (not 404 — we don't hide existence from legitimate admins)
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
```

**Why this is the right mechanism (not middleware):**
- Cookie auth handler's challenge flow is the canonical insertion point — intercepts the 401 response BEFORE it becomes a redirect.
- A middleware-based approach would need to inspect response status after the fact (fragile — would also hit 401s from Phase 2 Bearer endpoints).
- Pinning it to `/admin/login` being an explicit exception keeps the login page itself reachable.

### Startup fail-fast (D-04 — ADMIN-03)

**Mechanism:** `IHostedService.StartAsync` query against `admin_users`. Thrown `InvalidOperationException` kills the host before Kestrel starts serving. [VERIFIED: https://andrewlock.net/controlling-ihostedservice-execution-order-in-aspnetcore-3/ + dotnet/aspnetcore#5900]

```csharp
internal sealed class SuperadminGateHostedService : IHostedService
{
    private readonly IHostEnvironment _env;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SuperadminGateHostedService> _logger;

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        // Use ExecuteSqlRawAsync or a direct query on AdminUser set
        var hasSuperadmin = await ctx.Set<AdminUser>()
            .AsNoTracking()
            .AnyAsync(u => u.Role == AdminRoles.Superadmin, ct);

        if (hasSuperadmin) return;

        if (_env.IsProduction())
        {
            throw new InvalidOperationException(
                "GameKit.Admin.UI is mounted in Production but no superadmin exists in admin_users. " +
                "Bootstrap the first admin by running: `dotnet gamekit admin create`. " +
                "The first admin created is automatically promoted to superadmin.");
        }

        _logger.LogWarning(
            "GameKit.Admin.UI: no superadmin exists in admin_users. Bootstrap one with " +
            "`dotnet gamekit admin create`. The admin UI will render a placeholder until then.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Ordering guarantee:**
- `IHostedService` implementations run in registration order [VERIFIED: andrewlock.net/controlling-ihostedservice-execution-order]
- `AddHostedService<AdminMigrationHostedService>()` MUST precede `AddHostedService<SuperadminGateHostedService>()` — so `admin_users` table exists when the gate queries it.
- The Host awaits `StartAsync` — exceptions propagate out and terminate the host. Kestrel starts AFTER all `IHostedService.StartAsync` completes.

**Why NOT `OptionsBuilder.ValidateOnStart()`:** That validates an options INSTANCE against its own rules (e.g. "Issuer must not be empty"). The D-04 assertion is a DB query ("how many superadmin rows?") — `ValidateOnStart` has no DB access and no services. [VERIFIED: MS Learn ValidateOnStart docs]

**Why NOT `IStartupFilter`:** IStartupFilter runs during app startup but does not have scoped services available during startup filtering. Also, it's designed for middleware injection — using it to query the DB is non-idiomatic. IHostedService is the canonical pattern.

### Login flow

1. `GET /admin/login` → Blazor Server renders `Login.razor` (uses `LoginLayout.razor` — no sidebar).
2. Submit posts to `POST /admin/api/login` (minimal API endpoint, rate-limited via `gamekit:admin:login` 5/min/IP).
3. Endpoint calls `IAdminAuthService.VerifyPasswordAsync(username, password)` → loads `AdminUser` by `citext` username, runs `IPasswordHasher.Verify(...)`.
4. On success, builds `ClaimsIdentity` with `ClaimTypes.NameIdentifier = admin.Id`, `ClaimTypes.Name = admin.Username`, `ClaimTypes.Role = admin.Role`; calls `HttpContext.SignInAsync("GameKitAdmin", principal)`.
5. Writes audit row `admin.session.login.success` (or `admin.session.login.failure` on wrong password).
6. Returns 200 with redirect URL in body (Blazor submits form, reads body, navigates).

---

## UI Hardening

### Per-request CSP nonce (D-15 — ADMIN-12)

**Middleware (hand-rolled, ~30 LOC):**

```csharp
internal sealed class AdminCspNonceMiddleware
{
    public const string NonceItemKey = "gamekit.admin.csp-nonce";
    private readonly RequestDelegate _next;
    private readonly PathString _adminPrefix;  // injected from GameKitAdminOptions.Prefix

    public AdminCspNonceMiddleware(RequestDelegate next, GameKitAdminOptions opts)
    {
        _next = next;
        _adminPrefix = opts.MountPath;  // e.g. "/admin"
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments(_adminPrefix))
        {
            await _next(ctx);
            return;
        }

        // Generate 128-bit nonce, base64 encode
        Span<byte> nonceBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);
        ctx.Items[NonceItemKey] = nonce;

        // Emit CSP header before response starts
        ctx.Response.OnStarting(() =>
        {
            if (!ctx.Response.Headers.ContainsKey("Content-Security-Policy"))
            {
                ctx.Response.Headers["Content-Security-Policy"] =
                    $"default-src 'self'; " +
                    $"script-src 'self' 'nonce-{nonce}'; " +
                    $"style-src 'self' 'unsafe-inline'; " +
                    $"img-src 'self' data:; " +
                    $"font-src 'self'; " +
                    $"connect-src 'self'; " +
                    $"frame-ancestors 'none'; " +
                    $"base-uri 'self'; " +
                    $"form-action 'self'";
            }
            return Task.CompletedTask;
        });

        await _next(ctx);
    }
}
```

**Threading the nonce into `App.razor`:**

```razor
@* App.razor — root component *@
@inject IHttpContextAccessor HttpContextAccessor

<!DOCTYPE html>
<html lang="en">
<head>
    <base href="/" />
    <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
    <link rel="stylesheet" href="@Assets["_content/GameKit.Admin.UI/GameKit.Admin.UI.bundle.scp.css"]" />
    <link rel="stylesheet" href="@Assets["_content/GameKit.Admin.UI/gamekit-admin.css"]" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js" nonce="@Nonce"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js" nonce="@Nonce"></script>
</body>
</html>

@code {
    private string? Nonce =>
        HttpContextAccessor.HttpContext?.Items[AdminCspNonceMiddleware.NonceItemKey] as string;
}
```

**Why hand-rolled over NetEscapades package:**
- Zero extra dependency — principle "install only what you need".
- `~30 LOC middleware + 5 LOC App.razor read` — trivially reviewable.
- MS itself shows the same pattern using `HttpContext` → `<script nonce="...">` threading. [CITED: damienbod — revisiting CSP nonce in Blazor, 2025]

**MudBlazor CSP compatibility:**
- MudBlazor ships **no inline `<script>` tags** — all JS is in `_content/MudBlazor/MudBlazor.min.js` external file, which `script-src 'self'` allows. [VERIFIED: reviewed MudBlazor 9.3.0 package contents via nuget.org]
- MudBlazor DOES emit **inline `style` attributes** for dynamic values (e.g. Snackbar transition timings, DataGrid resize handles). This is allowed by `style-src 'self' 'unsafe-inline'` in our policy.
- MudBlazor's `MudThemeProvider` generates a `<style>` block at runtime — also covered by `'unsafe-inline'` in `style-src`.
- **Icons** — Material Symbols font ships embedded in MudBlazor's static assets under `_content/MudBlazor/` — `self` covers it.

### Antiforgery on mutations (D-16 — ADMIN-12)

**Registration:**
```csharp
services.AddAntiforgery(o =>
{
    o.HeaderName = "X-GameKit-Admin-CSRF";
    o.Cookie.Name = "gk_admin_csrf";
    o.Cookie.HttpOnly = false;   // JS needs to read it to send header
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
```

**Pipeline insertion:**
```csharp
app.UseAntiforgery();  // inserted inside UseGameKitAdmin()
```

**Endpoint filter for minimal API:**
```csharp
internal sealed class AntiforgeryValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var antiforgery = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(ctx.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "csrf_validation_failed" });
        }
        return await next(ctx);
    }
}
```

**Usage on minimal-API mutation endpoints:**
```csharp
group.MapPost("/players/{id:guid}/ban", BanPlayerHandler)
    .RequireAuthorization(AdminPolicies.Admin)
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .AddEndpointFilter<ValidationEndpointFilter<BanPlayerRequest>>();
```

**Blazor Server `EditForm` antiforgery integration:**
- Blazor Server's `EditForm` handles antiforgery automatically when `.AddAntiforgery()` is registered + `UseAntiforgery()` is in the pipeline. [VERIFIED: MS Learn Blazor auth docs, Duende Understanding Anti-Forgery]
- Interactive Server components store the token in component state so it's available across circuit lifetime.
- **Gotcha:** if a Blazor component triggers a JSON POST via `HttpClient` (not an `EditForm`), the component must read the antiforgery token from `IAntiforgery.GetAndStoreTokens(...)` and add it to the request headers manually.

**Integration test:**
```csharp
[Fact]
public async Task BanPlayer_Without_Antiforgery_Returns_400()
{
    await using var host = await AdminTestHost.StartAsync(pg, wm);
    await host.LoginAsAdminAsync();
    var resp = await host.Client.PostAsJsonAsync("/admin/api/players/{id}/ban",
        new BanPlayerRequest { Reason = "spam" });
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
}
```

### CSP + antiforgery together

- Antiforgery cookie must be `HttpOnly=false` so JS can read it; nonce-protected `<script>` reads `document.cookie['gk_admin_csrf']` + emits `X-GameKit-Admin-CSRF` header on fetch.
- OR use Blazor Server's built-in `EditForm` wiring which emits a hidden `<input name="__RequestVerificationToken">` — works without JS cookie reads because SignalR passes the state.

---

## Data Access

### `admin_users` schema (Claude's discretion within D-06 constraint)

```csharp
public sealed class AdminUser
{
    public Guid Id { get; set; }                       // UUIDv7 from IIdGenerator
    public required string Username { get; set; }      // citext, unique, 3–32
    public required string PasswordHash { get; set; }  // BCrypt output, varchar(512)
    public required string Role { get; set; }          // "admin" | "superadmin"
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    // Defense-in-depth (recommended add):
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
```

```csharp
internal sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> b)
    {
        b.ToTable("admin_users");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Username)
            .IsRequired().HasColumnType("citext").HasMaxLength(32);
        b.Property(a => a.PasswordHash).IsRequired().HasMaxLength(512);
        b.Property(a => a.Role).IsRequired().HasMaxLength(16);
        b.Property(a => a.CreatedAt).IsRequired();
        b.Property(a => a.LastLoginAt);
        b.Property(a => a.FailedLoginCount).HasDefaultValue(0);
        b.Property(a => a.LockedUntil);
        // CHECK constraint — Postgres only, via HasCheckConstraint
        b.ToTable(t => t.HasCheckConstraint(
            "ck_admin_users_role",
            "role IN ('admin','superadmin')"));
        b.HasIndex(a => a.Username).IsUnique();
    }
}
```

### Per-package migration (mirrors Phase 2 precedent — already proven)

- History table: `__ef_migrations_admin` in schema `gamekit`
- Advisory lock key: `hashtext('gamekit.admin.migrations')::bigint` — **must be live-verified via Testcontainers** (Phase 2 had to correct its pre-computed value; follow that pattern)
- Migrations assembly: `GameKit.Admin.UI`
- `AdminModelBuilderExtension : IModelBuilderExtension` applies `AdminUserConfiguration`
- `AdminMigrationModelCustomizer : IModelCustomizer` for design-time migration (excludes Core + Auth entities)
- `AdminMigrationHostedService : IHostedService` — direct copy of `AuthMigrationHostedService` shape; swaps constants
- `AdminDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>` for `dotnet ef migrations add`

**citext extension check** — `citext` is already installed by the Auth migration (plan 02-02). Admin migration DOES NOT need to re-create it — just use `HasColumnType("citext")` for `Username`.

### Keyset pagination (D-12)

**Players list** — order by `(id DESC)`:

```csharp
public async Task<PaginatedResult<PlayerRow>> SearchByDisplayNameAsync(
    string prefix, Guid? afterId, int pageSize, CancellationToken ct)
{
    var q = _ctx.Players.AsNoTracking();

    // 1. apply filter
    if (!string.IsNullOrEmpty(prefix))
        q = q.Where(p => EF.Functions.ILike(p.DisplayName, prefix + "%"));

    // 2. apply cursor (keyset)
    if (afterId is not null)
        q = q.Where(p => p.Id < afterId.Value);   // id DESC — "after" means "less than"

    // 3. sort + take pageSize + 1 (sentinel)
    var rows = await q
        .OrderByDescending(p => p.Id)
        .Take(pageSize + 1)
        .Select(p => new PlayerRow(p.Id, p.DisplayName, p.CreatedAt, p.IsBanned))
        .ToListAsync(ct);

    var hasMore = rows.Count > pageSize;
    if (hasMore) rows.RemoveAt(pageSize);

    return new PaginatedResult<PlayerRow>(
        Items: rows,
        NextCursor: hasMore ? rows[^1].Id.ToString() : null,
        HasMore: hasMore);
}
```

**Audit log** — order by `(created_at DESC, id DESC)`:

```csharp
if (afterCreatedAt is not null && afterId is not null)
{
    q = q.Where(a =>
        a.CreatedAt < afterCreatedAt ||
        (a.CreatedAt == afterCreatedAt && a.Id < afterId));
}
q = q.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(pageSize + 1);
```

**Index additions** (new migration):
```csharp
migrationBuilder.CreateIndex("ix_players_display_name_trgm",
    schema: "gamekit", table: "players", column: "display_name")
    .Annotation("Npgsql:IndexMethod", "gin")
    .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
// Already indexed by AdminAuditLogConfiguration: (CreatedAt), (TargetType, TargetId), (ActorId)
// For keyset: add composite index:
migrationBuilder.CreateIndex("ix_admin_audit_log_created_at_id_desc",
    schema: "gamekit", table: "admin_audit_log",
    columns: new[] { "created_at", "id" }, descending: new[] { true, true });
```

**pg_trgm extension required for `display_name` prefix search performance** — needs `CREATE EXTENSION IF NOT EXISTS pg_trgm` in the Admin migration. `ILIKE 'prefix%'` uses a btree-like index if the column has a trigram GIN index.

### Unified search box logic (D-11 — ADMIN-05)

```csharp
public async Task<PlayerSearchResult?> SearchAsync(string query, CancellationToken ct)
{
    query = query.Trim();
    if (string.IsNullOrEmpty(query)) return null;

    // 1. UUID detection (36-char canonical or 32-char no-dash)
    if (Guid.TryParse(query, out var id))
    {
        var player = await _ctx.Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        return player is null ? null : MapSingle(player, "id");
    }

    // 2. provider:external_id form (e.g. "steam:7656...")
    var colonIdx = query.IndexOf(':');
    if (colonIdx > 0 && colonIdx < query.Length - 1)
    {
        var provider = query[..colonIdx];
        var externalId = query[(colonIdx + 1)..];
        var identity = await _ctx.Set<PlayerIdentity>().AsNoTracking()
            .Include(i => EF.Property<Player>(i, "Player"))
            .FirstOrDefaultAsync(i => i.Provider == provider && i.ExternalId == externalId, ct);
        if (identity is not null)
            return MapFromIdentity(identity);
    }

    // 3. display_name prefix via citext — fall through to paginated search
    return await PaginatedDisplayNamePrefixAsync(query, null, 50, ct);
}
```

**Note on PlayerIdentity navigation property:** The Phase 2 code uses foreign-key-only (no navigation). The admin search service will need to join via explicit `Where(i => ...).Join(_ctx.Players, i => i.PlayerId, p => p.Id, ...)` instead of `Include`.

---

## Panels

### Health panel (D-10, ADMIN-10)

**Three probes, all server-side:**

1. **Postgres connectivity** — open a connection, run `SELECT 1` with `CommandTimeout = 2s`. Record latency.
   ```csharp
   var sw = Stopwatch.StartNew();
   await using var conn = new NpgsqlConnection(connString);
   await conn.OpenAsync(ct);
   using var cmd = new NpgsqlCommand("SELECT 1", conn);
   cmd.CommandTimeout = 2;
   await cmd.ExecuteScalarAsync(ct);
   var latencyMs = sw.Elapsed.TotalMilliseconds;
   ```

2. **Redis connectivity** — `IConnectionMultiplexer.GetStatus()` or `IConnectionMultiplexer.IsConnected` + a `PING` roundtrip.
   ```csharp
   var db = _redis.GetDatabase();
   var sw = Stopwatch.StartNew();
   var ok = await db.PingAsync();  // returns TimeSpan
   ```

3. **Recent error rate** — in-memory ring buffer populated by a custom `ILoggerProvider`:
   ```csharp
   internal sealed class LogErrorCounter : ILoggerProvider
   {
       public ILogger CreateLogger(string categoryName) => new ErrorCounterLogger(_buffer);
       // ErrorCounterLogger.Log() increments the buffer when logLevel >= Error
   }
   // ErrorRateRingBuffer: 5-minute ring, 1-second buckets, thread-safe via Interlocked.Increment
   ```

**Polling** — `System.Threading.Timer` bound to component lifecycle:
```csharp
public partial class Health : ComponentBase, IAsyncDisposable
{
    private Timer? _timer;
    protected override void OnInitialized()
    {
        _timer = new Timer(async _ => await InvokeAsync(RefreshAsync), null,
            TimeSpan.Zero,
            Options.PanelRefreshInterval);
    }
    public async ValueTask DisposeAsync()
    {
        if (_timer is not null) await _timer.DisposeAsync();
    }
}
```

**Why not `PeriodicTimer`:** Works but requires a background loop; `System.Threading.Timer` with `InvokeAsync` marshals back to the circuit's sync context cleanly.

### Match history (ADMIN-08)

Direct EF query — reuse GDPR export pattern. No new endpoint unless pagination requires one (it does — use the minimal-API endpoint). Query shape:

```csharp
var rows = await _ctx.Set<SessionParticipant>()
    .AsNoTracking()
    .Where(p => p.PlayerId == playerId && p.Session!.State == GameSessionState.Completed)
    .Include(p => p.Session)
    .OrderByDescending(p => p.Session!.EndedAt)
    .Take(pageSize + 1)
    .Select(p => new MatchRow(
        SessionId: p.SessionId,
        LadderId: p.Session!.LadderId,
        Team: p.Team,
        Result: p.Result,
        RatingBefore: p.RatingBefore,
        RatingAfter: p.RatingAfter,
        Delta: p.Delta,
        CompletedAt: p.Session.EndedAt))
    .ToListAsync(ct);
```

### Queue depth + rank adjust placeholders (ADMIN-07, ADMIN-09)

**Detection pattern:**
```csharp
[Inject] IServiceProvider Sp { get; set; } = default!;
private bool MatchmakingInstalled =>
    Sp.GetService<IMatchmakingStrategy>() is not null;
```

If `false`, render `MudAlert Severity=Info Variant=Outlined` with the copy from UI-SPEC §11/§12:
- Matchmaking: `"Install GameKit.Matchmaking and add .AddMatchmaking(…) to your service registration to enable live queue telemetry."`
- Rankings: `"Install GameKit.Rankings and add .AddRankings(…) to enable manual rank adjustments."`

---

## CLI Bootstrap

### `dotnet gamekit admin create` (D-08 — ADMIN-11)

**Command settings class:**

```csharp
internal sealed class AdminCreateCommand : AsyncCommand<AdminCreateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--username <USERNAME>")]
        [Description("Username (3–32 chars, case-insensitive).")]
        public string? Username { get; init; }

        [CommandOption("-p|--password <PASSWORD>")]
        [Description("Password (≥12 chars recommended). If omitted, prompts without echoing.")]
        public string? Password { get; init; }

        [CommandOption("-r|--role <ROLE>")]
        [Description("Role: admin or superadmin. Ignored when no admin exists (first admin auto-superadmin).")]
        [DefaultValue("admin")]
        public string Role { get; init; } = "admin";

        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string (gamekit_owner role recommended).")]
        public string? ConnectionString { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext ctx, Settings s)
    {
        var conn = s.ConnectionString
            ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION")
            ?? throw new InvalidOperationException("No connection string supplied.");

        // Interactive fill-in
        var username = s.Username ?? AnsiConsole.Ask<string>("Username:");
        var password = s.Password ?? ReadPasswordMasked();

        // Validate
        if (username.Length is < 3 or > 32) return Fail("Username must be 3–32 chars.");
        if (password.Length < 8) return Fail("Password must be at least 8 chars.");
        if (s.Role is not ("admin" or "superadmin")) return Fail("Role must be admin or superadmin.");

        // Build DI (reuse GameKit builder)
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = conn; o.AutoMigrate = false; });
        // Register minimal Admin pieces we need to touch admin_users
        services.AddSingleton<IModelBuilderExtension, AdminModelBuilderExtension>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var dbCtx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // AUTO-PROMOTE first admin to superadmin
        var zeroAdmins = !await dbCtx.Set<AdminUser>().AnyAsync();
        var effectiveRole = zeroAdmins ? "superadmin" : s.Role;

        // Check unique
        if (await dbCtx.Set<AdminUser>().AnyAsync(a => a.Username == username))
            return Fail($"Username '{username}' already exists.");

        var hash = hasher.Hash(password);
        var admin = new AdminUser
        {
            Id = ids.NewId(),
            Username = username,
            PasswordHash = hash,
            Role = effectiveRole,
            CreatedAt = clock.UtcNow,
        };
        dbCtx.Set<AdminUser>().Add(admin);
        await dbCtx.SaveChangesAsync();

        AnsiConsole.MarkupLine($"[green]OK[/] — admin created.");
        AnsiConsole.MarkupLine($"  Username: [bold]{username}[/]");
        AnsiConsole.MarkupLine($"  Role:     [bold]{effectiveRole}[/]{(zeroAdmins ? " (auto-promoted — first admin)" : "")}");
        AnsiConsole.MarkupLine($"  Hash prefix: [dim]{hash[..Math.Min(8, hash.Length)]}…[/]");
        return 0;
    }

    private static string ReadPasswordMasked()
    {
        // Console.ReadKey(intercept: true) so password never echoes
        var sb = new StringBuilder();
        Console.Write("Password: ");
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Length--; Console.Write("\b \b"); continue; }
            if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write('*'); }
        }
        return sb.ToString();
    }

    private static int Fail(string msg) { AnsiConsole.MarkupLine($"[red]ERROR:[/] {msg}"); return 2; }
}
```

**Reference for Console.ReadKey pattern:** `AdminCreateCommand` is a new command; the existing Phase 1 stub file `src/GameKit.Cli/Commands/AdminCreateCommand.cs` will be REPLACED, not amended.

**csproj change:** `src/GameKit.Cli/GameKit.Cli.csproj` needs a new `<ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />` so the CLI can reference `AdminUser` + `AdminModelBuilderExtension`. BUT this adds Blazor as a transitive dep to the CLI — alternative: **expose `AdminUser` and `AdminModelBuilderExtension` as `public` types** so only the types we need flow through (they're currently `internal sealed`). The project ref is simpler; transitive Blazor deps don't run unless Blazor is mounted.

---

## Integration with Phase 2

### Ban enforcement at login + refresh (D-03)

**Login path** — every `IOAuthProvider.CompleteLoginAsync` must check `Player.IsBanned` AFTER upsert:

**Before (SteamOAuthProvider.cs, current):**
```csharp
// ... upsert logic ...
var tokens = await _refresh.IssueRootAsync(playerId, Provider, fingerprint, ct);
return OAuthResult.Ok(playerId, tokens);
```

**After:**
```csharp
// ... upsert logic ...
// Re-fetch player to get fresh IsBanned (upsert may have created or updated the row)
var player = await _ctx.Players.AsNoTracking().FirstAsync(p => p.Id == playerId, ct);
if (player.IsBanned)
{
    // Do NOT issue tokens. Return a failure with a banned-reason-hash.
    // The hash is of the ban reason — opaque to the player, but admins can decode via audit log.
    var reasonHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(player.BanReason ?? "")))[..16].ToLowerInvariant();
    return OAuthResult.Fail($"banned:{reasonHash}");
}
var tokens = await _refresh.IssueRootAsync(playerId, Provider, fingerprint, ct);
return OAuthResult.Ok(playerId, tokens);
```

**Affected providers (all must be patched):**
- `src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs`
- `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs`
- `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs`
- `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs`

**Shared helper recommended:** extract `BannedCheckHelper.CheckAsync(_ctx, playerId, ct)` static method that returns `OAuthResult?` (null if not banned, failure result if banned) to DRY the ban check across providers.

**Endpoint-level handling** (`src/GameKit.Auth/Http/AuthEndpoints.cs`):
- Login endpoints return 403 Forbidden with problem+json body `{ "error": "banned", "reason_hash": "..." }` when `OAuthResult.ErrorCode` starts with `banned:`.
- Player sees `"This account is banned"` — the `reason_hash` is for operator cross-reference with `admin_audit_log` (where the full reason is visible).

**Refresh path** — `RefreshTokenService.RotateAsync`:

**Before (current):** rotates happily even for banned players.

**After (insert before the happy-path rotation):**
```csharp
// Happy path: rotate.
// NEW: ban check BEFORE issuing child token
var player = await _ctx.Players.AsNoTracking()
    .FirstAsync(p => p.Id == current.PlayerId, cancellationToken)
    .ConfigureAwait(false);
if (player.IsBanned)
{
    await RevokeFamilyInScope(current.FamilyId, "player_banned", current.PlayerId, cancellationToken)
        .ConfigureAwait(false);
    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    throw new UnauthorizedException("player_banned");
}
// ... existing rotation logic
```

**Audit log:** the `refresh_family_revoked` audit row with reason `"player_banned"` closes the loop — admins can trace banned-token family revocations.

**Why NOT middleware:** Phase 1 analysis (CONTEXT D-03) rejected per-request DB round-trip — load too high. Login + refresh hits are low-frequency enough that the DB query is acceptable. Existing JWT access tokens self-expire within the configured lifetime (default 15 min).

### Audit writer pattern (D-17)

`IAdminAuditWriter` mirrors existing `IAuthAuditWriter` (plan 02-04):

```csharp
public interface IAdminAuditWriter
{
    Task WriteAsync(
        string action,             // "admin.player.ban", etc.
        string targetType,          // "player", "admin", "signing_key"
        Guid? targetId,
        Guid actorId,               // admin performing action
        object? before = null,
        object? after = null,
        string? reason = null,
        CancellationToken ct = default);
}

internal sealed class AdminAuditWriter : IAdminAuditWriter
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    // Scoped lifetime — rides caller's transaction. Phase 2 pattern.

    public async Task WriteAsync(...)
    {
        _ctx.AdminAuditLog.Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = action,          // e.g. "admin.player.ban"
            TargetType = targetType,
            TargetId = targetId,
            Before = before is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(before)),
            After = after is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(after)),
            Reason = reason,
            CreatedAt = _clock.UtcNow,
        });
        await _ctx.SaveChangesAsync(ct);
    }
}
```

**Action namespaces (D-17):**
- `admin.player.ban` — target = player
- `admin.player.unban` — target = player
- `admin.player.gdpr_delete` — target = player; superadmin-only
- `admin.player.rank_adjust` — target = player; superadmin-only (Phase 4 concrete impl)
- `admin.admin.create` — target = admin
- `admin.admin.delete` — target = admin
- `admin.signing_key.rotate` — target = signing_key; superadmin-only (Phase 2 key rotation surface — deferred mechanics)
- `admin.session.login.success` / `admin.session.login.failure` — target = admin (optional; useful for security audits)

**GDPR delete reuse:** superadmin delete panel calls `IGdprDeleteService.DeletePlayerAsync(playerId, actorId: admin.Id, reason, ct)` — existing service (Phase 1) already writes a `gdpr.delete` audit row. The admin UI adds a PARALLEL `admin.player.gdpr_delete` row via `IAdminAuditWriter` so both angles are captured. Alternatively (simpler), we rename the existing audit to `admin.player.gdpr_delete` and update Phase 1 service to write that namespace. Planner decides.

---

## Validation Architecture

> Per GSD Nyquist — required because `workflow.nyquist_validation` is not explicitly false.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Testcontainers 4.11.0 + Microsoft.AspNetCore.Mvc.Testing 10.0.0 + WireMock.Net 2.2.0 |
| Config file | `tests/GameKit.Admin.Integration.Tests/xunit.runner.json` (mirror Phase 2) + shared `tests/xunit.runner.json` |
| Quick run command | `dotnet test tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj --no-build` |
| Full suite command | `dotnet test --no-build` (runs unit + integration + CLI across all projects) |

### New test projects

| Project | Purpose |
|---------|---------|
| `tests/GameKit.Admin.Tests` | Unit tests — options validation, audit writer, search input-type detection, FluentValidation rules |
| `tests/GameKit.Admin.Integration.Tests` | Integration tests — cookie auth, 404-not-401, CSP header, antiforgery enforcement, superadmin gate, keyset pagination, ban enforcement |
| `tests/GameKit.Admin.UI.Tests` (optional) | bUnit component tests for Blazor pages (consider deferring if bUnit adds complexity; MudBlazor has bUnit test harness) |

### Shared fixture extensions

Add `AdminIntegrationFixture` to `tests/GameKit.TestFixtures/`:
```csharp
public sealed class AdminIntegrationFixture
{
    public PostgresFixture Postgres { get; }
    public RedisFixture Redis { get; }
    public AdminIntegrationFixture(PostgresFixture pg, RedisFixture r) { Postgres = pg; Redis = r; }
}
// No WireMock — admin surface doesn't egress to external providers.
```

Add `AdminTestHost.cs` (mirrors `AuthTestHost.cs`) — `WebApplicationFactory`-like in-process test server with:
- Core + Auth + Admin migrations applied in order
- `AddGameKit().AddAuth(...).AddGameKitAdmin(...)` composed
- Admin cookie scheme wired
- Optionally seeds a superadmin for tests that need authenticated requests

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File |
|--------|----------|-----------|-------------------|------|
| ADMIN-01 | RCL builds + packs to NuGet | build | `dotnet pack src/GameKit.Admin.UI -c Release` | CI job `pack-admin` |
| ADMIN-02 | `MapGameKitAdmin("/custom-prefix")` mounts at custom path | integration | `dotnet test --filter FullyQualifiedName~MountPathTests` | `tests/GameKit.Admin.Integration.Tests/MountPathTests.cs` |
| ADMIN-03a | Unauth GET /admin in Production → 404 | integration | `FullyQualifiedName~ProductionReturnsNotFoundTest` | `tests/GameKit.Admin.Integration.Tests/AuthSchemeIsolationTests.cs` |
| ADMIN-03b | Unauth GET /admin in Development → 302 to /admin/login | integration | same file, `DevelopmentRedirectsToLoginTest` | same |
| ADMIN-03c | Startup throws InvalidOperationException in Production when zero superadmins | integration | `FullyQualifiedName~SuperadminGate_Production_ThrowsTest` | `tests/GameKit.Admin.Integration.Tests/SuperadminGateTests.cs` |
| ADMIN-03d | Startup logs warning (no throw) in Development when zero superadmins | integration | `SuperadminGate_Development_WarnsTest` | same |
| ADMIN-04 | Scheme name = `"GameKitAdmin"`, distinct from `"Bearer"` | unit | `FullyQualifiedName~AdminSchemeNameTest` | `tests/GameKit.Admin.Tests/AuthenticationConfigTests.cs` |
| ADMIN-04 (SC #6) | **Player JWT cannot auth into /admin/* → 404** | integration | `PlayerJwt_CannotAccessAdmin_Returns404` | `tests/GameKit.Admin.Integration.Tests/AuthSchemeIsolationTests.cs` |
| ADMIN-05 | Unified search auto-detects UUID / provider:external_id / display_name | integration | `PlayerSearch_UnifiedBox_DetectsInputType` (3 scenarios) | `tests/GameKit.Admin.Integration.Tests/PlayerSearchTests.cs` |
| ADMIN-06 | Ban writes audit row with actor/before/after/reason | integration | `BanPlayer_WritesAuditRow` | `tests/GameKit.Admin.Integration.Tests/BanFlowTests.cs` |
| ADMIN-06 | Unban is symmetrically audited | integration | `UnbanPlayer_WritesAuditRow` | same |
| ADMIN-06 | Banned player cannot login (Phase 2 ban-enforcement test) | integration | `BannedPlayer_Login_Returns403` | `tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs` |
| ADMIN-06 | Banned player refresh revokes family | integration | `BannedPlayer_Refresh_RevokesFamily` | same |
| ADMIN-07 | Rank-adjust panel shows placeholder when Rankings absent | unit (bUnit) | `RankAdjustPage_NoRankings_ShowsPlaceholder` | `tests/GameKit.Admin.UI.Tests/RankAdjustPageTests.cs` |
| ADMIN-08 | Match history EF query returns completed sessions for player | integration | `MatchHistory_ReturnsCompletedSessions` | `tests/GameKit.Admin.Integration.Tests/MatchHistoryTests.cs` |
| ADMIN-09 | Queue-depth panel shows placeholder when Matchmaking absent | unit (bUnit) | `QueueDepthPage_NoMatchmaking_ShowsPlaceholder` | `tests/GameKit.Admin.UI.Tests/QueueDepthPageTests.cs` |
| ADMIN-10 | Health panel reports OK/Down for PG + Redis | integration | `HealthProbe_Postgres_Ok` + `HealthProbe_Redis_Ok` + `HealthProbe_Postgres_Down_WhenStopped` | `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` |
| ADMIN-11 | `dotnet gamekit admin create --username x --password y` creates admin | integration | `AdminCreateCommand_CreatesAdmin` | `tests/GameKit.Cli.Tests/AdminCreateCommandTests.cs` |
| ADMIN-11 | First admin auto-promoted to superadmin | integration | `AdminCreateCommand_FirstAdmin_AutoPromoted` | same |
| ADMIN-11 | Interactive prompt fires when flag missing | unit | `ReadPasswordMasked_DoesNotEcho` | same (mocked Console) |
| ADMIN-12 | CSP header present on /admin/* response | integration | `CspHeader_PresentOnAdminResponses` | `tests/GameKit.Admin.Integration.Tests/CspTests.cs` |
| ADMIN-12 | CSP header absent on non-admin response (e.g. /auth/*) | integration | `CspHeader_AbsentOnNonAdminResponses` | same |
| ADMIN-12 | Nonce differs per request | integration | `CspNonce_UniquePerRequest` | same |
| ADMIN-12 | POST /admin/api/players/{id}/ban without CSRF → 400 | integration | `Antiforgery_MutationWithoutToken_Returns400` | `tests/GameKit.Admin.Integration.Tests/AntiforgeryTests.cs` |
| ADMIN-12 | POST with valid CSRF → 200 | integration | `Antiforgery_MutationWithToken_Succeeds` | same |
| SC #1 | E2E: mount + CLI bootstrap + login + search | integration | `RoadmapSc1_EndToEnd` | `tests/GameKit.Admin.Integration.Tests/RoadmapScenarioTests.cs` |
| SC #5 | E2E: CSP + CSRF present | integration | `RoadmapSc5_CspAndCsrf` | same |
| SC #6 | E2E: player JWT rejected | integration | `RoadmapSc6_PlayerJwtRejected` | same |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` (unit only — fast, < 30s)
- **Per wave merge:** `dotnet test` (full suite — unit + integration + CLI; Testcontainers spin up fresh Postgres + Redis per collection, ~2-5 min)
- **Phase gate:** Full suite green + `dotnet pack src/GameKit.Admin.UI -c Release` produces valid `.nupkg`

### Wave 0 Gaps

- [ ] `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` — unit project (new)
- [ ] `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` — integration project (new)
- [ ] `tests/GameKit.Admin.UI.Tests/GameKit.Admin.UI.Tests.csproj` — bUnit project (OPTIONAL — planner may defer)
- [ ] `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs` — new composite fixture
- [ ] `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` — in-process host (mirrors `AuthTestHost.cs`)
- [ ] `tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs` — xUnit collection `"Admin"` binding fixtures
- [ ] bUnit package pin in `Directory.Packages.props` — `bunit` ≥ 1.30 if component tests adopted

---

## Landmines & Pitfalls

### 1. Middleware ordering — strict, same as Phase 2

**Wrong:** `UseGameKitAdmin` before `UseGameKitAuth` → CSP middleware runs before authentication, so `AuthenticationStateProvider` in Blazor components is empty.

**Right:**
```
UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → UseGameKitAdmin → MapGameKit + MapAuth + MapGameKitAdmin
```

`UseGameKitAdmin` (new) inserts: `AdminCspNonceMiddleware` + `UseAntiforgery()` + `AdminNotFoundWhenUnauthorizedMiddleware` (wraps Production 404 guard for non-admin-login paths).

### 2. Scheme forwarding leaks — do NOT set `DefaultChallengeScheme`

**Wrong:**
```csharp
services.AddAuthentication("GameKitAdmin")  // changes default — Bearer endpoints 302 to /admin/login!
    .AddCookie("GameKitAdmin", ...);
```

**Right:**
```csharp
services.AddAuthentication()  // no default — Bearer default from Phase 2 preserved
    .AddCookie("GameKitAdmin", ...);
// Authorization policies explicitly opt into the scheme:
options.AddPolicy("admin", p => p
    .AddAuthenticationSchemes("GameKitAdmin")  // <-- explicit
    .RequireRole(...));
```

`AddAuthentication()` without an argument is additive; it doesn't reset the default scheme set by Phase 2's `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`.

### 3. IHostedService start order matters

`SuperadminGateHostedService` MUST be registered AFTER `AdminMigrationHostedService` so `admin_users` exists when the gate runs. IHostedServices run in registration order [VERIFIED: andrewlock.net].

### 4. `ExecuteUpdate` / `ExecuteDelete` bypass the change tracker — audit rows must be added BEFORE the mutation

Phase 2 `GdprDeleteService` already deals with this: `.Add(auditRow) + SaveChangesAsync()` is called BEFORE `.ExecuteDeleteAsync()`. Mirror this pattern in `PlayerBanService`.

### 5. Blazor Server render mode + CSP nonce — the nonce must be threaded into the `blazor.web.js` script tag

If the nonce is missing from `<script src="_framework/blazor.web.js" nonce="@Nonce">`, the Blazor Server circuit fails to establish → every page is blank. Cross-check: an integration test should probe `/admin/login` and assert the returned HTML contains `nonce="..."` on the `blazor.web.js` tag (string-match).

### 6. citext + EF Core `.ToLower()` — do NOT lower-case in C#

`EF.Functions.ILike(player.DisplayName, prefix + "%")` runs Postgres `ILIKE` — case-insensitive without transforming the C# string. The `citext` column handles uniqueness case-insensitively at the DB level (for `admin_users.Username`); for `players.display_name` (which is NOT citext — existing schema), use `ILike` or add a trigram GIN index.

### 7. MudBlazor's `MudThemeProvider` and `MudSnackbarProvider` must be rendered at the layout root

**Wrong:** theme provider inside a sub-component renders per-page.
**Right:**
```razor
@* MainLayout.razor — render once *@
<MudThemeProvider Theme="@GameKitAdminTheme.Default" />
<MudDialogProvider />
<MudSnackbarProvider />
@Body
```

### 8. `Console.ReadKey(intercept: true)` doesn't work in CI/non-TTY — flag-only path must work

If `Console.IsInputRedirected` is true (pipe, non-TTY), fall back to a clear error: `"Password prompt requires an interactive terminal. Pass --password via flag or environment variable."`

### 9. Ban-enforcement re-fetch is a second DB round-trip per login

Acceptable: login is low-frequency. The alternative (upsert + check inline) requires a JOIN against players within the provider's existing queries. The shared `BannedCheckHelper` makes the extra round-trip explicit and testable.

### 10. `SignInAsync` inside Blazor Server requires a `HttpContext` — must be done at endpoint level, not inside a Blazor component

**Wrong:** `HttpContext.SignInAsync` called from a Razor component's `@code` — the response has already started.
**Right:** the login Blazor component POSTs to a minimal-API endpoint which calls `SignInAsync` → redirect → Blazor reloads with the cookie.

### 11. `admin_audit_log` schema does NOT have `Username` or `Role` columns — JSON before/after must capture them

The existing entity (Phase 1) has `ActorId` (Guid) but not `ActorUsername`. To make the audit log human-readable for admins, the `after_json` must include the admin's username. The admin UI list query JOINs `admin_audit_log.actor_id` → `admin_users.username` on read. If an admin is later deleted, their actor_id still points to the audit row but the JOIN returns null (FK is NOT declared at schema level by Phase 1's configuration — it references Players, but ActorId for admin actions is an AdminUser id, which is in a different table). **Schema note:** actor_id is un-FK'd — plan accordingly.

### 12. Antiforgery cookie + HttpOnly — cannot be HttpOnly if JS reads it

`services.AddAntiforgery(o => o.Cookie.HttpOnly = false);` is correct for the JS-reads-cookie-then-sends-header pattern. Blazor Server's built-in `EditForm` doesn't need this, but any `HttpClient.PostAsync` from JS does.

### 13. MudBlazor static assets path — consumer apps must call `MapRazorComponents<App>().WithStaticAssets()` for `_content/MudBlazor/*` to resolve

The `MapGameKitAdmin` implementation calls this internally. Consumer does not need to call `UseStaticFiles()` — `WithStaticAssets()` handles static web assets (.NET 9+ pattern).

### 14. RCL and cshtml vs razor extensions

`.razor` files compile into the RCL assembly; `.cshtml` (MVC Razor Pages) do not — we're not shipping Razor Pages. Stick to `.razor` everywhere.

### 15. `dotnet gamekit admin create` referencing `GameKit.Admin.UI` drags Blazor into the CLI

**Tradeoff:** either (a) project-ref + accept ~1.8MB MudBlazor in the CLI `.nupkg` (only if shipped as an installed tool; running `dotnet run --project src/GameKit.Cli` on dev machine doesn't care), or (b) define `AdminUser` + `AdminModelBuilderExtension` in a shared `GameKit.Admin.Abstractions` project that both CLI and Admin.UI depend on. Recommendation: **(a) — project-ref is simpler**; the CLI is distributed as a global tool, not a consumer dep, so the transitive Blazor bloat doesn't propagate to end users.

### 16. Blazor `<FocusOnNavigate>` component requires `RouteView` not `Route` — verify .NET 10 router shape

Not strictly a pitfall — just a reminder that the routing primitives changed shape in .NET 8+ (Blazor Web App). The RCL `App.razor` + `Routes.razor` pattern used in .NET 10 is the current idiom; check MS docs when writing the final shape.

### 17. MudBlazor DataGrid with `Virtualize=false` does NOT use built-in pagination — keyset is custom

UI-SPEC §Component Inventory calls this out: do NOT use `MudPagination`. Hand-wire a "Load more" button that appends the next page to an `ObservableCollection`.

### 18. CSP `form-action 'self'` blocks cross-origin form submits

Harmless for admin UI (no cross-origin forms). But if a future OAuth-linked admin flow exists, `form-action` must be relaxed.

---

## Open Questions (RESOLVED)

1. **RESOLVED: bUnit for component tests — adopt or defer?**
   - What we know: bUnit has MudBlazor test helpers and ships for .NET 10. Integration tests cover E2E behavior; bUnit adds unit-level component rendering assertions.
   - What's unclear: worth the test-project complexity if integration tests prove behavior?
   - Recommendation: **defer bUnit** to a follow-up plan. Integration tests + one smoke E2E test per page (via `WebApplicationFactory`) give ROADMAP SC coverage without the extra test harness. Add bUnit in a v2 phase if maintainers want faster component-level regression detection.

2. **RESOLVED: Avatar images from Steam/Discord — defer or handle in Phase 3?**
   - UI-SPEC flagged: v1 default is "no avatar images" to keep CSP `img-src 'self' data:` minimal.
   - Plans should NOT add avatar rendering in Phase 3. Track as a follow-up TODO.

3. **RESOLVED: Dashboard card contents (FLAG 4 from UI-SPEC) — expose as endpoints or let Blazor query `GameKitDbContext` directly?**
   - What we know: Blazor Server has direct DB access via the scoped `GameKitDbContext`. An endpoint adds HTTP overhead.
   - Recommendation: **direct DbContext access** for dashboard cards (they're panels, not cross-origin API surface). Minimal-API endpoints only when the component needs pagination state persisted across navigation or JS fetch.

4. **RESOLVED: Admin session cookie lifetime default + remember-me** — planner discretion per CONTEXT.
   - Recommendation: 8h sliding (default); remember-me extends to 30 days. Store the extension flag in the cookie auth properties.

5. **RESOLVED: Signing-key rotation audit (`admin.signing_key.rotate`)** — Phase 2 doesn't ship a rotate flow; Phase 3 declares the audit namespace.
   - Recommendation: **declare the namespace constant in AdminAuditActions.cs but don't build the rotate flow**. Phase 2 has no rotate endpoint; wiring it up is a future phase. The constant exists so tests don't break when a customer rotates keys out-of-band.

6. **RESOLVED: Health panel error-rate ring-buffer window** — 5-minute default suggested. Acceptable?
   - Recommendation: 5-minute rolling window, 1-second buckets. Configurable via `GameKitAdminOptions.HealthErrorRateWindow` + `HealthErrorRateBucketSize`.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET SDK 10.0.106 | All builds | ✓ | 10.0.106 (pinned via global.json) | — |
| Postgres (for Testcontainers) | Integration tests | ✓ | 17.9 image | — |
| Redis (for Testcontainers) | Health panel tests | ✓ | per Testcontainers.Redis default | — |
| Docker daemon | Testcontainers | ✓ (per OPS-08 — no skip-if-no-docker fallback) | — | — |
| MudBlazor 9.3.0 (NuGet) | UI | ✓ (net10.0 TFM confirmed 2026-04-08) | 9.3.0 | Fall back to 8.15 with net9.0 TFM if restore fails (unlikely) |

**Missing dependencies with no fallback:** None — all deps verified GA on net10.0.
**Missing dependencies with fallback:** None.

---

## Project Constraints (from CLAUDE.md)

Directly applicable to Phase 3:

- **GPL license** — every source file needs `// SPDX-License-Identifier: GPL-3.0-or-later` header + `// Copyright (c) 2026 GameKit contributors`
- **Self-hosted only, no cloud dependencies** — Admin UI includes NO telemetry, NO phone-home. No OTel hard dep.
- **.NET 10 + ASP.NET Core 10** — MudBlazor 9.3.0 verified net10.0 TFM
- **EF Core 10.0.6 + Npgsql 10.0.1** — already pinned
- **Postgres only** — `admin_users` uses citext (already enabled by Phase 2 migration)
- **XML doc comments on every public API** — CS1591 enforced as error
- **Per-package migrations** — `__ef_migrations_admin`, distinct advisory-lock key (live-verify per 02-02 precedent)
- **Never store raw refresh tokens** — Phase 3 doesn't issue refresh tokens; admin session is cookie-based
- **Metadata JSONB columns sparse + non-relational** — not applicable (admin_users has none)
- **FluentValidation for DTO validation** — already pattern; mirror
- **No MediatR / AutoMapper** — plain services + hand-written mapping
- **MinVer coordinated release train** — all 6 packages stamp the same version; no action required in Phase 3
- **`/admin` package ships as own NuGet** — `<PackageId>GameKit.Admin.UI</PackageId>` already set

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | BCrypt.Net-Next password hashing (reuse Phase 2 `IPasswordHasher`); cookie + `SecureFlag` + `HttpOnly` + `SameSite=Lax` |
| V3 Session Management | yes | DataProtection-signed cookie; 8h sliding; `/admin/logout` clears cookie; separate scheme from player JWT |
| V4 Access Control | yes | Role-based (`admin` + `superadmin`); authorization policies pin scheme |
| V5 Input Validation | yes | FluentValidation 12 on every admin DTO; ban reason 3–512 chars enforced server-side + client-side |
| V6 Cryptography | yes | BCrypt work-factor 12 (library default); 128-bit CSP nonce from `RandomNumberGenerator.Fill`; never roll own crypto |
| V10 Malicious Code | partial | No third-party scripts (CSP `script-src 'self' 'nonce-...'` blocks external/eval); MudBlazor bundled locally |
| V11 Business Logic | yes | Rate limiting on `/admin/login` (5/min/IP); ban reason mandatory; superadmin-only for irreversible ops |
| V13 API + Web Service | yes | Antiforgery on all POST/DELETE/PATCH; CSRF cookie SameSite=Lax |

### Known Threat Patterns for Blazor Server Admin UI

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Credential stuffing | Spoofing | Rate-limit `/admin/login` 5/min/IP (D-18) + BCrypt work-factor 12 + timing-safe verify |
| CSRF on mutation | Tampering | `IAntiforgery.ValidateRequestAsync` endpoint filter (D-16) |
| XSS via MudBlazor component | Information Disclosure | Strict CSP `script-src 'self' 'nonce-...'` (D-15) — blocks inline scripts |
| Clickjacking | Tampering | CSP `frame-ancestors 'none'` (D-15) |
| Session fixation | Spoofing | `HttpContext.SignInAsync` regenerates the cookie after login |
| Privilege escalation (admin → superadmin) | Elevation of Privilege | Authorization policy pin (`RequireRole("superadmin")`); cannot be bypassed client-side |
| Player JWT reaching admin | Elevation of Privilege | Policy pins scheme `"GameKitAdmin"`; JWT handler returns "no handler matched" → redirected by cookie challenge → 404 in Production (D-04) |
| Unauthenticated admin enumeration | Information Disclosure | 404 (not 401) in Production on `/admin` (D-04/ADMIN-03); reveals no structure to anon probes |
| SQL injection via search box | Tampering | EF Core parameterized queries (`EF.Functions.ILike(col, prefix + "%")` — prefix is parameterized); no string concat |
| Weak admin password | Spoofing | Password policy documented ("≥12 chars recommended"); not enforced in v1 (opinion ≠ security control — document as known gap) |
| Timing attack on admin username lookup | Information Disclosure | Use `IPasswordHasher.Verify(pw, dummyHash)` on user-not-found to equalize wall-clock time (mirror Phase 2 `PasswordOAuthProvider.DummyHash` pattern — `T-02-16`) |
| Audit log tampering | Repudiation | Audit log writes inside same transaction as mutation; no admin API to modify/delete audit rows |
| Forgotten admin cookie after logout | Spoofing | `HttpContext.SignOutAsync("GameKitAdmin")` clears server-signed cookie; session cannot be reused |

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `_Host.razor` + `<Component>` | `App.razor` + `<Routes />` + `MapRazorComponents<App>()` | .NET 8 (Blazor Web App model) | Phase 3 uses `App.razor` + `Routes.razor` pattern |
| `UseEndpoints(e => e.MapBlazorHub())` | `endpoints.MapRazorComponents<App>().AddInteractiveServerRenderMode()` | .NET 8 | Modern endpoint-routing for Blazor Server |
| Cookie auth redirect 302 for API endpoints | Direct 401/403 for "known API endpoints" in .NET 10 | .NET 10 | Our endpoints register as Blazor routes, so cookie redirect still fires — we override via `CookieAuthenticationEvents.OnRedirectToLogin` for 404 behavior |
| FluentValidation.AspNetCore auto-validation | Explicit `IValidator<T>` injection + endpoint filter | FV 11+ | Already adopted in Phase 2 |
| `AddAntiforgery` on by default for Razor Pages | Must be explicit for Blazor Server + minimal API | .NET 8+ | Phase 3 calls `AddAntiforgery` explicitly |

**Deprecated/outdated:**
- `FluentValidation.AspNetCore` NuGet package (archived; DO NOT add)
- `AspNet.Security.OpenId.Steam` (Phase 2 rejected contrib; no impact on Phase 3)
- `IdentityServer4` (archived)
- MediatR (RPL-licensed v13+)

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | BCrypt work-factor 12 is the `BCrypt.Net-Next` default and provides adequate protection for admin creds | Security Domain | LOW — matches Phase 2 baseline; can bump to 14 as config |
| A2 | Blazor Server's `EditForm` handles antiforgery automatically when `.AddAntiforgery()` is registered | UI Hardening | LOW — MS docs confirm; if wrong, add explicit `AntiForgeryToken` component |
| A3 | Existing `admin_audit_log.ActorId` is NOT FK-constrained at DB level (only indexed) | Landmines | LOW — planner must verify via schema introspection; existing Phase 1 config shows no `HasOne<Player>()` FK |
| A4 | `System.Threading.Timer` inside Blazor Server component is the right polling mechanism (UI-SPEC §D-10) | Panels | LOW — decision locked in CONTEXT D-10 |
| A5 | `IHostedService.StartAsync` throwing kills the host before Kestrel opens listening sockets | Authentication | LOW — verified via dotnet/aspnetcore#5900 + andrewlock.net |
| A6 | MudBlazor 9.3.0 emits no inline `<script>` tags | UI Hardening | MEDIUM — researcher reviewed package docs + UI-SPEC asserts this; executor must confirm by inspecting shipped `.nupkg` during plan 03-07. If wrong, extend CSP with hash-based allow or use nonce threading to inline scripts |
| A7 | `citext` extension is already enabled by Phase 2 Auth migration — Admin migration does NOT need to re-create it | Data Access | LOW — verified by reading `20260418000000_AuthInitial.cs` |
| A8 | The Advisory-lock key `hashtext('gamekit.admin.migrations')::bigint` differs from Core (`1800940027`) and Auth (`-298890956`) | Data Access | LOW — Postgres `hashtext` is deterministic + different input strings produce different outputs. Must live-verify per 02-02 precedent |
| A9 | RCL `Microsoft.NET.Sdk.Razor` SDK supports Blazor Server components (not just Razor Pages) | Package Composition | LOW — verified via MS Learn class-libraries docs |
| A10 | Consumer's `MapRazorComponents<App>().WithStaticAssets()` call inside `MapGameKitAdmin` exposes MudBlazor static assets at `_content/MudBlazor/*` | Package Composition | LOW — .NET 9+ static-web-asset pipeline handles this |
| A11 | The `admin_audit_log` table structure (from Phase 1) is sufficient for Phase 3 actions — no schema changes needed | Integration with Phase 2 | LOW — the existing shape (ActorId, Action, TargetType, TargetId, Before, After, Reason, CreatedAt) matches D-17 exactly |
| A12 | Phase 2 `RefreshTokenService.RotateAsync` can be patched to add ban check without altering its `IRefreshTokenService` contract | Integration with Phase 2 | LOW — internal logic change inside the Scoped service |
| A13 | `BannedCheckHelper.CheckAsync` returning `OAuthResult?` is an internal utility — not a public API surface | Integration with Phase 2 | LOW — pattern matches Phase 2 internal helpers |
| A14 | `form-action 'self'` in CSP does NOT break MudBlazor forms or antiforgery cookie flow | UI Hardening | LOW — same-origin form submit is the ONLY flow in admin UI |
| A15 | Admin cookie name `gk_admin_session` does not collide with any existing consumer-app cookie | Authentication | LOW — customer can override via `GameKitAdminOptions.Cookie.Name` |

---

## Sources

### Primary (HIGH confidence)

- [NuGet: MudBlazor 9.3.0](https://www.nuget.org/packages/MudBlazor/9.3.0) — HIGH (version + TFMs verified 2026-04-18)
- [MS Learn: Enforce a CSP for ASP.NET Core Blazor (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/content-security-policy?view=aspnetcore-10.0) — HIGH (authoritative CSP guidance)
- [MS Learn: Consume Razor components from a Razor class library (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-10.0) — HIGH (RCL packaging)
- [MS Learn: Use cookie authentication without ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) — HIGH
- [MS Learn: Cookie login redirects are disabled for known API endpoints (.NET 10 breaking change)](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/cookie-authentication-api-endpoints) — HIGH
- [MS Learn: ValidateOnStart<TOptions> method](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.optionsbuilderextensions.validateonstart) — HIGH
- [MS Learn: Prevent CSRF attacks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-9.0) — HIGH
- [MS Learn: CookieAuthenticationHandler.HandleChallengeAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationhandler.handlechallengeasync) — HIGH
- [Existing codebase — `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` + `AuthBuilderExtensions.cs`](file:///home/noah/Desktop/projects/gamekit/src/GameKit.Auth) — HIGH (direct pattern source)
- [Existing codebase — `src/GameKit.Core/Entities/AdminAuditLog.cs` + `Data/Configurations/AdminAuditLogConfiguration.cs`](file:///home/noah/Desktop/projects/gamekit/src/GameKit.Core) — HIGH (target entity already migrated)

### Secondary (MEDIUM confidence)

- [damienbod: Revisiting CSP nonce in Blazor (2025-05)](https://damienbod.com/2025/05/26/revisiting-using-a-content-security-policy-csp-nonce-in-blazor/) — MEDIUM (pattern inspiration; recommends NetEscapades package which we decline)
- [Andrew Lock: Controlling IHostedService execution order](https://andrewlock.net/controlling-ihostedservice-execution-order-in-aspnetcore-3/) — MEDIUM (order guarantee for SuperadminGate after AdminMigration)
- [Duende: Understanding Anti-Forgery in ASP.NET Core](https://duendesoftware.com/blog/20250325-understanding-antiforgery-in-aspnetcore) — MEDIUM (validates pattern)
- [Milan Jovanović: Keyset pagination with EF Core](https://www.milanjovanovic.tech/blog/cursor-pagination-vs-offset-pagination-in-ef-core) (or equivalent) — MEDIUM
- [GitHub: dotnet/aspnetcore#5900 — "Don't swallow IHostedService.StartAsync exceptions"](https://github.com/dotnet/aspnetcore/issues/5900) — MEDIUM (confirms startup fail-fast semantics)

### Tertiary (LOW confidence)

- [GitHub: IvanJosipovic/BlazorAntiForgery](https://github.com/IvanJosipovic/BlazorAntiForgery) — LOW (alternative pattern; not adopted)

---

## Metadata

**Confidence breakdown:**

- Standard stack: **HIGH** — MudBlazor 9.3.0 `net10.0` TFM confirmed on nuget.org; all other deps already pinned repo-wide from Phases 1–2
- Architecture (scheme isolation, 404-not-401, startup gate, per-package migrations): **HIGH** — patterns match Phase 2 precedent; MS docs authoritative on cookie auth events
- CSP + antiforgery: **HIGH** — MS docs + damienbod pattern; hand-rolled middleware is minimal + transparent
- Phase 2 ban enforcement patches: **HIGH** — direct code inspection confirms the change points
- Blazor Server RCL packaging: **MEDIUM-HIGH** — MS docs cover the RCL shape; `MapRazorComponents<App>()` rooted at a prefix is the pattern but the research researcher did not produce a working prototype
- Keyset pagination: **HIGH** — well-documented EF Core pattern; no external lib needed
- Pitfalls: **MEDIUM** — most are verified Phase 2 issues; some are Blazor-specific ordering/initialization hazards

**Research date:** 2026-04-18
**Valid until:** 2026-05-18 (30 days — stack is stable; MudBlazor upgrade cadence is the primary watch item)

---

## RESEARCH COMPLETE
