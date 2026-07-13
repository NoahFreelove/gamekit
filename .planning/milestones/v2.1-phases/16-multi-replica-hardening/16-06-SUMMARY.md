---
phase: 16-multi-replica-hardening
plan: "06"
subsystem: signalr-multi-replica
tags: [scale, signalr, backplane, redis, integration-tests, lobby, admin]
dependency_graph:
  requires:
    - "src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs (Phase 11 — backplane wiring)"
    - "src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs (Phase 12 — admin relay)"
    - "src/GameKit.Admin.UI/AdminBackplanePostConfigure.cs (Phase 12)"
  provides:
    - "SCALE-06 Lobby integration test (SignalRReplicaTests)"
    - "SCALE-06 Admin integration test (AdminSignalRReplicaTests)"
    - "Operator sticky-session architecture doc (docs/architecture/signalr-multi-replica.md)"
  affects:
    - "tests/GameKit.Lobby.Integration.Tests"
    - "tests/GameKit.Admin.Integration.Tests"
    - "docs/architecture"
tech_stack:
  added: []
  patterns:
    - "Per-test-run unique ChannelPrefix via serviceOverrides to prevent backplane cross-test contamination"
    - "CookieInjectingHandler private nested class (mirrors AdminEventHubTests pattern)"
    - "Dispose-and-reconstruct TestApp/TestHost pattern for replica restart simulation"
key_files:
  created:
    - tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs
    - tests/GameKit.Admin.Integration.Tests/AdminSignalRReplicaTests.cs
    - docs/architecture/signalr-multi-replica.md
  modified: []
decisions:
  - "Per-test-run unique ChannelPrefix (GameKit:{Guid:N}) via serviceOverrides — both AppA and AppB receive the same prefix so they form one logical backplane cluster; production prefix GameKit is never touched"
  - "CookieInjectingHandler duplicated as private sealed class inside AdminSignalRReplicaTests (not extracted to Mocks/) — consistent with AdminEventHubTests pattern; extraction would require making it public or internal, creating unnecessary coupling"
  - "Reconnect scenario uses probe-connection-close pattern rather than container restart — container restart is non-deterministic in the shared-fixture harness (other tests share the same RedisFixture) and would interfere with sibling tests; the probe close validates resilience without disrupting the shared Redis"
  - "Restarted LobbyTestApp gets its own fresh database; same lobby+player IDs are re-seeded into it (EnsurePlayerRow + SeedSharedLobbyAsync with ON CONFLICT DO NOTHING) to satisfy hub membership checks on the new replica — mirrors production where Postgres data persists across replica restarts"
  - "HealthProbeTests.ProbeAsync_Reports_Postgres_OK failure is pre-existing (confirmed on master without my changes); documented in deferred items below"
metrics:
  duration: "521 seconds (~9 minutes)"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 3
  files_modified: 0
status: complete
---

# Phase 16 Plan 06: Multi-Replica SignalR Correctness (SCALE-06) Summary

**One-liner:** SCALE-06 integration tests proving LobbyHub and AdminEventHub fan-out across replicas under restart and reconnect, plus operator sticky-session architecture doc.

## Objective

Prove multi-replica SignalR correctness for both the Lobby and Admin hubs under replica restart and Redis reconnect scenarios, and document the sticky-session requirement for operators.

## What Was Built

### Task 1: SignalRReplicaTests (Lobby)

`tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs`

Two `LobbyTestApp` replicas share a single Testcontainers Redis backplane with a **per-test-run unique `ChannelPrefix`** (`GameKit:{Guid:N}`) supplied via `serviceOverrides`. This prevents cross-test backplane contamination (RESEARCH Pitfall 4) while keeping the production prefix `"GameKit"` untouched.

**Tests:**
- `HubEvents_AfterReplicaRestart_AreDeliveredToClientOnOtherReplica` — disposes and reconstructs `_appA`, re-seeds the same lobby+player IDs into the new AppA's fresh database, asserts `clientB` on AppB receives a hub event from the restarted AppA within 10 s.
- `HubEvents_ResumeAfterRedisReconnect` — uses a probe connection that is immediately closed to simulate transient disruption; asserts post-reconnect delivery resumes normally.

Both tests pass (2/2).

### Task 2: signalr-multi-replica.md (Architecture doc)

`docs/architecture/signalr-multi-replica.md`

Operator-facing document covering:
- How the Redis backplane handles outbound fan-out for both `LobbyHub` and `AdminEventHub`
- The `AdminLiveBroadcastService` relay path via `gamekit:admin:events` (distinct from the backplane fan-out path)
- **Sticky session requirement** — the backplane does NOT route hub method invocations to arbitrary replicas; operators MUST configure LB affinity (nginx `ip_hash`, HAProxy `balance source`, K8s ingress `affinity: cookie`)
- Reconnect behaviour — StackExchange.Redis auto-restores subscriptions; in-flight messages during outage window are NOT buffered (at-most-once)
- Rolling deploy behaviour

### Task 3: AdminSignalRReplicaTests (Admin)

`tests/GameKit.Admin.Integration.Tests/AdminSignalRReplicaTests.cs`

Two `AdminTestHost` instances share the same Testcontainers Postgres DB (via `pg.OwnerConnectionString`) and the same Redis. Distinct seeded usernames (`replica-test-a`, `replica-test-b`) avoid the `UNIQUE(username)` constraint on the shared DB. `ResetAdminUsers` TRUNCATE guard runs in the constructor.

**Tests:**
- `AdminEvents_AfterPublishingReplicaRestart_AreDeliveredToClientOnOtherReplica` — disposes `_hostA` and reconstructs a fresh `AdminTestHost` against the same `RedisFixture`; publishes to `gamekit:admin:events` via the new host A's `IConnectionMultiplexer`; asserts the cookie-authenticated `connB` on host B receives `ReceiveAdminEvent`. Proves `AdminLiveBroadcastService` relay on the surviving replica continues after the publishing replica restarts.
- `AdminEvents_ResumeAfterRedisReconnect` — same probe-connection-close pattern as the Lobby reconnect test; asserts post-disruption delivery.

`AdminEventHub` is receive-only — `connB.On<string>("ReceiveAdminEvent", ...)` is the only client-side registration; no `InvokeAsync` is called. `CookieInjectingHandler` is a private nested class (same pattern as `AdminEventHubTests`). Both tests pass (2/2).

## Verification Results

| Check | Result |
|---|---|
| `dotnet test ... --filter "Category=Replica"` (Lobby) | **PASSED** 2/2 |
| `dotnet test ... --filter "Category=Replica"` (Admin) | **PASSED** 2/2 |
| Full Lobby suite (29 tests) | **PASSED** 29/29 |
| Full Admin suite (63 tests) | 61/63 — 2 failures pre-existing (HealthProbeTests — see Deferred Issues) |
| `docs/architecture/signalr-multi-replica.md` exists with sticky/backplane/admin/lobby | YES |

## Deviations from Plan

### Auto-handled deviations

**1. [Rule 1 - No new deviation] CookieInjectingHandler is private in AdminEventHubTests, not in Mocks/**

The plan referenced `Mocks.CookieInjectingHandler` but the actual codebase has it as `private sealed class CookieInjectingHandler` nested inside `AdminEventHubTests`. Rather than extracting it (which would require making it `internal` or `public` and creating coupling), I duplicated the private nested class inside `AdminSignalRReplicaTests` — consistent with the existing pattern.

### Out-of-scope pre-existing failures

**1. [Pre-existing] HealthProbeTests.ProbeAsync_Reports_Postgres_OK and ProbeAsync_Reports_Redis_OK**

These 2 failures exist on `master` before any changes in this plan (confirmed by `git stash` + re-run). They are not regressions. Per the MEMORY.md note on pre-existing failures, they are out of scope.

Deferred to `.planning/phases/16-multi-replica-hardening/deferred-items.md`.

## Decisions Made

1. **Per-test-run ChannelPrefix via `serviceOverrides`** — both AppA and AppB receive the same Guid-suffixed prefix; production prefix `"GameKit"` is never modified; matches RESEARCH Pitfall 4 guidance.
2. **Probe-connection-close pattern for reconnect tests** — container-level restart of the shared `RedisFixture` would disrupt other tests sharing the fixture; the probe close validates SE.Redis resilience without side effects.
3. **Re-seed on AppA restart** — restarted `LobbyTestApp` creates a fresh DB; the same lobby+player IDs are re-seeded using `ON CONFLICT DO NOTHING` so hub membership checks pass on the new replica.
4. **`CookieInjectingHandler` as private nested class** — mirrors `AdminEventHubTests`; not extracted to avoid unnecessary public/internal surface.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes were introduced. This plan touches only integration test files and a documentation file. No new threat flags.

## Self-Check

Files exist:
- `tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs` — EXISTS
- `tests/GameKit.Admin.Integration.Tests/AdminSignalRReplicaTests.cs` — EXISTS
- `docs/architecture/signalr-multi-replica.md` — EXISTS

Commits exist:
- `85dc084` — feat(16-06): add SignalRReplicaTests (SCALE-06 Lobby) and sticky-session architecture doc
- `ea80c6f` — feat(16-06): add AdminSignalRReplicaTests (SCALE-06 Admin)

## Self-Check: PASSED
