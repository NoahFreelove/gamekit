---
phase: 20-docs-tutorial
plan: "04"
subsystem: docs/concepts
tags: [docs, concepts, interfaces, DOCS-03]
dependency_graph:
  requires: [20-02]
  provides: [docs/concepts/*.md (12 files), DOCS-03]
  affects: []
tech_stack:
  added: []
  patterns:
    - Per-package concepts markdown (what-it-does / interfaces / responsibility-line / wire-up)
    - Interface-citation verification against src/ (grep public interface before cite)
key_files:
  created:
    - docs/concepts/index.md
    - docs/concepts/core.md
    - docs/concepts/auth.md
    - docs/concepts/auth-argon2.md
    - docs/concepts/auth-providers.md
    - docs/concepts/rankings.md
    - docs/concepts/matchmaking.md
    - docs/concepts/presence.md
    - docs/concepts/lobby.md
    - docs/concepts/admin-ui.md
    - docs/concepts/openapi.md
    - docs/concepts/cli.md
  modified: []
decisions:
  - "Presence read-side (IPresenceProvider) documented in core.md not presence.md — it lives in GameKit.Core"
  - "IMatchmakerLease documented in matchmaking.md as an internal lease (not consumer-implemented)"
  - "openapi.md notes AddGameKitOpenApi is IServiceCollection extension (not IGameKitBuilder chain)"
  - "admin-ui.md explicitly documents Auth coupling as designed v1 constraint and HLTH-06 delegation"
metrics:
  duration: "~25 min"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 12
  files_modified: 0
status: complete
---

# Phase 20 Plan 04: Per-Package Concepts Documentation (DOCS-03) Summary

**One-liner:** 12 concepts docs covering every shipped GameKit package — what it does, its
replaceable-interface seams, and the library-vs-consumer responsibility line; all interface
citations verified against `src/` with no invented names.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Core, Auth, Auth.Argon2, auth-providers docs | 8315110 | core.md, auth.md, auth-argon2.md, auth-providers.md |
| 2 | Rankings, Matchmaking, Presence, Lobby docs | 4265038 | rankings.md, matchmaking.md, presence.md, lobby.md |
| 3 | Admin.UI, OpenApi, Cli docs + index | dc7b07d | admin-ui.md, openapi.md, cli.md, index.md |

## Verification Results

### Docs Presence Check (12/12)
All 12 files in `docs/concepts/` exist with substantive content:
`core.md`, `auth.md`, `auth-argon2.md`, `auth-providers.md`, `rankings.md`, `matchmaking.md`,
`presence.md`, `lobby.md`, `admin-ui.md`, `openapi.md`, `cli.md`, `index.md`.

### Interface Citation Accuracy (45/45 OK)
Every interface cited in the docs was verified against `grep -rq "public interface IName" src/<pkg>/`.
No invented interface names. Full list verified:

**GameKit.Core (9):** `IGameKitBuilder`, `ISessionLifecycleObserver`, `IPostSessionCompleteHandler`,
`IGdprDeleteExtension`, `IPlayerRatingProvider`, `IPlayerDisplayNameResolver`,
`IGameKitRateLimitPolicies`, `IModelBuilderExtension`, `ILeaderLease`

**GameKit.Auth (10):** `IOAuthProvider`, `IPasswordHasher`, `IJwtIssuer`, `IRefreshTokenService`,
`IIdentityLinker`, `IAccountMergeService`, `IAuthAuditWriter`, `IGuestUpgradeService`,
`IExternalIdHasher`, `IIsGuestResolver`

**GameKit.Rankings (7):** `IRankingAlgorithm`, `ILeaderboardService`, `IRankAdjustService`,
`IEndSeasonService`, `IServiceTokenService`, `IGdprExportService`, `IGameKitRankingsBuilder`

**GameKit.Matchmaking (8):** `IMatchmakingStrategy`, `IMatchmakerTicker`, `IProposalService`,
`IBackfillService`, `IMatchmakingControlService`, `IPartyCodeGenerator`, `IPartyService`,
`IGameKitMatchmakingBuilder`

**GameKit.Presence (1):** `IPresenceWriter`

**GameKit.Lobby (3):** `ILobbyService`, `ILobbyMessageHandler`, `ILobbyClient`

**GameKit.Admin.UI (7):** `IAdminAuthService`, `IPlayerBanService`, `IAdminUserService`,
`IPlayerSearchService`, `IHealthProbeService`, `IAdminAuditWriter`, `IRedisErrorRateCounter`

### Task Verifications (all PASS)
- Task 1: `for f in core auth auth-argon2 auth-providers; do test -f docs/concepts/$f.md; done && grep -q 'IGameKitBuilder' ... && grep -q 'IOAuthProvider' ... && grep -q 'IPasswordHasher' ... && echo PASS` → **PASS**
- Task 2: `for f in rankings matchmaking presence lobby; do ... done && grep -q 'IRankingAlgorithm' ... && grep -q 'IMatchmakingStrategy' ... && grep -q 'IPresenceWriter' ... && grep -q 'ILobbyService' ... && echo PASS` → **PASS**
- Task 3: `for f in admin-ui openapi cli index; do ... done && grep -q 'IAdminAuthService' ... && grep -q 'core.md' ... && grep -q 'matchmaking.md' ... && echo PASS` → **PASS**

## Key Content Decisions

### core.md
Documents that observability (`AddGameKitObservability`) and health checks (`AddGameKitHealthChecks`
+ `MapGameKitHealth`) are **Core extension methods — not separate packages**. This matches the
plan prohibition against inventing a `GameKit.Observability` package.

### auth-providers.md
Explicitly documents the **explicit registration requirement**: Scrutor scans only the
`GameKit.Auth` assembly, so Apple/Epic/Google providers must be wired via `.AddApple()` /
`.AddEpic()` / `.AddGoogle()` after `AddAuth(...)`. This is the #1 gotcha for consumers
adding sibling providers.

### matchmaking.md
Documents the **pool-name routing rule**: `PoolName = null` routes to the `"default"` pool.
Two tickets only match if they share ladder AND pool name — this is the root cause of the
`matchmaking.html` poolName bug found in RESEARCH (DOCS-06).

### presence.md
Notes that `IPresenceProvider` (read-side) lives in `GameKit.Core`, not `GameKit.Presence`,
to keep the read-side decoupled from the Redis write path.

### admin-ui.md
Documents the Auth coupling (v1 hard requirement, not a bug) and the HLTH-06 delegation:
`IHealthProbeService` delegates to Core's health checks so the admin panel and `/health/ready`
are always consistent.

### openapi.md
Notes that `AddGameKitOpenApi` is an `IServiceCollection` extension (not `IGameKitBuilder`
chain) and documents the `DocumentName` collision hazard for consumers who also call
`AddOpenApi("v1", ...)`.

### index.md
Opens with the "every algorithm is a replaceable interface" framing from CLAUDE.md and
includes a summary table of all 12 key replaceable interfaces with their package, default
implementation, and "replace when" guidance.

## Deviations from Plan

None — plan executed exactly as written. All 12 files delivered. No invented interfaces.
No GameKit.Observability package referenced.

## Known Stubs

None — all docs reference real APIs. No placeholder content.

## Threat Flags

None — concepts docs describe the replaceable-seam surface conceptually; no new endpoints,
auth paths, or schema changes were introduced.

## Self-Check: PASSED

All 12 `docs/concepts/*.md` files confirmed present on disk.
All 45 interface citations confirmed against `src/` (grep verified, zero failures).
Commits 8315110, 4265038, dc7b07d confirmed in git log.
