# Architecture Patterns

**Domain:** v2.0 integration design — adding providers, lobby, rating-aware matchmaking, and multi-replica admin to an existing mature GameKit v1.0 codebase.
**Researched:** 2026-06-05
**Confidence:** HIGH — all claims grounded in actual source files; paths cited throughout.

---

## Existing Architecture (verified from code)

The v1.0 codebase follows a strict set of patterns that v2 must obey.

### Package dependency graph (current)

```
GameKit.Core
    └─ GameKit.Auth           (ProjectReference → Core)
        └─ GameKit.Admin.UI   (ProjectReference → Auth + Core)
            └─ GameKit.Rankings  (ProjectReference → Core; Admin ProjectRef for design-time only)
                └─ GameKit.Matchmaking (ProjectReference → Core + Rankings; Auth + Admin for design-time boundary only)
                    └─ GameKit.Presence  (ProjectReference → Core only; registers against IPresenceProvider in Core)
GameKit.OpenApi  (thin docs; no runtime deps on sibling packages)
GameKit.Cli
```

Key constraint: the `→` direction is only ever DOWN this list (Core is at the root). No package has a back-reference to a downstream package at runtime. The design-time-only `ProjectReference` annotations (Matchmaking → Auth/Admin.UI) exist exclusively for the `typeof()` exclusion list in `MatchmakingMigrationModelCustomizer` — they carry zero runtime coupling.

### Per-package migration pattern (locked)

Source: `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs`, `src/GameKit.Auth/Data/AuthMigrationConstants.cs`, STATE.md locked decisions.

Every package that owns persistent state must have:

| Artifact | Naming convention | Advisory lock keys (all live-verified) |
|---|---|---|
| `{Pkg}MigrationConstants` | `__ef_migrations_{pkg}` history table | Core = 1800940027 |
| `{Pkg}MigrationModelCustomizer` | ExcludeFromMigrations all prior-package entities | Auth = -298890956 |
| `{Pkg}DesignTimeDbContextFactory` | applies only own entities | Admin = -2101739634 |
| `{Pkg}MigrationHostedService` | acquires advisory lock at IHost.StartAsync | Rankings = -156812172 |

Matchmaking = 388956820. Presence = not yet verified (Phase 6 shipped it; no advisory key in code — see `src/GameKit.Presence/Builder/PresenceBuilderExtensions.cs` which has no migration hosted service, confirming Presence is stateless in v1).

Packages that own no Postgres tables (Presence, OpenApi, Cli, Auth.Argon2) need NO migration machinery.

### Pluggable-strategy seam (Scrutor)

Source: `src/GameKit.Core/Data/IModelBuilderExtension.cs`, `src/GameKit.Auth/Providers/IOAuthProvider.cs`, `src/GameKit.Auth/Services/IPasswordHasher.cs`.

All strategy-interface implementations are auto-discovered via Scrutor's `publicOnly: false` assembly scan in each package's `Add*()` extension. The Core defines the interface; sibling packages implement it. Customers can also add their own implementations to their own assembly and Scrutor finds them.

### Background-service + Redis leader-election pattern (locked)

Source: `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` lines 65-132.

`BackgroundService` + `PeriodicTimer` + Polly v8 retry + `SET NX PX` Redis distributed lock. The ticker writes a heartbeat key with TTL 5× the tick interval. The same pattern is required for any v2 background job that must run on exactly one replica.

### Optional-port / null-object-default pattern (locked)

Source: `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs`, `src/GameKit.Core/Services/IPresenceProvider.cs`, STATE.md line 209 ("Optional port injection via factory lambda (GetService<T>) for IPostSessionCompleteHandler, IIdempotencyStore, ICanonicalRequestHasher — Core operates in degraded mode when Rankings not installed").

When Core (or Matchmaking) needs to call an upstream package without a hard dep, it defines an interface in Core and uses `GetService<T>` (nullable resolution) with a null-object fallback. This is the approved seam pattern.

---

## Question 1: Rankings → Matchmaking Rating Seam

### Problem (grounded in code)

In `src/GameKit.Matchmaking/Services/MatchmakingService.cs` lines 201-204:
```csharp
var queuedMembers = memberPlayerIds
    .Select(pid => new QueuedPartyMember(pid, Rating: 0, RatingDeviation: 0, Volatility: 0))
    .ToList();
```

All members get `Rating: 0`. This zero-rating is written into the Redis ticket hash at `aggregateRating` and is what `EloRangeMatchmakingStrategy` uses for bracket comparisons. The `QueuedPartyMember` record in `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` already carries the three Glicko-2 fields — the plumbing exists; the source is missing.

Matchmaking has a compile-time `ProjectReference → Rankings` (for migration boundary only), so there is NO runtime Matchmaking→Rankings hard dep today. The v1 comment ("v1 reads zero-rated members from the party") explicitly marks this as tech debt.

### Proposed interface: `IPlayerRatingProvider` in `GameKit.Core`

Place this in `src/GameKit.Core/Services/IPlayerRatingProvider.cs`.

```csharp
/// <summary>
/// Optional port: returns Glicko-2 rating data for a set of players on a given ladder.
/// Implemented by <c>GameKit.Rankings</c>. When not installed, Matchmaking resolves null
/// and uses zero-rated defaults (preserving v1 behaviour).
/// </summary>
public interface IPlayerRatingProvider
{
    /// <summary>
    /// Returns rating snapshots for <paramref name="playerIds"/> on <paramref name="ladderId"/>.
    /// Players with no existing rank row are returned with default Glicko-2 values
    /// (rating = 1500.0, RD = 350.0, volatility = 0.06 — the Glicko-2 standard defaults).
    /// </summary>
    ValueTask<IReadOnlyDictionary<Guid, PlayerRatingSnapshot>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default);
}

/// <summary>
/// Glicko-2 rating snapshot for a single player. Mirrors <c>PlayerRank</c> from
/// <c>GameKit.Rankings</c> without creating a cross-package entity dependency.
/// </summary>
public sealed record PlayerRatingSnapshot(
    Guid PlayerId,
    double Rating,
    double RatingDeviation,
    double Volatility);
```

The `PlayerRankingsProvider : IPlayerRatingProvider` implementation lives in `src/GameKit.Rankings/`, performs a batched `SELECT` against `player_ranks WHERE player_id = ANY(@ids) AND ladder_id = @ladderId`, and is registered in `AddRankings()` as:

```csharp
services.TryAddSingleton<IPlayerRatingProvider, PlayerRankingsProvider>();
```

### Where MatchmakingService reads it

Source read point: `EnqueueAsync` in `src/GameKit.Matchmaking/Services/MatchmakingService.cs` lines 200-217 (the `queuedMembers` construction).

`IPlayerRatingProvider?` is injected as a nullable optional dep via constructor parameter with a default of `null`:

```csharp
public MatchmakingService(
    ...existing params...,
    IPlayerRatingProvider? ratingProvider = null)
```

Then in `EnqueueAsync` Step 4, replace the zero-fill:

```csharp
IReadOnlyDictionary<Guid, PlayerRatingSnapshot> ratings =
    ratingProvider is not null
        ? await ratingProvider.GetRatingsAsync(memberPlayerIds, ladderId, ct)
        : ImmutableDictionary<Guid, PlayerRatingSnapshot>.Empty;

var queuedMembers = memberPlayerIds.Select(pid =>
{
    ratings.TryGetValue(pid, out var r);
    return new QueuedPartyMember(
        pid,
        Rating: r?.Rating ?? 0,
        RatingDeviation: r?.RatingDeviation ?? 0,
        Volatility: r?.Volatility ?? 0);
}).ToList();
```

This is the ONLY code change in `MatchmakingService` — the existing `aggregateRating` computation, Redis HSET, and spread-cap logic work unchanged.

### Redis ticket hash caching

The `aggregateRating` field is already written to `mm:ticket:{id}` at enqueue time (line 276 in `MatchmakingService.cs`). With real ratings flowing in, this cached value is correct at enqueue time. For long-waiting tickets the cache goes stale — this is documented and accepted (comment in `QueuedParty.cs` lines 19-21: "cache may be stale by up to one ratings period for long-waiting tickets").

Per-member ratings are also serialized as JSON into the `members` hash field (line 265-276 in `MatchmakingService.cs`). With real ratings, `QueuedPartyMember.Rating/RatingDeviation/Volatility` will be non-zero, enabling `PartyRatingAggregator.GlickoWeighted` to work correctly for the first time.

The ticker (`MatchmakerTickerService.BuildQueuedPartyFromHash`) reads both `aggregateRating` and `members` from the hash — no ticker changes needed. The existing `QueuedParty` and `EloRangeMatchmakingStrategy` are already rating-aware; they just received zeros in v1.

### Package independence preserved

- `GameKit.Core` defines `IPlayerRatingProvider` — no new deps.
- `GameKit.Rankings` implements it — no new deps (it already owns `player_ranks`).
- `GameKit.Matchmaking` injects it as `?` optional — no new compile-time dep on Rankings beyond the design-time boundary that already exists.
- A consumer who installs only Matchmaking without Rankings gets the v1 zero-rating behaviour silently.

---

## Question 2: New Package Integration

### GameKit.Auth.Argon2

**Purpose:** `Argon2idPasswordHasher : IPasswordHasher` using Isopoh.Cryptography.Argon2.

**Migration:** NONE. This package is stateless — it provides only an `IPasswordHasher` implementation. No new Postgres tables. No migration hosted service. No advisory lock.

**Integration point:** `IPasswordHasher` is defined in `src/GameKit.Auth/Services/IPasswordHasher.cs`. `BCryptPasswordHasher` is the default. `Argon2idPasswordHasher` replaces it. The consumer opts in by calling `AddArgon2()` (or `AddAuth().UseArgon2()`) which does `services.Replace(ServiceDescriptor.Singleton<IPasswordHasher, Argon2idPasswordHasher>())`. No other code changes.

**ProjectReference:** `GameKit.Auth.Argon2 → GameKit.Auth` (for the interface). No dep on Core, Rankings, Matchmaking.

**Build order:** Any phase after Phase 2 (Auth is already shipped). Independent of all other v2 work. Goes first because it is the simplest.

---

### GameKit.Auth.Google / .Apple / .Epic (OAuth providers)

**Purpose:** Each provides an `IOAuthProvider` implementation using `aspnet-contrib` `AuthenticationBuilder` handlers.

**Migration:** NONE. These packages are stateless — they register authentication schemes and implement `IOAuthProvider`. No new Postgres tables. The `player_identities` table already stores any provider's identity; the `Provider` discriminator column accepts new string values. No schema change needed for new providers.

**Integration point:** `IOAuthProvider` is defined in `src/GameKit.Auth/Providers/IOAuthProvider.cs`. `DiscordOAuthProvider` in `src/GameKit.Auth/Providers/` is the reference implementation pattern. Each new provider package registers its `IOAuthProvider` implementation via Scrutor scan in `AddAuth()` (existing scan picks up any `IOAuthProvider` in any assembly). The aspnet-contrib handler is conditionally registered (like Discord in STATE.md line 147: "Discord authentication scheme registered conditionally only when ClientId+ClientSecret both supplied").

**ProjectReference:** `GameKit.Auth.Google/Apple/Epic → GameKit.Auth` only.

**Build order:** After Auth.Argon2 (though independent in practice). All three provider packages can be built in parallel.

**Asymmetric consideration for Apple:** Apple Sign-In uses a JWT-based identity token (not OAuth2 code flow) and requires a `p8` private key. The `IOAuthProvider.CompleteLoginAsync` contract is the right abstraction level, but the "challenge" step (generating an authorization URL) differs from Discord/Google. The provider package must also expose a `/auth/challenge/apple` endpoint that generates the Apple URL with the correct `response_type=code` + `scope=name email`. This is contained entirely within the provider package — no Core or Auth changes.

---

### GameKit.Lobby

**Does it need its own migration?** YES. Lobby introduces new Postgres tables (`lobbies`, `lobby_members`). It follows the per-package migration pattern exactly.

**Advisory lock key:** Needs live-verification via `SELECT hashtext('gamekit.lobby.migrations')::bigint` in Testcontainers. Placeholder until verified.

**Migration model customizer exclusion list:** All six prior packages: Core (4 entities) + Auth (3) + Admin (1) + Rankings (7) + Matchmaking (5) + Presence (0, stateless) = 20 entity types to exclude.

**Migration timestamp:** Use deterministic `20260520000000_LobbyInitial` (one day after Matchmaking's `20260516000000`).

---

## Question 3: Account Merge

### Package ownership

Account merge lives in **`GameKit.Auth`**. Rationale: the `player_identities`, `player_credentials`, and `refresh_tokens` tables are Auth-owned entities. The merge operation needs to re-point all of them from the source player to the target player. The merge also touches `players` (Core-owned) and cross-package tables, which is why it is the highest-risk operation.

### Data model

No new table is strictly required for the merge operation itself, but an `account_merges` table (owned by Auth) should record merge history for audit and idempotency.

```sql
-- Auth package migration
CREATE TABLE gamekit.account_merges (
    id         uuid PRIMARY KEY,
    source_player_id uuid NOT NULL,   -- player that was absorbed (may be deleted)
    target_player_id uuid NOT NULL REFERENCES gamekit.players(id) ON DELETE RESTRICT,
    merged_at  timestamptz NOT NULL,
    actor_id   uuid,                  -- admin who triggered or NULL for self-service
    metadata   jsonb
);
CREATE INDEX idx_account_merges_source ON gamekit.account_merges (source_player_id);
CREATE INDEX idx_account_merges_target ON gamekit.account_merges (target_player_id);
```

### FK references that must be re-pointed (from code inspection)

| Table | Owner package | FK column | Action |
|---|---|---|---|
| `player_identities` | Auth | `player_id` | UPDATE SET player_id = target WHERE player_id = source |
| `player_credentials` | Auth | `player_id` | UPDATE SET player_id = target WHERE player_id = source |
| `refresh_tokens` | Auth | `player_id` | REVOKE ALL (DELETE WHERE player_id = source — tokens for absorbed account must be invalidated) |
| `game_sessions` | Core | session rows use participant join, not direct player FK | no change needed |
| `session_participants` | Core | `player_id` | UPDATE SET player_id = target WHERE player_id = source (or leave if already same ladder) |
| `player_ranks` | Rankings | `player_id` | MERGE STRATEGY (see below) |
| `admin_audit_log` | Core | `actor_id` | UPDATE SET actor_id = target WHERE actor_id = source |
| `matchmaking_tickets` | Matchmaking | no direct player FK (party-based) | no direct change; stale Redis tickets expire |
| `party_members` | Matchmaking | `player_id` | UPDATE SET player_id = target WHERE player_id = source (unique constraint on player_id per party — may conflict) |
| `account_merges` | Auth | `source_player_id`, `target_player_id` | check for cycles |

**`player_ranks` merge strategy:** Both players may have a rank row on the same ladder. Cannot simply re-point the FK — there is a `UNIQUE(player_id, ladder_id)` constraint. Options:

1. **Keep higher-rated rank** (recommended): UPDATE the source row to point to target only if `source.Rating > target.Rating` on each ladder; otherwise DELETE the source row. Write audit trail before delete.
2. **Weighted average**: combine ratings using Glicko-2 mean (requires Rankings knowledge inside Auth — violates package boundaries).
3. **Defer to operator**: expose a `IRankMergeStrategy` interface with a default of "keep-higher" and an "keep-target" alternative.

Option 1 (keep-higher) is the correct default. It is computable without calling into Rankings — Auth only needs to read the two `double` rating columns and compare. The query `SELECT * FROM gamekit.player_ranks WHERE player_id IN (source, target) AND ladder_id = x` is readable from Auth via the shared `GameKitDbContext` (same DbContext, Auth has access to all tables via EF model).

### Transaction design

```
BEGIN ISOLATION LEVEL SERIALIZABLE;
-- 1. Lock both player rows in ID order (prevent deadlock)
SELECT id FROM gamekit.players WHERE id IN (source, target) ORDER BY id FOR UPDATE;
-- 2. Verify source is not already merged (idempotency check via account_merges)
SELECT id FROM gamekit.account_merges WHERE source_player_id = source AND target_player_id = target LIMIT 1;
-- return already_merged if found
-- 3. Verify neither player is banned (or caller chose to merge anyway)
-- 4. Re-point player_identities
UPDATE gamekit.player_identities SET player_id = target WHERE player_id = source;
-- UNIQUE(provider, external_id) is on the identity itself, not player_id, so no conflict
-- 5. Re-point player_credentials
UPDATE gamekit.player_credentials SET player_id = target WHERE player_id = source;
-- UNIQUE(username) is on username, not player_id, so no conflict
-- 6. Revoke all refresh tokens for source player
DELETE FROM gamekit.refresh_tokens WHERE player_id = source;
-- 7. Merge player_ranks (per-ladder: keep higher)
-- For each ladder where both have a row: keep the higher-rated one
-- (handled in application code with N small queries or a single CTE)
-- 8. Re-point session_participants
UPDATE gamekit.session_participants SET player_id = target WHERE player_id = source;
-- UNIQUE(session_id, player_id) may conflict if both players were in the same session
-- Conflict = violation of business invariant; abort merge
-- 9. Re-point admin_audit_log.actor_id (optional — keeps history coherent)
UPDATE gamekit.admin_audit_log SET actor_id = target WHERE actor_id = source;
-- 10. Delete the source player row (CASCADE deletes any remaining orphans)
DELETE FROM gamekit.players WHERE id = source;
-- 11. Insert account_merges record
INSERT INTO gamekit.account_merges ...;
-- 12. Insert admin_audit_log record
INSERT INTO gamekit.admin_audit_log (action='auth.account_merge', target_id=target, before=..., after=...) ...;
COMMIT;
```

**Isolation level:** SERIALIZABLE. The source and target players must be locked together to prevent concurrent merges racing. Lock acquisition in ID order prevents deadlock. Retry on `40001` (serialization failure) — reuse the existing `SerializationFailureRetry` pattern from `src/GameKit.Rankings/Services/SerializationFailureRetry.cs`.

**Idempotency:** Check `account_merges` for an existing `(source, target)` row before proceeding. Return `MergeResult.AlreadyMerged` if found. This makes the operation safe to retry.

**Admin audit log:** The `admin_audit_log` entity is defined in `src/GameKit.Core/Entities/AdminAuditLog.cs`. Auth writes to it directly via `_ctx.Set<AdminAuditLog>()` (same pattern as `EndSeasonService` in STATE.md line 211 — not via `IAdminAuditWriter` to avoid circular dependency). Action literal: `"auth.account_merge"`.

**`IAccountMergeService` interface** lives in `GameKit.Auth`. Exposed on the `IGameKitAuthBuilder` fluent builder. The Admin UI calls it via `IAccountMergeService` injected into an admin Blazor component or admin API endpoint.

---

## Question 4: Regional Pools

### Region as Matchmaking-local concept

Region does NOT need to be a Core concept. The existing `PoolName` field on `MatchmakingTicket` (`src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` line 52) is already designed for this: "Multiple pools per ladder support region affinity or game-mode segmentation". The PoolName is used as the queue key suffix: `mm:queue:{ladderId}:{poolName}` (verified in `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs`).

### What "first-class" means vs. v1 escape hatch

v1 used `metadata.region` as an unstructured escape hatch with no validation, no pool routing, and no queue partitioning. v2 makes region first-class by:

1. Adding `AllowedRegions IReadOnlyList<string>` to `MatchmakingLadderConfig` (in `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs`). Empty = all regions allowed (backwards-compatible default).
2. Adding `RegionName string?` to the enqueue request DTO. Validated against `AllowedRegions` at enqueue. Defaults to `null` → maps to pool `"default"` (existing v1 behaviour).
3. When `RegionName` is non-null, `MatchmakingService.EnqueueAsync` uses `poolName = $"{regionName}"` (or `"{ladderName}:{regionName}"` — operator's naming convention) rather than `"default"`.
4. The Redis queue key becomes `mm:queue:{ladderId}:{regionName}` — the ticker's SCAN glob `mm:queue:*:{poolName}` already handles this by scanning per-ladder.

**No schema migration needed.** `PoolName` is already a Postgres column (varchar in `matchmaking_tickets`). Region is just a poolName value. The ticker's existing `server.Keys(pattern: poolGlob)` scan in `MatchmakerTickerService.ProcessPoolAsync` already iterates multiple pools per ladder.

**Cross-region fallback:** When a player's regional pool has no matching ticket after the bracket reaches `BracketEnd`, the ticker could optionally promote the ticket to a `"global"` pool. This is an operator config choice (`MaxWaitBeforeGlobalFallbackSeconds int?` on `MatchmakingLadderConfig`, null = no fallback). Implemented entirely within `MatchmakingService.EnqueueAsync` (enqueue into both regional and global pools simultaneously, or re-enqueue after timeout — latter is cleaner, matches the reconciler sweep pattern).

---

## Question 5: GameKit.Lobby

### Entity model

```
lobbies
├── id             uuid PK
├── name           text NOT NULL
├── owner_id       uuid FK → players(id) ON DELETE SET NULL
├── ladder_id      uuid FK → ladders(id) ON DELETE SET NULL  (optional)
├── state          integer  (Open=0, ReadyChecking=1, Closed=2, InGame=3)
├── max_members    integer NOT NULL DEFAULT 10
├── created_at     timestamptz
├── updated_at     timestamptz
└── metadata       jsonb

lobby_members
├── id             uuid PK
├── lobby_id       uuid FK → lobbies(id) ON DELETE CASCADE
├── player_id      uuid FK → players(id) ON DELETE CASCADE
├── ready          boolean NOT NULL DEFAULT false
├── joined_at      timestamptz
└── UNIQUE(lobby_id, player_id)

lobby_messages  (in-lobby chat — PERSISTED, not ephemeral)
├── id             uuid PK
├── lobby_id       uuid FK → lobbies(id) ON DELETE CASCADE
├── sender_id      uuid FK → players(id) ON DELETE SET NULL
├── body           text NOT NULL CHECK (char_length(body) <= 500)
├── sent_at        timestamptz
└── INDEX (lobby_id, sent_at DESC)  -- for paginated history fetch
```

**Persisted vs. ephemeral:** Chat messages should be persisted (Postgres, NOT ephemeral Redis). Rationale: (1) players rejoin after disconnect and need history; (2) admin moderation may need a record; (3) the volume is low (lobbies have max ~10 members, short lifespan). A 30-day retention cleanup job (reuse `MatchmakingRetentionCleanupService` pattern) handles bloat.

### Ready-check → party ticket flow

When all `lobby_members.ready = true` (and `lobby.state = ReadyChecking`), `LobbyService.TryStartMatchmakingAsync` calls `IMatchmakingService.EnqueueAsync` with a party ticket. The Matchmaking party model already supports this: `MatchmakingService.EnqueueAsync` accepts a `partyId` (which is the `lobby.id` cast to a party context). However, since Lobby introduces a new party concept separate from Matchmaking's `Party` entity, the cleanest approach is:

1. `LobbyService.TryStartMatchmakingAsync` creates a `Party` row in Matchmaking (via `IPartyService.CreateAsync`) with members drawn from `lobby_members`.
2. Then calls `IMatchmakingService.EnqueueAsync` with the new `partyId`.
3. Lobby state transitions to `InGame`.

This keeps Matchmaking's existing party model intact and avoids a circular `Lobby → Matchmaking` dep. The Lobby package takes a runtime dep on `IMatchmakingService` (Matchmaking package).

**Package dependency:** `GameKit.Lobby → GameKit.Matchmaking` (runtime). This is a NEW downward arc in the dependency chain. It does not create a cycle (Matchmaking has no ref to Lobby).

### SignalR hub placement

The SignalR hub lives in `GameKit.Lobby`. Each lobby gets a group: `"lobby:{lobbyId}"`. Players subscribe via `Groups.AddToGroupAsync` on `JoinLobbyAsync`. Messages are published via `Clients.Group(...)`.

The SignalR hub is registered in `AddLobby()` via `services.AddSignalR()` + `builder.Services.TryAddSingleton<ILobbyHubContext>()`. The endpoint is mapped in `MapLobby()` via `app.MapHub<LobbyHub>("/hubs/lobby")`.

**Redis backplane for multi-replica:** The Lobby SignalR hub must use the Redis backplane from day one (not added later). In `AddLobby()`:

```csharp
services.AddSignalR()
        .AddStackExchangeRedis(redisConnectionString, opts =>
            opts.Configuration.ChannelPrefix = RedisChannel.Literal("gamekit:signalr"));
```

The `IConnectionMultiplexer` is already registered by the consumer (required for Matchmaking and Presence). `AddStackExchangeRedis` accepts a connection string — the consumer passes `GameKitLobbyOptions.RedisConnectionString` (defaults to pulling from the same connection string as the multiplexer).

### Per-package migration

YES — Lobby owns `lobbies`, `lobby_members`, `lobby_messages` tables. New advisory lock key needed (live-verify `SELECT hashtext('gamekit.lobby.migrations')::bigint`). Exclusion list: 20 prior-package entities (Core 4 + Auth 3 + Admin 1 + Rankings 7 + Matchmaking 5). Migration timestamp: `20260521000000_LobbyInitial`.

---

## Question 6: Multi-Replica Admin UI

### What breaks across replicas today

**Primary hazard:** `ErrorRateRingBuffer` (`src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs`) is an in-memory Singleton registered in `AddGameKitAdmin()`. It counts errors from `LogErrorCounter` (`src/GameKit.Admin.UI/Services/LogErrorCounter.cs`) which taps `ILoggerProvider`. Each replica has its own independent ring buffer counting only its own log errors. The health panel's "recent error rate" tile shows per-replica data, not aggregate. On 3 replicas an operator sees 1/3 of the actual error rate.

**Secondary hazard:** The `HealthProbeService` Postgres/Redis probes (in `src/GameKit.Admin.UI/Services/HealthProbeService.cs`) are per-request, not shared state, so they are fine across replicas.

**Tertiary hazard:** Admin user session state. v1 uses cookie auth (Blazor Server). Cookie encryption keys must be shared across replicas (`services.AddDataProtection().PersistKeysToFileSystem(...)` or `PersistKeysToDbContext(...)` — an operator concern, not a GameKit obligation). GameKit should document this requirement but not solve it prescriptively.

### Fixing the error-rate ring buffer for multi-replica

Three options:

1. **Replace with Redis counter (recommended):** Replace `ErrorRateRingBuffer` with a Redis-backed sliding window using `INCRBY` on time-bucketed keys (`"gk:admin:errors:{bucket_epoch_seconds}"`) with `EXPIRE` set to the window duration. The `LogErrorCounter.ILoggerProvider` tap writes to Redis instead of in-memory. `RecentErrorCount()` does a Redis `MGET` over the window bucket keys and sums. This gives aggregate error count across all replicas.

2. **Leave in-memory + document:** Accept that each replica shows only its own error rate. Add prominent documentation: "health panel error rate is per-replica in multi-instance deployments; use your APM (OTel) for aggregated metrics." Low implementation cost; suitable if the admin health panel is understood as an approximate indicator.

3. **OpenTelemetry metrics push:** Export the error counter as an OTel metric (already optional in GameKit via `ActivitySource`/`Meter`). The operator's OTel backend aggregates. Out of scope for multi-replica admin UI itself.

**Recommended:** Option 1 (Redis counter). It preserves the same zero-config experience for single-replica installs (Redis is already required). The implementation change is:

- New class `RedisErrorRateCounter` that replaces `ErrorRateRingBuffer` as the in-memory store.
- `LogErrorCounter` is modified to call `RedisErrorRateCounter.IncrementAsync()` (fire-and-forget `await db.StringIncrByAsync(...).ConfigureAwait(false)`) instead of `ErrorRateRingBuffer.IncrementError()`.
- `AddGameKitAdmin()` registers `RedisErrorRateCounter` as a Singleton alongside `LogErrorCounter`.
- `HealthProbeService.GetHealthReportAsync` reads from `RedisErrorRateCounter` rather than `ErrorRateRingBuffer`.
- `ErrorRateRingBuffer` is **kept** for the test harness (it implements `IClock`-driven decay tests); the production path switches to `RedisErrorRateCounter`.

### SignalR backplane wiring

The Admin UI does NOT currently use SignalR for panel updates. v1 health panel uses Blazor Server + `InvokeAsync(StateHasChanged)` on a periodic timer (client-side polling via `RefreshInterval` option). For multi-replica v2:

**Approach:** Add SignalR hub `AdminEventHub` in `GameKit.Admin.UI` for real-time admin notifications (player ban events, audit writes, health state changes). Register Redis backplane in `AddGameKitAdmin()` alongside the existing SignalR health polling.

```csharp
// In AddGameKitAdmin():
services.AddSignalR()
        .AddStackExchangeRedis(adminOpts.RedisConnectionString,
            opts => opts.Configuration.ChannelPrefix = RedisChannel.Literal("gamekit:admin"));
```

The `GameKitAdminOptions` gains a `RedisConnectionString string?` property (null = disabled SignalR backplane; admin works in single-replica mode without it).

This is an **additive change** — existing Admin UI functionality (Blazor Server health panel, player CRUD, audit) continues to work unchanged on a single replica. The backplane is opt-in via the connection string option.

**IHostedService for live updates:** A new `AdminLiveBroadcastService : BackgroundService` subscribes to a Redis Pub/Sub channel `"gamekit:admin:events"` and pushes events to `IHubContext<AdminEventHub>`. Other packages (Auth ban, Rankings end-season) publish to this channel when a significant event occurs. This is the same Pub/Sub pattern already used by `MatchmakingService` (STATUS channel) and the ticker.

---

## Component Boundaries Summary

| Component | Package | New or Modified | Migration Needed | Deps (runtime) |
|---|---|---|---|---|
| `IPlayerRatingProvider` interface | `GameKit.Core` | NEW | No | — |
| `PlayerRankingsProvider : IPlayerRatingProvider` | `GameKit.Rankings` | NEW | No | Core |
| `MatchmakingService` enqueue rating injection | `GameKit.Matchmaking` | MODIFIED | No | Core (IPlayerRatingProvider optional) |
| `GameKit.Auth.Argon2` package | NEW PACKAGE | NEW | No | Auth |
| `GameKit.Auth.Google` / `.Apple` / `.Epic` | NEW PACKAGES | NEW | No | Auth |
| `IAccountMergeService` + `AccountMergeService` | `GameKit.Auth` | NEW | YES (account_merges table) | Auth |
| `MatchmakingLadderConfig.AllowedRegions` | `GameKit.Matchmaking` | MODIFIED | No (PoolName already exists) | — |
| `EnqueueRequest.RegionName` DTO | `GameKit.Matchmaking` | MODIFIED | No | — |
| `GameKit.Lobby` package | NEW PACKAGE | NEW | YES (3 tables) | Core + Matchmaking |
| `LobbyHub : Hub` (SignalR) | `GameKit.Lobby` | NEW | No | — |
| `RedisErrorRateCounter` | `GameKit.Admin.UI` | NEW | No | — |
| `AdminLiveBroadcastService` | `GameKit.Admin.UI` | NEW | No | — |
| Rank decay background service | `GameKit.Rankings` | NEW | YES (decay_log column or table) | Rankings |
| Placement matches config | `GameKit.Rankings` | MODIFIED | YES (placement state in player_ranks) | Rankings |
| Backfill into in-progress sessions | `GameKit.Core` + `GameKit.Matchmaking` | MODIFIED | No (SessionState already exists) | — |

---

## Suggested Build Order

Dependencies flow top-to-bottom. Items at the same level can be built in parallel.

```
Phase 1: Core seam + stateless auth add-ons (no migration, lowest risk)
    ├── Add IPlayerRatingProvider + PlayerRatingSnapshot to GameKit.Core
    ├── Add PlayerRankingsProvider to GameKit.Rankings (implements IPlayerRatingProvider)
    ├── Modify MatchmakingService.EnqueueAsync to inject IPlayerRatingProvider?
    ├── Ship GameKit.Auth.Argon2 (IPasswordHasher, Isopoh, no migration)
    └── Ship GameKit.Auth.Google / .Apple / .Epic (IOAuthProvider, no migration)

Phase 2: Rankings depth (migration + new background services)
    ├── Rank decay: new background service + PlayerRank.LastDecayAt column (migration)
    ├── Placement matches: PlacementMatchesRemaining column in player_ranks (migration)
    └── Requires: Phase 1 IPlayerRatingProvider (decay reads ratings)

Phase 3: Regional pools (no migration — pure config + PoolName convention)
    ├── MatchmakingLadderConfig.AllowedRegions
    ├── EnqueueRequest.RegionName
    └── Requires: Phase 1 Matchmaking changes (enqueue path already modified)

Phase 4: Account merge (high-risk — SERIALIZABLE tx, cross-table FK surgery)
    ├── account_merges table migration in GameKit.Auth (new advisory lock key)
    ├── AccountMergeService (SERIALIZABLE, retry on 40001)
    ├── Admin UI "Account Merge" flow
    └── Requires: Phase 1 (Auth.Argon2 + providers unblocked), Phase 2 stable (ranks table stable)

Phase 5: GameKit.Lobby (new package, new migration, SignalR)
    ├── Lobby entities + migration (advisory lock live-verified)
    ├── LobbyHub (SignalR + Redis backplane)
    ├── LobbyService.TryStartMatchmakingAsync → IMatchmakingService / IPartyService
    └── Requires: Phase 3 (regional pools stable), Matchmaking package stable

Phase 6: Multi-replica Admin UI
    ├── RedisErrorRateCounter (replace ErrorRateRingBuffer on hot path)
    ├── AdminLiveBroadcastService (Redis Pub/Sub → IHubContext)
    ├── AdminEventHub (SignalR + Redis backplane, opt-in)
    └── Requires: Phase 5 (SignalR pattern validated in Lobby)

Phase 7: Fix "Rank adjust" admin stub page (deferred v1 tech debt)
    └── Requires: Phase 2 (rank decay + placement stable)
```

**Rationale for ordering:**
- Phase 1 first because it is the prerequisite seam for all rating-aware work AND contains zero-migration packages that can ship independently.
- Phase 2 before Account Merge because the account_merges migration must know the final `player_ranks` schema (Phase 2 adds columns).
- Phase 3 (regional pools) before Phase 5 (Lobby) because Lobby's `TryStartMatchmakingAsync` needs the stable enqueue API with RegionName support.
- Phase 4 (account merge) is isolated: high risk, reversible (the merge record prevents re-merge), no downstream deps.
- Phase 6 (Admin multi-replica) last because it is operational polish, not a new feature gate, and benefits from the SignalR pattern proven in Phase 5 Lobby.

---

## New Advisory Lock Keys Needed

New packages that own migrations require new advisory lock keys. Each must be live-verified in Testcontainers before use.

| Package | `hashtext(...)` input | Live-verify SQL |
|---|---|---|
| `GameKit.Auth` (account_merges) | existing Auth key = -298890956 | New migration in EXISTING Auth package — uses existing key, new timestamp |
| `GameKit.Lobby` | `'gamekit.lobby.migrations'` | `SELECT hashtext('gamekit.lobby.migrations')::bigint` |
| Rankings (decay, placement) | existing Rankings key = -156812172 | New migrations in EXISTING Rankings package — uses existing key |

Note: new migrations in existing packages reuse the existing advisory lock key for that package (the key serialises all migrations within a package). Only wholly NEW packages need a new key.

---

## Sources

All findings verified by direct file reads. No external sources consulted — the codebase IS the authoritative source for this research.

| File | Relevance |
|------|-----------|
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs:201-204` | Zero-rating hardcode — the exact tech debt being fixed |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:504-577` | `BuildQueuedPartyFromHash` — reads `aggregateRating` and `members` fields; no change needed |
| `src/GameKit.Matchmaking/Strategy/QueuedParty.cs:19-21` | Documents "cache may be stale" acceptance |
| `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs:112-125` | Bracket + symmetric-overlap rule — works unchanged once ratings are real |
| `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` | Null-object-default port pattern — template for IPlayerRatingProvider |
| `src/GameKit.Core/Services/IPresenceProvider.cs` | Optional-interface pattern in Core — confirms the IPlayerRatingProvider placement |
| `src/GameKit.Core/Data/IModelBuilderExtension.cs` | TryAddEnumerable Singleton pattern |
| `src/GameKit.Auth/Providers/IOAuthProvider.cs` | Scrutor strategy interface pattern |
| `src/GameKit.Auth/Services/IPasswordHasher.cs` | IPasswordHasher interface for Auth.Argon2 |
| `src/GameKit.Auth/Entities/PlayerIdentity.cs` | UNIQUE(provider, external_id) — no migration needed for new providers |
| `src/GameKit.Core/Entities/AdminAuditLog.cs` | AccountMerge audit record target |
| `src/GameKit.Core/Entities/Player.cs` | Source/target of account merge — ON DELETE CASCADE on identities confirmed |
| `src/GameKit.Rankings/Entities/PlayerRank.cs` | UNIQUE(player_id, ladder_id) — merge strategy constraint |
| `src/GameKit.Rankings/Entities/Ladder.cs` | FK refs from new tables |
| `src/GameKit.Admin.UI/Services/ErrorRateRingBuffer.cs` | In-memory counter — multi-replica hazard identified |
| `src/GameKit.Admin.UI/Services/LogErrorCounter.cs` | ILoggerProvider tap — modification point for RedisErrorRateCounter |
| `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs:52` | PoolName already exists — no schema change for regional pools |
| `src/GameKit.Presence/Builder/PresenceBuilderExtensions.cs` | Confirmed Presence is stateless (no migration hosted service) |
| `.planning/STATE.md` locked decisions | Advisory lock keys, per-package migration boundary rules |
