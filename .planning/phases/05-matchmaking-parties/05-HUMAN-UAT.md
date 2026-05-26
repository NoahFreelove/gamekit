---
phase: 05
slug: matchmaking-parties
type: human-uat
created: 2026-05-18
requirements:
  - MATCH-13   # SC#3 1k-concurrent load test (manual run gate)
  - MATCH-14   # Admin UI live queue-depth + leader-identity (visual)
covers_validation_section: "Manual-Only Verifications"
---

# Phase 5 — Human UAT Items

> Manual / visual verifications surfaced for `/gsd:verify-work` to consume. Each row maps to
> exactly one item in `05-VALIDATION.md` §Manual-Only Verifications plus the SC#3 phase-gate
> load test. These are NOT automated by the integration or load test suites — they require
> operator-driven UAT.

---

## UAT-1: Admin UI live panels render queue depth / leader identity

**Requirements covered:** MATCH-14
**Validation source:** `05-VALIDATION.md` §Manual-Only Verifications row 1
**Why manual:** Visual integration with the Phase 3 Blazor `MainLayout`. Component-level
snapshot tests are brittle and Phase 3 set the precedent of UAT for admin chrome.

### Test instructions

1. **Pre-flight:** `docker compose up -d` (Postgres + Redis healthy via `docker ps`).
2. **Run the sample app** that ships the admin UI:
   ```bash
   dotnet run --project samples/TicTacToeDuel
   ```
3. **Log in as admin.** Navigate to `http://localhost:5000/admin`. Authenticate using the
   seeded admin credentials (`docker/postgres/init/*` defaults).
4. **Navigate to the matchmaking panel:** `/admin/matchmaking`
   (the panel is rendered by `QueueDepth.razor` at `@page "/admin/matchmaking"`,
   resolving `IMatchmakingObservability` via reflection — Plan 05-08 Task 4).
5. **Enqueue test tickets via curl** (use a guest-login JWT from `/auth/login/guest`):
   ```bash
   TOKEN=$(curl -s -X POST http://localhost:5000/auth/login/guest \
     -H 'X-GameKit-Device: uat-mm' -H 'Content-Type: application/json' -d '{}' \
     | jq -r .accessToken)
   LID=$(curl -s http://localhost:5000/demo/ladder-id/tictactoe | jq -r .id)
   for i in $(seq 1 3); do
     curl -X POST http://localhost:5000/api/mm/queue \
       -H "Authorization: Bearer $TOKEN" \
       -H "Content-Type: application/json" \
       -d "{\"ladderId\":\"$LID\",\"poolName\":\"tictactoe\"}"
   done
   ```
   Note: each enqueue from the same `X-GameKit-Device` value reuses the same guest
   player; vary it (`uat-mm-a`, `uat-mm-b`, …) if you want distinct queued tickets.
6. **Confirm the panel updates** within the polling interval (default 2 s):
   - Queue depth row for ladder `tictactoe` shows depth = 3 (or whatever the matcher has
     not yet drained).
   - Leader identity row shows a non-empty `{MachineName}:{Guid}` string.
   - Last-renewed timestamp updates as the ticker (500 ms) renews the lease.

### Expected outcome

The admin UI reflects the live Redis state — NOT a Postgres mirror. SC#6 phase gate
(`MatchmakingObservabilityTests.NotSourcedFromReconciliationMirrors`) already proves this
at the integration-test level; this UAT confirms the Blazor render path is wired
end-to-end with the operator's expected visual feedback.

### Pass / fail signals

- ✅ Pass: panel updates with each enqueue/match cycle; depth + leader identity track Redis.
- ❌ Fail: panel shows static "—" or stale data → reflection lookup of
  `IMatchmakingObservability` failed (check `Sp.GetService` returned non-null;
  Plan 05-08 Task 4 deviation §1 documents the reflection-safe fallback).

---

## UAT-2: `pause-queue` / `drain-queue` admin command-palette verbs

**Requirements covered:** MATCH-14
**Validation source:** `05-VALIDATION.md` §Manual-Only Verifications row 2
**Why manual:** Chrome interaction (command palette + confirm dialog) is not worth a UI
test-harness investment — same rationale as the Phase 3 admin chrome UAT pattern.

### Test instructions

1. **Pre-flight:** sample app running, admin logged in (as in UAT-1 step 1-4).
2. **Open the command palette:** press `⌘K` (macOS) or `Ctrl+K` (Windows/Linux).
3. **Type "pause queue"** — the `pause-queue` verb (registered by
   `AdminCommandRegistry`, Plan 05-08 Task 4) appears with `RequiresTarget=true`.
4. **Select the verb** — a confirmation dialog prompts for the ladder name (e.g.
   `tictactoe`).
5. **Confirm.** Behind the scenes:
   - `POST /admin/api/matchmaking/{ladderId}/pause-queue` is invoked
     (`MatchmakingAdminEndpoints`, Plan 05-08 Task 4).
   - `IAdminAuditWriter` records the `matchmaking.pause_queue` action.
   - Redis key `mm:control:paused` is SET.
6. **Verify enqueue is rejected.** Repeat the curl `POST /api/mm/queue` from UAT-1 step 5
   — the response should be HTTP 503 with body indicating queue paused.
7. **Type "drain queue"** in the palette. Confirm on the ladder. The matcher continues to
   process EXISTING tickets but rejects NEW enqueues.
8. **Inspect the audit log:** `GET /admin/api/audit?action=matchmaking.pause_queue` shows
   the rows with timestamp + admin actor + ladder target.

### Expected outcome

Operator can pause + drain a queue per-ladder via the admin chrome. The action is
audit-logged. The verbs respect the per-ladder scope decided in OQ-5
(`05-08-SUMMARY.md` §RESEARCH Open Questions row 5 — closed in Plan 05-08).

### Pass / fail signals

- ✅ Pass: palette shows both verbs; dialog prompts for ladder; Redis flag set;
  enqueue returns 503; audit row written.
- ❌ Fail: palette doesn't show the verb → `AdminCommandRegistry` registration missing
  (Plan 05-08 Task 4). Verb errors on confirm → cookie auth or
  `Superadmin` policy missing.

---

## UAT-3: `TicTacToeDuel` sample app 1v1 happy path

**Requirements covered:** MATCH-01 through MATCH-15 (sample integration)
**Validation source:** `05-VALIDATION.md` §Manual-Only Verifications row 3
**Why manual:** End-to-end demo behavior — the sample's value is showing integration
works, not unit-testable. The two-tab browser demo (one regular + one private/incognito)
is the canonical UAT for SC#1 + SC#5 visual verification.

### Test instructions

This UAT is the canonical operator demo shipped in Plan 05-09 Task 3. See
`samples/TicTacToeDuel/README.md` §Matchmaking for the full step-by-step procedure.
Abbreviated here:

1. **Pre-flight:**
   ```bash
   docker compose up -d
   ./scripts/gen-test-rsa-pem.sh   # if not already run
   dotnet run --project samples/TicTacToeDuel
   ```
   App on `http://localhost:5000`.
2. **Browser tab 1:** open `/matchmaking.html`. Click "Play as Guest". Click "Find Match".
3. **Browser tab 2 (private/incognito to get a separate JWT in localStorage):** open
   `/matchmaking.html`. Click "Play as Guest". Click "Find Match".
4. **Within ~1 s** both tabs transition to "Match proposed!" with a 10-second countdown
   (D-07 accept timeout).
5. **Click "Accept"** in both tabs within the 10 s window.
6. **Both tabs display "Matched! Both players accepted."** with the shared `sessionId`.

### Expected outcome

The 1v1 enqueue → propose → accept → match path completes end-to-end through the live
endpoints + ticker + proposal-service + session-create path. All Phase 5 packages
exercise their public surface against a real Redis + Postgres stack.

### Pass / fail signals

- ✅ Pass: both tabs show the same `sessionId` within ~11 s of clicking "Find Match"
  (1 s match-formation + 10 s accept countdown).
- ❌ Fail: see `05-09-SUMMARY.md` §Checkpoint Status (Task 3) for the four canonical
  failure modes (a)-(d) and their operator remediation steps.

---

## SC#3 Phase Gate: 1 000-concurrent-ticket sustained load test

**Requirements covered:** MATCH-13
**Validation source:** `05-VALIDATION.md` §SC#3 + §Sampling Rate
**Why manual:** 10+ minute runtime — not appropriate for default `dotnet test`. Operator
runs explicitly before phase sign-off.

### Test instructions

1. **Pre-flight:** Docker daemon running with ≥4 GB free
   (`docker info | grep "Total Memory"` ≥ `4GiB`). The test owns its own
   Testcontainer Postgres + Redis pair — no shared docker-compose setup needed.
2. **Build:**
   ```bash
   dotnet build tests/GameKit.Matchmaking.LoadTests --nologo
   ```
3. **Run:**
   ```bash
   dotnet test tests/GameKit.Matchmaking.LoadTests \
     --filter Category=LoadTest \
     --no-build \
     --logger "console;verbosity=detailed"
   ```
4. **Observe ~12-minute runtime** (10-minute sustain + ~2-minute warm-up + assertions).
5. **Halfway report at ~5-minute mark** prints:
   `TicksObserved=...`, `MaxIterationMs=...`, `p99=...`,
   `PoolExhaustionEvents=0`, `PoolWaitEvents=...`, `DroppedEvents=0`,
   `matched count: ...`.
6. **Final report** prints the SC#3 summary table; test PASSES if all four assertions
   hold (budget ≤ 50, pool ex == 0, dropped == 0, matched ≥ 1 000).

### Expected outcome

Test PASSED. Numerical bar:
- `MaxIterationMs` ≤ 50 (per-tick budget)
- `PoolExhaustionEvents` == 0 (Pitfall §8 verified under load)
- `DroppedEvents` == 0 (D-15 channel capacity sufficient)
- `Matched tickets` ≥ 1 000 (matcher made forward progress)

### Pass / fail signals + remediation

See `tests/GameKit.Matchmaking.LoadTests/README.md` §Failure-mode triage for the full
table mapping each assertion failure to its likely cause and operator remediation.

---

## Sign-off

**For `/gsd:verify-work`:** each UAT-* item above is a separate verification request. The
verifier prompts the operator with the test instructions, accepts a pass/fail signal, and
records the result. The SC#3 load test is the gating item — phase cannot close until it
passes.

| UAT | Status |
|-----|--------|
| UAT-1 (admin UI live panels) | 🟡 issue → fixed 2026-05-22 (Dashboard ns typo + ticker-vs-observability lock-pattern bug; heartbeat key added) — retest in 05-UAT.md |
| UAT-2 (pause-queue / drain-queue verbs) | ⬜ pending |
| UAT-3 (TicTacToeDuel 1v1 happy path) | ✅ pass 2026-05-22 (recorded in 05-UAT.md) |
| SC#3 load test | ✅ pass 2026-05-18 (commit `8809c77`; MaxIterationMs=29, Matched=3092, Dropped=0 — see 05-10-SUMMARY.md §Post-UAT Resolution) |

---

*Phase: 05-matchmaking-parties*
*UAT package created: 2026-05-18*
