---
phase: 03-admin-ui
plan: 04
subsystem: admin-ui
tags:
  - admin-ui
  - cookie-auth
  - rate-limiting
  - security-hardening
  - wave-2
dependencies:
  requires:
    - phase: 03-01
      provides: tests/GameKit.Admin.Tests xUnit harness (Moq + FrameworkReference Microsoft.AspNetCore.App)
    - phase: 03-03
      provides: AdminAuthenticationSchemeConstants (Scheme/CookieName/CSRF names) consumed by tests + future callers
  provides:
    - src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs — 404-in-Prod / 302-in-Dev cookie challenge handler + 403 on access-denied
    - src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs — gamekit:admin:login sliding-window 5/min/IP policy + public constant AdminLoginPolicy
  affects:
    - 03-06 (AddGameKitAdmin wires the AddCookie(...) scheme with EventsType=typeof(AdminCookieEvents) + DI registration; AddRateLimiter-then-AddAdminRateLimits order)
    - 03-07 (/admin/api/login endpoint chains .RequireRateLimiting(AdminRateLimitRegistrations.AdminLoginPolicy))
    - 03-13 (integration tests assert 404-in-Prod and 302-in-Dev behavior end-to-end via WebApplicationFactory)
tech-stack:
  added: []
  patterns:
    - "Subclass CookieAuthenticationEvents for environment-branched challenge translation — canonical ASP.NET Core 10 hook (RESEARCH §404 not 401, MS Learn: CookieAuthenticationHandler.HandleChallengeAsync)"
    - "RateLimiterOptions configured via services.Configure<RateLimiterOptions>(...) after the caller has invoked services.AddRateLimiter() — idempotent under repeated invocation; matches Phase 2 AuthRateLimitRegistrations shape but sliding-window + IP-only partition"
    - "Pinned policy-name constant (AdminLoginPolicy = 'gamekit:admin:login') is the external contract — mirrors GameKitRateLimitPolicies from plan 02-07"
    - "W3 minimal-introspection test pattern: RateLimiterOptions.PolicyMap is not public in .NET 10, so the unit test proves only the builder runs clean and the constant is stable; end-to-end 429 behavior deferred to plan 03-07 integration tests (explicitly called out in plan frontmatter + acceptance criteria)"
key-files:
  created:
    - src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs
    - src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs
    - tests/GameKit.Admin.Tests/AdminCookieEventsTests.cs
    - tests/GameKit.Admin.Tests/AdminRateLimitRegistrationTests.cs
  modified: []
decisions:
  - "AdminCookieEvents is public sealed (not internal) — plan 03-06 AddGameKitAdmin registers it via .Configure<CookieAuthenticationOptions>(scheme, o => o.EventsType = typeof(AdminCookieEvents)) + services.AddScoped<AdminCookieEvents>(); tests construct it directly. Matches the public-surface convention used for other Admin.UI configuration types (GameKitAdminOptions, AdminRoles, AdminPolicies)."
  - "AdminCookieEvents reads context.Options.LoginPath dynamically (NOT a hardcoded '/admin/login' constant) — if a consumer rebinds the login path via AdminCookieOptions customization, the 404-suppression exception still lines up with the rebind. Defends T-03-04-06 (spoofing via LoginPath rebind)."
  - "AdminCookieEvents.RedirectToLogin uses PathString.StartsWithSegments(context.Options.LoginPath) — exact matching semantics: '/admin/login' matches, '/admin/logins-page' does NOT (segment boundaries respected). Defensive against path-prefix tricks."
  - "Policy name lives as a public const on AdminRateLimitRegistrations (not on a separate AdminRateLimitPolicies class) — Auth's plan 02-07 uses IGameKitRateLimitPolicies interface because it had three policies; Admin has one, so a local constant is enough. If plans 03-05/03-07 add a second policy, extract to Authorization/AdminRateLimitPolicies.cs."
  - "Test for AdminRateLimitRegistrations is minimal-introspection per plan W3 — PolicyMap is internal on .NET 10 and the negative 'throws on duplicate' assertion was unreliable across 8.0/9.0/10.0 (see plan NOTE). The unit test here proves (a) AddAdminRateLimits runs cleanly through IOptionsMonitor<RateLimiterOptions> materialization and (b) AdminLoginPolicy constant value has not regressed. End-to-end 429 throttling (5/min/IP) is covered by plan 03-07 AdminLoginEndpointTests.RateLimit_After5Failures_Returns429."
  - "AddRateLimiter extension method lives in namespace Microsoft.AspNetCore.Builder (decision from plan 02-07; captured in STATE). Test file imports `using Microsoft.AspNetCore.Builder;` — matches the Auth precedent exactly; the enclosing RateLimiting types live in Microsoft.AspNetCore.RateLimiting but the builder extension is in the Builder namespace."
metrics:
  duration_minutes: 6
  tasks_completed: 2
  files_created: 4
  files_modified: 0
  tests_passing:
    unit_cookie_events: 7
    unit_rate_limit: 1
  completed_date: 2026-04-19
requirements_completed: []
---

# Phase 03 Plan 04: Admin Cookie Events + Login Rate-Limit Policy Summary

Shipped the two security primitives that the admin cookie-auth flow pivots on: `AdminCookieEvents` (subclass of `CookieAuthenticationEvents` that translates the challenge into a 404 in Production for `/admin/*` paths except `/admin/login`, falls through to the normal 302 in Development/Staging, and returns 403 on access-denied regardless of environment) and `AdminRateLimitRegistrations` (a single sliding-window policy `gamekit:admin:login` — 5 permits / 1 minute / IP partition / 6 segments for 10-second slide granularity). Both are pure utilities — no DI wiring, no endpoints — consumed by plan 03-06's `AddGameKitAdmin` call chain. All acceptance criteria met, 0 deviations, full solution builds 0 warnings / 0 errors.

## Performance

- **Duration:** ~6 minutes
- **Started:** 2026-04-19 (plan execution)
- **Completed:** 2026-04-19
- **Tasks:** 2 (both TDD — RED compile-fail on missing types, then GREEN)
- **Files created:** 4
- **Tests added:** 7 (cookie events: 4-theory + 3 facts) + 1 (rate limit) = 8 total

## Task Commits

1. **Task 1: AdminCookieEvents — 404-in-Prod / 302-in-Dev / 403 access-denied (TDD)** — `fbc73f4` (feat)
2. **Task 2: AdminRateLimitRegistrations — gamekit:admin:login sliding-window (TDD)** — `c662d09` (feat)

## Status-code matrix implemented (`AdminCookieEvents`)

| Environment | Request path | Flow | Status |
|-------------|--------------|------|--------|
| Production  | `/admin` (exact) | `RedirectToLogin` → 404 suppress | **404** |
| Production  | `/admin/` | `RedirectToLogin` → 404 suppress | **404** |
| Production  | `/admin/players` | `RedirectToLogin` → 404 suppress | **404** |
| Production  | `/admin/api/players/search` | `RedirectToLogin` → 404 suppress | **404** |
| Production  | `/admin/login` | `RedirectToLogin` → `base.RedirectToLogin` | **302** (Location: /admin/login) |
| Development | `/admin/players` | `RedirectToLogin` → `base.RedirectToLogin` | **302** (Location: /admin/login) |
| Development | `/admin/login` | `RedirectToLogin` → `base.RedirectToLogin` | **302** |
| any         | any `/admin/*` (authenticated, wrong role) | `RedirectToAccessDenied` | **403** |

**Key implementation detail:** The 404 branch keys off `_env.IsProduction()` (HostingEnvironmentExtensions) AND `!context.Request.Path.StartsWithSegments(context.Options.LoginPath)`. The login path is read dynamically from the cookie options — a consumer that rebinds `LoginPath` via the cookie scheme configuration still gets correct behavior (no hardcoded `/admin/login` string in the class).

**Why not a middleware:** A response-status-rewriting middleware would need to inspect response bytes after-the-fact and would also snag Phase 2 Bearer 401s on unrelated routes. The `CookieAuthenticationEvents` hook is the canonical ASP.NET Core 10 insertion point (RESEARCH §404 not 401; MS Learn `CookieAuthenticationHandler.HandleChallengeAsync`).

## Rate-limit policy parameters (`AdminRateLimitRegistrations`)

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Policy name (public const) | `"gamekit:admin:login"` | D-18; external contract; referenced by plan 03-07's `.RequireRateLimiting(...)` |
| Algorithm | `SlidingWindowRateLimiterOptions` | Smoother rate control than `FixedWindow`; no 2× burst at window boundaries |
| `PermitLimit` | 5 | D-18 — balances legitimate forget-password retries against credential stuffing |
| `Window` | 1 minute | D-18 |
| `SegmentsPerWindow` | 6 | 10-second slide granularity (6 × 10s = 60s window) |
| `QueueLimit` | 0 | Reject over-limit requests immediately (429) — no queuing |
| `QueueProcessingOrder` | `OldestFirst` | Standard (irrelevant with QueueLimit=0; set defensively) |
| `AutoReplenishment` | `true` | Time-based replenishment (standard) |
| Partition key | `httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"` | **IP-only** per D-18/SP-8 — admin operators do not send `X-GameKit-Device`; composite key from plan 02-07 is inappropriate here |

## Unit-test path landed for rate-limit introspection

**W3 minimal-introspection** (the second option listed in the plan behavior block).

The `RateLimiterOptions.PolicyMap` property is internal on .NET 10 and the negative "throws-on-duplicate" assertion pattern the plan originally contemplated would have been unreliable across framework versions (the plan's NOTE explicitly flagged this). What the single `[Fact]` asserts:

1. `services.AddRateLimiter(_ => { });` + `services.AddAdminRateLimits();` composes without exception.
2. `IOptionsMonitor<RateLimiterOptions>` materializes `CurrentValue` — proves the configure-action ran through the options pipeline without deferred-throw surprises.
3. The `AdminLoginPolicy` public-const value is exactly `"gamekit:admin:login"` — catches any regression at the external-contract layer.

End-to-end 429 throttling (5/min/IP burst rejection) is not covered here and is explicitly deferred to plan 03-07 `AdminLoginEndpointTests.RateLimit_After5Failures_Returns429` (WebApplicationFactory integration — needs the `/admin/api/login` endpoint which this plan does not ship).

## Test counts

| Project | Passed | Failed | Skipped | Delta from 03-03 |
|---------|--------|--------|---------|------------------|
| `GameKit.Admin.Tests` (unit) | **13** | 0 | 0 | +8 (7 cookie events + 1 rate-limit) |
| `GameKit.Admin.Integration.Tests` | 3 | 0 | 0 | unchanged |

Full solution (`dotnet build GameKit.sln -c Debug`) — **17 projects, 0 warnings, 0 errors.**

## Files Created / Modified (authoritative list)

### Created (4)

- `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs` — 50 LOC; `public sealed class AdminCookieEvents : CookieAuthenticationEvents`; override `RedirectToLogin` + `RedirectToAccessDenied`; constructor accepts `IHostEnvironment`.
- `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` — 47 LOC; `public static class AdminRateLimitRegistrations`; `public const string AdminLoginPolicy = "gamekit:admin:login"`; `public static IServiceCollection AddAdminRateLimits(this IServiceCollection services)`.
- `tests/GameKit.Admin.Tests/AdminCookieEventsTests.cs` — 96 LOC; 4 `[Theory]` cases (Production non-login) + 3 `[Fact]`s (Production login, Development, AccessDenied).
- `tests/GameKit.Admin.Tests/AdminRateLimitRegistrationTests.cs` — 41 LOC; 1 `[Fact]` asserting registration completes + constant stability (W3).

### Modified (0)

No existing files modified — the new files slot into the existing folder structure (`Authentication/` already exists from plan 03-03; `Http/RateLimiting/` is new but nested via directory convention).

## Deviations from Plan

**None — plan executed exactly as written.** Both tasks completed with the literal code shown in the plan's `<action>` blocks. No Rule-1/2/3 auto-fixes triggered. One minor compile nudge during Task 2 iteration (not a deviation):

- The test file initially omitted `using Microsoft.AspNetCore.Builder;`. Per STATE decision from plan 02-07, `AddRateLimiter` (the extension method the plan calls) lives in namespace `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.RateLimiting` as the enclosing `RateLimiterOptions` / `SlidingWindowRateLimiterOptions` types might suggest. The plan's action-block code was correct; adding the `using` aligns the test exactly with the Auth precedent (`AuthRateLimitRegistrations` internal usage).

**RED phase confirmed:**
- Task 1 RED — CS0246 `AdminCookieEvents` not found (4 occurrences; the type did not exist).
- Task 2 RED — CS0234 `GameKit.Admin.UI.Http` namespace does not exist (the namespace + type did not exist).

**GREEN phase first-run success:**
- Task 1 — 7 tests passed on first run after implementation dropped.
- Task 2 — 1 test passed on first run after implementation dropped (with the `using Microsoft.AspNetCore.Builder;` import already in place).

## Threats mitigated vs accepted (per plan `<threat_model>`)

| Threat ID | Category | Disposition | How addressed in this plan |
|-----------|----------|-------------|-----------------------------|
| T-03-04-01 | Information Disclosure (404 vs 401 timing enumeration of `/admin/*`) | **mitigate** | `AdminCookieEvents.RedirectToLogin` returns uniform 404 in Production for any `/admin/*` path except `/admin/login` — anonymous probes cannot distinguish "admin mounted" from "route nonexistent". Development/Staging remains 302 (accepted non-prod risk). |
| T-03-04-02 | Spoofing (credential stuffing on `/admin/login`) | **mitigate** | `gamekit:admin:login` sliding window 5/min/IP caps per-IP attempt rate. (BCrypt work-factor 12 from `IPasswordHasher` reuse handles slow-path timing; landed in Phase 2.) |
| T-03-04-03 | Elevation of Privilege (wrong-role endpoint access) | **mitigate** | `AdminCookieEvents.RedirectToAccessDenied` returns 403 unconditionally. Scheme-pinned authorization policy (plan 03-06 + 03-13) prevents a forwarded Bearer from satisfying admin policies. |
| T-03-04-04 | Denial of Service (distributed credential stuffing from many IPs) | **accept** | Rate-limit is per-partition; distributed countermeasures (captcha, WAF) are out of scope for v1 per CONTEXT deferred list. |
| T-03-04-05 | Tampering (X-Forwarded-For spoofing to evade IP partition) | **accept** | `httpContext.Connection.RemoteIpAddress` reads socket-level address (not XFF header) by default. Consumer behind a trusted proxy must register `ForwardedHeadersMiddleware` separately — documented for plan 03-06. |
| T-03-04-06 | Spoofing (rebinding `LoginPath` in `CookieAuthenticationOptions`) | **mitigate** | `AdminCookieEvents.RedirectToLogin` reads `context.Options.LoginPath` dynamically — if a consumer rebinds the path, the 404-exception check still matches. No hardcoded string. |

## Threat Flags

None. This plan introduces no security surface not already enumerated in `<threat_model>`.

## Known Stubs

None. Both types ship production-ready defaults. The single `[Fact]` rate-limit test is minimal-introspection by design (plan W3) — it is not a stub, and its scope is explicitly delineated in the acceptance criteria.

## Self-Check: PASSED

Verification run after writing this SUMMARY:

- File existence checks (4 created):
  - `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs` — FOUND
  - `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` — FOUND
  - `tests/GameKit.Admin.Tests/AdminCookieEventsTests.cs` — FOUND
  - `tests/GameKit.Admin.Tests/AdminRateLimitRegistrationTests.cs` — FOUND
- Commit existence checks:
  - `fbc73f4` — FOUND (Task 1: feat — AdminCookieEvents)
  - `c662d09` — FOUND (Task 2: feat — AdminRateLimitRegistrations)
- Build: `dotnet build GameKit.sln -c Debug` — 17 projects / 0 warnings / 0 errors
- Tests: `dotnet test tests/GameKit.Admin.Tests/ --filter 'FullyQualifiedName~AdminCookieEventsTests|FullyQualifiedName~AdminRateLimitRegistrationTests'` — 8/0/0
