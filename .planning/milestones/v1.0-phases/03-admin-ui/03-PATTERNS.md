---
phase: 03
phase_name: admin-ui
mapped: 2026-04-18
analog_source: src/GameKit.Auth (primary), src/GameKit.Core (secondary), src/GameKit.Cli (CLI), tests/GameKit.* (tests)
---

# Phase 3: Admin UI — Pattern Map

**Mapped:** 2026-04-18
**Files analyzed:** ~50 new (GameKit.Admin.UI package + tests + patches)
**Analogs found:** 48 / 50 exact-or-role match; 2 Blazor-UI files have no direct analog (no prior Razor work in repo)

The single most important context here is that **`src/GameKit.Auth` is a nearly 1:1 analog for everything Admin.UI does server-side** (options tree, fluent builder, migration hosted service, per-package migration shape, audit writer, endpoint filters, validator registration, rate-limit registration). Planner should prefer pointing at concrete Phase-2 files over re-describing patterns in prose.

---

## File Classification

### New — Package skeleton & DI

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` (rewrite) | csproj | n/a | `src/GameKit.Auth/GameKit.Auth.csproj` | exact (csproj shape; add `Microsoft.NET.Sdk.Razor`, MudBlazor, FrameworkReference) |
| `src/GameKit.Admin.UI/AssemblyInfo.cs` (rewrite) | assembly-info | n/a | `src/GameKit.Auth/AssemblyInfo.cs` | exact (InternalsVisibleTo shape) |
| `src/GameKit.Admin.UI/GameKitAdminOptions.cs` | options | n/a | `src/GameKit.Auth/GameKitAuthOptions.cs` | exact (nested option group tree) |
| `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` | fluent builder | n/a | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` | exact (`AddAuth` on `IGameKitBuilder`) |
| `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` | fluent builder | request-response | `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs` | exact (`UseGameKitAuth` + `MapAuth`) |

### New — Data layer (entity + migration + hosted services)

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Entities/AdminUser.cs` | entity | CRUD | `src/GameKit.Auth/Entities/PlayerCredential.cs` | exact (citext username + BCrypt hash + FK shape) |
| `src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs` | EF config | CRUD | `src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs` | exact (citext column + HasCheckConstraint pattern) |
| `src/GameKit.Admin.UI/Data/AdminModelBuilderExtension.cs` | model-builder | n/a | `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs` | exact (same class shape) |
| `src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs` | constants | n/a | `src/GameKit.Auth/Data/AuthMigrationConstants.cs` | exact (history-table + advisory-lock key) |
| `src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs` | hosted service | CRUD | `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` | exact (IHostedService applying package migrations) |
| `src/GameKit.Admin.UI/Data/AdminDesignTimeDbContextFactory.cs` | design-time factory | n/a | `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` | exact (both factory + migration-customizer patterns) |
| `src/GameKit.Admin.UI/Data/AdminMigrationModelCustomizer.cs` (in same file as factory, per Auth precedent) | model customizer | n/a | `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` (contains `AuthMigrationModelCustomizer`) | exact (same pattern — ExcludeFromMigrations on sibling entities) |
| `src/GameKit.Admin.UI/Migrations/YYYYMMDDHHMMSS_AdminInitial.cs` | migration | n/a | `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs` | exact (per-package migration boundary) |
| `src/GameKit.Admin.UI/Migrations/GameKitDbContextModelSnapshot.cs` | snapshot | n/a | `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs` | exact (each package ships its own snapshot) |

### New — Authentication / authorization / startup

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Authentication/AdminAuthenticationSchemeConstants.cs` | constants | n/a | `src/GameKit.Auth/Providers/Steam/SteamConstants.cs` | role-match (string constants) |
| `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs` | auth events | request-response | — (no cookie auth in repo; Phase 2 uses `AddJwtBearer` directly — see `AuthBuilderExtensions.AddAuth` lines 176–193) | partial (use JwtBearer options-wiring pattern as structural analog) |
| `src/GameKit.Admin.UI/Authentication/SuperadminGateHostedService.cs` | hosted service | request-response | `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` | role-match (IHostedService + scoped DbContext + env-conditional throw) |
| `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` | constants | n/a | `src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs` | role-match (named policy strings) |
| `src/GameKit.Admin.UI/Authorization/AdminRoles.cs` | constants | n/a | `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs` | role-match |

### New — Services (audit, auth, search, ban, health, admins)

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Services/IAdminAuditWriter.cs` | interface | CRUD | `src/GameKit.Auth/Services/IAuthAuditWriter.cs` | **exact** (mirror verbatim) |
| `src/GameKit.Admin.UI/Services/AdminAuditWriter.cs` | service | CRUD | `src/GameKit.Auth/Services/AuthAuditWriter.cs` | **exact** (same ctor, same shape, extends with `before` field support) |
| `src/GameKit.Admin.UI/Services/IAdminAuthService.cs` + `AdminAuthService.cs` | service | CRUD | `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` (credential lookup + BCrypt verify + audit write) | role-match |
| `src/GameKit.Admin.UI/Services/IPlayerSearchService.cs` + `PlayerSearchService.cs` | service | CRUD (keyset) | `src/GameKit.Core/Http/PlayerEndpoints.cs` (AsNoTracking + OrderBy + Take) | role-match |
| `src/GameKit.Admin.UI/Services/IPlayerBanService.cs` + `PlayerBanService.cs` | service | CRUD | `src/GameKit.Core/Services/GdprDeleteService.cs` (SERIALIZABLE tx + snapshot-before + audit-write pattern) | role-match (ban = less-destructive tx) |
| `src/GameKit.Admin.UI/Services/IAdminUserService.cs` + `AdminUserService.cs` | service | CRUD | `src/GameKit.Auth/Services/GuestUpgradeService.cs` (create + unique check + audit) | role-match |
| `src/GameKit.Admin.UI/Services/IHealthProbeService.cs` + `HealthProbeService.cs` | service | request-response | — (no health probe exists) | none |
| `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` + `LogErrorCounter.cs` | utility + ILoggerProvider | event-driven | — (no custom ILoggerProvider in repo) | none |

### New — HTTP / endpoints / rate limits / validators / filters

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` | endpoint group | request-response | `src/GameKit.Auth/Http/AuthEndpoints.cs` | exact (static class + `MapGroup` + `.AddEndpointFilter<...>()` chains) |
| `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` | config | request-response | `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs` | **exact** (swap FixedWindow → SlidingWindow; IP-only partition) |
| `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` | endpoint filter | request-response | `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs` | role-match (same `IEndpointFilter` generic shape) |
| `src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs` | endpoint filter | request-response | `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs` | **exact** (copy verbatim) |
| `src/GameKit.Admin.UI/Http/Contracts/BanPlayerRequest.cs` | DTO | request-response | `src/GameKit.Auth/Http/Contracts/LoginRequest.cs` | exact (positional record) |
| `src/GameKit.Admin.UI/Http/Contracts/UnbanPlayerRequest.cs` | DTO | request-response | `src/GameKit.Auth/Http/Contracts/LoginRequest.cs` | exact |
| `src/GameKit.Admin.UI/Http/Contracts/CreateAdminRequest.cs` | DTO | request-response | `src/GameKit.Auth/Http/Contracts/RegisterRequest.cs` | exact |
| `src/GameKit.Admin.UI/Http/Contracts/PlayerSearchRequest.cs` + `PlayerSearchResult.cs` | DTO | request-response | `src/GameKit.Auth/Http/Contracts/LoginRequest.cs` | exact |
| `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs` | validator | request-response | `src/GameKit.Auth/Http/Validators/RegisterRequestValidator.cs` | **exact** (FluentValidation + options injection) |
| `src/GameKit.Admin.UI/Http/Validators/CreateAdminRequestValidator.cs` | validator | request-response | `src/GameKit.Auth/Http/Validators/RegisterRequestValidator.cs` | exact |
| `src/GameKit.Admin.UI/Http/Validators/PlayerSearchRequestValidator.cs` | validator | request-response | `src/GameKit.Auth/Http/Validators/LoginRequestValidator.cs` | exact |

### New — Middleware

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs` | middleware | request-response | — (no custom middleware in repo) | none (research §UI Hardening provides the pattern) |
| `src/GameKit.Admin.UI/Middleware/AdminNotFoundWhenUnauthorizedMiddleware.cs` | middleware | request-response | — (no custom middleware in repo) | none (RESEARCH.md uses `CookieAuthenticationEvents.OnRedirectToLogin` instead; see research §Authentication) |

### New — Blazor Server components, layouts, pages (no repo analog; MudBlazor)

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Components/App.razor` | root component | SSR | — | none (first Blazor work in repo; MudBlazor docs are the canonical source) |
| `src/GameKit.Admin.UI/Components/Routes.razor` | routing | SSR | — | none |
| `src/GameKit.Admin.UI/Components/_Imports.razor` | imports | n/a | — | none |
| `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor` (+ `.razor.css`) | layout | SSR | — | none (UI-SPEC §Layout Shell authoritative) |
| `src/GameKit.Admin.UI/Components/Layout/LoginLayout.razor` (+ `.razor.css`) | layout | SSR | — | none |
| `src/GameKit.Admin.UI/Components/Layout/TopNav.razor` / `SideNav.razor` | component | SSR | — | none |
| `src/GameKit.Admin.UI/Components/Pages/Login.razor` | page | request-response | — | none (UI-SPEC §1 authoritative) |
| `src/GameKit.Admin.UI/Components/Pages/Dashboard.razor` | page | SSR | — | none |
| `src/GameKit.Admin.UI/Components/Pages/PlayerSearch.razor` | page | streaming (debounced search) | — | none (UI-SPEC §4 authoritative) |
| `src/GameKit.Admin.UI/Components/Pages/PlayerDetail.razor` | page | CRUD | — | none |
| `src/GameKit.Admin.UI/Components/Pages/Audit.razor` | page | CRUD (keyset) | — | none |
| `src/GameKit.Admin.UI/Components/Pages/Matches.razor` | page | CRUD | — | none |
| `src/GameKit.Admin.UI/Components/Pages/Health.razor` | page | polling | — | none (RESEARCH §Health panel timer pattern authoritative) |
| `src/GameKit.Admin.UI/Components/Pages/QueueDepth.razor` | page | polling | — | none |
| `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor` | page | CRUD | — | none |
| `src/GameKit.Admin.UI/Components/Pages/Admins.razor` | page | CRUD | — | none |
| `src/GameKit.Admin.UI/Components/Dialogs/BanPlayerDialog.razor` | dialog | CRUD | — | none (UI-SPEC §6 authoritative) |
| `src/GameKit.Admin.UI/Components/Dialogs/UnbanPlayerDialog.razor` | dialog | CRUD | — | none |
| `src/GameKit.Admin.UI/Components/Dialogs/GdprDeleteDialog.razor` | dialog | CRUD | — | none (UI-SPEC §8 — two-step confirmation) |
| `src/GameKit.Admin.UI/Components/Dialogs/CreateAdminDialog.razor` + `DeleteAdminDialog.razor` | dialog | CRUD | — | none |
| `src/GameKit.Admin.UI/Components/Shared/*` (chips, paginator, placeholder alert) | component | SSR | — | none |
| `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css` | asset | n/a | — | none |

### New — CLI

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Cli/Commands/Admin/AdminCreateCommand.cs` (replaces stub) | CLI command | CRUD | `src/GameKit.Cli/Commands/MigrateCommand.cs` | **exact** (Settings class + ServiceCollection + scoped DbContext) |

### Modified — Phase 2 patches (ban enforcement per D-03)

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `src/GameKit.Auth/Services/BannedCheckHelper.cs` (new, inside `GameKit.Auth`) | utility | CRUD | — (new helper) | role-match (static helper) |
| `src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs` | modify | CRUD | self-edit | exact (insert ban check after upsert, before `IssueRootAsync`) |
| `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | modify | CRUD | self-edit | exact |
| `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` | modify | CRUD | self-edit | exact (guest rarely banned — still apply for uniformity) |
| `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` | modify | CRUD | self-edit | exact |
| `src/GameKit.Auth/Services/RefreshTokenService.cs` | modify | CRUD | self-edit | exact (add ban check before happy-path rotation; revoke family if banned) |

### Modified — sample + CLAUDE.md + Directory.Packages.props

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `samples/TicTacToeDuel/Program.cs` | sample config | request-response | self (existing AddGameKit+AddAuth chain) | exact (append `.AddGameKitAdmin(...)` + `app.UseGameKitAdmin()` + `app.MapGameKitAdmin("/admin")`) |
| `samples/TicTacToeDuel/TicTacToeDuel.csproj` | csproj | n/a | self | exact (add ProjectReference to `src/GameKit.Admin.UI`) |
| `Directory.Packages.props` | pin table | n/a | self | exact (add `<PackageVersion Include="MudBlazor" Version="9.3.0" />`) |
| `CLAUDE.md` | docs | n/a | self | exact (new "Per-Package NuGet Dependencies — `GameKit.Admin.UI`" row) |
| `src/GameKit.Cli/GameKit.Cli.csproj` | csproj | n/a | self | exact (add ProjectReference to `GameKit.Admin.UI` so CLI can reference `AdminUser`) |

### New — Tests

| File | Role | Data Flow | Closest Analog | Match Quality |
|------|------|-----------|----------------|---------------|
| `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` | test csproj | n/a | `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` (inferred — check via glob) | exact |
| `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` | test csproj | n/a | `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj` | **exact** |
| `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` | test host | n/a | `tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs` | **exact** (WebApplicationFactory shape, Core+Auth+Admin migration apply) |
| `tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs` | integration test | CRUD | `tests/GameKit.Auth.Integration.Tests/AuthSchemaTests.cs` | **exact** (assert tables + history table exist after migration) |
| `tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs` | integration test | CRUD | `tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs` | **exact** (assert live hashtext matches pinned key; assert distinct from Core + Auth keys) |
| `tests/GameKit.Admin.Integration.Tests/AuthSchemeIsolationTests.cs` | integration test | request-response | `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` | role-match |
| `tests/GameKit.Admin.Integration.Tests/SuperadminGateTests.cs` | integration test | request-response | `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` | role-match |
| `tests/GameKit.Admin.Integration.Tests/CspTests.cs` + `AntiforgeryTests.cs` + `BanFlowTests.cs` + `HealthProbeTests.cs` + `PlayerSearchTests.cs` + `MatchHistoryTests.cs` + `BanEnforcementTests.cs` + `MountPathTests.cs` + `RoadmapScenarioTests.cs` | integration tests | request-response | `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` (structural template) | role-match |
| `tests/GameKit.Cli.Tests/AdminCreateCommandTests.cs` | test | CRUD | existing `tests/GameKit.Cli.Tests` (inferred — `MigrateCommand` test likely present) | role-match |
| `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs` | composite fixture | n/a | `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs` | **exact** |

---

## Shared Patterns

These cross-cutting patterns apply to multiple new files. Planner should emit **one `read_first` entry** per pattern and reference it from every plan that uses it rather than repeating the block.

### SP-1 — SPDX header + XML-doc discipline (every new `.cs` file)

**Source:** `src/GameKit.Core/Data/GameKitDbContext.cs:1-2`, plus every other C# file in the repo.
**Apply to:** EVERY new `.cs` file (Admin.UI, CLI, tests).

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

CLAUDE.md mandates XML-doc comments on every public type/member (CS1591-as-error). See `src/GameKit.Auth/Services/IAuthAuditWriter.cs` lines 10-33 for the canonical tone: one-sentence summary, `<param>` per argument, `<remarks>` for non-obvious behavior.

### SP-2 — AssemblyInfo.cs shape (per-package)

**Source:** `src/GameKit.Auth/AssemblyInfo.cs:1-12`
**Apply to:** `src/GameKit.Admin.UI/AssemblyInfo.cs` (rewrite existing two-line file).

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Admin.Tests")]
[assembly: InternalsVisibleTo("GameKit.Admin.Integration.Tests")]

namespace GameKit.Admin.UI;

/// <summary>Marker type so other assemblies can pin a reference to GameKit.Admin.UI at compile time.</summary>
internal static class AdminUiMarker { }
```

### SP-3 — Per-package migration shape (history table + advisory lock + hosted service + design-time factory)

**Sources (read all four in sequence):**
1. `src/GameKit.Auth/Data/AuthMigrationConstants.cs:11-35` — constants file (history table + advisory lock key, with live-verification reference)
2. `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs:15-24` — IModelBuilderExtension that applies sibling configurations
3. `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-85` — hosted service that applies migrations under the advisory lock (consumes `GameKitOptions` + `MigrationRunner.MigrateWithLockAsync`)
4. `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs:41-117` — design-time factory + sibling `AuthMigrationModelCustomizer` (excludes Core entities via `ExcludeFromMigrations()`)

**Apply to:** every Data file under `src/GameKit.Admin.UI/Data/`.

**Key points to mirror verbatim:**
- `public const string MigrationsHistoryTable = "__ef_migrations_admin";`
- Advisory lock key documented as `hashtext('gamekit.admin.migrations')::bigint` with a comment saying the pre-computed value MUST be live-verified via a `AdminAdvisoryLockKeyTests` integration test (see SP-13).
- Hosted service registered with `AddHostedService<AdminMigrationHostedService>()` AFTER `AddHostedService<AuthMigrationHostedService>()` in `AddGameKitAdmin` so admin tables are created after Auth tables (FK cascade to `gamekit.players`).
- `AdminMigrationModelCustomizer.Customize` must call `ExcludeFromMigrations()` on the Core entities (`Player`, `GameSession`, `SessionParticipant`, `AdminAuditLog`) AND the three Auth entities (`PlayerIdentity`, `PlayerCredential`, `RefreshToken`) — one more list than AuthMigration customizer excludes.

### SP-4 — `IXxxAuditWriter` + `XxxAuditWriter` pair (Scoped service)

**Source:** `src/GameKit.Auth/Services/IAuthAuditWriter.cs:15-33` + `src/GameKit.Auth/Services/AuthAuditWriter.cs:15-56`

**Apply to:** `IAdminAuditWriter.cs` + `AdminAuditWriter.cs`.

Mirror the interface **verbatim**, add one parameter — `object? before`:

```csharp
Task WriteAsync(
    string action,              // e.g. "admin.player.ban"
    string targetType,          // e.g. "player"
    Guid? targetId,
    Guid actorId,               // admin performing action (NON-nullable for Admin; Auth uses nullable)
    object? before,             // NEW — ban/unban/rank-adjust diff captures both sides
    object? after,
    string? reason,
    CancellationToken cancellationToken = default);
```

Implementation mirrors `AuthAuditWriter.WriteAsync` lines 32-55 — inject `GameKitDbContext`, `IClock`, `IIdGenerator`, build `AdminAuditLog` with `Id = _ids.NewId()`, serialize before/after via `JsonSerializer.Serialize → JsonDocument.Parse`, call `_ctx.SaveChangesAsync(ct)`.

**Scoped lifetime** — rides the caller's transaction (see `AddAuth` registration at `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:87`).

### SP-5 — Fluent builder extension (`AddGameKitAdmin` on `IGameKitBuilder`)

**Source:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:44-256`

**Apply to:** `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`.

**Order of registrations mirrors Auth**:
1. Construct + validate options → `builder.Services.AddSingleton(opts)`
2. `TryAddEnumerable<IModelBuilderExtension, AdminModelBuilderExtension>` (line 59-60 of Auth)
3. `AddHostedService<AdminMigrationHostedService>()` (line 64 of Auth)
4. **NEW** — `AddHostedService<SuperadminGateHostedService>()` — unique to Admin
5. **NEW** — `AddAuthentication().AddCookie("GameKitAdmin", options => ...)` — see RESEARCH.md §Authentication lines 343-357 for cookie options block
6. **NEW** — `AddAuthorization(ao => { ao.AddPolicy(...); })` with `.AddAuthenticationSchemes("GameKitAdmin")` pinning
7. Scoped services: `IAdminAuditWriter`, `IAdminAuthService`, `IPlayerSearchService`, `IPlayerBanService`, `IAdminUserService`, `IHealthProbeService`
8. Singleton: `ErrorRateRingBuffer`, `ILoggerProvider, LogErrorCounter`
9. Rate limiter: `AddAdminRateLimits(new GameKitRateLimitPolicies())` — mirror line 163 of Auth
10. **NEW** — `AddAntiforgery(o => o.HeaderName = "X-GameKit-Admin-CSRF")`
11. **NEW** — `AddRazorComponents().AddInteractiveServerComponents()`
12. **NEW** — `AddMudServices()`
13. FluentValidation scoped: `IValidator<BanPlayerRequest>`, `IValidator<CreateAdminRequest>`, `IValidator<PlayerSearchRequest>`
14. `return builder;`

**Validation helper** — mirror `ValidateAuthOptions` at `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:259-293` (throws `ArgumentException` with field-path message).

### SP-6 — Application builder extensions (`UseGameKitAdmin` + `MapGameKitAdmin`)

**Source:** `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs:25-62`

**Apply to:** `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs`.

Two extension methods:

- `UseGameKitAdmin(this IApplicationBuilder app)` — calls `app.UseMiddleware<AdminCspNonceMiddleware>()` + `app.UseAntiforgery()` (per RESEARCH.md §CSP + antiforgery).  Keep the method body compact and documented with the required ordering note (same doc style as `UseGameKitAuth` lines 26-39).
- `MapGameKitAdmin(this IEndpointRouteBuilder routes, string prefix = "/admin")` — mirror `MapAuth` (lines 55-61 of Auth): accept the prefix, call `routes.MapRazorComponents<App>().AddInteractiveServerRenderMode().WithStaticAssets()`, then `var group = routes.MapGroup($"{prefix}/api"); AdminEndpoints.Map(group);`.

### SP-7 — Endpoint group + FluentValidation filter + rate-limit policy (minimal API pattern)

**Sources (read together):**
1. `src/GameKit.Auth/Http/AuthEndpoints.cs:48-89` — endpoint group registration with `.AddEndpointFilter<ValidationEndpointFilter<TRequest>>()` + `.RequireRateLimiting(policies.AuthLogin)` + `.RequireAuthorization()`
2. `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs:20-40` — generic endpoint filter (COPY VERBATIM into Admin.UI; do not re-implement)

**Apply to:** `src/GameKit.Admin.UI/Http/AdminEndpoints.cs`.

**Endpoint auth policy** — every admin endpoint carries `.RequireAuthorization(AdminPolicies.Admin)` or `.RequireAuthorization(AdminPolicies.Superadmin)` — the policy itself pins `AuthenticationSchemes = ["GameKitAdmin"]` so a player Bearer token cannot satisfy it (RESEARCH.md §Scheme Isolation).

**Mutation endpoints** additionally add `.AddEndpointFilter<AntiforgeryValidationFilter>()` before the validation filter so CSRF fails BEFORE deserialization.

Example endpoint shape (mirror `LoginAsync` at `AuthEndpoints.cs:93-123`):

```csharp
group.MapPost("/players/{id:guid}/ban", BanPlayerHandler)
    .RequireAuthorization(AdminPolicies.Admin)
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .AddEndpointFilter<ValidationEndpointFilter<BanPlayerRequest>>()
    .RequireRateLimiting(policies.AdminMutation); // optional — planner decides
```

### SP-8 — Rate-limit registration (sliding window for admin login)

**Source:** `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs:22-97`

**Apply to:** `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs`.

Copy the file near-verbatim. Two differences:
- Policy is **sliding window**, not fixed window — use `RateLimitPartition.GetSlidingWindowLimiter` + `SlidingWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 }` (6 segments so each 10s boundary moves the window forward).
- Partition key is **IP only** (not IP+fingerprint — admin operators do not send `X-GameKit-Device`). See `AddPolicy` at line 80 of Auth for the structure; replace the fp composition with `partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
- Policy name is `gamekit:admin:login` (per D-18). Either add to `IGameKitRateLimitPolicies` (preferred — lives in Core) OR hard-code the string in the Admin constants file. Auth's pattern at `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:163` resolves `new GameKitRateLimitPolicies()` inline to avoid the DI round-trip.

### SP-9 — FluentValidation validator + options injection

**Source:** `src/GameKit.Auth/Http/Validators/RegisterRequestValidator.cs:15-39`

**Apply to:** every validator under `src/GameKit.Admin.UI/Http/Validators/`.

Key pattern:
- Inherit `AbstractValidator<TRequest>`.
- Resolve options via constructor injection (`GameKitAdminOptions`).
- Use `When(x => x.Foo is not null, () => RuleFor(x => x.Foo)...)` for conditional rules.
- Compile regexes in the constructor with `RegexOptions.Compiled`.

**Ban reason rules** (D-09, 3–512 chars, required):

```csharp
public sealed class BanPlayerRequestValidator : AbstractValidator<BanPlayerRequest>
{
    public BanPlayerRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MinimumLength(3).WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(512).WithMessage("Reason is too long (max 512 characters).");
    }
}
```

Copy structure from the `RegisterRequestValidator` for `CreateAdminRequestValidator` (username regex + min password length).

### SP-10 — Entity + `IEntityTypeConfiguration<T>` pair (citext + HasCheckConstraint)

**Source:** `src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs:12-34`

**Apply to:** `src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs`.

Pattern:

```csharp
internal sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> b)
    {
        b.ToTable("admin_users");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();

        b.Property(a => a.Username).IsRequired().HasColumnType("citext").HasMaxLength(32);
        b.Property(a => a.PasswordHash).IsRequired().HasMaxLength(72);   // BCrypt <=72 chars
        b.Property(a => a.Role).IsRequired().HasMaxLength(16);
        b.Property(a => a.CreatedAt).IsRequired();
        b.Property(a => a.LastLoginAt);
        // Defense-in-depth (RESEARCH §admin_users schema):
        b.Property(a => a.FailedLoginCount).HasDefaultValue(0);
        b.Property(a => a.LockedUntil);

        // CHECK constraint for role enum (D-06)
        b.ToTable(t => t.HasCheckConstraint(
            "ck_admin_users_role",
            "role IN ('admin','superadmin')"));

        b.HasIndex(a => a.Username).IsUnique();

        // NO FK to players — admin_users is a separate identity store (D-06).
    }
}
```

**Migration citext note** — `CREATE EXTENSION IF NOT EXISTS citext` is already installed by `AuthInitial` migration (see `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs:22`). Admin migration MUST NOT recreate it. Admin migration depends on Auth migration running first, which the hosted-service registration order guarantees (SP-3).

### SP-11 — Hosted service startup gate pattern (superadmin gate)

**Source:** `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-85` (structural analog — IHostedService lifecycle, scoped DbContext, log-or-throw decision)

**Apply to:** `src/GameKit.Admin.UI/Authentication/SuperadminGateHostedService.cs`.

Key pattern (novel — no exact analog):

```csharp
internal sealed class SuperadminGateHostedService : IHostedService
{
    private readonly IHostEnvironment _env;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SuperadminGateHostedService> _logger;

    public SuperadminGateHostedService(IHostEnvironment env, IServiceProvider sp, ILogger<SuperadminGateHostedService> logger)
    { _env = env; _sp = sp; _logger = logger; }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var hasSuper = await ctx.Set<AdminUser>().AsNoTracking()
            .AnyAsync(u => u.Role == AdminRoles.Superadmin, ct);
        if (hasSuper) return;

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

**Registration order** — `AddHostedService<AdminMigrationHostedService>()` **must** precede `AddHostedService<SuperadminGateHostedService>()` in `AddGameKitAdmin`, so `admin_users` table exists when the gate queries it (RESEARCH §Startup fail-fast).

### SP-12 — Integration test fixture composition + WebApplicationFactory-style host

**Sources (read all three):**
1. `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs:13-31` — composite fixture
2. `tests/GameKit.TestFixtures/PostgresFixture.cs:17-62` — Postgres container fixture with three-role connection strings
3. `tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs:39-208` — in-process host with Core+Auth migration apply + service composition

**Apply to:**
- `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs` — mirror `AuthIntegrationFixture` verbatim (`PostgresFixture` + `RedisFixture` — admin surface has no WireMock egress).
- `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` — mirror `AuthTestHost` structure. Key differences:
  - `StartAsync(PostgresFixture pg)` — no WireMock dep.
  - Apply **three** migration passes in `MigrateAsync`: Core, Auth, Admin (add a third block after line 189 of `AuthTestHost` that builds a DbContext with `AdminMigrationConstants.MigrationsHistoryTable` + `AdminMigrationModelCustomizer` and calls `MigrateAsync`).
  - Host composition: `services.AddGameKit(o => ...).AddAuth(o => { o.SkipAuthenticationSchemeRegistration = true; ... }).AddGameKitAdmin(o => ...)`.
  - Middleware order: `app.UseRouting(); app.UseRateLimiter(); app.UseGameKitAuth(); app.UseGameKit(); app.UseGameKitAdmin(); app.UseEndpoints(e => { e.MapAuth(); e.MapGameKit(); e.MapGameKitAdmin("/admin"); });`
  - Optionally seed a superadmin via a `SeedAdminAsync(string username, string password, string role)` helper so per-test login flows can exercise authenticated paths.
  - Add `SetEnvironment(string env)` or pass `web.UseEnvironment("Production")` so `SuperadminGateTests` can flip environments.

### SP-13 — Advisory-lock live-verification test

**Source:** `tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs:22-39`

**Apply to:** `tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs`.

Copy verbatim, swap constants:

```csharp
[Fact]
public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
{
    await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT hashtext('gamekit.admin.migrations')::bigint";
    var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    Assert.Equal(AdminMigrationConstants.AdvisoryLockKey, computed);
}

[Fact]
public void AdminKey_Is_Distinct_From_Core_And_Auth_Keys()
{
    Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey, AdminMigrationConstants.AdvisoryLockKey);
    Assert.NotEqual(AuthMigrationConstants.AdvisoryLockKey, AdminMigrationConstants.AdvisoryLockKey);
}
```

The pinned constant value is **unknown at plan time** — first implementation iteration sets a placeholder, runs the test against Postgres (Testcontainers), and records the computed `hashtext` result as the final value. Mirrors the Phase-2 precedent documented in the `AuthMigrationConstants.AdvisoryLockKey` XML-doc lines 22-24.

### SP-14 — Schema existence test

**Source:** `tests/GameKit.Auth.Integration.Tests/AuthSchemaTests.cs:22-80`

**Apply to:** `tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs`.

Build Core services with Auth + Admin model-builder extensions registered via `TryAddEnumerable`, apply migrations Core→Auth→Admin in order, then assert `admin_users` + `__ef_migrations_admin` exist under `gamekit.*`, AND assert `__ef_migrations_core` + `__ef_migrations_auth` still coexist (per-package isolation).

### SP-15 — CLI command (Settings class + ServiceCollection + scoped DbContext)

**Source:** `src/GameKit.Cli/Commands/MigrateCommand.cs:17-59`

**Apply to:** `src/GameKit.Cli/Commands/Admin/AdminCreateCommand.cs` (replaces existing stub at `src/GameKit.Cli/Commands/AdminCreateCommand.cs:11-19`).

Pattern:
- `internal sealed class AdminCreateCommand : AsyncCommand<AdminCreateCommand.Settings>`.
- Nested `public sealed class Settings : CommandSettings` with `[CommandOption]` + `[Description]` + `[DefaultValue]` attributes.
- Body builds `new ServiceCollection()`, calls `services.AddGameKit(o => { o.ConnectionString = conn; o.AutoMigrate = false; })`, plus `services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AdminModelBuilderExtension>())` + `services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>()` + the BCrypt-hasher GameKitAuthOptions singleton.
- `await using var sp = services.BuildServiceProvider(); await using var scope = sp.CreateAsyncScope();` — resolve `GameKitDbContext`, `IPasswordHasher`, `IIdGenerator`, `IClock`.
- Use `AnsiConsole.Ask<string>` / `AnsiConsole.MarkupLine` for prompts + results. Mask password via `Console.ReadKey(intercept: true)` loop (see RESEARCH.md §CLI Bootstrap lines 1065-1078 for the full `ReadPasswordMasked` helper).
- Return 0 on success, 2 on validation failure (same convention as `MigrateCommand`).

**Program.cs wiring:** `src/GameKit.Cli/Program.cs:7-16` currently registers `AdminCreateCommand` under `"admin"`. The planner keeps that name OR switches to a branch: `config.AddBranch("admin", admin => { admin.AddCommand<AdminCreateCommand>("create"); })` so the command is `dotnet gamekit admin create` instead of `dotnet gamekit admin --create` — matches D-08 expectation.

**csproj:** `src/GameKit.Cli/GameKit.Cli.csproj:15-18` — add `<ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />` so `AdminUser` and `AdminModelBuilderExtension` are visible. `InternalsVisibleTo` is not required — expose these types as `public` OR add IVT to Cli. Prefer making them public (they're package-surface types anyway).

### SP-16 — Phase-2 ban check helper + provider patches

**Source:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs:88-100` (EF AsNoTracking query pattern) + RESEARCH.md §Integration with Phase 2 lines 1094-1118.

**Apply to:**
- NEW: `src/GameKit.Auth/Services/BannedCheckHelper.cs` — static helper exposing `public static async Task<OAuthResult?> CheckAsync(GameKitDbContext ctx, Guid playerId, CancellationToken ct)`. Returns `null` when not banned, `OAuthResult.Fail($"banned:{reasonHash}")` when banned. Uses `SHA256.HashData` + `Convert.ToHexString`.
- MODIFY: 4 provider files (`SteamOAuthProvider.cs`, `DiscordOAuthProvider.cs`, `GuestOAuthProvider.cs`, `PasswordOAuthProvider.cs`). Insert the ban check after the player upsert, before `_refresh.IssueRootAsync(...)`. The diff is 3-4 lines of new code per provider:
  ```csharp
  var banned = await BannedCheckHelper.CheckAsync(_ctx, playerId, cancellationToken).ConfigureAwait(false);
  if (banned is not null) return banned.Value;
  var tokens = await _refresh.IssueRootAsync(playerId, Provider, fingerprint, cancellationToken).ConfigureAwait(false);
  ```
- MODIFY: `src/GameKit.Auth/Services/RefreshTokenService.cs` — insert ban check in `RotateAsync` AFTER the live-row expiry/fingerprint checks (around line 160) and BEFORE the happy-path rotation. Use the existing `RevokeFamilyInScope` helper (already in file) with reason `"player_banned"`. See RESEARCH.md lines 1132-1150 for exact patch location and shape.

All patches require new tests: `BannedPlayer_Login_Returns403` (per provider) and `BannedPlayer_Refresh_RevokesFamily`. Tests live in `tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs` but actually exercise Phase-2 paths.

### SP-17 — Test csproj shape

**Source:** `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj:1-28`

**Apply to:** `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` and `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj`.

Exact structure:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GameKit.Admin.Integration.Tests</RootNamespace>
    <AssemblyName>GameKit.Admin.Integration.Tests</AssemblyName>
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
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

Note: WireMock.Net is **not** required for Admin tests (no external egress to mock).

---

## Pattern Assignments (Per-File Concrete Actions)

Each new source file below has (a) an **analog file** the planner should put in `read_first`, (b) a **pattern** to apply, and (c) a tight **action** note. Shared patterns referenced by `SP-NN`.

### `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` (rewrite)

- **Analog:** `src/GameKit.Auth/GameKit.Auth.csproj:1-52`
- **Action:** Rewrite with `<Project Sdk="Microsoft.NET.Sdk.Razor">` (NOT plain SDK — RESEARCH.md §Package Composition line 192), add `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, `<ProjectReference>` for `GameKit.Core` and `GameKit.Auth`, `<PackageReference>` for EF Core + Npgsql + `MudBlazor` + FluentValidation (both packages) + StackExchange.Redis + `Microsoft.EntityFrameworkCore.Design` (with `<PrivateAssets>all</PrivateAssets>`). PackageId/Tags/Description already present — keep.

### `src/GameKit.Admin.UI/AssemblyInfo.cs` (rewrite)

- **Analog:** `src/GameKit.Auth/AssemblyInfo.cs:1-12`
- **Action:** See SP-2. Replace 2-line stub with `InternalsVisibleTo` for the two test assemblies + marker type.

### `src/GameKit.Admin.UI/GameKitAdminOptions.cs`

- **Analog:** `src/GameKit.Auth/GameKitAuthOptions.cs:12-41` (nested option groups)
- **Action:** Root options class with nested groups: `CookieOptions` (name, `ExpireTimeSpan`, `SlidingExpiration`, `LoginPath`, `LogoutPath`, `RememberMeDuration`), `PanelOptions` (`PanelRefreshInterval` default `TimeSpan.FromSeconds(10)`), `CspOptions` (policy string template + report-only toggle), `MountPath` (default `/admin`). Include XML-doc on every public member.

### `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`

- **Analog:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:44-293`
- **Action:** See SP-5. Public static `AddGameKitAdmin(this IGameKitBuilder, Action<GameKitAdminOptions>)`. Internal static `ValidateAdminOptions(opts)` — throws `ArgumentException` for misconfigured cookie paths / invalid panel refresh interval.

### `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs`

- **Analog:** `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs:25-62`
- **Action:** See SP-6. Two methods: `UseGameKitAdmin(IApplicationBuilder)` → inserts middleware (nonce + antiforgery + optional NotFoundWhenUnauthorized if chosen over cookie-events approach), returns the builder. `MapGameKitAdmin(IEndpointRouteBuilder, string prefix = "/admin")` → maps Razor components + `/api` subgroup.

### `src/GameKit.Admin.UI/Entities/AdminUser.cs`

- **Analog:** `src/GameKit.Auth/Entities/PlayerCredential.cs:14-27` + `src/GameKit.Auth/Entities/RefreshToken.cs:14-48` (optional columns)
- **Action:** `public sealed class AdminUser` with `Id (Guid)`, `Username (required string, citext)`, `PasswordHash (required string)`, `Role (required string)`, `CreatedAt (DateTimeOffset)`, `LastLoginAt (DateTimeOffset?)`, `FailedLoginCount (int)`, `LockedUntil (DateTimeOffset?)`. XML-doc every property (Phase-2 precedent).

### `src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs`

- **Analog:** `src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs:12-34`
- **Action:** See SP-10. `internal sealed` + `IEntityTypeConfiguration<AdminUser>`, citext + HasCheckConstraint for role enum.

### `src/GameKit.Admin.UI/Data/AdminModelBuilderExtension.cs`

- **Analog:** `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs:15-24`
- **Action:** `internal sealed class AdminModelBuilderExtension : IModelBuilderExtension` that applies `AdminUserConfiguration`.

### `src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs`

- **Analog:** `src/GameKit.Auth/Data/AuthMigrationConstants.cs:11-35`
- **Action:** `public static class AdminMigrationConstants` with `public const string MigrationsHistoryTable = "__ef_migrations_admin";` and `public const long AdvisoryLockKey = PLACEHOLDER_LIVE_VERIFIED_IN_TEST_FIRST_RUN;`. XML-doc references SP-13 live-verification test and the `SELECT hashtext('gamekit.admin.migrations')::bigint` derivation.

### `src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs`

- **Analog:** `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-85`
- **Action:** `internal sealed class AdminMigrationHostedService : IHostedService`. Copy verbatim, swap constants (`AuthMigrationConstants` → `AdminMigrationConstants`), swap customizer (`AuthMigrationModelCustomizer` → `AdminMigrationModelCustomizer`), swap log messages.

### `src/GameKit.Admin.UI/Data/AdminDesignTimeDbContextFactory.cs`

- **Analog:** `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs:41-117`
- **Action:** Two types in one file (same file structure as Auth): `AdminDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>` + `AdminMigrationModelCustomizer : RelationalModelCustomizer`. The customizer applies `AdminUserConfiguration` AND excludes Core + Auth entities via `ExcludeFromMigrations()`. See SP-3 for the full exclusion list.

### `src/GameKit.Admin.UI/Migrations/YYYYMMDDHHMMSS_AdminInitial.cs`

- **Analog:** `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs:1-80`
- **Action:** Generated by `dotnet ef migrations add AdminInitial --project src/GameKit.Admin.UI --startup-project src/GameKit.Admin.UI`. DO NOT hand-edit except to add `CREATE EXTENSION IF NOT EXISTS pg_trgm;` prologue for the trigram GIN index mentioned in RESEARCH.md §Index additions lines 837-847, if the planner chooses to add it. Does NOT re-add `citext` extension (Auth already installs it).

### `src/GameKit.Admin.UI/Authentication/AdminAuthenticationSchemeConstants.cs`

- **Analog:** `src/GameKit.Auth/Providers/Steam/SteamConstants.cs` (structural — string constants)
- **Action:** `public static class AdminAuthenticationSchemeConstants { public const string Scheme = "GameKitAdmin"; public const string CookieName = "gk_admin_session"; public const string CsrfHeaderName = "X-GameKit-Admin-CSRF"; public const string CsrfCookieName = "gk_admin_csrf"; }`.

### `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs`

- **Analog:** No direct analog. Structurally similar to `AuthBuilderExtensions` lines 176-253 (events wired into an auth handler), but the mechanism is different.
- **Action:** `public sealed class AdminCookieEvents : CookieAuthenticationEvents`. Override `RedirectToLogin(RedirectContext<CookieAuthenticationOptions>)` to return 404 when `_env.IsProduction()` AND path is NOT `/admin/login` (see RESEARCH.md §404-not-401 lines 473-497 for exact body). Override `RedirectToAccessDenied` to return 403 unconditionally. Inject `IHostEnvironment` via constructor. Register via `.AddCookie(options => options.EventsType = typeof(AdminCookieEvents))` in `AddGameKitAdmin` (see SP-5 step 5).

### `src/GameKit.Admin.UI/Authentication/SuperadminGateHostedService.cs`

- **Analog:** `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-85` (structural — IHostedService + scoped DbContext)
- **Action:** See SP-11 — full concrete code block.

### `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` + `AdminRoles.cs`

- **Analog:** `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs:7-38` (constants-only class)
- **Action:** Two small classes. `AdminPolicies` has `public const string Admin = "gamekit.admin.admin"` + `Superadmin`. `AdminRoles` has `public const string Admin = "admin"` + `Superadmin = "superadmin"`. Referenced from `AddGameKitAdmin` authorization block and from endpoint `.RequireAuthorization(AdminPolicies.Admin)` chains.

### `src/GameKit.Admin.UI/Services/IAdminAuditWriter.cs` + `AdminAuditWriter.cs`

- **Analog:** `src/GameKit.Auth/Services/IAuthAuditWriter.cs:15-33` + `src/GameKit.Auth/Services/AuthAuditWriter.cs:15-56`
- **Action:** See SP-4. Mirror verbatim; add `object? before` parameter to `WriteAsync`. Scoped registration.

### `src/GameKit.Admin.UI/Services/IAdminAuthService.cs` + `AdminAuthService.cs`

- **Analog:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs:34-100` (credential lookup + BCrypt verify + audit write)
- **Action:** Interface exposes `Task<Guid?> VerifyPasswordAsync(string username, string password, CancellationToken ct)` returning admin id on success, null on failure. Implementation loads `AdminUser` by `citext` username, calls `IPasswordHasher.Verify`, writes `admin.session.login.success` / `admin.session.login.failure` audit rows, updates `LastLoginAt` on success. Include dummy-hash timing equalization as in `PasswordOAuthProvider.DummyHash` line 40.

### `src/GameKit.Admin.UI/Services/IPlayerSearchService.cs` + `PlayerSearchService.cs`

- **Analog:** `src/GameKit.Core/Http/PlayerEndpoints.cs:19-53` (AsNoTracking + OrderBy + Take + anonymous projection)
- **Action:** Implement `SearchAsync(string query, Guid? afterId, int pageSize, CancellationToken ct)`. Input-type detection per RESEARCH.md §Unified search lines 854-886 (Guid.TryParse → id lookup; `provider:external_id` split → PlayerIdentity lookup; else → `display_name` prefix via `EF.Functions.ILike(p.DisplayName, prefix + "%")`). Keyset pagination per RESEARCH.md §Keyset lines 794-821. Returns `PaginatedResult<PlayerRow>` record with `NextCursor`.

### `src/GameKit.Admin.UI/Services/IPlayerBanService.cs` + `PlayerBanService.cs`

- **Analog:** `src/GameKit.Core/Services/GdprDeleteService.cs:16-84` (SERIALIZABLE tx + snapshot-before + audit-write + ExecuteUpdate)
- **Action:** Two methods: `BanAsync(Guid playerId, Guid actorId, string reason, CancellationToken ct)` and `UnbanAsync(Guid playerId, Guid actorId, string? reason, CancellationToken ct)`. BEGIN SERIALIZABLE TX, snapshot `Player` state (Before), `ExecuteUpdate` setting `IsBanned`/`BannedAt`/`BanReason`, write `admin.player.ban` or `admin.player.unban` audit via `IAdminAuditWriter` (passes before+after), COMMIT. The SERIALIZABLE isolation matches `GdprDeleteService` line 33 and handles the `admin_audit_log` + `players` row concurrent update.

### `src/GameKit.Admin.UI/Services/IAdminUserService.cs` + `AdminUserService.cs`

- **Analog:** `src/GameKit.Auth/Services/GuestUpgradeService.cs` (read the file — scoped service doing SERIALIZABLE upsert + unique check)
- **Action:** Methods: `CreateAsync(username, password, role, actorId, ct)`, `DeleteAsync(Guid adminId, Guid actorId, ct)`, `ListAsync(Guid? afterId, int pageSize, ct)`. Creates use `IPasswordHasher.Hash`, check `admin_users.Username` uniqueness under SERIALIZABLE (CONTEXT carryover D-14 note in Phase 2). Delete blocks removing the last superadmin (`Role == superadmin` count check). Audits `admin.admin.create` + `admin.admin.delete` via `IAdminAuditWriter`.

### `src/GameKit.Admin.UI/Services/IHealthProbeService.cs` + `HealthProbeService.cs`

- **Analog:** None (novel). Use RESEARCH.md §Health panel lines 894-921 as the authoritative source.
- **Action:** `CheckPostgresAsync(ct)` — opens `NpgsqlConnection`, runs `SELECT 1` with 2s timeout, returns `(Status, latencyMs)`. `CheckRedisAsync(ct)` — `IConnectionMultiplexer.GetDatabase().PingAsync()`. `GetRecentErrorRate()` — reads from singleton `ErrorRateRingBuffer`. Returns `HealthReport` record with three tiles + last-checked timestamp.

### `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` + `LogErrorCounter.cs`

- **Analog:** None (novel ILoggerProvider). Use RESEARCH.md §Recent error rate lines 913-921.
- **Action:** `ErrorRateRingBuffer` — thread-safe 5-minute ring with 1-second buckets, `Interlocked.Increment`. `LogErrorCounter : ILoggerProvider` — `CreateLogger` returns an `ILogger` whose `Log()` method increments the buffer when `logLevel >= LogLevel.Error`. Registered as `Singleton<ILoggerProvider, LogErrorCounter>` in `AddGameKitAdmin`.

### `src/GameKit.Admin.UI/Http/AdminEndpoints.cs`

- **Analog:** `src/GameKit.Auth/Http/AuthEndpoints.cs:42-89` (group registration) + lines 93-200 (handler shape)
- **Action:** See SP-7. Static class with `public static RouteGroupBuilder Map(RouteGroupBuilder api)`. Routes:
  - `POST /login` (anonymous, rate-limited `gamekit:admin:login`, validator filter)
  - `POST /logout` (anonymous — cookie present is enough)
  - `POST /players/search` (Admin policy, validator filter) — returns `PaginatedResult<PlayerSearchResult>`
  - `POST /players/{id:guid}/ban` (Admin, antiforgery + validator filter)
  - `POST /players/{id:guid}/unban` (Admin, antiforgery + validator filter)
  - `POST /players/{id:guid}/gdpr-delete` (Superadmin, antiforgery) — calls `IGdprDeleteService.DeletePlayerAsync`
  - `GET /admins` (Superadmin)
  - `POST /admins` (Superadmin, antiforgery + validator filter)
  - `DELETE /admins/{id:guid}` (Superadmin, antiforgery)
  - `GET /audit` (Admin) — keyset-paginated audit query
  - `GET /match-history` (Admin)
  - `GET /health` (Admin) — returns HealthReport

### `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs`

- **Analog:** `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs:22-97`
- **Action:** See SP-8.

### `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs`

- **Analog:** `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs:20-40` (structural)
- **Action:** Resolve `IAntiforgery` from `ctx.HttpContext.RequestServices`, call `await antiforgery.ValidateRequestAsync(ctx.HttpContext)`, catch `AntiforgeryValidationException` → `Results.BadRequest(new { error = "csrf_validation_failed" })`. See RESEARCH.md §Antiforgery endpoint filter lines 681-697 for the exact 15-line body.

### `src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs`

- **Analog:** `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs:20-40`
- **Action:** Copy verbatim — identical generic filter. Considered making it shared in Core, but Auth and Admin remain independent.

### `src/GameKit.Admin.UI/Http/Contracts/BanPlayerRequest.cs` + `UnbanPlayerRequest.cs` + `CreateAdminRequest.cs` + `PlayerSearchRequest.cs` + `PlayerSearchResult.cs` + `PaginatedResult.cs`

- **Analog:** `src/GameKit.Auth/Http/Contracts/LoginRequest.cs:12`, `src/GameKit.Auth/Http/Contracts/RegisterRequest.cs`
- **Action:** Positional records (`public sealed record BanPlayerRequest(string Reason);` — one line + XML-doc). Include full XML-doc on the record and each parameter (CS1591 strict).

### `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs`

- **Analog:** `src/GameKit.Auth/Http/Validators/RegisterRequestValidator.cs:15-39`
- **Action:** See SP-9 — full concrete code block.

### `src/GameKit.Admin.UI/Http/Validators/CreateAdminRequestValidator.cs`

- **Analog:** `src/GameKit.Auth/Http/Validators/RegisterRequestValidator.cs:15-39`
- **Action:** Username regex (e.g. `^[a-z0-9_-]{3,32}$`), password min length (≥ 12 recommended per UI-SPEC §13; enforced at ≥ 8 in CLI — keep a looser minimum here), role must be `"admin"` or `"superadmin"`.

### `src/GameKit.Admin.UI/Http/Validators/PlayerSearchRequestValidator.cs`

- **Analog:** `src/GameKit.Auth/Http/Validators/LoginRequestValidator.cs:14-28`
- **Action:** Presence check on `Query`, max length 256, min 1. Page size range 1–50 (default 50).

### `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs`

- **Analog:** None (novel). RESEARCH.md §Per-request CSP nonce lines 571-618 gives the full ~30-line body.
- **Action:** `internal sealed class AdminCspNonceMiddleware` — accepts `RequestDelegate` + `GameKitAdminOptions` + `PathString prefix`, generates 128-bit base64 nonce via `RandomNumberGenerator.Fill(stackalloc byte[16])`, stashes in `ctx.Items[NonceItemKey]`, registers `OnStarting` callback to emit the Content-Security-Policy header. Pattern comment referring to RESEARCH.md §UI Hardening. Constant: `public const string NonceItemKey = "gamekit.admin.csp-nonce";`.

### `src/GameKit.Admin.UI/Middleware/AdminNotFoundWhenUnauthorizedMiddleware.cs` (OPTIONAL)

- **Analog:** None. RESEARCH.md favors `CookieAuthenticationEvents.OnRedirectToLogin` over middleware (§Why this is the right mechanism, lines 500-503).
- **Action:** Planner may skip this file entirely if `AdminCookieEvents.RedirectToLogin` handles the 404 case. Keep only if a middleware-based belt-and-suspenders guard is wanted.

### Blazor components (`src/GameKit.Admin.UI/Components/**/*.razor` + `.razor.css`)

- **Analog:** None in repo.
- **Authoritative source:** UI-SPEC (`03-UI-SPEC.md`) — 15 surface contracts + component inventory + copywriting contract. Do NOT invent new components; mirror the inventory at §Component Inventory.
- **Action:** Planner creates one plan (or a pair of plans) to author the Razor surfaces. Each component file gets SPDX header + XML-doc on the component class via `@code { }`. Use MudBlazor components from the §Registry Safety whitelist only. CSS goes in `.razor.css` scoped files except `wwwroot/gamekit-admin.css` which carries root-level custom-property declarations (see UI-SPEC §Color — `--gk-color-*` tokens on `.gk-admin-root`). Polling uses `System.Threading.Timer` + `IAsyncDisposable` per RESEARCH.md §Polling lines 923-938.

### `src/GameKit.Cli/Commands/Admin/AdminCreateCommand.cs` (replaces stub)

- **Analog:** `src/GameKit.Cli/Commands/MigrateCommand.cs:17-59`
- **Action:** See SP-15. Replace 19-line stub at `src/GameKit.Cli/Commands/AdminCreateCommand.cs`. Move under `Commands/Admin/` subdirectory for grouping (research §CLI Bootstrap lines 981-1087 gives the complete body — copy it).

### `src/GameKit.Cli/Program.cs` (modify)

- **Analog:** self
- **Action:** Swap the `config.AddCommand<AdminCreateCommand>("admin")` single-command registration for a branch: `config.AddBranch("admin", admin => { admin.AddCommand<AdminCreateCommand>("create").WithDescription("Create an admin user (interactive or flag-driven)."); });`. Command invocation becomes `dotnet gamekit admin create ...`.

### `src/GameKit.Cli/GameKit.Cli.csproj` (modify)

- **Analog:** self + `src/GameKit.Auth/GameKit.Auth.csproj` for the ProjectReference pattern
- **Action:** Add `<ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />` after the `GameKit.Core` ref. This pulls in Blazor/MudBlazor as transitive build deps for the CLI — acceptable because the CLI only exercises non-UI types (`AdminUser`, `AdminModelBuilderExtension`, `AdminMigrationConstants`).

### `src/GameKit.Auth/Services/BannedCheckHelper.cs` (NEW in Phase 2 package)

- **Analog:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs:88-125` (EF query shape)
- **Action:** See SP-16. Static helper with one method returning `OAuthResult?`. Note this is a Phase-3 change that lives INSIDE the Phase-2 `GameKit.Auth` project — Phase 3 modifies Phase 2 code.

### `src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs` (modify)

- **Analog:** self
- **Action:** See SP-16. Insert 2–3 new lines after the player upsert + save, before `_refresh.IssueRootAsync`. Use `BannedCheckHelper.CheckAsync`.

### `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` (modify)

- **Analog:** self
- **Action:** Same as Steam.

### `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` (modify)

- **Analog:** self (see `GuestOAuthProvider.cs:60-91`)
- **Action:** Insert ban check after `SaveChangesAsync` at line 83, before `_refresh.IssueRootAsync` at line 87. Guests are freshly-created, so `IsBanned=false` always — the check is a no-op but kept for uniformity.

### `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` (modify)

- **Analog:** self
- **Action:** Same as Steam, plus update RegisterAsync path to reject if the upsert targets an existing banned player.

### `src/GameKit.Auth/Services/RefreshTokenService.cs` (modify)

- **Analog:** self (see `RefreshTokenService.cs:94-160`)
- **Action:** See SP-16. Insert ban check between the fingerprint-match validation (line 159) and the happy-path rotation (line 161+). On banned, call existing `RevokeFamilyInScope(current.FamilyId, "player_banned", current.PlayerId, ct)`, commit, throw `UnauthorizedException("player_banned")`.

### `samples/TicTacToeDuel/Program.cs` (modify)

- **Analog:** self
- **Action:** After `.AddAuth(auth => ...)` block (line 48), append `.AddGameKitAdmin(admin => { admin.MountPath = "/admin"; /* planner picks other options */ });`. In middleware block (lines 61-64), insert `app.UseGameKitAdmin();` AFTER `app.UseGameKit();` (per RESEARCH.md middleware pipeline contract line 431). Add `app.MapGameKitAdmin("/admin");` after `app.MapDemo()` at line 68.

### `samples/TicTacToeDuel/TicTacToeDuel.csproj` (modify)

- **Analog:** self
- **Action:** Add `<ProjectReference Include="..\..\src\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />`.

### `Directory.Packages.props` (modify)

- **Analog:** self (lines 43-56 — Phase-2 addition pattern)
- **Action:** Add one block:
  ```xml
  <!-- Phase 3 Admin UI — MudBlazor 9.3.0 verified GA on net10.0 2026-04-18 (MIT / GPL-compatible) -->
  <PackageVersion Include="MudBlazor" Version="9.3.0" />
  ```
  Place it in a new section after the Auth block with a comment referencing 03-RESEARCH.md §Version verification (lines 166-174).

### `CLAUDE.md` (modify)

- **Analog:** self — existing per-package sections (currently empty stubs)
- **Action:** Populate `### GameKit.Admin.UI` under `## Per-Package NuGet Dependencies` with a dependency row: MudBlazor 9.3.0 (Blazor component library), StackExchange.Redis 2.8.41 (health probe), FluentValidation 12.1.1 (validators). Cross-link to 03-RESEARCH.md.

---

## Test File Pattern Assignments

### `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` + `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj:1-28`
- **Action:** See SP-17.

### `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs:39-208`
- **Action:** See SP-12. Extend `MigrateAsync` to apply Core → Auth → Admin migrations in sequence.

### `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs`

- **Analog:** `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs:13-31`
- **Action:** Two-property composite (`PostgresFixture`, `RedisFixture`), no WireMock. Add a new xUnit collection definition in `tests/GameKit.TestFixtures/CollectionDefinitions.cs` (new file at the same level) — e.g. `[CollectionDefinition("Admin")] public class AdminCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture> {}`.

### `tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/AuthSchemaTests.cs:22-80`
- **Action:** See SP-14.

### `tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs:22-39`
- **Action:** See SP-13.

### `tests/GameKit.Admin.Integration.Tests/AuthSchemeIsolationTests.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` (structural — test class using `AuthTestHost` + `[Collection("Auth")]`)
- **Action:** Seed a player via guest provider, grab their JWT, send `GET /admin/players/search` with `Authorization: Bearer <jwt>` in Production-env host. Assert `404`. Repeat in Development, assert `302` to `/admin/login`. Per RESEARCH.md §Integration test lines 452-466.

### `tests/GameKit.Admin.Integration.Tests/SuperadminGateTests.cs`

- **Analog:** None direct; use `AuthEndpointsE2ETests.cs` as structural template.
- **Action:** Two tests: (1) Production host with zero `admin_users` rows → `StartAsync` throws `InvalidOperationException` with message containing `"dotnet gamekit admin create"`. (2) Development host with zero rows → host starts, log contains a warning with same guidance.

### `tests/GameKit.Admin.Integration.Tests/CspTests.cs`

- **Analog:** None direct; structural `AuthEndpointsE2ETests.cs`.
- **Action:** (1) `GET /admin/login` → response has `Content-Security-Policy` header matching the pinned template. (2) Two sequential requests → nonces differ. (3) `GET /auth/me` → no `Content-Security-Policy` header (admin CSP only applies to `/admin/*`).

### `tests/GameKit.Admin.Integration.Tests/AntiforgeryTests.cs`

- **Analog:** None direct.
- **Action:** (1) POST `/admin/api/players/{id}/ban` WITHOUT the CSRF header → 400 with `error = "csrf_validation_failed"`. (2) GET `/admin/login` to receive the CSRF cookie + seed the token, POST with valid header → 200.

### `tests/GameKit.Admin.Integration.Tests/BanFlowTests.cs`

- **Analog:** RESEARCH.md §Phase Requirements → Test Map row for ADMIN-06.
- **Action:** Seed superadmin → login → POST ban → assert `AdminAuditLog` row with `Action = "admin.player.ban"`, `ActorId = superadmin.Id`, `TargetId = player.Id`, `Before.is_banned = false`, `After.is_banned = true`, `Reason` = request value.

### `tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/RefreshTokenServiceTests.cs` (inferred from directory listing — refresh path tests)
- **Action:** (1) Create player → ban via service → attempt login (all 4 providers) → assert 403 with `banned:<reasonHash>`. (2) Create player → issue refresh token → ban → attempt rotate → assert `UnauthorizedException` / 401 + family revoked audit row.

### `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs`

- **Analog:** None.
- **Action:** Ping Postgres — `Status = OK`, `LatencyMs > 0`. Ping Redis — `Status = OK`. Kill Redis container (or skip and use a bogus connection string) — `Status = Down`.

### `tests/GameKit.Admin.Integration.Tests/PlayerSearchTests.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs` (uses seeded players).
- **Action:** Seed 3 players + 2 identities. Query UUID → returns exact row. Query `steam:<externalId>` → returns via identity lookup. Query display-name prefix → returns citext-insensitive matches.

### `tests/GameKit.Admin.Integration.Tests/MatchHistoryTests.cs`

- **Analog:** None direct; reuse GDPR query pattern.
- **Action:** Seed player + 3 completed sessions → `GET /admin/api/match-history?playerId=...` returns 3 rows, newest first, with rating-before/after columns populated.

### `tests/GameKit.Admin.Integration.Tests/MountPathTests.cs`

- **Analog:** None.
- **Action:** Mount admin at `/custom-admin-path`, assert `/custom-admin-path/login` reachable + `/admin/login` returns 404.

### `tests/GameKit.Admin.Integration.Tests/RoadmapScenarioTests.cs`

- **Analog:** `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` (E2E scenario structure).
- **Action:** Three tests mapping to SC #1 + SC #5 + SC #6 per RESEARCH.md Test Map lines 1284-1286. Each test is a multi-step scenario against `AdminTestHost`.

### `tests/GameKit.Cli.Tests/AdminCreateCommandTests.cs` (new file in existing project)

- **Analog:** (inferred) existing `MigrateCommand` tests in the same project.
- **Action:** (1) Non-interactive — `--username x --password y` on empty DB creates admin with `Role = "superadmin"` (auto-promoted). (2) Non-interactive on non-empty DB with `--role admin` creates with role admin. (3) Duplicate username → exit code 2. (4) Short password → exit code 2. (5) `ReadPasswordMasked` unit-mockable via redirected `Console.In` — verify asterisks, not echo.

---

## No Analog Found (Blazor UI only)

| File | Role | Reason |
|------|------|--------|
| All `src/GameKit.Admin.UI/Components/**/*.razor` (+ `.razor.css`) | Blazor page/component/layout/dialog | First Blazor work in the repo. No existing Razor files to copy from. UI-SPEC is the authoritative source; MudBlazor docs + RESEARCH.md §Panels give the mechanical patterns (timer polling, DataGrid binding, Dialog service). |
| `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css` | Static asset | First CSS authoring in the repo. UI-SPEC §Color + §Spacing Scale declare the design tokens; CSS custom properties on `.gk-admin-root` pull them together. |
| `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs` | middleware | No custom middleware precedent — all middleware so far is ASP.NET Core built-ins wired via `UseXxx()` extension methods. RESEARCH.md §Per-request CSP nonce lines 571-618 provides the full body. |
| `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` + `LogErrorCounter.cs` | utility + ILoggerProvider | No custom `ILoggerProvider` precedent. RESEARCH.md §Recent error rate lines 913-921 is the pattern source. |

---

## Metadata

**Analog search scope:**
- `src/GameKit.Core/` (all subdirectories — entities, data, services, builder, rate-limiting, http)
- `src/GameKit.Auth/` (full sweep — primary analog source)
- `src/GameKit.Cli/` (CLI pattern source for AdminCreateCommand)
- `tests/GameKit.Auth.Integration.Tests/` (test host + schema test + advisory lock test)
- `tests/GameKit.TestFixtures/` (Postgres + Redis fixture + composite fixture)
- `samples/TicTacToeDuel/` (consumer-side wiring example)

**Files scanned:** ~70 source files + 10 test files + 5 config files.

**Pattern extraction date:** 2026-04-18

**Confidence:** HIGH — GameKit.Auth and GameKit.Core provide near-complete pattern coverage for all Admin.UI server-side files. Blazor UI layer is the only gap and is covered by the approved UI-SPEC.
