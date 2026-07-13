---
phase: 18
slug: security-audit
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-23
---

# Phase 18 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11 (Postgres) + WebApplicationFactory/TestServer + Moq |
| **Quick run command** | `dotnet test tests/GameKit.Auth.Tests` |
| **Full suite command** | `dotnet test GameKit.sln` (after SEC-07: NO `-p:NuGetAudit=false` needed — gate is ON and clean) |
| **Estimated runtime** | ~120–300 s (GDPR completeness spins Postgres) |

---

## Sampling Rate

- **After every task commit:** affected package's unit suite
- **After every plan wave:** affected-package full suites incl. Integration.Tests
- **Before verification:** full affected-package suites green; full-solution build green under `NuGetAuditMode=all` (no audit suppression flag)
- **Max feedback latency:** ~300 s

---

## Per-Task Verification Map

| Task | Requirement | Secure Behavior | Test Type | Automated Command | Status |
|------|-------------|-----------------|-----------|-------------------|--------|
| CVE gate + MessagePack pin | SEC-07 | Build fails on high/critical CVE; MessagePack pinned 3.1.7 so build is CLEAN with gate ON | build gate | `dotnet build GameKit.sln` (no NuGetAudit=false) → 0 errors | ⬜ |
| JWT threat tests | SEC-01 | alg:none / wrong aud/iss / expired / revoked-refresh all rejected | unit/integration | `dotnet test tests/GameKit.Auth.Tests --filter JwtThreat` | ⬜ |
| Admin route-enum | SEC-02 | every `/admin/*` requires GameKitAdmin cookie; player JWT → 401/403 | integration (enum EndpointDataSource) | `dotnet test tests/GameKit.Admin.Integration.Tests --filter RouteAuth` | ⬜ |
| Rate-limit enum | SEC-03 | every auth write endpoint has an enforced rate-limit policy | integration (enum endpoints) | `dotnet test tests/GameKit.Auth.Integration.Tests --filter RateLimit` | ⬜ |
| GDPR completeness | SEC-04 | seed player across ALL FK tables (incl party_members, account_merges, lobby_members, matchmaking_tickets) → zero residual after DeletePlayerAsync | integration (Testcontainers PG) | `dotnet test ...Integration.Tests --filter GdprDeleteCoverage` | ⬜ |
| Egress allow-list | SEC-05 | no outbound HTTP beyond OAuth hosts; no SaaS OTLP string in src/ or samples/ | unit + static grep | handler test + `dotnet test --filter Egress` + CI grep | ⬜ |
| Refresh-hash + CSRF | SEC-06 | stored refresh token is SHA-256 (never raw); admin state-change w/o antiforgery → 400 | unit + integration | `dotnet test --filter "RefreshHash|Csrf"` | ⬜ |
| Security checklist | SEC-08 | docs/security-checklist.md maps threat→impl→test for all 5 surfaces | docs presence + link | file-exists + content assertion | ⬜ |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] SEC-07 CVE gate (NuGetAuditMode=all in Directory.Build.props) + MessagePack 3.1.7 pin in Directory.Packages.props — FIRST task; full solution must then build clean
- [ ] `GdprDeleteCoverageTests` (Testcontainers) — the all-tables completeness test (SEC-04); will FAIL until DeletePlayerAsync handles party_members + account_merges RESTRICT FKs
- [ ] JWT threat test suite (SEC-01)
- [ ] Admin route-enum + rate-limit-enum tests (SEC-02/03)
- [ ] Egress handler/static tests (SEC-05); refresh-hash + CSRF tests (SEC-06)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Security checklist prose completeness | SEC-08 | threat-model mapping quality is human-judged | Review docs/security-checklist.md maps every SEC surface threat→impl→test |

*All functional behaviors have automated verification; only the checklist prose is manual.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 300s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
