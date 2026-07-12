---
phase: quick-260712-hdx
plan: 01
subsystem: testing
tags: [gdpr, rankings, admin-ui, account-merge, playwright, nuget-audit]

requires:
  - phase: 04-rankings-sessions-gdpr
    provides: GDPR export endpoints (RankingsPlayerEndpoints, RankingsAdminEndpoints) + EndSeasonDialog/RankAdjustDialog admin-UI verbs
  - phase: 10-account-merge
    provides: AccountMergeService + POST /admin/api/players/merge endpoint
provides:
  - RankingsExportEndpointTests.cs — 3 HTTP-layer tests closing 04-HUMAN-UAT item 5
  - Headless-browser evidence for 4 admin-UI/merge UAT items (browser-results.json + screenshots + DB verification log)
  - Two Directory.Packages.props transitive-pin security fixes (Scriban.Signed 7.2.5, Microsoft.OpenApi 2.10.0)
  - Updated 04-HUMAN-UAT.md (items 5/6/8/9) and 10-VERIFICATION.md (status flipped to verified)
affects: [04-rankings-sessions-gdpr, 10-account-merge, ci, nuget-audit]

tech-stack:
  added: []
  patterns:
    - "In-test AuthenticationHandler<AuthenticationSchemeOptions> reading X-Test-Sub/X-Test-Role headers, set as default authenticate+challenge scheme, for HTTP-layer endpoint tests that don't need a full JWT/cookie auth pipeline"
    - "Transitive NuGet vulnerability pins in Directory.Packages.props (mirroring the Phase 18-01 MessagePack precedent) — stay on the same major line as the incoming transitive version to avoid source-breaking upgrades"

key-files:
  created:
    - tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs
    - .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/browser-results.json
    - .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/ (4 screenshots, 2 merge-response.json, db-verification-log.txt)
    - .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/deferred-items.md
  modified:
    - Directory.Packages.props
    - .planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md
    - .planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md

key-decisions:
  - "Item 6 (AdminRankAdjustTransactionTests HTTP-test gap) recorded as accepted-with-rationale per user instruction — no new tests added; service-layer + palette-flow coverage deemed sufficient"
  - "Pinned Scriban.Signed 7.2.5 (GHSA-24c8-4792-22hx) and Microsoft.OpenApi 2.10.0 (GHSA-v5pm-xwqc-g5wc) as transitive-only Directory.Packages.props entries — both were pre-existing NuGetAudit HIGH-severity blockers that prevented building GameKit.TestFixtures-dependent test projects and the TicTacToeDuel sample respectively; fixed within the same major version line as the incoming transitive dependency to avoid a source-breaking major bump"
  - "Items 1-4 of 04-HUMAN-UAT.md left untouched (still [pending] in this branch) per explicit task scope — Summary counts recomputed from the file's actual post-edit content (passed=3, accepted=1, pending=5), not from an assumption that items 1-4 were already closed elsewhere"

patterns-established:
  - "Pattern: RankingsExportEndpointTestServer (in-process TestServer + TestAuthHandler) for exercising MapRankingsPlayer/MapRankingsAdmin authorization at the real HTTP layer without a full JWT/cookie pipeline"

requirements-completed: [RANK-13, AUTH-23, AUTH-24, AUTH-25, AUTH-26]

coverage:
  - id: D1
    description: "GET /api/players/{id}/export returns 403 when the authenticated principal's sub claim does not match the route id (D-16 sub-mismatch)"
    requirement: "RANK-13"
    verification:
      - kind: integration
        ref: "tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs#PlayerSubMismatch_Returns_403"
        status: pass
    human_judgment: false
  - id: D2
    description: "GET /admin/api/players/{id}/export requires the superadmin policy and writes exactly one admin.player.gdpr_export audit row on success"
    requirement: "RANK-13"
    verification:
      - kind: integration
        ref: "tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs#AdminPath_Requires_Superadmin_And_Writes_Audit"
        status: pass
    human_judgment: false
  - id: D3
    description: "GET /admin/api/players/{id}/export returns 403 and writes zero audit rows for an authenticated non-superadmin admin principal"
    requirement: "RANK-13"
    verification:
      - kind: integration
        ref: "tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs#AdminPath_NonSuperadmin_Returns_403_NoAudit"
        status: pass
    human_judgment: false
  - id: D4
    description: "EndSeasonDialog type-name-to-confirm gate: End Season button disabled until the operator types the exact ladder name, enabled after"
    verification:
      - kind: automated_ui
        ref: "playwright:.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/item1-disabled-state.png + item1-enabled-state.png"
        status: pass
    human_judgment: false
  - id: D5
    description: "RankAdjustDialog palette flow: ladder selector populates from live data, rating field accepts a value within configured bounds, submit writes an admin.player.rank_adjust audit row"
    verification:
      - kind: automated_ui
        ref: "playwright:.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/item2-dialog-opened.png + item2-after-submit.png"
        status: pass
      - kind: other
        ref: ".planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/db-verification-log.txt (admin_audit_log + player_ranks query)"
        status: pass
    human_judgment: false
  - id: D6
    description: "Live account merge via Admin UI: POST /admin/api/players/merge returns 200 status=merged, source player tombstoned, exactly one auth.account_merge audit row"
    requirement: "AUTH-23, AUTH-26"
    verification:
      - kind: e2e
        ref: ".planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/item3-merge-response.json"
        status: pass
      - kind: other
        ref: ".planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/db-verification-log.txt (tombstone + audit-row query)"
        status: pass
    human_judgment: false
  - id: D7
    description: "Idempotent second identical merge: 200 status=already_merged, no duplicate audit row, token revocation fired only once"
    requirement: "AUTH-24, AUTH-25"
    verification:
      - kind: e2e
        ref: ".planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/item4-merge-response.json"
        status: pass
      - kind: other
        ref: ".planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/db-verification-log.txt (single auth.logout.all row query)"
        status: pass
    human_judgment: false

duration: 40min
completed: 2026-07-12
status: complete
---

# Quick Task 260712-hdx: Close Automatable Outstanding UAT Items Summary

**Closed 04-HUMAN-UAT item 5 with 3 new GDPR-export HTTP tests, verified 4 admin-UI/account-merge items via live headless-browser UAT against the TicTacToeDuel sample (all 4 PASS with DB-verified evidence), and honestly recorded every outcome in the two archived docs — no production code was changed.**

## Performance

- **Duration:** ~40 min
- **Completed:** 2026-07-12
- **Tasks:** 3/3 completed
- **Files modified:** 13 (1 new test file, 1 Directory.Packages.props, 2 archived docs, browser-results.json, deferred-items.md, 6 evidence files, this SUMMARY)

## Accomplishments

- Added `RankingsExportEndpointTests.cs` (3 new tests, all green against Testcontainers Postgres+Redis) exercising both GDPR-export endpoints at the HTTP layer: player-path sub-claim mismatch → 403, admin-path superadmin-gated export with exactly one audit row, and a non-superadmin negative case. Closes 04-HUMAN-UAT item 5.
- Drove a real headless-browser UAT (Playwright chrome-headless-shell) against the live TicTacToeDuel sample and verified all 4 outstanding admin-UI/merge items — EndSeasonDialog confirm gate, RankAdjustDialog palette flow (with DB-verified audit row + applied rating), live account merge, and idempotent re-merge (with DB-verified single audit row + single token revocation). All 4 PASS.
- Fixed two pre-existing HIGH-severity NuGetAudit blockers that were preventing this task's own verification from running: Scriban.Signed (GHSA-24c8-4792-22hx, blocked all WireMock.Net-dependent test projects) and Microsoft.OpenApi (GHSA-v5pm-xwqc-g5wc, blocked the TicTacToeDuel sample build). Both fixed via same-major-line transitive pins in Directory.Packages.props, mirroring the Phase 18-01 MessagePack precedent.
- Recorded item 6 (AdminRankAdjustTransactionTests HTTP-test gap) as accepted-with-rationale per explicit user instruction — no new tests added.
- Flipped 10-VERIFICATION.md status from `human_needed` to `verified` — both human_verification entries now carry `result: pass` with evidence references.

## Task Commits

1. **Task 1: Add player-path 403 + admin-path superadmin/audit HTTP tests** — `f820204` (test)
2. **Task 2: Headless-browser verification of 4 admin-UI/merge items** — `bb0af0d` (test)
3. **Task 3: Record actual results in 04-HUMAN-UAT.md, 10-VERIFICATION.md, and this SUMMARY** — recorded in the orchestrator's docs commit (per constraint: executor does not commit docs artifacts)

**Plan metadata:** committed by the orchestrator (docs commit, per constraint).

## Files Created/Modified

- `tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs` — 3 new HTTP-layer GDPR-export tests + `TestAuthHandler` + `RankingsExportEndpointTestServer` + `RankingsExportEndpointTestModelCustomizer`
- `Directory.Packages.props` — added `Scriban.Signed 7.2.5` and `Microsoft.OpenApi 2.10.0` transitive pins (both security fixes, both Rule-3 blocking-issue auto-fixes)
- `.planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md` — items 5/6/8/9 updated with actual results; Summary counts recomputed (passed=3, accepted=1, pending=5); item 7 left pending
- `.planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md` — status flipped `human_needed` → `verified`; both `human_verification[].result` fields populated; "Human Verification Required" section items 1-2 annotated with pass evidence
- `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/browser-results.json` — 4-item machine-readable results log
- `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/` — 4 screenshots, 2 merge HTTP-response JSON captures, 1 DB-verification query log
- `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/deferred-items.md` — documents the Microsoft.OpenApi vulnerability discovery/escalation-to-fix and notes it was resolved (not deferred) once it became a Task 2 blocker

## Decisions Made

- **Item 6 = accepted, not fixed:** per explicit user instruction, recorded as an accepted gap with rationale (service-layer `RankAdjustServiceTests` + palette-flow coverage in item 9 deemed sufficient) rather than writing new HTTP tests.
- **Items 1-4 of 04-HUMAN-UAT.md left untouched:** the task prompt stated these were "closed earlier today," but this worktree's copy of the file still shows them `[pending]` (only one commit — the original v2.0 kickoff — touches the file in this branch's history). Per the explicit "update only items 5, 6, 8, 9" scope instruction, items 1-4 were NOT modified. Summary counts (`passed: 3, accepted: 1, pending: 5`) were computed from the file's actual post-edit content, not from the assumption that items 1-4 already show `pass` — this keeps the bookkeeping internally consistent and honest even though it means the file's `pending` count (5) is higher than the task-prompt's framing implied.
- **Scriban.Signed and Microsoft.OpenApi transitive pins:** both are Rule-3 blocking-issue auto-fixes (not new package installs — pinning an already-present transitive dependency to a higher version within the same major line, exactly mirroring the existing MessagePack 3.1.7 precedent already documented in Directory.Packages.props). Both were required to even build/run this task's own verification (GameKit.Rankings.Integration.Tests needs GameKit.TestFixtures → WireMock.Net → Scriban.Signed; the TicTacToeDuel sample needs GameKit.OpenApi → Microsoft.AspNetCore.OpenApi → Microsoft.OpenApi).
- **Merge test used fresh player pairs:** the browser script was run twice during iteration; the first pair (UAT-MergeSource/UAT-MergeTarget) got merged during script debugging, so the final recorded run used a second fresh pair (UAT-MergeSource2/UAT-MergeTarget2) to get a clean "merged" (not "already_merged") result for item 3, followed immediately by the idempotent replay for item 4.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] Scriban.Signed 7.0.6 NuGetAudit HIGH-severity blocker**
- **Found during:** Task 1, first `dotnet build tests/GameKit.Rankings.Integration.Tests` attempt
- **Issue:** `WireMock.Net.Minimal 2.2.0` (already-pinned, used by `GameKit.TestFixtures`) transitively requires `Scriban.Signed 7.0.6`, which falls inside the vulnerable range for GHSA-24c8-4792-22hx (array.insert_at DoS). `NuGetAuditMode=all` + `NuGetAuditLevel=high` (repo-wide gate, Phase 18-01) failed restore for every project that references `GameKit.TestFixtures` — not just the new test file.
- **Fix:** Added `<PackageVersion Include="Scriban.Signed" Version="7.2.5" />` transitive pin to `Directory.Packages.props` (first-patched is 2.0.0's sibling 7.2.0; 7.2.5 is latest-stable on the same line).
- **Files modified:** `Directory.Packages.props`
- **Verification:** `dotnet build tests/GameKit.Rankings.Integration.Tests -c Release` now succeeds; `dotnet test tests/GameKit.Rankings.Integration.Tests -c Release` is 77/77 green.
- **Committed in:** `f820204` (part of Task 1 commit)

**2. [Rule 3 - Blocking issue] Microsoft.OpenApi 2.0.0 NuGetAudit HIGH-severity blocker**
- **Found during:** Task 2, `dotnet build samples/TicTacToeDuel` attempt (required to run the live sample for headless-browser UAT)
- **Issue:** `Microsoft.AspNetCore.OpenApi 10.0.8` (already-pinned) transitively requires `Microsoft.OpenApi 2.0.0`, which falls inside the vulnerable range for GHSA-v5pm-xwqc-g5wc (circular-schema-reference parsing DoS). Blocked the sample build entirely, which is Task 2's core deliverable dependency (not an out-of-scope discovery, unlike the initial full-solution sanity-check finding logged in `deferred-items.md`).
- **Fix:** Added `<PackageVersion Include="Microsoft.OpenApi" Version="2.10.0" />` transitive pin (first-patched 2.7.5; 2.10.0 latest-stable on the 2.x line — deliberately NOT the 3.x line, which is a source-breaking OpenAPI-3.1 rewrite).
- **Files modified:** `Directory.Packages.props`
- **Verification:** `dotnet build samples/TicTacToeDuel -c Release` now succeeds; the sample was successfully launched and driven through all 4 UAT checks.
- **Committed in:** `bb0af0d` (part of Task 2 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 3 — blocking-issue NuGet security pins)
**Impact on plan:** Both fixes were necessary preconditions for the plan's own verification steps to run at all; neither touches production application code. No scope creep — the (unrelated) 7-project Microsoft.OpenApi blast radius outside this task's direct path is documented in `deferred-items.md` as already resolved by the same pin.

## Issues Encountered

None beyond the two NuGetAudit blockers documented above. The Playwright script initially mis-filtered the command-palette search (needed "adjust" not "adjust rating" to match the label "Adjust player rank") and the EndSeasonDialog confirm-gate check initially failed because MudBlazor's `MudTextField` is not `Immediate` by default — `.fill()` alone doesn't trigger the `@bind-Value:after` round-trip; a `Tab` press to blur was required. Both were iteration fixes to the throwaway script (never committed) before the final recorded run.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- 04-HUMAN-UAT.md: items 5, 8, 9 closed PASS; item 6 accepted with rationale; items 1-4 remain pending in this branch (status stays `partial` — the file's `status` field was not touched since item 7 keeps it open regardless).
- 10-VERIFICATION.md: status flipped to `verified` — Phase 10 (Account Merge) human-verification is now fully closed.
- Remaining open item across both files: 04-HUMAN-UAT item 7 (CR-02 per-session delta semantics) — explicitly out of scope for this quick task, left `[pending]`.
- No blockers for future phase work.

---
*Quick task: 260712-hdx*
*Completed: 2026-07-12*

## Self-Check: PASSED

All claimed files exist on disk and all claimed commits exist in git history:

- `tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs` — FOUND
- `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/browser-results.json` — FOUND
- `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/evidence/db-verification-log.txt` — FOUND
- `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/deferred-items.md` — FOUND
- `Directory.Packages.props` — FOUND
- `.planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md` — FOUND
- `.planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md` — FOUND
- Commit `f820204` (Task 1) — FOUND
- Commit `bb0af0d` (Task 2) — FOUND
- Commit `163abac` (Task 3) — FOUND
