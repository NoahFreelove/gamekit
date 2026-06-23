---
phase: 18-security-audit
verified: 2026-06-23T00:00:00Z
status: passed
score: 5/5
behavior_unverified: 0
overrides_applied: 0
re_verification: false
---

# Phase 18: Security Audit — Verification Report

**Phase Goal:** Every auth/admin/GDPR/egress/rate-limit security invariant is verified by an automated test and a CI gate; known CVEs are impossible to merge undetected; threat model traceable requirement→implementation→test.

**Verified:** 2026-06-23
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | CVE gate (`NuGetAuditMode=all`) fails build on high/critical CVE; MessagePack pinned 3.1.7 → full solution builds CLEAN with NO `-p:NuGetAudit=false` suppression | VERIFIED | `dotnet build GameKit.sln --configuration Release -warnaserror` → Build succeeded, 0 Warning(s), 0 Error(s). `Directory.Build.props` line 43: `<NuGetAuditMode>all</NuGetAuditMode>`, line 44: `<NuGetAuditLevel>high</NuGetAuditLevel>`. `Directory.Packages.props` lines 110-111: `MessagePack` and `MessagePack.Annotations` pinned to `3.1.7`. No `NuGetAuditSuppress` entry exists. No `-p:NuGetAudit=false` anywhere in ci.yml, Directory.Build.props, or Directory.Packages.props. `dotnet list package --vulnerable --include-transitive` returns no HIGH/CRITICAL rows. CI has explicit `Vulnerability scan (SEC-07 gate)` step that exits non-zero on High/Critical. Commits `fe47f30` (Task 1) and `5a495ff` (Task 2) present. |
| 2 | JWT threat tests reject alg:none, wrong aud/iss, expired, revoked-refresh-exchange | VERIFIED | `JwtThreatModelTests` (5 facts): `AlgNone_Token_Is_Rejected`, `HmacDowngrade_Token_Is_Rejected`, `WrongIssuer_Token_Is_Rejected`, `WrongAudience_Token_Is_Rejected`, `Expired_Token_Is_Rejected` — all 5 pass (live run: 5/5 Passed). `RevokedRefreshExchangeTests` (2 facts): `Revoked_RefreshToken_Cannot_Be_Exchanged`, `NeverIssued_RefreshToken_Returns_401` — both pass (live run: 2/2 Passed). `JwtThreatModelTests` uses `ProductionParams()` mirroring `AuthBuilderExtensions.cs` lines 199-210. Commits `040be46` and `f73a803` present. |
| 3 | Admin route-enum test: every /admin/* requires GameKitAdmin cookie, player JWT → 401/403; CSRF: state-changing admin call w/o antiforgery → 400 | VERIFIED | `AdminRouteAuthAuditTests` (2 facts): `AllAdminRoutes_Either_AreAnonymousAllowlisted_Or_HaveAdminPolicy` (dynamic `EndpointDataSource` walk), `PlayerJwt_IsRejected_OnExistingAdminRoute` (existence-guarded behavioral check) — both pass (live run: 2/2 Passed). `CsrfRegressionTests` (3 facts): `BanMutation_Without_Antiforgery_Returns_Exactly_400`, `UnbanMutation_Without_Antiforgery_Returns_Exactly_400`, `DeleteAdminMutation_Without_Antiforgery_Returns_Exactly_400` — all 3 pass (live run: 3/3 Passed). Commits `18541ea` and `12567ce` present. |
| 4 | GdprDeleteCoverage: seed player across EVERY FK table (incl lobby_members, party_members, matchmaking_tickets, account_merges) → zero residual post-DeletePlayerAsync | VERIFIED | `GdprDeleteCoverageTests` (1 fact, integration): passes (live run: 1/1 Passed). Seeds player across all 13 FK tables including `matchmaking_tickets` (SC#4 — transitive via `parties.OwnerPlayerId CASCADE → matchmaking_tickets.PartyId SET NULL`). `IGdprDeleteExtension` interface exists in `src/GameKit.Core/Services/IGdprDeleteExtension.cs`. `AuthGdprDeleteExtension` deletes `account_merges WHERE TargetPlayerId == playerId` via `ExecuteDeleteAsync`. `MatchmakingGdprDeleteExtension` deletes `party_members WHERE PlayerId == playerId` via `ExecuteDeleteAsync`. Both registered via `TryAddEnumerable(Scoped<IGdprDeleteExtension, ...>)` in `AddAuth` and `AddMatchmaking`. `GdprDeleteService` invokes extensions inside SERIALIZABLE transaction between audit `SaveChangesAsync` and `players.ExecuteDeleteAsync`. `GameKit.Core` has zero upward `ProjectReference` to Auth or Matchmaking (verified by grep). Commits `9c725b1`, `e08517d`, `30a9a9a` present. |
| 5 | Egress: no outbound HTTP beyond OAuth hosts (incl Apple/Google now wired); no SaaS OTLP string in samples/ or src/ | VERIFIED | `EgressAuditTests` (19 facts, unit): passes (live run: 19/19 Passed). Apple backchannel wired via `apple.BackchannelHttpHandler = new EgressAllowListHandler(resolvedOpts) { InnerHandler = new HttpClientHandler() }` in `AppleBuilderExtensions.cs`. Google same pattern in `GoogleBuilderExtensions.cs`. `AppleProviderHosts = ["appleid.apple.com"]` and `GoogleProviderHosts = ["oauth2.googleapis.com", "www.googleapis.com", "accounts.google.com"]` declared public. Grep of `src/` and `samples/` for `honeycomb.io|datadoghq.com|newrelic.com|grafana-cloud|grafana.net|lightstep.com` returns zero hits. CI has static egress + air-gap gate step (check 1: bare `new HttpClient(`; check 2: SaaS telemetry hostnames). `RefreshTokenHashingTests` (3 facts): `IssueRootAsync_Stores_Sha256Hex_Not_RawToken`, `RotateAsync_Stores_Sha256Hex_For_Child_Token`, `NoColumn_Contains_RawToken_As_Literal` — all 3 pass (live run: 3/3 Passed). Commits `cc29e60`, `ec04731`, `d76722c` present. |

**Score:** 5/5 truths verified (0 present-behavior-unverified)

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Directory.Packages.props` | MessagePack + MessagePack.Annotations transitive pin to 3.1.7 | VERIFIED | Lines 110-111 contain both pins with SEC-07 comment citing GHSA-hv8m-jj95-wg3x. No NuGetAuditSuppress entries. |
| `Directory.Build.props` | NuGetAuditMode=all + NuGetAuditLevel=high | VERIFIED | Lines 43-44; comment explains SEC-07 gate and -p:NuGetAudit=false is obsolete. |
| `.github/workflows/ci.yml` | Explicit `dotnet list package --vulnerable --include-transitive` CI step + static egress gate | VERIFIED | Lines 112-100: `Vulnerability scan (SEC-07 gate)` step exits non-zero on High/Critical. Lines 42-100: static egress gate with 2 grep checks. No -p:NuGetAudit=false anywhere in workflow. |
| `src/GameKit.Core/Services/IGdprDeleteExtension.cs` | Public interface for package-owned pre-delete cleanup | VERIFIED | Interface present, fully XML-documented, transaction contract documented, TryAddEnumerable registration guidance documented. |
| `src/GameKit.Auth/Services/AuthGdprDeleteExtension.cs` | Deletes account_merges WHERE TargetPlayerId = playerId | VERIFIED | `internal sealed` implementation, `ExecuteDeleteAsync` on `AccountMerge` with `TargetPlayerId == playerId` predicate. |
| `src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs` | Deletes party_members WHERE PlayerId = playerId | VERIFIED | `internal sealed` implementation, `ExecuteDeleteAsync` on `PartyMember` with `PlayerId == playerId` predicate. |
| `src/GameKit.Core/Services/GdprDeleteService.cs` | Invokes IEnumerable<IGdprDeleteExtension> inside SERIALIZABLE tx | VERIFIED | Lines 90-93: foreach loop over `_extensions`, invoked after audit `SaveChangesAsync` and before `players.ExecuteDeleteAsync`. |
| `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs` | Apple backchannel through EgressAllowListHandler | VERIFIED | `AppleProviderHosts = ["appleid.apple.com"]` public; `apple.BackchannelHttpHandler` assigned at AddApple time. |
| `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs` | Google backchannel through EgressAllowListHandler | VERIFIED | `GoogleProviderHosts = ["oauth2.googleapis.com", "www.googleapis.com", "accounts.google.com"]` public; `google.BackchannelHttpHandler` assigned at AddGoogle time. |
| `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs` | 5 JWT forgery rejection facts | VERIFIED | 5 facts present, all pass live. |
| `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` | 2 revoked/unknown refresh token facts | VERIFIED | 2 facts present, both pass live. |
| `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs` | Dynamic admin route audit + player-JWT rejection | VERIFIED | 2 test methods, both pass live. Dynamic EndpointDataSource walk, not a hardcoded list. Existence guard before behavioral assertion. |
| `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs` | 4 rate-limit audit facts (login/refresh/register covered; logout exclusion documented) | VERIFIED | 4 methods present, all pass live. Uses `EnableRateLimitingAttribute` (correct public type). |
| `tests/GameKit.Core.Integration.Tests/GdprDeleteCoverageTests.cs` | All-FK-tables GDPR delete completeness | VERIFIED | 1 fact, passes live. Seeds matchmaking_tickets (SC#4 transitive coverage explicitly documented in-test). |
| `tests/GameKit.Auth.Tests/EgressAuditTests.cs` | 19 egress allow/deny facts including Apple/Google hosts | VERIFIED | 19 facts, all pass live. |
| `tests/GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs` | SHA-256 stored, not raw | VERIFIED | 3 facts, all pass live. |
| `tests/GameKit.Admin.Integration.Tests/CsrfRegressionTests.cs` | Admin mutation without antiforgery → exactly 400 | VERIFIED | 3 facts, all pass live. Assert.Equal(HttpStatusCode.BadRequest, ...). |
| `docs/security-checklist.md` | Threat→implementation→test traceability for all 5 surfaces | VERIFIED | File exists, 9 sections present, all 8 test classes cited. Traceability table covers SEC-01..08. MessagePack section states "This is an upgrade, NOT a suppression." `bash` verification loop passes. |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Directory.Build.props` | All csproj | `NuGetAuditMode` inherited via MSBuild property | VERIFIED | `<NuGetAuditMode>all</NuGetAuditMode>` in common `<PropertyGroup>`, solution-wide effect confirmed by clean build. |
| `Directory.Packages.props` | SignalR transitive chain | `CentralPackageTransitivePinningEnabled=true` propagates MessagePack 3.1.7 | VERIFIED | `dotnet list package --vulnerable` returns zero HIGH/CRITICAL; MessagePack 2.5.187 gone. |
| `GdprDeleteService` | `IGdprDeleteExtension` | `IEnumerable<IGdprDeleteExtension>` constructor injection, foreach loop inside SERIALIZABLE tx | VERIFIED | Lines 22, 37, 90-93 of GdprDeleteService.cs. |
| `AuthBuilderExtensions.AddAuth` | `AuthGdprDeleteExtension` | `TryAddEnumerable(Scoped<IGdprDeleteExtension, AuthGdprDeleteExtension>())` | VERIFIED | Line 69 of AuthBuilderExtensions.cs. |
| `MatchmakingBuilderExtensions.AddMatchmaking` | `MatchmakingGdprDeleteExtension` | `TryAddEnumerable(Scoped<IGdprDeleteExtension, MatchmakingGdprDeleteExtension>())` | VERIFIED | Line 95 of MatchmakingBuilderExtensions.cs. |
| `AppleBuilderExtensions.AddApple` | `EgressAllowListHandler` | `apple.BackchannelHttpHandler = new EgressAllowListHandler(resolvedOpts)` | VERIFIED | Lines 150-154 of AppleBuilderExtensions.cs. |
| `GoogleBuilderExtensions.AddGoogle` | `EgressAllowListHandler` | `google.BackchannelHttpHandler = new EgressAllowListHandler(resolvedOpts)` | VERIFIED | Lines 122-126 of GoogleBuilderExtensions.cs. |
| `docs/security-checklist.md` | All 8 test classes | Traceability table cites each by name | VERIFIED | Bash loop: `checklist references all shipped tests: PASSED`. |

---

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full solution builds clean (no audit suppression) | `dotnet build GameKit.sln --configuration Release -warnaserror` | Build succeeded. 0 Warning(s). 0 Error(s). | PASS |
| No HIGH/CRITICAL CVEs in dependency graph | `dotnet list package --vulnerable --include-transitive` | No HIGH or CRITICAL rows | PASS |
| JWT threat tests — 5 forgery cases rejected | `dotnet test tests/GameKit.Auth.Tests --filter FullyQualifiedName~JwtThreatModel` | 5/5 Passed | PASS |
| Rate-limit audit — login/refresh/register covered | `dotnet test tests/GameKit.Auth.Tests --filter FullyQualifiedName~AuthRateLimitAudit` | 4/4 Passed | PASS |
| Egress audit — 19 allow/deny facts including Apple/Google | `dotnet test tests/GameKit.Auth.Tests --filter FullyQualifiedName~EgressAudit` | 19/19 Passed | PASS |
| Revoked refresh token rejected at /auth/refresh | `dotnet test tests/GameKit.Auth.Integration.Tests --filter FullyQualifiedName~RevokedRefreshExchange` | 2/2 Passed | PASS |
| Refresh token stored as SHA-256, not raw | `dotnet test tests/GameKit.Auth.Integration.Tests --filter FullyQualifiedName~RefreshTokenHashing` | 3/3 Passed | PASS |
| Admin route-enum + player JWT rejected | `dotnet test tests/GameKit.Admin.Integration.Tests --filter FullyQualifiedName~AdminRouteAuthAudit` | 2/2 Passed | PASS |
| CSRF: admin mutation without antiforgery → 400 | `dotnet test tests/GameKit.Admin.Integration.Tests --filter FullyQualifiedName~CsrfRegression` | 3/3 Passed | PASS |
| GDPR: all-FK-tables zero residual after DeletePlayerAsync | `dotnet test tests/GameKit.Core.Integration.Tests --filter FullyQualifiedName~GdprDeleteCoverage` | 1/1 Passed | PASS |
| No SaaS telemetry hostnames in src/ or samples/ | `grep -rniE 'honeycomb\.io\|datadoghq\.com...' src/ samples/` | Zero hits | PASS |
| Security checklist references all 8 test classes | bash loop verifying each class name in docs/security-checklist.md | "checklist references all shipped tests: PASSED" | PASS |

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| SEC-01 | 18-03 | JWT threat model: alg:none, downgrade, wrong aud/iss, expired, revoked refresh all rejected | SATISFIED | `JwtThreatModelTests` (5/5), `RevokedRefreshExchangeTests` (2/2) — all live-verified |
| SEC-02 | 18-04 | Admin route enumeration: every /admin/* requires GameKitAdmin cookie scheme | SATISFIED | `AdminRouteAuthAuditTests` (2/2) — dynamic EndpointDataSource walk, player JWT rejected |
| SEC-03 | 18-04 | Rate limiting: login/refresh/register carry enforced policies; logout exclusion documented | SATISFIED | `AuthRateLimitAuditTests` (4/4) — login, refresh, register have `EnableRateLimitingAttribute`; logout confirmed excluded |
| SEC-04 | 18-02 | GDPR delete completeness: all FK tables covered; party_members + account_merges RESTRICT gaps fixed | SATISFIED | `GdprDeleteCoverageTests` (1/1) — seeds across 13 FK tables including matchmaking_tickets (SC#4 transitive), asserts zero residual |
| SEC-05 | 18-05 | Egress air-gap: no outbound HTTP beyond OAuth hosts; Apple/Google backchannel wired; no SaaS OTLP in src/samples/ | SATISFIED | `EgressAuditTests` (19/19); BackchannelHttpHandler wired in AppleBuilderExtensions + GoogleBuilderExtensions; CI static grep gate present and passes |
| SEC-06 | 18-05 | Refresh token SHA-256 storage; CSRF → exactly 400 | SATISFIED | `RefreshTokenHashingTests` (3/3); `CsrfRegressionTests` (3/3) |
| SEC-07 | 18-01 | CVE gate: NuGetAuditMode=all; MessagePack 3.1.7 upgrade eliminates GHSA-hv8m-jj95-wg3x; solution builds clean without suppression | SATISFIED | `dotnet build -warnaserror` clean; MessagePack 3.1.7 in Directory.Packages.props; no NuGetAuditSuppress; CI vulnerability scan step present |
| SEC-08 | 18-06 | Security checklist: threat→implementation→test traceability document for all 5 surfaces | SATISFIED | `docs/security-checklist.md` exists, 9 sections, all 8 test classes cited, SEC-01..08 traceability table complete |

---

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| — | None found | — | Clean |

No `TBD`, `FIXME`, or `XXX` markers found in any Phase 18-modified files. No placeholder returns or stub implementations. No stubs in test assertions (all live-running against real containers or real validators).

---

### Human Verification Required

The 18-VALIDATION.md identifies one manual item:

**1. Security checklist prose completeness (SEC-08)**

- **Test:** Review `docs/security-checklist.md` — does it map every SEC surface threat→implementation→test with accurate prose?
- **Expected:** Nine sections covering JWT, admin, rate-limit, GDPR, egress, refresh token, CVE gate, and traceability table, with no false or missing claims.
- **Why human:** Checklist narrative quality and completeness is a human judgment call — the automated loop only verifies test class names are cited, not that the prose is accurate.

Note: The automated bash loop (`checklist references all shipped tests: PASSED`) and the traceability table coverage (SEC-01..08 all mapped) provide strong evidence the checklist is substantive. The prose has been spot-checked and appears accurate. This human review is low-risk given automated validation.

---

### Gaps Summary

No gaps. All 5 success criteria verified by live test runs and codebase inspection:

1. SEC-07 CVE gate: `NuGetAuditMode=all` active, MessagePack 3.1.7 pin present as upgrade (not suppression), full solution builds clean without `-p:NuGetAudit=false`, CI vulnerability scan step present. VERIFIED.

2. SEC-01 JWT tests: All 7 facts across `JwtThreatModelTests` + `RevokedRefreshExchangeTests` pass live. Token validation parameters mirror production configuration. VERIFIED.

3. SEC-02/03 Admin route + CSRF: `AdminRouteAuthAuditTests` dynamically walks `EndpointDataSource`, player JWT rejected on existing route. `CsrfRegressionTests` asserts exactly HTTP 400 on tokenless mutations. `AuthRateLimitAuditTests` confirms login/refresh/register all have rate-limit metadata. All pass live. VERIFIED.

4. SEC-04 GDPR delete: `IGdprDeleteExtension` pattern implemented package-boundary-clean. Both RESTRICT FK gaps (party_members, account_merges) fixed. `GdprDeleteCoverageTests` seeds matchmaking_tickets (SC#4 transitive coverage documented in-test). All-FK-tables test passes live. Core package boundary preserved (no upward references). VERIFIED.

5. SEC-05 Egress: Apple/Google backchannel wired through `EgressAllowListHandler` at AddApple/AddGoogle time (approach b). Host lists co-located with provider packages. No SaaS telemetry hostnames in codebase. CI static grep gate present. `EgressAuditTests` passes 19/19 live. VERIFIED.

---

## VERIFICATION COMPLETE

**Status:** PASSED
**Score:** 5/5 must-haves verified
**All 8 security test classes pass live** (unit + integration, no -p:NuGetAudit=false needed)
**Full solution builds clean under NuGetAuditMode=all with no suppression flags**

Pre-existing failures noted as NOT Phase 18 regressions:
- `Migrate_Twice_Is_Idempotent` in Core.Integration.Tests (stale assertion pre-dating Phase 13, documented in MEMORY.md)
- 2 HealthProbeTests in Admin.Integration.Tests (pre-existing container timing flakiness from Phase 14, documented in 18-04-SUMMARY.md)

The single human verification item (security checklist prose completeness) is low-risk given the automated traceability loop passes and the table is substantively correct per spot-check. Phase goal is achieved.

---

_Verified: 2026-06-23_
_Verifier: Claude (gsd-verifier)_
