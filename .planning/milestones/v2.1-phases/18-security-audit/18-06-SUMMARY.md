---
phase: 18-security-audit
plan: "06"
subsystem: documentation
tags: [security, traceability, checklist, stride, sec-08]
requirements: [SEC-08]
dependency_graph:
  requires: ["18-01", "18-02", "18-03", "18-04", "18-05"]
  provides: [security-checklist, threat-traceability, sec-01-08-mapping]
  affects: [docs/security-checklist.md]
tech_stack:
  added: []
  patterns:
    - STRIDE threat table over five security surfaces
    - requirement → implementation → test traceability table
key_files:
  created:
    - docs/security-checklist.md
  modified: []
decisions:
  - "Checklist cites only greppable, shipped test classes — all 8 confirmed present in repo before writing"
  - "CVE section describes MessagePack 3.1.7 upgrade as eliminating GHSA-hv8m-jj95-wg3x (not suppressed)"
  - "Egress section reflects as-built approach b (provider packages append their own hosts at registration time)"
  - "Rate-limit section explicitly documents logout exclusion per RFC 7009 (intentional, not a gap)"
metrics:
  duration: "~4 minutes"
  completed: "2026-06-23"
  tasks_completed: 1
  tasks_total: 1
  files_changed: 1
status: complete
---

# Phase 18 Plan 06: SEC-08 Security Checklist Summary

**One-liner:** `docs/security-checklist.md` maps all five security surfaces (JWT, admin, rate-limit, egress, GDPR) plus the CVE gate from threat-model entry to implementation file to shipped test class, giving auditors a single traceable document for the Phase 18 security posture.

## What Was Built

### Task 1: docs/security-checklist.md (f80b190)

Created `docs/security-checklist.md` with nine sections:

1. **Threat Model Summary** — STRIDE table (10 threats) covering auth, admin, GDPR, egress, and supply-chain surfaces. Each row names the STRIDE category, the attacked surface, the specific threat, and the mitigation.

2. **JWT Security Controls** — `TokenValidationParameters` configuration from `AuthBuilderExtensions.cs:190-210`: `RequireSignedTokens=true`, RSA-SHA256 signing, `ValidateIssuer/Audience/Lifetime=true`. Control table mapping each `TokenValidationParameters` property to the attack it prevents.

3. **Admin Security Controls** — Cookie scheme isolation (`GameKitAdmin`), `AdminCookieEvents` challenge-suppression to 404 in Production, `AntiforgeryValidationFilter` returning exactly HTTP 400 + `"csrf_validation_failed"`. Full endpoint authorization inventory (17 rows including the `admin/login/submit` form POST handler).

4. **Rate Limiting** — Policy inventory for auth and admin endpoints; deliberate-exclusion table for logout/me/challenge/callback/link with per-endpoint rationale. Rate-limit policy thresholds and partition keys (IP + fingerprint).

5. **GDPR Delete Completeness** — Full FK table map (13 tables) with `OnDelete` behavior, gap status, and fix for GAP 1 (`party_members` RESTRICT) and GAP 2 (`account_merges` RESTRICT). `IGdprDeleteExtension` architecture (Option A, mirrors `IModelBuilderExtension` pattern); Auth and Matchmaking implementations.

6. **Egress Controls** — `EgressAllowListHandler` DelegatingHandler; allowed-hosts inventory by provider package; Apple/Google gap closure (approach b, Phase 18-05); two CI grep gates (bare `HttpClient` + SaaS telemetry hostnames).

7. **Refresh Token Security** — SHA-256 storage (`Sha256Hex`); call sites in `IssueRootAsync`, `RotateAsync`, `RevokeFamilyAsync`; rotation and family revocation mechanics.

8. **CVE Gate** — `NuGetAuditMode=all` + `NuGetAuditLevel=high` in `Directory.Build.props`; explicit CI vulnerability scan step; MessagePack 3.1.7 transitive pin that **eliminates** GHSA-hv8m-jj95-wg3x by upgrade (not suppression); `NuGetAuditSuppress` zero-entry confirmation; `-p:NuGetAudit=false` declared obsolete.

9. **Traceability Table** — SEC-01..08 mapped to implementation file(s) and test class. All 8 shipped test classes cited:
   - `JwtThreatModelTests` (GameKit.Auth.Tests)
   - `RevokedRefreshExchangeTests` (GameKit.Auth.Integration.Tests)
   - `AdminRouteAuthAuditTests` (GameKit.Admin.Integration.Tests)
   - `AuthRateLimitAuditTests` (GameKit.Auth.Tests)
   - `GdprDeleteCoverageTests` (GameKit.Core.Integration.Tests)
   - `EgressAuditTests` (GameKit.Auth.Tests)
   - `RefreshTokenHashingTests` (GameKit.Auth.Integration.Tests)
   - `CsrfRegressionTests` (GameKit.Admin.Integration.Tests)

## Deviations from Plan

None — plan executed exactly as written.

Pre-writing verification confirmed all 8 test classes are greppable in the repo (`grep -r "class $t" tests/ --include="*.cs"`). The checklist reflects as-built reality from the five prior SUMMARY files (18-01 through 18-05), not the plan's original spec.

## Verification

### Automated verify step (plan-specified bash loop)

```
bash -c 'test -f docs/security-checklist.md && for t in JwtThreatModelTests \
  RevokedRefreshExchangeTests AdminRouteAuthAuditTests AuthRateLimitAuditTests \
  GdprDeleteCoverageTests EgressAuditTests RefreshTokenHashingTests CsrfRegressionTests; do \
  grep -q "$t" docs/security-checklist.md || { echo "MISSING ref: $t"; exit 1; }; done; \
  echo "checklist references all shipped tests"'

checklist references all shipped tests
```

All 8 test classes confirmed: PASSED.

## Threat Mitigations Applied

| Threat ID | Status | Mitigation |
|-----------|--------|-----------|
| T-18-06-01 (Repudiation — undocumented security claim) | RESOLVED | Traceability table cites only greppable, shipped test classes; verify step fails if any cited test is missing |

## Known Stubs

None. Every cited test class, implementation file, and implementation detail reflects shipped, greppable code.

## Threat Flags

None. `docs/security-checklist.md` is a documentation file with no new network surface.

## Self-Check: PASSED

- [x] `docs/security-checklist.md` exists — FOUND
- [x] All 8 test class names greppable in checklist — VERIFIED (bash loop passed)
- [x] CVE section states upgrade (not suppression) — VERIFIED (section 8 text confirmed)
- [x] Commit `f80b190` exists — VERIFIED (`git log --oneline | head -1`)
- [x] No deletions in commit — VERIFIED (`git diff --diff-filter=D HEAD~1 HEAD` returned empty)
