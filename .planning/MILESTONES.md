# MILESTONES: GameKit

## v2.1 Operability & Hardening (Shipped: 2026-07-13)

**Phases completed:** 9 phases (13–21), 49 plans, 50 tasks
**Timeline:** 2026-06-14 → 2026-07-12 (28 days) · 1,263 files changed, ~155k insertions
**Closeout:** verified_closeout — all 9 phases verified, milestone audit passed 47/47 requirements, 8/8 integration points, 3/3 E2E flows

**Delivered:** Production operability for every GameKit package — full OpenTelemetry instrumentation, K8s-grade health probes, multi-replica correctness, backup/DR tooling, a sealed security audit, load-test evidence, complete docs, and a 3D multiplayer demo proving the whole stack end-to-end.

**Key accomplishments:**

- Full observability: ActivitySource/Meter instrumentation across Matchmaking, Rankings, Lobby + `AddGameKitObservability()` builder, W3C trace propagation through Redis tickets, and a self-hosted OTel Collector + Prometheus + Grafana + Tempo compose stack with provisioned dashboards (Phases 13, 15)
- K8s probe contract: tag-based live/ready health checks with per-package migration-readiness reporters, leader-lease Degraded reporting, and integration tests proving liveness-under-Postgres-down and readiness gating (Phase 14)
- Multi-replica hardening: unified `ILeaderLease` in Core, shutdown-surviving lease release (`CancellationToken.None` + CI grep gate), idempotent session completion, split-brain and graceful-drain integration tests, SignalR Redis-backplane correctness under replica restart (Phase 16)
- Backup/DR + migration ops: `gamekit migrations list` / `apply --dry-run`, `gamekit db backup/restore`, Postgres/Redis runbooks, destructive `Down()` migrations sealed, and a CI-verified DR round-trip (backup → drop → restore → health green) (Phase 17)
- Security audit sealed: NuGetAudit gates ON (mode=all, level=high) with MessagePack/Scriban/OpenApi transitive pins, GDPR-delete extension surface, JWT/admin/rate-limit hardening tests, and a traceable security checklist (Phase 18)
- Ship-proof: k6 load tests + PERF-06 benchmark regression gate in CI (Phase 19); 12 concept docs, tutorial with CI smoke gate, v2.0→v2.1 upgrade guide, 10 ADRs (Phase 20); Platformer3D 3D multiplayer demo with custom ranking algorithm + matchmaking strategy, verified via two-player headless-browser e2e (Phase 21, merged e1acdeb)

---

## v2.0 Expansion: Providers, Lobby & Rating-Aware Play (Shipped: 2026-06-07)

**Phases completed:** 6 phases, 26 plans, 48 tasks

**Key accomplishments:**

- One-liner:
- 1. [Rule 1 - Bug] NU1109 diamond-dependency: IdentityModel pins needed upgrading
- 1. [Rule 3 - Blocking] BannedCheckHelper inaccessible from sibling assembly
- Sign-In-with-Apple sibling package with per-exchange ES256 client secret (GenerateClientSecret=true), Apple sub-as-external_id, first-login-only relay email+name to PlayerIdentity.Metadata JSONB, and conditional scheme registration
- `EpicOAuthOptions : OAuthOptions`
- BCrypt→Argon2id transparent rehash wired in PasswordOAuthProvider.CompleteLoginAsync, proven end-to-end with Testcontainers Postgres; password_hash column extended to varchar(512) for Argon2 hash storage
- player_ranks schema frozen with decay/placement columns via raw-SQL migration, GameKitRankingsDecayOptions added as full phase-8 options surface, visible-rank hiding wired in LeaderboardRowDto, and Glickman inactivity formula proven by scale-correct unit test
- Scale-correct Glicko-2 RD inflation for inactive above-threshold players via a dedicated Redis lease key, with Testcontainers proof of leader election and non-collision with the ticker service
- Test service resolution: PendingRatingUpdatesAdapter registered as concrete type for direct test access
- One-liner:
- One-liner:
- MATCH-18 regional pool routing: RegionName HTTP field with FluentValidation guard, AllowedRegions membership check in MatchmakingService, GetPoolNamesForLadder ticker loop, all 4 RegionalPoolTests green
- One-liner:
- One-liner:
- `20260606000000_AddMergedIntoPlayerId`
- `AccountMerge.cs`
- 1. [Rule 2 - Missing critical functionality] Cross-package project reference would cause circular dependency
- Superadmin POST /admin/api/players/merge endpoint with antiforgery + rate-limiting, and 19-test Testcontainers suite (SC#1–#5) all green against real Postgres + Redis
- `LobbyMigrationConstants.AdvisoryLockKey = 12178347L`
- lobbies + lobby_members EF data model, 20-entity LobbyMigrationModelCustomizer, advisory-lock-serialized 20260522000000_LobbyInitial migration, and schema tests confirming tables exist with zero lobby_message% tables (LOBBY-04 anti-feature enforced at DB level)
- [Authorize] LobbyHub on Redis backplane (ChannelPrefix "GameKit") with chained JWT WebSocket query-string token extraction (SC#2), SERIALIZABLE all-ready MarkReadyAsync with Polly retry + post-commit IHubContext broadcast (LOBBY-03), relay-only chat seam enforcing LOBBY-04 at both interface and runtime level, and full REST endpoint surface.
- Real IPartyService.CreateAsync + JoinAsync + IMatchmakingService.EnqueueAsync(partyId) wiring in LobbyService, two-TestServer SignalR harness (LobbyTestApp + LobbyTestModelCustomizer), and all four success-criteria integration tests (SC#2/3/4/5) — all 11 Lobby integration tests green.
- One-liner:
- Dead /admin/rankings/adjust stub replaced with player-search + IDialogService.ShowAsync<RankAdjustDialog> flow; SC#3 integration test proves AdjustAsync writes admin_audit_log row (action 'admin.player.rank_adjust') against real Postgres
- Additive Redis-backed INCRBY error counter (gamekit:admin:errors:{epoch_bucket}) that aggregates across replicas so the health panel shows the true fleet-wide error rate, proven by a two-host Testcontainers SC#1 test.
- JWT-secure admin live-event hub on a Redis backplane: messages published to `"gamekit:admin:events"` reach all connected admin sessions regardless of which replica they hit; the hub is gated by the `GameKitAdmin` cookie scheme (player JWT refused), proven by three Testcontainers integration tests.

---

A running index of shipped milestones. Newest first.

| Version | Name | Shipped | Duration | Requirements | Archive |
|---------|------|---------|----------|--------------|---------|
| v1.0 | Initial 6-Phase Build-Out | 2026-05-30 | ~6 weeks (2026-04-15 → 2026-05-26) | 92/92 | [roadmap](milestones/v1.0-ROADMAP.md) · [requirements](milestones/v1.0-REQUIREMENTS.md) · [audit](v1.0-MILESTONE-AUDIT.md) |

## v1.0 — Initial 6-Phase Build-Out

Shipped GameKit as 7 composable GPL NuGet packages on .NET 10 (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi) plus a CLI, a build-time version-stamp source generator, and a `dotnet new gamekit` template. Self-hosted on Postgres + Redis only; every algorithm a DI-swappable interface; no cloud, no telemetry.

- **Phases:** 7 · **Plans:** 60 · **Commits:** 152 · **Source:** ~34.3k LOC · **Tests:** ~29.6k LOC (18 projects)
- **Status:** ✅ Complete — audit `tech_debt` (no blockers; 2 documented integration warnings carried to v1.x)
