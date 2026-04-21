---
phase: 03-admin-ui
plan: 05
subsystem: admin-ui
tags:
  - admin-ui
  - csp
  - nonce
  - antiforgery
  - csrf
  - endpoint-filter
  - middleware
  - wave-2
dependencies:
  requires:
    - phase: 03-03
      provides: "GameKitAdminOptions.MountPath (default /admin) — CSP middleware keys off it; AdminAuthenticationSchemeConstants is NOT consumed directly in this plan (antiforgery names bind in plan 03-06 when AddAntiforgery is wired)."
  provides:
    - "AdminCspNonceMiddleware: per-request 128-bit nonce + strict Content-Security-Policy header on every response under GameKitAdminOptions.MountPath (D-15 / ADMIN-12)"
    - "HttpContext.Items key constant AdminCspNonceMiddleware.NonceItemKey = \"gamekit.admin.csp-nonce\" — consumed by plan 03-08's App.razor via IHttpContextAccessor to thread the nonce into <script nonce=\"...\"> tags"
    - "AntiforgeryValidationFilter: stateless IEndpointFilter wrapping IAntiforgery.ValidateRequestAsync; returns 400 { error = \"csrf_validation_failed\" } on AntiforgeryValidationException (D-16 / ADMIN-12)"
    - "ValidationEndpointFilter<TRequest> in GameKit.Admin.UI.Http.EndpointFilters — verbatim copy of the GameKit.Auth analog (only namespace line differs) so admin endpoints do not cross-reference the Auth namespace"
  affects:
    - "03-06 (UseGameKitAdmin wires app.UseMiddleware<AdminCspNonceMiddleware>() + app.UseAntiforgery(); AddAntiforgery supplies IAntiforgery that this plan's filter resolves from RequestServices)"
    - "03-07 (mutation endpoints register .AddEndpointFilter<AntiforgeryValidationFilter>() BEFORE .AddEndpointFilter<ValidationEndpointFilter<TRequest>>() so CSRF fails before body deserialization)"
    - "03-08 (App.razor reads ctx.Items[AdminCspNonceMiddleware.NonceItemKey] via IHttpContextAccessor and emits <script nonce=@Nonce> tags — MudBlazor 9.3.0 ships no inline <script> tags so 'self' + nonce covers every JS load)"
    - "03-13 (end-to-end integration test asserts Content-Security-Policy header present on /admin/* responses; admin mutation without token returns 400)"
tech-stack:
  added: []
  patterns:
    - "Per-request CSP nonce pattern: RandomNumberGenerator.Fill(stackalloc byte[16]) + Convert.ToBase64String + HttpContext.Items stash + Response.OnStarting callback for header emission before body flush"
    - "PathString.StartsWithSegments gate on MountPath for admin-scoped middleware (pattern reusable by plan 03-06 when adding AdminNotFoundWhenUnauthorized middleware)"
    - "Stateless IEndpointFilter resolves dependencies from ctx.HttpContext.RequestServices rather than a constructor — allows AddEndpointFilter<TFilter>() type-based registration without DI-registered filter instances"
    - "Copy-verbatim filter pattern for cross-package generics: GameKit.Admin.UI.Http.EndpointFilters.ValidationEndpointFilter<T> is byte-identical to the GameKit.Auth analog modulo the namespace line (PATTERNS directive)"
    - "Test-only IHttpResponseFeature with independent Headers/StatusCode/Body storage + OnStarting capture/replay — enables unit-testing middleware that wires OnStarting callbacks without spinning up TestServer/Kestrel"
key-files:
  created:
    - src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs
    - src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs
    - src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs
    - tests/GameKit.Admin.Tests/AdminCspNonceMiddlewareTests.cs
    - tests/GameKit.Admin.Tests/AntiforgeryValidationFilterTests.cs
  modified: []
key-decisions:
  - "AdminCspNonceMiddleware is public sealed (not internal) — consistent with the plan's concrete code block. InternalsVisibleTo('GameKit.Admin.Tests') exists but the plan's constructor signature is public and is intended to be callable from any consumer that wants to place the middleware manually. The plan's test file uses `new(next, opts)` directly with no internal access."
  - "Chose not to forward test-only HttpResponseFeature to the live HttpContext's Headers/StatusCode — instead owns independent storage. The stock IHttpResponseFeature installed by DefaultHttpContext returns the same feature when queried via ctx.Response.Headers (forwarding would infinite-recurse). Independent storage is safe because the assertions read through ctx.Response.Headers which resolves to the feature we installed."
  - "Full CSP policy string is hard-coded inside AdminCspNonceMiddleware per plan spec (D-15). AdminCspOptions.ReportOnly exists as a future-hook surface but is NOT consumed in this plan — wiring the Report-Only companion header lands in a later plan along with any consumer-provided report-uri."
  - "OnStarting callbacks fire in reverse registration order in the test feature — matches Kestrel's actual semantics so the test exercises the same ordering guarantee the production pipeline provides."
requirements-completed:
  - ADMIN-12
duration: ~20min
completed: 2026-04-19
---

# Phase 03 Plan 05: CSP Nonce Middleware + Antiforgery Endpoint Filter Summary

**Per-request 128-bit CSP nonce middleware scoped to MountPath + stateless antiforgery IEndpointFilter returning 400 csrf_validation_failed + verbatim ValidationEndpointFilter<T> copy — ADMIN-12 Wave-2 security primitives ready for plan 03-06 pipeline wiring.**

## Performance

- **Duration:** approximately 20 min
- **Started:** 2026-04-19T13:07:00Z (approximate)
- **Completed:** 2026-04-19T13:27:00Z
- **Tasks:** 2 (both `type="auto"` with `tdd="true"`)
- **Files created:** 5
- **Files modified:** 0
- **Unit tests added:** 6 (4 CSP + 2 Antiforgery)

## Accomplishments

- `AdminCspNonceMiddleware` — 128-bit `RandomNumberGenerator.Fill(stackalloc byte[16])` nonce generated per admin request, stored under `HttpContext.Items["gamekit.admin.csp-nonce"]`, and a strict `Content-Security-Policy` header emitted via `Response.OnStarting`. Non-admin paths pass through untouched (early return on `PathString.StartsWithSegments(MountPath)` miss).
- `AntiforgeryValidationFilter` — single-purpose stateless `IEndpointFilter` that resolves `IAntiforgery` from `ctx.HttpContext.RequestServices`, awaits `ValidateRequestAsync`, and on `AntiforgeryValidationException` returns `Results.BadRequest(new { error = "csrf_validation_failed" })`. On success, delegates to `next`.
- `ValidationEndpointFilter<TRequest>` copied verbatim from `GameKit.Auth.Http.EndpointFilters` — only the `namespace` line differs. `diff` output confirmed as a single-line delta.
- 6 new unit tests: admin-path nonce+CSP, non-admin pass-through, per-request nonce uniqueness, custom `MountPath` scoping, antiforgery valid-token path, antiforgery invalid-token → BadRequest-typed result.
- Test-only `TestResponseFeature` (`IHttpResponseFeature`) supplies independent `Headers`/`StatusCode`/`Body` storage and replays captured `OnStarting` callbacks in reverse registration order — unblocks unit-testing middleware whose behavior lives inside `OnStarting` without a live Kestrel pipeline.

## Task Commits

Each task was committed atomically with `--no-verify`:

1. **Task 1: AdminCspNonceMiddleware — per-request 128-bit nonce + strict CSP** — `1c0d2a2` (feat)
2. **Task 2: AntiforgeryValidationFilter + ValidationEndpointFilter copy** — `d5a1d7a` (feat)

## Exact CSP Policy Emitted

```
default-src 'self'; script-src 'self' 'nonce-<base64-16>'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'
```

Directive-by-directive rationale (per D-15 + RESEARCH §UI Hardening):

| Directive | Value | Purpose |
|-----------|-------|---------|
| `default-src` | `'self'` | Deny-by-default for any resource class not explicitly listed |
| `script-src` | `'self' 'nonce-<per-request>'` | Allow same-origin scripts; per-request nonce permits controlled inline blocks (Blazor Server runtime in plan 03-08 reads the nonce via `IHttpContextAccessor` and threads it into `<script nonce=@Nonce>`) |
| `style-src` | `'self' 'unsafe-inline'` | MudBlazor emits inline `style` attributes for dynamic sizing (Snackbar transitions, DataGrid resize handles) and `<style>` blocks via `MudThemeProvider` — `'unsafe-inline'` covers these (MudBlazor 9.3.0 ships no inline `<script>` per RESEARCH A6) |
| `img-src` | `'self' data:` | `data:` URI support for inline SVG icons embedded in MudBlazor components |
| `font-src` | `'self'` | Material Symbols font ships inside `_content/MudBlazor/` (self-served) |
| `connect-src` | `'self'` | Blazor Server SignalR circuit returns to the same origin |
| `frame-ancestors` | `'none'` | Unconditional clickjacking mitigation — no parent frame permitted (T-03-05-02) |
| `base-uri` | `'self'` | Prevents attacker-injected `<base href>` from redirecting relative URLs |
| `form-action` | `'self'` | `EditForm` submissions cannot be redirected to attacker origins |

**No** `report-uri` / `report-to` — consistent with the library's "no phone home" invariant (T-03-05-06 accepted). `AdminCspOptions.ReportOnly` exists as a v1+ hook but is NOT consumed by this plan; a future plan may layer a `Content-Security-Policy-Report-Only` companion header when a consumer provides their own reporting endpoint.

## Nonce Size + Encoding

- **Entropy:** 128 bits (16 bytes) via `System.Security.Cryptography.RandomNumberGenerator.Fill(Span<byte>)`.
- **Allocation:** `stackalloc byte[16]` — zero heap pressure in the hot path.
- **Encoding:** `Convert.ToBase64String(Span<byte>)` → 24-character base64 string including padding (`==` tail).
- **Per-request freshness:** Test `TwoAdminRequests_ProduceDifferentNonces` asserts two sequential invocations produce distinct values (T-03-05-07 mitigated — cryptographic entropy makes predictability non-issue).
- **Storage key:** `public const string AdminCspNonceMiddleware.NonceItemKey = "gamekit.admin.csp-nonce"` — plan 03-08 will consume this constant from `App.razor` so there is no duplicated string literal.

## Variance from the Auth ValidationEndpointFilter analog

**Zero behavioral variance.** The single-line `diff` between `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs` and `src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs` is:

```
10c10
< namespace GameKit.Auth.Http.EndpointFilters;
---
> namespace GameKit.Admin.UI.Http.EndpointFilters;
```

Everything else — SPDX header, using statements, XML docs, type signature, generic constraint (`where TRequest : class`), the `OfType<TRequest>().FirstOrDefault()` first-argument resolution, the `Results.ValidationProblem(result.ToDictionary())` failure branch, and the `ConfigureAwait(false)` continuations — is byte-for-byte identical. This matches PATTERNS §Copy-verbatim directive and the plan's acceptance criterion "structurally identical modulo namespace line".

## Threats Mitigated vs Deferred

From the plan's `<threat_model>`:

| Threat | Disposition | Mitigation in this plan |
|--------|-------------|--------------------------|
| T-03-05-01 — XSS via MudBlazor inline script | mitigate | `script-src 'self' 'nonce-<per-request>'` blocks any inline script without the per-request nonce. MudBlazor 9.3.0 ships no inline `<script>` tags (RESEARCH A6 verified against nuget.org 2026-04-18 package contents) so no allowlist entries needed beyond the nonce. |
| T-03-05-02 — Clickjacking via iframe embedding | mitigate | `frame-ancestors 'none'` emitted in every CSP header. Browsers honor unconditionally. |
| T-03-05-03 — CSRF on mutation endpoints | mitigate | `AntiforgeryValidationFilter` resolves `IAntiforgery` and awaits `ValidateRequestAsync`; failure → 400 `csrf_validation_failed`. Unit test `InvalidToken_Returns_BadRequest_With_CsrfError` verifies the short-circuit. Wiring onto each POST/DELETE/PATCH handler lands in plan 03-07. |
| T-03-05-04 — CSP nonce leaks to non-admin pages | mitigate | Unit test `NonAdminPath_NoNonce_NoCspHeader` asserts both `ctx.Items[NonceItemKey] == null` AND `Content-Security-Policy` absent for `/auth/login`. Additional custom-prefix test `CustomMountPath_Applies_To_Configured_Prefix_Only` confirms the scoping follows the option value. |
| T-03-05-05 — nonce replay | accept | Single-request scope. Documented in plan; no code action. |
| T-03-05-06 — CSP report-uri topology leak | accept / defer | No `report-uri` or `report-to` directive emitted. `AdminCspOptions.ReportOnly` exists as an options hook; consumer must own any future report endpoint. |
| T-03-05-07 — predictable nonce | mitigate | `RandomNumberGenerator.Fill` is the .NET cryptographic RNG; 128 bits is well above any realistic brute-force or birthday-collision threshold for per-request scope. Unit test `TwoAdminRequests_ProduceDifferentNonces` covers the "same-seed" regression risk. |

## Files Created/Modified

- `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs` — per-request 128-bit nonce + CSP emission
- `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` — stateless `IEndpointFilter` wrapping `IAntiforgery.ValidateRequestAsync`
- `src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs` — verbatim copy of the Auth analog (only namespace line differs)
- `tests/GameKit.Admin.Tests/AdminCspNonceMiddlewareTests.cs` — 4 unit tests + `TestResponseFeature` harness
- `tests/GameKit.Admin.Tests/AntiforgeryValidationFilterTests.cs` — 2 unit tests using `Mock<IAntiforgery>` (MockBehavior.Strict)

## Decisions Made

1. **Class visibility: `public sealed` for both filters and the middleware.** The plan's reference code uses `public sealed class`, the plan's test file instantiates via `new AdminCspNonceMiddleware(next, opts)` without relying on `InternalsVisibleTo`, and `UseMiddleware<T>` at wiring time (plan 03-06) needs the constructor visible to the hosting application. PATTERNS.md line 721 suggests `internal sealed` but the plan's concrete code block (lines 250+) takes precedence per plan-supremacy during execution.
2. **Test-only `TestResponseFeature` owns independent Headers/StatusCode/Body.** Forwarding `Headers` to `ctx.Response.Headers` produces infinite recursion because `HttpResponse.Headers` resolves through `IHttpResponseFeature.Headers` — our own feature. Independent storage is the only viable unit-test path for `OnStarting`-gated behavior without standing up TestServer.
3. **Keep `AdminCspOptions.ReportOnly` unconsumed.** The options property was registered in plan 03-03 for a future companion `Content-Security-Policy-Report-Only` header. This plan's objective is the enforce-only header from D-15; adding the Report-Only branch is out of scope.
4. **`OnStarting` callbacks fire in reverse registration order in the test feature.** Matches Kestrel semantics so a future middleware that also registers `OnStarting` and expects to overwrite a downstream header behaves identically in tests and production.

## Deviations from Plan

**One minor deviation (Rule 3 - Blocking):** The plan's test scaffolding used `ctx.Response.StartAsync()` to trigger `OnStarting` callbacks, but `DefaultHttpContext`'s default `IHttpResponseFeature` implementation is a no-op for OnStarting — callbacks never fire, so the plan's tests would fail with "String is empty" assertions. Fixed inline (Rule 3) by substituting a test-only `TestResponseFeature` that captures OnStarting callbacks and replays them via a new `FireOnStartingAsync()` helper. Behavior, nonce length, CSP directives, and all assertions are exactly as the plan specified. Logged here for traceability; no production code affected.

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Replace `Response.StartAsync()` trigger with explicit `FireOnStartingAsync()` harness**
- **Found during:** Task 1 (AdminCspNonceMiddleware TDD GREEN phase)
- **Issue:** The plan's test code relied on `ctx.Response.StartAsync()` to invoke `OnStarting` callbacks, but `DefaultHttpContext` ships a no-op `IHttpResponseFeature` whose `OnStarting` discards callbacks. Assertions read empty headers; two tests failed with "String is empty" / "Expected True Actual False".
- **Fix:** Introduced a private `TestResponseFeature : IHttpResponseFeature` in `AdminCspNonceMiddlewareTests.cs` that owns independent `Headers`/`StatusCode`/`Body` storage and exposes `FireOnStartingAsync()`. Test `MakeCtx` now `Features.Set<IHttpResponseFeature>(...)` to install it; each test calls `await FireOnStartingAsync(ctx)` instead of `Response.StartAsync()`. Production middleware unchanged.
- **Files modified:** `tests/GameKit.Admin.Tests/AdminCspNonceMiddlewareTests.cs` (test harness only, no source change)
- **Verification:** `dotnet test ... --filter 'AdminCspNonceMiddlewareTests'` → 4/4 passed.
- **Committed in:** `1c0d2a2` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 3 - Blocking, test harness)
**Impact on plan:** Zero production-code impact. Test scaffolding correction only — identical assertion surface, identical test names, identical CSP directive checks. The plan's CSP policy, nonce size, `MountPath` scoping, and acceptance criteria are unchanged.

## Issues Encountered

- `TestResponseFeature.Headers { get => _ctx.Response.Headers }` initial wiring caused a `StackOverflowException` because `HttpResponse.Headers` forwards to `IHttpResponseFeature.Headers` — infinite recursion. Fixed by giving the test feature independent `HeaderDictionary`/`StatusCode`/`Body` storage in the same commit.

## Self-Check

- [x] `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs` exists
- [x] `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` exists
- [x] `src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs` exists
- [x] `tests/GameKit.Admin.Tests/AdminCspNonceMiddlewareTests.cs` exists
- [x] `tests/GameKit.Admin.Tests/AntiforgeryValidationFilterTests.cs` exists
- [x] Commit `1c0d2a2` reachable (Task 1)
- [x] Commit `d5a1d7a` reachable (Task 2)
- [x] Full solution `dotnet build` green (0 warnings, 0 errors)
- [x] `dotnet test tests/GameKit.Admin.Tests/` green (11/11 tests pass, 6 new)
- [x] Verification check `grep -q 'frame-ancestors ..none..' ...Middleware.cs` passes
- [x] Verification check `grep -q 'gamekit.admin.csp-nonce' ...Middleware.cs` passes
- [x] Verification check `grep -q 'csrf_validation_failed' ...AntiforgeryValidationFilter.cs` passes
- [x] Verification check `grep -q 'namespace GameKit.Admin.UI.Http.EndpointFilters' ...ValidationEndpointFilter.cs` passes
- [x] `diff` of Admin vs Auth `ValidationEndpointFilter.cs` produces exactly one line delta (namespace declaration)

## Self-Check: PASSED

## Next Wave / Plan Readiness

- Plan 03-06 (`UseGameKitAdmin`) can now wire `app.UseMiddleware<AdminCspNonceMiddleware>()` at the correct position and register `services.AddAntiforgery(o => { o.HeaderName = "X-GameKit-Admin-CSRF"; o.Cookie.Name = "gk_admin_csrf"; ... })` knowing `AntiforgeryValidationFilter` will resolve the resulting `IAntiforgery` from RequestServices.
- Plan 03-07 (mutation endpoints) can append `.AddEndpointFilter<AntiforgeryValidationFilter>().AddEndpointFilter<ValidationEndpointFilter<TRequest>>()` in that order and rely on the CSRF check short-circuiting before FluentValidation body inspection.
- Plan 03-08 (App.razor) can read `HttpContext.Items[AdminCspNonceMiddleware.NonceItemKey]` via `IHttpContextAccessor` and thread the nonce into `<script nonce=@Nonce src="...">` tags; MudBlazor's `_content/MudBlazor/MudBlazor.min.js` is external so `script-src 'self'` already covers it, meaning the nonce is only strictly required if we ever emit an inline `<script>` block (defensive belt-and-suspenders).
- Plan 03-13 (end-to-end integration test) can assert (a) every response under `/admin/*` carries `Content-Security-Policy`, (b) responses under `/auth/*` do not, (c) `/admin/api/players/{id}/ban` without the antiforgery token returns 400 with body `{"error":"csrf_validation_failed"}`.

---
*Phase: 03-admin-ui*
*Completed: 2026-04-19*
