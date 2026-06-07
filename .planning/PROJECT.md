# GameKit

## What This Is

GameKit is a self-hostable, GPL-licensed open-source .NET library that gives game developers auth, player management, matchmaking, rankings, presence, and session tracking as composable ASP.NET Core modules. It is **not** a standalone server — it is a set of NuGet packages a game developer integrates into their own ASP.NET Core app to produce a complete, self-hosted backend running on hardware they control (Postgres + Redis only).

## Core Value

A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.

## Requirements

### Validated

<!-- Shipped and confirmed in v1.0 (2026-05-30). 92/92 requirements. Detail archived. -->

- ✓ **v2.0 — Expansion: Providers, Lobby & Rating-Aware Play** (2026-06-07) — Core rating seam; Argon2 + Google/Apple/Epic OAuth sibling packages; rating-aware/regional/backfill matchmaking; rank decay + placement; account merge; `GameKit.Lobby` (SignalR+Redis); multi-replica Admin + MinVer release-train close-out. 29/29 requirements. Detail: `milestones/v2.0-REQUIREMENTS.md`.

<!-- v2.0 Active features are now Validated (above). Active is empty until /gsd:new-milestone. -->

> The per-feature v2.0 "Active" checklist below is superseded by the line above; full traceability is archived in `milestones/v2.0-REQUIREMENTS.md`.

- ✓ **Foundation / Core** (CORE-01..17, OPS-01..03/06..10, DIST-01) — `GameKit.Core`, owned `GameKitDbContext`, per-package migrations, GDPR delete, rate-limit helpers, GPL+egress guards — v1.0 Phase 1
- ✓ **Authentication** (AUTH-01..16) — `GameKit.Auth`, JWT issuance, refresh-token rotation, BCrypt hasher, in-house Steam OpenID + Discord OAuth, guest upgrade, identity linking — v1.0 Phase 2
- ✓ **Admin UI** (ADMIN-01..12) — `GameKit.Admin.UI` Blazor Server (MudBlazor), cookie auth, CSP/antiforgery, player CRUD/ban, audit, health panel — v1.0 Phases 3 + 3.1
- ✓ **Rankings + Sessions + GDPR** (RANK-01..14) — `GameKit.Rankings`, vendored Glicko-2, batched `IRankingAlgorithm`, idempotent session-complete, seasonal reset, rank-adjust audit — v1.0 Phase 4
- ✓ **Matchmaking + Parties** (MATCH-01..15) — `GameKit.Matchmaking`, crash-safe Redis tickets, leader-elected ticker, reconciliation, EloRange strategy (rating-blind in v1), party tickets — v1.0 Phase 5
- ✓ **Presence + OpenAPI + Distribution** (PRES-01..06, OPEN-01, DIST-02..06, OPS-04..05) — `GameKit.Presence`, per-package OpenAPI docs, coordinated MinVer release train, `dotnet new gamekit` template, CLI, ops guide — v1.0 Phase 6

> Full v1.0 requirement text: `.planning/milestones/v1.0-REQUIREMENTS.md`. Audit: `.planning/v1.0-MILESTONE-AUDIT.md`.

### Active

<!-- Current scope: v2.0 — Expansion: Providers, Lobby & Rating-Aware Play. REQ-IDs assigned in REQUIREMENTS.md. -->

**Auth expansion**
- [ ] Argon2 password hasher as opt-in sibling package `GameKit.Auth.Argon2` (Isopoh)
- [ ] Additional OAuth providers (Google / Apple / Epic) as opt-in sibling packages
- [ ] Account merge — combine two distinct `player_id`s into one (reverses a v1 Out-of-Scope call; treat as high-risk)

**Rankings / Matchmaking depth**
- [ ] Rating-aware matchmaking — wire Rankings → Matchmaking so EloRange uses real player ratings (v1.0 carried-forward tech debt; EloRange currently runs on rating=0)
- [ ] Configurable rank decay for inactive top-tier players
- [ ] Placement matches (initial high-RD games)
- [ ] Backfill into in-progress sessions
- [ ] Regional matchmaking pools as a first-class concept (reverses a v1 Out-of-Scope call; was `metadata.region` escape hatch)

**Lobby**
- [ ] New `GameKit.Lobby` package — ready-checks, in-lobby chat, persistent groups

**Admin**
- [ ] Multi-replica Admin UI via SignalR + **Redis** backplane (no Azure SignalR)
- [ ] Replace the dead Admin "Rank adjust" stub nav page with the working flow (v1.0 carried-forward tech debt)

### Out of Scope

<!-- Explicit boundaries carried from v1.0. -->

- **AI / LLM integrations of any kind** — GPL self-hosted commitment; no AI moderation, matchmaking, content gen, or telemetry analysis
- **Cloud-only / SaaS dependencies** — library must run air-gapped; SignalR backplane is Redis, not a managed service
- **Telemetry / phone-home** — library never collects or transmits usage data
- **Game server hosting / orchestration, netcode, voice chat** — use Agones/Multiplay, Mirror/Fish-Net, Vivox, etc.
- **Inventory / economy / progression, analytics pipeline, billing/entitlements** — operator brings their own; FK into `gamekit.players(id)`
- **MySQL / SQL Server providers** — Postgres-only; provider abstraction may open this in a later milestone
- **Friends graph (`GameKit.Social`)** — deferred to a later v1.x/v2.x sibling package (not in v2.0)
- **DDoS mitigation, anti-cheat, parental controls** — network/engine/operator concerns

## Context

- **Mature v1.0 codebase**: ~34.3k LOC source + ~29.6k LOC tests across 18 projects; 7 shipped NuGet packages + CLI + template + build-time version-stamp generator. v2 extends this same codebase and release train.
- **Established patterns** the v2 build must follow: per-package migrations (distinct advisory-lock key + `__ef_migrations_<pkg>` history table + design-time factory + `ExcludeFromMigrations` for prior packages; never mutate Core tables); coordinated MinVer release train with exact-pinned sibling refs `[X.Y.Z]`; `BackgroundService` + `PeriodicTimer` + Polly for periodic jobs (Redis leader election via `SET NX PX`); Scrutor assembly scanning for pluggable strategies; XML docs on every public API (CS1591-as-error).
- **Two v1 Out-of-Scope reversals** in this milestone (account merge; first-class regional pools) — research/roadmap should treat these as the riskiest items and surface a clear migration/data-model story.
- **Sample app** `samples/TicTacToeDuel` is the composition-root reference and integration harness.
- **Build/run posture for v2**: user wants the build to run **fully autonomously** (`gsd-autonomous`) with automated verification (xUnit + Testcontainers + the GSD verifier/nyquist gates) rather than conversational UAT.

## Constraints

- **License**: GPL — fully open-source, no proprietary deps, no telemetry, no phone-home
- **Self-hosted only**: zero cloud-service dependencies; complete backend stands up with this library + Postgres + Redis on operator hardware
- **Runtime**: .NET 10 LTS; ASP.NET Core 10; EF Core 10 + Npgsql (Postgres only); Redis via StackExchange.Redis
- **Distribution**: every `/src` project ships as its own NuGet package on the coordinated release train (all packages share one version; sibling refs exact-pinned)
- **Migration boundaries**: packages never modify Core tables — only add tables or FK references
- **Public API discipline**: XML doc comments on every public API (CS1591 enforced as error)
- **Security invariants**: refresh tokens stored SHA-256-hashed (raw issued once); JSONB `metadata` is sparse/non-relational only
- **Testing**: xUnit + Testcontainers (real Postgres + Redis) + Moq — no skip-if-no-docker fallbacks

## Key Decisions

<!-- Project-level decisions that constrain future work. v1.0 decisions carried; v2 decisions added as made. -->

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| In-house Glicko-2 (vendored, BSD-3-Clause from MaartenStaa) over a NuGet dep | ~150 LOC, no maintained library exists | ✓ Good (v1.0) |
| `BackgroundService` + `PeriodicTimer` + Polly over Hangfire/Quartz | Library can't impose DB tables/dashboard on consumers | ✓ Good (v1.0) |
| MinVer coordinated release train, exact-pinned siblings | One source of truth; simplest composable-package story | ✓ Good (v1.0) |
| Reject MediatR / AutoMapper (RPL/commercial after v13) | License risk inside consumers' apps | ✓ Good (v1.0) |
| Scrutor + MS.DI over source-gen DI | A library cannot dictate the consumer's container | ✓ Good (v1.0) |
| v2 SignalR backplane MUST be Redis (not Azure SignalR) | Zero-cloud GPL constraint | — Pending (v2.0) |
| Account merge + first-class regional pools enter scope (reversing v1) | User-prioritized for v2.0 | — Pending (v2.0, high-risk) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

## Shipped: v2.0 — Expansion: Providers, Lobby & Rating-Aware Play (2026-06-07)

**Goal:** Deepen GameKit from a complete v1 backend into a richer platform — more auth options, matchmaking that actually uses skill ratings, real lobbies, and an Admin UI that scales horizontally — all still GPL, self-hosted, zero-cloud.

**Target features:**
- Auth: Argon2 hasher · Google/Apple/Epic OAuth · account merge
- Rankings/Matchmaking: rating-aware EloRange · rank decay · placement matches · backfill · first-class regional pools
- Lobby: new `GameKit.Lobby` package (ready-checks, in-lobby chat, persistent groups)
- Admin: multi-replica UI (SignalR + Redis backplane) · fix "Rank adjust" stub page

---
*Last updated: 2026-06-07 — v2.0 shipped (Phases 7–12, 29/29 requirements, audit `tech_debt` with minor non-blocking items). Next: `/gsd:new-milestone`.*
