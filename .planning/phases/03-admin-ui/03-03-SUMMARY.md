---
phase: 03-admin-ui
plan: 03
subsystem: admin-ui
tags:
  - admin-ui
  - rcl
  - razor-sdk
  - mudblazor
  - options
  - constants
  - wave-1
dependencies:
  requires:
    - phase: 03-01
      provides: tests/GameKit.Admin.Tests + SmokeTests placeholder + Directory.Packages.props MudBlazor 9.3.0 pin
    - phase: 03-02
      provides: GameKit.Admin.UI csproj baseline (Phase-1/Plan-02 stub) + GameKit.Auth ProjectReference (W5)
  provides:
    - GameKit.Admin.UI as a Razor Class Library (Microsoft.NET.Sdk.Razor) with MudBlazor + FluentValidation + StackExchange.Redis package references
    - AdminUiMarker internal marker type (compile-time anchor for test assemblies via InternalsVisibleTo)
    - GameKitAdminOptions root options + nested AdminCookieOptions / AdminPanelOptions / AdminCspOptions (production-safe defaults)
    - Authorization/AdminRoles (admin, superadmin) + Authorization/AdminPolicies (gamekit.admin.admin, gamekit.admin.superadmin)
    - Authentication/AdminAuthenticationSchemeConstants (Scheme=GameKitAdmin, CookieName=gk_admin_session, CSRF header/cookie names)
  affects:
    - 03-04 (AdminCookieEvents wires AddAuthentication(AdminAuthenticationSchemeConstants.Scheme).AddCookie + reads AdminCookieOptions defaults)
    - 03-05 (AdminCspNonceMiddleware reads AdminCspOptions.ReportOnly; AntiforgeryValidationFilter reads AdminAuthenticationSchemeConstants.CsrfHeaderName/CsrfCookieName)
    - 03-06 (AddGameKitAdmin fluent builder consumes GameKitAdminOptions; SuperadminGateHostedService keys off AdminPolicies.Superadmin; AdminPolicies registered against the GameKitAdmin scheme)
    - 03-07 (/admin/api/* endpoints RequireAuthorization(AdminPolicies.Admin) or AdminPolicies.Superadmin)
    - 03-08 (Blazor RCL pages compile because of Razor SDK)
    - 03-13 (CrossSchemeIsolationTests assert player JWT cannot satisfy GameKitAdmin scheme; MountPathTests probe /admin prefix)
tech-stack:
  added:
    - "Microsoft.NET.Sdk.Razor SDK on GameKit.Admin.UI (replaces plain Microsoft.NET.Sdk so .razor files compile in plan 03-08)"
    - "MudBlazor 9.3.0 PackageReference on GameKit.Admin.UI (CPM pin already in Directory.Packages.props from plan 03-01)"
    - "FluentValidation + FluentValidation.DependencyInjectionExtensions PackageReferences on GameKit.Admin.UI (admin DTO validation in plan 03-07)"
    - "StackExchange.Redis PackageReference on GameKit.Admin.UI (live queue-depth panels in plans 03-06/03-09)"
    - "FrameworkReference Microsoft.AspNetCore.App on GameKit.Admin.UI (Cookies/Antiforgery shared-framework types)"
    - "AddRazorSupportForMvc=true property on GameKit.Admin.UI csproj (RCL Blazor Server compile-time requirement)"
  patterns:
    - "Per-package marker type pattern (SP-2 mirror): internal static class AdminUiMarker — paired with [assembly: InternalsVisibleTo] grants on AssemblyInfo.cs (mirrors GameKit.Auth.AuthMarker exactly)"
    - "Nested options tree pattern: root GameKitAdminOptions exposes Cookie/Panel/Csp subobjects (mirrors GameKitAuthOptions Jwt/Steam/Discord/Password subtree shape from plan 02-03)"
    - "Pinned-string constants pattern for roles + policies + scheme names (mirrors GameKitRateLimitPolicies from plan 02-07): every string literal that crosses a boundary lives in a public const, not inline"
    - "TDD RED-GREEN cycle for the constants/options task: tests written first (compile-fail RED proves test wiring); GREEN drop happens via 4 source files in one shot since the symbols are pure data with no behavior"
key-files:
  created:
    - src/GameKit.Admin.UI/GameKitAdminOptions.cs
    - src/GameKit.Admin.UI/Authorization/AdminRoles.cs
    - src/GameKit.Admin.UI/Authorization/AdminPolicies.cs
    - src/GameKit.Admin.UI/Authentication/AdminAuthenticationSchemeConstants.cs
    - tests/GameKit.Admin.Tests/GameKitAdminOptionsValidationTests.cs
  modified:
    - src/GameKit.Admin.UI/GameKit.Admin.UI.csproj
    - src/GameKit.Admin.UI/AssemblyInfo.cs
    - tests/GameKit.Admin.Tests/SmokeTests.cs
decisions:
  - "GameKit.Admin.UI promoted from plain Microsoft.NET.Sdk to Microsoft.NET.Sdk.Razor — required for Wave-4 plan 03-08 to compile .razor pages; AddRazorSupportForMvc=true property set per the RCL Blazor Server template"
  - "Existing Phase-1 csproj header preserved verbatim (PackageId, Description, PackageTags, RootNamespace, AssemblyName); EF Core + Design package references and Core/Auth ProjectReferences from plan 03-02 carried over unchanged into the new Razor SDK shape"
  - "AdminUiMarker is internal static (not public) — matches GameKit.Auth.AuthMarker convention; reachable from test assemblies via [assembly: InternalsVisibleTo(\"GameKit.Admin.Tests\")] + InternalsVisibleTo(\"GameKit.Admin.Integration.Tests\")"
  - "GameKitAdminOptions.MountPath default = \"/admin\" (CONTEXT-pinned) but plan-01 CLAUDE.md note records that MountPath only scopes the API prefix (/admin/api/*); Blazor @page routes and MudBlazor _content/* static assets remain root-relative for v1 — XML doc on the property documents this scope"
  - "AdminCookieOptions defaults: 8h sliding session + 30d remember-me window per CONTEXT discretion (matches UI-SPEC §Layout Shell); SlidingExpiration true by default"
  - "AdminPanelOptions defaults: 10s refresh interval + 5m error-rate window + 1s ring-buffer bucket — establishes baseline for D-10 health-tile bounds; plan 03-06 AddGameKitAdmin will enforce non-zero validation per T-03-03-04 mitigation"
  - "AdminCspOptions.ReportOnly default = false — no CSP-Report endpoint phone-home, matches \"install only what you need\"; plan 03-05 AdminCspNonceMiddleware hard-codes the enforce policy and reads ReportOnly purely as a defense-in-depth dev toggle"
  - "AdminRoles values are lowercase (admin, superadmin) — matches admin_users.role CHECK constraint values shipped in plan 03-02 migration (ck_admin_users_role IN ('admin','superadmin'))"
  - "AdminPolicies values are dotted lower-case (gamekit.admin.admin / gamekit.admin.superadmin) — namespaced under gamekit.admin.* to avoid collision with consumer-defined ASP.NET authorization policies"
  - "AdminAuthenticationSchemeConstants.Scheme = \"GameKitAdmin\" — distinct from JwtBearerDefaults.AuthenticationScheme (\"Bearer\") so a player JWT (Bearer scheme) cannot satisfy admin endpoints, satisfying ROADMAP SC #6; plan 03-13 CrossSchemeIsolationTests will assert this empirically"
  - "AdminAuthenticationSchemeConstants.CookieName mirrors AdminCookieOptions.Name default (\"gk_admin_session\") — the constant is the wire-protocol fixed default; the option lets a consumer override only if their host has a cookie collision (T-03-03-02 mitigation)"
metrics:
  duration_minutes: 14
  tasks_completed: 2
  files_created: 5
  files_modified: 3
  tests_passing:
    unit_validation: 4
    unit_smoke: 1
  completed_date: 2026-04-19
requirements_completed: []
---

# Phase 03 Plan 03: GameKit.Admin.UI Skeleton — RCL + Options + Constants Summary

Promoted `GameKit.Admin.UI` from a plain `Microsoft.NET.Sdk` library project to a full Blazor Server Razor Class Library: `Microsoft.NET.Sdk.Razor` SDK + MudBlazor / FluentValidation / StackExchange.Redis package references + `Microsoft.AspNetCore.App` framework reference. Added the `AdminUiMarker` internal type behind `InternalsVisibleTo` grants for both Admin test assemblies. Shipped the configuration surface (`GameKitAdminOptions` with nested `AdminCookieOptions` / `AdminPanelOptions` / `AdminCspOptions`) and three small constants classes (`AdminRoles`, `AdminPolicies`, `AdminAuthenticationSchemeConstants`) that plans 03-04 through 03-09 will consume verbatim. No runtime behavior — this plan exists purely so downstream plans have something to bind against.

## Performance

- **Duration:** ~14 min
- **Started:** 2026-04-19T04:15:00Z
- **Completed:** 2026-04-19T04:29:17Z
- **Tasks:** 2
- **Files created:** 5
- **Files modified:** 3
- **Tests added:** 4 unit (validation) + 1 unit smoke replacement (assertion strengthened)

## Task Commits

1. **Task 1: Rewrite csproj as RCL + AdminUiMarker** — `a614f3e` (feat)
2. **Task 2: GameKitAdminOptions + AdminRoles + AdminPolicies + scheme constants (TDD)** — `cc2cf49` (feat)

**Plan metadata:** _(this commit, see Final Commit below)_

## Before / After csproj diff

| Property / ItemGroup | Before (Phase-1 stub + Plan-02 EF additions) | After (Plan-03 RCL) |
|----------------------|------------------------------------------------|---------------------|
| `Sdk` | `Microsoft.NET.Sdk` | **`Microsoft.NET.Sdk.Razor`** |
| `AddRazorSupportForMvc` | _(absent)_ | `true` |
| `<FrameworkReference>` | _(absent)_ | `Microsoft.AspNetCore.App` |
| MudBlazor PackageReference | _(absent)_ | added |
| FluentValidation + DI extensions PackageReferences | _(absent)_ | added |
| StackExchange.Redis PackageReference | _(absent)_ | added |
| EF Core + Relational + Npgsql PackageReferences | present (from 03-02) | preserved |
| EF Core Design (PrivateAssets=all) | present (from 03-02) | preserved |
| Core + Auth ProjectReferences | present (from 03-02 W5) | preserved |
| `<PackageId>` / `<Description>` / `<PackageTags>` / `<RootNamespace>` / `<AssemblyName>` | present (Phase 1) | preserved verbatim |
| `<TargetFramework>` / `<Version>` | inherited from Directory.Build.props (MinVer + net10.0) | unchanged |

The csproj also drops two large XML comment blocks from plan 03-02 (W5 dependency-direction note + EF Core block comment) — they were Phase-1/Plan-02 narrative and are now redundant with this SUMMARY's decisions list and CLAUDE.md's GameKit.Admin.UI block. The behavioral shape is unchanged.

## GameKitAdminOptions surface — properties + defaults

| Property | Type | Default | Source / rationale |
|----------|------|---------|---------------------|
| `MountPath` | `string` (mutable) | `"/admin"` | CONTEXT-pinned API prefix; Blazor routes and `_content/*` are root-relative (B1 step 4 scope note in CLAUDE.md) |
| `Cookie` | `AdminCookieOptions` (init-only ref) | `new()` | Cookie subtree (4 properties below) |
| `Panel` | `AdminPanelOptions` (init-only ref) | `new()` | Health/queue panel subtree (3 properties below) |
| `Csp` | `AdminCspOptions` (init-only ref) | `new()` | CSP subtree (1 property below) |
| `Cookie.Name` | `string` (mutable) | `"gk_admin_session"` | Matches `AdminAuthenticationSchemeConstants.CookieName` (consumer can override on collision per T-03-03-02) |
| `Cookie.ExpireTimeSpan` | `TimeSpan` (mutable) | `8h` | UI-SPEC §Layout Shell + CONTEXT discretion (D-01) |
| `Cookie.SlidingExpiration` | `bool` (mutable) | `true` | UI-SPEC §Layout Shell |
| `Cookie.RememberMeDuration` | `TimeSpan` (mutable) | `30d` | CONTEXT discretion (D-01); cookie lifetime ceiling when remember-me is checked at login |
| `Panel.RefreshInterval` | `TimeSpan` (mutable) | `10s` | RESEARCH §Health Probe / D-10 baseline; plan 03-06 enforces `> TimeSpan.Zero` (T-03-03-04) |
| `Panel.HealthErrorRateWindow` | `TimeSpan` (mutable) | `5m` | RESEARCH §Health Probe rolling window |
| `Panel.HealthErrorRateBucketSize` | `TimeSpan` (mutable) | `1s` | RESEARCH §Health Probe ring-buffer bucket granularity |
| `Csp.ReportOnly` | `bool` (mutable) | `false` | "Install only what you need" — no phone-home; defense-in-depth toggle for local dev hardening |

## Pinned-string constants

| Constant | Value | Consumer |
|----------|-------|----------|
| `AdminRoles.Admin` | `"admin"` | `admin_users.role` CHECK (plan 03-02 migration); cookie role claim issued in plan 03-04 login |
| `AdminRoles.Superadmin` | `"superadmin"` | `admin_users.role` CHECK; superadmin-only endpoints in plan 03-07 |
| `AdminPolicies.Admin` | `"gamekit.admin.admin"` | `RequireAuthorization()` calls in plan 03-07 endpoints + plan 03-09 page authorization |
| `AdminPolicies.Superadmin` | `"gamekit.admin.superadmin"` | Same as above for superadmin-only paths |
| `AdminAuthenticationSchemeConstants.Scheme` | `"GameKitAdmin"` | `services.AddAuthentication(AdminAuthenticationSchemeConstants.Scheme).AddCookie(...)` in plan 03-04 |
| `AdminAuthenticationSchemeConstants.CookieName` | `"gk_admin_session"` | Cookie auth options + `LoginAsAdminAsync` test helper from plan 03-01 |
| `AdminAuthenticationSchemeConstants.CsrfHeaderName` | `"X-GameKit-Admin-CSRF"` | `IAntiforgery` header name in plan 03-05 + `HarvestAntiforgeryTokenAsync` from plan 03-01 |
| `AdminAuthenticationSchemeConstants.CsrfCookieName` | `"gk_admin_csrf"` | CSRF cookie name read by Blazor JS in plan 03-08 shell |

## Files Created / Modified (authoritative list)

### Created (5)

- `src/GameKit.Admin.UI/GameKitAdminOptions.cs` — root options + 3 nested option types (`AdminCookieOptions`, `AdminPanelOptions`, `AdminCspOptions`); 11 documented public properties total.
- `src/GameKit.Admin.UI/Authorization/AdminRoles.cs` — `public static class` with two `const string`s (`Admin`, `Superadmin`).
- `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` — `public static class` with two `const string`s.
- `src/GameKit.Admin.UI/Authentication/AdminAuthenticationSchemeConstants.cs` — `public static class` with four `const string`s.
- `tests/GameKit.Admin.Tests/GameKitAdminOptionsValidationTests.cs` — 4 `[Fact]`s: defaults table, `AdminRoles`, `AdminAuthenticationSchemeConstants`, `AdminPolicies`.

### Modified (3)

- `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` — full RCL rewrite per the table above.
- `src/GameKit.Admin.UI/AssemblyInfo.cs` — added `using System.Runtime.CompilerServices;`, two `[assembly: InternalsVisibleTo]` grants, and `internal static class AdminUiMarker { }` (was a 2-line SPDX-only file before).
- `tests/GameKit.Admin.Tests/SmokeTests.cs` — replaced `Assert.True(true)` placeholder with `Assert.NotNull(typeof(GameKit.Admin.UI.AdminUiMarker))` (proves InternalsVisibleTo grant + RCL compiles + load works).

## Variance from PATTERNS SP-2 shape

None. `AssemblyInfo.cs` matches `src/GameKit.Auth/AssemblyInfo.cs` line-for-line (modulo type name + namespace + InternalsVisibleTo target names). The marker type is `internal static class` (not `public`) per the Auth precedent. SPDX header on every new file. Public API XML doc on every public member.

## Test counts

| Project | Passed | Failed | Skipped | Notes |
|---------|--------|--------|---------|-------|
| `GameKit.Admin.Tests` (unit) | **5** | 0 | 0 | 1 smoke (strengthened from 03-01 placeholder) + 4 validation (this plan) |
| `GameKit.Admin.Integration.Tests` | 3 | 0 | 0 | unchanged from plan 03-02 (no changes here) |

Full solution build (`dotnet build GameKit.sln -c Debug --nologo`) — 17 projects, 0 warnings, 0 errors.

## Deviations from Plan

None — plan executed exactly as written. Both tasks completed with the literal code shown in the plan's `<action>` blocks, with one minor formatting variance:

- The plan's csproj template did not include the W5 / EF Core block comments from plan 03-02; the rewrite drops them deliberately (decisions list documents the carry-over). This matches the plan's "REWRITE" verb (not "EDIT").

No Rule-1/2/3 auto-fixes triggered. RED phase confirmed compile-fail (CS0234 on `GameKit.Admin.UI.Authentication` + `GameKit.Admin.UI.Authorization` namespaces) before GREEN drop. GREEN phase passed all 4 new tests on first run.

## Threat Flags

None. The threat register entries (T-03-03-01..04) are all `accept` or future-`mitigate` (T-03-03-04 enforced in plan 03-06 `AddGameKitAdmin` validator); this plan introduces no new surface beyond what `<threat_model>` already enumerated.

## Known Stubs

None. `GameKitAdminOptions` defaults are production-safe and immediately usable. No empty-collection bindings, no placeholder text in any public type.

## Self-Check: PASSED

Verification run after writing this SUMMARY:

- File existence checks (5 created):
  - `src/GameKit.Admin.UI/GameKitAdminOptions.cs` — FOUND
  - `src/GameKit.Admin.UI/Authorization/AdminRoles.cs` — FOUND
  - `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` — FOUND
  - `src/GameKit.Admin.UI/Authentication/AdminAuthenticationSchemeConstants.cs` — FOUND
  - `tests/GameKit.Admin.Tests/GameKitAdminOptionsValidationTests.cs` — FOUND
- Commit existence checks:
  - `a614f3e` — FOUND (task 1: feat — RCL csproj + AdminUiMarker)
  - `cc2cf49` — FOUND (task 2: feat — options + constants + 4 validation tests)
