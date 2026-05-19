---
phase: 05
phase_name: Matchmaking + Parties
gathered: 2026-05-16
status: Ready for research / planning
---

# Phase 5: Matchmaking + Parties — Context

**Gathered:** 2026-05-16
**Status:** Ready for research / planning

<domain>
## Phase Boundary

Ships `GameKit.Matchmaking` as its own NuGet package (MATCH-01). Delivers a
Redis-backed live queue + party-aware matchmaker with a Dota/CS-style
**accept-step proposal flow** before sessions are created. Default
`EloRangeMatchmakingStrategy` uses Phase-4 Glicko-2 ratings with time-flexed
brackets (MATCH-09/10). Runs as a `BackgroundService` + `PeriodicTimer` +
Polly v8 retry (MATCH-07), elected via Redis distributed lock (MATCH-08).

Persists three new Postgres entities under `__ef_migrations_matchmaking` /
`gamekit` schema with its own advisory-lock key distinct from Core / Auth /
Admin / Rankings (MATCH-15):

1. **`parties`** — durable party row (MATCH-03 widened: parties are
   first-class, not runtime-only). Has owner `PlayerId`, party-code (6–8
   char random), state machine (`Open | Queueing | InMatch | Dissolved`),
   `CreatedAt`, optional auto-expiry.
2. **`party_members`** — FK to `parties`, FK to `players`. PlayerId is the
   only cross-provider key — Steam-linked + Discord-linked players can
   party (multi-identity model from Phase 2 honored).
3. **`matchmaking_tickets`** — analytics-only async-write table (MATCH-02).
   Redis remains source of truth for the live queue (MATCH-04).
4. **`decline_history`** — tracks escalating cooldown state for players who
   decline or time out match proposals.

Wires `POST /api/parties` (create), `POST /api/parties/join` (code-based),
`POST /api/mm/queue` (enqueue ticket), `GET /api/mm/queue/{ticketId}/status`
(long-poll match-status: `queued | proposed | matched | cancelled`),
`POST /api/mm/proposal/{proposalId}/accept` and `decline`. Per-player
enqueue rate limit via the existing `IGameKitRateLimitPolicies` interface
(MATCH-11).

Phase-3 admin UI panels show live queue depth + lease count + leader
identity sourced from Redis (MATCH-14), not Postgres reconciliation
mirrors. New admin verbs likely: `pause-queue`, `drain-queue`.

Chaos-recovery (MATCH-12): a reconciliation `BackgroundService` runs every
30s + on startup; claims abandoned tickets / pending proposals / pending
`game_sessions` from Postgres. **Never rehydrates Redis from Postgres** —
Redis is recreated empty after crash, reconciliation marks abandoned rows
`expired/cancelled` so they don't leak.

Load test (MATCH-13) is a phase gate: 1k concurrent tickets sustained 10
min against a single Redis + Postgres pair, no matchmaker iteration
exceeding its configured budget, no Npgsql pool exhaustion.

**Out of scope (deferred to later phases or v2):**
- Direct invite by PlayerId (sibling table `party_invites` + accept
  endpoints). Code-based join only in v1.
- SignalR / WebSockets push for match-found events — long-poll only in v1
  (mirrors the Phase-4 'no real-time push' constraint).
- Party chat / voice — out of scope; communication is the customer's app.
- Cross-server matchmaking / region affinity.
- Friends list / social graph.
- Skill-balanced team auto-split (MMR-balanced split). v1 uses simple
  random or party-order team assignment.
- Priority lane for long-waiters past the max bracket.

</domain>

<decisions>
## Implementation Decisions

### Party model & lifecycle

- **D-01:** Parties are **durable Postgres entities**, not runtime-only.
  `parties` table has its own ID + party-code + state machine. The
  `party_members` row references both `parties.id` and `players.id`.
- **D-02:** Players join via **short party code** (6–8 random chars,
  e.g. `K7Q3M2`). Owner creates the party; the package mints the code;
  joining player calls `POST /api/parties/join` with `{ code }`. Codes
  are case-insensitive, single-active-party, expire when the party
  dissolves. Familiar UX (Among Us / Jackbox / Fall Guys).
- **D-03 [deferred]:** **Direct-invite-by-PlayerId is deferred to a future phase.**
  Stub `party_invites` is NOT created in v1 — future phase adds the
  table + invite endpoints once a friends-list surface exists.
- **D-04:** **Mid-queue disconnect → cancel the entire ticket, keep the
  party row alive.** The party is durable; the ticket is transient. Once
  the disconnected member reconnects, the party can re-enqueue. Avoids
  surprise N−1 matches a dropped player might want to rejoin. No
  per-ladder override in v1.
- **D-05:** **Cross-provider parties are allowed.** Party membership
  references the canonical `Player` row, not `PlayerIdentity`. A
  Steam-linked + Discord-linked pair can party because both share a
  `Player` row under Phase 2's multi-identity model.

### Match-found flow

- **D-06:** **Accept-step proposal model** (Dota / CS-style), not
  immediate session creation. On match, matcher writes a
  `match_proposals` row, parks all tickets, and notifies clients.
  Session is only created after all participants accept within the
  timeout. This adds chaos surface (proposals need reconciliation) but
  matches the user-chosen UX for ranked-style play.
- **D-07:** **Accept timeout = 10 seconds (CS:GO-style).** Tight window
  forces engaged players, fast queue churn. Pairs with the escalating
  cooldown below. No per-ladder override in v1 — single global value
  in `GameKitMatchmakingOptions`.
- **D-08:** **Escalating decline cooldown.** First decline / timeout:
  3 min. Second within a configurable window: 15 min. Third: 30 min.
  Tracked in a `decline_history` Postgres table (persistence survives
  app restart). Window + step durations live in
  `GameKitMatchmakingOptions`. Cooldown lockout is enforced at the
  `POST /api/mm/queue` endpoint (returns `403 ProhibitedDuringCooldown`
  with `retryAfterSeconds`).
- **D-09:** **On proposal failure, the accepting parties auto-re-queue
  at the front of their pool with the original bracket flex
  preserved.** Implementation: keep the ticket's original `queuedAt`
  timestamp on re-insertion. Ranked players who did nothing wrong
  don't lose their accumulated bracket. The matcher recognises a
  re-queued ticket by inspecting `queuedAt` — no separate state field
  needed.
- **D-10:** **Long-poll status endpoint** for clients: `GET
  /api/mm/queue/{ticketId}/status` holds for up to ~30s and returns
  `{ status: 'queued' | 'proposed' | 'matched' | 'cancelled',
  proposalId?, deadline?, sessionId? }`. Clients poll until decision.
  No SignalR in v1 (matches Phase-4's 'no real-time push' constraint).
  Short-poll fallback NOT shipped — long-poll is the single supported
  surface.

### Bracket flex curve & strategy config

- **D-11:** **Default strategy = `EloRangeMatchmakingStrategy` with
  linear bracket ramp `100 → 500 over 40s`, capped at ±500
  afterward.** Computed as `bracket(t) = min(100 + (400 · t/40), 500)`
  where `t` is seconds in queue. Matches SC#1 literal text. Step /
  exponential / no-cap variants explicitly rejected for v1.
- **D-12:** **Per-ladder configurable curve** via the Phase-4
  `AddLadder(opts => …)` model. Options surface:
  `BracketStart` (default 100), `BracketEnd` (default 500),
  `BracketRampSeconds` (default 40). Pairs naturally with the existing
  `LadderConfig` JSONB column on the Phase-4 `ladders` table.
- **D-13:** **Party-rating aggregator is configurable per ladder** via
  `AddLadder(opts => opts.PartyRatingAggregator = Mean | Max |
  GlickoWeighted)`. Default = `Mean` (simple arithmetic mean of all
  party members' current rating from `player_ranks`). The `Max`
  variant pairs against the highest-rated member; `GlickoWeighted`
  uses RD-aware weighting. Surface this as an enum + switch arm
  inside the default strategy.
- **D-14:** **Optional max within-party rating spread cap**, per
  ladder, default disabled. `AddLadder(opts =>
  opts.MaxPartyRatingSpread = 500)` rejects parties whose
  `Max(rating) − Min(rating)` exceeds the cap. Default = `null` (no
  cap). On rejection, the enqueue returns
  `400 PartyRatingSpreadExceeded`. v1 prefers a no-default-cap stance
  — operators opt in.

### Postgres async-write durability

- **D-15:** **In-memory `Channel<TicketEvent>` + drain
  BackgroundService.** Producer writes terminal/transition events into
  a bounded `System.Threading.Channels.Channel<TicketEvent>`. A
  dedicated `MatchmakingAnalyticsDrainService` reads batches of N
  items (or every M seconds, whichever fires first) and writes them to
  Postgres. Polly v8 retry on transient Postgres failure. Channel is
  bounded — on full, drop newest event and increment a counter. Stays
  in-process; analytics is best-effort; matchmaking never blocks on a
  Postgres write.
- **D-16:** **On sustained Postgres outage: log + drop, never block
  matching.** After Polly retries exhaust, the drain service drops the
  batch and increments an OpenTelemetry counter
  (`matchmaking.analytics.dropped_events`) so the operator's existing
  OTel exporter (Phase-4 opt-in) picks it up. Matchmaking continues
  serving live tickets from Redis. No disk spill, no operator paging
  built into the package — operators wire alerts off the OTel metric.
- **D-17:** **30-day retention on `matchmaking_tickets`** via a
  `MatchmakingRetentionCleanupService` (nightly periodic timer,
  startup-immediate pass — mirrors Phase 4's
  `IdempotencyCleanupService` pattern). Retention window configurable
  via `GameKitMatchmakingOptions.TicketRetention` (default 30 days).
- **D-18:** **Event taxonomy: lifecycle terminals + accept-flow
  events.** Recorded events:
  - `Queued` — ticket enqueued
  - `Proposed` — included in a match proposal (records `proposalId`)
  - `Accepted` — player accepted within window
  - `Declined` — player explicitly declined
  - `TimedOut` — accept window expired without response
  - `Matched` — proposal succeeded, `game_session` created
  - `Cancelled` — ticket cancelled (manual cancel or party-DC per D-04)
  - `Expired` — reconciler marked the ticket abandoned (MATCH-06)

  Recording every bracket-flex step was explicitly rejected — too much
  write volume for the SC#3 1k-concurrent budget. The flex curve can
  still be plotted from `Queued.queuedAt → terminal.terminatedAt`.

### Claude's Discretion

The following details were not user-chosen; planner / researcher should
pick the implementation that best fits the rest of the GSD codebase:

- **Pool partitioning shape** — one Redis sorted set per `{ladderId,
  poolName}` or a single per-ladder sorted set with a composite key.
- **Proposal storage** — Redis hash with TTL = AcceptTimeoutSeconds + a
  reaping service, vs a Postgres `match_proposals` table with a
  reconciler. Default expectation: Redis hash (fits SC#2 chaos test
  shape; reconciler only sweeps Postgres for orphaned sessions, not
  proposals).
- **Team split algorithm after match formation** — v1 ships random
  team assignment. MMR-balanced split is deferred (in `<deferred>`).
- **Party code alphabet** — Crockford base32 (no I / L / O / 0 / 1)
  recommended to avoid OCR/typing collisions; 6 chars = 32^6 ≈ 1B
  collision space.
- **Reconciler scope** — proposed contract: every 30s + startup, scan
  Postgres for `matchmaking_tickets` in non-terminal states older than
  N min and not present in Redis → mark `Expired`. Scan
  `game_sessions` in `Active` state with no participants showing
  heartbeat → mark `Cancelled` and audit.
- **Sample app (`TicTacToeDuel`) demonstration scope** — recommended:
  1v1 enqueue path only (TicTacToe doesn't need parties of 2+).
  Document the party-create / party-join flow in README; don't
  duplicate-UI a party demo.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project-level invariants
- `CLAUDE.md` — `.NET 10 LTS`, GPL, self-hosted only, Polly v8 (not
  HTTP-resilience for Redis), no MediatR, no Hangfire, MS.DI + Scrutor,
  BackgroundService + Polly pattern for background work, OpenTelemetry
  opt-in.
- `.planning/REQUIREMENTS.md` lines 83–97 — MATCH-01..15 verbatim.
- `.planning/ROADMAP.md` § Phase 5 — Success Criteria 1–6 (matchmaker
  semantics, chaos test, load test as phase gate, leader election,
  enqueue rate limit, admin panel live state).

### Prior-phase decisions to honor
- `.planning/phases/01-foundation-core-migrations-ops-defaults-gpl/01-RESEARCH.md`
  — per-package migration pattern (`__ef_migrations_<pkg>`),
  `IModelBuilderExtension`, `MigrationsHistoryTable` wiring, unique
  advisory-lock key.
- `.planning/phases/02-authentication/02-CONTEXT.md` — multi-identity
  model (one `Player`, many `PlayerIdentity`), JWT scheme used at the
  matchmaking endpoints, `IPasswordHasher` pattern.
- `.planning/phases/03-admin-ui/03-CONTEXT.md` — `AdminCommandRegistry`,
  `AdminAuditActions`, `AuditSentenceTemplates`, `IAdminAuditWriter`.
  New admin verbs (`pause-queue`, `drain-queue`) wire through these.
- `.planning/phases/04-rankings-sessions-gdpr/04-CONTEXT.md` — `D-22`
  port-and-adapter for cross-package endpoint wiring;
  `IGameKitRateLimitPolicies` interface; `IClock` injection; Polly v8
  Redis-retry pattern.
- `.planning/phases/04-rankings-sessions-gdpr/04-RESEARCH.md` —
  Pitfalls §3 (CLI model customizer), §6 (lease check mid-tick), §12
  (GDPR-null skip).
- `.planning/phases/04-rankings-sessions-gdpr/04-SUMMARY` files —
  `RankingsTickerService` is the BackgroundService pattern to mirror,
  `RankingsTickerLeaseHelper` is the Redis lock pattern.

### Code references (read before designing)
- `src/GameKit.Rankings/Services/RankingsTickerService.cs` —
  BackgroundService + PeriodicTimer + Redis leader-election precedent.
- `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` —
  fencing-token-safe Redis lock pattern (SET NX PX + LockTake / Extend
  / Release).
- `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs` —
  nightly cleanup BackgroundService pattern (mirror this for
  `MatchmakingRetentionCleanupService`).
- `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs` — pattern
  for per-package advisory-lock-key constant + verification test.
- `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs`
  — pattern for extending `IGameKitRateLimitPolicies` with a new
  per-package rate limit (mirror for `MatchmakingEnqueue` policy).
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.*.cs` —
  fluent builder pattern with `AddLadder(opts => …)` to extend.
- `src/GameKit.Core/Entities/GameSession.cs`,
  `src/GameKit.Core/Entities/SessionParticipant.cs` — `Team` int field
  exists on participants; matchmaker writes team assignments at
  session creation time.

### Specs to NOT re-read
- No SPEC.md exists for Phase 5. Requirements are in
  `.planning/REQUIREMENTS.md` MATCH-01..15 and the Phase-5 ROADMAP
  block.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`GameKit.Matchmaking.csproj` skeleton already exists** at
  `src/GameKit.Matchmaking/` (`AssemblyInfo.cs` only). PackageId,
  RootNamespace, ProjectReference to `GameKit.Core` already declared.
  Just add source files.
- **`RankingsTickerService.cs`** — copy the BackgroundService +
  PeriodicTimer + Polly v8 retry + lease-helper composition pattern.
- **`RankingsTickerLeaseHelper.cs`** — copy the fencing-token Redis
  lock pattern; rename `gamekit:rankings:ticker:lock` →
  `gamekit:matchmaking:matcher:lock`. Verify lock-key collision-safe
  in a test.
- **`IdempotencyCleanupService.cs`** — copy as
  `MatchmakingRetentionCleanupService.cs`.
- **`IGameKitRateLimitPolicies`** — extend with
  `MatchmakingEnqueue` partition keyed by `ClaimTypes.NameIdentifier`.
- **Phase-4 audit + admin-palette wiring** — add new verbs through
  `AdminCommandRegistry` + `AdminAuditActions` + `AuditSentenceTemplates`.
- **`IClock`** — inject for deterministic time in tests
  (`StepClock` precedent already in
  `tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs`).

### Established Patterns
- **Pascal-case Npgsql columns** — `"TicketId"`, `"PartyId"`,
  `"BracketStart"`. Raw SQL must quote.
- **Enum mapping** — Phase 4 used `HasConversion<string>()` and got
  bitten by integer-cast SQL seeds. **Default to integer enum storage
  for Phase 5** unless string mapping is explicitly justified.
- **Per-package migration** — `__ef_migrations_matchmaking` history
  table; unique advisory-lock key constant + integration test that
  verifies `SELECT hashtext('gamekit.matchmaking.migrations')::bigint`
  matches the C# constant and is distinct from Core / Auth / Admin /
  Rankings.
- **Test pattern** — `RankingsFixture` composite (PostgresFixture +
  RedisFixture) is the precedent — `MatchmakingFixture` should
  compose the same.
- **Test-only model customizer** — `RankingsCliModelCustomizer`
  (Pitfall §3) is the precedent for bypassing the global EF model
  cache when tests need cross-package entities in a single context.

### Integration Points
- **Phase 4 Glicko-2 ratings** — `EloRangeMatchmakingStrategy` reads
  `player_ranks.rating / rd / volatility` directly. No cross-package
  service call needed; both packages share the Postgres schema.
- **Phase 4 sessions** — matchmaker creates `game_sessions` rows on
  successful proposal accept; `SessionParticipant.Team` carries the
  team assignment.
- **Phase 3 admin UI** — `MainLayout.razor` switch arm + new dialogs
  (`PauseQueueDialog`, `DrainQueueDialog`?). Live queue state panel
  reads from Redis via a new `IMatchmakingObservability` port.
- **Phase 2 auth** — JWT bearer protects `/api/parties/*` and
  `/api/mm/*`; `ClaimTypes.NameIdentifier = PlayerId` claim used for
  rate-limit partitioning.
- **Sample app (`TicTacToeDuel`)** — wire `AddMatchmaking()` +
  `AddLadder(...)` in `Program.cs`; document 1v1 enqueue flow in
  README. Don't add a UI for parties to the sample.

</code_context>

<specifics>
## Specific Ideas

- **CS:GO-style 10-second accept window** — user explicitly named this
  reference point when picking the accept timeout.
- **Among Us / Jackbox / Fall Guys-style 6-8 char party code** —
  familiar UX from these games is the reference for the code-based
  join flow.
- **Dota 2-style escalating decline cooldown** — 3 → 15 → 30 min step
  ladder is the canonical example.

</specifics>

<deferred>
## Deferred Ideas

Captured during discussion but belong in future phases or v2:

- **Direct invite by PlayerId** — `party_invites` table + invite
  endpoints. Defer until a friends-list surface exists. Stub NOT
  shipped in v1 (no feature flag, no table).
- **SignalR / WebSocket push for match-found events** — mirrors the
  Phase-4 'real-time rating push' deferral. Long-poll-only in v1.
- **Party chat / voice** — out of scope for v1; communication is
  the customer's app responsibility.
- **Cross-server matchmaking / region affinity** — v1 assumes a
  single Redis + Postgres pair (matches SC#3 load-test framing).
- **Friends list / social graph** — out of scope; party-code join is
  the v1 social surface.
- **MMR-balanced team split** — v1 ships random team assignment from
  the matched parties. MMR-balanced split is a tuning improvement
  that can ship without breaking the API.
- **Priority lane for long-waiters past max bracket** — operators can
  build this in a custom strategy; the default caps at ±500 and never
  prioritises stuck tickets.
- **Disk-spill or operator-paging health flip on Postgres outage** —
  v1 logs + drops + emits an OTel counter. Operator wires alerts off
  the metric. Heavier durability paths deferred until usage shows
  dropped-event volume matters.
- **Per-ladder accept-timeout override** — single global 10s in v1.
  Per-ladder override deferred unless concrete operator request.
- **Per-ladder cancel-vs-shrink disconnect policy** — v1 hard-codes
  "cancel ticket, party survives" (D-04). Per-ladder override
  deferred.

</deferred>

---

*Phase: 05-Matchmaking + Parties*
*Context gathered: 2026-05-16*
