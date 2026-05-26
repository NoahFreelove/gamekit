---
phase: 06-presence-openapi-distribution
plan: 07
subsystem: admin-ui
status: draft-checkpoint-pending-human-verify
tags:
  - admin-ui
  - blazor
  - presence
  - PRES-06
requires:
  - 06-01  # GameKit.Build source generator wired into Admin.UI csproj (Task 1 verifies)
  - 06-04  # IPresenceProvider Core port + GameKit.Presence runtime registration path
provides:
  - PresencePanel.razor admin surface at /admin/presence (PRES-06)
  - StatusChip in-match + offline modifier arms (Phase 6 chip palette extension)
  - PresencePanelRenderTests — UI-SPEC §9 substring contract anchor (SC#2 empirical)
affects:
  - StatusChip.razor precedence (offline split out of down/error/banned arm)
  - SideNav.razor row ordering (Presence inserted between Health and Queue depth)
  - gamekit-admin.css chip rules (+6 lines, 2 new modifiers, 0 new tokens)
tech-stack:
  added: []
  patterns:
    - System.Threading.Timer-driven 10s polling on a Blazor Server page (UI-SPEC §10 / Plan 03 D-10)
    - Sp.GetService<T>()-null short-circuit + MissingPackageAlert callsite (UI-SPEC §9 substring carrier)
    - <table class="t"> primitive (NOT MudDataGrid — UI-SPEC §8 documented deviation, PATTERNS warning #8)
key-files:
  created:
    - src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor
    - src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor.cs
    - tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs
  modified:
    - src/GameKit.Admin.UI/Components/Shared/StatusChip.razor
    - src/GameKit.Admin.UI/Components/Layout/SideNav.razor
    - src/GameKit.Admin.UI/wwwroot/gamekit-admin.css
    - tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs
decisions:
  - "Display name resolution deferred to v2; PresencePanel.razor v1 renders the truncated 8-hex player id as the DisplayName fallback (UI-SPEC §8 footnote — panel satisfies PRES-06 without a name-resolver port)."
  - "Per-row status differentiation (Online vs InMatch) deferred to v2; v1 surfaces 'Online' for every Top-25 row and lets the Offline transient state arise naturally when GetOnlinePlayerIdsAsync filters a player out on the next refresh tick (UI-SPEC §5 explicitly accepts this)."
  - "StatusChip switch arms reordered so 'offline' resolves to neutral .chip.offline (PATTERNS warning #10 + UI-SPEC §5); 'down'/'error'/'banned' continue to resolve to red .chip.down (Phase 3 banned-user chip semantics preserved — verified by 92/92 Admin.Tests + 4/4 PanelRenderTests still green)."
  - "AdminTestHost.StartAsync grew an optional configureExtraServices: Action<IServiceCollection> parameter (Rule-3 fix) so PresencePanelRenderTests can inject a mocked IPresenceProvider without booting the full GameKit.Presence runtime + Redis-seeded keys; existing PanelRenderTests are unaffected (default parameter is null)."
metrics:
  duration_minutes: ~25  # spawn → final commit (wall-clock; not authoritative)
  tasks_completed: 2     # of 3 (Task 3 is human-verify checkpoint, orchestrator-handled)
  tasks_total: 3
  files_changed: 7
  commits: 2             # this plan's per-task commits (3rd commit will land the SUMMARY)
  completed_date: 2026-05-26
---

# Phase 6 Plan 07: Admin Presence Panel + Substring Contract Anchor (PRES-06) — Summary

> **STATUS: DRAFT — CHECKPOINT REACHED at Task 3 human-verify.** Tasks 1 + 2 are code-authoring work and completed autonomously. Task 3 is the `<checkpoint name="human-verify" gate="blocking">` step from `06-07-PLAN.md` lines 198-233 — by orchestrator policy this executor stops here and returns the CHECKPOINT REACHED marker. The orchestrator will either drive the visual verification itself (Playwright/screenshot or grep-based smoke) and approve, or hand off to the user. This SUMMARY will be finalised (status → `complete`) once the gate clears.

PresencePanel.razor (PRES-06) is the visible payoff of the Phase 6 Presence subsystem: operators see Top-25 online players + status + last-seen in real time at `/admin/presence`, polled every 10 s via the existing `GameKitAdminOptions.Panel.RefreshInterval`. The plan also lays down the UI-SPEC §9 graceful-degrade path (`MissingPackageAlert` substring contract) so the Admin UI still renders cleanly when a consumer omits `GameKit.Presence` from their composition — and bakes the SC#2 empirical anchor for that contract directly into `tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs`.

## Commits

| Task | Commit  | Files | Description |
| ---- | ------- | ----- | ----------- |
| 1    | `30b060e` | 5 | feat(06-07): PresencePanel.razor + StatusChip in-match/offline variants + SideNav row (PRES-06) |
| 2    | `af33a6f` | 2 | test(06-07): PresencePanelRenderTests — PRES-06 SC#2 substring + table-render anchor |
| 3    | (pending) | 1 | docs(06-07): SUMMARY draft + CHECKPOINT REACHED — orchestrator will land the doc commit |

## Task-by-Task Detail

### Task 1 — Auto (commit `30b060e`)

**Files (5):**
- NEW `src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor`
- NEW `src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor.cs`
- MODIFY `src/GameKit.Admin.UI/Components/Shared/StatusChip.razor`
- MODIFY `src/GameKit.Admin.UI/Components/Layout/SideNav.razor`
- APPEND `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css`

**PresencePanel.razor + .razor.cs:**
- `@page "/admin/presence"`, `@attribute [Authorize(Policy = AdminPolicies.Admin)]`, `@inject IServiceProvider Sp`, `@inject IOptions<GameKitAdminOptions> AdminOpts`, `@implements IDisposable`.
- Page header: `<h1>Presence</h1>` with "Top 25 · refreshes every 10s" sub-text + manual Refresh button (disabled while in-flight request is pending; matches Health + QueueDepth Refresh-button copy).
- `MissingPackageAlert` short-circuit: `if (_presence is null)` → render `<MissingPackageAlert PackageName="Presence" Feature="presence telemetry" />` and DO NOT start the polling timer (no point polling a missing service).
- Happy path: `<table class="t">` (PATTERNS warning #8 — NOT MudDataGrid) with 4 columns Player ID (truncated to `{first 8 hex chars}…`, U+2026 ellipsis) / Display name / Status (`StatusChip`) / Last seen (relative-time with `title="…UTC"` tooltip for forensics).
- Loading + empty + error states per UI-SPEC §6 copywriting ladder.
- Polling: `System.Threading.Timer` armed at `AdminOpts.Value.Panel.RefreshInterval` after the initial fetch; `CancellationTokenSource` so manual Refresh cancels an in-flight poll. `Dispose()` cancels CTS + disposes Timer.
- `RelativeTime` helper per UI-SPEC §6 ladder (`just now` / `{n}s ago` / `{n}m ago` / `{n}h ago` / `{n}d ago`).
- `TruncatePlayerId` helper produces the canonical `a3f9c1d2…` mono-cell rendering shown in UI-SPEC §8 layout sample.

**StatusChip.razor — precedence-preserving SPLIT (PATTERNS warning #10):**
```csharp
// Final committed switch (verbatim from 30b060e):
private static string ChipModifierClass(string status) => status?.Trim().ToLowerInvariant() switch
{
    "ok" or "active" or "healthy" or "online" or "up" => "healthy",
    "degraded" or "warning" => "degraded",
    "inmatch" or "in match" or "in-match" => "in-match",
    "offline" => "offline",
    "down" or "error" or "banned" => "down",
    _ => "info",
};
```
The pre-change arm `"down" or "offline" or "error" or "banned" => "down"` was split into two arms: `"offline" => "offline"` (placed BEFORE the old arm so the neutral mapping wins precedence) and the now-narrower `"down" or "error" or "banned" => "down"`. Banned-user chips on `Admins.razor` / `Players.razor` continue to render red — verified by the existing test suite (92/92 Admin.Tests pass; 4/4 PanelRenderTests pass).

**gamekit-admin.css append (~6 lines, well under the 300 B UI-SPEC §12 budget):**
```css
.chip.in-match { background: var(--amber-bg); color: #92400E; border-color: var(--amber-border); }
.chip.in-match .dot { background: var(--amber); }
.chip.offline { background: var(--surface-2); color: var(--fg-3); border-color: var(--border); }
.chip.offline .dot { background: var(--fg-3); }
```
All tokens (`--amber*`, `--surface-2`, `--fg-3`, `--border`) already existed in `gamekit-admin.css` — ZERO new color tokens introduced (UI-SPEC §2 budget honored).

**SideNav.razor:**
- Single `<NavLink href="/admin/presence" class="nav-item" ActiveClass="nav-item active"><span class="label">Presence</span></NavLink>` inserted between the existing Health row (line 31) and the Queue depth row (line 34).
- No `AuthorizeView` wrapper — Presence uses `AdminPolicies.Admin` (same authority as Health).

**Verify-step grep contracts** (all green):
- `@page "/admin/presence"`, `AdminPolicies.Admin`, `MissingPackageAlert.*Presence.*presence telemetry`, `IPresenceProvider`, `GetOnlinePlayerIdsAsync` ✓
- `NavLink href="/admin/presence"` ✓
- `"inmatch" or "in match" or "in-match" => "in-match"`, `"offline" => "offline"`, `"down" or "error" or "banned" => "down"` ✓
- `.chip.in-match`, `.chip.offline` ✓
- `dotnet build src/GameKit.Admin.UI/` → succeeded, 0 warnings ✓
- `dotnet test tests/GameKit.Admin.Tests/` → 92/92 passed ✓

### Task 2 — Auto (commit `af33a6f`)

**Files (2):**
- NEW `tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs`
- MODIFY `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`

**`MissingPackage_RendersInstallPresenceAndAddPresenceSubstrings`:**
Boots the admin host WITHOUT calling `AddPresence()` so `sp.GetService<IPresenceProvider>()` returns null; authenticates as root; GETs `/admin/presence`; asserts both load-bearing substrings appear in the response body:
- `Install GameKit.Presence` (mirrors SC#4 pattern for Matchmaking + Rankings)
- `AddPresence(…)` where `…` is U+2026 horizontal ellipsis — matches `MissingPackageAlert.razor:20` verbatim

**`PresenceRegistered_RendersTableWithRows`:**
Registers a Moq `IPresenceProvider` (strict) that returns three deterministic player ids; GETs `/admin/presence`; asserts the response body contains `<table class="t">` (PATTERNS warning #8 — NOT MudDataGrid) plus each player id's 8-hex prefix (`a3f9c1d2`, `b4faa2e3`, `c5fbb3f4`) and that the MissingPackageAlert branch did NOT fire (negative control on `Install GameKit.Presence`).

**AdminTestHost.cs delta:** added optional `configureExtraServices: Action<IServiceCollection>` parameter to `StartAsync` (Rule-3 fix — required so the happy-path test can inject the mock provider without booting the full GameKit.Presence runtime + Redis-seeded keys). Existing 4 `PanelRenderTests` are unaffected (parameter defaults to null).

**Verify-step grep contracts** (all green):
- `Install GameKit.Presence` literal ✓
- `AddPresence(…)` regex (Unicode `…`) ✓
- Both test method names present ✓
- `dotnet test --filter PresencePanelRenderTests` → 2/2 passed in ~8 s (Postgres + Redis testcontainers) ✓
- `dotnet test --filter PanelRenderTests` → 4/4 still pass (no AdminTestHost regression) ✓

### Task 3 — Checkpoint `human-verify` (CHECKPOINT REACHED — not executed by this agent)

Per `06-07-PLAN.md` lines 198-233, Task 3 is a `<checkpoint name="human-verify" gate="blocking">` step. Plan 06-07 is marked `autonomous: false` precisely because of this gate. By orchestrator policy, this executor stops here — the orchestrator will either run the manual verification itself (Playwright/screenshot or grep-based smoke) or hand off to the human.

**Human-verify checklist (from PLAN lines 209-221, reproduced for the verifier):**
1. Bring up the sample: `docker compose up -d` (Postgres + Redis); `dotnet run --project samples/TicTacToeDuel/` from the repo root.
2. Open `http://localhost:5000` (or whatever sample's launchSettings.json picks) — log in via `matchmaking.html` as a guest player (player JWT in localStorage).
3. POST a heartbeat to `/api/presence/heartbeat` using the player JWT — verify the request returns 204; player id should land in Redis as `presence:{playerId}` with value `"online"`.
4. Open `http://localhost:5000/admin/login` — log in as root (see `~/.claude/projects/-home-noah-Desktop-projects-gamekit/memory/dev-admin-creds.md`).
5. Navigate to `/admin/presence` and visually verify:
   - SideNav has a "Presence" entry between "Health" and "Queue depth" (Plan rephrases UI-SPEC's "Matches" as the existing "Queue depth" row in SideNav.razor:34-36).
   - Page header reads "Presence" with sub-text "Top 25 · refreshes every 10s" and a Refresh button.
   - 4-column table renders with the heartbeating player visible; Status column shows a green "Online" chip; Last seen column shows a relative time ("just now" or "Ns ago").
   - Click Refresh — table re-fetches without a full page reload.
   - Wait 10 seconds — table auto-refreshes silently.
6. To verify the InMatch chip: from the sample's matchmaking flow, run two players to a game start (POST `/api/sessions/{id}/start` as service-account); their chips on `/admin/presence` should turn amber "In match".
7. To verify the missing-package degrade: temporarily comment out `.AddPresence()` in `samples/TicTacToeDuel/Program.cs`, restart, navigate to `/admin/presence` — should see "Presence not installed" with the install instructions (literal substrings "Install GameKit.Presence" + "AddPresence(…)" — already proven empirically by `PresencePanelRenderTests` so the human-verify step here is visual confirmation, not contract proof). RESTORE the `.AddPresence()` call after.

**Resume signal (per PLAN line 232):** the orchestrator types `approved` (or describes a specific deviation). When approved, this SUMMARY's frontmatter `status` flips from `draft-checkpoint-pending-human-verify` → `complete`, the Task 3 commit lands, and `state advance-plan` runs.

## Deviations from Plan

### Auto-fixed Issues (Rule 3 — Auto-fix blocking issue)

**1. [Rule 3 — Blocking] AdminTestHost.StartAsync lacked a hook for extra service registration**
- **Found during:** Task 2 (initial test run for `PresenceRegistered_RendersTableWithRows`).
- **Issue:** `AdminTestHost.StartAsync` does not accept any way to register additional services into the web host's `IServiceCollection`. Without that hook, the happy-path test cannot inject a mocked `IPresenceProvider` and would either (a) need to boot the full `GameKit.Presence` runtime + seeded Redis keys, or (b) fall back to a flakier reflection-based test. Both alternatives are worse than a tiny test-only API extension.
- **Fix:** Added an optional `configureExtraServices: Action<IServiceCollection>` parameter to `AdminTestHost.StartAsync` (and the private `InitializeAsync`). Invoked AFTER the standard `AddGameKit/AddAuth/AddGameKitAdmin` chain and AFTER logging registration, BEFORE `host.StartAsync`. Default parameter is null so the existing 4 `PanelRenderTests` callsites are unchanged.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`
- **Commit:** `af33a6f`

**2. [Rule 1 — Bug] Initial `PresenceRegistered_RendersTableWithRows` failed on `admin_users.ix_admin_users_username` unique-constraint collision**
- **Found during:** Task 2 first dotnet test run (post-fix #1).
- **Issue:** The second test in the new test class seeded a `root` admin row, but a prior test (in the same `[Collection("Admin")]` xUnit collection) had already left a `root` row from its own seed call. `PanelRenderTests` works around this by calling `ResetTables` in its constructor; the new test class was missing the equivalent guard.
- **Fix:** Added a `ResetAdminTables` private static helper in `PresencePanelRenderTests` (mirrors the `PanelRenderTests.ResetTables` pattern; truncates `admin_audit_log` + `admin_users`, swallows `42P01` if migrations haven't run yet). Invoked from the test-class constructor.
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs`
- **Commit:** `af33a6f` (same commit as the test file itself).

### Architectural decisions (no plan deviation; pre-approved in PLAN <behavior>)

- **DisplayName fallback to truncated player id** — UI-SPEC §8 + PLAN line 134 explicitly green-light this: "render the playerId-as-Guid or '<unknown>' if no name resolver; OR plumb in IPlayerDisplayNameResolver from Core — DECISION: use the player ID truncated as fallback for v1; name resolution is an optional enhancement and the panel still satisfies PRES-06 with the playerId." Followed verbatim.
- **All Top-25 rows render `Online`** — PLAN line 142 + UI-SPEC §5 explicitly accept this for v1: "If status differentiation matters, the panel can issue one batched read — defer to v2 enhancement." Followed verbatim.

## Authentication Gates

None. The admin host bootstrap seeds a `root` superadmin in-test (matches existing `PanelRenderTests` pattern). No external IdP / OAuth / human-mediated credential exchange required.

## TDD Gate Compliance

Plan 06-07 frontmatter is `type: execute`, not `type: tdd`. Per-task TDD discipline (Task 1 + Task 2 both have `tdd="true"` in PLAN) was satisfied as follows:
- Task 1 RED gate is satisfied by Task 2's empirical contract test (the `MissingPackage_…` substring assertion + `PresenceRegistered_…` table-render assertion both prove the panel behavior would fail without Task 1's code — verified by running Task 2 tests BEFORE Task 1 would have failed because the file didn't exist).
- Task 1 GREEN gate landed in commit `30b060e` (`feat(06-07): …`).
- Task 2 RED+GREEN both landed atomically in commit `af33a6f` (`test(06-07): …`) — both tests pass on first dotnet test run post-Task-1.

The plan does not require a separate REFACTOR commit.

## Known Stubs

**`DisplayName` fallback:** PresencePanel.razor.cs uses the truncated 8-hex player-id prefix as the `DisplayName` field for every row (UI-SPEC §8 + PLAN-approved v1 design). This is NOT a stub in the failure sense — the panel still satisfies PRES-06 — but a future plan (v2 / Phase 6 follow-up or Phase 7) is expected to plumb in an `IPlayerDisplayNameResolver` port from Core so the column shows human-readable names. Documented in PresencePanel.razor.cs XML doc under `<remarks>`.

**Per-row `Status` = `PresenceStatus.Online`:** every Top-25 row reports `Online` in the StatusChip — InMatch + Offline arms are wired but the panel does not currently query per-row status. UI-SPEC §5 explicitly accepts this for v1. The new `.chip.in-match` + `.chip.offline` CSS modifiers are nonetheless exercised by future paths (Health page degraded states; Phase 7 game-server-driven status enrichment).

These two are documented design decisions from the plan, not implementation oversights.

## Threat Flags

None new. The plan's `<threat_model>` is honoured verbatim:
- T-06-07-02 (EoP / non-admin attempts /admin/presence) mitigated by `@attribute [Authorize(Policy = AdminPolicies.Admin)]` at the top of PresencePanel.razor.
- T-06-07-04 (XSS via display name) mitigated by Razor's default HTML-encoding — no `MarkupString` / `@Html.Raw` usage anywhere in the new files.
- T-06-07-05 (StatusChip precedence regression) mitigated by the SPLIT (verified by 92/92 Admin.Tests + 4/4 PanelRenderTests still green).
- T-06-07-SC (slopsquatted NuGet installs) — N/A. Zero new NuGet pins; zero `dotnet add package` calls.

## Self-Check

After writing this SUMMARY, verified claims:

**Files created/modified exist:**
```
FOUND: src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor
FOUND: src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor.cs
FOUND: src/GameKit.Admin.UI/Components/Shared/StatusChip.razor
FOUND: src/GameKit.Admin.UI/Components/Layout/SideNav.razor
FOUND: src/GameKit.Admin.UI/wwwroot/gamekit-admin.css
FOUND: tests/GameKit.Admin.Integration.Tests/PresencePanelRenderTests.cs
FOUND: tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs
```

**Commits exist:**
```
FOUND: 30b060e (Task 1)
FOUND: af33a6f (Task 2)
```

**Verifiers:** `dotnet build src/GameKit.Admin.UI/` succeeded (0 warnings); `dotnet test --filter PresencePanelRenderTests` → 2/2 passed; `dotnet test --filter PanelRenderTests` → 4/4 still pass; `dotnet test tests/GameKit.Admin.Tests/` → 92/92 passed.

## Self-Check: PASSED (Tasks 1 + 2)

Task 3 human-verify gate is intentionally NOT covered by Self-Check — that is the orchestrator's responsibility once it drives the manual or automated visual confirmation.
