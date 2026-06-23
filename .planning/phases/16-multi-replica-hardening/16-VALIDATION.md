---
phase: 16
slug: multi-replica-hardening
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-22
---

# Phase 16 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11 (Postgres + Redis) + Moq |
| **Config file** | per-test-project `.csproj`; `tests/*.Integration.Tests` |
| **Quick run command** | `dotnet test tests/GameKit.Core.Tests -p:NuGetAudit=false` |
| **Full suite command** | `dotnet test GameKit.sln -p:NuGetAudit=false` (Docker required for Testcontainers) |
| **Estimated runtime** | ~90–240 seconds (integration tests spin Postgres + Redis containers) |

---

## Sampling Rate

- **After every task commit:** Run the affected package's unit suite (`dotnet test tests/<pkg>.Tests`)
- **After every plan wave:** Run affected-package full suites including `.Integration.Tests`
- **Before verification:** Full affected-package suites must be green (per "full-suite gate" rule — sibling integration tests must not be left red)
- **Max feedback latency:** ~240 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 16-01-xx | 01 | 1 | SCALE-01 | — | All lease helpers implement `ILeaderLease`; no `LockTakeAsync` outside an `ILeaderLease` impl | unit + grep gate | `dotnet test tests/GameKit.Core.Tests` + CI grep | ❌ W0 | ⬜ pending |
| 16-02-xx | 02 | 2 | SCALE-02 | — | `ReleaseLeaseAsync` uses `CancellationToken.None` on all finally paths (5 sites) | unit (assert token) | `dotnet test tests/GameKit.Matchmaking.Tests` | ❌ W0 | ⬜ pending |
| 16-03-xx | 03 | 2 | SCALE-03 | — | Concurrent `SessionCompleteAsync` w/ same key → exactly one `game_sessions` row | integration (Testcontainers PG) | `dotnet test tests/GameKit.Core.Integration.Tests` | ❌ W0 | ⬜ pending |
| 16-04-xx | 04 | 3 | SCALE-04 | — | Lease expiry mid-tick → zero duplicate `game_sessions`, no ticker gap > 1 lock TTL | integration (2 replicas, PG+Redis) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter MatchmakerSplitBrainTests` | ❌ W0 | ⬜ pending |
| 16-05-xx | 05 | 3 | SCALE-05 | — | 100 concurrent in-flight requests + SIGTERM → zero 5xx, zero duplicate matches | integration (drain) | `dotnet test tests/...Integration.Tests --filter GracefulDrain` | ❌ W0 | ⬜ pending |
| 16-06-a | 06 | 1 | SCALE-06 (Lobby) | — | All lobby clients receive hub events regardless of sending replica under restart + Redis reconnect | integration (2 replicas, Redis backplane) | `dotnet test tests/GameKit.Lobby.Integration.Tests --filter "Category=Replica"` | ❌ W0 (`SignalRReplicaTests`) | ⬜ pending |
| 16-06-b | 06 | 1 | SCALE-06 (Admin) | T-16-06-03 | An admin event published to `gamekit:admin:events` from a restarted replica reaches a cookie-authed admin client on the other replica under restart + Redis reconnect | integration (2 AdminTestHost replicas, gamekit:admin:events relay) | `dotnet test tests/GameKit.Admin.Integration.Tests --filter "Category=Replica"` | ❌ W0 (`AdminSignalRReplicaTests`) | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerSplitBrainTests.cs` — new (SCALE-04); two-app + shared PG + `LockTtlSeconds=2`, lease expiry via `IChaosInterceptor.BeforeLuaClaim`
- [ ] Graceful-drain integration test (SCALE-05) — 100 concurrent requests + SIGTERM harness
- [ ] `tests/GameKit.Core.Integration.Tests` idempotency test (SCALE-03) — concurrent `SessionCompleteAsync`
- [ ] `tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs` — Lobby replica-restart + Redis reconnect (SCALE-06, Lobby half)
- [ ] `tests/GameKit.Admin.Integration.Tests/AdminSignalRReplicaTests.cs` — Admin replica-restart + Redis reconnect via the gamekit:admin:events relay (SCALE-06, Admin half); reuses existing `AdminTestHost` + `Mocks.CookieInjectingHandler` (no new project)
- [ ] Core migration adding `game_sessions.IdempotencyKey varchar(128) NULL` + partial unique index

*Existing infrastructure (Testcontainers PG+Redis fixtures, `LobbyTestApp`, `RedisFixture`, `IChaosInterceptor`) covers the harness; new test files above are the Wave 0 additions.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| (none) | — | All Phase 16 invariants are expressible as automated Testcontainers tests | — |

*All phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 240s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
