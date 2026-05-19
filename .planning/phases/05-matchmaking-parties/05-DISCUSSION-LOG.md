# Phase 5: Matchmaking + Parties — Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-16
**Phase:** 05-Matchmaking + Parties
**Areas discussed:** Party model & lifecycle, Match-found flow, Bracket flex curve & config knobs, Postgres async-write durability semantics

---

## Party model & lifecycle

### Q1: Durable entity vs runtime construct?

| Option | Description | Selected |
|--------|-------------|----------|
| Runtime-only (Recommended) | Client sends `{ playerIds, ladderId }` on enqueue. No `parties` table. | |
| Durable parties (table + invite codes) | Add `parties` + `party_members` tables, party-code/invite endpoints. | ✓ |
| Hybrid — durable only when N>1 | Solo runtime-only; N>1 creates transient `parties` row. | |

**User's choice:** Durable parties (table + invite codes)
**Notes:** Bigger upfront surface but persistent identity for cross-session play and invite flows.

### Q2: How does a player join someone else's party?

| Option | Description | Selected |
|--------|-------------|----------|
| Short code (Recommended) | 6–8 char code minted on create; `POST /api/parties/join` with `{ code }`. | |
| Direct invite by PlayerId | `POST /api/parties/{id}/invite` + `party_invites` table + accept endpoint. | |
| Both — code OR direct invite | Both flows. | |
| Code now, direct invite later | Code-based join in v1; direct invite deferred to future phase. | ✓ |

**User's choice:** Code now, direct invite later
**Notes:** No `party_invites` stub in v1 — future phase adds it once friends list exists.

### Q3: Party member disconnect mid-queue — what happens?

| Option | Description | Selected |
|--------|-------------|----------|
| Cancel the whole ticket, party stays (Recommended) | Ticket cancelled, party row survives, can re-enqueue. | ✓ |
| Shrink ticket to N-1 and continue queuing | Drop the DC'd member, keep matching at new size. | |
| Configurable per ladder | `MatchmakingDisconnectPolicy = CancelTicket \| ShrinkAndContinue`. | |

**User's choice:** Cancel the whole ticket, party stays
**Notes:** Per-ladder override deferred — single hard-coded behavior in v1.

### Q4: Cross-provider parties allowed?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — PlayerId is the only key (Recommended) | References canonical `Player` row; multi-identity model honored. | ✓ |
| No — same-provider only | Reject join if joiner's primary provider differs from owner's. | |
| You decide | Default to PlayerId-only. | |

**User's choice:** Yes — PlayerId is the only key

---

## Match-found flow

### Q1: Immediate session creation or accept-step?

| Option | Description | Selected |
|--------|-------------|----------|
| Immediate — create session, return result (Recommended for v1) | One entity to reconcile; no AFK gate. | |
| Accept-step with timeout (Dota/CS-style) | `match_proposals` row, players must accept within window. | ✓ |
| Configurable per ladder | `RequireAccept` toggle. | |

**User's choice:** Accept-step with timeout (Dota/CS-style)
**Notes:** Bigger chaos surface (proposals need their own reconciliation path) but better ranked-play UX.

### Q2: Accept timeout duration?

| Option | Description | Selected |
|--------|-------------|----------|
| 10 seconds (CS:GO-style) | Tight, forces engagement, fast churn. | ✓ |
| 30 seconds (Dota-style, Recommended) | Comfortable for tab-switched players. | |
| 60 seconds | Generous; slows queue on declines. | |
| Configurable per ladder | `AcceptTimeoutSeconds`. | |

**User's choice:** 10 seconds (CS:GO-style)
**Notes:** Single global value in v1; per-ladder override deferred.

### Q3: Decline / timeout penalty?

| Option | Description | Selected |
|--------|-------------|----------|
| No penalty | Just dequeue, free to re-enqueue. | |
| Short cooldown (Recommended) | N-min lockout (default 3–5). Redis TTL. | |
| Escalating cooldown | 3 → 15 → 30 min with `decline_history` table. | ✓ |
| You decide | Default = short fixed. | |

**User's choice:** Escalating cooldown
**Notes:** Adds a Postgres `decline_history` table for persistence across app restart.

### Q4: What happens to accepting parties when proposal fails?

| Option | Description | Selected |
|--------|-------------|----------|
| Auto re-queue at front with original bracket flex (Recommended) | Preserve `queuedAt`; re-insert at front. | ✓ |
| Auto re-queue at back, reset bracket | Back of queue, ±100 start. | |
| Return to client — they re-enqueue manually | Client surfaces UI. | |

**User's choice:** Auto re-queue at front with original bracket flex

### Q5: How do clients learn a match was found?

| Option | Description | Selected |
|--------|-------------|----------|
| Long-poll (Recommended) | `GET /api/mm/queue/{id}/status` holds up to 30s. | ✓ |
| Short-poll every 2s | Simple but high request count. | |
| Push via SignalR | Best UX; adds runtime dep deferred in Phase 4. | |

**User's choice:** Long-poll
**Notes:** Short-poll fallback NOT shipped; SignalR deferred to a later phase.

---

## Bracket flex curve & config knobs

### Q1: Default bracket-widening curve shape?

| Option | Description | Selected |
|--------|-------------|----------|
| Linear ramp 100 → 500 over 40s (Recommended for v1) | `bracket(t) = min(100 + (400·t/40), 500)`. | ✓ |
| Step function (100 / 250 / 500 at 0/15/30s) | Discrete steps. | |
| Exponential / sigmoid | Better quality early. | |
| Linear ramp with NO cap after 40s | Never starves queue. | |

**User's choice:** Linear ramp 100 → 500 over 40s

### Q2: Where to configure the curve?

| Option | Description | Selected |
|--------|-------------|----------|
| Per ladder (Recommended) | `AddLadder(opts => opts.BracketStart/End/RampSeconds)`. | ✓ |
| Single global default | One curve for all ladders. | |
| Strategy-implementation choice (no curve API) | Customers swap strategy. | |

**User's choice:** Per ladder

### Q3: Multi-player party rating aggregation?

| Option | Description | Selected |
|--------|-------------|----------|
| Mean party rating (Recommended) | Simple average. | |
| Max party rating | Highest member's number. | |
| Glicko-2 weighted (rd-aware) | Lower-RD ratings count more. | |
| Configurable per ladder | Enum `Mean \| Max \| GlickoWeighted`. | ✓ |

**User's choice:** Configurable per ladder
**Notes:** Default = Mean unless ladder overrides.

### Q4: Within-party rating spread cap?

| Option | Description | Selected |
|--------|-------------|----------|
| No cap — trust the operator (Recommended for v1) | Operator builds in custom strategy if needed. | |
| Configurable cap per ladder, default disabled | Knob present, off by default. | ✓ |
| Configurable cap, default 800 | Knob with opinionated default. | |

**User's choice:** Configurable cap per ladder, default disabled
**Notes:** Returns `400 PartyRatingSpreadExceeded` on rejection.

---

## Postgres async-write durability semantics

### Q1: Async-write implementation strategy?

| Option | Description | Selected |
|--------|-------------|----------|
| In-memory `Channel<TicketEvent>` + drain BackgroundService (Recommended) | Bounded channel, batched drain, Polly retry. | ✓ |
| Durable outbox table (transactional) | Sync write to outbox in same tx; drain reads + deletes. | |
| Fire-and-forget `Task.Run` | Simplest; hostile to connection pool under load. | |
| Redis Stream + drain to Postgres | `XADD mm:events` → drain via `XREAD GROUP`. | |

**User's choice:** In-memory `Channel<TicketEvent>` + drain BackgroundService

### Q2: Postgres outage behavior?

| Option | Description | Selected |
|--------|-------------|----------|
| Log + drop, never block matching (Recommended) | Polly exhausted → drop batch, emit OTel counter. | ✓ |
| Buffer in memory until Postgres returns | Channel fills → drop or backpressure. | |
| Spill to local file | `pending.jsonl`, drain on recovery. | |
| Page the operator (health endpoint flip) | 503 health flip via admin panel. | |

**User's choice:** Log + drop, never block matching
**Notes:** Operator wires alerts off the `matchmaking.analytics.dropped_events` OTel counter.

### Q3: `matchmaking_tickets` retention policy?

| Option | Description | Selected |
|--------|-------------|----------|
| 30 days, daily cleanup BackgroundService (Recommended) | Mirrors Phase-4 IdempotencyCleanupService. | ✓ |
| Forever — operator manages retention | No cleanup; unbounded growth. | |
| 7 days | Aggressive, may be too short for debugging. | |

**User's choice:** 30 days, daily cleanup BackgroundService

### Q4: Which ticket events to record?

| Option | Description | Selected |
|--------|-------------|----------|
| Lifecycle terminals only (Recommended) | `Matched / Cancelled / Expired`. | |
| Every state transition + bracket-flex snapshot | 5–10× write volume. | |
| Lifecycle terminals + accept-flow events | Adds `Proposed / Accepted / Declined / TimedOut`. | ✓ |

**User's choice:** Lifecycle terminals + accept-flow events
**Notes:** Bracket-flex snapshots explicitly excluded (volume concern at 1k concurrent tickets per SC#3).

---

## Deferred Ideas captured during discussion

- Direct invite by PlayerId (future phase with friends list)
- SignalR / WebSocket push for match-found events
- Party chat / voice (out of scope — customer app concern)
- Cross-server matchmaking / region affinity
- Friends list / social graph
- MMR-balanced team split (v1 ships random)
- Priority lane for long-waiters past max bracket
- Disk spill / operator paging on Postgres outage
- Per-ladder accept-timeout override
- Per-ladder cancel-vs-shrink disconnect policy

## Claude's Discretion items

The user explicitly left these to downstream agents:

- Pool partitioning shape (one Redis sorted set per `{ladderId, poolName}` vs composite key)
- Proposal storage shape (Redis hash with TTL vs Postgres `match_proposals` table)
- Team split algorithm (v1 ships random)
- Party code alphabet (recommended: Crockford base32)
- Reconciler scope details
- Sample app demonstration scope (recommended: 1v1 only, document party flow in README)
