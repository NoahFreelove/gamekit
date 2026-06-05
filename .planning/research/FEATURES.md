# Feature Research

**Domain:** Self-hosted game-services backend library (v2.0 expansion)
**Researched:** 2026-06-05
**Confidence:** HIGH (for standard patterns); MEDIUM (for GameKit-specific composition model)

---

## Scope of This Document

This research covers the **10 v2.0 feature areas** only. All v1.0 features (JWT auth, BCrypt,
Steam/Discord OAuth, Glicko-2 rankings, EloRange matchmaking, presence, Blazor Admin UI) are
treated as given. Dependencies on v1 features are noted but not re-researched.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features game developers assume any serious game-services library provides. Missing them makes
GameKit feel incomplete compared to PlayFab, AccelByte, or Nakama.

| Feature | Why Expected | Complexity | Dependencies |
|---------|--------------|------------|--------------|
| Rating-aware matchmaking | Any "skill-based matchmaking" that runs on rating=0 is a known placeholder; devs expect the seam to be wired | MEDIUM | `GameKit.Rankings` → `GameKit.Matchmaking` seam; v1 `IMatchmakingStrategy` already party-aware |
| Placement matches (initial calibration) | Every competitive game with a ranking system gates visible rank behind N calibration games; Glicko-2 high-RD start is the natural mechanism | LOW-MEDIUM | `GameKit.Rankings` ladders; new `placement_complete` flag on `player_ranks` |
| Rank decay (top-tier inactivity) | Overwatch 2, LoL, CS2 all apply decay only at Master+ to keep leaderboards live; lower tiers see RD inflation only | MEDIUM | `GameKit.Rankings` `player_ranks`; needs a `BackgroundService` decay job |
| Regional matchmaking pools | Latency-sensitive games demand region routing; metadata escape hatch is not good enough for production use | MEDIUM | New `pool_id` or `region` column on matchmaking tickets; Redis pool key namespacing |
| Backfill | Games that allow mid-session replacement expect the matchmaker to re-enqueue for open slots | MEDIUM | `game_sessions` open-slot tracking; `IMatchmakingStrategy` backfill mode; existing party-ticket model |
| Argon2 sibling hasher | OWASP recommends Argon2id as the preferred algorithm; a library that offers only BCrypt feels dated in 2026 | LOW | `IPasswordHasher` (v1); Isopoh.Cryptography.Argon2 (already selected); rehash-on-login migration path |
| Google OAuth | Google is the most common identity provider across mobile and web; nearly every game with web login expects it | LOW | `IOAuthProvider` (v1); aspnet-contrib pattern already set by Discord/Steam |
| Multi-replica Admin UI | Any operator running >1 replica expects the health and queue-depth panels to reflect global state, not a single node | MEDIUM | SignalR + Redis backplane (StackExchange.Redis already present); sticky sessions requirement |

### Differentiators (Competitive Advantage)

Features that go beyond the minimum and give GameKit a meaningful edge over off-the-shelf
solutions, consistent with the "every algorithm is an interface" ethos.

| Feature | Value Proposition | Complexity | Dependencies |
|---------|-------------------|------------|--------------|
| Account merge with explicit conflict policy | Most OSS backends refuse account merge entirely or do a naive overwrite; a principled merge with documented conflict semantics (sum stats, keep higher rating, prefer newer identity data) is a strong differentiator | HIGH | `GameKit.Core` GDPR delete scaffolding; `player_identities`, `player_credentials`, `player_ranks`, `matchmaking_tickets`, `session_participants` all need FK re-pointing; SERIALIZABLE transaction |
| Rating-aware bracket expansion over wait time using RD | Using Glicko-2 RD (confidence interval) as the natural bracket width — not just ±N points — is theoretically correct and rare in OSS implementations; bracket widens as `±(base_range + RD + elapsed_factor)` | MEDIUM | Requires Rankings seam; adds `elapsed_seconds` to ticker state; existing EloRange bracket-flex hook already exists (v1 has time factor) |
| Rank decay via RD inflation (not rating loss) | Decaying by inflating RD (making the system less certain) rather than subtracting rating points is Glicko-2-correct and fairer than point loss; visible rank can drop as a consequence, but the mechanism is confidence-based | MEDIUM | `GameKit.Rankings` decay job; configurable per-ladder |
| Apple Sign-In + Epic OAuth providers | Apple is mandatory for any iOS game; Epic is common for cross-store distribution | MEDIUM (Apple harder due to SIWA quirks) | `IOAuthProvider`; Apple requires OIDC not raw OAuth2; `form_post` response mode; private relay email handling |

### Anti-Features (Deliberately NOT Building)

Features that seem natural but should be explicitly excluded to control scope, prevent license
violations, or avoid the wrong abstraction.

| Feature | Why Requested | Why Problematic | What to Do Instead |
|---------|---------------|-----------------|-------------------|
| Persistent general-purpose in-lobby chat history | Lobby chat seems obvious; devs ask for it | Chat history storage is unbounded write load, raises GDPR obligations (message content is PII), requires content moderation hooks — none of which belong in a GPL game-services library. "Real-time chat" is Out of Scope per PROJECT.md | Lobby `GameKit.Lobby` provides ephemeral SignalR message relay only (no storage); operator stores chat in their own tables if needed |
| Global friend list / social graph in Lobby | Persistent groups look like a friend system | `GameKit.Social` is explicitly deferred to a later milestone. Lobby persistent groups are pre-match coordination rooms, not social graphs | `GameKit.Lobby` group = a set of `player_id`s with a lobby ID; friendship semantics require `GameKit.Social` |
| AI-assisted matchmaking / smart backfill | "Smart" backfill that optimizes team balance sounds useful | AI integrations are explicitly Out of Scope (GPL self-hosted commitment); any ML model introduces cloud/SaaS dependencies or opaque black-box behavior | `IMatchmakingStrategy` interface is the extension point; operators plug in their own strategy |
| Cross-region data federation for matchmaking | Regional pools look like they imply global cross-region matching | Cross-database / cross-instance matchmaking is a distributed systems problem outside the library's scope; the library runs on one database. Multi-region deployment is an operator topology concern | Regional pools are same-instance queue isolation by a `region` string; cross-region requires the operator to run federation at the infrastructure layer |
| Webhook / event-push on account merge | Observers on merge completion seem useful | Operators can react post-merge by querying the returned merged player ID; a webhook bus introduces another dependency and phone-home risk | `IAccountMergeCompletedHandler` optional interface scanned by Scrutor — same pattern as `IPostSessionCompleteHandler` in v1 |
| Forced rank reset on merge | Merging two high-ranked accounts and averaging is tempting | Rating math on merge is a game-design decision, not a library decision; the library should preserve both rating histories and let the operator decide | Merge carries forward the **higher** Glicko-2 rating by default (configurable); both rating histories are preserved in `session_participants` audit trail |
| Placement matches that hide ALL rank information | Some games hide rank entirely during placement | Hiding vs. showing provisional rank is a UI/game-design decision; the library should not gate leaderboard queries | Placement flag (`is_in_placement`) exposed on `player_ranks`; the game client decides what to show |
| Auto-balance team composition during backfill | Rebalancing existing players when a slot is backfilled | Moves already-placed players; violates game fairness expectations; the session is live | Backfill finds the best-rated available player for the open slot; no re-balancing of existing participants |
| SignalR for game-state communication | Lobby has SignalR; devs want to reuse it for game events | Real-time game communication (netcode, game state sync) is explicitly Out of Scope. Blurring lobby comms with game comms creates coupling | Lobby SignalR hub is scoped to pre-match coordination only; `IDisconnectHandler` lets the game server take over after match starts |

---

## Feature-by-Feature Detailed Notes

### 1. Argon2 Sibling Hasher (`GameKit.Auth.Argon2`)

**Standard behavior:**
- `IPasswordHasher` is the v1 extension point; Argon2 is a drop-in replacement.
- On login: verify with whichever hasher produced the stored hash. BCrypt hashes begin `$2a$`/`$2b$`; Isopoh Argon2id hashes begin `$argon2id$`. Detect the prefix, verify with the correct hasher, then re-hash with Argon2id and update `player_credentials.password_hash` in the same request transaction.
- No bulk migration needed and none possible (plaintext not stored).
- After 6-12 months of active logins, most active players will be on Argon2id naturally.
- Inactive accounts keep their BCrypt hash indefinitely; this is acceptable — they cannot be attacked without the hash, and the hash is properly salted.

**Complexity:** LOW. The `IPasswordHasher` interface exists. Isopoh is already selected. The rehash-on-login pattern is a 10-line change in `AuthService.LoginAsync`. The new package is one project file plus tests.

**Dependencies:** `GameKit.Auth` (references `IPasswordHasher`).

---

### 2. Google OAuth Provider (`GameKit.Auth.Google`)

**Standard behavior:**
- Scopes to request: `openid email profile` (minimal). Returns: `sub` (stable unique identifier), `email`, `name`, `picture` URL.
- The `sub` claim is the canonical identity key — do NOT use email as the external ID, as emails can change.
- `IOAuthProvider` pattern: register an `AuthenticationScheme` via `AddGoogle(...)`, wrap in GameKit's provider abstraction. Same pattern as Discord.
- Identity data maps to `player_identities`: `provider="google"`, `external_id=sub`, `display_name=name`, `avatar_url=picture`.

**Complexity:** LOW. Google's OIDC endpoint is well-documented; `Microsoft.AspNetCore.Authentication.Google` is in the shared framework (no new NuGet dep needed).

**Dependencies:** `GameKit.Auth`; aspnet-contrib pattern already established.

---

### 3. Apple Sign-In Provider (`GameKit.Auth.Apple`)

**Standard behavior:**
- Apple uses OIDC, not raw OAuth2. The `sub` claim in the Apple ID token is the stable identifier.
- Scopes: `name email` only (those are the only two Apple offers).
- **Critical quirk:** User data (name, email) is only returned on the **first sign-in**. Subsequent logins return only `sub`. The system must persist name/email at first login because Apple will never resend it.
- **Private relay email:** Apple may return a `@privaterelay.appleid.com` address. Store it as-is; do not treat it as invalid. Never send marketing email to it (Apple blocks non-transactional use).
- **Response mode:** Apple requires `response_mode=form_post` when requesting name/email scopes. Backend must accept a POST callback, not a GET redirect.
- Identity data maps to `player_identities`: `provider="apple"`, `external_id=sub`, `display_name=name` (persisted at first login only), `avatar_url=null` (Apple provides no picture URL).

**Complexity:** MEDIUM-HIGH. The OIDC flow is mostly standard but the first-login-only identity data, `form_post` requirement, and private relay email handling add non-trivial edge cases. `AspNet.Security.OAuth.Apple` (aspnet-contrib) exists and handles most of the complexity.

**Dependencies:** `GameKit.Auth`; `AspNet.Security.OAuth.Apple` (aspnet-contrib, same family as Discord provider).

---

### 4. Epic Games OAuth Provider (`GameKit.Auth.Epic`)

**Standard behavior:**
- Epic uses standard OAuth2 with EAS (Epic Account Services).
- Scopes to request: `basic_profile` (minimum for identity). Optionally `friends_list`, `presence` (do not request unless needed — scope creep).
- Returns: Epic Account ID (stable), display name, linked account info (Steam, PSN, Xbox, etc.).
- Identity data maps to `player_identities`: `provider="epic"`, `external_id=epic_account_id`, `display_name=displayName`.
- Note: Epic's EOS also has a separate "Connect Interface" for platform-native tokens — this is distinct from the OAuth flow. GameKit's provider covers the OAuth path only.

**Complexity:** LOW-MEDIUM. Standard OAuth2; `AspNet.Security.OAuth.EpicGames` (aspnet-contrib) exists.

**Dependencies:** `GameKit.Auth`.

---

### 5. Account Merge

**Standard behavior (from shipped games):**
- Overwatch 2 (the canonical reference): sum aggregate stats (total wins, playtime), take the max for peak values (best hero stats), carry over the highest rank.
- Klei (Don't Starve): all secondary account unlocks transfer to primary; some record types replace (cookbook, registry = last-write-wins).
- General pattern across real systems: one account is designated "primary" (keeps the `player_id`); the other is "secondary" (its `player_id` is tombstoned). All FK references that point to the secondary are re-pointed to the primary.

**Conflict rules for GameKit's default policy (all configurable via `IAccountMergePolicy`):**
- `player_identities`: All identities from both accounts merge onto the primary. Conflict (same provider, different external_id) is impossible by definition (two accounts can't have the same external_id for the same provider). If both had the same provider but different external IDs, both are retained.
- `player_credentials` (password): Primary account's credential wins. If primary has no credential and secondary does, carry secondary's credential over.
- `player_ranks`: Take the **higher** Glicko-2 `rating` for each ladder. RD defaults to `max(primary_RD, secondary_RD)` to reflect merged uncertainty. wins/losses/draws are **summed**. Both histories remain intact in `session_participants` (they reference the sessions, not the account; re-point session_participants FK to primary player_id).
- `refresh_tokens`: Revoke all secondary tokens immediately (security). Primary tokens remain.
- `matchmaking_tickets`: Secondary's queued/active tickets are cancelled. Primary's tickets are unaffected.
- `game_sessions` / `session_participants`: FK re-point secondary → primary. Historical record preserved.
- `admin_audit_log`: Entries for secondary player are NOT migrated (audit trail stays with original actor ID). Add a merge audit entry referencing both IDs.
- `metadata JSONB`: Operator-defined; `IAccountMergePolicy` exposes a merge hook. Default: primary metadata wins.
- `deleted_at` / `is_banned`: If either account is banned, the merged account is banned. GDPR-deleted accounts cannot be merged (guard on entry).

**Transaction requirement:** The entire merge must execute in a single `SERIALIZABLE` transaction. This is non-negotiable — partial merges corrupt the data model irreversibly.

**Post-merge:** Secondary `player_id` row is soft-deleted (tombstoned with `deleted_at` + a `merged_into_player_id` column for traceability). All JWT tokens for the secondary player are revoked. The merge event is written to `admin_audit_log`.

**Risk assessment:** HIGH risk. This is the hardest feature in v2.0. The FK surface across 8+ tables, the SERIALIZABLE transaction scope, and the irreversibility make it the most likely to cause data corruption if rushed. Requires the most thorough test coverage of any v2 feature.

**Complexity:** HIGH.

**Dependencies:** `GameKit.Core` (`players`, `admin_audit_log`, `game_sessions`, `session_participants`); `GameKit.Auth` (`player_identities`, `player_credentials`, `refresh_tokens`); `GameKit.Rankings` (`player_ranks`); `GameKit.Matchmaking` (`matchmaking_tickets`, `party_members`).

---

### 6. Rating-Aware Matchmaking

**Standard behavior:**
- The v1 wart: `MatchmakingService.EnqueueAsync` hardcodes member ratings to `0`. The fix: at enqueue time, look up `player_ranks` for the specified `ladder_id` (or a configured default ladder) and store the real Glicko-2 rating in the Redis ticket hash.
- **Bracket width using RD:** The correct Glicko-2 matchmaking window is `[rating - k*RD, rating + k*RD]` where `k` defaults to 2 (capturing ~95% confidence interval). This is better than a flat ±N because it respects uncertainty — a new player with RD=300 gets a wide bracket; a veteran with RD=50 gets a tight one.
- **Bracket expansion over wait time:** The existing v1 `EloRangeMatchmakingStrategy` already has a time-based flex (`±100 → ±500 over 40s`). For v2, this expands to: `base_range = k * RD`, then `current_range = base_range + (elapsed_seconds / expand_seconds) * max_expansion`. Both `k`, `expand_seconds`, and `max_expansion` are configurable per-strategy instance.
- **Dependency injection:** `GameKit.Matchmaking` must NOT take a hard project reference to `GameKit.Rankings` (circular dependency risk in some install configurations, and violates "install only what you need"). Instead, expose an `IRatingSource` interface in `GameKit.Core` (or `GameKit.Matchmaking`) that the caller wires up when both packages are installed: `services.AddMatchmaking().WithRatingsFrom<RankingsRatingSource>()`.

**Complexity:** MEDIUM. The algorithm change is small. The dependency-injection seam design (avoiding a hard package reference) is the interesting part.

**Dependencies:** New `IRatingSource` abstraction in `GameKit.Core` or `GameKit.Matchmaking`; `GameKit.Rankings` provides the concrete `RankingsRatingSource` implementation.

---

### 7. Rank Decay

**Standard behavior (from shipped games):**
- Decay applies only to top-tier players (e.g., top N% by rating, or rating above a configured threshold). Applying it to everyone punishes casual players and is widely considered unfair.
- Two decay mechanisms exist in practice:
  1. **RD inflation only (Glicko-2 native):** The RD grows with inactivity per Glicko-2's standard formula. This does NOT directly lower rating — but visible rank (e.g. "Diamond") is derived from `rating - 2*RD`, so the visible rank can drop. This is the correct Glicko-2 approach.
  2. **Explicit rating subtraction:** LoL Apex Tiers (-75 LP/day after a threshold). Simple but statistically incorrect — it imposes artificial regression toward the mean rather than confidence loss.
- GameKit should implement **RD inflation only** (mechanism 1). This is algorithm-correct, avoids penalizing players unfairly for life events, and is already partially supported by Glicko-2's inactivity formula — the decay job just needs to run the Glicko-2 "no games played" period update on the RD column.
- The decay `BackgroundService` runs on a configurable schedule (daily default) and applies only to players whose `last_match_at` exceeds the configured `inactivity_threshold_days` AND whose `rating` exceeds the `decay_threshold_rating`. Both are per-ladder config.

**Complexity:** MEDIUM. The algorithm (Glicko-2 idle period update) is small. The `BackgroundService`, Redis leader election, and configurable per-ladder thresholds add surface area. Leader election pattern is identical to v1 matchmaking ticker — reuse it.

**Dependencies:** `GameKit.Rankings` (`player_ranks`, `ladders`); Redis leader lock (same pattern as `GameKit.Matchmaking`).

---

### 8. Placement Matches

**Standard behavior:**
- New players start with Glicko-2's default high RD (~350 on a 0-3000 scale), meaning the system is uncertain about their skill.
- A `placement_matches_required` count is configured per-ladder (common: 5-10 games). Until that count is reached, the player is in "placement" mode.
- During placement: rating changes are large (high RD = high sensitivity), which is correct behavior — this is not a special-case, it's Glicko-2 working as designed.
- Visible rank is suppressed or shows "Placement" until `placement_matches_required` is met. This is a UI convention, not a math change.
- Implementation: add `placement_matches_remaining` (int) and `is_in_placement` (bool, derived) to `player_ranks`. Decrement on each session complete. When it hits 0, the player "completes placement."

**Complexity:** LOW-MEDIUM. The math is unchanged — Glicko-2's high RD already handles calibration correctly. The work is: schema addition, decrement-on-complete wiring in `SessionCompleteService`, and exposing `is_in_placement` in the leaderboard/rank API responses.

**Dependencies:** `GameKit.Rankings` (`player_ranks`); `session_participants` / `SessionCompleteService` in `GameKit.Core`.

---

### 9. Backfill

**Standard behavior (from AWS FlexMatch, Open Match):**
- Backfill is triggered by the **game server** (or the session owner) when a player slot becomes open in an active session.
- The backfill request is a normal matchmaking ticket with a special `backfill_session_id` parameter. The matchmaker finds a player who fits the session's existing composition (rating range, region pool, etc.) and routes them to the session.
- The backfill player is matched against the current session's participants' ratings (not against an empty lobby).
- Ticket priority: backfill tickets should be given higher queue priority or a dedicated sub-queue to minimize game disruption time.
- The game server is authoritative on accepting or rejecting the backfill candidate (it may be too late in the game to usefully backfill).

**Implementation for GameKit:**
- New ticket type: `backfill` (enum on `matchmaking_tickets.ticket_type`).
- `POST /api/matchmaking/backfill` endpoint: body includes `session_id`, `open_slots`, `team_id` (optional), `pool_id`/`region`.
- The matchmaker ticker picks up backfill tickets from a separate Redis sorted set (higher priority) and matches them against the specified session's constraint snapshot.
- On match: existing session's `session_participants` gets the new player added; no new `game_sessions` row created.

**Complexity:** MEDIUM. New ticket type, new endpoint, small ticker change. The session-mutation path (adding a participant to an active session) is new and needs idempotency guards.

**Dependencies:** `GameKit.Matchmaking` (tickets, ticker, `IMatchmakingStrategy`); `GameKit.Core` (`game_sessions`, `session_participants`).

---

### 10. Regional Matchmaking Pools

**Standard behavior:**
- A "pool" is a named, isolated queue. Players must join a pool by name; the matchmaker only matches players within the same pool.
- The most common pool axis is region (us-east, eu-west, ap-southeast), but pools can also represent game modes, rulesets, or skill brackets.
- In v1, `metadata.region` was the escape hatch. This worked but required custom `IMatchmakingStrategy` logic to filter by metadata — not first-class.
- First-class implementation: `pool_id` (string, required at enqueue) becomes a top-level field on `matchmaking_tickets` and the Redis sorted set key includes the pool: `matchmaking:{pool_id}:queue`. The leader ticker iterates all known pools.
- Pool discovery: pools are not pre-registered (that would require schema changes per-game); the set of live pools is derived from the active Redis keys. The ticker scans for all `matchmaking:*:queue` keys on startup to discover pools.
- Cross-pool matching: not supported within a single GameKit instance. This is operator-level infrastructure (e.g., run a regional GameKit instance per zone; implement cross-zone matchmaking at the load-balancer layer).

**Complexity:** MEDIUM. Requires adding `pool_id` to the ticket schema (migration), changing the Redis key structure (breaking change for v1 → v2 migrators), updating the ticker to iterate pools, and updating the enqueue API. The breaking Redis key change requires a migration note.

**Dependencies:** `GameKit.Matchmaking`; `GameKit.Core` (migration).

---

### 11. Lobby (`GameKit.Lobby` — new package)

**Lobby vs. Matchmaking vs. Party — composition model:**

This is the most architecturally significant design decision in v2.0. The evidence from PlayFab, Unity Lobby, and Nakama all points to the same model:

> **Lobby is pre-match coordination; Matchmaking is player discovery; Party is a pre-existing social group. Lobby composes with Matchmaking via an "arranged lobby" pattern — Matchmaking finds the players, Lobby provides the coordination room.**

Concrete lifecycle for GameKit v2:

```
Option A — Party-first:
  Player creates Party (v1 party ticket with known members)
  → Party enqueues a matchmaking ticket
  → Matchmaker finds a match
  → Match result triggers lobby creation with match participants
  → Lobby: ready-check, share connection string, chat
  → All ready → game server notified → session transitions to Active
  → Lobby archived

Option B — Lobby-first (open public lobbies):
  Player creates Lobby (public, waiting)
  → Other players join via lobby browser
  → When full → Party ticket enqueued from lobby members
  → Matchmaker finds a match OR lobby owner starts directly
  → Same post-match lobby flow as above
```

Both options must be supported. The `GameKit.Lobby` package is a **separate NuGet package** that:
1. Depends on `GameKit.Core` (players, game_sessions).
2. Depends on `GameKit.Matchmaking` (to enqueue party tickets from lobby membership).
3. Does NOT depend on `GameKit.Rankings` (ranking is matchmaking's concern).
4. Provides a SignalR hub for real-time lobby events (join, leave, ready-state change, chat relay).

**Lobby data model:**
- `lobbies` table: `id`, `owner_player_id`, `access_policy` (public/private/invite), `max_members`, `state` (waiting/in_match/archived), `created_at`, `archived_at`, `metadata JSONB`.
- `lobby_members` table: `lobby_id`, `player_id`, `is_ready`, `joined_at`, `metadata JSONB` (for loadout/team preferences).
- No chat history table. The SignalR hub relays messages in real-time only. Operators who want persistence store it in their own tables.

**Ready-check:**
- Members toggle `is_ready` via `POST /api/lobbies/{id}/ready`.
- When all members are ready, the lobby owner receives a `LobbyReadyEvent` over SignalR. The owner triggers match start (separate API call) — the library does NOT auto-start on full-ready because game design varies (countdown, vote, etc.).

**Persistent groups:**
- "Persistent group" = a lobby that persists across sessions (state resets to `waiting` after a session completes, members stay). The v1 `party_members` model in Matchmaking is ephemeral (destroyed after match). `GameKit.Lobby` groups persist until explicitly disbanded.
- After a session completes, the `GameKit.Lobby` `ISessionLifecycleObserver` (same pattern as v1 Presence) resets member `is_ready` to false and sets lobby state back to `waiting`.

**In-lobby chat:**
- Ephemeral only. The SignalR hub broadcasts messages to all lobby members. No storage. Operators hook `ILobbyMessageHandler` to intercept and store/moderate if needed.
- Out of scope: profanity filtering, content moderation, message history — these are operator concerns.

**Lobby ↔ Matchmaking composition:**
- When the lobby owner triggers matchmaking from a lobby, `GameKit.Lobby` creates a party ticket in `GameKit.Matchmaking` with the lobby's members. The `matchmaking_ticket.lobby_id` FK links the ticket back to the lobby.
- On match found, `GameKit.Matchmaking` raises an event (existing `IMatchFoundHandler` pattern or new). `GameKit.Lobby` subscribes and transitions the lobby to `in_match` state, broadcasts the session connection info to all members.
- Backfill re-entries: when a backfill slot opens in the matched session, the lobby's remaining members (or new members who joined post-match) can be re-enqueued.

**Complexity:** HIGH. New package, new schema (2 tables), SignalR hub, Redis backplane requirement (for multi-replica), lobby lifecycle state machine, integration with Matchmaking party tickets and session lifecycle observer pattern.

**Dependencies:** `GameKit.Core`; `GameKit.Matchmaking`; ASP.NET Core SignalR (in shared framework); Redis (StackExchange.Redis, already present).

---

### 12. Multi-Replica Admin UI

**Standard behavior:**
- Without a backplane, each replica's Blazor Server connections are isolated. A queue-depth panel on replica A shows only what replica A's in-memory state knows. A health alert on replica B never reaches a user connected to replica A.
- The fix: add the Redis SignalR backplane via `services.AddSignalR().AddStackExchangeRedis(connectionString)`. All hub method calls (groups, users, all clients) are automatically forwarded through Redis pub/sub.
- Sticky sessions (affinity routing) are still required because SignalR's HTTP negotiation and WebSocket upgrade must hit the same replica. The backplane handles message fan-out, not connection migration. Operators must configure sticky sessions at their load balancer (nginx: `ip_hash` or `cookie` affinity; Kubernetes: session affinity on Service).
- Package: `Microsoft.AspNetCore.SignalR.StackExchangeRedis`. This is the official Microsoft package, MIT-licensed, GPL-compatible, uses the StackExchange.Redis client already present.
- Message loss caveat: if Redis is unavailable, messages during the outage are dropped (not buffered). Admin UI degradation is acceptable; the panels will stale-out rather than crash.

**Complexity:** MEDIUM. The backplane configuration is a one-line `AddStackExchangeRedis()` call. The non-trivial parts are: ensuring the AdminHub (if one doesn't exist yet, needs creation), documenting the sticky-session requirement, and wiring the existing queue-depth / health panel data pushes through the hub rather than polling.

**Dependencies:** `GameKit.Admin.UI`; `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (new NuGet dep, but StackExchange.Redis is already in the project); `GameKit.Matchmaking` (queue depth); `GameKit.Core` (health).

---

## Feature Dependencies

```
GameKit.Auth.Argon2
    └──requires──> GameKit.Auth (IPasswordHasher interface)

GameKit.Auth.Google / GameKit.Auth.Apple / GameKit.Auth.Epic
    └──requires──> GameKit.Auth (IOAuthProvider interface)

Account Merge
    └──requires──> GameKit.Auth (player_identities, player_credentials, refresh_tokens)
    └──requires──> GameKit.Core (players, game_sessions, session_participants, admin_audit_log)
    └──requires (optional)──> GameKit.Rankings (player_ranks)
    └──requires (optional)──> GameKit.Matchmaking (matchmaking_tickets, party_members)

Rating-Aware Matchmaking
    └──requires──> IRatingSource (new abstraction in GameKit.Core or GameKit.Matchmaking)
    └──wired-by──> GameKit.Rankings (provides RankingsRatingSource implementing IRatingSource)
    └──requires (fix)──> GameKit.Matchmaking (EloRangeMatchmakingStrategy, enqueue path)

Rank Decay
    └──requires──> GameKit.Rankings (player_ranks, ladders, decay job)
    └──requires──> Redis (leader election for decay job)

Placement Matches
    └──requires──> GameKit.Rankings (player_ranks: new placement_matches_remaining column)
    └──requires──> GameKit.Core (session_participants, SessionCompleteService)

Backfill
    └──requires──> GameKit.Matchmaking (tickets, ticker)
    └──requires──> GameKit.Core (game_sessions, session_participants)

Regional Pools
    └──requires──> GameKit.Matchmaking (ticket schema, Redis key structure, ticker)

GameKit.Lobby
    └──requires──> GameKit.Core
    └──requires──> GameKit.Matchmaking (party ticket enqueue)
    └──requires──> ASP.NET Core SignalR (shared framework)
    └──requires──> Redis (for multi-replica SignalR backplane)
    └──enhances──> Backfill (backfill slots can be filled from lobby members)

Multi-Replica Admin UI
    └──requires──> GameKit.Admin.UI (existing Blazor Server hub)
    └──requires──> Microsoft.AspNetCore.SignalR.StackExchangeRedis
    └──requires (operator)──> Sticky sessions at load balancer level
```

### Dependency Notes

- **Rating-aware matchmaking requires IRatingSource, not a direct project reference to GameKit.Rankings.** A hard `ProjectReference` from Matchmaking → Rankings would prevent operators from installing Matchmaking without Rankings and would create a circular dependency risk for the account-merge path. The interface must live in `GameKit.Core` or `GameKit.Matchmaking` with Rankings providing the concrete impl via Scrutor scanning.

- **Account merge is a cross-package operation touching 5+ packages.** It should live in `GameKit.Core` as a service (not in Auth or Rankings) with optional module registrations: `services.AddGameKit().AddAccountMerge()`. Each package registers its own merge handler via `IAccountMergeModuleHandler`.

- **GameKit.Lobby depends on GameKit.Matchmaking.** This means Lobby cannot be installed without Matchmaking. This is an acceptable coupling — a lobby system that cannot enqueue for matchmaking is only half a lobby. Document this clearly.

- **Regional pools change the Redis key structure.** A v1 → v2 migration guide must address this. Operators who used `metadata.region` in v1 need a migration path.

---

## v2.0 Phase Recommendations

### Phase 1 (Foundation for v2 features) — Build first because others depend on it

- [ ] Rating-aware matchmaking (IRatingSource seam + EloRange fix) — unblocks rating-dependent features
- [ ] Placement matches — low complexity, high developer value, only Rankings dep
- [ ] Argon2 sibling hasher — low complexity, no blockers, standalone package
- [ ] Google OAuth provider — low complexity, highest demand OAuth provider

### Phase 2 (Matchmaking depth) — Depends on Phase 1 rating seam

- [ ] Rank decay job — depends on Rankings, can run in parallel with Lobby
- [ ] Regional matchmaking pools — changes Redis key structure; do before Lobby to avoid Lobby redis naming rework
- [ ] Apple Sign-In provider — MEDIUM complexity; can run in parallel
- [ ] Epic OAuth provider — LOW complexity; can run in parallel
- [ ] Fix Admin "Rank adjust" stub page — quick win, v1 carried-forward debt

### Phase 3 (Lobby + Backfill + Multi-replica Admin) — Highest complexity, all others should be stable first

- [ ] Backfill — depends on Matchmaking being stable
- [ ] GameKit.Lobby package — highest surface area; depends on Matchmaking + Core
- [ ] Multi-replica Admin UI (SignalR Redis backplane) — depends on Lobby SignalR patterns being established
- [ ] Account merge — highest risk; run last so rankings/matchmaking data models are stable

### Defer from v2.0

- [ ] Friends graph (`GameKit.Social`) — explicitly Out of Scope
- [ ] Chat history storage — explicitly Anti-Feature
- [ ] Cross-region federation — operator infrastructure concern

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Rating-aware matchmaking (v1 wart fix) | HIGH | MEDIUM | P1 |
| Placement matches | HIGH | LOW | P1 |
| Argon2 sibling hasher | MEDIUM | LOW | P1 |
| Google OAuth | HIGH | LOW | P1 |
| Rank decay | MEDIUM | MEDIUM | P2 |
| Regional pools | HIGH | MEDIUM | P2 |
| Apple Sign-In | MEDIUM | MEDIUM | P2 |
| Epic OAuth | LOW-MEDIUM | LOW | P2 |
| Backfill | MEDIUM | MEDIUM | P2 |
| Admin rank-adjust stub fix | HIGH (quality) | LOW | P2 |
| GameKit.Lobby (full package) | HIGH | HIGH | P2 |
| Multi-replica Admin UI | MEDIUM | MEDIUM | P2 |
| Account merge | HIGH | HIGH | P3 (highest risk) |

**Priority key:**
- P1: Block v2.0 release without them; low-risk quick wins or fixes to v1 warts
- P2: Ship in v2.0; medium complexity, manageable in parallel phases
- P3: Ship in v2.0 but requires the rest of v2 to stabilize first; highest risk

---

## Sources

- [PlayFab: Use lobby and matchmaking together](https://learn.microsoft.com/en-us/gaming/playfab/multiplayer/lobby/lobby-and-matchmaking) — MEDIUM (authoritative for lobby/matchmaking composition model)
- [Glicko.net: Glicko-2 System PDF](https://www.glicko.net/glicko/glicko2.pdf) — HIGH (algorithm specification; v1 already uses this)
- [GitHub Gist: So You Want to Use Glicko-2 for Your Game's Ratings](https://gist.github.com/gpluscb/302d6b71a8d0fe9f4350d45bc828f802) — MEDIUM (practical Glicko-2 implementation advice)
- [Hypersect: The Online Skill Ranking of INVERSUS Deluxe](http://blog.hypersect.com/the-online-skill-ranking-of-inversus-deluxe/) — MEDIUM (practical Glicko-2 in a shipped game)
- [AWS: FlexMatch Backfill Functionality](https://docs.aws.amazon.com/gameliftservers/latest/flexmatchguide/match-backfill-client.html) — HIGH (canonical backfill design reference)
- [Open Match: Backfill Guide](https://open-match.dev/site/docs/guides/backfill/) — HIGH (open-source backfill design reference)
- [MS Learn: Redis backplane for ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0) — HIGH (official SignalR Redis backplane docs)
- [Scott Brady: Implementing Sign in with Apple in ASP.NET Core](https://www.scottbrady.io/openid-connect/implementing-sign-in-with-apple-in-aspnet-core) — HIGH (SIWA ASP.NET Core implementation details)
- [Google Developers: OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect) — HIGH (Google OAuth scopes and identity data)
- [Epic Online Services: Auth Interface](https://dev.epicgames.com/docs/services/en-US/EpicAccountServices/AuthInterface/index.html) — HIGH (Epic OAuth/EAS documentation)
- [FusionAuth: Cross-Platform Gaming Accounts](https://fusionauth.io/articles/gaming-entertainment/cross-platform-game-accounts) — MEDIUM (identity management patterns for games)
- [Riot Games: LoL Rank Decay documentation](https://support-leagueoflegends.riotgames.com/hc/en-us/articles/4405783687443) — HIGH (production rank decay design)
- [DEV Community: Migrating to Argon2](https://dev.to/rsa/migrating-existing-code-to-a-new-password-hashing-algorithm-43n5) — MEDIUM (rehash-on-login migration pattern)
- [guptadeepak.com: Password Hashing in 2026 Framework](https://guptadeepak.com/bcrypt-vs-argon2-vs-scrypt-vs-pbkdf2-password-hashing-decision-framework-2026/) — MEDIUM (current password hashing recommendations)

---

*Feature research for: GameKit v2.0 — Providers, Lobby & Rating-Aware Play*
*Researched: 2026-06-05*
