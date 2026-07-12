---
phase: quick-260712-hdx
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs
  - .planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md
  - .planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md
  - .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/260712-hdx-SUMMARY.md
autonomous: true
requirements: [RANK-13, AUTH-23, AUTH-24, AUTH-25, AUTH-26]
user_setup: []

must_haves:
  truths:
    - "GET /api/players/{B}/export with an authenticated principal whose sub claim != B returns 403 (player-path sub-mismatch)."
    - "GET /admin/api/players/{id}/export as a superadmin principal returns 200 and writes exactly one admin.player.gdpr_export audit row; a non-superadmin admin principal gets 403 and writes zero audit rows."
    - "Each of the 4 browser checks (EndSeasonDialog confirm gate, RankAdjustDialog palette flow, live merge, idempotent second merge) produces a recorded PASS or FAIL with on-disk evidence — failures are recorded honestly, not fixed ad-hoc."
    - "04-HUMAN-UAT.md items 5/6/8/9 and 10-VERIFICATION.md merge status reflect the ACTUAL results of Task 1 + Task 2; item 7 (CR-02) stays pending; ROADMAP.md is untouched."
  artifacts:
    - tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs
    - .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/ (browser evidence: results log + screenshots)
    - .planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md
    - .planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md
  key_links:
    - "New tests mount MapRankingsPlayer + MapRankingsAdmin and authenticate via an in-test authentication scheme (default) plus manually-registered gamekit.admin.superadmin / gamekit.admin.admin policies bound to that scheme."
    - "Browser script authenticates via POST /admin/api/login (gk_admin_session cookie) + X-GameKit-Admin-CSRF header (harvested __RequestVerificationToken), and asserts against gamekit.admin_audit_log / gamekit.account_merges on the :5433 Postgres."
---

<objective>
Close the automatable outstanding UAT items from the cross-phase audit: (1) add two GDPR-export HTTP endpoint integration tests to GameKit.Rankings.Integration.Tests, (2) run headless-browser verification of 4 admin-UI/merge items against the live TicTacToeDuel sample, and (3) record the actual pass/fail outcomes in the two archived UAT/VERIFICATION files + the quick-task SUMMARY.

Purpose: Convert "pending — needs human" UAT items into either automated regression coverage (item 5), an accepted-gap record (item 6), or honestly-recorded browser outcomes (items 8/9 + Phase 10 merge). No production code changes — this is verification + test coverage + honest bookkeeping.

Output: One new test file, browser evidence in the quick dir, and edits confined to the two named .planning docs + the SUMMARY.
</objective>

<execution_context>
@$HOME/.claude/gsd-core/workflows/execute-plan.md
@$HOME/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md

# Test-host + Testcontainers patterns to reuse (READ these — do not reinvent)
@tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs
@tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs
@tests/GameKit.Rankings.Integration.Tests/SessionsStartEndpointTests.cs
@tests/GameKit.Rankings.Integration.Tests/SessionLifecycleTestServer.cs

# Endpoints under test (authorization + handler behavior)
@src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs
@src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs

# Admin login + CSRF + merge flow for the browser task
@tests/GameKit.Admin.Integration.Tests/WebApplicationFactoryExtensions.cs

# Local-run + browser + creds (memory files — read for exact commands)
@/home/noah/.claude/projects/-home-noah-Desktop-projects-gamekit/memory/local-sample-run-port-5433.md
@/home/noah/.claude/projects/-home-noah-Desktop-projects-gamekit/memory/dev-admin-creds.md
@/home/noah/.claude/projects/-home-noah-Desktop-projects-gamekit/memory/headless-browser-playwright.md

# Files to edit in Task 3 (edit ONLY these two + the SUMMARY)
@.planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md
@.planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add player-path 403 + admin-path superadmin/audit HTTP tests to GameKit.Rankings.Integration.Tests</name>
  <files>tests/GameKit.Rankings.Integration.Tests/RankingsExportEndpointTests.cs</files>
  <action>
Create ONE new test file `RankingsExportEndpointTests.cs` in `tests/GameKit.Rankings.Integration.Tests/` (namespace `GameKit.Rankings.Integration.Tests`, GPL SPDX header + copyright line matching the sibling files). This closes 04-HUMAN-UAT item 5 by exercising both GDPR-export endpoints at the HTTP layer against a real Testcontainers Postgres + Redis.

Structure (reuse existing patterns — do NOT rebuild fixtures from scratch):
- Mark the class `[Collection("Rankings")]` + `[Trait("Category", "Integration")]` and inject `PostgresFixture` + `RedisFixture` via the constructor (same as SessionsStartEndpointTests / SessionCompleteIdempotencyTests — that collection already bundles both fixtures).
- In `InitializeAsync`, create a fresh database and apply Core + Auth + Rankings migrations. Copy the `CreateFreshDatabaseAsync` + `ApplyMigrationsAsync` sequence from GdprExportContractTests (it applies Core, THEN Auth via AuthMigrationConstants, THEN Rankings) — the Auth step is required because IGdprExportService reads player_identities + player_credentials, and admin_audit_log is created by the Core migration (Core-owned table), so no Admin migration is needed.
- Build an in-process TestServer following SessionLifecycleTestServer: `HostBuilder().ConfigureWebHost(UseTestServer)`, register `AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; }).AddRankings(o => {}).AddLadder(<name>)`, register `IConnectionMultiplexer` → the Redis fixture connection string (the Rankings ticker hosted service resolves it at startup), and override the DbContext with a model customizer that applies BOTH Auth (PlayerIdentity, PlayerCredential) AND Rankings entities — reuse the exact configuration in GdprExportContractTests' `GdprTestModelCustomizer` (copy it as a private/internal customizer in this file, or a shared one; AdminAuditLog is a Core entity already in the base model so it needs no extra config).

Authentication for the test host (this is the crux — the endpoints authorize before the handler runs):
- Add a minimal in-test `AuthenticationHandler<AuthenticationSchemeOptions>` (e.g. `TestAuthHandler`) registered as a NAMED scheme AND set as the default authenticate + default challenge scheme. Register it AFTER the AddGameKit/AddRankings chain so its default-scheme wins; if AddRankings pins a conflicting default, additionally set `DefaultAuthenticateScheme`/`DefaultChallengeScheme` to the test scheme via `Configure<AuthenticationOptions>`. The handler reads request headers: when an `X-Test-Sub` header is present it returns `AuthenticateResult.Success` with a `ClaimsIdentity` carrying `ClaimTypes.NameIdentifier` = that header value, and when an `X-Test-Role` header is present adds a `ClaimTypes.Role` claim = that value; when `X-Test-Sub` is absent it returns `AuthenticateResult.NoResult()` so anonymous requests are challenged.
- Register the two admin authorization policies the mapped admin group needs, bound to the test scheme: `"gamekit.admin.superadmin"` = `RequireAuthenticatedUser().AddAuthenticationSchemes(<TestScheme>).RequireRole("superadmin")`, and `"gamekit.admin.admin"` = same but `RequireRole("admin","superadmin")`. These names are the literals RankingsAdminEndpoints references (`SuperadminPolicy`/`AdminPolicy`).
- In `app.Configure`: `UseRouting()` → `UseAuthentication()` → `UseAuthorization()` → `UseEndpoints(e => { e.MapRankingsPlayer(); e.MapRankingsAdmin(); })` (add `using GameKit.Rankings.Http;`). If mounting MapRankingsAdmin trips a startup/DI error from the POST endpoints' antiforgery filters, add `services.AddAntiforgery()` (the GET export endpoint we hit has no antiforgery filter, so no token is needed for it).

Tests (name them EXACTLY as 04-HUMAN-UAT item 5 expects so the grep-by-name gap closes):
- `PlayerSubMismatch_Returns_403`: seed nothing (or a player) as needed; issue `GET /api/players/{B}/export` with header `X-Test-Sub` = some guid A where A != B; assert HTTP 403. This proves the D-16 sub-claim mismatch → Results.Forbid() path in RankingsPlayerEndpoints.
- `AdminPath_Requires_Superadmin_And_Writes_Audit`: seed one player row (raw Npgsql, PascalCase columns per the sibling seed helpers) so ExportWithSizeAsync returns non-null; issue `GET /admin/api/players/{playerId}/export` with headers `X-Test-Sub` = an admin guid + `X-Test-Role` = "superadmin"; assert HTTP 200, then query `gamekit.admin_audit_log` and assert exactly ONE row with Action = "admin.player.gdpr_export" and TargetId = playerId and ActorId = the admin guid.
- `AdminPath_NonSuperadmin_Returns_403_NoAudit`: same admin export GET but `X-Test-Role` = "admin"; assert HTTP 403 and assert zero admin.player.gdpr_export rows for that player (proves the endpoint is superadmin-gated, not merely authenticated-gated).

Keep the audit-action string comparison in test assertions only (query results); do not add head-comments that quote a literal a later negative grep would match. All raw SQL uses the quoted-PascalCase column convention already used in this project's seed helpers.
  </action>
  <verify>
    <automated>dotnet test tests/GameKit.Rankings.Integration.Tests -c Release --filter "FullyQualifiedName~RankingsExportEndpointTests"</automated>
  </verify>
  <done>New file compiles; the 3 tests run green against Testcontainers Postgres + Redis; PlayerSubMismatch_Returns_403 and AdminPath_Requires_Superadmin_And_Writes_Audit exist by exact name; the full `dotnet test tests/GameKit.Rankings.Integration.Tests -c Release` project run is green (no regressions).</done>
</task>

<task type="auto">
  <name>Task 2: Headless-browser verification of 4 admin-UI/merge items against the live TicTacToeDuel sample</name>
  <files>.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/browser-results.json</files>
  <action>
Stand up the sample and drive a THROWAWAY Playwright script to verify 4 items. Keep the script and any node_modules in the scratchpad dir (`/tmp/claude-1000/-home-noah-Desktop-projects-gamekit/.../scratchpad`) or another gitignored location — do NOT commit the script or credentials into the repo. Write ONLY the evidence (a machine-readable `browser-results.json` + screenshots + a text log) into the quick-task directory `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/`.

Environment bring-up (follow the memory files verbatim):
- Start the gamekit Postgres as a standalone container on host port 5433 (host :5432 is the user's own systemd Postgres — do NOT touch it). Use the exact `docker run ... -p 5433:5432 postgres:17.9` invocation with the existing `gamekit_gamekit-postgres-data` volume + init scripts from local-sample-run-port-5433.md; `docker rm gamekit-postgres` first if the name is taken. Ensure gamekit-redis is up on 6379.
- Ensure a superadmin admin exists: `root` / `uat-dev-2026`. If `gamekit.admin_users` is empty, re-bootstrap via the `gamekit admin create` CLI command in dev-admin-creds.md.
- Launch TicTacToeDuel with connection strings pointed at Port=5433 and `ASPNETCORE_URLS=http://localhost:5000` (env-override pattern in local-sample-run-port-5433.md). Wait for it to be reachable before driving the browser.
- Reuse the already-installed Playwright chrome-headless-shell at `~/.cache/ms-playwright/chromium_headless_shell-*/.../chrome-headless-shell` with `args:['--no-sandbox','--disable-gpu']` (snap chromium cannot sandbox). Reuse the `tests/e2e-browser.mjs` driver pattern from the phase-21-demo worktree if present. Only if `playwright-core` is genuinely absent, install it locally in the scratchpad (dev-only tool, verify `playwright` on npmjs.com first — it is the Microsoft-published package; never added to any repo manifest).

Checks (record PASS/FAIL + evidence for EACH; seed prerequisite data via psql on :5433 as needed — a ladder with a current season + a player rank for items 1-2, and two distinct player rows/identities to merge for items 3-4):
1. EndSeasonDialog type-name-to-confirm gate: open the admin command palette, trigger the end-season verb for a ladder, confirm the "End Season" action is DISABLED until the operator types the exact ladder name, and ENABLED once typed. Screenshot both states.
2. RankAdjustDialog palette flow: open the rank-adjust verb from the palette; confirm the ladder selector populates from live data; confirm the numeric rating field enforces the min/max from GameKitRankingsOptions; submit a valid adjust and confirm an `admin.player.rank_adjust` audit row lands in gamekit.admin_audit_log.
3. Live account merge via Admin UI: authenticate through the browser (POST /admin/api/login → gk_admin_session cookie; harvest __RequestVerificationToken and send it as the X-GameKit-Admin-CSRF header). Issue `POST /admin/api/players/merge` with valid source + target player IDs; assert HTTP 200 with status='merged'; assert in DB the source player row is tombstoned (merged_into_player_id set, deleted_at set) and exactly one `auth.account_merge` row exists in gamekit.admin_audit_log.
4. Idempotent second identical merge: repeat the same merge request; assert HTTP 200 with status='already_merged'; assert still exactly ONE auth.account_merge audit row (no duplicate) and that token revocation happened only once.

Honesty rule (hard requirement): if any check FAILS, or a prerequisite is unavailable (e.g. the sample does not wire the Rankings admin palette, or a dialog/verb is absent), record it as a FAIL in browser-results.json with the observed evidence and move on. Do NOT patch production code or fudge the sample to force a pass — fixing any discovered defect is a SEPARATE decision the user makes later. browser-results.json must contain one entry per item with fields: item, description, status (pass|fail|blocked), evidence (path to screenshot/log), notes.

Tear down the standalone :5433 Postgres container and the sample process when done; leave the user's host Postgres on :5432 untouched.
  </action>
  <verify>
    <automated>test -f .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/browser-results.json && node -e "const r=require('./.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/browser-results.json'); if(!Array.isArray(r)||r.length!==4) process.exit(1); for(const x of r){ if(!['pass','fail','blocked'].includes(x.status)) process.exit(1);} console.log('4 items recorded:', r.map(x=>x.item+':'+x.status).join(', '))"</automated>
  </verify>
  <done>browser-results.json exists in the quick dir with exactly 4 items, each carrying a status of pass/fail/blocked plus evidence; screenshots/logs for each item are present; the throwaway script + any node_modules live only in the scratchpad/gitignored location (git status shows no new script or .env under the repo); the standalone :5433 container is removed and the host :5432 Postgres was never modified.</done>
</task>

<task type="auto">
  <name>Task 3: Record actual results in 04-HUMAN-UAT.md, 10-VERIFICATION.md, and the quick SUMMARY</name>
  <files>.planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md, .planning/milestones/v2.0-phases/10-account-merge/10-VERIFICATION.md, .planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/260712-hdx-SUMMARY.md</files>
  <action>
Edit ONLY the two named archived docs + write the SUMMARY. Do NOT touch ROADMAP.md or any other .planning file. Use `Edit` (scoped replacement) on the two existing docs — never `Write` them.

In `04-HUMAN-UAT.md`:
- Item 5 (GdprExportContractTests / PlayerSubMismatch_Returns_403 + AdminPath_Requires_Superadmin_And_Writes_Audit): if Task 1's tests are green, set `result: pass` with evidence "closed by RankingsExportEndpointTests.cs (Task 1, quick 260712-hdx): PlayerSubMismatch_Returns_403 + AdminPath_Requires_Superadmin_And_Writes_Audit + AdminPath_NonSuperadmin_Returns_403_NoAudit green against Testcontainers". If they are red, record `result: [fail]` honestly with the failure detail.
- Item 6 (rank-adjust HTTP test gap — ShortReason_Returns_400 / MissingAntiforgery_Returns_400 / PlayerJWT_Returns_403): record `result: accepted — user accepted this HTTP-test gap on 2026-07-12; the RankAdjust authorization/antiforgery/validation path is covered at the service layer (RankAdjustServiceTests) and by the palette flow in item 9. NOT adding new tests.` Do NOT write new tests for this item.
- Item 8 (EndSeasonDialog confirm gate): copy the Task 2 item-1 outcome verbatim — `result: pass` (with screenshot evidence path) or `result: [fail]`/`[blocked]` with the recorded reason.
- Item 9 (RankAdjustDialog palette flow): copy the Task 2 item-2 outcome the same way (include the admin.player.rank_adjust audit-row observation).
- Item 7 (CR-02 per-session delta semantics): leave `result: [pending]` — explicitly OUT OF SCOPE for this quick task.
- Update the `## Summary` block from the ACTUAL results: total stays 9; recompute `passed` (items 1-4 already pass, plus 5/8/9 if they passed), `issues` = count of any fail results from this run, `pending` = items still pending (item 7, i.e. 1 if nothing else regressed). Add an `accepted: 1` line for item 6. Bump the front-matter `updated` timestamp; if all of 5/8/9 pass and no fails, you MAY set `status: complete` (leave `partial` if item 7 keeps it open — item 7 pending means it stays `partial`).

In `10-VERIFICATION.md`:
- Only if BOTH browser merge checks (Task 2 items 3 and 4) passed: flip front-matter `status: human_needed` → `status: verified`, and under the `human_verification:` block append a `result:` note to each of the two entries (`result: pass — <evidence: HTTP 200 merged / already_merged, single auth.account_merge row, source tombstoned; quick 260712-hdx browser run 2026-07-12>`). Also annotate the "Human Verification Required" section items 1-2 with the pass evidence.
- If EITHER merge check failed/blocked: KEEP `status: human_needed` and record the failure honestly against the corresponding human_verification entry (do not flip to verified).

SUMMARY: write `260712-hdx-SUMMARY.md` in the quick dir using the summary template — what changed (1 new test file, browser evidence, 2 doc updates), the per-item outcomes, any FAILs surfaced (and that fixing them is deferred to the user), and the files touched. Reference browser-results.json as the evidence of record.
  </action>
  <verify>
    <automated>bash -c 'set -e; git -C /home/noah/Desktop/projects/gamekit diff --name-only | grep -q "04-HUMAN-UAT.md"; git -C /home/noah/Desktop/projects/gamekit diff --name-only | grep -q "10-account-merge/10-VERIFICATION.md"; test -f /home/noah/Desktop/projects/gamekit/.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/260712-hdx-SUMMARY.md; ! git -C /home/noah/Desktop/projects/gamekit diff --name-only | grep -q "ROADMAP.md"; grep -Eq "accepted|pass|fail" /home/noah/Desktop/projects/gamekit/.planning/milestones/v1.0-phases/04-rankings-sessions-gdpr/04-HUMAN-UAT.md; echo OK'</automated>
  </verify>
  <done>04-HUMAN-UAT.md items 5/6/8/9 carry results derived from actual Task 1 + Task 2 outcomes (item 5 pass-or-fail by test result; item 6 = accepted-with-rationale, no new tests; items 8/9 = browser outcomes); item 7 stays pending; Summary counts recomputed. 10-VERIFICATION.md status flipped to verified ONLY if both merge checks passed, else stays human_needed with the failure recorded. SUMMARY written. ROADMAP.md untouched.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| throwaway script → repo | Dev-only Playwright script + dev admin creds must not leak into version control |
| verification host → user's :5432 Postgres | The sample must run on :5433; the host's own systemd Postgres on :5432 must never be modified |

## STRIDE Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-hdx-01 | Information Disclosure | throwaway browser script / .dev-admin.env | medium | mitigate | Keep script + node_modules in scratchpad/gitignored path; commit only evidence (results JSON + screenshots) under the quick dir; Task 2 `done` asserts git status shows no new script/.env under the repo |
| T-hdx-02 | Tampering | host Postgres on :5432 | high | mitigate | Run the gamekit DB only as a standalone container on :5433 per memory; never bind/alter :5432; tear down the :5433 container after the run |
| T-hdx-SC | Tampering | npm playwright-core install | low | accept | Reuse the already-installed Microsoft-published Playwright chrome-headless-shell; if install is unavoidable it is a dev-only tool verified on npmjs.com, never added to any repo manifest — no runtime/shipped dependency introduced |
</threat_model>

<verification>
- `dotnet test tests/GameKit.Rankings.Integration.Tests -c Release` is green (new tests + no regressions).
- `browser-results.json` records 4 items each with a pass/fail/blocked status + evidence.
- `git diff --name-only` shows exactly the new test file, the two named .planning docs, the SUMMARY, and browser evidence — and NOT ROADMAP.md.
- Any FAIL discovered by the browser run is recorded honestly (issues count) rather than fixed ad-hoc.
</verification>

<success_criteria>
- Two GDPR-export HTTP tests exist by exact name (PlayerSubMismatch_Returns_403, AdminPath_Requires_Superadmin_And_Writes_Audit) plus the non-superadmin negative test, all green against Testcontainers.
- 4 admin-UI/merge browser checks each produce a recorded, evidence-backed outcome.
- 04-HUMAN-UAT.md items 5/6/8/9 updated (item 6 = accepted, item 7 left pending); 10-VERIFICATION.md merge status reflects actual outcomes; SUMMARY written; ROADMAP.md untouched.
</success_criteria>

<output>
Create `.planning/quick/260712-hdx-close-automatable-outstanding-uat-items-/260712-hdx-SUMMARY.md` when done.
</output>