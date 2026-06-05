---
status: complete
phase: 05-matchmaking-parties
source:
  - 05-HUMAN-UAT.md
  - 05-10-SUMMARY.md (Post-UAT Resolution)
  - 05-VALIDATION.md
started: 2026-05-22T00:00:00Z
updated: 2026-05-24T20:40:00Z
---

## Current Test

[testing complete]

## Tests

### 1. SC#3 — 1000-concurrent-ticket sustained load test
expected: Operator already ran 2026-05-18 (commit 8809c77). Numbers: MaxIterationMs=29, p99=13.83ms, Pool exhaustion=0, Dropped=0, Matched=3092. Confirming recorded pass.
result: pass
signed_off_at: 2026-05-22
evidence: commit 8809c77 (Post-UAT Resolution section in 05-10-SUMMARY.md)

### 2. UAT-3 — TicTacToeDuel sample 1v1 happy path
expected: |
  Pre-flight: docker compose up -d; ./scripts/gen-test-rsa-pem.sh (if needed);
  dotnet run --project samples/TicTacToeDuel. Open two browser tabs (one regular,
  one private/incognito for separate JWT) → http://localhost:5000/matchmaking.html.
  Both click "Play as Guest" then "Find Match". Within ~1s both transition to
  "Match proposed!" with 10-second accept countdown. Both click "Accept" within window.
  Both tabs display "Matched! Both players accepted." with the same sessionId.
result: pass
signed_off_at: 2026-05-22
notes: |
  Pre-flight friction logged for v1.1: (1) host postgres (system service) conflicts with
  container on 5432 — operator stops host service for the session; (2) `dotnet run
  --no-launch-profile` skips the ASPNETCORE_ENVIRONMENT=Development env-var that loads
  the connection-strings appsettings — sample app crashes "Missing ConnectionStrings:Redis"
  unless explicitly set. README should mention both. (3) Operator initially navigated to
  / (Phase 2 sample auth page) instead of /matchmaking.html (Phase 5 page) — could be
  resolved by linking matchmaking from index.html or making /matchmaking.html the default.

### 3. UAT-1 — Admin UI live panels (queue depth + leader identity)
expected: |
  With sample app still running, log into admin at http://localhost:5000/admin
  (seeded admin credentials). Navigate to /admin/matchmaking/health. Enqueue 3 test
  tickets via curl (POST /api/mm/queue with guest JWT, pool tictactoe). Within the
  2-second poll interval the queue-depth row for ladder tictactoe shows the enqueued
  count (or whatever the matcher hasn't yet drained). Leader identity row shows a
  non-empty {MachineName}:{Guid}. Last-renewed timestamp updates as the 500ms ticker
  renews the lease. Pass: panel reflects live Redis state. Fail: panel shows "—" or
  stale data (reflection lookup of IMatchmakingObservability failed).
result: pass-after-fix
initial_result: issue
final_result: pass
signed_off_at: 2026-05-22
reported: |
  Two separate defects surfaced. Dashboard widget at /admin always shows "Matchmaking
  not installed" alert even though all matchmaking endpoints + ticker + matching work
  end-to-end. On /admin/matchmaking the queue-depth column renders correctly (depth=1
  observed for a solo unmatched ticket) but Leader instance and Active leases are
  permanently blank/0.
severity: major
retest: |
  After D1 + D2 fixes applied + sample app rebuilt + restarted:
  - /admin dashboard widget no longer shows MissingPackageAlert (D1 namespace fix
    resolved the reflection lookup).
  - /admin/matchmaking shows Leader instance =
    "noah-ubuntu:bee9daee-1e4f-4598-8cda-68cbb5ebc479" (the running ticker's
    InstanceId), Active leases = 1, queue depth row for ladder
    019e381b-11dd-71c4-ab8d-a9df898b7ecd / pool tictactoe / depth=1.
  - Verified Redis state independently: heartbeat key TTL refreshes every 500ms
    (drops only ~85ms across 1s polls because ticker resets it each tick).
  Operator signed off pass 2026-05-22 03:15 UTC.
defects:
  - id: UAT-1-D1
    file: src/GameKit.Admin.UI/Components/Pages/Dashboard.razor:150
    summary: |
      Type-lookup string uses wrong namespace. Looks up
      "GameKit.Matchmaking.IMatchmakingStrategy, GameKit.Matchmaking" but the
      interface actually lives at GameKit.Matchmaking.Strategy.IMatchmakingStrategy
      (src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs:7). Type.GetType
      returns null -> _matchmakingInstalled = false -> MissingPackageAlert always
      renders on dashboard. QueueDepth.razor uses the correct namespace
      (GameKit.Matchmaking.Services.IMatchmakingObservability) which is why that
      page works.
    fix: One-line edit on Dashboard.razor:150 — insert ".Strategy." into the namespace.
    severity: major
    why_not_caught: |
      No integration test exercises the Dashboard.razor render path with
      GameKit.Matchmaking installed. The reflection-only contract means a typo
      compiles but produces silent runtime mis-classification. A bUnit or
      Playwright smoke test on /admin/ with matchmaking referenced would have
      caught it.

  - id: UAT-1-D2
    file: src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:257 + RedisMatchmakingObservability.cs:55
    summary: |
      Acquire/release-per-tick design vs. observability snapshot reads. The
      ticker's RunOnceAsync acquires the lock at tick start (SET NX EX) and
      ALWAYS releases via UNLINK in the finally block. Each tick takes ~2 ms;
      cadence is 500 ms. The lock key exists for ~0.4% of wall-clock time.
      RedisMatchmakingObservability.GetQueueStatsAsync does a single point-in-time
      StringGetAsync against the lock key, so leaderInstanceId is null 99.6% of
      the time. UI displays "(no replica currently holds the lease)" and
      ActiveLeaseCount=0 persistently — operator believes matchmaking is dead.
    verification: |
      Confirmed via `docker exec gamekit-redis redis-cli MONITOR` for 3s with the
      ticker running:
        371.324350 SET gamekit:matchmaking:matcher:lock noah-ubuntu:0a73... EX 90 NX
        ...8 sub-commands including SCAN, ZRANGEBYSCORE, EXPIRE renews...
        371.326073 UNLINK gamekit:matchmaking:matcher:lock
      Full tick: 1.7 ms. Subsequent ticks at 371.824, 372.325 — exactly the 500ms
      cadence. 20 sequential `redis-cli GET` samples at 100ms cadence caught the
      lock 0 times. The 05-HUMAN-UAT.md instructions ("Last-renewed timestamp
      updates as the ticker (500 ms) renews the lease") imply hold-and-renew —
      but the implementation is release-per-tick.
    fix: |
      Two options:
      (a) Lowest-risk: change MatchmakerTickerService to acquire the lock once
          (on first tick that succeeds) and only EXPIRE-renew it on subsequent
          ticks. Release only on graceful shutdown / OperationCanceledException.
          Matches the comment in MatchmakerLeaseHelper.cs ("the Lua-script
          release path is the fencing-token guard that prevents this instance
          from ever deleting another instance's lock") which assumes long-lived
          tenure. Distributed-lock libraries (RedLock.NET, etc.) all use this
          pattern.
      (b) Write a separate "matcher heartbeat" key on each tick with a short
          TTL (~2× tick interval) and have observability read THAT key. Less
          surgery but adds a Redis write per tick (negligible).
      Option (a) is recommended — it also fixes the leader-election semantics
      (with release-per-tick, two replicas can both end up running ticks back-to-back).

  - id: UAT-1-D3 (documentation)
    file: .planning/phases/05-matchmaking-parties/05-HUMAN-UAT.md (sign-off table) + samples/TicTacToeDuel/README.md
    summary: |
      Three doc inconsistencies surfaced before testing could even start:
      (1) Sign-off table claims SC#3 still ⬜ pending — commit 8809c77 evidence
          says it's GREEN.
      (2) HUMAN-UAT instructions point operator at /admin/matchmaking/health;
          README points at /admin/matchmaking/queue-depth; actual @page is
          /admin/matchmaking.
      (3) HUMAN-UAT enqueue curl: `/api/auth/guest` (wrong path; actual is
          /auth/login/guest, requires `{}` body + Content-Type) and
          `{"ladderId":"<resolved-from-/demo/ladder-id/tictactoe>"}` — the
          resolver returns `{"id":...}` not `{"ladderId":...}`.
    fix: |
      Reconcile sign-off table to commit-evidence reality; correct the URL in
      both docs to /admin/matchmaking; fix the curl example in HUMAN-UAT.
    severity: minor

### 4. UAT-2 — Admin command-palette pause-queue / drain-queue verbs
expected: |
  In the admin UI, press ⌘K (or Ctrl+K). Type "pause queue" — pause-queue verb appears
  (RequiresTarget=true). Select it → confirmation dialog prompts for ladder name
  (tictactoe). Confirm. Behind: POST /admin/api/matchmaking/{ladderId}/pause-queue;
  IAdminAuditWriter records matchmaking.pause_queue; Redis key mm:control:paused SET.
  Re-run the curl enqueue from UAT-1 step 5 — response is HTTP 503 (queue paused).
  Then type "drain queue" in palette, confirm on the ladder — matcher continues
  existing tickets but rejects NEW enqueues. GET /admin/api/audit?action=matchmaking.pause_queue
  shows the audit row with timestamp + admin actor + ladder target.
result: pass-after-fix
initial_result: issue
final_result: pass
signed_off_at: 2026-05-24
reported: |
  Two rounds of inline-fixed defects. Round 1 — palette UI (operator walkthrough):
  D1 substring search missed multi-word queries ("pause queue" → "Pause matchmaking
  queue"); D2 palette trapped in stale subview on close/reopen; D3 arrow keys + Enter
  had no visible effect (no default selection + Blazor-scoped CSS rule didn't match
  JS-created rows); D4 target-search rows non-clickable (click handler early-bail
  before data-target-id branch). Round 2 — pause/drain enforcement (surfaced after
  D1-D4 fixed): D5 ticker checked only the global mm:control:paused key, never the
  per-ladder mm:control:paused:{ladderId} written by the new IMatchmakingControlService;
  D6 MatchmakingService.EnqueueAsync had NO pause/drain check at all — palette+dialog
  set Redis flags + wrote audit but enqueue still returned 200. The dialog WIP shipped
  the control surface but stopped short of teaching the rest of the system to obey it.
severity: blocker
retest: |
  After commits 95da329 (D1-D4 palette fixes + WIP feature) and 29b7bfe (D5+D6
  pause/drain enforcement) + sample app rebuilt + restarted, operator
  re-walked the full UAT-2 chain in browser. Pass evidence:
  - ⌘K + "pause queue" → "Pause matchmaking queue" verb visible (D1).
  - Mid-flow close-and-reopen returns to root verb list (D2).
  - Arrow keys highlight rows with violet outline + bg tint (D3).
  - Ladder row "tictactoe" clickable, opens PauseQueueDialog (D4).
  - Dialog Confirm → POST /admin/api/matchmaking/{ladderId}/pause-queue
    succeeds. Redis key mm:control:paused:019e381b-... SET (verified via
    redis-cli KEYS). Audit row admin.matchmaking.pause_queue written with
    ActorId=root, TargetType=ladder, TargetId=019e381b-... (verified via
    psql admin_audit_log).
  - Re-enqueue curl returns HTTP 503 with Retry-After: 60 and
    {"error":"queue_paused","detail":"queue_paused","retryAfterSeconds":60} (D6).
  - Drain verb same chain: ⌘K + "drain" → "Drain matchmaking queue" → ladder
    pick → Confirm → mm:control:drain:019e381b-... SET + audit row
    admin.matchmaking.drain_queue. With pause cleared, enqueue returns
    HTTP 503 with {"error":"queue_draining"} (distinct from queue_paused).
defects:
  - id: UAT-2-D1
    file: src/GameKit.Admin.UI/wwwroot/gamekit-admin.js:218
    summary: |
      Substring-only matcher (label.indexOf(q)) couldn't span query tokens
      across non-contiguous words in the label. WIP added the first long-word
      verbs ("Pause matchmaking queue") where natural queries skip middle words.
    fix: |
      Tokenize query on whitespace; require every token to substring-match the
      label in any order. ~2 lines in _filterPalette.
    severity: major
    regression_flavor: regression
  - id: UAT-2-D2
    file: src/GameKit.Admin.UI/wwwroot/gamekit-admin.js:102-115
    summary: |
      closePalette reset input.value / placeholder but never restored the
      SSR .palette-list DOM that _enterTargetPick / _renderLadderResults /
      _renderTargetResults destroyed via `while (list.firstChild) removeChild`.
      Reopen with ⌘K left operator in stale subview markup with no path back.
    fix: |
      Snapshot the SSR .palette-list children on first openPalette via
      cloneNode(true); restore the clones on every subsequent open. Symmetric
      reset of input.value + placeholder moved into openPalette (out of the
      _selectedAction!=null conditional in closePalette).
    severity: major
    regression_flavor: pre-existing-masked-by-player-only-verbs
  - id: UAT-2-D3
    file: |
      src/GameKit.Admin.UI/wwwroot/gamekit-admin.js:96
      src/GameKit.Admin.UI/wwwroot/gamekit-admin.css:953-964
      src/GameKit.Admin.UI/Components/Shared/CommandPalette.razor.css:24-28
    summary: |
      Two-part defect. (a) openPalette never seeded _resetSelection() — SSR
      verb rows opened aria-selected=false, Enter no-op until operator typed
      something. (b) The only [aria-selected="true"] highlight rule lived in
      Blazor-scoped CommandPalette.razor.css; the [b-XXXXXXXX] scope attribute
      is stamped at SSR render time only, so JS-created subview buttons
      (document.createElement) never matched the rule even when aria-selected
      was written correctly.
    fix: |
      (a) call _resetSelection() at end of openPalette so first row is selected
      on open. (b) move .palette-row[aria-selected="true"] highlight to the
      GLOBAL gamekit-admin.css next to .palette-row.active; remove from the
      scoped sheet (left an inline comment to deter drift). JS-created
      subview rows now pick up the rule.
    severity: major
    regression_flavor: pre-existing-masked-by-player-only-verbs
  - id: UAT-2-D4
    file: src/GameKit.Admin.UI/wwwroot/gamekit-admin.js:281
    summary: |
      Click handler had `if (!commandId) return;` BEFORE the data-target-id
      dispatch branch. _renderLadderResults / _renderTargetResults build
      target buttons with data-target-id + data-display-name but NO
      data-command-id (the verb lives in _selectedAction.commandId at module
      scope). Every target click hit the guard and silently bailed. Player
      target path had the same bug; Phase 03.1 UAT exercised dispatch via
      HTTP POST, never the click → search → click → dialog chain end-to-end.
    fix: |
      Hoist the data-target-id branch ABOVE the commandId guard; dispatch
      via _selectedAction.commandId. Added _selectedAction null-guard so
      a stale target-id button (defensive) cannot fire after close.
    severity: blocker
    regression_flavor: pre-existing-masked-by-player-only-verbs
  - id: UAT-2-D5
    file: src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:205
    summary: |
      MatchmakerTickerService.RunOnceAsync checked the GLOBAL
      `mm:control:paused` kill-switch (no ladder suffix) once per tick. The
      WIP RedisMatchmakingControlService.PauseAsync writes per-ladder
      `mm:control:paused:{ladderId}` keys. Two namespaces, never met — paused
      ladders kept matching tickets behind operators' backs.
    fix: |
      Add per-ladder pause check inside ProcessPoolAsync's foreach (queueKey)
      loop. ExtractLadderId already resolves the ladder GUID from the queue
      key path (mm:queue:{ladderId}:{poolName}); KeyExistsAsync against
      ControlPausedKeyForLadder skips just that ladder, leaving siblings
      processable in the same tick. Drain intentionally has NO equivalent
      skip — drained ladders KEEP matching existing tickets per the drain
      contract; drain only gates new enqueues.
    severity: major
    regression_flavor: incomplete-WIP-feature
  - id: UAT-2-D6
    file: src/GameKit.Matchmaking/Services/MatchmakingService.cs (Step 3.5)
    summary: |
      MatchmakingService.EnqueueAsync had NO pause/drain check. EnqueueOutcome
      enum had no RejectedDueToQueuePaused / RejectedDueToQueueDraining values.
      Pause set the Redis flag, audit row written, but next enqueue went 200.
      The UAT-2 expected behavior ("re-run enqueue → HTTP 503") tested
      functionality that was never plumbed.
    fix: |
      Add EnqueueOutcome values 6 + 7. Insert Step 3.5 gate after ladder
      lookup (so cooldown / invalid-party still take precedence): KeyExists
      against per-ladder pause key → return RejectedDueToQueuePaused; same
      for drain → return RejectedDueToQueueDraining. MatchmakingEndpoints
      maps both to HTTP 503 via new ServiceUnavailableResult IResult that
      also writes Retry-After: 60 (RFC 7231 §7.1.3) so client retry policies
      back off without parsing the body. Body shape matches the existing
      rejection envelope: { error, detail, retryAfterSeconds }.
    severity: blocker
    regression_flavor: incomplete-WIP-feature
follow_up:
  - title: Add "unpause" / "resume queue" admin verb
    note: |
      Today the only way to clear the per-ladder pause / drain flag is
      redis-cli DEL or a SQL helper. AdminCommandRegistry should gain a
      pair of unpause-queue / undrain-queue verbs (or a single
      toggle-queue verb) so operators can undo mid-incident without
      shell access. Out of scope for UAT-2; file as Plan 05-11 or v1.1
      backlog.
  - title: Reason field on pause/drain dialog
    note: |
      Audit rows show Reason="(no reason)" because PauseQueueDialog +
      DrainQueueDialog don't surface a reason input. The
      IMatchmakingControlService.PauseAsync signature already takes a
      reason parameter — dialog just needs a MudTextField wired through.
      Audit-trail quality improvement; not a blocker.
  - title: Integration test for the 503 enqueue path
    note: |
      D6 fix is currently verified only by live operator curl. Add an
      xUnit + Testcontainers test that SETs the per-ladder pause key
      directly, calls EnqueueAsync via WebApplicationFactory, asserts
      503 + Retry-After + queue_paused body. Same shape for drain.
      Belongs in tests/GameKit.Matchmaking.Integration.Tests.

## Summary

total: 4
passed: 4
issues: 0
pending: 0
skipped: 0
issues_resolved_inline: 4

## Gaps

[none — all four UAT-2 defect rounds resolved inline; see "Resolved Gaps" below]

## Historical Gaps (resolved inline before complete)

- truth: "Operator can dispatch pause-queue and drain-queue verbs through the admin ⌘K command palette, including selecting a ladder target and confirming the dialog."
  status: failed
  reason: |
    User reported four palette defects during UAT-2 walkthrough (2026-05-24):
    D1 substring search doesn't tokenize ("pause queue" misses "Pause matchmaking queue");
    D2 palette state persists across close/reopen (input + filter stick);
    D3 arrow-key navigation broken (no row selection, Enter no-op);
    D4 target-search rows non-clickable (ladder list appears but cannot be picked).
    Net effect: pause-queue / drain-queue verbs are completely unreachable from
    the palette. D1 is regression-flavored (WIP added the long-word labels that
    surfaced the matcher's limit). D2/D3/D4 are pre-existing palette JS gaps
    that the player-only verb set in Phase 03.1 UAT masked — git show b5424ca
    confirms the click-handler early-bail guard predates the WIP.
  severity: blocker
  test: 4
  root_cause: |
    Four discrete palette JS defects (gsd-debugger session
    .planning/debug/palette-uat2-defects.md):
    - D1: gamekit-admin.js:218 — substring-only matcher
      `label.indexOf(q) !== -1` fails when query words are non-contiguous in
      the label. Pre-WIP verb labels were ≤3 words so natural multi-word
      queries happened to be contiguous; "Pause matchmaking queue" is the
      first label where "pause queue" skips a middle word.
    - D2: gamekit-admin.js:102-115 — closePalette() resets input.value /
      placeholder but never restores the SSR verb-list DOM after
      _enterTargetPick → _renderLadderResults → _renderTargetResults
      destroys it (each does `while (list.firstChild) list.removeChild(...)`).
      No DOM snapshot, no Blazor re-render trigger. Operator reopens into
      stale subview markup.
    - D3 (two-part):
      (a) gamekit-admin.js:96 — openPalette() never calls _resetSelection(),
          so SSR verb rows all stay aria-selected="false"; Enter finds no
          active row and no-ops until the operator types.
      (b) The only [aria-selected="true"] highlight rule lives in the
          Blazor-scoped CommandPalette.razor.css:24-28 — its [b-XXXXXXXX]
          scope attr is added at SSR-render time and is NOT inherited by
          buttons that JS creates via document.createElement('button').
          Global gamekit-admin.css:953-964 only styles `.palette-row.active`
          (a class the JS never sets). Dynamic subview rows look unselected
          even when aria-selected="true" is written correctly.
    - D4: gamekit-admin.js:281 — `if (!commandId) return;` early-bails the
      click handler before reaching the data-target-id branch at lines
      284-291. _renderLadderResults (lines 369-380) and _renderTargetResults
      (lines 425-437) build buttons with data-target-id / data-display-name
      / data-label but never set data-command-id. The dispatch branch is
      unreachable dead code on every dynamically rendered target row;
      player-target path has the same bug but was never exercised end-to-end
      in Phase 03.1 UAT (dispatch was tested via HTTP POST, not the click
      chain).
  artifacts:
    - path: src/GameKit.Admin.UI/wwwroot/gamekit-admin.js
      issue: substring matcher (line 218); closePalette no DOM restore (102-115); openPalette no _resetSelection seed (96); click handler early-bail blocks target-row dispatch (281); target buttons missing data-command-id (369-380, 425-437)
    - path: src/GameKit.Admin.UI/wwwroot/gamekit-admin.css
      issue: lines 953-964 style only `.palette-row.active` (class never set), missing global [aria-selected="true"] rule
    - path: src/GameKit.Admin.UI/Components/Shared/CommandPalette.razor.css
      issue: lines 24-28 are Blazor-scoped, so the highlight only matches SSR-rendered verb rows — needs to move to global stylesheet
  missing:
    - "D1 — tokenize query on whitespace, require every token to match label substring-wise in any order: `q.split(/\\s+/).filter(Boolean).every(t => label.indexOf(t) !== -1)`"
    - "D2 — snapshot the SSR `.palette-list` children on first openPalette() and restore them inside closePalette() (lower-risk alternative to a Blazor StateHasChanged bridge)"
    - "D3a — call _resetSelection() at the end of openPalette() so the SSR verb list opens with row 0 active"
    - "D3b — move the `.palette-row[aria-selected=\"true\"]` highlight from CommandPalette.razor.css to the global gamekit-admin.css so JS-created rows pick up the rule"
    - "D4 — either hoist the data-target-id branch above the `if (!commandId) return;` guard and dispatch via _selectedAction.commandId, OR write data-command-id={_selectedAction.commandId} onto every target button in _renderLadderResults and _renderTargetResults"
  debug_session: .planning/debug/palette-uat2-defects.md

## Resolved Gaps (fixed inline 2026-05-22)

- truth: "Admin dashboard widget (/admin) accurately reports whether GameKit.Matchmaking is installed and operational"
  initial_status: failed
  resolution: fixed-inline
  fix: |
    Dashboard.razor:150 — corrected the reflection lookup string from
    "GameKit.Matchmaking.IMatchmakingStrategy, GameKit.Matchmaking" to
    "GameKit.Matchmaking.Strategy.IMatchmakingStrategy, GameKit.Matchmaking"
    (the interface lives in the .Strategy. namespace). Type.GetType now resolves,
    Sp.GetService finds the registered IMatchmakingStrategy implementation,
    _matchmakingInstalled = true, dashboard renders queue telemetry instead of
    the MissingPackageAlert. Operator confirmed pass on retest.
  severity: major
  test: 3
  defect_id: UAT-1-D1
  follow-up: |
    No bUnit/Playwright render test for the Admin Dashboard with matchmaking
    referenced exists today. A single render test exercising Dashboard.razor
    would have caught this typo at CI time. Defer to Phase 6 testing pass.

- truth: "Admin /admin/matchmaking panel shows the current leader instance and active lease count when the matchmaker ticker is running"
  initial_status: failed
  resolution: fixed-inline
  fix: |
    Two-part change after correcting initial design recommendation (the matcher
    lock is intentionally short-lived to coordinate mutex semantics with the
    reconciler + retention sweep — hold-and-renew would starve them):

    (a) Added MatchmakingRedisKeys.MatcherHeartbeat
        (= "gamekit:matchmaking:matcher:heartbeat") constant.
    (b) MatchmakerTickerService.RunOnceAsync writes the heartbeat after
        successful lock acquire with value = _lease.InstanceId and TTL =
        5 × TickIntervalMs. The lock semantics are unchanged.
    (c) RedisMatchmakingObservability.GetQueueStatsAsync now reads
        MatcherHeartbeat (not MatcherLock) to populate LeaderInstanceId +
        ActiveLeaseCount. Heartbeat is present iff a matcher has ticked in the
        recent past, so the panel correctly reports liveness without the
        snapshot-vs-lock race.
    (d) Updated MatchmakingObservabilityTests.GetQueueStats_LeaderIdentity_*
        to write MatcherHeartbeat in the seed step. Renamed to
        _Comes_From_HeartbeatKey_Not_Postgres.

    Verified in-running app: heartbeat key TTL refreshes every 500ms (drops only
    ~85ms across 1s polls because ticker resets it each tick); panel shows
    Leader instance = "noah-ubuntu:bee9daee-..." and Active leases = 1
    continuously. Operator confirmed pass on retest.
  severity: major
  test: 3
  defect_id: UAT-1-D2
  follow-up: |
    No integration test currently asserts that leaderInstanceId is non-null
    against a LIVE ticker (the test seeds the key directly). Adding such a test
    would require starting the BackgroundService in-test then polling — defer
    to Phase 6 testing pass. The current observability test still pins the
    contract that LeaderInstanceId comes from the documented key.

- truth: "Phase 5 sign-off docs reflect commit-evidence reality and route the operator to existing routes"
  initial_status: failed
  resolution: fixed-inline
  fix: |
    (1) 05-HUMAN-UAT.md sign-off table reconciled: SC#3 ✅ pass 2026-05-18
        (cites commit 8809c77 + summary section), UAT-3 ✅ pass 2026-05-22,
        UAT-1 🟡 issue → fixed 2026-05-22.
    (2) Admin matchmaking URL corrected to /admin/matchmaking in both
        05-HUMAN-UAT.md step 4 and samples/TicTacToeDuel/README.md §Admin
        queue-depth panel (was /admin/matchmaking/health and
        /admin/matchmaking/queue-depth respectively; actual @page is
        /admin/matchmaking).
    (3) 05-HUMAN-UAT.md curl example corrected: path /auth/login/guest with
        explicit Content-Type and {} body (was /api/auth/guest with no body —
        the route exists but rejects empty bodies); response key 'id' not
        'ladderId'; added X-GameKit-Device header.
  severity: minor
  test: 3
  defect_id: UAT-1-D3

## Notes

- This file consumes 05-HUMAN-UAT.md (the operator-instructions artifact). The
  HUMAN-UAT.md sign-off table will be reconciled to match this file's results at the
  end of the session.
- SC#3 was operator-run on 2026-05-18; recorded here as a sign-off of the existing
  commit evidence rather than a re-run.
- UAT-3 / UAT-1 / UAT-2 share the same sample-app session — running them in this order
  minimises restarts.
