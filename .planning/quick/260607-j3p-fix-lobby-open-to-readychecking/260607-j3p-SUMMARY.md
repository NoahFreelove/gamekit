---
status: complete
phase: quick-260607-j3p
plan: 01
subsystem: GameKit.Lobby
tags: [bug-fix, lobby, state-machine, signalr, integration-tests]
dependency_graph:
  requires: []
  provides: [Open->ReadyChecking auto-start in JoinLobbyAsync, member-count fix, OpenToReadyCheckTests]
  affects: [src/GameKit.Lobby/Services/LobbyService.cs, src/GameKit.Lobby/Services/ILobbyService.cs, tests/GameKit.Lobby.Integration.Tests/OpenToReadyCheckTests.cs]
tech_stack:
  added: []
  patterns: [auto-start-when-full state transition, guarded EF fixup add, TCS-based SignalR broadcast assertion]
key_files:
  modified:
    - src/GameKit.Lobby/Services/LobbyService.cs
    - src/GameKit.Lobby/Services/ILobbyService.cs
    - .planning/milestones/v2.0-MILESTONE-AUDIT.md
  created:
    - tests/GameKit.Lobby.Integration.Tests/OpenToReadyCheckTests.cs
decisions:
  - "Auto-start when full: JoinLobbyAsync (not CreateLobbyAsync) is the correct trigger location — the fill event is a join, not a create."
  - "Guarded lobby.Members.Add: All(m => m.Id != member.Id) guard applied in both CreateLobbyAsync and JoinLobbyAsync to prevent EF relationship fixup double-count."
  - "No new migration, no new public API surface — pure runtime behavior fix."
  - "MILESTONE-AUDIT update committed in the same PR as the fix (doc-only commit 7b5a70f)."
metrics:
  duration: ~5min
  completed: 2026-06-07
  tasks: 3
  files_changed: 4
---

# Quick 260607-j3p: Lobby Open to ReadyChecking Fix Summary

**One-liner:** JoinLobbyAsync auto-starts ReadyChecking when a join fills the lobby to MaxMembers, making the LOBBY-03 ready-check to matchmaking to InGame flow reachable through the public REST API for the first time.

## What Was Fixed

Two bugs in `GameKit.Lobby/Services/LobbyService.cs`:

**Bug 1 (critical): Missing Open to ReadyChecking edge**
A lobby created via `POST /api/lobbies` was stuck in `LobbyState.Open` forever. The `MarkReadyAsync` all-ready gate is guarded by `lobby.State == LobbyState.ReadyChecking`, which was never satisfiable from a real Open lobby. The `ReadyChecking` state was only ever assigned by `RevertToReadyCheckingAsync` (reachable only FROM InGame) — a dead path from Open.

The root cause: there was no `Open->ReadyChecking` edge. Existing tests in `ReadyCheckTests` only exercised the all-ready flow because `SeedLobbyAsync` inserts `State=1 (ReadyChecking)` directly via raw Npgsql, masking the missing edge.

**Fix:** In `JoinLobbyAsync`, after the guarded member add, check: if `lobby.Members.Count == lobby.MaxMembers && lobby.State == LobbyState.Open`, then set `State = ReadyChecking`, persist, and broadcast `ReceiveStateUpdateAsync(new LobbyStateUpdate(lobbyId, LobbyState.ReadyChecking))` — mirroring the existing `MarkReadyAsync` broadcast pattern exactly.

**Bug 2 (cosmetic): Member count over-reported by 1**
Both `CreateLobbyAsync` and `JoinLobbyAsync` called `lobby.Members.Add(member)` AFTER `_ctx.Set<LobbyMemberEntity>().Add(member)`. EF Core relationship fixup already attaches the tracked member to `lobby.Members` during the `Add()` call, so the explicit `lobby.Members.Add(member)` duplicated the reference. Live symptom: REST join returned `memberCount=3` for a 2-member lobby.

**Fix:** Replace the unconditional `lobby.Members.Add(member)` with a guard: `if (lobby.Members.All(m => m.Id != member.Id)) lobby.Members.Add(member)` in both methods.

## Tasks Completed

### Task 1 — LobbyService fix
- **Commit:** `06954bb`
- **Files:** `src/GameKit.Lobby/Services/LobbyService.cs`, `src/GameKit.Lobby/Services/ILobbyService.cs`
- Guarded `lobby.Members.Add` in both `CreateLobbyAsync` and `JoinLobbyAsync`
- Added Open->ReadyChecking auto-start trigger in `JoinLobbyAsync` (after guarded add)
- Updated `ILobbyService.JoinLobbyAsync` XML summary to describe the new trigger (CS1591 compliance)
- Build: clean under `-warnaserror`, 0 warnings, 0 errors

### Task 2 — Integration tests
- **Commit:** `efba2cf`
- **File:** `tests/GameKit.Lobby.Integration.Tests/OpenToReadyCheckTests.cs`
- **Test A** (`FullLifecycle_FromOpen_Through_InGame_With_PartyCreated`): Creates a maxMembers=2 lobby via REST, connects owner hub, REST-joins second player to fill lobby, asserts ReadyChecking broadcast + DB state, then both mark ready and asserts InGame broadcast + DB state + party row in `gamekit.parties`. No `SeedLobbyAsync` used.
- **Test B** (`MemberCount_IsNotOverCounted_AfterRestJoin`): Creates a maxMembers=8 lobby (non-filling join), REST-joins second player, asserts `memberCount==2` from join response AND from `GET /api/lobbies/{id}`. Guards against the EF fixup double-count regression.
- Both tests pass against real Postgres + Redis via Testcontainers.

### Task 3 — Full-suite gate + MILESTONE-AUDIT update
- **Full suite result:** 20 tests, 0 failed, 0 skipped (18 pre-existing + 2 new)
- **Commit:** `7b5a70f`
- MILESTONE-AUDIT.md `tech_debt: phase: 11-gamekit-lobby items:` extended with a RESOLVED entry referencing `260607-j3p`. YAML validated with `yaml.safe_load`. Test coverage line updated to `Lobby 20`. W-1 item and all other phase blocks untouched.

## Deviations from Plan

None — plan executed exactly as written. All three actions (guarded add in CreateLobbyAsync, guarded add in JoinLobbyAsync, Open->ReadyChecking trigger in JoinLobbyAsync only) match the plan spec.

The audit-doc edit was committed (not left staged) in commit `7b5a70f` as the final doc-only commit per the plan's commit-message spec.

## Known Stubs

None. The fix wires a real state transition with real persistence and real SignalR broadcast. No hardcoded states, no placeholder values, no mock data flows.

## Threat Flags

No new threat surface introduced. The Open->ReadyChecking trigger runs only inside the existing `JoinLobbyAsync` path, after the existing MaxMembers cap guard and duplicate-member guard — neither gate is bypassed by the trigger. The trigger only fires on the join that REACHES the cap; the cap guard at the top of `JoinLobbyAsync` still rejects joins when already at the cap. See threat model T-j3p-01 in the PLAN for full disposition.

## Self-Check: PASSED

- `src/GameKit.Lobby/Services/LobbyService.cs` — modified, present
- `src/GameKit.Lobby/Services/ILobbyService.cs` — modified, present
- `tests/GameKit.Lobby.Integration.Tests/OpenToReadyCheckTests.cs` — created, present
- `.planning/milestones/v2.0-MILESTONE-AUDIT.md` — modified, j3p entry present (grep -c returns 2)
- Commits: `06954bb`, `efba2cf`, `7b5a70f` — all present in git log
- Full Lobby integration suite: 20 passed, 0 failed
