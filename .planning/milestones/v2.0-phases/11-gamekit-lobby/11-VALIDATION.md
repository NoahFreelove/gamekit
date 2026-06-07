---
phase: 11
slug: gamekit-lobby
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-06
---

# Phase 11 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 (.NET 10) + Testcontainers 4.11 (real Postgres + Redis) + Moq + Microsoft.AspNetCore.SignalR.Client (test client) |
| **Config file** | `tests/GameKit.Lobby.Integration.Tests/` (new) — two-TestServer shared-Redis-backplane harness |
| **Quick run command** | `dotnet test tests/GameKit.Lobby.Tests/GameKit.Lobby.Tests.csproj` (unit, if present) |
| **Full suite command** | `dotnet test GameKit.sln` |
| **Estimated runtime** | unit <30s; Lobby integration (Testcontainers + SignalR) ~3-6min |

---

## Sampling Rate

- **After every task commit:** run the affected package's unit/quick command.
- **After every plan wave:** run the Lobby integration project.
- **Before `/gsd:verify-work`:** full suite green.
- **Max feedback latency:** ~30s unit; integration on wave boundaries.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 11-xx | TBD | 0 | OPS-11/LOBBY-01 | — | Lobby advisory-lock key pairwise-distinct from all 5 existing keys (live-verified) | integration | `dotnet test tests/GameKit.Lobby.Integration.Tests/... --filter AdvisoryLock` | ❌ W0 | ⬜ pending |
| 11-xx | TBD | TBD | LOBBY-02 | T-11-ws-authz | Player JWT authenticates WS upgrade to /hubs/lobby; unauthenticated upgrade → HTTP 401 before handshake | integration (2 TestServer) | `dotnet test ... --filter WebSocketAuth` | ❌ W0 | ⬜ pending |
| 11-xx | TBD | TBD | LOBBY-03/LOBBY-05 | — | All lobby_members.ready=true → TryStartMatchmakingAsync submits party ticket via IMatchmakingService.EnqueueAsync; state ReadyChecking→InGame; broadcast observed | integration | `dotnet test ... --filter ReadyCheck` | ❌ W0 | ⬜ pending |
| 11-xx | TBD | TBD | LOBBY-04 | — | Chat reaches all group members in real time; NO chat table exists; nothing written to Postgres on send (anti-feature) | integration | `dotnet test ... --filter ChatEphemeral` | ❌ W0 | ⬜ pending |
| 11-xx | TBD | TBD | LOBBY-06 | — | Broadcast from LobbyHub instance A reaches a client on instance B via shared Redis backplane | integration (2 TestServer) | `dotnet test ... --filter Backplane` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*Task IDs finalized by the planner; this strategy seeds the five success-criteria behaviors.*

---

## Wave 0 Requirements

- [ ] New `tests/GameKit.Lobby.Integration.Tests/` project + two-TestServer shared-Redis-backplane harness + Testcontainers fixtures.
- [ ] Lobby advisory-lock-key pairwise-distinctness test (SC#1) — live `SELECT hashtext('gamekit.lobby.migrations')::bigint`, distinct from Core/Auth/Admin/Rankings/Matchmaking.
- [ ] WebSocket-auth (SC#2), ready-check→matchmaking (SC#3), chat-ephemeral/no-table (SC#4), backplane cross-instance (SC#5) test stubs.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| (none) | — | — | — |

*All phase behaviors have automated verification (xUnit + Testcontainers + two-TestServer SignalR harness).*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s (unit)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
