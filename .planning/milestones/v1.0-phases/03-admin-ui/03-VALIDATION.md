---
phase: 03
slug: admin-ui
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-04-18
updated: 2026-04-18
---

# Phase 03 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11.0 + Moq 4.20.72 + bUnit (optional; deferred per RESEARCH.md open question #1) |
| **Config file** | `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` + `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` |
| **Quick run command** | `dotnet test tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj --logger "console;verbosity=minimal"` |
| **Full suite command** | `dotnet test --logger "console;verbosity=minimal"` (runs Admin + Auth + Core suites) |
| **Estimated runtime** | ~90–120s full (Testcontainers cold start dominates); ~5–10s unit-only |

---

## Sampling Rate

- **After every task commit:** Run quick unit suite for the affected project (`dotnet test tests/GameKit.Admin.Tests/`)
- **After every plan wave:** Run full suite (`dotnet test`) to catch cross-package regressions (Auth ban-enforcement patches affect Phase 2 tests)
- **Before `/gsd-verify-work`:** Full suite must be green including Testcontainers integration tests
- **Max feedback latency:** 120 seconds

---

## Per-Task Verification Map

One row per `<task>` across 03-01..03-13. Columns derived from each task's `<verify>` / `<automated>` block. `checkpoint:human-verify` tasks appear in the Manual-Only Verifications section below instead of here.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 03-01-T1 | 03-01 | 0 | ADMIN-01 | T-03-01-01..03 | Test projects + CPM pin created | unit (smoke) | `dotnet build tests/GameKit.Admin.Tests/... && dotnet test tests/GameKit.Admin.Tests/ --no-build` | `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj`, `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj`, `tests/GameKit.Admin.Tests/SmokeTests.cs` | pending |
| 03-01-T2 | 03-01 | 0 | ADMIN-01 | T-03-01-04..06 | Integration fixture + FakePlayerJwtIssuer + WebAppFactoryExtensions | integration scaffold | `dotnet build tests/GameKit.Admin.Integration.Tests/` | `tests/GameKit.Admin.Integration.Tests/AdminIntegrationFixture.cs`, `WebApplicationFactoryExtensions.cs`, `Mocks/FakePlayerJwtIssuer.cs` | pending |
| 03-01-T3 | 03-01 | 0 | ADMIN-01 | — | CLAUDE.md Phase 3 section + full-solution compile gate | full build | `dotnet build GameKit.sln -c Debug --nologo` | `CLAUDE.md` (updated) | pending |
| 03-02-T1 | 03-02 | 1 | ADMIN-04 | T-03-02-01..02 | AdminUser entity + EF configuration + ModelBuilderExtension + MigrationConstants | unit (build + compile-time) | `dotnet build src/GameKit.Admin.UI -c Debug --nologo && dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~AdminUser' --no-build` | `src/GameKit.Admin.UI/Entities/AdminUser.cs`, `Data/AdminMigrationConstants.cs`, `Data/AdminModelBuilderExtension.cs` | pending |
| 03-02-T2 | 03-02 | 1 | ADMIN-04 | T-03-02-03..04 | Customizer + DesignTimeFactory + MigrationHostedService + Initial migration | build + dotnet-ef validation | `dotnet ef migrations list --project src/GameKit.Admin.UI` | `src/GameKit.Admin.UI/Data/AdminMigrationModelCustomizer.cs`, `AdminDesignTimeDbContextFactory.cs`, `AdminMigrationHostedService.cs`, `Migrations/*_AdminInitial.cs` | pending |
| 03-02-T3 | 03-02 | 1 | ADMIN-04 | T-03-02-05..06 | Live-verify advisory-lock hashtext + post-migration schema assert | integration (Testcontainers) | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter 'AdminAdvisoryLockKeyTests\|AdminSchemaTests' --logger 'console;verbosity=minimal'` | `tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs`, `AdminSchemaTests.cs` | pending |
| 03-03-T1 | 03-03 | 1 | ADMIN-01, ADMIN-02 | T-03-03-01..02 | RCL csproj + AssemblyInfo marker | unit (smoke) | `dotnet build src/GameKit.Admin.UI && dotnet test tests/GameKit.Admin.Tests/ --no-build` | `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj`, `AssemblyInfo.cs` | pending |
| 03-03-T2 | 03-03 | 1 | ADMIN-01, ADMIN-02 | T-03-03-03..04 | Options defaults + roles + policies + scheme constants | unit | `dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~GameKitAdminOptionsValidationTests' --logger 'console;verbosity=minimal'` | `GameKitAdminOptions.cs`, `Authorization/AdminRoles.cs`, `AdminPolicies.cs`, `Authentication/AdminAuthenticationSchemeConstants.cs`, `tests/GameKit.Admin.Tests/GameKitAdminOptionsValidationTests.cs` | pending |
| 03-04-T1 | 03-04 | 2 | ADMIN-03, ADMIN-04 | T-03-04-01..03 | AdminCookieEvents 404/302/403 shape per D-04 | unit (xUnit Theory) | `dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~AdminCookieEventsTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs`, `tests/GameKit.Admin.Tests/AdminCookieEventsTests.cs` | pending |
| 03-04-T2 | 03-04 | 2 | ADMIN-03, ADMIN-04 | T-03-04-04 | `gamekit:admin:login` sliding window 5/min/IP registered | unit | `dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~AdminRateLimitRegistrationTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs`, `tests/GameKit.Admin.Tests/AdminRateLimitRegistrationTests.cs` | pending |
| 03-05-T1 | 03-05 | 2 | ADMIN-12 | T-03-05-01..02 | AdminCspNonceMiddleware — 128-bit nonce + hard-coded policy | unit | `dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~AdminCspNonceMiddlewareTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs`, `tests/GameKit.Admin.Tests/AdminCspNonceMiddlewareTests.cs` | pending |
| 03-05-T2 | 03-05 | 2 | ADMIN-12 | T-03-05-03..04 | AntiforgeryValidationFilter + ValidationEndpointFilter<T> | unit | `dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~AntiforgeryValidationFilterTests\|FullyQualifiedName~ValidationEndpointFilterTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs`, `ValidationEndpointFilter.cs`, test files | pending |
| 03-06-T1 | 03-06 | 3 | ADMIN-03, ADMIN-04, ADMIN-05, ADMIN-06, ADMIN-10 | T-03-06-01, T-03-06-03 | AdminAuditWriter + PlayerSearchService.ClassifyInput + AdminAuthService DummyHash + PlayerBanService (SERIALIZABLE) | unit (TDD) | `dotnet test tests/GameKit.Admin.Tests/ --filter 'AdminAuditWriterTests\|PlayerSearchInputDetectionTests\|AdminAuthServiceTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Services/*.cs`, `tests/GameKit.Admin.Tests/AdminAuditWriterTests.cs`, `PlayerSearchInputDetectionTests.cs` | pending |
| 03-06-T2 | 03-06 | 3 | ADMIN-03, ADMIN-04, ADMIN-05, ADMIN-06, ADMIN-10 | T-03-06-02, T-03-06-05, T-03-06-08 | Health probe + RingBuffer decay + SuperadminGate + AdminTestHost + PlayerBanService integration | integration (Testcontainers) | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter 'SuperadminGateTests\|PlayerBanServiceTests\|HealthProbeTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Services/HealthProbeService.cs`, `ErrorRateRingBuffer.cs`, `Authentication/SuperadminGateHostedService.cs`, `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`, `SuperadminGateTests.cs`, `PlayerBanServiceTests.cs`, `HealthProbeTests.cs` | pending |
| 03-06-T3 | 03-06 | 3 | ADMIN-03, ADMIN-04, ADMIN-05, ADMIN-06, ADMIN-10 | T-03-06-04, T-03-06-06, T-03-06-07 | AddGameKitAdmin SP-5 order + UseGameKitAdmin + MapGameKitAdmin (HTTP API prefix only; Blazor shell fixed at /admin) | build + integration | `dotnet build GameKit.sln -c Debug --nologo && dotnet test tests/GameKit.Admin.Integration.Tests/ --filter 'SuperadminGateTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`, `AdminApplicationBuilderExtensions.cs`, `Http/AdminEndpoints.cs` (stub) | pending |
| 03-07-T1 | 03-07 | 4 | ADMIN-02, ADMIN-05, ADMIN-06, ADMIN-07, ADMIN-08, ADMIN-12 | T-03-07-01..02 | Request DTOs + FluentValidation validators (3–512 char reason, etc.) | unit (TDD) | `dotnet test tests/GameKit.Admin.Tests/ --filter 'BanPlayerRequestValidatorTests\|CreateAdminRequestValidatorTests' --logger 'console;verbosity=minimal'` | `src/GameKit.Admin.UI/Http/Contracts/*.cs`, `Http/Validators/*.cs`, `tests/GameKit.Admin.Tests/BanPlayerRequestValidatorTests.cs`, `CreateAdminRequestValidatorTests.cs` | pending |
| 03-07-T2 | 03-07 | 4 | ADMIN-02, ADMIN-05, ADMIN-06, ADMIN-07, ADMIN-08, ADMIN-12 | T-03-07-03..05 | Full minimal-API surface — filters + authorization + rate limiting | build | `dotnet build src/GameKit.Admin.UI -c Debug --nologo` | `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` | pending |
| 03-07-T3 | 03-07 | 4 | ADMIN-02, ADMIN-05, ADMIN-06, ADMIN-07, ADMIN-08, ADMIN-12 | T-03-07-06..08 | Integration: login success + CSRF 400 + player search + ban flow + rate-limit 429 after 5 | integration | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter 'AdminLoginEndpointTests\|PlayerSearchEndpointTests\|BanFlowTests\|AntiforgeryTests' --logger 'console;verbosity=minimal'` | `tests/GameKit.Admin.Integration.Tests/AdminLoginEndpointTests.cs`, `PlayerSearchEndpointTests.cs`, `BanFlowTests.cs`, `AntiforgeryTests.cs` | pending |
| 03-08-T1 | 03-08 | 4 | ADMIN-01 | T-03-08-01..02 | Theme + _Imports + App.razor (nonce-aware) + Routes.razor compile | build | `dotnet build src/GameKit.Admin.UI -c Debug --nologo` | `src/GameKit.Admin.UI/GameKitAdminTheme.cs`, `Components/App.razor`, `Routes.razor`, `_Imports.razor`, `wwwroot/admin.css` | pending |
| 03-08-T2 | 03-08 | 4 | ADMIN-01 | T-03-08-03 | MainLayout + LoginLayout + TopNav + SideNav + MissingPackageAlert compile | build | `dotnet build src/GameKit.Admin.UI -c Debug --nologo` | `Components/Layout/MainLayout.razor`, `LoginLayout.razor`, `TopNav.razor`, `SideNav.razor`, `Components/Shared/MissingPackageAlert.razor` | pending |
| 03-09-T1 | 03-09 | 5 | ADMIN-05, ADMIN-07, ADMIN-08, ADMIN-09, ADMIN-10 | T-03-09-01..03, T-03-09-06 | Login + Dashboard + PlayerSearch + PlayerDetail + Ban/Unban/GDPR dialogs with CSRF token injection | build + grep-shape | `dotnet build src/GameKit.Admin.UI -c Debug --nologo && grep -l '@page "/admin/login"' ...` | `Components/Pages/Login.razor`, `Dashboard.razor`, `PlayerSearch.razor`, `PlayerDetail.razor`, `Dialogs/BanPlayerDialog.razor`, `UnbanPlayerDialog.razor`, `GdprDeleteDialog.razor` | pending |
| 03-09-T2 | 03-09 | 5 | ADMIN-05, ADMIN-07, ADMIN-08, ADMIN-09, ADMIN-10 | T-03-09-04..05 | Audit + Matches + Health (IAsyncDisposable) + QueueDepth/RankAdjust MissingPackageAlert + Admins + Create/DeleteAdmin dialogs | build + grep-shape | `dotnet build src/GameKit.Admin.UI -c Debug --nologo && grep -q '@page "/admin/audit"' ...` | `Components/Pages/Audit.razor`, `Matches.razor`, `Health.razor`, `QueueDepth.razor`, `RankAdjust.razor`, `Admins.razor`, `Dialogs/CreateAdminDialog.razor`, `DeleteAdminDialog.razor` | pending |
| 03-10-T1 | 03-10 | 5 | ADMIN-06 | T-03-10-01..03 | BannedCheckHelper + 4 provider patches + RefreshTokenService ban-check gate; Phase 2 regression-free | integration (live PG) | `dotnet test tests/GameKit.Auth.Tests/ && dotnet test tests/GameKit.Auth.Integration.Tests/` | `src/GameKit.Auth/Services/BannedCheckHelper.cs`; patched `SteamOAuthProvider.cs`, `DiscordOAuthProvider.cs`, `GuestOAuthProvider.cs`, `PasswordOAuthProvider.cs`, `RefreshTokenService.cs` | pending |
| 03-10-T2 | 03-10 | 5 | ADMIN-06 | T-03-10-04..05 | BanEnforcementTests covers all 4 providers + refresh family revoke | integration | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter 'BanEnforcementTests' --logger 'console;verbosity=minimal'` | `tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs` | pending |
| 03-11-T1 | 03-11 | 5 | ADMIN-11 | T-03-11-01..05 | AdminCreateCommand + Program branch + CLI csproj reference + non-TTY guard | unit + CLI smoke | `dotnet build GameKit.sln && dotnet run --project src/GameKit.Cli -- admin create --help` | `src/GameKit.Cli/Commands/AdminCreateCommand.cs`, `Program.cs`, `GameKit.Cli.csproj` | pending |
| 03-11-T2 | 03-11 | 5 | ADMIN-11 | T-03-11-01..05 | AdminCreateCommandTests — auto-promote + validation + duplicate | integration (live PG) | `dotnet test tests/GameKit.Cli.Tests/ --filter 'AdminCreateCommandTests' --logger 'console;verbosity=minimal'` | `tests/GameKit.Cli.Tests/AdminCreateCommandTests.cs` | pending |
| 03-12-T1 | 03-12 | 5 | ADMIN-02 | — | Sample Program.cs + csproj + README wiring | build | `dotnet build samples/TicTacToeDuel -c Debug --nologo` | `samples/TicTacToeDuel/Program.cs`, `TicTacToeDuel.csproj`, `README.md` | pending |
| 03-13-T1 | 03-13 | 6 | ADMIN-02, ADMIN-03, ADMIN-04 | T-03-13-01..02 | RoadmapScenarioTests (SC #1) + ProductionGateTests (SC #2) + MountPathTests (API prefix only) | integration | `dotnet test tests/GameKit.Admin.Integration.Tests/ --filter 'RoadmapScenarioTests\|ProductionGateTests\|MountPathTests' --logger 'console;verbosity=minimal'` | `tests/GameKit.Admin.Integration.Tests/RoadmapScenarioTests.cs`, `ProductionGateTests.cs`, `MountPathTests.cs` | pending |
| 03-13-T2 | 03-13 | 6 | ADMIN-09, ADMIN-10, ADMIN-12 | T-03-13-03..05 | CrossSchemeIsolationTests (SC #6) + CspAndAntiforgeryTests (SC #5) + PanelRenderTests (SC #4) | integration (full suite) | `dotnet test --logger 'console;verbosity=minimal'` | `tests/GameKit.Admin.Integration.Tests/CrossSchemeIsolationTests.cs`, `CspAndAntiforgeryTests.cs`, `PanelRenderTests.cs` | pending |

---

## Wave 0 Requirements

- [x] `tests/GameKit.Admin.Tests/GameKit.Admin.Tests.csproj` — unit test project (xUnit + Moq)
- [x] `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` — integration test project (Testcontainers + WebApplicationFactory)
- [x] `tests/GameKit.Admin.Integration.Tests/AdminIntegrationFixture.cs` — shared Testcontainers fixture mirroring Phase 2 `AuthIntegrationFixture` shape (Postgres + Redis + applied Core/Auth/Admin migrations)
- [x] `tests/GameKit.Admin.Integration.Tests/WebApplicationFactoryExtensions.cs` — helpers for admin-cookie acquisition + anti-CSRF token harvest
- [x] `tests/GameKit.Admin.Integration.Tests/Mocks/FakePlayerJwtIssuer.cs` — stable player JWT source for success-criterion #6 (JWT-cannot-auth-to-admin)

All five artifacts are created by plan 03-01 (Wave 0). Every downstream test task references these Wave 0 outputs via `AdminTestHost.StartAsync(...)` or direct `AdminIntegrationFixture` dependency.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| MudBlazor DataGrid visual polish on Chrome/Firefox/Safari latest | ADMIN-01 | No cross-browser automation harness in Phase 3 scope | Launch sample app, navigate to `/admin/players`, verify column alignment, sticky header, sort icons, keyboard focus rings |
| Admin login form UX (tab order, autofocus, password field does not autocomplete=on) | ADMIN-04 | Accessibility + UX judgement calls beyond unit test reach | Manual login screen walkthrough, confirm username autofocus, Tab → password → Enter submits |
| CSP violation reporter picks (RESEARCH.md open question #2) | ADMIN-12 | Policy decision — whether to emit `report-to` header in v1 | Operator policy statement in SUMMARY.md; no automated test |
| First-admin bootstrap message operator-friendliness (D-08 prints username + role + hashed-credential prefix) | ADMIN-11 | Message wording is subjective | Run `dotnet gamekit admin create`, verify output is scannable and the password prompt does not echo |
| 03-12-T2 — end-to-end sample app smoke (browse to `/admin/login`, bootstrap admin, search by identity, ban, confirm audit row) | ADMIN-02 | checkpoint:human-verify in plan 03-12; SC #1 anchor is also automated in 03-13 but operator-facing experience needs eyes | See `03-12-PLAN.md` Task 2 `<how-to-verify>` block |

---

## Success-Criteria → Test Mapping (from ROADMAP SC #1–#6)

| SC | Description | Test Type | File |
|----|-------------|-----------|------|
| #1 | Operator can `MapGameKitAdmin`, bootstrap admin via CLI, log in with a distinct scheme, search by id / display_name / provider:external_id | E2E WebApplicationFactory | `tests/GameKit.Admin.Integration.Tests/RoadmapScenarioTests.cs` |
| #2 | Unauthenticated `/admin` in Production returns 404 (not 401); startup throws when no superadmin exists | Integration + startup unit test | `tests/GameKit.Admin.Integration.Tests/ProductionGateTests.cs` + `SuperadminGateTests.cs` |
| #3 | Ban with reason writes `admin_audit_log`; banned player blocked at Auth; unban symmetric | Integration (multi-package) | `tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs` (in Admin integration project, touching `GameKit.Auth`) |
| #4 | Match-history, health, rank-adjust, queue-depth panels render without error; Rankings/Matchmaking placeholders when absent | Integration + bUnit-optional | `tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs` |
| #5 | CSRF + CSP integration tests — mutation without token returns 400; admin pages ship CSP header blocking framing | Integration | `tests/GameKit.Admin.Integration.Tests/CspAndAntiforgeryTests.cs` |
| #6 | Player JWT cannot authenticate into any admin endpoint (404/403 regardless of valid player token) | Integration | `tests/GameKit.Admin.Integration.Tests/CrossSchemeIsolationTests.cs` |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (test projects, fixtures, helpers listed above)
- [x] No watch-mode flags in CI-bound commands
- [x] Feedback latency < 120s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending (awaiting checker re-verification after this revision pass)
