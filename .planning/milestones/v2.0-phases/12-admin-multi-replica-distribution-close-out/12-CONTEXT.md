# Phase 12: Admin Multi-Replica + Distribution Close-Out - Context

**Gathered:** 2026-06-07
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

The Admin UI is correct across multiple replicas (Redis-backed error counter, SignalR backplane, Data Protection key sharing documented), the dead Rank-adjust stub is fixed, and all five new packages join the coordinated MinVer release train.

**Depends on:** Phase 11 (the `AdminEventHub` reuses the SignalR + Redis backplane pattern proven in Lobby; Phase 8 must be complete so the wired `RankAdjustService` has the finalized Rankings schema it writes to).

**Requirements:** ADMIN-13, ADMIN-14, ADMIN-15, DIST-07

**Success Criteria (what must be TRUE):**
1. The health panel "recent error rate" tile shows the aggregate error count across all replicas: an error logged on replica A increments the count visible on replica B — verified by writing to `RedisErrorRateCounter` in one test context and asserting from another.
2. An `AdminEventHub` SignalR message published via Redis Pub/Sub channel `"gamekit:admin:events"` reaches all connected admin sessions regardless of which replica they are connected to; the `AdminLiveBroadcastService` `BackgroundService` is responsible for the relay.
3. A developer navigating to `/admin/rankings/adjust` reaches a functional rank-adjustment UI that calls the existing `IRankAdjustService` and produces an `admin_audit_log` row — the dead stub page is replaced.
4. All five new packages (`GameKit.Auth.Argon2`, `GameKit.Auth.Google`, `GameKit.Auth.Apple`, `GameKit.Auth.Epic`, `GameKit.Lobby`) are present in the MinVer release train: they share the same version as all other GameKit packages, carry exact-pinned `[X.Y.Z]` sibling refs, and are covered by the `GameKitVersionAssertionHostedService` mismatch check.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — discuss phase was skipped per user setting. Use ROADMAP phase goal, success criteria, and codebase conventions.

### UI design (SC#3 rank-adjust page)
The rank-adjust page is a SINGLE form added to the EXISTING, already-designed Admin UI (Phase 3 + the Phase 3.1 redesign: MudBlazor, violet-600 accent, density tokens, master-detail/dialog patterns). NO new design system is needed — the page MUST follow the existing admin page/dialog patterns (see `src/GameKit.Admin.UI/Components/Pages/*.razor` + `Components/Shared/*` such as PlayerDetailPane, the ban dialog, StatusChip, HealthTileView). A fresh UI-SPEC design contract is therefore not warranted; the pattern-mapper maps the rank-adjust page to those existing analogs and the gsd-ui-review audit runs after execution as an advisory visual check.

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research. Key surfaces: the existing in-memory error-rate counter from Phase 3 (ErrorRateRingBuffer + LogErrorCounter ILoggerProvider + HealthProbeService thresholds) — SC#1 replaces/augments it with a Redis-backed `RedisErrorRateCounter` aggregating across replicas; the SignalR + Redis backplane pattern just proven in Phase 11 GameKit.Lobby (LobbyRedisBackplanePostConfigure reusing IConnectionMultiplexer) — SC#2 reuses it for `AdminEventHub` + `AdminLiveBroadcastService` over Redis Pub/Sub channel `gamekit:admin:events`; the dead Rank-adjust stub nav page in GameKit.Admin.UI + the existing `IRankAdjustService` + `IAdminAuditWriter` (SC#3); `GameKitVersionAssertionHostedService` + the MinVer version-stamp generator (GameKit.Build/GameKitVersionGenerator) for SC#4; the coordinated MinVer release train + exact-pinned sibling refs `[X.Y.Z]`.

</code_context>

<specifics>
## Specific Ideas

No specific requirements beyond the success criteria — discuss phase skipped. SignalR backplane is Redis (NOT Azure SignalR) — zero-cloud GPL constraint. Data Protection key sharing across replicas is a DOCUMENTATION deliverable (ops guide), not necessarily a code feature. The 5 new packages already exist as packable projects (Auth.Argon2 from Phase 7, Auth.Google/Apple/Epic from Phase 7, Lobby from Phase 11) — SC#4 is about ensuring each is on the MinVer train (same version, exact-pinned sibling refs, covered by GameKitVersionAssertionHostedService).

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
