# Phase 11: GameKit.Lobby (New Package) - Context

**Gathered:** 2026-06-06
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

A new `GameKit.Lobby` NuGet package delivers ready-checks, ephemeral in-lobby chat, and persistent groups — group membership (`lobbies` + `lobby_members`) is backed by Postgres tables; chat messages are relayed live via a SignalR hub on a Redis backplane and are NEVER persisted (LOBBY-04 anti-feature).

**Depends on:** Phase 9 (Lobby's `TryStartMatchmakingAsync` calls `IMatchmakingService.EnqueueAsync` with a `RegionName`; that API must be stable — i.e., regional pool support must be present — before Lobby integrates against it).

**Requirements:** LOBBY-01, LOBBY-02, LOBBY-03, LOBBY-04, LOBBY-05, LOBBY-06, OPS-11

**Success Criteria (what must be TRUE):**
1. The `GameKit.Lobby` advisory-lock key (`hashtext('gamekit.lobby.migrations')::bigint`) is live-verified pairwise-distinct from all five existing package keys in a Testcontainers Wave 0 test before any other integration tests run.
2. A player JWT authenticates a WebSocket upgrade to `/hubs/lobby`; an unauthenticated upgrade attempt returns HTTP 401 before the WebSocket handshake completes — verified by an integration test using two `TestServer` instances sharing a Redis backplane.
3. When all `lobby_members.ready = true`, `LobbyService.TryStartMatchmakingAsync` submits a party ticket to `IMatchmakingService.EnqueueAsync` and the lobby state transitions from `ReadyChecking` to `InGame`; the transition is observable via the SignalR group broadcast.
4. A chat message sent via the hub reaches all connected members in the same lobby group in real time; chat is ephemeral — an integration test asserts NO chat-message table exists and nothing is written to Postgres on send (LOBBY-04 anti-feature: no chat log storage).
5. A SignalR message broadcast from `LobbyHub` instance A reaches a client connected to `LobbyHub` instance B when both are connected to the same Redis backplane — verified by a two-`TestServer` integration test.

**UI note:** `GameKit.Lobby` ships NO visual UI — it is a backend package (SignalR hub + Postgres tables). The consuming game builds its own client against the `/hubs/lobby` SignalR endpoint. No UI-SPEC is warranted (the ROADMAP "UI hint: yes" flags lobby-adjacency, not a Blazor surface in this package).

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — discuss phase was skipped per user setting. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions. This is the project's FIRST SignalR usage and FIRST Redis-backplane usage — research the SignalR + StackExchange.Redis backplane integration carefully. The Lobby advisory lock key is TBD and MUST be live-verified (`SELECT hashtext('gamekit.lobby.migrations')::bigint`) pairwise-distinct from the five existing keys (Core=1800940027, Auth=-298890956, Admin=-2101739634, Rankings=-156812172, Matchmaking=388956820) in a Wave 0 Testcontainers test.

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research. Key surfaces to reuse: the per-package migration pattern (advisory lock + __ef_migrations_lobby history table + design-time factory + ExcludeFromMigrations for prior packages + deterministic timestamp); JWT Bearer auth (Phase 2) for WebSocket upgrade authn; IMatchmakingService.EnqueueAsync with RegionName + party ticket (Phase 9) for TryStartMatchmakingAsync; BackgroundService patterns; the new-package skeleton (csproj + AssemblyInfo + builder extension AddLobby() + MapLobby()) mirroring how GameKit.Matchmaking was bootstrapped in Phase 5; coordinated MinVer release train (the package's inclusion in the train + version-assertion is Phase 12 scope, but the csproj must be train-ready).

</code_context>

<specifics>
## Specific Ideas

No specific requirements beyond the success criteria — discuss phase skipped. LOBBY-04 is an ANTI-feature: chat is NEVER persisted (no chat table; integration test must assert no chat table exists and nothing is written to Postgres on send). SignalR Redis backplane MUST be Redis (StackExchange.Redis), NOT Azure SignalR (zero-cloud GPL constraint).

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
