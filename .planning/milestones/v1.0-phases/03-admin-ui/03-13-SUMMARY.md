---
phase: 03-admin-ui
plan: 13
subsystem: testing
tags:
  - admin-ui
  - integration-tests
  - roadmap-success-criteria
  - csp
  - antiforgery
  - cross-scheme-isolation
  - mount-path
  - production-gate
  - panel-render
  - phase-gate
  - wave-6
dependencies:
  requires:
    - phase: 03-06
      provides: AdminTestHost + IHealthProbeService + IPlayerSearchService + SuperadminGateHostedService (consumed by RoadmapScenarioTests + ProductionGateTests + PanelRenderTests)
    - phase: 03-07
      provides: /admin/api/* 12-endpoint surface (consumed by every integration test in this plan; CSRF-405 anchor in CspAndAntiforgeryTests)
    - phase: 03-08
      provides: Blazor shell + App.razor + MapRazorComponents (so /admin/login + /admin/matchmaking + /admin/rankings/adjust render under MapGameKitAdmin)
    - phase: 03-09
      provides: QueueDepth.razor + RankAdjust.razor + Health.razor pages (anchored by PanelRenderTests)
    - phase: 03-10
      provides: BannedCheckHelper + Auth provider patches (no direct test in this plan; Auth ban tests live in BanEnforcementTests)
    - phase: 03-11
      provides: dotnet gamekit admin create CLI (the SC#1 RoadmapScenarioTests "bootstrap" step is covered by SeedAdminAsync, which mirrors the CLI path exercised by Cli.Tests)
  provides:
    - RoadmapScenarioTests (SC#1 — full operator journey: mount + bootstrap + login + 3-mode search)
    - ProductionGateTests (SC#2 — 404 in Production / 302 in Development / startup throw / login reachable)
    - CrossSchemeIsolationTests (SC#6 — player JWT cannot authenticate into /admin/api/*)
    - CspAndAntiforgeryTests (SC#5 — 7-directive CSP + unique nonces + scoped to /admin/* + 400 csrf_validation_failed)
    - PanelRenderTests (SC#4 — MissingPackageAlert renders + HealthReport returns 3 probes + match-history join works)
    - MountPathTests (ADMIN-02 — custom MountPath relocates API prefix; Blazor shell stays at /admin)
    - AdminTestHost.StartAsync `configureAdmin` overload — per-test GameKitAdminOptions overrides
    - AdminCspNonceMiddleware override-CSP behavior (replaces ASP.NET Core's static-SSR default with the strict GameKit policy)
  affects:
    - Phase 3 phase gate — full solution test green is now anchored to SC #1–#6
    - Future phases (04 Rankings, 05 Matchmaking) can rely on the SC#4 placeholder contract (`Install GameKit.{Package}` copy is locked by PanelRenderTests)
tech-stack:
  added: []
  patterns:
    - "SC-mapped test class naming: each ROADMAP success criterion has a dedicated test class whose [Fact(DisplayName=)] annotations begin with the matching `SC#N:` tag for traceability — Phase 3 review can grep `SC#1` … `SC#6` and see one-test-per-criterion."
    - "Admin-cookie plumbing in tests via manual Set-Cookie head extraction (TestServer's default HttpClient does NOT auto-persist cookies — mirrored from PlayerSearchEndpointTests/AdminLoginEndpointTests so all three new files use the same idiom)."
    - "Direct-Npgsql player + identity + session + participant seeding — no EF DbContext in the integration test seed paths, avoiding the FOLLOW-UP-02-03-01 two-service-provider quirk that would otherwise require AdminRuntimeQueryCustomizer plumbing per test."
    - "CSP override-on-startup: AdminCspNonceMiddleware unconditionally writes the strict GameKit policy on /admin/* responses, replacing whatever default ASP.NET Core's static-SSR pipeline emitted earlier (the ContainsKey-guarded set silently ceded to a weaker `frame-ancestors 'self'` default; the fix takes precedence)."
key-files:
  created:
    - tests/GameKit.Admin.Integration.Tests/RoadmapScenarioTests.cs
    - tests/GameKit.Admin.Integration.Tests/ProductionGateTests.cs
    - tests/GameKit.Admin.Integration.Tests/MountPathTests.cs
    - tests/GameKit.Admin.Integration.Tests/CrossSchemeIsolationTests.cs
    - tests/GameKit.Admin.Integration.Tests/CspAndAntiforgeryTests.cs
    - tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs
  modified:
    - tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs
    - src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs
key-decisions:
  - "MountPathTests asserts the documented v1 contract literally: MountPath relocates only the admin HTTP API prefix (/admin/api/*); Blazor pages remain at /admin/* (root-relative @page routes). The test asserts (a) /custom-admin-path/api/health is NOT 404; (b) /admin/api/health IS 404; (c) /admin/login IS 2xx — covering all three sides of the contract."
  - "CspAndAntiforgeryTests probes /auth/login/guest (anonymous-accepting POST) for the non-admin no-CSP-header check rather than /auth/me (Bearer-required) — the response status is irrelevant; only the absence of the Content-Security-Policy header matters for the scope contract."
  - "The PlayerJwt-rejected test in Production asserts 404 specifically (not 401, not 403) — proving the cookie scheme's RedirectToLogin → 404 short-circuit fires under AdminCookieEvents instead of a JWT Bearer 401 challenge, which would leak admin-mounted-vs-unmounted distinguishability to an unauthenticated attacker."
  - "AdminTestHost.StartAsync gains an Action<GameKitAdminOptions>? configureAdmin parameter so MountPathTests can override MountPath without a parallel test-host class. Backwards-compatible (default null) for the 49 existing call sites in 03-06/03-07/03-09/03-10 tests."
  - "Removed the if (!ContainsKey('Content-Security-Policy')) skip-guard in AdminCspNonceMiddleware. ASP.NET Core's static-SSR Blazor antiforgery pipeline emits a default CSP that uses 'frame-ancestors self' (not 'none'), and the prior guard let that weaker default through unchanged. The override is safe because the middleware ONLY runs under /admin/* (path-prefix gated)."
  - "RoadmapScenarioTests intentionally stops at three search modes (UUID / display-name / provider:external_id) and one player — running the same assertion three times with the same seeded player verifies the unified-classifier contract without exhausting the test budget on edge cases (covered separately by PlayerSearchEndpointTests in plan 03-07)."
  - "PanelRenderTests/MatchHistoryPanel seeds a completed GameSession via direct Npgsql INSERT with State='Completed' (string column via HasConversion<string>()) — tests the SessionParticipant→GameSession manual join under the no-nav-property constraint that SessionParticipantConfiguration enforces (Phase-1 GDPR cascade decision)."
patterns-established:
  - "SC-anchor test classes: One [Fact(DisplayName=\"SC#N: ...\")] per criterion per file. Future readers can `grep 'SC#1'` to locate the test that proves a roadmap claim."
  - "Mandatory CSP directive list in test code (MandatoryDirectives string array) — single source of truth for the 7 directives, asserted verbatim. Future hardening (e.g. adding `report-uri`) requires updating this list AND the middleware in lockstep."
  - "Seed-direct-via-Npgsql for cross-table fixtures: integration tests that need players + identities + sessions + participants insert directly via Npgsql instead of EF — avoids per-test EF-context-customizer plumbing, reduces flake on InMemory-vs-Postgres column-type drift."
requirements-completed:
  - ADMIN-02
  - ADMIN-03
  - ADMIN-04
  - ADMIN-09
  - ADMIN-10
  - ADMIN-12
metrics:
  duration_minutes: 18
  tasks_completed: 2
  files_created: 6
  files_modified: 2
  tests_passing:
    unit: 54
    integration: 53
  completed_date: 2026-04-26
---

# Phase 03 Plan 13: ROADMAP SC #1–#6 Integration Test Matrix Summary

**Phase 3 capped with the ROADMAP success-criteria test matrix — six new integration test files (one per SC, plus MountPathTests for ADMIN-02 supporting coverage) that together anchor every SC claim to a green automated test.**

## Performance

- **Duration:** approximately 18 min
- **Started:** 2026-04-26T (start of plan execution)
- **Completed:** 2026-04-26T (end of plan execution)
- **Tasks:** 2 (Task 1 = RoadmapScenarioTests + ProductionGateTests + MountPathTests; Task 2 = CrossSchemeIsolationTests + CspAndAntiforgeryTests + PanelRenderTests)
- **Files created:** 6
- **Files modified:** 2 (AdminTestHost gains the configureAdmin overload; AdminCspNonceMiddleware drops the ContainsKey guard)
- **Tests added:** 13 integration tests across 6 SC-anchored classes

## Accomplishments

- ROADMAP Phase 3 SC#1–SC#6 each anchored to a [Fact(DisplayName="SC#N: …")] in a dedicated test class — operator can `grep 'SC#3'` and find the one regression test that proves the criterion.
- ADMIN-02 MountPath documented v1 contract verified: API prefix relocates to the custom path; Blazor shell remains at `/admin/*`; default API prefix yields 404.
- The path-based scheme isolation that AdminBuilderExtensions wires (DefaultByPath + cookie + JwtBearer fork) is now empirically proven against a real player JWT minted by `FakePlayerJwtIssuer` — Production returns 404, Development returns non-200; never 200.
- AdminCspNonceMiddleware now unconditionally overrides any prior CSP header on `/admin/*` responses — closes the gap where ASP.NET Core's static-SSR Blazor pipeline pre-set a weaker default `frame-ancestors 'self'` that the prior `ContainsKey`-guard silently honored.
- Admin integration suite: 23 → **53** tests; all green with the new middleware override behavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: RoadmapScenarioTests + ProductionGateTests + MountPathTests** — `9a862da` (test) — 6 facts.
2. **Task 2: CrossSchemeIsolationTests + CspAndAntiforgeryTests + PanelRenderTests + middleware override fix** — `954c35e` (test) — 10 facts plus the AdminCspNonceMiddleware bug fix.

_Final SUMMARY commit will follow this file's creation._

## SC → Test File → [Fact] Map

| ROADMAP SC | Test File | [Fact] DisplayName |
|------------|-----------|---------------------|
| **#1** End-to-end mount + bootstrap + login + search | RoadmapScenarioTests.cs | `SC#1: Mount /admin, bootstrap admin via service, login, search by id + identity + displayname` |
| **#2** Unauth 404 in Production | ProductionGateTests.cs | `SC#2: Production unauthenticated GET /admin/players returns 404 (not 401, not 302)` |
| **#2** Dev redirects to login | ProductionGateTests.cs | `SC#2: Development unauthenticated GET /admin/players redirects to /admin/login` |
| **#2** Startup gate throws in Production | ProductionGateTests.cs | `SC#2: Production with no superadmin throws InvalidOperationException at host startup` |
| **#2** Login path reachable | ProductionGateTests.cs | `SC#2: Production /admin/login is reachable anonymously (operator must be able to authenticate)` |
| **#4** Match-history panel returns sessions | PanelRenderTests.cs | `SC#4: /admin/api/match-history returns completed sessions for the queried player` |
| **#4** Health panel returns 3 probes | PanelRenderTests.cs | `SC#4: /admin/api/health returns 3-probe HealthReport (Postgres + Redis + ErrorRate)` |
| **#4** Queue-depth placeholder | PanelRenderTests.cs | `SC#4: /admin/matchmaking renders MissingPackageAlert when GameKit.Matchmaking is not registered` |
| **#4** Rank-adjust placeholder | PanelRenderTests.cs | `SC#4: /admin/rankings/adjust renders MissingPackageAlert when GameKit.Rankings is not registered (superadmin)` |
| **#5** CSP header present with 7 directives | CspAndAntiforgeryTests.cs | `SC#5: /admin/login response includes Content-Security-Policy with all 7 mandatory directives` |
| **#5** Per-request unique nonces | CspAndAntiforgeryTests.cs | `SC#5: Two sequential admin responses carry different per-request nonces` |
| **#5** CSP scoped to /admin/* | CspAndAntiforgeryTests.cs | `SC#5: CSP header is scoped to /admin/* — non-admin path responses do NOT receive it` |
| **#5** Antiforgery enforced on mutations | CspAndAntiforgeryTests.cs | `SC#5: POST /admin/api/players/{id}/ban without antiforgery token returns 400 csrf_validation_failed` |
| **#6** Player JWT 404 in Production | CrossSchemeIsolationTests.cs | `SC#6: Player JWT in Bearer header cannot access /admin/api/* in Production (returns 404)` |
| **#6** Player JWT never 200 | CrossSchemeIsolationTests.cs | `SC#6: Player JWT cannot satisfy admin policy even on Development /admin/api/* (no 200)` |
| ADMIN-02 supporting | MountPathTests.cs | `MountPath: Custom MountPath relocates API prefix; Blazor shell stays at /admin` |

**Total Phase 3 SC anchors:** 15 facts spanning SC#1, SC#2, SC#4, SC#5, SC#6 (plus ADMIN-02). SC#3 (ban audit) is anchored by `BanEnforcementTests` from plan 03-10.

## Test Counts (Final Phase 3 State)

| Suite | Pre-plan | Post-plan | Δ |
|-------|----------|-----------|---|
| `GameKit.Admin.Tests` (unit) | 54 | **54** | +0 (this plan ships only integration tests) |
| `GameKit.Admin.Integration.Tests` | 23 (+ban tests from 03-10) | **53** | +13 in this plan (other deltas from waves 5-6 sibling plans) |
| `GameKit.Auth.Tests` (unit) | 35 | 35 | unchanged |
| `GameKit.Auth.Integration.Tests` | 44 | 44 | unchanged (pre-existing PendingModelChangesWarning failures documented in `deferred-items.md` are out of scope) |
| `GameKit.Core.Tests` (unit) | 130 | 130 | unchanged |
| `GameKit.Core.Integration.Tests` | 9 | 9 | unchanged |
| `GameKit.Cli.Tests` | 6 | 6 | unchanged |

**Phase 3 admin-track delta:** Admin.Tests 5 → 54; Admin.Integration.Tests 3 → 53. Total Phase 3 net new tests: ~99 (54 unit + 50 integration including ban-enforcement).

## Decisions Made

See frontmatter `key-decisions` list — 7 load-bearing decisions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] AdminCspNonceMiddleware skipped its own CSP when ASP.NET Core's static-SSR Blazor pipeline had pre-set one**
- **Found during:** Task 2 GREEN — `CspAndAntiforgeryTests.AdminResponse_Has_ContentSecurityPolicy_Header` failed asserting `default-src 'self'` against an actual response that read `frame-ancestors 'self'` and nothing else. The actual CSP on `/admin/login` was the ASP.NET Core static-SSR antiforgery default, NOT the GameKit middleware's policy.
- **Issue:** The middleware's `OnStarting` callback ran `if (!ctx.Response.Headers.ContainsKey("Content-Security-Policy")) { ... }`, ceding the response to whatever weaker CSP the antiforgery middleware (or any earlier middleware) had emitted. SC#5's contract is "the GameKit admin policy is shipped on every admin response" — the guard caused the test to assert the documented invariant, see the framework default, and fail.
- **Fix:** Removed the ContainsKey guard. The middleware now writes its strict policy unconditionally inside `OnStarting`. The middleware ONLY runs under the admin path prefix (the early-return at the top of `InvokeAsync`), so the override cannot affect non-admin responses.
- **Files modified:** `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs`.
- **Verification:** All 4 facts in `CspAndAntiforgeryTests` pass. The 4 unit tests in `AdminCspNonceMiddlewareTests` (which never pre-set a CSP before invoking the middleware) continue to pass.
- **Committed in:** `954c35e` (Task 2 commit).
- **Threat-model alignment:** Closes T-03-13-02 (CSP test asserts weaker policy than spec) — the test now passes ONLY when every documented directive is present in the response header, regardless of any prior middleware's intent.

---

**Total deviations:** 1 auto-fixed (1 Rule-1 correctness bug).
**Impact on plan:** None on scope or acceptance criteria. The fix is the textbook solution for "your middleware must take precedence" and matches the documented threat-model intent for SC#5.

## Issues Encountered

**Pre-existing Auth integration test failures:** `dotnet test` against the full solution shows 38/44 failures in `GameKit.Auth.Integration.Tests` with the error `Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning`. These were verified pre-existing (reproducible on the base branch via `git stash`); they are outside this plan's scope per the deviation rules' scope-boundary. Documented in `.planning/phases/03-admin-ui/deferred-items.md` (originally captured by plan 03-10 executor; persisted through plans 03-11 and 03-13).

## Threat Flags

None. The threat-model entries T-03-13-01 through T-03-13-05 are all addressed:

- T-03-13-01 (Spoofing: SC#6 fails) — `CrossSchemeIsolationTests` asserts player JWT → 404 in Production AND ≠ 200 in Development. Dual coverage means a regression in either env surfaces immediately.
- T-03-13-02 (Tampering: weaker CSP slips through) — Mandatory directive list iterated verbatim against the response; the middleware override fix closes the only known bypass path.
- T-03-13-03 (Info Disclosure: 401 vs 404 leak) — `Production_UnauthenticatedGET_AdminPath_Returns404` asserts 404 specifically (not 401, not 302).
- T-03-13-04 (EoP: missing-package alert exposes sibling DLLs) — `PanelRenderTests` asserts the placeholder copy is the documented `Install GameKit.{Package}` literal; nothing reflects sibling DLL contents into the response. Accept disposition is preserved.
- T-03-13-05 (DoS: Testcontainers churn) — Accept disposition; OPS-08 mandates Testcontainers. Per-test isolation is more valuable than CI speed for the SC matrix.

## Known Stubs

None. Every test class ships fully-wired assertions. No `[Skip(...)]` attributes on any new fact.

## Self-Check: PASSED

Verification run after writing this SUMMARY:

- File existence checks (6 created files):
  - `tests/GameKit.Admin.Integration.Tests/RoadmapScenarioTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/ProductionGateTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/MountPathTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/CrossSchemeIsolationTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/CspAndAntiforgeryTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs` — FOUND
- Modified files (2):
  - `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` — `configureAdmin` overload added
  - `src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs` — ContainsKey guard removed
- Commit existence checks:
  - `9a862da` — FOUND (Task 1)
  - `954c35e` — FOUND (Task 2)
- `dotnet test tests/GameKit.Admin.Integration.Tests/` — **53/0/0** green (all admin integration tests passing on master).
- `dotnet test tests/GameKit.Admin.Tests/` — **54/0/0** green (no regression in unit tests after middleware change).
- `dotnet build GameKit.sln` — 17 projects / 0 warnings / 0 errors.

## Phase 3 Ready-to-Ship Checklist

- [x] All 12 ADMIN-XX requirements anchored to integration tests (per Phase Requirements → Test Map in `03-RESEARCH.md`).
- [x] All 6 ROADMAP Phase 3 success criteria anchored to a [Fact(DisplayName="SC#N:...")] (one per criterion).
- [x] `AdminCspNonceMiddleware` is the single source of truth for the admin CSP — overrides any prior weaker policy.
- [x] `MountPath` v1 contract documented and tested (API prefix relocates; Blazor shell stays at /admin).
- [x] `dotnet test tests/GameKit.Admin.Integration.Tests/` green (53/0/0).
- [x] `dotnet test tests/GameKit.Admin.Tests/` green (54/0/0).
- [x] Plan 03-12 (TicTacToeDuel sample wiring) at human-verify checkpoint — does NOT block this plan; sample work is decoupled from the SC test matrix.
- [ ] Plan 03-12 human-verify completion remains as the only Phase 3 close-out gap (operator walkthrough pending; tracked in STATE.md).

## Next Steps

- Phase 3 close-out depends only on plan 03-12's pending human-verify checkpoint (operator walkthrough of the TicTacToeDuel sample). This plan's SC matrix is independent of the sample app.
- Future phases (Phase 4 Rankings) can rely on the `Install GameKit.Rankings` placeholder copy (locked here) when wiring `MapGameKitRankings` integration with the admin UI.
- The pre-existing Auth integration `PendingModelChangesWarning` failures (deferred-items.md) should be picked up by a Phase 3 gap plan or the first Phase 4 plan to touch a migration. Not blocking phase closure for the SC test matrix.

---
*Phase: 03-admin-ui*
*Plan: 13*
*Completed: 2026-04-26*
