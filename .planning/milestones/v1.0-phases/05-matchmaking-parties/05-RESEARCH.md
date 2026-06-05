# Phase 5: Matchmaking + Parties — Research

**Researched:** 2026-05-16
**Domain:** Redis-backed live matchmaking queue, party lifecycle, accept-flow proposals, BackgroundService leader election, Channel-based async Postgres drain, Npgsql pool management under load.
**Confidence:** HIGH — all locked decisions from CONTEXT.md are unambiguous; Claude's-Discretion areas are resolved below with rationale tied to existing codebase patterns; no new NuGet packages are required.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Party model & lifecycle**
- **D-01:** Parties are durable Postgres entities. `parties` table: id + party-code + state machine + owner `PlayerId` + auto-expiry. `party_members`: FK to `parties` + FK to `players`.
- **D-02:** Join via short party code (6–8 random chars). Case-insensitive. Single-active-party per player. Expire on dissolve.
- **D-03:** Direct-invite-by-PlayerId deferred. No `party_invites` table, no stub.
- **D-04:** Mid-queue disconnect: cancel ticket, keep party row alive. Party can re-enqueue after reconnect.
- **D-05:** Cross-provider parties allowed. Membership references canonical `Player` row, not `PlayerIdentity`.

**Match-found flow**
- **D-06:** Accept-step proposal model. Matcher writes `match_proposals`, parks tickets, notifies clients. Session created only after all participants accept within timeout.
- **D-07:** Accept timeout = 10 seconds (global, no per-ladder override in v1).
- **D-08:** Escalating decline cooldown: 3 min / 15 min / 30 min. Tracked in `decline_history` Postgres table. Cooldown enforced at `POST /api/mm/queue` (returns `403 ProhibitedDuringCooldown` + `retryAfterSeconds`). Window + step durations in `GameKitMatchmakingOptions`.
- **D-09:** On proposal failure, accepting parties auto-re-queue at front with original `queuedAt` preserved (original timestamp retained on re-insertion).
- **D-10:** Long-poll `GET /api/mm/queue/{ticketId}/status` — holds ≤30s, returns `{ status, proposalId?, deadline?, sessionId? }`. No SignalR. No short-poll fallback.

**Bracket flex curve & strategy config**
- **D-11:** Default `EloRangeMatchmakingStrategy`. Formula: `bracket(t) = min(100 + (400 · t/40), 500)`. `t` = seconds in queue. Linear ramp 100→500 over 40s.
- **D-12:** Per-ladder curve config via `AddLadder(opts => …)`: `BracketStart` (default 100), `BracketEnd` (default 500), `BracketRampSeconds` (default 40).
- **D-13:** Party-rating aggregator per ladder: `Mean | Max | GlickoWeighted`. Default = `Mean`. Surface as enum + switch arm in `EloRangeMatchmakingStrategy`.
- **D-14:** Optional `MaxPartyRatingSpread` per ladder (default `null` = disabled). On rejection: `400 PartyRatingSpreadExceeded`.

**Postgres async-write durability**
- **D-15:** `Channel<TicketEvent>` + `MatchmakingAnalyticsDrainService`. Bounded channel. Drop newest on full + increment OTel counter. Producer = hot path; consumer = drain service. Polly v8 retry on transient Postgres failure.
- **D-16:** On Postgres outage: log + drop. OTel counter `matchmaking.analytics.dropped_events`. Matchmaking continues from Redis. No disk spill.
- **D-17:** 30-day retention on `matchmaking_tickets` via `MatchmakingRetentionCleanupService` (nightly + startup-immediate). Default 30 days, configurable via `GameKitMatchmakingOptions.TicketRetention`.
- **D-18:** Event taxonomy (8 events): `Queued`, `Proposed`, `Accepted`, `Declined`, `TimedOut`, `Matched`, `Cancelled`, `Expired`. No bracket-flex-step events (volume concern).

### Claude's Discretion

- Pool partitioning shape: one sorted set per `{ladderId, poolName}` or single per-ladder set with composite key.
- Proposal storage: Redis hash with TTL vs Postgres `match_proposals` table.
- Team split algorithm: v1 ships random. MMR-balanced deferred.
- Party code alphabet.
- Reconciler scope details.
- Sample app (`TicTacToeDuel`) demonstration scope.

### Deferred Ideas (OUT OF SCOPE)

- Direct invite by PlayerId.
- SignalR / WebSocket push.
- Party chat / voice.
- Cross-server matchmaking / region affinity.
- Friends list / social graph.
- MMR-balanced team split.
- Priority lane for long-waiters past max bracket.
- Disk-spill or operator-paging on Postgres outage.
- Per-ladder accept-timeout override.
- Per-ladder cancel-vs-shrink disconnect policy.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| MATCH-01 | Ships as `GameKit.Matchmaking` NuGet package | Skeleton csproj at `src/GameKit.Matchmaking/` confirmed — `AssemblyInfo.cs` only. No new NuGet packages needed; all deps already pinned. |
| MATCH-02 | `matchmaking_tickets` entity — async-write for analytics, Redis is live source of truth | `Channel<TicketEvent>` + drain service (D-15/D-16). EF entity in matchmaking migration. |
| MATCH-03 | `party_members` entity — 1-N from v1 | Durable Postgres entities (D-01/D-05). Schema: `parties` + `party_members` tables. |
| MATCH-04 | Redis is source of truth for live queue | Sorted sets per `{ladderId, poolName}`, score = `queuedAt` (Unix seconds). Never rehydrate from Postgres. |
| MATCH-05 | Atomic ticket claim via Redis WATCH/MULTI to prevent double-matching | Lua script recommendation — see §Decision 3 below. |
| MATCH-06 | Reconciliation worker (30s + startup) — claims abandoned tickets, never rehydrates Redis | `MatchmakingReconcilerService : BackgroundService`. Sweeps non-terminal ticket rows > N min old not present in Redis. |
| MATCH-07 | `BackgroundService` + `PeriodicTimer` + Polly retry | Mirrors `RankingsTickerService` exactly. `MatchmakerTickerService`. |
| MATCH-08 | Leader election via Redis distributed lock | `MatchmakerLeaseHelper` (copy of `RankingsTickerLeaseHelper`, new lock key). |
| MATCH-09 | `IMatchmakingStrategy.Match(Party, candidates)` — party-aware from v1 | Interface shape defined below. Party aggregate rating is the primary ranking scalar. |
| MATCH-10 | Default `EloRangeMatchmakingStrategy` with bracket flex | `bracket(t) = min(BracketStart + (BracketEnd − BracketStart) · t / BracketRampSeconds, BracketEnd)`. |
| MATCH-11 | Per-player rate limit on enqueue | `IGameKitRateLimitPolicies` extension `MatchmakingEnqueue`, partitioned by `ClaimTypes.NameIdentifier`. |
| MATCH-12 | Chaos test: kill mid-match → no duplicates, no ghost tickets | `MatchmakingChaosTests` — in-process simulation (abort pending ops, verify reconciler cleans up). |
| MATCH-13 | Load test as phase gate (1k concurrent tickets, 10 min) | Separate `GameKit.Matchmaking.LoadTests` project. Stopwatch-per-iteration budget assertion. |
| MATCH-14 | Admin UI queue-depth + health panels wired to Redis live state | `IMatchmakingObservability` port + `RedisMatchmakingObservability` adapter. Phase 3 `QueueDepth.razor` already has a placeholder waiting for this interface. |
| MATCH-15 | Per-package migrations, `__ef_migrations_matchmaking` | `MatchmakingMigrationConstants` with unique advisory-lock key. Verification test mirrors `RankingsAdvisoryLockKeyTests`. |
</phase_requirements>

---

## Executive Summary

Phase 5 ships `GameKit.Matchmaking` as the fifth NuGet package. The domain has two meaningfully hard problems that do not exist in prior phases: **atomic multi-ticket claim** (two replicas must never double-match the same ticket pair) and **chaos durability** (a crash between "matcher found a match" and "game session created" must leave zero ghost tickets or orphaned sessions). Every other concern — migration scaffolding, BackgroundService + Polly + Redis lock, per-package advisory key, builder extension, admin wiring — is a direct port of the Phase 4 `GameKit.Rankings` pattern and can be coded mechanically.

The recommended architectural answer to both hard problems is: (1) a **Lua script for atomic claim-and-park**, which removes the ticket from the sorted set and writes the proposal hash in a single EVAL call that Redis serializes, and (2) a **reconciler that only writes `Expired`/`Cancelled` status to Postgres** and never touches Redis — Redis is recreated empty after any crash, and the reconciler sweeps Postgres for stale non-terminal rows. The accept-flow is: Redis sorted-set queue → Lua-script match formation → Redis hash proposal (TTL = 10s) → long-poll status endpoint via Redis pub/sub race → all-accept → `game_sessions` INSERT → `MatchedEvent` drained to Postgres analytics.

The `Channel<TicketEvent>` drain architecture for Postgres async-write is a deliberate trade-off: the matchmaker never waits on a Postgres write in the hot path. The drain service is best-effort; analytics may lose events under sustained Postgres outage (D-16). This is correct for a game backend — live matching is more important than analytics completeness.

**Primary recommendation:** Implement the Lua atomic-claim path first (it is the correctness anchor for SC#2 and SC#4). Build the reconciler second. Defer the long-poll implementation to a later wave because it has no correctness dependency on the reconciler — it is a read-path optimization.

---

## Architectural Responsibility Map

All capabilities run in the API/Backend tier (server-side library). No browser, no SSR frontend tier, no CDN tier for any matchmaking logic. The admin UI panel is the one exception: QueueDepth.razor is a Blazor Server component (Frontend Server tier) that calls through to `IMatchmakingObservability` (Backend tier).

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Live queue (sorted sets) | Cache (Redis) | — | Redis is source of truth per MATCH-04. No Postgres write on enqueue. |
| `POST /api/mm/queue` enqueue | API / Backend | Cache (Redis ZADD) + DB (Channel write) | Endpoint writes to Redis sorted set and fires a `Queued` event into the Channel. |
| `MatchmakerTickerService` | API / Backend | Cache (Redis Lua/EVAL) | Background loop; only the leader runs. |
| Match proposal storage | Cache (Redis hash + TTL) | — | Ephemeral; proposal TTL = 10s. Reconciler sweeps Postgres for orphaned sessions only (not proposals). |
| `POST /api/mm/proposal/{id}/accept` | API / Backend | Cache (Redis hash) + DB (game_sessions INSERT) | Final accept creates the game session in Postgres. |
| Long-poll `GET /api/mm/queue/{ticketId}/status` | API / Backend | Cache (Redis pub/sub subscribe) | Server subscribes to `mm:status:{ticketId}` channel and races a 30s timeout. |
| `MatchmakingReconcilerService` | API / Backend | DB (Postgres scan + UPDATE) | 30s periodic sweep. Does NOT write to Redis. |
| `MatchmakingAnalyticsDrainService` | API / Backend | DB (Postgres batch INSERT) | Best-effort analytics. Never on the hot path. |
| `MatchmakingRetentionCleanupService` | API / Backend | DB (Postgres DELETE WHERE) | Nightly. Mirrors `IdempotencyCleanupService`. |
| Party CRUD (`/api/parties/*`) | API / Backend | DB (Postgres) | Durable entities. Party code is Postgres-resident. |
| `IMatchmakingObservability` port | API / Backend | Cache (Redis ZCARD, GET) | Reads ZCARD per sorted set + leader identity key from Redis. |
| Admin QueueDepth panel | Frontend Server (Blazor Server) | API/Backend (via `IMatchmakingObservability`) | `QueueDepth.razor` already exists with a placeholder. Phase 5 fills it in. |
| `parties` / `party_members` / `matchmaking_tickets` / `decline_history` tables | DB / Storage | API / Backend (writer) | Owned tables in `gamekit` schema under matchmaking migration. |

---

## Recommended Architecture

### Component Diagram (data flow)

```
Client
  │
  ├─ POST /api/parties  ──────────────────► PartyService
  │                                              │
  │                                              └─ INSERT parties + party_members (Postgres)
  │
  ├─ POST /api/mm/queue ──────────────────► MatchmakingService
  │   (JWT-protected,                            │
  │    rate-limited by PlayerId)                 ├─ cooldown check (decline_history, Postgres)
  │                                              ├─ party spread check (D-14)
  │                                              ├─ ZADD mm:queue:{ladderId}:{pool} score=Unix(queuedAt)
  │                                              ├─ HSET mm:ticket:{id} {payload}
  │                                              └─ Channel.TryWrite(Queued event)
  │
  ├─ GET /api/mm/queue/{ticketId}/status ─► LongPollHandler
  │   (holds ≤30s)                               │
  │                                              ├─ SUBSCRIBE mm:status:{ticketId}
  │                                              └─ race: message received | 30s timeout | CT
  │
  ├─ POST /api/mm/proposal/{id}/accept ──► ProposalService
  │                                              │
  │                                              ├─ verify proposal hash exists (Redis HGETALL)
  │                                              ├─ mark player accepted (HSET mm:proposal:{id}:accepted:{pid})
  │                                              ├─ if all accepted → INSERT game_sessions + participants (Postgres)
  │                                              ├─ PUBLISH mm:status:{ticketId} "matched" to all ticket IDs
  │                                              └─ Channel.TryWrite(Matched events)
  │
  └─ POST /api/mm/proposal/{id}/decline ─► ProposalService
                                                  │
                                                  ├─ INSERT decline_history (Postgres)
                                                  ├─ re-queue accepting parties (ZADD with original queuedAt)
                                                  └─ PUBLISH mm:status:{ticketId} "cancelled" to declining party

MatchmakerTickerService (BackgroundService, PeriodicTimer ~500ms)
  │
  ├─ TryAcquireLease (Redis LockTake "gamekit:matchmaking:matcher:lock")
  │   └─ NOT acquired → return LockNotAcquired (non-leader replica)
  │
  ├─ [leader only] For each ladder:
  │   ├─ RenewLease mid-tick (Pitfall §6 — bail if false)
  │   ├─ ZRANGEBYSCORE mm:queue:{ladderId}:{pool} candidates
  │   ├─ For each candidate pair that overlaps brackets:
  │   │   └─ Lua script: EVAL atomic-claim
  │   │       ├─ ZREM tickets from sorted set
  │   │       ├─ HSET mm:proposal:{id} {ticketIds, deadline, ladderId}
  │   │       ├─ EXPIRE mm:proposal:{id} 10 (D-07)
  │   │       └─ HSET mm:ticket:{id} status=Proposed proposalId={id}
  │   │
  │   └─ PUBLISH mm:status:{ticketId} "proposed" to each ticket holder
  │
  └─ ReleaseLease

MatchmakingReconcilerService (BackgroundService, 30s + startup)
  │
  └─ Scan matchmaking_tickets WHERE status NOT IN (Matched, Cancelled, Expired)
          AND queued_at < (now - StaleTicketThreshold)
     └─ UPDATE matchmaking_tickets SET status = 'Expired' (Postgres only; no Redis write)
     Scan game_sessions WHERE state = 'Active' AND created_at < (now - OrphanSessionThreshold)
       AND no heartbeat from participants
     └─ session.Cancel() + IAdminAuditWriter (optional)

MatchmakingAnalyticsDrainService (BackgroundService)
  │
  └─ Channel<TicketEvent>.Reader — drain batches of ≤100 or every 5s
     ├─ Batch INSERT matchmaking_tickets (Postgres)
     └─ On Polly exhaustion → log + increment matchmaking.analytics.dropped_events

MatchmakingRetentionCleanupService (BackgroundService)
  └─ Nightly + startup: DELETE FROM matchmaking_tickets WHERE terminal_at < (now - 30d)
```

### Four Canonical Paths

**Path 1 — enqueue → matched (happy path)**
1. `POST /api/mm/queue` → ZADD + Channel(Queued)
2. Ticker Lua script atomically removes both tickets, writes proposal hash (TTL=10s), sets `mm:ticket:{id}.status=Proposed`
3. PUBLISH `mm:status:{ticketId}` → "proposed" wakes both long-polls
4. Both players `POST /api/mm/proposal/{id}/accept` within 10s
5. All-accepted check → INSERT `game_sessions` + `session_participants` (Teams assigned randomly)
6. PUBLISH "matched" to all long-polls
7. Channel receives `Matched` events → drain service writes to `matchmaking_tickets`

**Path 2 — enqueue → cancelled (decline/timeout)**
1. Steps 1–3 same as above
2. One player `POST /api/mm/proposal/{id}/decline` (or 10s TTL expires)
3. INSERT `decline_history` for declining player
4. Re-ZADD accepting parties' tickets with original `queuedAt` score (D-09)
5. PUBLISH "cancelled" to declining party's long-polls
6. Channel receives `Declined`/`TimedOut` events

**Path 3 — enqueue → app crash → reconciled as Expired**
1. `POST /api/mm/queue` → ZADD → ticket row in flight via Channel (may not have flushed to Postgres yet)
2. App process dies mid-tick
3. Redis is recreated empty on restart. Redis has no knowledge of the stale ticket.
4. `MatchmakingReconcilerService` runs on startup sweep
5. Finds `matchmaking_tickets` rows in `Queued` state older than stale threshold (configurable, default 5 min)
6. UPDATE status = `Expired` (Postgres only)
7. Players receive no long-poll wake — they must re-enqueue after reconnect

**Path 4 — proposal crash → reconciled orphaned session**
1. All-accepted path begins; `game_sessions` INSERT succeeds; app crashes before PUBLISH
2. On restart: `MatchmakingReconcilerService` finds `game_sessions` in `Active` state with `created_at` older than orphan threshold and no participant activity
3. `game_sessions.Cancel()` + audit
4. Ticket rows remain `Matched` in analytics (Postgres) — correct: the match was made; the session was cancelled by infra, not the player

### Key Class Names and Responsibilities

| Class / Interface | Responsibility |
|-------------------|---------------|
| `IMatchmakingStrategy` | Contract: `MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now)` |
| `EloRangeMatchmakingStrategy` | Default impl: bracket-flex, `GlickoWeighted`/`Mean`/`Max` aggregator switch |
| `IMatchmakingService` | Application service for enqueue, cancel, status query, proposal accept/decline |
| `IPartyService` | Party CRUD: create, join (code), dissolve, get |
| `MatchmakerTickerService` | `BackgroundService` + `PeriodicTimer` (500ms) — leader-elected |
| `MatchmakerLeaseHelper` | Copy of `RankingsTickerLeaseHelper`; lock key `gamekit:matchmaking:matcher:lock` |
| `MatchmakingReconcilerService` | `BackgroundService` — 30s periodic + startup; sweeps stale Postgres rows; never touches Redis |
| `MatchmakingAnalyticsDrainService` | `BackgroundService` — drains `Channel<TicketEvent>` to Postgres |
| `MatchmakingRetentionCleanupService` | `BackgroundService` — nightly 30-day retention sweep; mirrors `IdempotencyCleanupService` |
| `IMatchmakingObservability` | Port: `GetQueueStatsAsync(ct)` → `MatchmakingQueueStats` (per-pool depths, lease count, leader identity) |
| `RedisMatchmakingObservability` | Adapter: ZCARD per sorted set + GET on lock key for leader identity |
| `MatchmakingMigrationConstants` | `MigrationsHistoryTable = "__ef_migrations_matchmaking"`, `AdvisoryLockKey` (live-verified) |
| `MatchmakingMatchmakerKeys` | Redis key constants (`mm:queue:{ladderId}:{pool}`, `mm:ticket:{id}`, `mm:proposal:{id}`, `mm:status:{ticketId}`) |
| `MatchmakingModelBuilderExtension` | EF `IModelBuilderExtension` for 4 entities |
| `MatchmakingBuilderExtensions` | Fluent `AddMatchmaking()` extension on `IGameKitBuilder`; extends `AddLadder` with matchmaking config |

---

## Decisions with Recommendations

### Decision 1 — Pool Partitioning Shape (Claude's Discretion)

**Options:**
- A: One Redis sorted set per `{ladderId, poolName}` — key `mm:queue:{ladderId}:{pool}`
- B: Single per-ladder sorted set with composite member value encoding pool name

**Recommendation: Option A — one sorted set per `{ladderId, poolName}`.**

Rationale: ZRANGEBYSCORE on a single-pool set is O(log N + M); cross-pool scan would require ZRANGEBYSCORE across multiple keys anyway. Separate keys give ZCARD per pool for free (powers `IMatchmakingObservability`). Key pattern `mm:queue:{ladderId}:{pool}` — pool defaults to `"default"` if `GameKitMatchmakingOptions.Pools` is not configured. `ZCARD mm:queue:*` via SCAN (not KEYS) to enumerate pools for the admin panel.

The `poolName` field is surfaced as an optional property on the enqueue request body. For v1, most operators will use a single `"default"` pool per ladder. The design does not limit pool count; the matcher iterates all registered pools in one tick.

### Decision 2 — Proposal Storage (Claude's Discretion)

**Options:**
- A: Redis hash `mm:proposal:{id}` with `EXPIRE = AcceptTimeoutSeconds` (+ reaping service)
- B: Postgres `match_proposals` table with TTL column and reconciler

**Recommendation: Option A — Redis hash with TTL.**

Rationale from CONTEXT.md default expectation: "Fits SC#2 chaos test shape; reconciler only sweeps Postgres for orphaned sessions, not proposals." If Redis goes down, proposals are lost — but Redis is required for the live queue anyway (self-hosted `docker-compose.yml` ships with `--maxmemory-policy noeviction`). A crashed proposal simply times out from the client's perspective; the player re-queues. The reconciler's job is to clean `matchmaking_tickets` and `game_sessions` in Postgres — it does not need to know about proposals.

Proposal hash fields: `{ ticketIds: "uuid,uuid,...", ladderId: "uuid", deadline: "ISO-8601", poolName: "default", state: "pending|complete" }`. Individual accept tracking: per-ticket accept key `mm:proposal:{id}:accepted:{ticketId}` (SET with same TTL), so the "all accepted" check is `KEYS mm:proposal:{id}:accepted:*` count vs. expected party count — or better: an atomic Lua "check-and-complete" script to avoid race between last accept and session creation.

### Decision 3 — Atomic Match Formation (MATCH-05)

**The correctness anchor for SC#2 and SC#4.**

**Recommendation: Lua script evaluated server-side via `IDatabase.ScriptEvaluateAsync`.**

The `WATCH/MULTI/EXEC` alternative works but is client-driven — if the app process crashes after EXEC sends but before the response is read, the outcome is ambiguous. A Lua script is atomically executed by Redis and is idempotent when called with the same proposalId.

Lua script pseudocode (key arguments passed as KEYS[], ARGV[]):
```lua
-- KEYS: sorted-set keys for each pool candidate ticket; ARGV: ticket ids, proposal id, deadline, lease value
-- Step 1: verify lease token still matches (fencing — guards against stale leader)
if redis.call('GET', KEYS['leaseKey']) ~= ARGV['leaseValue'] then
  return {err='LEASE_LOST'}
end
-- Step 2: verify all candidate tickets still exist in the sorted set (not already claimed)
for _, ticketKey in ipairs(ticketZsetKeys) do
  if redis.call('ZSCORE', ticketKey, ARGV[ticketId]) == false then
    return {err='TICKET_GONE'}
  end
end
-- Step 3: atomic remove + write proposal
for _, ticketZsetKey, ticketId in pairs(candidates) do
  redis.call('ZREM', ticketZsetKey, ticketId)
  redis.call('HSET', 'mm:ticket:'..ticketId, 'status', 'Proposed', 'proposalId', ARGV['proposalId'])
end
redis.call('HSET', 'mm:proposal:'..ARGV['proposalId'], ...) -- proposal fields
redis.call('EXPIRE', 'mm:proposal:'..ARGV['proposalId'], ARGV['ttlSeconds'])
return 'OK'
```

The lease check inside the Lua script is the critical fencing token guard (Pitfall §2 below). If the lock expired between the last `RenewLeaseAsync` call and the Lua EVAL, the script returns `LEASE_LOST` and the ticker bails — no partial state written.

[ASSUMED] — the exact Lua script must be authored as a planning task with a unit test; this pseudocode shows the required semantics.

### Decision 4 — Bracket Flex Computation

`bracket(t) = min(BracketStart + (BracketEnd - BracketStart) * t / BracketRampSeconds, BracketEnd)`

Where `t` is computed **per candidate ticket** as `(now - ticket.queuedAt).TotalSeconds`.

**Bracket overlap check (symmetric):** Ticket A (rating `rA`, bracket `bA`) and Ticket B (rating `rB`, bracket `bB`) match if `|rA - rB| <= bA AND |rA - rB| <= bB`. Both must be inside each other's bracket — the constraint is conjunctive. This prevents a low-rated ticket with a very wide bracket from pulling in a high-rated ticket whose own bracket hasn't widened yet.

`t` is calculated from the `IClock.UtcNow` snapshot taken at the start of each tick, applied uniformly to all candidates in that tick. This prevents per-candidate clock skew within a single tick.

For re-queued tickets (D-09), the original `queuedAt` is preserved — so a player who accepted and got re-queued due to a partner's decline retains their accumulated bracket. The score in the sorted set IS the `queuedAt` Unix timestamp, so preserving the score preserves the bracket.

### Decision 5 — Party-Rating Aggregator

`GlickoWeighted` math: weighted mean where each member's weight = `1 / RD^2` (inverse variance). This is the standard uncertainty-weighted estimate from Glicko theory.

```csharp
// GlickoWeighted implementation sketch
double sumWeightedRating = 0;
double sumWeights = 0;
foreach (var member in party.Members)
{
    var rd = member.RatingDeviation;
    var weight = 1.0 / (rd * rd);
    sumWeightedRating += weight * member.Rating;
    sumWeights += weight;
}
return sumWeights > 0 ? sumWeightedRating / sumWeights : party.Members.Average(m => m.Rating);
```

**Where computed:** On enqueue, compute the party aggregate rating once and store it on the ticket hash (`mm:ticket:{id}.aggregateRating`). The matcher reads this cached value rather than recomputing each tick. This is correct because `player_ranks.rating` does not change mid-queue (the rankings ticker drains on a ~1h period, far longer than a typical queue wait).

**V1 recommendation:** Cache aggregate rating at enqueue time. Document in `IMatchmakingStrategy` XML docs that the cached value may be stale by up to one ratings period for long-waiting tickets.

### Decision 6 — Leader Election + Reconciliation Interplay

| Service | Runs on | Rationale |
|---------|---------|-----------|
| `MatchmakerTickerService` | Leader only | Only the leader claims tickets via Lua script. Non-leaders return `LockNotAcquired` and wait. |
| `MatchmakingReconcilerService` | **Leader only** | Two replicas running simultaneous `UPDATE status = 'Expired'` produce harmless duplicate UPDATEs (idempotent SET), but running on all replicas causes unnecessary Postgres load under a 1k-ticket test. Simpler to restrict to leader. Acquire the same leader lock before running the sweep. |
| `MatchmakingAnalyticsDrainService` | **Every replica** | Each replica writes events into its own local `Channel<TicketEvent>`. The channel is in-process; there is no shared channel across replicas. Each replica drains its own channel. This is correct — each replica only fires events for the tickets it processed. |
| `MatchmakingRetentionCleanupService` | **Any one replica** | Nightly DELETE is idempotent. Running on every replica wastes Postgres connections but doesn't corrupt data. Recommendation: leader-gated to save connections, same as reconciler. |

[ASSUMED] — the "reconciler on leader only" recommendation is a design choice, not a verified constraint from official documentation.

### Decision 7 — Channel-Based Analytics Durability

**Bounded channel capacity:** 10,000 events. Rationale: At 1k concurrent tickets with ~8 events per ticket lifecycle, the full test run generates ~8k events. A 10k buffer gives headroom for bursts without occupying excessive memory (each `TicketEvent` record is ~100 bytes = ~1 MB total).

**Drain batch:** 100 events per Postgres INSERT batch, or every 5 seconds, whichever fires first. `System.Threading.Channels.Channel.Reader.ReadAllAsync` with `CancellationToken` + an outer `PeriodicTimer`-style check is the idiomatic .NET 10 pattern.

**Polly retry spec for drain:**
```csharp
new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 4,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(500),
        ShouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>()
            .Handle<DbUpdateException>()
    })
    .AddTimeout(TimeSpan.FromSeconds(30))
    .Build()
```

**OTel counter:** `matchmaking.analytics.dropped_events` — instrument as `Counter<long>` via `System.Diagnostics.Metrics.Meter("GameKit.Matchmaking", "1.0.0")`. Tags: `{ "reason": "channel_full" | "polly_exhausted" }`. Follows D-16 naming exactly.

[ASSUMED] — exact Polly parameter values (500ms base delay, 4 attempts, 30s timeout) are reasonable defaults but may need operator tuning guidance in XML docs.

### Decision 8 — Cooldown Enforcement

**Where the check lives:** Only at `POST /api/mm/queue`. Not at proposal-accept.

Rationale: A player who is already in a proposal flow (status = `Proposed`) got there by having no cooldown at enqueue time. Re-checking at accept would be a race condition (cooldown kicks in between enqueue and accept). The ticket is parked for ≤10 seconds — an acceptable window. Re-add a cooldown check at accept only if abuse data shows this gap is exploited.

**`decline_history` schema:**
- `id UUID PK`
- `player_id UUID FK players.id NOT NULL`
- `declined_at TIMESTAMPTZ NOT NULL`
- `proposal_id UUID NOT NULL` (reference to the proposal hash — stored as text for durability, not FK)
- Index: `(player_id, declined_at DESC)` for rolling-window queries

**Cooldown query:** `SELECT COUNT(*) FROM decline_history WHERE player_id = @id AND declined_at > (now() - @window)`. Window = `GameKitMatchmakingOptions.CooldownWindowMinutes` (default 60 min). Count = 1 → 3 min cooldown. Count = 2 → 15 min. Count ≥ 3 → 30 min. Latest `declined_at + cooldown_duration > now` → return `retryAfterSeconds = (latest + duration - now).TotalSeconds`.

**UTC-only:** use `IClock.UtcNow` throughout. Never `DateTime.Now`. Pitfall §4 below.

### Decision 9 — Long-Poll Status Endpoint

**Recommended pattern:** Redis pub/sub channel `mm:status:{ticketId}`.

On match formation, the ticker/proposal service PUBLISHes to `mm:status:{ticketId}` for each affected ticket. The long-poll handler:
1. First reads the current status from `mm:ticket:{id}` (fast HGET) — returns immediately if already `Proposed`/`Matched`/`Cancelled`
2. Otherwise: subscribes to `mm:status:{ticketId}` via `ISubscriber.SubscribeAsync`
3. Races: first message received vs. 30s timeout via `CancellationTokenSource.CancelAfter(30s)`
4. On timeout: return `{ status: "queued" }` — client polls again
5. On message: parse and return `{ status, proposalId?, deadline?, sessionId? }`
6. Unsubscribe in `finally` block to prevent connection leak (Pitfall §5 below)

[ASSUMED] — StackExchange.Redis `ISubscriber.SubscribeAsync` with async `ChannelMessageQueue` is the idiomatic pattern. No official Context7 verification was run (no ctx7 available in environment), but StackExchange.Redis pub/sub is a well-documented stable feature.

**Connection pool concern:** Each long-poll request holds a Redis pub/sub subscriber for up to 30s. With 1k concurrent players polling, this is 1k concurrent subscribers. StackExchange.Redis uses a dedicated multiplexer connection for pub/sub — the subscriber connection is shared, not per-request. The per-request resource is a `ChannelMessageQueue` registration, which is a lightweight struct. This is safe at 1k scale.

### Decision 10 — Rate-Limit Integration

Extend `IGameKitRateLimitPolicies` with `MatchmakingEnqueue` — following the identical pattern in `RankingsRateLimitRegistrations.cs`:

```csharp
// Policy: 5 requests per minute per PlayerId (ClaimTypes.NameIdentifier)
// Falls back to RemoteIp if claim absent (mirrors Auth rate-limit precedent)
opt.AddPolicy(names.MatchmakingEnqueue, httpContext =>
{
    var playerId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var partitionKey = string.IsNullOrEmpty(playerId)
        ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        : $"player:{playerId}";
    return RateLimitPartition.GetSlidingWindowLimiter(partitionKey,
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 });
});
```

5 enqueue requests/min/player is generous for normal gameplay (a player re-queues only after a match or cancel) and tight enough to prevent spam. `SlidingWindowLimiter` over `FixedWindowLimiter` to prevent boundary-burst abuse.

### Decision 11 — Admin UI Live Panels

**`IMatchmakingObservability` port:**
```csharp
public interface IMatchmakingObservability
{
    Task<MatchmakingQueueStats> GetQueueStatsAsync(CancellationToken ct);
}

public record MatchmakingQueueStats(
    IReadOnlyList<PoolDepth> Pools,
    int ActiveLeaseCount,       // 1 if leader lock held, 0 if not
    string? LeaderInstanceId,   // value of the lock key (MachineName:Guid format)
    DateTimeOffset AsOf);

public record PoolDepth(Guid LadderId, string PoolName, long Depth);
```

**`RedisMatchmakingObservability` adapter:** On `GetQueueStatsAsync`, SCAN for `mm:queue:*` keys, ZCARD each, GET `gamekit:matchmaking:matcher:lock` for leader identity. Register this adapter only when `AddMatchmaking()` is called — the `QueueDepth.razor` page already uses reflection-safe null-check to detect presence (see `QueueDepth.razor` code read above).

**New admin verbs:** `pause-queue` and `drain-queue` wired through `AdminCommandRegistry` + `AdminAuditActions` + `AuditSentenceTemplates`. Actions:
- `admin.matchmaking.pause_queue` — sets a Redis key `mm:control:paused` that the ticker checks before processing
- `admin.matchmaking.drain_queue` — sets `mm:control:drain` + expires all sorted set entries in the next tick (or signals the ticker to ZREMRANGEBYSCORE everything)

**Audit sentence templates (to add to `AuditSentenceTemplates`):**
- `pause-queue`: `"Paused matchmaking queue for ladder {target}."`
- `drain-queue`: `"Drained matchmaking queue for ladder {target}."`

### Decision 12 — Per-Package EF Migration

```csharp
public static class MatchmakingMigrationConstants
{
    public const string MigrationsHistoryTable = "__ef_migrations_matchmaking";
    // Live-verify: SELECT hashtext('gamekit.matchmaking.migrations')::bigint
    // Placeholder — MUST be replaced with Testcontainers-verified value in Wave 0
    public const long AdvisoryLockKey = 0L; // PLACEHOLDER — see RankingsAdvisoryLockKeyTests pattern
}
```

Verification test (`MatchmakingAdvisoryLockKeyTests`) mirrors `RankingsAdvisoryLockKeyTests` exactly — two assertions:
1. C# constant matches live `SELECT hashtext('gamekit.matchmaking.migrations')::bigint`
2. Key is distinct from Core (1800940027L), Auth (-298890956L), Admin (-2101739634L), Rankings (-156812172L)

Migration timestamp: `20260516000000_MatchmakingInitial` (next day in the deterministic-timestamp convention).

Tables in the matchmaking migration:
- `parties` — id, party_code (varchar 8, unique, index), state (integer enum), owner_player_id (FK players.id), created_at, expires_at (nullable)
- `party_members` — id, party_id (FK parties.id), player_id (FK players.id), joined_at; UNIQUE (player_id) for single-active-party constraint; UNIQUE (party_id, player_id)
- `matchmaking_tickets` — id, party_id (FK parties.id nullable — solo enqueue may have no party row), ladder_id (FK ladders.id), pool_name, status (integer), queued_at, terminal_at (nullable), session_id (FK game_sessions.id nullable)
- `decline_history` — id, player_id (FK players.id), declined_at, proposal_id (UUID stored as text)
- `ticket_events` — id, ticket_id (FK matchmaking_tickets.id), event_type (integer), occurred_at, payload (jsonb nullable)

**Note on enum storage:** CONTEXT.md explicitly notes Phase 4 was "bitten by integer-cast SQL seeds" with `HasConversion<string>()`. Phase 5 **uses integer enum storage** (default EF behavior, no `HasConversion<string>()`) for party state, ticket status, and event type. Seeds use the integer value directly in migration SQL.

**Note on party_members unique constraint:** `UNIQUE (player_id)` enforces single-active-party per player. When a player dissolves their party or the party transitions to `Dissolved` state, this constraint must be released — either via DELETE on `party_members` or via a partial unique index `WHERE party.state NOT IN (Dissolved)`. Recommendation: partial unique index `CREATE UNIQUE INDEX uq_party_members_active_player ON gamekit.party_members ("PlayerId") WHERE ... ` — but EF Core partial indexes require raw SQL in the migration. Simpler: enforce in application code with a SERIALIZABLE transaction that checks for existing active party membership before INSERT.

[ASSUMED] — partial unique index approach requires raw-SQL in EF migration. The application-code enforcement approach (SERIALIZABLE check + INSERT) is consistent with how Phase 2 handles the GuestUpgrade race. Use application-code approach for v1.

### Decision 13 — 1k-Concurrent-Ticket Load Test

**Project:** `tests/GameKit.Matchmaking.LoadTests` — separate from unit/integration tests. xUnit project with `[Fact(Timeout = 15 * 60 * 1000)]`.

**Harness shape:**
```csharp
// 1. Spin up Testcontainers Postgres + Redis
// 2. Build WebApplicationFactory<MatchmakingTestApp> with 1k-ticket load config
// 3. Parallel.ForEachAsync with DegreeOfParallelism=1000: POST /api/mm/queue
// 4. Run for 10 minutes: assert via Interlocked counters
//    - matchesFormed > 0 every minute
//    - maxIterationMs (Stopwatch per MatchmakerTickerService.RunOnceAsync) ≤ configured budget
//    - no NpgsqlException.IsTransient connection pool exhaustion events
// 5. Final assertions: no duplicate game_sessions rows, ticker budget not exceeded
```

**Iteration budget:** 50ms (configurable via `GameKitMatchmakingOptions.Ticker.MaxIterationBudgetMs`). The ticker does not enforce this — it is measured externally by the load test via an `IMatchmakingObservability` OTel-compatible hook. If any iteration exceeds the budget, the test fails.

**Npgsql pool exhaustion detection:** Configure Npgsql with `Maximum Pool Size=25` in the load test (tight, to stress-test). Listen to `NpgsqlEventSource` or count `NpgsqlException` with message containing "pool" to detect exhaustion. Alternatively: check `NpgsqlConnectionPoolMetrics` via OTel if Npgsql 10 exposes this.

[ASSUMED] — exact Npgsql pool exhaustion detection API needs verification against Npgsql 10.0.x docs. Fallback: count `DbException` with `"connection pool"` in message.

### Decision 14 — Chaos Integration Test

**Recommendation: in-process simulation (not separate child process).**

Creating a separate child process with `dotnet run` in an xUnit test is slow, environment-dependent, and flaky in CI. The in-process approach is:

1. Build a `WebApplicationFactory<MatchmakingTestApp>` backed by Testcontainers
2. Enqueue 100 parties (50 tickets)
3. Inject a `IChaosInterceptor` that throws `OperationCanceledException` at a specific point in the Lua claim flow — simulating a crash mid-match
4. Run the ticker once — some matches are formed, some are interrupted
5. Reset the interceptor
6. Run the reconciler explicitly via `MatchmakingReconcilerService.RunSweepOnceAsync()`
7. Assert:
   - No duplicate `game_sessions` rows (SELECT COUNT(*) with participant overlap check)
   - No ghost keys in Redis (SCAN `mm:ticket:*` — all claimed tickets should be absent)
   - `matchmaking_tickets` rows for interrupted tickets have status `Expired`
   - No player appears in two active sessions

The `IChaosInterceptor` is registered only in test builds (not exposed in the public package surface).

### Decision 15 — Leader-Election Integration Test

Mirror the `RankingsTickerLeaderElectionTests` pattern exactly:

```csharp
// Build two separate ServiceProviders pointing at the same Redis + Postgres Testcontainers
await using var sp1 = BuildMatchmakerServiceProvider(cs, redisCs, suffix: "1");
await using var sp2 = BuildMatchmakerServiceProvider(cs, redisCs, suffix: "2");
var ticker1 = sp1.GetRequiredService<IMatchmakerTicker>();
var ticker2 = sp2.GetRequiredService<IMatchmakerTicker>();
// Flush stale lock key
await db.KeyDeleteAsync("gamekit:matchmaking:matcher:lock");
// Run both concurrently — exactly one drains
var results = await Task.WhenAll(ticker1.RunOnceAsync(ct), ticker2.RunOnceAsync(ct));
Assert.Single(results, r => r == MatcherTickResult.Matched || r == MatcherTickResult.NoMatch);
Assert.Single(results, r => r == MatcherTickResult.LockNotAcquired);
```

**Forced failover test:** After leader releases lock, manually force TTL expiry by deleting the key, then run non-leader — it should now acquire. Timing: `LeaseTtl` default = 90s (matches rankings). For the test, use a reduced TTL (5s) injected via options override. Wait `LeaseTtl + 1s` then assert the former non-leader acquires.

### Decision 16 — Sample App Demonstration

`TicTacToeDuel` gets a 1v1 enqueue path. No party UI. Changes to `samples/TicTacToeDuel/Program.cs`:
```csharp
builder.Services
    .AddGameKit(opts => { ... })
    .AddRankings()
    .AddMatchmaking(opts =>
    {
        opts.Ticker.TickIntervalMs = 500;
    })
    .AddLadder("tictactoe", ladder =>
    {
        ladder.BracketStart = 100;
        ladder.BracketEnd = 500;
        ladder.BracketRampSeconds = 40;
        ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
    });
```

README section: documents the full party-create / party-join flow (POST /api/parties, POST /api/parties/join) and the 1v1 enqueue path. No Razor/Blazor UI changes for parties.

### Decision 17 — NuGet Dependencies for GameKit.Matchmaking

No new packages. All deps are already in `Directory.Packages.props`:

| Package | Version | Purpose |
|---------|---------|---------|
| `StackExchange.Redis` | 2.8.41 | Live queue sorted sets + pub/sub + distributed lock |
| `Microsoft.EntityFrameworkCore` | 10.0.6 | ORM for 4 new entities |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 | Postgres provider |
| `Polly` | 8.5.2 | Non-HTTP retry pipeline for Redis (direct; not via Http.Resilience) |
| `FluentValidation` | 12.1.1 | Enqueue + party request validation |
| `System.Threading.Channels` | BCL (net10.0) | `Channel<TicketEvent>` — no NuGet reference needed |
| `System.Diagnostics.Metrics` | BCL (net10.0) | OTel `Meter` for dropped-events counter |

`GameKit.Matchmaking.csproj` adds `ProjectReference` to `GameKit.Core` (already declared) and must also add `ProjectReference` to `GameKit.Rankings` because `EloRangeMatchmakingStrategy` reads `player_ranks.rating / rd` from the shared Postgres schema (same pattern as the `D-22` session-complete port). The dependency direction: `GameKit.Matchmaking` → `GameKit.Rankings` → `GameKit.Core`.

[ASSUMED] — the `GameKit.Matchmaking` → `GameKit.Rankings` project reference is a new coupling. Verify this does not create a circular dependency (it does not — Rankings has no reference to Matchmaking). This coupling is intentional: the default strategy reads ratings, and the Phase 5 CONTEXT explicitly says "EloRangeMatchmakingStrategy reads `player_ranks.rating / rd / volatility` directly."

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | Inherits from `Directory.Build.props` (xUnit auto-detected) |
| Unit test run | `dotnet test tests/GameKit.Matchmaking.Tests/ -x` |
| Integration test run | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ -x` |
| Load test run (phase gate) | `dotnet test tests/GameKit.Matchmaking.LoadTests/ --no-build` |

### Test Projects to Create

| Project | Parallels | Content |
|---------|-----------|---------|
| `tests/GameKit.Matchmaking.Tests/` | `GameKit.Rankings.Tests/` | Unit tests: bracket flex math, `GlickoWeighted` aggregator, cooldown escalation logic, party code generation, `MatchmakerLeaseHelper` mock, `TicketEvent` channel drop behavior |
| `tests/GameKit.Matchmaking.Integration.Tests/` | `GameKit.Rankings.Integration.Tests/` | Integration: migration determinism, advisory lock key, party CRUD endpoints, enqueue endpoint, reconciler sweep, leader election (2 replicas), chaos test, rate-limit test, admin observability panel |
| `tests/GameKit.Matchmaking.LoadTests/` | *(new — no prior precedent)* | Phase gate: 1k-concurrent-ticket load test (Testcontainers, extended timeout, budget assertion) |

### Fixture Shape

```csharp
// MatchmakingFixture mirrors RankingsFixture — composes PostgresFixture + RedisFixture
[CollectionDefinition("Matchmaking")]
public sealed class MatchmakingCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

// MatchmakingTestModelCustomizer — same Pitfall §3 pattern as TickerTestModelCustomizer
internal sealed class MatchmakingTestModelCustomizer : RelationalModelCustomizer { ... }
```

### `IClock` / `StepClock` Injection Points

- `EloRangeMatchmakingStrategy.Match(...)` receives `DateTimeOffset now` from the caller (ticker passes `_clock.UtcNow`) — deterministic for tests
- `MatchmakingReconcilerService` consumes `IClock` for stale threshold calculations
- `MatchmakingRetentionCleanupService` consumes `IClock` for cutoff calculations
- `IMatchmakingService.EnqueueAsync(...)` records `queuedAt = _clock.UtcNow`

`StepClock` (already exists in `tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs`) can be reused by adding it to `GameKit.TestFixtures`.

### Phase SC → Test Class Mapping

| SC | Assertion | Test class |
|----|-----------|-----------|
| SC#1 | Party of 1-N enqueues; bracket widens 100→500 in ~40s; `matchmaking_tickets` written async | `MatchmakingHappyPathTests` (integration) — use `StepClock` to advance `queuedAt` by 40s, assert bracket at each step |
| SC#2 | Chaos: kill mid-match → no duplicate sessions, no ghost keys, no player in 2 active sessions | `MatchmakingChaosTests` (integration) — `IChaosInterceptor` abort + reconciler sweep + assertions |
| SC#3 | 1k concurrent tickets, 10 min, no budget exceeded, no pool exhaustion | `MatchmakingLoadTests` (load test project) — phase gate |
| SC#4 | 2 replicas share Redis; exactly one holds lock; forced failover within lease TTL | `MatchmakingLeaderElectionTests` (integration) — mirrors `RankingsTickerLeaderElectionTests` |
| SC#5 | Rate-limit returns 429 on spam; no duplicate tickets | `MatchmakingRateLimitTests` (integration) — `WebApplicationFactory` + rapid-fire POST |
| SC#6 | Admin panel shows live Redis state; not from Postgres reconciliation mirrors | `MatchmakingObservabilityTests` (integration) — enqueue N tickets, call `IMatchmakingObservability.GetQueueStatsAsync`, assert ZCARD matches |

### Wave 0 Gaps

- [ ] `tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj` — unit test project
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` — integration test project
- [ ] `tests/GameKit.Matchmaking.LoadTests/GameKit.Matchmaking.LoadTests.csproj` — load test project
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` — `[CollectionDefinition("Matchmaking")]` mirroring Rankings
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestModelCustomizer.cs` — Pitfall §3 bypass
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` — Wave 0 mandatory: live-verify `hashtext('gamekit.matchmaking.migrations')::bigint` and assert distinct from all prior packages
- [ ] `StepClock` moved to `tests/GameKit.TestFixtures/` for reuse (or duplicated if that creates a coupling issue)

---

## Pitfalls

### §1 — Never Rehydrate Redis from Postgres

**What goes wrong:** On app restart after crash, the reconciler reads `matchmaking_tickets` rows in `Queued` state and tries to ZADD them back into Redis. The ticker then processes them, but the Redis queue also received new enqueue calls from clients who re-polled after the crash. Result: duplicate tickets for the same player in the live queue.

**Why it happens:** Conflating "analytics source of truth" (Postgres) with "live source of truth" (Redis).

**How to avoid:** The reconciler NEVER writes to Redis. Its only job is to UPDATE Postgres rows to terminal states (`Expired`, `Cancelled`). After a crash, Redis is empty. Clients who were mid-queue receive a timeout from their long-poll and re-enqueue. The reconciler cleans up the orphaned Postgres rows.

**Warning signs:** ZADD calls in `MatchmakingReconcilerService` code — any such call is a bug.

### §2 — Lease Lost Mid-Tick: Stale Write After Lock Expiry

**What goes wrong:** The ticker acquires the lock, begins processing 500 tickets, takes longer than `LockTtlSeconds`, lock expires, another replica acquires the lock and begins processing. Both replicas execute the Lua claim script against overlapping candidates, producing duplicate proposals.

**Why it happens:** Lock TTL too short, or tick work volume exceeds budget.

**How to avoid:**
1. `RenewLeaseAsync` before processing each pool (mirrors `RankingsTickerService` line 180 pattern exactly — the pattern is already proven)
2. Lua script includes lease-value fencing check (Decision 3 above) — script returns `LEASE_LOST` if the lock value changed between renewal and EVAL
3. Keep tick budget low enough that renewal happens frequently (process N tickets per pool, then renew)

**Warning signs:** Two replicas each logging `lock acquired` without a `lock released` from the first; OTel traces showing two concurrent drain spans.

### §3 — CLI Model Customizer for Cross-Package Entities (carry-forward from Phase 4 Pitfall §3)

**What goes wrong:** Integration tests that need both Core + Matchmaking entities in a single `GameKitDbContext` hit EF's global model cache. The default `IModelCustomizer` only applies extensions registered in DI. A test that builds its own `ServiceCollection` without the runtime app's DI extensions gets a context missing Matchmaking entities.

**Why it happens:** EF Core caches the compiled model per `DbContextOptions` configuration. The global model cache key is based on the `DbContextOptions` — without the `ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()` override, the cached model from a prior test (built without matchmaking entities) is reused.

**How to avoid:** Every test `ServiceProvider` that needs matchmaking entities must call `.ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()` on the `DbContextOptionsBuilder`. `MatchmakingTestModelCustomizer : RelationalModelCustomizer` explicitly calls `new MatchmakingModelBuilderExtension().ApplyTo(modelBuilder)` in `Customize(...)`. Mirrors `TickerTestModelCustomizer` exactly.

### §4 — Cooldown Timezone Bug

**What goes wrong:** Cooldown thresholds computed with `DateTime.Now` (local time) instead of `DateTimeOffset.UtcNow`. On a server where the system timezone differs from UTC, the cooldown window is off by the UTC offset. Players in UTC+9 might get a cooldown that expires 9 hours early; players in UTC-8 get one 8 hours too long.

**Why it happens:** Using `DateTime.Now` anywhere in the cooldown query or insertion path.

**How to avoid:** All timestamps use `IClock.UtcNow` (`DateTimeOffset`). Postgres stores `TIMESTAMPTZ` columns. The cooldown query: `WHERE declined_at > (@now - @window)` where `@now` is from `IClock.UtcNow`.

**Warning signs:** Any use of `DateTime` (non-Offset) in Matchmaking code. `DateTime.Now` is a compile-time warning if `#pragma warning error CS0618` is added — but more practically, enforce via code review checklist.

### §5 — Long-Poll Connection Leak on Client Abandon

**What goes wrong:** A client opens `GET /api/mm/queue/{ticketId}/status`. The client closes the connection (browser tab closed, mobile app backgrounded). The ASP.NET Core `HttpContext.RequestAborted` `CancellationToken` fires. If the handler does not propagate this token to the Redis subscriber, the `ISubscriber` channel registration remains open until the 30s timeout fires.

At 1k concurrent long-polls, this means up to 1k × 30s = 30k "orphaned" subscriptions accumulating before they time out.

**Why it happens:** Using a fixed `CancellationTokenSource.CancelAfter(30s)` without linking `HttpContext.RequestAborted`.

**How to avoid:** Always link both tokens:
```csharp
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    HttpContext.RequestAborted,
    CancellationTokenSource.CreateLinkedTokenSource().Token);
linkedCts.CancelAfter(TimeSpan.FromSeconds(30));
```
Put the unsubscribe in a `finally` block:
```csharp
finally { await subscriber.UnsubscribeAsync(channel); }
```

**Warning signs:** StackExchange.Redis connection stats showing growing subscriber count over time; memory leak in long-running tests.

### §6 — Redis Sorted-Set Ordering Ambiguity on Tie Score

**What goes wrong:** Two tickets queued at the same Unix second get identical scores in the sorted set. `ZRANGEBYSCORE` returns them in an undefined order (Redis returns them in lexicographic member-value order as a tiebreaker). The matcher picks candidate pairs based on position — the "oldest waiter first" guarantee breaks when multiple tickets have the same second-granularity score.

**Why it happens:** Using `DateTimeOffset.ToUnixTimeSeconds()` (second granularity) instead of milliseconds for the score.

**How to avoid:** Use `DateTimeOffset.ToUnixTimeMilliseconds()` for the sorted-set score. This reduces collision probability from "every ticket in the same second" to "every ticket in the same millisecond." For bracket flex, `t` is computed from `(now - ticket.queuedAt)` where `queuedAt` is reconstructed from the score — use the same millisecond precision throughout.

**Warning signs:** Unit tests for bracket flex failing on 1-second boundary because scores tie and ZRANGEBYSCORE returns unexpected ordering.

### §7 — Analytics Channel Drop Counter Increment Without OTel Meter Registration

**What goes wrong:** The drain service increments `matchmaking.analytics.dropped_events` via `Counter<long>.Add(1)` but the operator never sees the metric because they didn't register `AddMeter("GameKit.Matchmaking")` in their OTel SDK setup. The counter increments silently into a no-op instrument.

**Why it happens:** OTel instruments are no-ops when no MeterProvider subscribes. This is by design (opt-in) but creates an observability blind spot.

**How to avoid:** XML doc on `AddMatchmaking()` must prominently state: "To observe dropped analytics events, register `AddMeter("GameKit.Matchmaking")` in your OpenTelemetry SDK setup." This matches the Phase 4 `ActivitySource("GameKit.Rankings.Ticker")` documentation pattern.

**Warning signs:** `matchmaking.analytics.dropped_events` counter always 0 in operator dashboards even during a Postgres outage.

### §8 — Npgsql Pool Exhaustion Under Load

**What goes wrong:** The load test enqueues 1k tickets, each triggering a Channel write. The drain service batches 100 events per INSERT. If Postgres is slow, the drain service may hold connections longer than expected. Simultaneously, the reconciler and retention service each open their own scoped `GameKitDbContext` connections. At 1k concurrent tickets, the default Npgsql pool size (100 connections) may be exhausted.

**Why it happens:** Too many services competing for the Npgsql pool simultaneously. The drain service's retry loop holds connections across Polly retry sleeps.

**How to avoid:**
1. Drain service batch connection lifetime: open connection, INSERT batch, close connection. Do not hold the connection across the Polly retry sleep.
2. `AddTimeout` on the Polly pipeline (30s) so a hung Postgres call releases the connection within a bounded time.
3. Document `Maximum Pool Size` recommendation in the ops guide: 25–50 for a typical single-server deployment. Load test verifies at `MaxPoolSize=25`.

### §9 — Party Code Case-Insensitivity in Postgres

**What goes wrong:** Party code is stored as `varchar(8)` with a `UNIQUE` constraint. A player submits `k7q3m2` (lowercase) to join a party created with code `K7Q3M2`. The lookup `WHERE party_code = @code` does a case-sensitive comparison (default Postgres string comparison is case-sensitive for `varchar`). No party found. 

**Why it happens:** D-02 says codes are case-insensitive, but the default Postgres `varchar` type IS case-sensitive.

**How to avoid:** Two options:
- A: `CITEXT` column type (Postgres extension) — automatically case-insensitive comparisons. Phase 2 Auth already uses `CREATE EXTENSION IF NOT EXISTS citext` in the migration. Phase 5 matchmaking migration can reuse this extension.
- B: Store and compare in uppercase: `party_code = @code.ToUpperInvariant()` in application code.

**Recommendation: Option A (citext).** The Auth migration already creates the extension. Matchmaking migration declares `party_code CITEXT NOT NULL UNIQUE` — the `UNIQUE` index on `citext` is automatically case-insensitive.

**Warning signs:** `POST /api/parties/join` returns 404 for valid codes when the client submits lowercase.

### §10 — Accept-Flow Partial Accept Race

**What goes wrong:** A 4-player proposal has 3 of 4 players accept. The 4th player's accept and the 10s TTL expiry hit Redis at the same moment. The proposal hash is gone (TTL expired). The 4th player's `POST /api/mm/proposal/{id}/accept` returns 404 (proposal not found). The 3 accepting players' tickets were already de-queued. They are left in limbo — not re-queued (D-09 only applies to the accepting parties when the *decline* fires, not TTL expiry).

**Why it happens:** No atomic check-and-complete step. The 3 accepting players' tickets need to be re-queued regardless of whether the 4th player accepts or times out. The proposal TTL expiry is the "timeout" event — the proposal reaping service (or the ticker's proposal-sweep step) must detect proposals that have expired without full acceptance and re-queue all accepting parties.

**How to avoid:** The ticker's proposal sweep (run once per tick, leader only) must:
1. SCAN for `mm:proposal:*` keys (with a cursor, not KEYS)
2. For each that has `state=pending` and TTL near zero or expired: read accepted ticket IDs
3. Re-ZADD them with their original `queuedAt` score
4. PUBLISH `mm:status:{ticketId}` "cancelled" to any waiting long-polls

This is done in the ticker's proposal-reaping step, run after the main match-formation step. The drain service records `TimedOut` events for non-accepted parties.

---

## Open Questions for the Planner (RESOLVED)

> All six open questions surfaced during research were resolved during planning. Each item below carries an inline `**RESOLVED:**` annotation citing the closing plan + decision. The phase's `05-09-PLAN.md` SUMMARY block also enumerates these resolutions for cross-reference.

**OQ-1 — Exact Lua script implementation.** The atomic-claim Lua script semantics are specified in Decision 3. The exact Lua source, its KEYS/ARGV binding, and its error return codes must be authored as a plan task with a unit test against a real Redis (Testcontainers). Flag this as the first deliverable in Wave 1.

**RESOLVED:** Closed in **05-04** (Task 3). The inline Lua source (≤30 lines) lives in `src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs` and is executed via `IDatabase.ScriptEvaluateAsync` from `EloRangeMatchmakingStrategy` / the matchmaker ticker. The script's KEYS/ARGV binding + the `OK` / `LEASE_LOST` / `TICKET_GONE` return semantics are verified by `AtomicClaimScriptTests` against a Testcontainer Redis (4 [Fact]s, including the EVALSHA fast-path).

**OQ-2 — `party_members` active-party uniqueness enforcement.** Decision 12 recommends application-code enforcement (SERIALIZABLE transaction) over a partial unique index. The planner should decide whether to include a non-partial unique index and handle the constraint violation in code, or use a partial index with raw migration SQL. Either works; the tradeoff is EF migration complexity vs. application-code complexity.

**RESOLVED:** Closed in **05-02** (Task 1 + Task 3). The EF config for `PartyMember` declares a composite UNIQUE on `(PartyId, PlayerId)` only (NOT a partial index); the Postgres-level active-party uniqueness is enforced in application code by `PartyService.CreateAsync` / `JoinAsync` running under `IsolationLevel.Serializable` (Plan 05-04 Task 2). The initial migration `20260516000000_MatchmakingInitial` emits the composite UNIQUE constraint; no raw partial-index SQL is required. Tradeoff chosen per RESEARCH §OQ-2 recommendation.

**OQ-3 — `GameKit.Matchmaking` → `GameKit.Rankings` project reference.** If the planner decides not to take the Rankings project reference (to keep packages decoupled), the default strategy's rating read must be done via a query against the shared `player_ranks` table using the shared `GameKitDbContext` — which already includes Rankings entities at runtime (since both packages contribute to the same context via `IModelBuilderExtension`). No project reference is needed at the C# layer if the strategy uses the ORM (EF already includes the entity). Confirm: the strategy reads `player_ranks` via EF `ctx.Set<PlayerRank>()` — available at runtime even without a csproj `ProjectReference`. The planner should verify this is safe and update Decision 17 accordingly. [ASSUMED — low risk, but confirm before Wave 2 plan.]

**RESOLVED:** Closed in **05-02** (Task 1). `GameKit.Matchmaking.csproj` declares a `ProjectReference` to `GameKit.Rankings` — the coupling is intentional (the default strategy reads `PlayerRank` via the shared context, and the test-side `MatchmakingTestModelCustomizer` applies BOTH `MatchmakingModelBuilderExtension` and `RankingsModelBuilderExtension`). Circular reference was verified absent (Rankings does not back-reference Matchmaking). Decision 17 is now authoritative in the codebase: take the reference, keep model-customizer discipline.

**OQ-4 — `MatchmakingRetentionCleanupService` scope vs. `MatchmakingAnalyticsDrainService`.** Both services call `ExecuteDeleteAsync` / batch INSERT on `matchmaking_tickets`. Run the retention service during the load test at the same time as the drain service to catch any write-write contention under Npgsql pool pressure.

**RESOLVED:** Closed in **05-10** (load test). `LoadTestFixture` runs the host with `Maximum Pool Size=25` (Pitfall §8 mitigation) and does NOT disable the reconciler or retention services during the 10-minute sustain — both run on their natural schedules concurrently with the drain. `MatchmakingAnalyticsDrainService.FlushBatch` opens/closes the Postgres connection per batch and releases it across Polly retry sleeps (Pitfall §8), so a hung retention sweep cannot starve the drain. SC#3's `Pool.AssertNoPoolExhaustion()` gates this verification.

**OQ-5 — Admin `pause-queue` / `drain-queue` verb full wiring.** Specifying these as `AdminCommandRegistry` entries requires knowing the exact `TargetType` for the commands (ladder? global?). For v1, recommend: both verbs target the entire queue (global scope), not per-ladder. Concrete UX: a confirmation dialog in the admin UI (no target type). The planner should confirm this with the admin wiring pattern before adding to `AdminCommandRegistry`.

**RESOLVED:** Closed in **05-08** (Task 3). Reversed RESEARCH's v1 recommendation: both `pause-queue` and `drain-queue` are registered with `RequiresTarget: true` (per-ladder scope) in `AdminCommandRegistry`. The Redis control key is per-ladder (`mm:control:paused:{ladderId}`) and the ticker checks it inside the per-pool loop. Per-ladder scope was chosen so an operator can pause matchmaking for one ladder (e.g. season changeover) without disrupting others. The audit row carries `targetType=ladder, targetId=ladderId`.

**OQ-6 — `QueueDepth.razor` full implementation.** The page currently has a placeholder (`"Queue telemetry will render when GameKit.Matchmaking ships"`). Phase 5 must replace this with a real panel consuming `IMatchmakingObservability`. This is a Blazor Server (`GameKit.Admin.UI`) change — it requires a `ProjectReference` from `GameKit.Admin.UI` to `GameKit.Matchmaking`. Or: use the same reflection-safe approach the page already uses to detect the interface, and resolve it from DI without a direct project reference. Recommendation: add the `ProjectReference` (analogous to how `GameKit.Admin.UI` already references `GameKit.Auth`). The planner should confirm the dependency direction.

**RESOLVED:** Closed in **05-08** (Task 3) per D-22's port-and-adapter pattern. `IMatchmakingObservability` is declared in `src/GameKit.Matchmaking/Services/` and bound via standard DI; `GameKit.Admin.UI.csproj` gains a `ProjectReference` to `GameKit.Matchmaking` (mirrors the existing Admin.UI → Auth coupling). `QueueDepth.razor` `[Inject]`s `IMatchmakingObservability` with compile-time type safety — no reflection. The existing Phase 3 reflection-safe `Type.GetType` fallback block is preserved as a defensive scaffold for hosts that prune the Matchmaking package from their build.

---

## Standard Stack

### Core (all already pinned in `Directory.Packages.props` — no new packages)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `StackExchange.Redis` | **2.8.41** | Sorted sets (queue), hashes (tickets/proposals), pub/sub (long-poll notify), distributed lock (leader election) | [VERIFIED: Directory.Packages.props] |
| `Microsoft.EntityFrameworkCore` | **10.0.6** | ORM for 5 new entities | [VERIFIED: Directory.Packages.props] |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.1** | Postgres provider; `citext` support via extension | [VERIFIED: Directory.Packages.props] |
| `Polly` | **8.5.2** | Non-HTTP retry pipeline for Redis reconnect + analytics drain Postgres retry | [VERIFIED: Directory.Packages.props — Phase 4 plan 06 pin] |
| `FluentValidation` | **12.1.1** | Enqueue + party request validation | [VERIFIED: Directory.Packages.props] |
| `System.Threading.Channels` | BCL (net10.0) | `Channel<TicketEvent>` bounded channel — no NuGet reference | [VERIFIED: BCL since .NET Core 2.1; confirmed in net10.0 shared framework] |
| `System.Diagnostics.Metrics` | BCL (net10.0) | `Meter` + `Counter<long>` for dropped-events OTel | [VERIFIED: BCL] |
| `System.Diagnostics.ActivitySource` | BCL (net10.0) | Opt-in distributed tracing | [VERIFIED: BCL, same as Rankings `ActivitySource("GameKit.Rankings.Ticker")`] |

### Explicitly NOT Added

| Library | Why Not |
|---------|---------|
| `Microsoft.Extensions.Http.Resilience` | HTTP resilience only — Redis is not HTTP. Use raw `Polly` per CLAUDE.md §7. |
| `WireMock.Net` | No HTTP egress from `GameKit.Matchmaking` — matchmaking makes no outbound HTTP calls. |
| `SignalR` | Deferred per D-10 / deferred section of CONTEXT.md. Long-poll only in v1. |
| Any new NuGet package | Not required. All deps already pinned. |

---

## Package Legitimacy Audit

**Determination: Not applicable for Phase 5.**

Phase 5 introduces zero new NuGet package references. All packages it consumes were pinned and audited in Phases 1–4. The slopcheck / legitimacy gate exists to catch hallucinated package names in fresh installs; Phase 5 has no fresh installs.

| Package | Registry | Disposition |
|---------|----------|-------------|
| All packages | Already pinned (Phases 1–4) | Approved — no re-audit required |

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| `WATCH/MULTI/EXEC` for atomic queue claim | Lua script via `IDatabase.ScriptEvaluateAsync` | Lua is server-side atomic; WATCH can fail on concurrent access requiring client retry |
| `Channel` with unbounded capacity | Bounded `Channel<T>` with drop-on-full | Prevents memory OOM under sustained Postgres outage |
| `IDatabase.LockTakeAsync` raw string | `IDatabase.LockTakeAsync` with Lua-verified release (StackExchange.Redis built-in) | Phase 4 established this as the "Don't Hand-Roll" pattern — reuse |
| Per-match rating updates (Glicko-2) | Batched rating period via ticker (Phase 4 pattern) | Phase 5 matchmaker only *creates* game sessions — it does not touch ratings |

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Atomic multi-ticket claim | Custom WATCH/MULTI/EXEC with retry loop | Lua script via `IDatabase.ScriptEvaluateAsync` | Lua is atomically executed by Redis; WATCH retry under contention produces O(N) network round trips |
| Redis distributed lock | `StringSetAsync(k, v, ttl, When.NotExists)` + manual Lua release | `IDatabase.LockTakeAsync / LockExtendAsync / LockReleaseAsync` | Built-in StackExchange.Redis Lua-verified release. Already proven in `RankingsTickerLeaseHelper`. |
| Party code generation | UUID substring or `Random.Next` | `RandomNumberGenerator.GetBytes(5)` → Crockford base32 encode | Cryptographically random; Crockford base32 (no I/L/O/0/1) eliminates OCR/typing collisions |
| Bounded channel event dropping | Manual lock + list size check | `BoundedChannelOptions { FullMode = BoundedChannelFullMode.DropNewest }` | BCL handles thread-safe bounded drop correctly; hand-rolling introduces TOCTOU races |
| Background timer loop | `Task.Delay` loop | `PeriodicTimer` | `PeriodicTimer` skips missed ticks (no drift/stacking); `Task.Delay` can stack under load. Proven in `RankingsTickerService`. |

---

## Project Constraints (from CLAUDE.md)

| Directive | How Phase 5 honors it |
|-----------|----------------------|
| **GPL — no proprietary deps, no telemetry, no phone-home** | All packages GPL-compatible; OTel opt-in via `ActivitySource`/`Meter` only; zero outbound HTTP |
| **Self-hosted only. No cloud-service dependencies** | Redis + Postgres only; shipped `docker-compose.yml` already provides both |
| **.NET 10 LTS, EF Core 10.0.6, Npgsql 10.0.1** | All pinned; no version changes needed |
| **Polly v8 (direct) for non-HTTP resilience** | `Polly 8.5.2` direct for Redis reconnect + analytics drain; NOT `Microsoft.Extensions.Http.Resilience` |
| **BackgroundService + PeriodicTimer (not Hangfire/Quartz)** | All 4 background services use this pattern |
| **No MediatR / AutoMapper / Hangfire** | No mediator, no mapping library, no scheduler |
| **Per-package migration boundaries — never modify Core tables** | Matchmaking migration adds new tables only; `game_sessions` FK from `matchmaking_tickets` is a reference (no Core table modification) |
| **Integer enum storage (Phase 5 explicit rule)** | `PartyState`, `MatchmakingTicketStatus`, `TicketEventType` all use integer storage |
| **Per-package advisory-lock key distinct from Core/Auth/Admin/Rankings** | `MatchmakingAdvisoryLockKeyTests` verifies this at test time |
| **xUnit + Testcontainers + Moq for integration tests** | All 3 test projects use real Postgres + Redis via Testcontainers |
| **XML doc comments on every public API (CS1591 error)** | Enforced repo-wide by `Directory.Build.props` |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `GlickoWeighted` formula: `weight = 1/RD^2` | Decision 5 | Party aggregate rating slightly different; requires re-verification against Glickman literature |
| A2 | Polly parameters: 4 attempts, 500ms base, 30s timeout for analytics drain | Decision 7 | Operators may need to tune under high-latency Postgres; document as configurable |
| A3 | `MatchmakingReconcilerService` should be leader-only | Decision 6 | Running on all replicas is safe (idempotent UPDATEs) but adds Postgres load |
| A4 | Exact Lua script KEYS/ARGV binding | Decision 3 | Wrong binding causes Redis error; must be tested against real Redis in Wave 0 |
| A5 | `GameKit.Matchmaking` can read `player_ranks` via `GameKitDbContext` without a csproj ProjectReference to `GameKit.Rankings` | Decision 17 / OQ-3 | If EF model customizer discovery doesn't include Rankings entities at runtime without explicit registration, the strategy query fails |
| A6 | Npgsql pool exhaustion detection via `DbException` message in load test | Decision 13 | Npgsql 10 may have a dedicated exception type or metric; verify against Npgsql 10 docs |
| A7 | `StackExchange.Redis` pub/sub `ISubscriber.SubscribeAsync` holds a lightweight per-request registration (not a dedicated connection per subscriber) | Decision 9 | If each subscriber uses a dedicated connection at 1k concurrent, Redis would run out of connections |
| A8 | `QueueDepth.razor` full implementation requires a `ProjectReference` from `GameKit.Admin.UI` to `GameKit.Matchmaking` | OQ-6 | Alternative reflection-safe approach avoids the project reference at cost of losing compile-time type safety |

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | Testcontainers for all test projects | ✓ | confirmed by prior phases running Testcontainers | — |
| Redis (via Testcontainers) | All integration + load tests | ✓ | `Testcontainers.Redis 4.11.0` pinned | — |
| Postgres (via Testcontainers) | All integration + load tests | ✓ | `Testcontainers.PostgreSql 4.11.0` pinned | — |
| `.NET 10 SDK 10.0.106` | Build + test | ✓ | `global.json` pins this | — |

**Missing dependencies:** None. All runtime and test infrastructure is available.

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | Yes — JWT at `/api/mm/*` and `/api/parties/*` | `Microsoft.AspNetCore.Authentication.JwtBearer` (Phase 2); `ClaimTypes.NameIdentifier` = PlayerId for partition key |
| V3 Session Management | No — matchmaking sessions are game sessions (Phase 1 entity), not HTTP sessions | — |
| V4 Access Control | Yes — enqueue must validate JWT sub matches party owner | `ICurrentPlayer.PlayerId` claim check in `IMatchmakingService.EnqueueAsync` |
| V5 Input Validation | Yes — enqueue + party endpoints | `FluentValidation` endpoint filters (Phase 3 pattern) |
| V6 Cryptography | Party codes — must use `RandomNumberGenerator`, not `Random` | `RandomNumberGenerator.GetBytes(5)` → Crockford base32 |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Ticket spam / queue flooding | Denial of Service | Per-player rate limit `MatchmakingEnqueue` (MATCH-11, D-11) |
| Double-enqueue (same player, two simultaneous POSTs) | Tampering | Rate limit (5/min/player) + application check for existing active ticket |
| Fake proposal accept (player sends accept for a proposal they're not in) | Tampering | Proposal hash lookup verifies `player_id` is in the proposal's `ticketIds` list |
| Clock manipulation attack on cooldown | Tampering | `IClock.UtcNow` — server-authoritative; client cannot influence the clock |
| Admin queue drain without authentication | Elevation of Privilege | `pause-queue` / `drain-queue` verbs behind `AdminPolicies.Superadmin` + antiforgery filter (Phase 3 pattern) |

---

## Sources

### Primary (HIGH confidence)

- [VERIFIED: `src/GameKit.Rankings/Services/RankingsTickerService.cs`] — BackgroundService + PeriodicTimer + Polly + lease-check mid-tick pattern
- [VERIFIED: `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs`] — `LockTake / LockExtend / LockRelease` with Polly v8 pipeline
- [VERIFIED: `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs`] — nightly cleanup BackgroundService pattern
- [VERIFIED: `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs`] — advisory lock key pattern + verification test structure
- [VERIFIED: `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs`] — `IGameKitRateLimitPolicies` extension pattern
- [VERIFIED: `tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs`] — two-replica leader election test shape
- [VERIFIED: `Directory.Packages.props`] — all package versions confirmed pinned (StackExchange.Redis 2.8.41, Polly 8.5.2, EF Core 10.0.6, Npgsql 10.0.1, FluentValidation 12.1.1)
- [VERIFIED: `src/GameKit.Admin.UI/Components/Pages/QueueDepth.razor`] — reflection-safe matchmaking detection already in place; placeholder awaiting Phase 5
- [VERIFIED: `.planning/phases/05-matchmaking-parties/05-CONTEXT.md`] — all 18 locked decisions
- [VERIFIED: `.planning/REQUIREMENTS.md` lines 83–97] — MATCH-01..15 verbatim

### Secondary (MEDIUM confidence)

- [CITED: `https://redis.io/docs/latest/develop/interact/programmability/eval-intro/`] — Lua scripting in Redis (EVAL atomicity guarantee)
- [CITED: `https://redis.io/docs/latest/develop/use/patterns/distributed-locks/`] — Redlock / single-node distributed lock pattern
- [CITED: `https://www.pollydocs.org/strategies/retry`] — Polly v8 `ResiliencePipelineBuilder` retry configuration
- [CITED: `https://redis.io/docs/latest/develop/interact/pubsub/`] — Redis Pub/Sub pattern for event notification

### Tertiary (LOW / ASSUMED)

- [ASSUMED] `GlickoWeighted` formula `weight = 1/RD^2` — derived from standard Glicko theory; verify against Glickman's original paper before implementation
- [ASSUMED] Npgsql 10 pool exhaustion detection via `DbException` message — verify against Npgsql 10 release notes
- [ASSUMED] `StackExchange.Redis` pub/sub shared subscriber connection at 1k scale — verify in load test

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already pinned and proven in prior phases
- Architecture: HIGH — direct mirror of Phase 4 patterns with matchmaking-specific additions; all locked decisions from CONTEXT.md
- Pitfalls: HIGH for §1–§3 (prior phase experience); MEDIUM for §4–§10 (derived from design analysis)
- Load test / chaos test shapes: MEDIUM — pattern is sound; exact implementation details need authoring in Wave 0

**Research date:** 2026-05-16
**Valid until:** 2026-08-16 (90 days — StackExchange.Redis and Polly are stable; Npgsql 10 is newly GA but stable for .NET 10 LTS)
