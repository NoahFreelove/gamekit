---
phase: 18-security-audit
plan: "05"
subsystem: Auth / Admin / CI
tags: [security, egress, refresh-token, csrf, sec-05, sec-06]
status: complete

dependency_graph:
  requires: ["18-01"]
  provides: ["egress-gap-closed", "apple-google-backchannel-wired", "refresh-hash-test", "csrf-regression-test", "egress-ci-gate"]
  affects: ["GameKit.Auth.Apple", "GameKit.Auth.Google", "GameKit.Auth.Tests", "GameKit.Auth.Integration.Tests", "GameKit.Admin.Integration.Tests", ".github/workflows/ci.yml"]

tech_stack:
  added: []
  patterns:
    - "DelegatingHandler egress wiring on OAuth backchannel (BackchannelHttpHandler property)"
    - "Per-provider host append at AddApple/AddGoogle time (approach b — co-located with provider)"
    - "SHA-256 hex verification: ComputeSha256Hex + regex ^[0-9a-f]{64}$ + NotEqual(raw)"
    - "CSRF regression pattern: login → POST mutation without antiforgery header → assert exact 400"
    - "Static grep gate: bare HttpClient + SaaS telemetry hostnames"

key_files:
  created:
    - tests/GameKit.Auth.Tests/EgressAuditTests.cs
    - tests/GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs
    - tests/GameKit.Admin.Integration.Tests/CsrfRegressionTests.cs
  modified:
    - src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs
    - src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs
    - tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj
    - .github/workflows/ci.yml

decisions:
  - "SEC-05 allowlist approach b chosen: each provider package appends its own hosts to GameKitAuthOptions.AllowedProviderHosts at AddApple/AddGoogle time — keeps host lists co-located with their provider, leaves DefaultAllowedHosts scoped to Steam+Discord"
  - "Apple hosts: appleid.apple.com (token exchange); Google hosts: oauth2.googleapis.com, www.googleapis.com, accounts.google.com"
  - "BackchannelHttpHandler pattern: construct EgressAllowListHandler(resolvedOpts){InnerHandler=new HttpClientHandler()} and assign to apple.BackchannelHttpHandler / google.BackchannelHttpHandler"
  - "AppleProviderHosts / GoogleProviderHosts changed internal→public to enable direct reference from EgressAuditTests without InternalsVisibleTo"
  - "Static grep check 2 refined: bans SaaS hostnames (honeycomb.io, datadoghq.com, newrelic.com, grafana.net/grafana-cloud, lightstep.com) NOT the generic 'otlp' keyword — which legitimately appears in GameKit.Core observability code referencing the self-hosted OTel Collector"

metrics:
  duration_minutes: 10
  completed_date: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_changed: 7
---

# Phase 18 Plan 05: SEC-05 Egress Gap Fix + SEC-06 Refresh/CSRF Tests Summary

Apple/Google OAuth backchannel egress gap closed; refresh-token SHA-256 storage and CSRF-gate regression tests added; static egress + air-gap CI gate wired.

## Tasks Completed

| Task | Description | Commit | Result |
|------|-------------|--------|--------|
| 1 | Close Apple/Google egress gap — wire backchannel through EgressAllowListHandler | cc29e60 | `dotnet build -warnaserror` clean on both packages |
| 2 | EgressAuditTests + RefreshTokenHashingTests + CsrfRegressionTests | ec04731 | 19 + 3 + 3 = 25 tests pass |
| 3 | Static egress + air-gap CI gate in ci.yml | d76722c | Both grep checks pass against current tree |

## SEC-05: Egress Gap Closure

### Gap Confirmed

The RESEARCH assumption A2 was confirmed as a real gap. `AppleBuilderExtensions` and `GoogleBuilderExtensions` did not set `BackchannelHttpHandler`, so Apple/Google token-exchange calls used an uncontrolled `HttpClient` that bypassed `EgressAllowListHandler`.

### Fix Applied (Approach b)

**AppleBuilderExtensions.cs:**
- Added `AppleProviderHosts = ["appleid.apple.com"]` (public static readonly, co-located with the provider)
- At AddApple time: append Apple hosts to `GameKitAuthOptions.AllowedProviderHosts` (same singleton that `EgressAllowListHandler` snapshots)
- Set `apple.BackchannelHttpHandler = new EgressAllowListHandler(resolvedOpts) { InnerHandler = new HttpClientHandler() }`

**GoogleBuilderExtensions.cs:**
- Added `GoogleProviderHosts = ["oauth2.googleapis.com", "www.googleapis.com", "accounts.google.com"]` (public static readonly)
- At AddGoogle time: append Google hosts to `AllowedProviderHosts`
- Set `google.BackchannelHttpHandler = new EgressAllowListHandler(resolvedOpts) { InnerHandler = new HttpClientHandler() }`

### Why Approach b

Keeps each provider's required hosts co-located with the provider package, not hardcoded into `DefaultAllowedHosts` (which was intentionally scoped to Steam+Discord). If GameKit.Auth.Apple is not installed, `appleid.apple.com` is never added to the allow-list — correct behavior.

### CI Gate

Two checks in `.github/workflows/ci.yml`:
1. `grep 'new HttpClient('` in `src/**/*.cs` — fails if any non-exempted file constructs a bare client. Exempts: `EgressAllowListHandler.cs`, `AppleBuilderExtensions.cs`, `GoogleBuilderExtensions.cs` (DelegatingHandler wiring), `AddHttpClient`/`IHttpClientFactory` named-client paths.
2. `grep -E 'honeycomb.io|datadoghq.com|newrelic.com|grafana-cloud|grafana.net|lightstep.com'` in `src/` + `samples/` — fails on any hardcoded SaaS telemetry hostname. Deliberately excludes generic "otlp"/"otelcol" because the sample app uses the self-hosted OpenTelemetry Collector (no phone-home).

## SEC-06: Refresh Token Hashing + CSRF Regression

### RefreshTokenHashingTests (3 tests, integration)

Verified against real Postgres via Testcontainers:
- `IssueRootAsync_Stores_Sha256Hex_Not_RawToken`: stored `TokenHash` matches `^[0-9a-f]{64}$`, equals `SHA-256(raw)`, is NOT equal to `raw`.
- `RotateAsync_Stores_Sha256Hex_For_Child_Token`: both root and child rows store hashes; `ReplacedByTokenHash` == child's `TokenHash`.
- `NoColumn_Contains_RawToken_As_Literal`: verifies no other string column on the row holds the raw token.

### CsrfRegressionTests (3 tests, integration)

Verified against real Postgres + Redis via Testcontainers:
- Ban mutation without antiforgery → `HttpStatusCode.BadRequest` (400) + body contains `"csrf_validation_failed"`.
- Unban mutation without antiforgery → 400 + `"csrf_validation_failed"`.
- Delete admin mutation without antiforgery → 400 + `"csrf_validation_failed"`.
- Pattern: login → POST/DELETE without `X-GameKit-Admin-CSRF` header → assert `Assert.Equal(HttpStatusCode.BadRequest, ...)`.

## Test Results

| Filter | Suite | Passed | Failed |
|--------|-------|--------|--------|
| `FullyQualifiedName~EgressAudit` | GameKit.Auth.Tests (unit) | 19 | 0 |
| `FullyQualifiedName~RefreshTokenHashing` | GameKit.Auth.Integration.Tests | 3 | 0 |
| `FullyQualifiedName~CsrfRegression` | GameKit.Admin.Integration.Tests | 3 | 0 |

Static grep gate: both checks pass against the current tree.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical Functionality] grep Check 2 pattern refined from 'otlp' to SaaS hostnames**
- **Found during:** Task 3 local verification
- **Issue:** Using `otlp` as a grep term produced false-positives from `GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` (which legitimately uses "OTLP" to refer to the open-source self-hosted OpenTelemetry Collector protocol) and from the sample app's observability config files.
- **Fix:** Replaced `otlp|otelcol|...` with explicit SaaS hostname patterns: `honeycomb.io|datadoghq.com|newrelic.com|grafana-cloud|grafana.net|lightstep.com`. This accurately distinguishes "phone-home to a managed SaaS collector" from "use the self-hosted OTel Collector that ships in docker-compose.observability.yml".
- **Files modified:** `.github/workflows/ci.yml`
- **Commit:** d76722c

**2. [Rule 2 - Missing Critical Functionality] AppleProviderHosts/GoogleProviderHosts changed internal→public**
- **Found during:** Task 2 — EgressAuditTests references `AppleBuilderExtensions.AppleProviderHosts` from a separate test assembly. `internal` requires `InternalsVisibleTo` plumbing; `public` is correct since these host lists are not secrets and are useful for consumers to inspect.
- **Fix:** Changed both arrays to `public static readonly` with XML doc comments.
- **Commit:** ec04731

## Threat Mitigations Applied

| Threat ID | Status |
|-----------|--------|
| T-18-05-01 (Apple/Google backchannel reaches non-allowlisted hosts) | Mitigated — BackchannelHttpHandler wired; EgressAuditTests asserts |
| T-18-05-02 (bare new HttpClient in src/ bypasses egress) | Mitigated — CI Check 1 fails on match |
| T-18-05-03 (hardcoded SaaS OTLP endpoint phones home) | Mitigated — CI Check 2 fails on SaaS hostnames |
| T-18-05-04 (raw refresh token stored in DB) | Mitigated — RefreshTokenHashingTests asserts SHA-256 only |
| T-18-05-05 (CSRF on admin mutation) | Mitigated — CsrfRegressionTests asserts exactly 400 |

## Known Stubs

None. All deliverables are fully wired and verified.

## Threat Flags

None. No new network endpoints or auth paths introduced.

## Self-Check: PASSED

- `tests/GameKit.Auth.Tests/EgressAuditTests.cs` — FOUND
- `tests/GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs` — FOUND
- `tests/GameKit.Admin.Integration.Tests/CsrfRegressionTests.cs` — FOUND
- Commits cc29e60, ec04731, d76722c — all present in git log
- Static grep gate — both checks pass against current tree
