---
phase: 18-security-audit
plan: "04"
subsystem: Security Audit
tags: [security, testing, route-enumeration, rate-limiting, auth-audit]
dependency_graph:
  requires: [18-01]
  provides: [AdminRouteAuthAuditTests, AuthRateLimitAuditTests]
  affects: [GameKit.Admin.Integration.Tests, GameKit.Auth.Tests]
tech_stack:
  added: []
  patterns:
    - EndpointDataSource enumeration for structural policy audit
    - IEndpointRouteBuilder.DataSources for no-host endpoint metadata inspection
    - EnableRateLimitingAttribute as public marker for RequireRateLimiting detection
    - AdminCookieEvents existence-guard pattern (guard before behavioral assertion)
key_files:
  created:
    - tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs
    - tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs
  modified: []
decisions:
  - "EnableRateLimitingAttribute is the correct public type to detect RequireRateLimiting — IRateLimiterMetadata is internal to the framework"
  - "Use IEndpointRouteBuilder.DataSources (not DI EndpointDataSource) for pre-StartAsync endpoint inspection in unit tests"
  - "admin/login/submit added to known-anonymous allowlist — AdminFormEndpoints.cs registers it as AllowAnonymous for static-SSR Blazor form login"
  - "No production source changes required — all three auth write endpoints already carried rate-limit policies"
metrics:
  duration: "~25 minutes"
  completed: "2026-06-23"
  tasks_total: 2
  tasks_completed: 2
  files_created: 2
  files_modified: 0
status: complete
---

# Phase 18 Plan 04: SEC-02 Admin Route Auth Audit + SEC-03 Rate-Limit Audit Summary

Implements SEC-02 (admin route enumeration + player-JWT rejection) and SEC-03 (auth write endpoint rate-limit enumeration) as CI-enforced structural invariants. Both requirements are now test-gated: a new unprotected `/admin/*` endpoint or a missing rate-limit on a write endpoint will fail CI before shipping.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | AdminRouteAuthAuditTests — SEC-02 | 18541ea | tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs |
| 2 | AuthRateLimitAuditTests — SEC-03 | 12567ce | tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs |

## Test Results

### SEC-02: AdminRouteAuthAuditTests (integration)

```
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

- `AllAdminRoutes_Either_AreAnonymousAllowlisted_Or_HaveAdminPolicy` — dynamically walks all `/admin/*` endpoints from `EndpointDataSource`, asserts each is either in the known-anonymous allowlist or carries `AdminPolicies.Admin` / `AdminPolicies.Superadmin`.
- `PlayerJwt_IsRejected_OnExistingAdminRoute` — asserts `admin/api/audit` EXISTS in `EndpointDataSource` (existence guard), then asserts a player Bearer JWT yields non-200 (404 in Production due to `AdminCookieEvents` cookie-challenge suppression).

### SEC-03: AuthRateLimitAuditTests (unit)

```
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4
```

- `Login_Endpoint_Has_RateLimiterMetadata` — asserts `EnableRateLimitingAttribute` on `/auth/login/{provider}`.
- `Refresh_Endpoint_Has_RateLimiterMetadata` — asserts `EnableRateLimitingAttribute` on `/auth/refresh`.
- `Register_Endpoint_Has_RateLimiterMetadata` — asserts `EnableRateLimitingAttribute` on `/auth/register`.
- `Logout_Endpoint_Has_No_RateLimiterMetadata_Intentional` — asserts `/auth/logout` has NO rate-limit (RFC-7009 exclusion documented).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Coverage] `admin/login/submit` missing from known-anonymous allowlist**
- **Found during:** Task 1 — first test run failed with `Endpoint '/admin/login/submit' has NO IAuthorizeData metadata and is NOT in the anonymous allowlist`
- **Issue:** `AdminFormEndpoints.MapAdminFormEndpoints` registers a fifth anonymous endpoint (`POST admin/login/submit`) beyond the four listed in the research doc. This endpoint is correctly `AllowAnonymous` (it's the static-SSR Blazor form POST handler for the login page). The research doc only listed `admin/login` and `admin/logout` as form endpoints, missing the distinct `/login/submit` action URL.
- **Fix:** Added `"admin/login/submit"` to `KnownAnonymousRoutes` with an inline comment explaining it is the `AdminFormEndpoints.cs` form POST handler, distinctly AllowAnonymous by design.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs`
- **Note:** This was a research doc gap, not a production security gap. The endpoint is correctly protected (AllowAnonymous is correct for login form submission).

**2. [Rule 1 - Bug] `IRateLimiterMetadata` is internal — replaced with `EnableRateLimitingAttribute`**
- **Found during:** Task 2 — `IRateLimiterMetadata` does not compile in the Auth.Tests project (not a public type in `Microsoft.AspNetCore.RateLimiting`).
- **Fix:** Used `EnableRateLimitingAttribute` (the actual public type placed on endpoints by `RequireRateLimiting(policyName)`). Verified via a diagnostic project that `RequireRateLimiting` places `Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute` in endpoint metadata.
- **Files modified:** `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs`

**3. [Rule 1 - Bug] `IEndpointRouteBuilder.DataSources` required instead of DI `EndpointDataSource` for pre-start inspection**
- **Found during:** Task 2 — `app.Services.GetRequiredService<EndpointDataSource>()` returns a `CompositeEndpointDataSource` with 0 endpoints before `StartAsync`. `MapGroup("/auth")` adds a `GroupEndpointDataSource` to `IEndpointRouteBuilder.DataSources` immediately when `MapAuth()` is called.
- **Fix:** Changed helper to use `((IEndpointRouteBuilder)_app).DataSources.SelectMany(ds => ds.Endpoints)` for endpoint enumeration. This avoids needing to start the host (no port binding, no hosted-service startup) for a pure metadata inspection test.
- **Files modified:** `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs`

**4. [Rule 1 - Bug] Route patterns from `MapGroup("/auth")` include leading slash**
- **Found during:** Task 2 — `RouteEndpoint.RoutePattern.RawText` is `/auth/login/{provider}` (with leading `/`) when using `MapGroup`. Filter `StartsWith("auth/")` matched nothing.
- **Fix:** Normalized route text with `.TrimStart('/')` before prefix comparison.
- **Files modified:** `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs`

**5. [Rule 1 - Bug] xUnit1030 violation — `ConfigureAwait(false)` in test methods**
- **Found during:** Task 1 build — xUnit analyzer rule xUnit1030 (`WarningsAsErrors` is set in the project) rejects `ConfigureAwait(false)` in `[Fact]` methods.
- **Fix:** Removed `.ConfigureAwait(false)` from the three `await` calls inside the two test methods.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs`

## Rate-Limit Policy Changes

**No rate-limit policies were added to production source files.** All three auth write endpoints (`/auth/login/{provider}`, `/auth/refresh`, `/auth/register`) already carried `RequireRateLimiting` in `AuthEndpoints.cs`. The SEC-03 audit confirmed existing coverage.

## Pre-existing Test Failures (Not Regressions)

The full `GameKit.Admin.Integration.Tests` suite run produced 2 failures in `HealthProbeTests`:
- `ProbeAsync_Reports_Postgres_OK`
- `ProbeAsync_Reports_Redis_OK`

These are pre-existing flaky failures caused by container-startup timing in the `HealthProbeService` probe tests. They pre-date plan 18-04 (the `HealthProbeTests.cs` file was created in Phase 14 and the flakiness is documented in project memory). They are unrelated to the `AdminRouteAuthAuditTests.cs` file added in this plan.

## Known Stubs

None. Both test files exercise real endpoints from real `EndpointDataSource` / `IEndpointRouteBuilder.DataSources`.

## Threat Flags

No new threat surface introduced. This plan is tests-only.

## Self-Check

### Commits exist:
- 18541ea — `test(18-04): SEC-02 AdminRouteAuthAuditTests` ✓
- 12567ce — `test(18-04): SEC-03 AuthRateLimitAuditTests` ✓

### Files exist:
- `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs` ✓
- `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs` ✓

## Self-Check: PASSED
