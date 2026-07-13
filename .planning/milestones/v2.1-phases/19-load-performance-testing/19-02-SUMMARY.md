---
phase: 19-load-performance-testing
plan: "02"
subsystem: k6-signalr-spike
tags: [k6, signalr, websocket, perf, lobby, spike, go-nogo]
dependency_graph:
  requires: [19-01]
  provides: [spike-signalr.js, helpers/signalr.js, tests/k6/README.md, SpikeHost, go-nogo-decision]
  affects: [19-05-PLAN.md]
tech_stack:
  added:
    - "k6 v2.0.0 (grafana/k6:latest Docker image, AGPLv3, external CLI only)"
    - "k6/websockets stable module (WHATWG WebSocket API)"
    - "SpikeHost: minimal ASP.NET Core 10 Kestrel host for spike infrastructure"
  patterns:
    - "k6 events-fire-after-sleep pattern: all WebSocket event callbacks run after sleep() completes in k6/websockets"
    - "SignalR JSON protocol: negotiate POST + WS upgrade + handshake frame + RECORD_SEP (0x1E) + hub invocation"
    - "SpikeHost: per-package EF Core migrations applied via GameKit.Cli before host startup"
key_files:
  created:
    - tests/k6/helpers/signalr.js
    - tests/k6/spike-signalr.js
    - tests/k6/README.md
    - tests/k6/SpikeHost/Program.cs
    - tests/k6/SpikeHost/SpikeHost.csproj
    - tests/k6/SpikeHost/Directory.Build.props
  modified: []
decisions:
  - "GO: stock grafana/k6 v2.0.0 with k6/websockets is sufficient for the SignalR JSON protocol — no xk6 extension needed. Plan 05 (fan-out scenario) proceeds with stock k6."
  - "k6/websockets events fire AFTER sleep() completes, not concurrently. All protocol state machine logic must live inside event callbacks (open/message/close). sleep() acts only as a session deadline."
  - "SpikeHost committed as developer tooling in tests/k6/SpikeHost/ — excluded from dotnet test via Directory.Build.props override that removes xUnit/Test.Sdk auto-inclusions from the parent tests/Directory.Build.props."
  - "Migrations applied via GameKit.Cli (canonical 6-package order: Core → Auth → Admin → Rankings → Matchmaking → Lobby) before SpikeHost startup."
metrics:
  duration: "19m"
  completed_date: "2026-06-23"
  tasks_completed: 2
  files_created: 6
status: complete
---

# Phase 19 Plan 02: k6 SignalR Spike + GO/NO-GO Checkpoint Summary

Stock `grafana/k6` v2.0.0 with the stable `k6/websockets` module successfully completes the full SignalR JSON protocol handshake against the real Lobby hub at `/hubs/lobby` — **GO decision recorded, Plan 05 fan-out proceeds with stock k6**.

---

## What Was Built

### Task 1: SignalR helper module + spike script

- **`tests/k6/helpers/signalr.js`** — Reusable SignalR helpers (`negotiateSignalR`, `connectSignalR`, `invoke`, `RECORD_SEP`) using `k6/websockets` (stable WHATWG API). Includes:
  - `negotiateVersion=1` query param in negotiate POST (required for ASP.NET Core SignalR 8+)
  - RECORD_SEP `\x1e` frame termination
  - Server ping (`type=6`) auto-pong
  - Multi-frame splitting on RECORD_SEP

- **`tests/k6/spike-signalr.js`** — Standalone GO/NO-GO spike implementing the six-step sequence:
  1. HTTP POST to `/hubs/lobby/negotiate?negotiateVersion=1`
  2. WebSocket upgrade to `/hubs/lobby?id=<connectionToken>`
  3. Send `{"protocol":"json","version":1}\x1e`
  4. Assert `{}` handshake-ack received
  5. Invoke `JoinLobbyAsync(<LOBBY_ID>)`
  6. Assert a result or hub error frame arrives

### Task 2: k6 README

- **`tests/k6/README.md`** — Documents Docker invocation (stdin + volume-mount modes), Linux `--network host` vs macOS `host.docker.internal`, credential hygiene (JWTs via `-e`, LOCAL stack only, never production), and the AGPLv3 external-CLI-only licensing posture.

### Task 3: SpikeHost (developer tool)

- **`tests/k6/SpikeHost/`** — Minimal ASP.NET Core 10 Kestrel host with the full GameKit Lobby pipeline (Auth + Rankings + Matchmaking + Lobby) on `http://localhost:5100`. Applies all 6-package migrations via `gamekit migrations apply`, seeds a test player/ladder/lobby, and prints `SPIKE_JWT` + `SPIKE_LOBBY_ID` for use with `docker run -e`.

---

## Task 3: GO/NO-GO Checkpoint Result

### Evidence — k6 Spike Output (2026-06-23, grafana/k6:latest v2.0.0)

```
Step 1 OK — connectionToken=3GxVflk6...
Step 2+3: WebSocket open — sending SignalR handshake...
Step 4 OK — handshake ack {} received
Step 5 — invoking JoinLobbyAsync("542f5418-f9bf-4e15-ab04-60eed0f6f9a5")
Step 6 OK — JoinLobbyAsync result: null
WebSocket closed: code=1000

  █ THRESHOLDS
    checks ✓ 'rate==1.0' rate=100.00%

  █ TOTAL RESULTS
    checks_succeeded: 100.00%  3 out of 3

    ✓ step1: negotiate returned connectionToken
    ✓ step3+4: SignalR JSON handshake ack received ({})
    ✓ step5+6: JoinLobbyAsync invocation got a response (result or error)

    WEBSOCKET
    ws_connecting:         avg=1.24ms
    ws_msgs_received:      2
    ws_msgs_sent:          3
    ws_session_duration:   avg=10s
```

### Decision: GO

Stock `grafana/k6` v2.0.0 with the stable `k6/websockets` module CAN complete the SignalR JSON protocol handshake. No xk6 extension build is needed. Plan 05 (Lobby SignalR fan-out scenario) PROCEEDS with stock k6.

**Infrastructure used for spike:**
- Postgres 17 on `localhost:5499` (Docker: `gamekit-spike-postgres`)
- Redis 8.6.2 on `localhost:6399` (Docker: `gamekit-spike-redis`)
- Migrations applied via `dotnet run --project src/GameKit.Cli -- migrations apply`
- SpikeHost (`tests/k6/SpikeHost`) started on `http://localhost:5100`

---

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed k6/websockets event dispatch timing model**
- **Found during:** Task 3 (spike execution)
- **Issue:** Initial spike used `sleep(3)` then checked `handshakeAckReceived` post-sleep. In k6/websockets, all event callbacks (open, message, close) fire AFTER sleep() completes, not concurrently during sleep. The state machine ran checks before events fired, always recording failure even when the protocol succeeded.
- **Discovery evidence:** Debug test showed `ws_msgs_received: 1` and `"WS message #1: \"{}\\u001e\""` — the handshake ack WAS being received, but only after sleep ended.
- **Fix:** Moved all protocol logic (handshake detection, invoke call) into the `message` handler; moved all `check()` calls into the `close` handler. `sleep()` now acts purely as a session deadline.
- **Files modified:** `tests/k6/spike-signalr.js`
- **Commit:** `593151a`

**2. [Rule 2 - Missing infrastructure] Added SpikeHost development tool**
- **Found during:** Task 3 execution
- **Issue:** The plan required running the spike against a live Lobby hub but provided no hosted infrastructure. The sample app TicTacToeDuel needed manual Postgres/Redis setup; the integration test `LobbyTestApp` was not externally addressable.
- **Fix:** Built `tests/k6/SpikeHost` — a minimal self-contained host that starts Kestrel on port 5100 with the full GameKit Lobby pipeline, applies migrations via the CLI, and seeds test data. Not shipped as NuGet; developer-only.
- **Files modified:** `tests/k6/SpikeHost/{Program.cs,SpikeHost.csproj,Directory.Build.props}`
- **Commit:** `f6100ba`

**3. [Rule 3 - Blocking] Removed test SDK auto-inclusion conflict**
- **Found during:** SpikeHost build
- **Issue:** The `tests/Directory.Build.props` auto-includes `Microsoft.NET.Test.Sdk` for all projects under `tests/`. This caused `CS7022: The entry point of the program is global code; ignoring AutoGeneratedProgram.Main entry point` since Test.Sdk generates its own Program.
- **Fix:** Added `tests/k6/SpikeHost/Directory.Build.props` that re-imports the repo root `Directory.Build.props` (skipping the `tests/` one) and removes the test runner package references.
- **Files modified:** `tests/k6/SpikeHost/Directory.Build.props`

**4. [Rule 3 - Blocking] Suppressed EF Core PendingModelChangesWarning in SpikeHost**
- **Found during:** SpikeHost migration step
- **Issue:** EF Core 10 throws `PendingModelChangesWarning` when applying migrations with a custom model customizer that doesn't exactly match the migration snapshot.
- **Fix:** Added `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` — safe for the spike because we're applying the committed migration history, not creating new migrations.

**5. [Rule 3 - Blocking] Added `Search Path=gamekit,public` to connection strings**
- **Found during:** SpikeHost seeding
- **Issue:** The `gamekit_app` role saw `relation "session_complete_idempotency" does not exist` because EF Core queries tables without schema prefix and the default search_path didn't include the `gamekit` schema.
- **Fix:** Added `Search Path=gamekit,public` to all connection strings in SpikeHost.

---

## Threat Surface Scan

No new security-relevant surface beyond what the plan's threat model documented:
- T-19-02-01 (JWT in scripts) — mitigated: all tokens via `__ENV`; `grep -rniE "eyJ..."` returns nothing
- T-19-02-02 (k6 against production) — mitigated: README states NEVER-against-production
- T-19-02-03 (AGPLv3 contamination) — mitigated: k6 is external CLI only; documented in README

---

## Known Stubs

None — this plan produces tooling scripts and an infrastructure host, not application features with data-binding requirements.

---

## Self-Check

### Created files exist:
- `tests/k6/helpers/signalr.js` — FOUND
- `tests/k6/spike-signalr.js` — FOUND
- `tests/k6/README.md` — FOUND
- `tests/k6/SpikeHost/Program.cs` — FOUND
- `tests/k6/SpikeHost/SpikeHost.csproj` — FOUND
- `tests/k6/SpikeHost/Directory.Build.props` — FOUND

### Commits exist:
- `8abb8b3` feat(19-02): add k6 SignalR helper module and spike script — FOUND
- `9b5b77a` docs(19-02): add k6 README — FOUND
- `593151a` fix(19-02): fix spike event-handler model — FOUND
- `f6100ba` chore(19-02): add SpikeHost — FOUND

### Spike result verified:
- k6 threshold `rate==1.0`: PASSED (100%)
- All 3 checks passed against live Lobby hub

## Self-Check: PASSED
