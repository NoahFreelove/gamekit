# Phase 16: Multi-Replica Hardening - Context

**Gathered:** 2026-06-22
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Multi-replica deployments are proven correct under leader churn, SIGTERM, and concurrent request storms — duplicate matches are impossible, graceful drain is zero-downtime, and a CI gate enforces these invariants before load tests run.

**Requirements:** SCALE-01, SCALE-02, SCALE-03, SCALE-04, SCALE-05, SCALE-06
**Depends on:** Phase 14 (Health & Readiness), Phase 15 (Per-Package OTel Instrumentation)
**UI hint:** no — backend/ops hardening phase. (The ui-plan-gate false-positives on the SignalR mention in criterion #5; treat as non-frontend and plan with `--skip-ui`.)

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — discuss phase was skipped per user setting (autonomous run). Use the ROADMAP phase goal, success criteria below, and existing codebase conventions to guide decisions.

### Success Criteria (what must be TRUE)
1. `ILeaderLease` in `GameKit.Core` is the single interface all three lease helpers implement; a grep of `src/` shows no `LockTakeAsync` call outside a class that implements `ILeaderLease`.
2. A two-replica Testcontainers integration test (`MatchmakerSplitBrainTests`) simulates lease expiry mid-tick and asserts zero duplicate rows in `game_sessions` and no ticker gap longer than one lock TTL — required CI gate.
3. A graceful-drain integration test sends 100 concurrent in-flight requests, triggers SIGTERM, asserts zero 5xx responses and zero duplicate matches; `ReleaseLeaseAsync` is verified to use `CancellationToken.None` (not the stopping token) on all finally paths.
4. Concurrent `SessionCompleteAsync` calls for the same idempotency key produce exactly one `game_sessions` row (`INSERT … ON CONFLICT DO NOTHING` proven by a dedicated Testcontainers test).
5. A SignalR multi-replica integration test with real Testcontainers Redis backplane confirms all connected lobby clients receive hub events regardless of which replica sends them under replica restart and Redis reconnect.

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research. Key existing seams to consolidate behind `ILeaderLease`: the Matchmaking ticker lease (`IMatchmakerLease` / `LockTakeAsync` from Phase 14), and any Admin / background-job leader locks. Idempotency for `SessionCompleteAsync` should build on the existing `game_sessions` write path. SignalR backplane is the Phase 11 GameKit.Lobby Redis backplane.

</code_context>

<specifics>
## Specific Ideas

No discuss-phase specifics — refer to ROADMAP phase description and success criteria. Honor known constraints: build affected packages with `-p:NuGetAudit=false` (pre-existing MessagePack NU1903 advisory), and ignore the stale Core.Integration `Migrate_Twice_Is_Idempotent` failure in full-suite gates.

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
