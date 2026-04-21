---
phase: 03-admin-ui
plan: 08
subsystem: admin-ui/shell
tags: [blazor-server, mudblazor, layout, theming, csp-nonce]
requires:
  - "GameKit.Admin.UI.Middleware.AdminCspNonceMiddleware.NonceItemKey (plan 03-05)"
  - "GameKit.Admin.UI.Builder.UseGameKitAdmin + MapGameKitAdmin (plan 03-06)"
  - "GameKit.Admin.UI.Authorization.AdminRoles.Superadmin (plan 03-03)"
  - "MudBlazor 9.3.0 (Directory.Packages.props, plan 03-01)"
provides:
  - "GameKit.Admin.UI.GameKitAdminTheme.Default (MudTheme with UI-SPEC §Color palette)"
  - "wwwroot/gamekit-admin.css (--gk-color-* + --gk-space-* CSS custom properties)"
  - "Components/App.razor (HTML document root, nonce-threaded <script> tags)"
  - "Components/Routes.razor (Router + AuthorizeRouteView + FocusOnNavigate h1)"
  - "Components/Layout/MainLayout.razor (MudThemeProvider + MudLayout + TopNav + SideNav + drawer)"
  - "Components/Layout/LoginLayout.razor (blank shell centered card, no top nav / sidebar)"
  - "Components/Layout/TopNav.razor (AppBar with brand, toggle, env chip, user menu with Log out)"
  - "Components/Layout/SideNav.razor (8-item NavMenu; AuthorizeView wraps superadmin-only items)"
  - "Components/Shared/EnvironmentChip.razor"
  - "Components/Shared/StatusChip.razor"
  - "Components/Shared/KeysetPaginator.razor"
  - "Components/Shared/MissingPackageAlert.razor"
affects:
  - "MapGameKitAdmin: now calls MapRazorComponents<Components.App>().AddInteractiveServerRenderMode().WithStaticAssets()"
tech-stack:
  added:
    - "MudBlazor 9.3.0 PaletteLight / LayoutProperties / Typography (verified via reflection against ~/.nuget/packages/mudblazor/9.3.0/lib/net10.0/MudBlazor.dll)"
    - "Blazor Server interactive render mode mount via MapRazorComponents<App>()"
    - "Blazor CSS isolation (*.razor.css) with ::deep selector for nested MudBlazor anchor styling"
  patterns:
    - "Per-request CSP nonce read from HttpContext.Items[AdminCspNonceMiddleware.NonceItemKey] and threaded via @Nonce on every <script> tag"
    - "AuthorizeView Roles=\"@AdminRoles.Superadmin\" for defense-in-depth superadmin UI gating (backend policy is authoritative)"
    - "CSS custom properties on .gk-admin-root expose UI-SPEC color + spacing tokens to non-MudBlazor markup"
    - "System font stack (no Google Fonts CDN) — CLAUDE.md zero-cloud-dep constraint"
key-files:
  created:
    - "src/GameKit.Admin.UI/GameKitAdminTheme.cs"
    - "src/GameKit.Admin.UI/wwwroot/gamekit-admin.css"
    - "src/GameKit.Admin.UI/Components/App.razor"
    - "src/GameKit.Admin.UI/Components/Routes.razor"
    - "src/GameKit.Admin.UI/Components/_Imports.razor"
    - "src/GameKit.Admin.UI/Components/Layout/MainLayout.razor"
    - "src/GameKit.Admin.UI/Components/Layout/MainLayout.razor.css"
    - "src/GameKit.Admin.UI/Components/Layout/LoginLayout.razor"
    - "src/GameKit.Admin.UI/Components/Layout/LoginLayout.razor.css"
    - "src/GameKit.Admin.UI/Components/Layout/TopNav.razor"
    - "src/GameKit.Admin.UI/Components/Layout/TopNav.razor.css"
    - "src/GameKit.Admin.UI/Components/Layout/SideNav.razor"
    - "src/GameKit.Admin.UI/Components/Layout/SideNav.razor.css"
    - "src/GameKit.Admin.UI/Components/Shared/EnvironmentChip.razor"
    - "src/GameKit.Admin.UI/Components/Shared/StatusChip.razor"
    - "src/GameKit.Admin.UI/Components/Shared/KeysetPaginator.razor"
    - "src/GameKit.Admin.UI/Components/Shared/MissingPackageAlert.razor"
  modified:
    - "src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs"
decisions:
  - "Verified MudBlazor 9.3.0 theme API by reflection before coding: PaletteLight uses MudColor-typed slots (Primary, Background, Surface, TextPrimary, Error, Success, Warning, Info, Divider, LinesDefault, TableHover, TableLines) and String-typed *Darken/*Lighten slots (PrimaryDarken, PrimaryLighten, ErrorLighten, SuccessLighten, WarningLighten, InfoLighten). DefaultTypography + H1Typography exist as distinct sub-types of BaseTypography. LayoutProperties exposes DrawerWidthLeft / DrawerMiniWidthLeft / AppbarHeight / DefaultBorderRadius. No property names required substitution — plan's pseudocode compiled as-is."
  - "MudSnackbarProvider in 9.3.0 does NOT expose Position or MaxDisplayed parameters — the provider is a pure provider sink; snackbar positioning/limits are configured via AddMudServices(cfg => cfg.SnackbarConfiguration.*) in the DI wiring. The plan's `<MudSnackbarProvider Position=... MaxDisplayed=.../>` attributes would silently land in UserAttributes (CaptureUnmatchedValues) without effect. Removed the attributes; DI-side configuration is a follow-up (plan 03-04 / 03-06 enhancement)."
  - "Logout flow: TopNav posts to /admin/api/logout via JSInterop fetch + forceLoad navigation to /admin/login. Antiforgery token threading is not yet wired in the shell; the login/logout plan (03-09) is expected to add the header. A try/catch around the JS call tolerates clicks before the circuit attaches."
  - "MainLayout owns drawer Open/Close state via a private field; TopNav receives an EventCallback parameter (OnToggleDrawer) so the toggle icon lives in the app bar while state stays single-sourced. Cookie-based persistence (gk_admin_sidebar) mentioned in UI-SPEC is deferred to a later plan."
  - "AuthorizeView wraps both the Rank-adjust and Admins nav items (superadmin-only). The MudDivider sits between them per UI-SPEC §Sidebar — the divider renders unconditionally; only the superadmin links it separates are hidden for non-superadmins."
  - "Routes.razor uses typeof(GameKit.Admin.UI.AdminUiMarker).Assembly — AdminUiMarker is internal but same-assembly access from the Razor source-generated class works."
metrics:
  duration_min: 32
  completed_at: 2026-04-19T14:20:26Z
  commits:
    - "61179c7: feat(03-08 t1) — Blazor shell + theme + nonce threading"
    - "7bd72cf: feat(03-08 t2) — Layouts + nav + shared components"
---

# Phase 03 Plan 08: Admin UI Shell (Blazor Server)

Blazor Server shell for the GameKit admin console — the HTML document, the router, the theme, two layouts, four shared components, and the `MapRazorComponents<App>()` wire-up. No page components ship in this plan (plan 03-09 drops Login / Dashboard / PlayerSearch / PlayerDetail / Audit / Matches / Health / QueueDepth / RankAdjust / Admins into the shell).

## What Shipped

### 1. Theme + global CSS

- `GameKitAdminTheme.Default` — a `MudTheme` singleton that encodes the UI-SPEC §Color palette exactly: indigo-600 primary (`#4263EB`), slate neutrals, red-600 danger, amber warning, green success, blue info. 6px border radius, 240px drawer (64px mini), 56px app bar, system font stack, 14px body + 20px H1.
- `wwwroot/gamekit-admin.css` — exposes 20+ `--gk-color-*` and `--gk-space-*` CSS custom properties on `.gk-admin-root`, plus a global `:focus-visible { outline: 2px solid var(--gk-color-focus) }` and a `@media (max-width: 1023px)` viewport warning per UI-SPEC §Min viewport. `font-variant-numeric: tabular-nums` is applied to tables for ID alignment.

### 2. Root + router

- `Components/App.razor` — HTML5 document with:
  - Exactly three stylesheets: `_content/MudBlazor/MudBlazor.min.css`, `_content/GameKit.Admin.UI/GameKit.Admin.UI.bundle.scp.css` (CSS-isolation bundle), `_content/GameKit.Admin.UI/gamekit-admin.css` (shell tokens). **No CDN references anywhere.**
  - `<script src="_framework/blazor.web.js" nonce="@Nonce">` + `<script src="_content/MudBlazor/MudBlazor.min.js" nonce="@Nonce">`. `Nonce` is read via `IHttpContextAccessor.HttpContext.Items[AdminCspNonceMiddleware.NonceItemKey]`.
- `Components/Routes.razor` — `<Router>` pinned to `typeof(AdminUiMarker).Assembly`; `<AuthorizeRouteView>` with `DefaultLayout=MainLayout`; `<NotAuthorized>` renders a forbidden-style MudAlert; `<FocusOnNavigate Selector="h1">` satisfies the UI-SPEC §Interaction Contract focus-on-navigate rule.
- `Components/_Imports.razor` — shared usings including `MudBlazor`, `Microsoft.AspNetCore.Components.Authorization`, `GameKit.Admin.UI.*`, and the three component sub-namespaces (`Components`, `Components.Layout`, `Components.Shared`) so unqualified component tags resolve.

### 3. Layouts

- `MainLayout.razor` — mounts the four singletons exactly once at the root (`MudThemeProvider` + `MudPopoverProvider` + `MudDialogProvider` + `MudSnackbarProvider`) to avoid the double-dialog / double-snackbar pitfall (UI-SPEC §7). Renders `<MudLayout>` with `<TopNav OnToggleDrawer="ToggleDrawer">` + `<MudDrawer @bind-Open="_drawerOpen" Variant=Persistent Width=240px MiniWidth=64px>` + `<MudMainContent><div class="gk-content">@Body</div></MudMainContent>`. Drawer state is owned by MainLayout.
- `LoginLayout.razor` — blank shell per UI-SPEC §1: no top nav, no sidebar. Centered 400px card on `bg` with 1-level shadow; plan 03-09 drops the actual form in.

### 4. Navigation

- `TopNav.razor` — `MudAppBar Dense Elevation=0 Color=Surface` with brand mark → menu-toggle `MudIconButton` → `<MudSpacer />` → `EnvironmentChip` (non-prod only via `!HostEnv.IsProduction()`) → `AuthorizeView.Authorized` user menu containing username header + `Log out` `MudMenuItem`. Logout POSTs to `/admin/api/logout` via JSInterop then navigates to `/admin/login`.
- `SideNav.razor` — 8 `MudNavLink` items in authoritative UI-SPEC §Sidebar order:

  | # | Label         | Href                      | Icon                 | Gating     |
  |---|---------------|---------------------------|----------------------|------------|
  | 1 | Dashboard     | `/admin`                  | `Home`               | admin      |
  | 2 | Players       | `/admin/players`          | `PersonSearch`       | admin      |
  | 3 | Match history | `/admin/matches`          | `History`            | admin      |
  | 4 | Audit log     | `/admin/audit`            | `ReceiptLong`        | admin      |
  | 5 | Health        | `/admin/health`           | `Favorite`           | admin      |
  | 6 | Queue depth   | `/admin/matchmaking`      | `Queue`              | admin      |
  | 7 | Rank adjust   | `/admin/rankings/adjust`  | `Tune`               | superadmin |
  | — | (divider)     | —                         | —                    | —          |
  | 8 | Admins        | `/admin/admins`           | `AdminPanelSettings` | superadmin |

  Items 7 and 8 wrap in `<AuthorizeView Roles="@AdminRoles.Superadmin">`. Scoped `::deep` CSS styles the active state (`primary-subtle` bg, 3px `primary` left border) and hover state (`surface-alt` bg).

### 5. Shared components

- `EnvironmentChip` — `MudChip<string> Variant=Filled Size=Small Color=Info` showing the env name. `[Parameter, EditorRequired] Env`.
- `StatusChip` — `MudChip<string>` with color mapped via pattern-match: OK/Active/Healthy/Online → Success; Degraded/Warning → Warning; Down/Offline/Error/Banned → Error; else → Info. `role="status"` + `aria-label="@Status"` per UI-SPEC §Interaction Contract.
- `KeysetPaginator` — outlined FullWidth `MudButton`. Shows `Load {PageSize} more` when `HasMore`; disabled `End of results` otherwise; `IsLoading` swaps the label for a `MudProgressCircular` + `Loading…`. `OnLoadMore` `EventCallback`.
- `MissingPackageAlert` — `MudAlert Severity=Info Variant=Outlined` with exact UI-SPEC §11 copy: heading `{PackageName} not installed`, body `Install GameKit.{PackageName} and add .Add{PackageName}(…) to your service registration to enable {Feature}.`.

### 6. `MapGameKitAdmin` extension

`Builder/AdminApplicationBuilderExtensions.cs` — `MapGameKitAdmin` now calls:

```csharp
routes.MapRazorComponents<Components.App>()
      .AddInteractiveServerRenderMode()
      .WithStaticAssets();
```

after mounting the `/admin/api` HTTP endpoint group. The Blazor console is mounted at root-relative `/admin/*` regardless of `GameKitAdminOptions.MountPath` (CLAUDE.md scope note: `MountPath` scopes only the HTTP API prefix; Blazor Server page routes are static `@page` directives).

## Theme palette actually shipped

Exact values in `GameKitAdminTheme.Default.PaletteLight`:

| Slot                   | Hex       | UI-SPEC Token               |
|------------------------|-----------|-----------------------------|
| `Primary`              | `#4263EB` | `--gk-color-primary`        |
| `PrimaryDarken`        | `#364FC7` | `--gk-color-primary-hover`  |
| `PrimaryLighten`       | `#EDF2FF` | `--gk-color-primary-subtle` |
| `Background`           | `#F8FAFC` | `--gk-color-bg`             |
| `Surface`              | `#FFFFFF` | `--gk-color-surface`        |
| `AppbarBackground`     | `#FFFFFF` | (surface)                   |
| `AppbarText`           | `#0F172A` | `--gk-color-text-primary`   |
| `DrawerBackground`     | `#FFFFFF` | `--gk-color-surface`        |
| `DrawerText`           | `#0F172A` | `--gk-color-text-primary`   |
| `TextPrimary`          | `#0F172A` | `--gk-color-text-primary`   |
| `TextSecondary`        | `#475569` | `--gk-color-text-secondary` |
| `TextDisabled`         | `#94A3B8` | `--gk-color-text-disabled`  |
| `Error`                | `#DC2626` | `--gk-color-danger`         |
| `ErrorLighten`         | `#FEE2E2` | `--gk-color-danger-subtle`  |
| `Success`              | `#16A34A` | `--gk-color-success`        |
| `SuccessLighten`       | `#DCFCE7` | `--gk-color-success-subtle` |
| `Warning`              | `#D97706` | `--gk-color-warning`        |
| `WarningLighten`       | `#FEF3C7` | `--gk-color-warning-subtle` |
| `Info`                 | `#2563EB` | `--gk-color-info`           |
| `InfoLighten`          | `#DBEAFE` | `--gk-color-info-subtle`    |
| `TableLines`           | `#E2E8F0` | `--gk-color-border`         |
| `TableHover`           | `#F1F5F9` | `--gk-color-surface-alt`    |
| `Divider`              | `#E2E8F0` | `--gk-color-border`         |
| `LinesDefault`         | `#E2E8F0` | `--gk-color-border`         |

Every value matches UI-SPEC §Color exactly.

## MudBlazor 9.3.0 API adjustments

Reflection probe against `~/.nuget/packages/mudblazor/9.3.0/lib/net10.0/MudBlazor.dll` was run before coding to verify names. **Every property name in the plan's pseudocode compiled as-is — no substitutions needed** — with two notable behavioral findings:

| Plan pseudocode                                                                 | Actual                                                                                                                                                                                  | Adjustment                                                                                                       |
|---------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `<MudSnackbarProvider Position=... MaxDisplayed=.../>`                          | `MudSnackbarProvider` in 9.3.0 exposes `RightToLeft` only (inherited `Class`, `Style`, `Tag`, `FieldId`, `UserAttributes`). Position + MaxDisplayed are `SnackbarConfiguration` members. | Removed the attributes; documented that snackbar config moves to `AddMudServices(...)` in DI (follow-up plan).   |
| `Defaults.Classes.Position.BottomRight`                                         | String constant with value `"mud-snackbar-location-bottom-right"`                                                                                                                       | Not used in final code (see row above) — verified for completeness.                                              |
| `PrimaryDarken`, `PrimaryLighten`, `ErrorLighten`, `SuccessLighten`, etc.       | `String` typed, not `MudColor` (hex string literals assign without conversion).                                                                                                         | No change needed.                                                                                                |
| `MudChip Variant=...`                                                           | `MudChip<T>` is generic — requires type param.                                                                                                                                          | Used `MudChip T="string"` in EnvironmentChip + StatusChip.                                                       |
| `Typo.h6`                                                                       | Enum value is lowercase `h6` — `Typo.h6` resolves correctly.                                                                                                                            | No change needed.                                                                                                |

Verified icons (all resolved against `MudBlazor.Icons.Material.Filled`): `Home`, `PersonSearch`, `History`, `ReceiptLong`, `Favorite`, `Queue`, `Tune`, `AdminPanelSettings`, `MenuOpen`, `AccountCircle`, `Logout`, `Info`. UI-SPEC §FLAG-2 Material Symbols subset concern is resolved — all referenced glyphs ship in MudBlazor 9.3.0.

## Zero CDN / external asset refs confirmed

```
grep -E 'https://|http://|fonts\.googleapis|cdn\.|jsdelivr|unpkg' src/GameKit.Admin.UI/Components/**/*.razor*
  → No matches found
grep -E 'https://|http://' src/GameKit.Admin.UI/wwwroot/gamekit-admin.css
  → No matches found
```

All stylesheet + script references are `_content/...` or `_framework/...`, both local. Font stack is system fonts only. UI-SPEC §Asset Loading discipline holds.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] Forward-reference to MainLayout in Task 1**

- **Found during:** Task 1 build
- **Issue:** `Routes.razor` (Task 1 scope) references `typeof(Layout.MainLayout)`, but `MainLayout.razor` is scoped to Task 2. Task 1 alone did not compile (`CS0246: The type or namespace 'Layout' could not be found`).
- **Fix:** Created a placeholder `Components/Layout/MainLayout.razor` in Task 1 (`@inherits LayoutComponentBase @Body` + a header comment explaining Task 2 overwrites it). Task 2 then replaces the placeholder with the full MudThemeProvider + MudLayout + TopNav + SideNav + MudMainContent tree.
- **Files modified:** `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor` (created in Task 1 commit, rewritten in Task 2 commit).
- **Commit:** 61179c7 (Task 1) + 7bd72cf (Task 2 rewrite).

### Rule 2 — added critical functionality not explicit in plan

**2. `@using GameKit.Admin.UI.Components.{Shared,Layout}` in `_Imports.razor`**

- **Issue:** Without these usings, `TopNav.razor` cannot resolve `<EnvironmentChip>` and `Routes.razor` cannot resolve `Layout.MainLayout` by short name. Build fails with `RZ10012: Found markup element with unexpected name 'EnvironmentChip'`.
- **Fix:** Added three additional `@using` directives to `_Imports.razor`: `GameKit.Admin.UI.Components`, `GameKit.Admin.UI.Components.Layout`, `GameKit.Admin.UI.Components.Shared`.
- **Files modified:** `src/GameKit.Admin.UI/Components/_Imports.razor`
- **Commit:** 7bd72cf (Task 2).

**3. Viewport-warning markup rendered (not just CSS)**

- **Issue:** Plan only mentions the `@media (max-width: 1023px)` CSS rule; without a DOM node for `.gk-viewport-warning`, the rule has nothing to style. UI-SPEC §Min viewport explicitly states the banner copy ("GameKit Admin is designed for desktop. Some views may overflow.").
- **Fix:** Added a `<div class="gk-viewport-warning" role="status">` inside `MainLayout.razor`'s content wrapper with the exact UI-SPEC copy. CSS keeps `display: none` above 1024px.
- **Files modified:** `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor`, `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css`.

**4. `MudPopoverProvider` added alongside other providers**

- **Issue:** MudBlazor 9.x requires `MudPopoverProvider` for menu / tooltip popovers to render correctly alongside `MudThemeProvider`. Plan mentions the three classic providers; `MudPopoverProvider` is the implicit fourth in 9.x.
- **Fix:** Added `<MudPopoverProvider />` after `<MudThemeProvider>` in both `MainLayout.razor` and `LoginLayout.razor`.

These are UI shell correctness requirements — without them the shell does not render, the viewport banner never appears, or tooltips/menus fail silently.

## Known Stubs

- `Components/Layout/MainLayout.razor` — Task 1's intermediate form (1-line `@Body` passthrough) existed only between commits 61179c7 and 7bd72cf. Task 2's commit supersedes it with the real layout. No stub remains at HEAD.
- Logout flow (`TopNav.OnLogoutAsync`) calls `/admin/api/logout` without an antiforgery header — the antiforgery token wiring is explicitly deferred to plan 03-09 (login/logout page). The menu item is intentionally wired so the shell exposes the action; the 03-09 plan will layer CSRF token threading on top.

No stubs prevent the shell's goal (render a themed, authenticated Blazor Server admin page with a per-request nonce + role-gated navigation) — that goal is achieved. Page components (pages that consume the shell) are plan 03-09 scope.

## Success Criteria Results

- [x] App.razor reads nonce from HttpContext.Items and threads to every `<script>` — verified via `grep 'nonce="@Nonce"' App.razor` (2 hits).
- [x] MainLayout renders MudTheme + MudDialog + MudSnackbar providers once at root — plus MudPopoverProvider (MudBlazor 9.x addition).
- [x] LoginLayout is blank (no sidebar, no top nav) — `grep -E 'TopNav|SideNav|MudDrawer' LoginLayout.razor` returns nothing.
- [x] SideNav hides superadmin-only items via AuthorizeView — 2 `<AuthorizeView Roles="@AdminRoles.Superadmin">` blocks wrap Rank-adjust and Admins.
- [x] GameKitAdminTheme matches UI-SPEC §Color hex values exactly — palette table above.
- [x] gamekit-admin.css declares all UI-SPEC color + spacing tokens — 21 `--gk-color-*` and 7 `--gk-space-*` custom properties.
- [x] MapGameKitAdmin wires MapRazorComponents<App>().AddInteractiveServerRenderMode().WithStaticAssets() — confirmed in `AdminApplicationBuilderExtensions.cs` line grep.
- [x] Build succeeds with no CSP-incompatible CDN refs — `dotnet build GameKit.sln` → 0 warnings, 0 errors; CDN grep → zero hits.

## Self-Check: PASSED

Files verified present (all FOUND):

- `src/GameKit.Admin.UI/GameKitAdminTheme.cs`
- `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css`
- `src/GameKit.Admin.UI/Components/App.razor`
- `src/GameKit.Admin.UI/Components/Routes.razor`
- `src/GameKit.Admin.UI/Components/_Imports.razor`
- `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor`
- `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor.css`
- `src/GameKit.Admin.UI/Components/Layout/LoginLayout.razor`
- `src/GameKit.Admin.UI/Components/Layout/LoginLayout.razor.css`
- `src/GameKit.Admin.UI/Components/Layout/TopNav.razor`
- `src/GameKit.Admin.UI/Components/Layout/TopNav.razor.css`
- `src/GameKit.Admin.UI/Components/Layout/SideNav.razor`
- `src/GameKit.Admin.UI/Components/Layout/SideNav.razor.css`
- `src/GameKit.Admin.UI/Components/Shared/EnvironmentChip.razor`
- `src/GameKit.Admin.UI/Components/Shared/StatusChip.razor`
- `src/GameKit.Admin.UI/Components/Shared/KeysetPaginator.razor`
- `src/GameKit.Admin.UI/Components/Shared/MissingPackageAlert.razor`
- `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` (modified)

Commits verified in `git log`:

- `61179c7` — feat(03-08 t1): Blazor shell — App.razor + Routes + theme + global CSS + nonce threading
- `7bd72cf` — feat(03-08 t2): MainLayout + LoginLayout + TopNav + SideNav + shared components
