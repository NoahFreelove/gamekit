# Pitfalls Research

**Domain:** Adding v2.0 features to a mature GPL self-hosted .NET 10 game-services backend
**Researched:** 2026-06-05
**Confidence:** HIGH (grounded in v1 codebase decisions + official documentation + verified community sources)

---

## Critical Pitfalls

### Pitfall 1: Apple Client-Secret JWT Expiry Causes Production Outage at 6-Month Mark

**What goes wrong:**
Apple Sign In does not use a static client secret. The client secret is a short-lived ES256-signed JWT
(max 6-month expiry) that must be generated from the developer's `.p8` private key, Key ID, and Team ID.
If the implementation generates this JWT once at startup and caches it, the backend silently starts returning
`invalid_client` errors to all Apple logins exactly 6 months after the last deploy — with no warning.

**Why it happens:**
The aspnet-contrib `AspNet.Security.OAuth.Apple` provider has a `GenerateClientSecret = true` flag and a
`ClientSecretExpiresAfter` property that default to auto-renewal. Developers who read examples for other
OAuth providers assume `ClientSecret` is a static config value and hardcode the JWT, missing that Apple's
is time-bounded. Alternatively, they wire the provider correctly but don't set up key rotation automation,
so the secret silently expires between deploys.

**How to avoid:**
- Use `GenerateClientSecret = true` (aspnet-contrib default) so a fresh JWT is generated per authorization
  code exchange. Do NOT cache the generated JWT across requests.
- Load the `.p8` key from the filesystem (or environment variable / secrets manager) via the `UsePrivateKey()`
  delegate — never hardcode the key bytes in source.
- Set `ClientSecretExpiresAfter = TimeSpan.FromDays(170)` (< 180) as the safe margin.
- Add an integration test that asserts `ClientSecretExpiresAfter` < 180 days.
- In the ops guide, document the `.p8` key rotation process: Apple does NOT rotate the key automatically —
  the developer generates a new `.p8` in the Apple Developer Portal and restarts the service.

**Warning signs:**
- All Apple logins return `invalid_client` while Steam/Discord/Google continue working.
- The Apple provider was configured more than 6 months ago without a deploy.
- The `.p8` key file path returns a 404 on the server (missing from deploy artifact).

**Phase to address:**
Auth providers phase (Google/Apple/Epic OAuth). Verified in the integration test suite: assert the provider
options object has `GenerateClientSecret = true` and `ClientSecretExpiresAfter.TotalDays < 180`.

---

### Pitfall 2: Apple Private Email Relay Breaks Cross-Provider Identity Linking

**What goes wrong:**
Apple allows users to hide their real email address behind a randomly generated `@privaterelay.appleid.com`
relay address that is unique per app/organization. Two consequences break the v1 identity-linking model:
(a) A user who previously created an account via Google (with their real email) cannot link to Apple unless
the system explicitly matches on the stable `sub` (Apple User Identifier), not on email.
(b) A user who signed up with Apple on one app/org registration will get a *different* relay address if
your Apple Developer credentials change (e.g., during an org transfer or if the Service ID is recreated).
In May 2025 a widespread incident caused Apple to reissue user identifiers, breaking stored `external_id`
links for all affected apps.

**Why it happens:**
The v1 `player_identities` model stores `external_id = provider-issued subject` and the identity-linker
code in `GameKit.Auth` already uses the `sub` claim (not email) for deduplication. The risk is in the
*email-based* identity-merge heuristic: if v2 account merge offers a "merge by email" shortcut and the
stored email for the Apple identity is the relay address (not real), the merge will fail to find the
canonical record even when the underlying human is the same person.

**How to avoid:**
- In the Apple provider `OnCreatingTicket`, extract `sub` (Apple User Identifier) as the canonical
  `external_id`. Never use the relay email as the linking key.
- Store the relay email in `player_identities.metadata` (JSONB) only for informational purposes.
- In the Account Merge UI, expose "Merge by Provider Identity" (matching on provider + external_id),
  not "Merge by Email" as the Apple path.
- Document for operators: if the Apple Service ID is ever recreated, all Apple `external_id` values are
  invalid and require a player-initiated re-link flow.
- Never use the relay email to send transactional email without first registering your send domain at
  `appleid.apple.com/auth/keys` (relay requires domain registration).

**Warning signs:**
- Apple logins create new player rows instead of linking to existing accounts after an Apple credential refresh.
- Player support tickets: "I lost my progress after logging in with Apple."
- `player_identities.external_id` for Apple provider contains `@privaterelay.appleid.com` strings instead of opaque subject identifiers.

**Phase to address:**
Auth providers phase. Assert in the Apple provider `OnCreatingTicket` callback that `external_id` is the
`sub` claim, not the email claim. Integration test: verify two logins with the same Apple `sub` but
different relay emails resolve to the same `player_id`.

---

### Pitfall 3: Account Merge Leaves Orphaned FKs or Splits the Merged Record on Retry

**What goes wrong:**
Account merge is a multi-table operation: re-home `player_identities`, `refresh_tokens`, `player_ranks`,
`matchmaking_tickets`, `game_session_participants`, `admin_audit_log` from the *source* player to the
*target* player, then soft-delete or hard-delete the source. If the operation crashes (or the client
retries) between steps, the database is in a partial state: some FKs point to the now-deleted source,
causing FK violations or silent data loss. The `admin_audit_log` FK from `actor_player_id` pointing at a
deleted row violates referential integrity unless the column is `SET NULL` or `DEFERRABLE`.

**Why it happens:**
Developers wrap the merge in a single `SERIALIZABLE` transaction and assume atomicity is sufficient.
But the merge operation is long (many tables), and if Redis state (presence heartbeat, matchmaking ticket)
also needs merging, that Redis mutation is outside the Postgres transaction boundary. A crash after
Postgres commits but before Redis is cleaned leaves Redis pointing at the deleted source player.

Additionally, naive idempotency: the second call to `MergeAsync(source, target)` may see the source
already deleted and interpret it as "merge already done" but the Redis cleanup from the first partial
call may not have run.

**How to avoid:**
- Introduce an `account_merge_log` table (Core or new package table — NOT modifying Core tables; add
  via the Auth or a dedicated merge package) with columns: `id UUID`, `source_player_id`, `target_player_id`,
  `status` (enum: `pending`, `committed`, `redis_cleaned`), `created_at`, `completed_at`.
- Run the Postgres re-homing inside a SERIALIZABLE transaction; write `status = committed` in the same
  transaction. Only after commit: run Redis cleanup and update `status = redis_cleaned`.
- Idempotency: on retry, look up `account_merge_log` by `(source_player_id, target_player_id)`. If
  `status = committed` and Redis not cleaned, resume at Redis step. If `status = redis_cleaned`, return
  success. If `status = pending`, the prior transaction rolled back — restart from scratch.
- FK on `admin_audit_log.actor_player_id`: change to `ON DELETE SET NULL` (or use a tombstone pattern
  where source player row is kept with `is_deleted = true` and FK on `player_id` is the authoritative
  anchor).
- Handle the banned ↔ unbanned player conflict: if source is banned and target is not (or vice versa),
  the merge must preserve the banned status on the surviving record. Define policy in options
  (`MergeOptions.BanBehavior = PreserveMostRestrictive`).
- Expose `IMergeService` as a replaceable interface so operators with special FK relationships can
  plug in their own migration logic.

**Warning signs:**
- `account_merge_log` rows stuck in `pending` state after a crash.
- FK violation exceptions from `admin_audit_log` after a merge.
- Player reports their match history doubled (merge ran twice against same pair without idempotency check).
- A banned player gained access to the target account's content after merge.

**Phase to address:**
Account merge phase. Implement `account_merge_log` as the first task in this phase. Integration test:
kill the process mid-merge and verify resume-on-restart produces a clean final state. Specifically test
the banned-source + active-target case.

---

### Pitfall 4: Advisory Lock Key Collision for New v2 Packages

**What goes wrong:**
The five v1 advisory-lock keys are already fixed:
- Core: `1800940027`
- Auth: `-298890956`
- Admin: `-2101739634`
- Rankings: `-156812172`
- Matchmaking: `388956820`

If v2 adds new packages (`GameKit.Lobby`, a potential `GameKit.Auth.Argon2` migration path, or a
`GameKit.Regions` package) and the developer picks advisory-lock keys without live-verifying via
`SELECT hashtext('gamekit.<pkg>.migrations')::bigint`, there is a risk of collision (either with an
existing package key or with itself under a different truncation). A collision means two packages try
to acquire the same Postgres advisory lock simultaneously during startup; in the best case one blocks
indefinitely, in the worst case both think they hold the lock and run migrations concurrently against
the same history table.

**Why it happens:**
The `hashtext()` → `int4` → `::bigint` chain was established in v1 and produces correct signed values
(negative keys are valid). The trap is using the live value *before* confirming it is pairwise-distinct
from all existing keys. Developers sometimes compute the key via `SELECT hashtext(...)::int` (omitting
`::bigint`) which silently truncates to `int32` range, producing a different value than the runtime
`pg_advisory_lock(bigint)` call which operates on full `int64`.

**How to avoid:**
- For every new package, run `SELECT hashtext('gamekit.<pkg>.migrations')::bigint` inside a Testcontainers
  Postgres instance and record the live value in `MigrationConstants.AdvisoryLockKey`.
- Add a companion pairwise-distinctness test in the new package's integration tests, duplicating all
  known keys as integer literals (not symbolic constants) — exactly mirroring `MatchmakingAdvisoryLockKeyTests`.
- Name the migration history table `__ef_migrations_<pkg>` (never share a history table across packages).
- In `XxxMigrationModelCustomizer`, explicitly `ExcludeFromMigrations` every entity from every prior
  package using `typeof()` references. A new v2 entity type added to a prior package that is not added
  to the exclusion list will cause the new package's next migration to try to CREATE a table already
  owned by the prior package.

**Warning signs:**
- Startup hangs at `[migration:acquiring advisory lock]` log line.
- `__ef_migrations_<pkg>` history table accumulates rows from a different package.
- A package's migration creates a table that already exists (EF generates `IF NOT EXISTS` for some
  providers but not all).
- The `MatchmakingAdvisoryLockKeyTests`-style pairwise test fails to compile (new key not yet added
  to the assertion matrix).

**Phase to address:**
Every phase that introduces a new package. The advisory-lock live-verify step must be gated before
any integration test executes (mirrors the v1 pattern: Wave 0 test is RED until the live-verified
key is committed).

---

### Pitfall 5: SignalR Redis Backplane Requires Sticky Sessions at the Load Balancer — Not Optional

**What goes wrong:**
When `GameKit.Lobby` adds a SignalR hub and the Admin UI adopts the Redis backplane for multi-replica
health broadcasts, deploying behind a round-robin load balancer without sticky sessions causes
`negotiate` → `connect` to land on different replicas. The SignalR negotiate step returns a
`connectionToken` that is only valid on the server that issued it. If the WebSocket upgrade goes to a
different replica, the connection fails with a 404 or an opaque transport error. The Redis backplane
routes *messages* across replicas but does NOT route *connection handshakes*.

**Why it happens:**
The official documentation states that sticky sessions are required but developers conflate "Redis
backplane routes messages" with "Redis backplane handles all scaling concerns." Docker Compose and
local Kubernetes clusters often happen to route stickily by accident (single-node, same pod), masking
the bug until a real multi-node deployment.

**How to avoid:**
- In the ops guide and `docker-compose.yml` sample, explicitly document the sticky-session requirement
  (IP hash or cookie-based) for any load balancer in front of a multi-replica deployment.
- Use `AddSignalR().AddStackExchangeRedis(connectionString, opts => opts.Configuration.ChannelPrefix = "gamekit")`.
  The channel prefix is mandatory — without it, lobby messages leak into other apps sharing the Redis
  instance (and vice versa in test environments).
- Register the backplane as optional/conditional: single-replica installs (the common case) should not
  require Redis backplane configuration. Gate it behind `AddGameKitLobby(opts => opts.UseRedisBackplane = true)`.
- Test the Redis backplane with a genuine two-instance `WebApplicationFactory` in `GameKit.Lobby.Integration.Tests`
  to catch the sticky-session gap before production.

**Warning signs:**
- `HubException: Connection not found` errors in the client after a successful negotiate.
- Lobby messages reach some clients but not others under load.
- Admin health panel shows one replica's ring buffer but not another's.
- Logs show `Backplane: received message for unknown connection`.

**Phase to address:**
Lobby phase (SignalR hub + Redis backplane wiring). Admin UI multi-replica phase. The integration test
must use two in-process `TestServer` instances sharing a Testcontainers Redis, with a manual sticky-session
simulation (pin each client to one server).

---

### Pitfall 6: Rating Feedback Loop — High-Rated Players Dominate Every Match After Wiring Rankings → Matchmaking

**What goes wrong:**
In v1, `EloRangeMatchmakingStrategy` uses `rating = 0` for all players — effectively random matching.
When v2 wires real ratings from `player_ranks`, any ladder with a small active player pool will funnel
all high-RD (new/inactive) players together with the top-rated players because the bracket-widening
timer expands the EloRange window until there are enough players to form a match. Result: new players
consistently face top-rated players; new players lose; new players churn; pool shrinks further;
bracket widens more. This is a self-reinforcing downward spiral.

**Why it happens:**
The bracket-widening logic in `GameKitMatchmakingOptions.Matchmaking.BracketRampSeconds` was configured
against a rating=0 world. In that world the window expansion is irrelevant (everyone is in the same
bracket). With real ratings and a sparse pool, the expansion becomes the dominant path.

**How to avoid:**
- Add a `MinPoolDepthBeforeBracketExpansion` option: bracket widening only begins after there are at
  least N players in queue (e.g., `N = 2 * party_size`). This prevents immediate widening for the
  first player in queue.
- Implement soft skill bands: new players (`RD > 200`) are matched first within a separate "placement
  pool" before competing on the rated ladder. The placement pool does not widen.
- Add a `MaxBracketWidth` cap (e.g., `± 500 rating`) that bracket-widening cannot exceed, so top-1%
  players are never matched against floor players regardless of queue depth.
- Expose per-ladder `BracketRampSeconds`, `MaxBracketWidth`, and `MinPoolDepthBeforeBracketExpansion`
  on `MatchmakingLadderConfig` (they already exist as per-ladder fields; wire them to the strategy).
- Add a queue-depth metric (already present as `MatchmakingRedisKeys.Queue`) to the Admin health panel
  so operators can observe pool starvation.

**Warning signs:**
- Median match rating spread consistently > 3× `BracketStart` value in production telemetry.
- New player retention rate drops after enabling rated matchmaking.
- Admin panel queue-depth gauge shows short queue but high bracket-spread.
- Glicko-2 RD for a cohort never converges (players churn before enough games accumulate).

**Phase to address:**
Rating-aware matchmaking phase. Before wiring real ratings, implement `MaxBracketWidth` and
`MinPoolDepthBeforeBracketExpansion` as guardrails. Add a placement-pool concept for high-RD players.

---

### Pitfall 7: Rank Decay Applied to Legitimately Absent Players Destroys Retention

**What goes wrong:**
Configurable rank decay deducts from a player's rating after an inactivity period. If the decay rate
is calibrated against Glicko-2's `RD` inflation (which already models uncertainty for absent players)
without understanding that Glicko-2 *already* increases RD over time, the implementation double-penalizes
absence: `RD` inflates (correct), AND `rating` drops (additional penalty on top). Players returning
from a vacation face a negative rating they did not earn in a fair match, quit, and leave negative reviews.

**Why it happens:**
The Glicko-2 paper's `phi'` update during inactive periods inflates RD (uncertainty), which means the
absent player will gain/lose more rating from their first match back — a natural "placement-like" effect.
Decay on top of this is redundant and harsh. The confusion is that "decay" in popular parlance (League
of Legends LP decay, Rocket League rank decay) refers to *rank tier* demotion, not raw rating delta.

**How to avoid:**
- Implement decay as *RD inflation only* (increasing uncertainty, not decreasing rating). This is the
  mathematically correct Glicko-2 approach.
- If the operator wants actual rating reduction (for leaderboard hygiene), add a separate `InactivePlayerRatingPenalty`
  that is explicitly distinct from RD inflation, applied only at a configurable inactivity threshold
  (e.g., 90 days), with a floor (`MinRatingAfterDecay`) so a player cannot decay below their initial rating.
- Always write a `rank_adjust_audit` row (via the existing `RankAdjustService`) when decay runs, tagged
  with `reason = "decay"` and the `period_end` timestamp, so players can inspect why their rating changed.
- Expose a `DecayProtectionDays` option: players below a configurable rank (e.g., Bronze tier) are
  immune to decay — new players should not be penalized for not being addicted to the game.
- The decay `BackgroundService` must acquire the same per-ladder Redis leader-election lock
  (`gamekit:matchmaking:matcher:lock`) as the matchmaking ticker to prevent decay running on a non-leader
  replica simultaneously with matchmaking.

**Warning signs:**
- Player rating becomes negative (impossible in fair Glicko-2).
- Players returning from absence find their rank 200+ points lower than before absence.
- Admin audit log shows `reason = "decay"` entries with large negative deltas.
- Player churn spikes correlate with decay job run timestamps.

**Phase to address:**
Rank decay phase. Unit test: a player with `R=1500, RD=50` who is absent for 30 days must NOT lose
rating, only gain RD. Integration test: verify decay audit rows are written. Verify decay respects
`DecayProtectionDays`.

---

### Pitfall 8: Placement Match RD Math — Win-Streak Inflates Rating Without Convergence

**What goes wrong:**
Placement matches for new players use the initial high-RD state (Glicko-2 `phi = 2.0+`) to converge
quickly to a skill estimate. A determined smurf (experienced player on a new account) will intentionally
face opponents weaker than their true skill during the first N placement games, lose 0 of them,
and arrive at an inflated rating that overshoots their true level. The large initial RD means the
algorithm *intends* to move the rating quickly — but it cannot distinguish "new to the game" from
"experienced player hiding skill."

**Why it happens:**
Glicko-2 is designed to converge for *honest* players. The algorithm has no built-in exploit detection.
Placement matches compound the problem: by definition these are the games where opponent quality is
lowest (other new players / unplaced players), so a ringer faces only the weakest opponents during
their placement window.

**How to avoid:**
- Limit placement matches to a separate pool — placed players do NOT appear in the placement ladder.
  Only unplaced (`RD > threshold`) players match against each other during placement.
- Cap placement match rating gain: a player cannot gain more than `MaxPlacementRatingGain` (e.g., 400)
  per placement season, regardless of win streak. This bound is stored, not computed, and the
  bound resets at season reset.
- After placement: transition the player to the rated ladder at their calculated rating, but with RD
  reduced to the placement-complete threshold — this prevents a second wave of high-RD volatility.
- The placement phase is 10 games (configurable `PlacementMatchCount`). After completion, flag
  `is_placed = true` on the `PlayerRank` record and route the player to the normal EloRange matchmaking.
- For smurf detection: flag any placement winner with `win_rate > 0.9` over placement games for admin
  review (write to `admin_audit_log`). Do NOT auto-rank them up — a human reviews.

**Warning signs:**
- Post-placement leaderboard shows new accounts in the top 5% with 100% win rate during placement.
- Glicko-2 volatility (`sigma`) for new accounts lands unusually high after placement (opponent quality mismatch).
- Admin audit log shows no placement-pool entries but `is_placed = true` records exist.

**Phase to address:**
Placement matches phase. Implement the separate placement pool as a distinct `MatchmakingLadderConfig`
variant (`IsPlacementLadder = true`). The rated-ladder strategy must gate on `is_placed = true`.

---

### Pitfall 9: Backfill Joins a Session After Outcome Is Already Decided

**What goes wrong:**
A player abandons 90 seconds into a 2-minute match. Backfill places a new player into the session.
The game ends 30 seconds after the backfill player joins, with them having played only 25% of the
session. The `SessionCompleteService` fires `IPostSessionCompleteHandler` with a result that assigns
the backfill player a full rating change (win or loss) for a session they barely participated in.
Worse: if the game is already lost when they join, they take a loss they cannot recover.

**Why it happens:**
The `GameSessionParticipant` model records `joined_at` and `left_at` but the v1
`IRankingAlgorithm.Apply(state, batch)` receives the full participant batch without a
"backfill-joined-late" flag. The ranking algorithm cannot distinguish a player who played the full
session from one who joined in the last minute.

**How to avoid:**
- Add `BackfillJoinedAt` (nullable `DateTimeOffset`) and `ParticipationFraction` (computed, 0.0–1.0)
  to `GameSessionParticipant`. Set `BackfillJoinedAt` when a backfill player joins.
- In `IRankingAlgorithm.Apply`, if `ParticipationFraction < MinParticipationForRatingChange` (configurable,
  e.g., `0.5`), skip the player from the batch — their rating is unchanged.
- The rating snapshot used for EloRange matching must be taken at the moment the matchmaking ticket
  is created (Redis `HSET` in the ticket hash), not at `SessionCompleteService` time. This prevents
  a player from changing their rating between match-found and session-complete.
- Emit an `admin_audit_log` row for every backfill join with `action = "session.backfill_join"` and
  the `ParticipationFraction` at session complete.

**Warning signs:**
- `player_ranks` records show rating delta = 0 for players with `backfill_joined_at IS NOT NULL` — this
  should be expected behavior after the fix, but if delta ≠ 0 for low-participation backfills, the
  guard is not running.
- Player complaints: "I joined an already-lost match and got penalized."
- `ParticipationFraction` column missing from `game_session_participants` schema (implementation shortcut
  that defers the column then never adds it).

**Phase to address:**
Backfill phase. The `ParticipationFraction` guard must be implemented in the *same* phase as backfill
itself — it cannot be deferred. Add a `BackfillJoinLateProtectionTests` integration test.

---

### Pitfall 10: Regional Pool Redis Keys Are Not Namespaced — Cross-Region Ticket Leakage

**What goes wrong:**
If regional matchmaking pools use Redis sorted-set keys like `gamekit:matchmaking:queue:<ladder>` without
a region segment, all regional queues for the same ladder merge into one global queue. Players in the
`us-east` pool match with players in the `eu-west` pool, defeating the purpose of regional pools.
Alternatively, if the region is embedded in the ladder NAME (e.g., `main-us-east`) rather than in the
Redis key structure, operators must define N×R ladder configs (N ladders × R regions) creating combinatorial
explosion in `Directory.Packages.props` and option objects.

**Why it happens:**
The v1 `MatchmakingRedisKeys` class uses `<ladder>` as the differentiator. Region was previously
an escape hatch via `metadata.region` on the ticket — the v1 ticker's matching logic reads `metadata`
JSONB to filter candidates. Promoting regions to first-class means the key schema must change, but
changing the key schema in a live system without a migration plan orphans existing tickets.

**How to avoid:**
- Adopt the key pattern: `gamekit:matchmaking:queue:<region>:<ladder>` as the authoritative sorted-set
  key. Region `__global` is the default (no regional pool configured), preserving v1 key semantics
  for operators who don't configure regions.
- Provide an explicit migration path in the ops guide: drain all tickets (admin API), update config,
  restart. Include a startup validation: if any `gamekit:matchmaking:queue:<ladder>` key (without
  region segment) exists AND regional pools are configured, log `WARNING: legacy queue key detected;
  tickets are in the global pool`.
- The `MatchmakingLadderConfig` gains a `Regions` list. The matchmaking ticker iterates each `(region, ladder)`
  pair as an independent matching pass.
- Implement cross-region fallback (operator-configurable): if `gamekit:matchmaking:queue:<region>:<ladder>`
  has fewer than `CrossRegionFallbackThreshold` players after `CrossRegionFallbackSeconds`, expand to
  adjacent regions in order. This prevents empty-pool starvation in low-population regions.

**Warning signs:**
- `us-east` players match with `eu-west` players consistently (ping complaints; logs show mixed-region
  `player_id` sets in completed sessions).
- `gamekit:matchmaking:queue:eu-west:main` key exists but `gamekit:matchmaking:queue:main` (v1 key)
  still has enqueued tickets after config migration.
- Testcontainers Redis `KEYS gamekit:matchmaking:queue:*` returns both old and new key patterns.

**Phase to address:**
Regional matchmaking phase. The key migration helper (drain + re-key) must be implemented and tested
before any regional configuration ships. Include a startup warning log for legacy keys.

---

### Pitfall 11: EF Core Migration Boundary Violation — New v2 Package Modifies a Core Table

**What goes wrong:**
A v2 package (e.g., account merge or regional pools) adds a column to `gamekit.players` (a Core-owned
table) in its migration because that is the most natural place. This violates the GameKit migration
boundary invariant: only `GameKit.Core` owns `gamekit.players`. If the column is added in the v2
package's migration, the Core's `PlayerConfiguration` EF mapping does not know about it, causing
EF to attempt to DROP the column on the next Core migration. On rollback, the v2 package's migration
runs `DropColumn` on the Core table, but the Core package's history table has no record of it.

**Why it happens:**
EF Core's design-time factory for a package only runs the *owning* package's model configurations
(plus `ExcludeFromMigrations` for others). If the developer adds a navigation property or column on
the `Player` entity *inside* `GameKit.Core` to support the new feature, the Core design-time factory
will see it and generate a Core migration — which is correct. The violation happens when they add it
to the *new package's* migration instead.

**How to avoid:**
- The rule is simple and absolute: if a new v2 feature needs a column on a Core entity (`Player`,
  `PlayerIdentity`, `PlayerCredential`, `GameSession`, `GameSessionParticipant`), that column goes
  into a new `GameKit.Core` migration with timestamp beyond the v1 final timestamp. The new package
  then declares a FK reference or reads the column via the existing EF Core model.
- Permitted for new packages: creating entirely NEW tables with an FK into `gamekit.players(id)`.
  Not permitted: `ALTER TABLE gamekit.players ADD COLUMN`.
- Add a CI check in the `MigrationBoundaryTests`: run `dotnet ef migrations script` for each package
  and assert the generated SQL contains no DDL on tables prefixed with another package's namespace
  (e.g., the Lobby migration script must not contain `ALTER TABLE gamekit.players`).
- The `MatchmakingMigrationModelCustomizer` pattern of enumerating prior-package entity types via
  `typeof()` + `ExcludeFromMigrations` must be extended to include any v2 Core entities added for v2.

**Warning signs:**
- EF `dotnet ef migrations script` for a non-Core package generates `ALTER TABLE gamekit.players`.
- The Core package's `dotnet ef migrations add` detects a pending model change after a v2 package
  is installed.
- Production `dotnet database update` throws `column already exists` or `column does not exist`.

**Phase to address:**
Every v2 phase that needs schema changes. The migration boundary CI check should be added in the
first v2 phase that touches schema (likely Auth Argon2 or Account Merge).

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Apple `.p8` key stored in `appsettings.json` (plaintext) | Simple local dev setup | Key exposed in logs, version control, process dump | Never in production; use env var or secrets manager |
| Skip `account_merge_log` idempotency table, wrap merge in one transaction | Less schema | Corrupted state on crash mid-long-tx; no resume path | Never; this table is 5 columns and required |
| Generate Argon2 hasher advisory-lock key by guessing instead of live-verifying | Saves 2 minutes | Possible collision with existing packages causing startup deadlock | Never; always run `SELECT hashtext(...)::bigint` in Testcontainers |
| Re-use `gamekit:matchmaking:matcher:lock` Redis key for decay background service | One less constant | Decay and matchmaking mutually exclude; matchmaking starves decay under load | Never; use a dedicated decay lock key |
| Single SignalR hub for both Lobby and Admin health broadcast | Fewer files | Admin actions (player bans, rank adjusts) accessible to lobby-authenticated players | Never; admin and player hubs must be separate with distinct `[Authorize]` policies |
| Store `ParticipationFraction` as computed column at query time (not persisted) | Avoids migration | Cannot index; cannot query for audit; recalculated per request | Never for audit-critical data; persist it |
| Use `metadata` JSONB for regional queue assignment instead of first-class `Regions` column | No migration needed | v1 ticker reads `metadata` with no index; cross-region fallback impossible to implement cleanly | v1 only (already done); v2 must promote to first-class |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Apple Sign In (aspnet-contrib) | Caching the generated ES256 JWT client secret across requests | Set `GenerateClientSecret = true`; the provider regenerates per exchange |
| Apple Sign In | Using `email` claim as identity key | Use `sub` (User Identifier) — email is a relay address that can change |
| Epic Games OAuth | Assuming email is available for identity linking | Epic Games blocks email access; linking must use `sub` only; do NOT offer "merge by email" for Epic accounts |
| Isopoh Argon2 (rehash-on-verify) | Forgetting to update the `password_hash` column after rehash | Rehash must `UPDATE player_credentials SET password_hash = newHash` inside the same request's transaction as the login |
| SignalR + Redis backplane | No channel prefix set | All apps sharing the Redis instance receive each other's hub messages; set `ChannelPrefix = "gamekit"` |
| SignalR + load balancer | Round-robin LB without sticky sessions | Negotiate and WebSocket upgrade land on different replicas; use IP-hash or cookie-based sticky sessions |
| EF Core per-package migrations | Using `int` (not `long`) to call `pg_advisory_lock` | Postgres `pg_advisory_lock` requires `bigint`; use `long` in .NET; `hashtext()` returns `int4` which must be cast `::bigint` |
| Google OAuth | Google ID tokens use `sub` (numeric string) as subject — do not confuse with email for identity key | Store `external_id = sub`; do not rely on email which can change if user changes Google account |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Argon2 memory tuning under concurrent login burst | CPU spike + `OutOfMemoryException` under load; login P99 > 5s | Tune `MemorySize` to `max(64MB, (total_mem * 0.1) / max_concurrent_logins)` | At > 50 concurrent Argon2 verifications (each requiring 64MB+ RAM) |
| Backfill session join spawns a full matchmaking re-enqueue | Matchmaking queue depth spikes on session abandonment waves | Backfill must NOT re-enqueue via the normal `EnqueueAsync` path; use a dedicated `BackfillTicket` type that bypasses the rate-limit partition | At > 10 concurrent abandonments per second |
| Rank decay `BackgroundService` full-table scan without index on `last_played_at` | Decay job takes minutes, blocks concurrent EF model builds | Index `(ladder_id, last_played_at)` on `player_ranks`; add index in the decay migration | At > 100k ranked players per ladder |
| SignalR Redis backplane with large message payloads (chat history) | Redis pub/sub message size grows; backplane latency spikes | Cap lobby chat message payload at 4KB; store chat history in Postgres, not Redis; Redis is event bus only | At > 100 active lobby connections with chat enabled |
| `ErrorRateRingBuffer` per replica (no aggregation) | Admin health panel shows one replica's error rate, not cluster total | Ring buffer is correct for single-replica; multi-replica requires Redis-backed aggregation or acknowledgment that the panel shows per-instance stats | At > 1 replica |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Expose `source_player_id` in account merge API response | Enumerates deleted player IDs; GDPR data exposure | Return only `target_player_id` in merge response; source is soft-deleted (or tombstoned) |
| Apple `.p8` private key in deploy artifact / Docker image | Key compromise allows impersonating any Apple user | Load from secrets manager or mounted volume; never in image layer |
| SignalR Lobby hub accessible without `[Authorize]` | Unauthenticated clients can send chat messages or trigger ready-checks | Hub class must have `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`; verify in integration test that unauthenticated WS upgrade returns 401 |
| CSRF token not validated on admin SignalR connection upgrade | Lateral movement from a lobby-authenticated player session | Admin hub must use the `GameKitAdmin` cookie scheme with antiforgery validation; player JWT scheme must not authenticate to the admin hub (mirrors v1 `CrossSchemeIsolationTests`) |
| Account merge without superadmin authorization | Player triggers merge of other players' accounts; mass data corruption | `POST /admin/api/merge` must require `gamekit.admin.superadmin` policy, not player JWT |
| Placement match result stored before game server confirms | Client can claim a win without server validation | `SessionCompleteService` must always be called by the game server (server-authoritative); never accept session results from a client endpoint |

---

## "Looks Done But Isn't" Checklist

- [ ] **Argon2 hasher:** `IPasswordHasher` registered, but v1 BCrypt hashes NOT rehashed on verify — active users still use BCrypt until each logs in. Verify by checking `password_hash` column values: BCrypt hashes start with `$2a$`; Argon2id hashes with `$argon2id$`. After 30 days, confirm at least 30% of active-user hashes have been migrated.
- [ ] **Apple Sign In:** Provider wired and login works locally, but `.p8` key has not been rotated in > 5 months. Verify `ClientSecretExpiresAfter` is set and < 180 days; verify ops runbook documents rotation.
- [ ] **Account merge:** Postgres transaction commits, but `account_merge_log.status` stuck at `committed` (Redis cleanup never ran). Verify `status = redis_cleaned` on all completed merges.
- [ ] **Regional pools:** Config shows regions, but Redis still has legacy `gamekit:matchmaking:queue:<ladder>` keys (without region segment). Verify via `KEYS gamekit:matchmaking:queue:*` that all live keys include a region segment.
- [ ] **Rank decay:** Decay job runs, but `RankAdjustAudit` table shows no `reason = "decay"` rows. Verify the decay service writes audit rows via the `RankAdjustService` (not silently updating `player_ranks` directly).
- [ ] **Placement matches:** `is_placed = true` for new players who never played a placement match. Verify `is_placed` is only set by the `SessionCompleteService` after `PlacementMatchCount` games, not at account creation.
- [ ] **Admin multi-replica:** Admin panel appears to show cluster health, but is actually showing one replica's ring buffer. Verify by killing one replica and confirming the panel reflects the change (or correctly discloses "per-instance view").
- [ ] **SignalR backplane:** Hub messages route correctly in single-instance tests, but `ChannelPrefix` is unset. Verify `options.Configuration.ChannelPrefix` is set to `"gamekit"` in `AddStackExchangeRedis`.
- [ ] **Advisory lock keys:** New `GameKit.Lobby` package has a placeholder `0L` advisory key. Verify by running the pairwise-distinctness integration test (mirrors `MatchmakingAdvisoryLockKeyTests`).
- [ ] **Migration boundary:** New v2 migration generates DDL against a Core-owned table. Verify by running `dotnet ef migrations script` for each new package and asserting no `ALTER TABLE gamekit.players` in output.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Apple client secret expired in production | LOW (if key is available) | Generate new ES256 JWT from `.p8` key; update config; rolling restart; no data loss |
| Apple client secret expired + `.p8` key lost | HIGH | Generate new `.p8` in Apple Developer Portal (invalidates old key); update config; restart; existing Apple-linked accounts remain valid (stored `sub` is stable); new logins work immediately |
| Account merge partial (Postgres committed, Redis dirty) | MEDIUM | Use `account_merge_log` to identify `status = committed` rows; run the Redis cleanup idempotently for each; mark `status = redis_cleaned` |
| Advisory lock key collision (startup hang) | MEDIUM | Kill one service instance; fix the key constant + live-verify; redeploy |
| Rating feedback loop already in production (new players churning) | MEDIUM | Enable `MaxBracketWidth` cap immediately (operator config, no deploy needed if option is wired); reset RD for high-RD players via admin rank-adjust API |
| Migration boundary violation (column added to Core table by non-Core package) | HIGH | Write a compensating migration in Core that takes ownership of the column; remove from non-Core package's migration history; requires coordinated deploy with Core version bump |
| SignalR backplane Redis outage (messages lost) | LOW (transient) | SignalR does not buffer; clients reconnect automatically; in-flight lobby state is lost but game state is Postgres-durable; document this as accepted behavior in ops guide |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Apple client-secret ES256 expiry | Auth providers (Google/Apple/Epic OAuth) | Integration test: assert `GenerateClientSecret = true` + `ClientSecretExpiresAfter < 180d` |
| Apple private relay identity collision | Auth providers | Integration test: two Apple logins with same `sub`, different relay email → same `player_id` |
| Account merge FK corruption and partial-merge | Account merge phase | Integration test: kill process mid-merge, verify `account_merge_log` resume |
| Advisory lock key collision (new v2 packages) | First plan of every new package wave | Pairwise-distinctness test (`typeof()`-based exclusion matrix) RED at wave start, GREEN after live-verify |
| SignalR sticky sessions | Lobby phase (SignalR hub) AND Admin multi-replica phase | Two-`TestServer` integration test; sticky-session requirement in ops guide |
| Rating feedback loop | Rating-aware matchmaking phase | Unit test: bracket expansion stops at `MaxBracketWidth`; pool-depth guard prevents immediate expansion |
| Rank decay double-penalizes absence | Rank decay phase | Unit test: absent player loses RD only, not rating |
| Placement match smurf exploit | Placement matches phase | Integration test: `win_rate > 0.9` placement triggers admin audit row; rated-ladder gates on `is_placed = true` |
| Backfill low-participation penalty | Backfill phase | Integration test: backfill player with `ParticipationFraction < 0.5` has `rating_delta = 0` |
| Regional pool key leakage | Regional matchmaking phase | `KEYS gamekit:matchmaking:queue:*` assertion; startup warning log test for legacy keys |
| Migration boundary violation | Every new-package phase | CI: `dotnet ef migrations script` output contains no DDL on prior-package tables |
| Epic Games email unavailable for linking | Auth providers | Smoke test: Epic `OnCreatingTicket` uses `sub` only; no email-based merge path offered |

---

## Sources

- [aspnet-contrib Sign in with Apple docs](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/blob/dev/docs/sign-in-with-apple.md) — HIGH (official provider docs, per-request secret generation, `UsePrivateKey()` API)
- [Apple Developer: Creating a client secret](https://developer.apple.com/documentation/accountorganizationaldatasharing/creating-a-client-secret) — HIGH (official, ES256 JWT constraint, 6-month max)
- [Scott Brady: Sign in with Apple in ASP.NET Core](https://www.scottbrady.io/openid-connect/implementing-sign-in-with-apple-in-aspnet-core) — MEDIUM (authoritative implementer guide)
- [ASO.dev: Sign In with Apple Private Relay Issue](https://aso.dev/blog/apple-sign-in/) — MEDIUM (May 2025 userIdentifier stability incident documented)
- [Microsoft Learn: SignalR scale + Redis backplane](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0) — HIGH (sticky-session requirement, channel prefix, Redis outage behavior)
- [Microsoft Learn: SignalR Redis backplane](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0) — HIGH (channel prefix mandatory, message loss on Redis outage)
- [Milan Jovanović: Scaling SignalR with Redis Backplane](https://www.milanjovanovic.tech/blog/scaling-signalr-with-redis-backplane) — MEDIUM (practical scale-out pattern)
- [gpluscb: So You Want to Use Glicko-2 for Your Game's Ratings](https://gist.github.com/gpluscb/302d6b71a8d0fe9f4350d45bc828f802) — HIGH (rating period constraints, counter-intuitive win loss, fractional period solution)
- [DEV Community: Migrating existing code to a new password hashing algorithm](https://dev.to/rsa/migrating-existing-code-to-a-new-password-hashing-algorithm-43n5) — MEDIUM (rehash-on-verify live migration pattern)
- [GitHub: ranisalt/node-argon2 — Migrating from another hash function](https://github.com/ranisalt/node-argon2/wiki/Migrating-from-another-hash-function) — MEDIUM (wrapping legacy hashes, sunset window strategy)
- [EOS Help: Epic Games OAuth integration invalid_client error](https://eoshelp.epicgames.com/s/article/When-integrating-OpenID-Connect-with-Epic-Account-Services-what-can-cause-the-invalid-client-error-when-exchanging-the-authorization-code) — MEDIUM (Epic-specific OAuth gotchas)
- [GameKit v1.0 STATE.md: advisory lock keys verified live](/.planning/STATE.md) — HIGH (primary source; five existing keys; Postgres 17.9 live-verified)
- [GameKit v1.0-MILESTONE-AUDIT.md: Matchmaking→Rankings seam warning](/.planning/v1.0-MILESTONE-AUDIT.md) — HIGH (primary source; EloRange runs on rating=0 tech debt)
- [EF Core issue #34439: migration lock mechanism](https://github.com/dotnet/efcore/issues/34439) — MEDIUM (advisory lock behavior in EF Core 9+, transaction interaction)

---

*Pitfalls research for: GameKit v2.0 — adding Argon2, Apple/Google/Epic OAuth, account merge, rating-aware matchmaking, rank decay, placement matches, backfill, regional pools, GameKit.Lobby + SignalR backplane, multi-replica Admin UI to a mature .NET 10 game-services library*
*Researched: 2026-06-05*
