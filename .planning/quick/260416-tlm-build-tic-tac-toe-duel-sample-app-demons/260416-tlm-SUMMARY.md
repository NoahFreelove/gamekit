---
phase: 260416-tlm-quick
plan: 01
subsystem: sample
tags: [aspnetcore, minimal-apis, efcore, jsonb, demo, vanilla-js, tic-tac-toe]

# Dependency graph
requires:
  - phase: 01-foundation-core-migrations-ops-defaults-gpl
    provides: GameKit.Core runtime (AddGameKit, UseGameKit, MapGameKit, GameKitDbContext, Player/GameSession/SessionParticipant entities, IClock, IIdGenerator, IPlayerDisplayNameResolver, JSONB metadata wiring)
provides:
  - Executable Phase-1 sample that exercises Postgres persistence, GameSession lifecycle, and JSONB board state end-to-end
  - Reference integration pattern "how does a game integrate GameKit.Core" (AddGameKit options wiring + MapDemo over GameKitDbContext)
  - Vanilla-JS zero-build-step static client backed by minimal-API /demo/* endpoints
affects: [phase-02-auth, future-samples]

# Tech tracking
tech-stack:
  added: []  # zero new NuGet deps
  patterns:
    - Anonymous /demo/* endpoint group deliberately segregated from /api/* (temporary until GameKit.Auth)
    - Board state stored in GameSession.Metadata (JSONB) via versioned {v:1, cells, moveCount, outcome} shape
    - Server-side re-derivation on every move (no client-provided board state trusted)
    - Team<->Mark convention (Team 0 = X, Team 1 = O) for participant rows
    - SessionResult mapped from terminal BoardOutcome at Complete() time

key-files:
  created:
    - samples/TicTacToeDuel/Game/TicTacToeBoard.cs
    - samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs
    - samples/TicTacToeDuel/Http/DemoContracts.cs
    - samples/TicTacToeDuel/Http/DemoEndpoints.cs
    - samples/TicTacToeDuel/wwwroot/index.html
    - samples/TicTacToeDuel/README.md
  modified:
    - samples/TicTacToeDuel/TicTacToeDuel.csproj (renamed from SampleGame.csproj; RootNamespace + AssemblyName updated)
    - samples/TicTacToeDuel/Program.cs (rewritten: UseDefaultFiles/UseStaticFiles + MapDemo())
    - GameKit.sln (project name + relative path updated; ProjectGuid preserved)

key-decisions:
  - "Board state lives in GameSession.Metadata as a versioned JSON document (v=1) rather than a bespoke table — the sample exercises the real JSONB surface Phase 1 shipped"
  - "Team 0 = X, Team 1 = O mapping convention documented at the domain-model level so callers can reason about it without reading the endpoint handler"
  - "Server-side board re-derivation on every move — the client never supplies cells[][]; FromJsonDocument + ApplyMove are the only mutation path"
  - "SPDX HTML-comment header on index.html even though the license-check script only covers .cs — defensive for future policy changes"
  - "Zero new NuGet dependencies — System.Text.Json (BCL) handles the JsonDocument round-trip; ASP.NET Core minimal APIs handle routing"
  - "JsonDocument.Dispose() called before replacing session.Metadata to avoid leaking the unmanaged pooled buffer"

patterns-established:
  - "Quick-plan sample pattern: rename (git mv) + domain module + Http endpoint group + wwwroot/ static client, all in one csproj, no new deps"
  - "/demo/* endpoint-group convention: anonymous, tagged TicTacToeDuel.Demo, TEMPORARY-commented above the register endpoint"
  - "Board serializer versioning: first field is an int 'v' schema number so older docs can be rejected/migrated cleanly"

requirements-completed: []  # quick task — not tied to a roadmap requirement id

# Metrics
duration: 5min
completed: 2026-04-17
---

# Quick 260416-tlm: Tic-Tac-Toe Duel Sample Summary

**Renamed SampleGame to TicTacToeDuel, added a vanilla-JS + minimal-API tic-tac-toe sample that exercises GameKit.Core's GameSession/SessionParticipant lifecycle and JSONB Metadata persistence end-to-end — zero new NuGet deps.**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-04-17T01:24:47Z
- **Completed:** 2026-04-17T01:30:12Z
- **Tasks:** 3
- **Files modified:** 9 (6 created, 3 renamed-or-edited)

## Accomplishments

- `samples/SampleGame/` renamed to `samples/TicTacToeDuel/` via `git mv` (history preserved); csproj, solution file, and `RootNamespace`/`AssemblyName` updated; solution ProjectGuid preserved so IDE state survives.
- Pure domain `TicTacToeBoard` + versioned `TicTacToeBoardSerializer` — 3x3 cells, Mark/BoardOutcome enums, full move-validation set (bounds, occupancy, turn, game-over) and winner detection (rows/cols/diagonals/draw).
- Four `/demo/*` minimal-API endpoints: anonymous `POST /players/register` (marked TEMPORARY in code + README), `POST /games`, `GET /games/{id}`, `POST /games/{id}/moves`. Create-game seeds an empty board into `GameSession.Metadata`; terminal outcome triggers `SessionParticipant.Result` assignment plus `GameSession.Complete(clock.UtcNow)`.
- Zero-framework `wwwroot/index.html` client with inline CSS + JS, 3x3 clickable grid, player-registration + start-game UX, outcome/new-game handling, fetch()-only transport.
- README with run instructions, endpoint reference table, Phase-2 auth-replacement warning, troubleshooting, and GPL license line.

## Task Commits

1. **Task 1: Rename SampleGame -> TicTacToeDuel** — `2f42f1d` (feat)
2. **Task 2: Domain model + demo API endpoints + Program.cs rewrite** — `2c009a3` (feat)
3. **Task 3: HTML client + README** — `677260e` (feat)

## Files Created/Modified

- `samples/TicTacToeDuel/Game/TicTacToeBoard.cs` (new) — pure 3x3 domain with `ApplyMove` validation + outcome computation.
- `samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs` (new) — `JsonDocument` <-> board round-trip at `v=1`.
- `samples/TicTacToeDuel/Http/DemoContracts.cs` (new) — request/response record DTOs.
- `samples/TicTacToeDuel/Http/DemoEndpoints.cs` (new) — `MapDemo()` endpoint group with four handlers; shared `BuildResponseAsync` helper resolves display names via `IPlayerDisplayNameResolver`.
- `samples/TicTacToeDuel/Program.cs` (rewritten) — `AddGameKit` wiring + `UseDefaultFiles`/`UseStaticFiles` (before `UseGameKit`) + `MapGameKit` + `MapDemo`.
- `samples/TicTacToeDuel/wwwroot/index.html` (new, 192 lines) — vanilla-JS client, no CDN.
- `samples/TicTacToeDuel/README.md` (new, 64 lines) — run instructions, endpoint list, temporary-endpoint warning.
- `samples/TicTacToeDuel/TicTacToeDuel.csproj` (renamed + edited) — `RootNamespace=TicTacToeDuel`, `AssemblyName=TicTacToeDuel`, no new `PackageReference`.
- `GameKit.sln` — project name `SampleGame` -> `TicTacToeDuel`, path updated, `ProjectGuid {50625367-…}` preserved.

## Decisions Made

- Metadata column chosen as the board store — not a new table — because it is the explicit Phase-1 JSONB surface and the sample's purpose is to exercise it.
- Server-side re-derivation on every move (client only sends `{playerId, row, col}`) — closes T-ttt-03 tampering vector at the domain level.
- No new NuGet dependencies — `System.Text.Json` round-trips the board and ASP.NET Core minimal APIs cover routing.
- Schema versioning via a leading `"v": 1` integer inside the JSON document — cheaper than bumping migrations when the board format ever changes.
- `JsonDocument.Dispose()` before reassigning `session.Metadata` in the move handler to release the pooled buffer owned by the previous document.

## Deviations from Plan

None — plan executed exactly as written. No auto-fixes, no blocked tasks, no architectural questions raised. All three task `<verify>` blocks and the overall `<verification>` block pass:

- `dotnet build` is green (0 warnings / 0 errors).
- `dotnet build samples/TicTacToeDuel` is green (0 warnings / 0 errors).
- `scripts/check-headers.sh` passes — every new `.cs` file carries the SPDX GPL-3.0-or-later header.
- `grep -r SampleGame` across the repo returns nothing (outside `.planning/` history).
- `samples/TicTacToeDuel/TicTacToeDuel.csproj` contains no `<PackageReference>` (only the `<ProjectReference>` to `GameKit.Core`).
- No file under `src/` or `tests/` was touched.

**Total deviations:** 0
**Impact on plan:** None.

## Issues Encountered

None.

## User Setup Required

None — the existing `docker-compose.yml` + `appsettings.Development.json` connection strings are reused. Runtime flow:

```bash
docker compose up -d
dotnet run --project samples/TicTacToeDuel
# open http://localhost:5000
```

## Manual Smoke Test

Not executed in this run (constraint "do NOT run `dotnet test`" kept the verification to build-level). Manual end-to-end smoke (`docker compose up -d` -> `dotnet run` -> open browser -> register -> play to terminal outcome) remains the authoritative acceptance. Build-time gates (Tasks 1-3 automated `verify` blocks) all pass.

## Phase-2 Follow-ups

- Replace `POST /demo/players/register` with authenticated registration from `GameKit.Auth` (Steam/Discord OAuth providers + JWT issuance per STACK.md).
- Attach the Phase-1 `GameKitRateLimitPolicies` to the demo surface once it becomes `/api/sessions` in Phase 3 (Matchmaking).
- Promote `POST /demo/games` + `POST /demo/games/{id}/moves` to proper `/api/sessions/*` endpoints — the demo's schema is compatible and the shape already flows through `IPlayerDisplayNameResolver`.
- Delete the `/demo/*` group from the sample once Phase 2/3 land the real equivalents.

## Next Phase Readiness

- Phase-1 foundation now has an executable, visual proof-of-life that touches every public surface Phase 1 shipped: DI wiring, migrations-under-lock, EF entities, JSONB metadata, display-name resolution, authorization gating on `/api/players`.
- Phase 2 can begin without blockers; the sample will serve as the regression target when the `/demo/players/register` endpoint is retired.

## Self-Check: PASSED

- [x] `samples/TicTacToeDuel/Game/TicTacToeBoard.cs` exists.
- [x] `samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs` exists.
- [x] `samples/TicTacToeDuel/Http/DemoContracts.cs` exists.
- [x] `samples/TicTacToeDuel/Http/DemoEndpoints.cs` exists.
- [x] `samples/TicTacToeDuel/Program.cs` exists (rewritten).
- [x] `samples/TicTacToeDuel/wwwroot/index.html` exists.
- [x] `samples/TicTacToeDuel/README.md` exists.
- [x] `samples/SampleGame/` removed.
- [x] Commit `2f42f1d` in git log (Task 1).
- [x] Commit `2c009a3` in git log (Task 2).
- [x] Commit `677260e` in git log (Task 3).

---
*Phase: 260416-tlm-quick*
*Completed: 2026-04-17*
