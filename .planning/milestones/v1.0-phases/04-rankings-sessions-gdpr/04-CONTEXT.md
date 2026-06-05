---
phase: 04
phase_name: Rankings + Sessions Wiring + GDPR Export
gathered: 2026-05-15
status: Ready for research / planning
---

# Phase 4: Rankings + Sessions Wiring + GDPR Export — Context

**Gathered:** 2026-05-15
**Status:** Ready for research / planning

<domain>
## Phase Boundary

Ships `GameKit.Rankings` as a NuGet package (RANK-01). Adds a swappable
`IRankingAlgorithm.Apply(state, batch)` contract (RANK-04, **batched only** —
per-match calls are explicitly forbidden per Pitfalls #1) with a default
implementation vendored from `MaartenStaa/glicko2-csharp` (RANK-05). Adds the
`ladders` and `player_ranks` tables under a new `__ef_migrations_rankings`
history (RANK-02/03/14), with `rating`, `rating_deviation`, `volatility`
columns stored as `double precision` (NOT `NUMERIC` — Pitfalls #13).

Wires `POST /api/sessions/{id}/complete` in `GameKit.Core` (RANK-11): a
state-conditional, idempotent endpoint that snapshots rating deltas onto
`session_participants` and enqueues the participants for the next batched
rating-period update. Wires `GET /api/players/{id}/export` for GDPR data
portability (RANK-13).

Lazy rank creation on first match (RANK-07); leaderboard queries with
`top-N` and `around-me` (RANK-08); `services.AddLadder("name")` registration
API (RANK-09); seasonal reset + archival (RANK-10); manual admin rank-adjust
that lands the Phase-3 palette verb on a real audited transaction (RANK-12).

**Out of scope (deferred to later phases or v2):**
- Real-time rating push (SignalR / WebSockets) — operators rely on the
  Phase-3 admin panel + polling for visibility in v1.
- Cross-ladder tournament play (sessions reference a single `LadderId`).
- Rating-adjustment APIs for non-admin actors.
- Time-of-day or quest-style ladder modifiers.
</domain>

<canonical_refs>
## Canonical References

**Locked elsewhere — downstream agents MUST read these before planning:**

- `.planning/PROJECT.md` — project-wide invariants (GPL, self-hosted, .NET 10).
- `.planning/REQUIREMENTS.md` — RANK-01..14 verbatim.
- `.planning/ROADMAP.md` — Phase 4 § Success Criteria 1–6.
- `CLAUDE.md` § Technology Stack — Glicko-2 vendoring policy + Polly v8 for
  Redis reconnect + OpenTelemetry opt-in pattern.
- `.planning/phases/01-foundation-core-migrations-ops-defaults-gpl/01-RESEARCH.md`
  — per-package migration pattern (`__ef_migrations_<pkg>`) +
  `IModelBuilderExtension` + `MigrationsHistoryTable` wiring.
- `.planning/phases/02-authentication/02-CONTEXT.md` — JWT scheme +
  `IPasswordHasher` + audit-log pattern for refresh-token rotation.
- `.planning/phases/03-admin-ui/03-CONTEXT.md` — admin cookie scheme,
  superadmin policy, `IAdminAuditWriter` contract (D-09), CSP nonce
  middleware. The Phase-3 admin `rank-adjust` palette verb (currently a
  no-op switch arm in `MainLayout.OpenDialog`) is what RANK-12 lights up.
- `src/GameKit.Core/Entities/GameSession.cs` — already has
  `LadderId Guid?`; FK constraint to `ladders` lands in this phase.
- `src/GameKit.Core/Entities/SessionParticipant.cs` — already has
  `RatingBefore` / `RatingAfter` / `RatingDelta` `double?` columns
  reserved for this phase.
- `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` — already declares
  `PlayerRankAdjust = "admin.player.rank_adjust"`. New action constant
  `LadderEndSeason = "admin.ladder.end_season"` will be added here.
- `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs` — already has
  a `PlayerRankAdjust` template; will need an `end-season` template.
- Glickman, M. *Example of the Glicko-2 System* (PDF at
  <https://www.glicko.net/glicko/glicko2.pdf>) — the worked example used as
  the RANK-06 1000-match convergence test fixture.
</canonical_refs>

<prior_decisions>
## Carrying Forward From Earlier Phases

- **Per-package migrations** under `__ef_migrations_rankings` history; the
  Rankings migration excludes Core/Auth/Admin entities from its model graph
  via the same `IModelCustomizer` pattern used by Auth + Admin (Phase 1
  D-13 + Phase 2 + Phase 3 D-02).
- **Rating columns = `double precision`**, NOT `NUMERIC(8,2)` (locked by
  RANK-03 + Pitfalls #13).
- **`IRankingAlgorithm.Apply(state, batch)`** is the only interface — no
  per-match overload exists, even as a convenience. (Pitfalls #1.)
- **Glicko-2 vendored** from `MaartenStaa/glicko2-csharp` (MIT, GPL-
  compatible). Attribution header in the source file. Glickman's worked
  example ships as a regression fixture (STACK.md §2).
- **Admin mutations write to `admin_audit_log`** via the existing
  `IAdminAuditWriter` from Phase 3 D-09. No new audit-write path.
- **Admin auth scheme** is `GameKitAdmin` cookie (Phase 3 D-01/D-02);
  superadmin-only is required for rank-adjust and end-season (Phase 3 D-06
  policy).
- **`IClock` / `IIdGenerator` / `ICurrentPlayer`** already injected in
  Core; new Rankings services consume the same primitives.
- **Background jobs via `BackgroundService` + Polly** (not Hangfire /
  Quartz — STACK.md §1). Multi-instance safety via Redis distributed lock
  (`SET NX PX`).
- **Rate-limit named policies** (CORE-12): a new constant
  `gamekit:sessions:complete` will be added under the same
  `IGameKitRateLimitPolicies` interface.
- **`SessionParticipant.PlayerId` is nullable** for GDPR cascade (Phase 1
  D-13). The GDPR export endpoint must filter `WHERE PlayerId = {id}` and
  cannot leak rows where `PlayerId IS NULL` from prior tombstones.
</prior_decisions>

<decisions>
## Implementation Decisions

### Rating Period / Window (RANK-04/05/06)

- **D-01:** Glicko-2 batch updates run on a **time window** via a
  `RankingsTickerService : BackgroundService`. The ticker checks each
  active ladder every 60 seconds; when an active ladder's rating period
  (default 1h) has elapsed since its last drain, the ticker applies
  `IRankingAlgorithm.Apply(state, batch)` against all `session_participants`
  rows that have a `Result` but no `RatingBefore`/`RatingAfter` yet for
  that ladder. One batch per ladder per period.
- **D-02:** Default `RatingPeriod = TimeSpan.FromHours(1)` per ladder.
  Overridable in the ladder's config JSONB. Glickman's worked example
  uses weeks for chess; 1h is appropriate for fast-cadence game sessions
  and gives operators headroom (RANK-06's convergence test uses
  ~50 matches/period, which 1h easily covers under load).
- **D-03:** Multi-instance safety via **Redis distributed lock**
  (`SET NX PX`). Lock key: `gamekit:rankings:ticker:lease`. TTL = 90s
  (one and a half tick cadences). The ticker self-renews mid-tick; on
  Redis disconnect, Polly v8 backs off and the next instance picks up
  leadership on TTL expiry. Mirrors the Phase-5 matchmaking ticker
  pattern committed in STACK.md §1.
- **D-04:** On batch failure (algorithm throws / Postgres deadlock), the
  ticker rolls back the per-ladder transaction and logs an
  `OpenTelemetry`-friendly `ActivitySource` event under
  `GameKit.Rankings.Ticker`. The pending rows stay un-applied until the
  next tick; no partial updates land. (`IRankingAlgorithm` implementations
  must be deterministic for the same input batch — guaranteed by
  Glickman's spec for the default; documented contract for replacers.)

### Session-Complete API Contract (RANK-11)

- **D-05:** `POST /api/sessions/{id}/complete` requires a **service-account
  bearer token** (`Authorization: Bearer <svc-token>`). Distinct from the
  player JWT scheme. Player JWTs hitting this endpoint return 403. This
  is "trusted game server only" — only the game's authoritative server
  knows real outcomes, and this design refuses to let clients self-report
  wins.
- **D-06:** Service tokens are minted via a new CLI verb
  `dotnet gamekit service-token issue --name <name> [--expires <duration>]`.
  Raw bearer is printed to stdout exactly once; only the SHA-256 hash is
  stored, in a new `service_tokens` table (mirrors the Phase-2
  refresh-token storage discipline). Token-revoke verb: `dotnet gamekit
  service-token revoke <name>`. Listing: `dotnet gamekit service-token
  list`. No web UI in v1.
- **D-07:** Endpoint is state-conditional via
  `UPDATE game_sessions SET state = 'completed', completed_at = @now
  WHERE id = @id AND state = 'active' RETURNING ...`. Zero rows updated
  means either (a) already completed → return the cached rating deltas
  from `session_participants` with `200 OK`, or (b) session in another
  state (`pending` / `cancelled` / `abandoned`) → return `409 Conflict`.
- **D-08:** Mandatory `Idempotency-Key` header. The `{session_id,
  idempotency_key}` pair is dedup'd in a new
  `session_complete_idempotency` table (columns: `session_id`,
  `idempotency_key text`, `response_hash text`, `created_at`) with a
  **24h TTL** (matches Stripe/Plaid conventions). Duplicate key with the
  same body → return cached response. Duplicate key with a different
  body → `409 Conflict idempotency_key_reused`. A cleanup
  `BackgroundService` deletes rows older than the TTL nightly (or on
  startup if the prior tick missed).
- **D-09:** Request body shape:
  ```json
  {
    "participants": [
      { "player_id": "uuid", "team": 0, "result": "win|loss|draw|forfeit", "score": 0 }
    ]
  }
  ```
  Validated by FluentValidation (mirrors the Phase-2/3 endpoint-filter
  pattern). Result enum mirrors `SessionResult`. Unrecognized player_id
  → `404`. A participant missing from the body → `400` (the session's
  recorded participant list is the source of truth).
- **D-10:** New rate-limit policy `gamekit:sessions:complete` —
  300 requests/min/service-token (game servers may report many
  concurrent matches). Burst configurable via `GameKitRankingsOptions`.

### Seasonal Reset (RANK-10)

- **D-11:** Season end is **admin-triggered only**. A new admin palette
  verb `end-season` (superadmin-only, fills one of the Phase-3
  "registered-but-no-dialog" slots flagged in 03.1-REVIEW-GAPS.md)
  opens a confirmation dialog: "End current season for ladder
  *<name>*. This archives `player_ranks` into `season_rank_archive` and
  applies the configured reset policy. Type the ladder name to
  confirm." Writes an `admin.ladder.end_season` audit row to
  `admin_audit_log` via the existing `IAdminAuditWriter`. Single
  SERIALIZABLE transaction.
- **D-12:** Reset strategy is **per-ladder** (config JSONB picks one of
  three variants). All three live as enum-backed `SeasonResetPolicy`
  variants in `GameKit.Rankings`:
  - `SoftRegress` (default): each player's new starting rating =
    `default_rating + (rating - default_rating) * RegressionFactor`;
    rating-deviation set to `min(RdCeiling, current_rd + RdBump)`;
    volatility reset to ladder default. Configurable fields:
    `RegressionFactor` (default 0.5), `RdCeiling` (default 200),
    `RdBump` (default 50).
  - `HardReset`: rating, RD, volatility all reset to ladder defaults.
  - `ArchiveOnly`: archive row written; live ranks unchanged. Seasons
    become a passive query-time concept.
- **D-13:** New table `season_rank_archive` (columns: `id`, `ladder_id`,
  `season_id`, `player_id` *(nullable for GDPR cascade)*, `rating`,
  `rating_deviation`, `volatility`, `wins`, `losses`, `draws`,
  `archived_at`). Composite index `(ladder_id, season_id, rating DESC)`
  for archived-season leaderboard queries. The archive table sits in
  the Rankings package's own migration (RANK-14).
- **D-14:** New table `ladder_seasons` (columns: `id`, `ladder_id`,
  `season_number int`, `started_at`, `ended_at` *(null while current)*,
  `ended_by_admin_id` *(null until ended)*). The "current season" for a
  ladder is the row with `ended_at IS NULL`. `end-season` closes the
  current row and opens a new one in the same transaction.

### GDPR Export (RANK-13)

- **D-15:** `GET /api/players/{id}/export` returns a **single-blob
  `application/json`** with the top-level shape:
  ```json
  {
    "player": { "id", "display_name", "created_at", "last_seen_at",
                "is_banned", "banned_at", "ban_reason" },
    "identities": [ { "provider", "external_id_hash", "created_at" } ],
    "credentials_metadata": [ { "created_at", "last_used_at" } ],
    "sessions": [ { "session_id", "ladder_id", "team", "result",
                    "rating_before", "rating_after", "completed_at" } ],
    "rating_history": [ { "ladder_id", "season_id", "rating", "rd",
                          "volatility", "snapshot_at" } ],
    "exported_at": "ISO-8601 UTC"
  }
  ```
  Password hashes, raw OAuth tokens, and refresh-token hashes are NEVER
  included. Identities include only the SHA-256 `external_id_hash`,
  matching the Phase-2 storage shape. A schema contract test asserts
  exactly these keys (RANK-13 success #5).
- **D-16:** Auth: **two endpoints, one handler.**
  - `GET /api/players/{id}/export` — player JWT only; `{id}` must match
    the JWT's `sub`. Cross-player attempts → 403.
  - `GET /admin/api/players/{id}/export` — admin cookie scheme,
    `Superadmin` policy. Operator can fulfill DSARs for players who
    can't log in (banned, lost-password, deceased). Writes an
    `admin.player.gdpr_export` audit row.
- **D-17:** Snapshot consistency: the handler opens a **`REPEATABLE
  READ` read-only transaction** at entry, reads every table inside it,
  commits at exit. Postgres MVCC gives a free point-in-time view across
  all reads with no writer blocking. Cleaner GDPR auditor story than
  eventual-consistency.
- **D-18:** Response size cap: 25 MB (configurable via
  `GameKitRankingsOptions.GdprExport.MaxBytes`). Exceeding the cap
  returns `413 Payload Too Large` with a problem-details body
  pointing operators at the (v2) chunked/streaming path. v1 ships the
  cap; the streaming path is a deferred idea.

### Manual Rank Adjustment (RANK-12)

- **D-19:** New endpoint `POST /admin/api/players/{id}/rank-adjust`
  (cookie scheme, `Superadmin` policy, antiforgery required). Body:
  ```json
  { "ladder_id": "uuid",
    "new_rating": 1500.0,
    "reason": "tournament-correction" }
  ```
  Validated by FluentValidation: `reason` 3–512 chars (mirrors the
  Phase-3 D-09 ban-reason policy); `new_rating` finite double, bounded
  to `[100, 4000]` (configurable). The Phase-3 admin palette's
  `rank-adjust` verb (currently a registered no-op) opens the dialog
  that POSTs here. Single SERIALIZABLE transaction:
  1. UPDATE `player_ranks` SET rating = @new_rating, rd = @new_rd,
     volatility = @vol WHERE player_id = @id AND ladder_id = @ladder_id
     (creates the row lazily if missing — matches RANK-07).
  2. INSERT into `admin_audit_log` via `IAdminAuditWriter` with action
     `admin.player.rank_adjust`, before/after JSON snapshots, and the
     reason text.
- **D-20:** Manual rank-adjusts **bypass the rating-period batch** —
  they take effect immediately, are visible in the next leaderboard
  query, and are NOT replayed if the participant later appears in a
  batched update. Operators are the authority.

### Library Boundaries & Wiring

- **D-21:** `services.AddLadder("name", config)` is a **build-time
  fluent API** on the same builder returned by `AddRankings()` (which
  itself is a method on `AddGameKit()`'s builder per the Phase-1
  composable pattern). Per-ladder config struct fields: `DefaultRating`
  (default 1500), `DefaultRd` (default 350), `DefaultVolatility`
  (default 0.06), `RatingPeriod` (default 1h), `SeasonResetPolicy`
  enum + per-variant fields. A row is INSERTed into `ladders` at
  startup via an `IHostedService` (idempotent by name). Runtime
  ladder CRUD is a deferred idea.
- **D-22:** The session-complete endpoint lives in **`GameKit.Core`**
  (it owns the `game_sessions` + `session_participants` tables) but
  consumes an `IPostSessionCompleteHandler` port. `GameKit.Rankings`
  registers an adapter that enqueues participants for the next batch
  drain (writes to a new lightweight `pending_rating_updates` table
  scoped to the rankings schema). Core has zero dependency on
  Rankings; Rankings extends Core through the port.
- **D-23:** Leaderboard query API (RANK-08) lives in `GameKit.Rankings`
  as `ILeaderboardService` with two methods: `TopAsync(ladderId,
  limit, ct)` (default limit 100) and `AroundAsync(ladderId,
  playerId, window, ct)` (default window 5 above + 5 below). Hot-path
  index `idx_player_ranks_ladder_rating` on `(ladder_id, rating
  DESC)`. v1 ships the service surface; HTTP exposure is a deferred
  idea (game devs may want their own wrapping/auth) — admin queries
  use the same service from a `/admin/api/leaderboard` GET.
</decisions>

<deferred>
## Deferred Ideas

Captured during discussion or recognized as obvious v2 scope. Not in
Phase 4. Surface to the roadmap backlog when v1 ships.

- **Real-time rating push** (SignalR / WebSockets) — operators use the
  Phase-3 admin polling pattern in v1. v2 may publish rating-changed
  events for live leaderboards.
- **Cross-ladder tournaments** — sessions reference a single
  `LadderId`. Multi-ladder play (e.g. a tournament that spans 1v1 and
  2v2 ladders) is a v2 design.
- **Auto season-rollover ticker** — `D-11` chose admin-only triggering
  for the v1 safety story. A `SeasonRolloverService` that reads
  `ladder_seasons.scheduled_end_at` is a clean v2 add.
- **Chunked / streaming GDPR export** — `D-18` ships a 25 MB cap; v2
  may add NDJSON streaming or zip-of-JSON for power users.
- **Runtime ladder CRUD via admin UI** — `D-21` ships build-time
  registration. v2 may add ladder create/edit/delete behind the
  Superadmin policy.
- **HTTP leaderboard exposure for player-facing surfaces** — `D-23`
  ships `ILeaderboardService` but only admin GET. Game-facing
  `/api/ladders/{id}/leaderboard` is a v2 add (deliberately punted so
  game devs can wrap their own auth/caching).
- **Multi-rating-system support** — v1 ships Glicko-2 as the default
  algorithm. The `IRankingAlgorithm` interface is the swap point;
  Elo / TrueSkill / MMR variants are user-implementable but not
  shipped.
- **Rotate JWT signing keys via admin UI** — the Phase-3 palette has
  the `rotate-signing-key` verb registered with no dialog; this is
  Phase 6 ops territory, not Phase 4.
</deferred>

<acceptance_anchors>
## ROADMAP Success Criteria → Decision Anchors

| SC  | What must be TRUE                                                                    | Where it lives                                |
| --- | ------------------------------------------------------------------------------------ | --------------------------------------------- |
| #1  | 1000-match convergence test passes within Glickman's tolerance                       | D-01/D-02 ticker + Glickman fixture           |
| #2  | `/sessions/{id}/complete` 5× retry → exactly one rating delta applied               | D-07/D-08 state-conditional + Idempotency-Key |
| #3  | Rating columns are `double precision`; before/after/delta snapshotted at completion | RANK-03 lock + `SessionParticipant.cs:39-45`  |
| #4  | Season archive preserves prior-season top-N + around-me queries                     | D-13/D-14 archive + season tables             |
| #5  | `/export` returns the documented JSON bundle                                         | D-15/D-16/D-17 export shape + auth + snapshot |
| #6  | Admin rank-adjust writes before/after to `admin_audit_log` atomically                | D-19/D-20 transactional adjust + audit-write  |
</acceptance_anchors>

<next_steps>
## Next Steps

1. `/clear` to drop the discussion context.
2. `/gsd-plan-phase 04` — researcher will deep-dive Glicko-2 vendoring,
   Postgres `double precision` schema patterns, Redis distributed-lock
   ergonomics for the ticker, REPEATABLE READ snapshot semantics, and
   the service-token storage shape (mirroring Phase-2 refresh-token
   discipline). Planner will produce 6–8 plans covering: ladder/rank
   schema + migration, Glicko-2 port + 1000-match fixture, session-
   complete endpoint + service-tokens + idempotency, rankings ticker
   + leaderboard service, season rollover + archive, GDPR export +
   admin gdpr_export verb, admin rank-adjust endpoint + dialog wiring.
</next_steps>
