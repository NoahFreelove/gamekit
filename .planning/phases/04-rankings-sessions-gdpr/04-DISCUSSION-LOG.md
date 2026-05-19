---
phase: 04
phase_name: Rankings + Sessions Wiring + GDPR Export
discussed: 2026-05-15
mode: default (single-question turns, AskUserQuestion)
---

# Phase 4 — Discussion Log

Human-reference audit trail of the discussion that produced
`04-CONTEXT.md`. Downstream agents (researcher, planner, executor) read
`04-CONTEXT.md`, not this file.

## Area selection

**Q:** Which areas of Phase 4 do you want to lock down before
research/planning starts? (multiSelect)

**Options presented:**
- Rating period / window
- Session-complete API contract
- Seasonal reset trigger + strategy
- GDPR export bundle shape

**User selected:** all four.

---

## Area 1 — Rating period / window

### Q1.1 — Rating-period trigger
**Options:**
1. Time window via BackgroundService *(recommended)*
2. Per-ladder match-count threshold
3. On-demand at session-complete
4. Hybrid time-OR-count

**User selected:** Time window via BackgroundService → D-01

### Q1.2 — Default period length + leader election
**Options:**
1. 1h default + Redis distributed lock *(recommended)*
2. 1d default + Redis lock
3. Postgres advisory lock instead
4. Single-instance only (no leader election)

**User selected:** 1h default + Redis distributed lock → D-02, D-03

---

## Area 2 — Session-complete API contract

### Q2.1 — Who calls POST /api/sessions/{id}/complete?
**Options:**
1. Trusted game server only (service token) *(recommended)*
2. Player JWT only (any participant)
3. Either, with audit trail
4. Player JWT + signed result envelope

**User selected:** Trusted game server only (service token) → D-05

### Q2.2 — Service token issuance + idempotency window
**Options:**
1. Pre-shared key + 24h Idempotency-Key TTL *(recommended)*
2. Reuse player-JWT signing key for service tokens
3. Pre-shared key + 1h TTL
4. No Idempotency-Key, rely on state-conditional UPDATE

**User selected:** Pre-shared key + 24h Idempotency-Key TTL →
D-06, D-07, D-08

---

## Area 3 — Seasonal reset trigger + strategy

### Q3.1 — How does a season end?
**Options:**
1. Admin-triggered via /admin (superadmin) *(recommended)*
2. Auto-end via BackgroundService at configured timestamp
3. Both — ticker as default + admin override
4. Neither in v1 — manual SQL recipe in ops guide

**User selected:** Admin-triggered via /admin (superadmin) → D-11

### Q3.2 — Reset strategy
**Options:**
1. Soft reset — regression toward the mean *(recommended)*
2. Hard reset — everyone back to defaults
3. Keep current ratings — archival only
4. Per-ladder choice

**User selected:** Per-ladder choice → D-12 (all three strategies live
as enum variants; ladder config picks one). Soft regress kept as the
sensible default.

---

## Area 4 — GDPR export bundle shape

### Q4.1 — Bundle shape + auth
**Options:**
1. Single-blob JSON, player-self OR superadmin *(recommended)*
2. Single-blob, player-self only
3. NDJSON streaming
4. Zip-of-JSON files

**User selected:** Single-blob JSON, player-self OR superadmin →
D-15, D-16

### Q4.2 — Snapshot consistency
**Options:**
1. REPEATABLE READ transaction wrapping the whole read *(recommended)*
2. SERIALIZABLE — strictest
3. Eventual consistency
4. Lock the player row for the duration

**User selected:** REPEATABLE READ transaction → D-17

---

## Claude's discretion (not asked, applied per prior decisions or
sensible defaults)

- **D-04** ticker failure semantics — pending rows stay un-applied;
  log via `ActivitySource`; deterministic algorithm contract
  documented. Standard library-resilience pattern.
- **D-09** session-complete body shape — derived from existing
  `SessionParticipant` columns + Phase-3 FluentValidation patterns.
- **D-10** rate-limit policy — extends the existing CORE-12 named-
  policy registry; 300/min default is a generous game-server quota.
- **D-13/D-14** archive + season-tracking tables — minimum schema
  needed to make D-12 work; mirrors common ranked-ladder DB designs.
- **D-18** export size cap — 25 MB is generous given the JSON keys
  in D-15 and the CORE-17 sparse-metadata guidance; > cap returns
  413 with a problem-details body.
- **D-19/D-20** rank-adjust endpoint — uses the Phase-3 ban-reason
  policy + audit-write pattern verbatim; bypasses the ticker because
  operators are authoritative.
- **D-21** `AddLadder` registration — matches the Phase-1 fluent
  composable pattern (`AddGameKit().AddAuth().AddRankings()`).
- **D-22** session-complete handler location — clean Core / Rankings
  separation via `IPostSessionCompleteHandler` port. Avoids a
  Core-depends-on-Rankings inversion.
- **D-23** leaderboard service — admin-only HTTP exposure in v1;
  player-facing wrapper is a deferred idea so game devs control auth.

---

## Deferred ideas (captured during discussion or recognized as v2)

- Real-time rating push (SignalR / WebSockets)
- Cross-ladder tournaments
- Auto season-rollover ticker (D-11 chose admin-only for v1)
- Chunked / streaming GDPR export (D-18 ships a 25 MB cap)
- Runtime ladder CRUD via admin UI (D-21 ships build-time only)
- Player-facing HTTP leaderboard endpoint (D-23 ships admin-only)
- Multi-rating-system support beyond the swap point
- Admin signing-key rotation (Phase 6 ops territory)
