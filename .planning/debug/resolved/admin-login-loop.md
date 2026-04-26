---
slug: admin-login-loop
status: resolved
trigger: |
  Plan 03-12 human-verify checkpoint: GameKit Admin UI login at http://localhost:5000/admin/login
  appears to loop / "still stuck on login screen" after Sign In click. All 11 prior fixes
  (App.razor @rendermode, MapStaticAssets, Microsoft.AspNetCore.App.Internal.Assets package,
  HttpClient BaseAddress, CSP ws:/wss:, cookie SecurePolicy = SameAsRequest, etc.) are committed
  in 96b49c0. Symptom persists. A separate build break (OpenTelemetry NU1902 advisories) was
  unblocking work that landed before this session resumed.
created: 2026-04-25
updated: 2026-04-25
---

# Admin login loop after fixes 6-11

## Symptoms

### Expected
After typing `root` / `hunter2hunter2` on `/admin/login` and clicking "Sign in", the browser is
authenticated, navigated to `/admin` (admin dashboard), and `gk_admin_session` cookie is set.
Dashboard renders.

### Actual
User reports "still stuck on login screen" after clicking Sign In. No visible authenticated
state in browser. Dashboard does not render.

### Errors observed (server log on prior attempt)
- Page load + SignalR negotiate + WebSocket upgrade — succeeded
- `Login.razor` `OnInitializedAsync` ran (`AdminUsers.ListAsync` first-admin-missing check)
- `RemoteNavigationManager`: "Navigation failed when changing the location to /admin/" — `TaskCanceledException`
- Unhandled exception in circuit ... `TaskCanceledException`
- Browser console: "Uncaught (in promise) WebSocket is not in the OPEN state"
- `POST /admin/api/login` NOT visible in the captured log snippet — could be log truncation,
  could be the request never fired, could be the request fired and 401'd. Unconfirmed.

NOTE: `TaskCanceledException` is likely a red herring. `Login.razor:154` calls
`Nav.NavigateTo(..., forceLoad: true)` on success, which intentionally tears down the SignalR
circuit. The circuit teardown throws on whatever was mid-flight. So the cancel does NOT imply
a bug — it implies the success branch was taken (or something else navigated away).

### Timeline
Phase 3 plan 03-12 task 2 (human-verify walkthrough). Started ~2026-04-25 with sample run.
Has never reached an authenticated `/admin` view in this session.

### Reproduction
1. `./scripts/run-sample.sh --reset-db --bootstrap`
2. Open http://localhost:5000/admin/login in browser
3. Type: `root` / `hunter2hunter2`
4. Click "Sign in"
5. Observe: still on login screen (no dashboard)

## Stack at time of break

### Already verified in code (commit 96b49c0)
- `src/GameKit.Admin.UI/Components/App.razor:30` — `<Routes @rendermode="@RenderMode.InteractiveServer" />`
- `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj:17` — `Microsoft.AspNetCore.App.Internal.Assets` PackageReference
- `samples/TicTacToeDuel/Program.cs:79` — `app.MapStaticAssets()`
- `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:158-168` — HttpClient registered with
  `BaseAddress` derived from `HttpContext` (`scheme://host/PathBase`)
- `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:90` + `145` — both auth cookie and
  antiforgery cookie use `CookieSecurePolicy.SameAsRequest` (so cookies survive on dev HTTP)
- `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs:82` — `connect-src 'self' ws: wss:`

### Unblocking work that landed in this session (uncommitted)
- `Directory.Packages.props` — added central transitive pins for `OpenTelemetry.*` 1.15.3 (and
  `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2) to override `WireMock.Net.OpenTelemetry/2.2.0`'s
  pull of vulnerable 1.14.0 series. Build now clean (0 warnings / 0 errors).

### Known unknowns (the diagnostics needed)
1. Does `POST /admin/api/login` actually fire? What status does it return? (Server log after Sign In click.)
2. After Sign In click, browser DevTools → Application → Cookies → localhost:5000 — is
   `gk_admin_session` present? What are its `Secure` / `HttpOnly` / `SameSite` flags?
3. CSP response header on `GET /admin/login` — does `connect-src` include `ws: wss:` as expected?

## Current Focus

- hypothesis: The admin `HttpClient` registration at `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:161-170`
  resolves `BaseAddress` from `IHttpContextAccessor.HttpContext`. In Blazor Server interactive
  mode, the SignalR circuit has no `HttpContext` — that only exists during the initial HTTP
  request + its prerender pass. So `OnClick="SignInAsync"` runs over SignalR → `HttpContext`
  is null → line 164 throws `InvalidOperationException` → Blazor's circuit error boundary
  swallows it silently → no `POST /admin/api/login` ever leaves the server.
- test: Change line 161-170 to derive `BaseAddress` from `NavigationManager.BaseUri` instead.
  `NavigationManager` is scoped per-circuit and works in both prerender and interactive modes.
  Re-run sample, click Sign In, expect to see `POST /admin/api/login` in the server log and
  `gk_admin_session` cookie set in browser.
- expecting: After fix, server log shows `Request finished POST /admin/api/login - 200`. Browser
  cookie store has `gk_admin_session` (HttpOnly, Secure=false because `SameAsRequest` on HTTP).
  Browser navigates to `/admin` and dashboard renders.
- next_action: Apply the fix to `AdminBuilderExtensions.cs:161-170`. Rebuild + re-run sample.
  Capture same three diagnostics. If pass: write resolution. If fail: capture circuit error
  log (likely needs `Microsoft.AspNetCore.Components.Server.Circuits` set to Debug level) to
  see what else is being swallowed.
- reasoning_checkpoint: (none)
- tdd_checkpoint: (none)

## Evidence

- 2026-04-25 — Server log on Sign In click shows ONLY:
  - `POST /_blazor/negotiate` → 200
  - `GET /_blazor?id=...` (SignalR connection upgrade)
  - EF query `SELECT ... FROM gamekit.admin_users` (this is `OnInitializedAsync` → `AdminUsers.ListAsync()`)
  - **NO `POST /admin/api/login` line.** The login HTTP call never reaches the server's HTTP
    pipeline. Since the HttpClient is in-process (server calling its own Kestrel via BaseAddress
    `localhost:5000`), it WOULD show up in the server log if it fired.
- 2026-04-25 — Browser DevTools → Application → Cookies → localhost:5000 after Sign In click:
  only `gk_admin_csrf` present. `gk_admin_session` absent. Confirms the auth cookie was never
  set, which can only happen if `LoginEndpoint.SignInAsync` never ran.
- 2026-04-25 — Browser DevTools → Network panel after Sign In click: only SignalR negotiate +
  initializers requests visible. No outgoing request to `/admin/api/login`. (Correctly so —
  `Http.PostAsJsonAsync` runs server-side in Blazor Server; browser would never see it.)
- 2026-04-25 — `OnInitializedAsync`'s EF query DID run, which means the page IS hitting the
  database during the prerender pass. So database, ports, and DI are functional. The break is
  exclusively at the interactive-mode → HttpClient-resolution boundary.

## Eliminated

- "Page rendered statically (rendermode missing)" — eliminated. SignalR negotiate succeeded;
  the circuit IS interactive. `App.razor:30` has `@rendermode="@RenderMode.InteractiveServer"`.
- "Cookie blocked because Secure=true on HTTP" — eliminated. `gk_admin_csrf` cookie IS set and
  kept by the browser, proving cookie writes work in this dev configuration. The auth cookie
  is missing because the login endpoint never executed, not because the browser dropped it.
- "Build break / sample didn't start" — eliminated. EF query proves the sample ran end-to-end.
- "CSP blocking the SignalR upgrade" — eliminated. SignalR connected (we see `GET /_blazor`).

## Resolution

- root_cause: Two architectural defects compounded into the symptom, plus a third defect
  exposed downstream after the first two were fixed.

  **Defect 1 — Server-side HttpClient cannot propagate `Set-Cookie` to the browser.**
  `Login.razor` injected an `HttpClient` and POSTed `/admin/api/login` from inside the
  Blazor interactive circuit. Even with the URI / `BaseAddress` correctly resolved (initial
  hypothesis above was about `IHttpContextAccessor.HttpContext` being null in the SignalR
  scope; the deeper truth is that even *if* the call succeeded, the cookie was unreachable),
  the `Set-Cookie` header lands on the server's `HttpClient`, never on the browser. So the
  login flow could not work as designed regardless of how the BaseAddress was wired.
  Five other dialogs (`BanPlayerDialog`, `UnbanPlayerDialog`, `CreateAdminDialog`,
  `DeleteAdminDialog`, `GdprDeleteDialog`) followed the same pattern; each was guaranteed
  401 because the loopback HttpClient does not carry the user's auth cookie either.

  **Defect 2 — Default auth scheme was JwtBearer; admin cookie was a NAMED scheme only.**
  Admin minimal-API endpoints worked because `RequireAuthorization(policy)` re-authenticates
  against the policy's `AddAuthenticationSchemes(...)`. But Blazor's `AuthorizeRouteView`
  reads `HttpContext.User`, which was built by the default scheme (JwtBearer = anonymous in
  a browser request with no Bearer token). Every admin Razor page rendered its NotAuthorized
  branch despite a valid `gk_admin_session` cookie.

  **Defect 3 — SignalR transport `/_blazor` not covered by the cookie-auth path.** Once the
  path-based default scheme forwarded `/admin/*` to the cookie scheme, prerender worked but
  the interactive circuit booted anonymous: the SignalR negotiate at `/_blazor/negotiate`
  is NOT under `/admin/*`, so it fell through to JwtBearer and the principal captured at
  circuit-start was empty. Page would render correctly during prerender, then flash to
  NotAuthorized when the circuit took over.

  **Defect 4 (collateral) — `Dashboard.razor` ran 5 EF queries via `Task.WhenAll` on a
  single `DbContext`.** EF Core forbids concurrent operations on a single context. Was
  hidden because nobody had click-tested the dashboard in a browser during phase 03-09 sign-off.

- fix: Combined architectural correction:

  1. **Static-SSR login + dedicated form handler.** `Login.razor` now uses
     `[ExcludeFromInteractiveRouting]` and posts a real HTML form to `/admin/login/submit`
     (registered by the new `AdminFormEndpoints`). The browser makes the POST; the browser
     receives Set-Cookie. New `SignInCoreAsync` helper in `AdminEndpoints.cs` is shared
     between the JSON `/admin/api/login` (kept for SPA / programmatic clients) and the new
     form handler. Architecture rule documented at the top of `AdminEndpoints.cs`.
  2. **Dialogs refactored to call domain services via DI.** `BanPlayerDialog`,
     `UnbanPlayerDialog`, `CreateAdminDialog`, `DeleteAdminDialog`, `GdprDeleteDialog` now
     `@inject IPlayerBanService` / `IAdminUserService` / `IGdprDeleteService` etc. Validators
     called inline via `IValidator<T>`. Actor id pulled from `AuthenticationStateProvider`.
     Antiforgery + CSRF round-trip dropped (not needed in-process; the JSON `/admin/api/*`
     endpoints retain those protections for external callers).
  3. **`HttpClient` registration deleted entirely.** No future page can `@inject HttpClient`
     and walk back into Defect 1. Comment block at the deletion site explains the rule.
  4. **Path-based default authentication scheme.** New `GameKit:DefaultByPath` policy scheme
     in `AdminBuilderExtensions.cs` forwards to the admin cookie for `/admin/*` and
     `/_blazor/*` requests; everything else still forwards to JwtBearer (preserves Phase-2
     player-API behavior). `HttpContext.User` is now populated with admin claims for both
     prerender and SignalR-circuit lifetime.
  5. **Per-page rendermode via `[ExcludeFromInteractiveRouting]`.** `App.razor`'s `Routes`
     uses a conditional `PageRenderMode` based on `HttpContext.AcceptsInteractiveRouting()`
     (built into .NET 10's `Microsoft.AspNetCore.Components.Endpoints`). Pages without the
     attribute render interactively (default); Login renders statically.
  6. **Dashboard EF queries serialized.** `Task.WhenAll` over EF queries replaced with
     sequential `await`s; `HealthSvc.ProbeAsync` (which uses its own clients, not the shared
     DbContext) still runs concurrently with the EF queries.

  Plus collateral unblocking work: pinned `OpenTelemetry.*` to 1.15.x in
  `Directory.Packages.props` to clear `NU1902` advisories transitively pulled by
  `WireMock.Net.OpenTelemetry/2.2.0`.

- verification:
  - Solution-wide build: 0 warnings, 0 errors.
  - `GameKit.Admin.Tests` (unit): 54/54 passing.
  - `GameKit.Admin.Integration.Tests`: 37/37 passing — including 6 new form-login flow tests
    (`FormLogin_ValidCredentials_Returns302WithCookie`,
    `FormLogin_InvalidCredentials_Returns302ToErrorPage_NoSessionCookie`,
    `FormLogin_PreservesReturnUrl_OnFailureRedirect`,
    `FormLogin_HonorsSafeReturnUrl_OnSuccessRedirect`,
    `FormLogin_RejectsOpenRedirect_FallsBackToAdminRoot`,
    `FormLogin_MissingAntiforgeryToken_Returns302UnavailableError`).
  - Browser walk-through (`http://localhost:5000`):
    - GET `/admin/login` → static-SSR form renders.
    - Sign in as `root` / `hunter2hunter2` → POST `/admin/login/submit` → 302 to `/admin`.
    - `gk_admin_session` cookie present in browser.
    - Dashboard renders during prerender AND stays rendered after circuit takeover.
    - No NotAuthorized branch.

- files_changed:
  - `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` (deleted HttpClient registration;
    added path-based `DefaultByPathScheme`)
  - `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs` (wired `MapAdminFormEndpoints`)
  - `src/GameKit.Admin.UI/Components/App.razor` (per-page conditional rendermode)
  - `src/GameKit.Admin.UI/Components/_Imports.razor` (added `Microsoft.AspNetCore.Components.Endpoints`)
  - `src/GameKit.Admin.UI/Components/Pages/Login.razor` (rewritten as static SSR form)
  - `src/GameKit.Admin.UI/Components/Pages/Login.razor.css` (NEW; styles for static form)
  - `src/GameKit.Admin.UI/Components/Pages/Dashboard.razor` (Task.WhenAll → sequential)
  - `src/GameKit.Admin.UI/Components/Dialogs/BanPlayerDialog.razor` (refactored to DI service)
  - `src/GameKit.Admin.UI/Components/Dialogs/UnbanPlayerDialog.razor` (refactored to DI service)
  - `src/GameKit.Admin.UI/Components/Dialogs/CreateAdminDialog.razor` (refactored to DI service)
  - `src/GameKit.Admin.UI/Components/Dialogs/DeleteAdminDialog.razor` (refactored to DI service)
  - `src/GameKit.Admin.UI/Components/Dialogs/GdprDeleteDialog.razor` (refactored to DI service)
  - `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` (extracted `SignInCoreAsync` helper; added architecture-note comment block)
  - `src/GameKit.Admin.UI/Http/AdminFormEndpoints.cs` (NEW; POST `/admin/login/submit`)
  - `Directory.Packages.props` (OpenTelemetry 1.15.x central pins)
  - `tests/GameKit.Admin.Integration.Tests/AdminLoginEndpointTests.cs` (added 6 form-flow tests)

## Follow-ups (not part of this debug session)

- `Auth.Integration.Tests` show pre-existing `PendingModelChangesWarning` failures unrelated
  to this work — separate investigation.
- Plan 03-12 walkthrough doc has stale credentials (Password=owner vs gamekit_owner_dev) and
  wrong URL (https://localhost:5001 vs http://localhost:5000) — separate doc-update task.
- 03-11 CLI `admin create` requires the sample to be booted at least once so
  `AdminMigrationHostedService` creates `gamekit.admin_users`. Better: have the CLI's
  `migrate` command apply Admin migrations too. Separate plan-defect task.
- The `<NotAuthorized>` message in `Routes.razor:20` is hardcoded to mention "superadmin"
  even when the failing page only requires Admin role. Minor copy bug worth fixing.
