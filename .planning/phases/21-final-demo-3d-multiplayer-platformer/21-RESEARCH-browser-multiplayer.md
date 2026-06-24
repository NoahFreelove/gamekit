# Phase 21: Browser Multiplayer Client-Wiring Spec

**Researched:** 2026-06-23
**Domain:** SignalR JS client vendoring, browser matchmaking polling, lobby→match flow
**Confidence:** HIGH — derived entirely from reading the actual server source and passing integration tests

---

## CRITICAL FINDING FIRST: Match-Assignment Signal

**Client-observable match assignment: YES, via REST poll, not a push event.**

The `LobbyHub`/`ILobbyClient` contract does NOT include a match-assignment push event. After
both members mark ready, `LobbyService.MarkReadyAsync` transitions the lobby to `InGame` and
broadcasts `ReceiveStateUpdateAsync({ State: "InGame" })` via SignalR. This tells each client
**the party entered matchmaking**, NOT that a session has been created. The session ID is only
observable by polling `GET /api/mm/queue/{ticketId}/status` until `status === "matched"`, at
which point `sessionId` is populated in the response.

Concretely: the browser client needs **two channels** — SignalR for lobby state transitions
(Open → ReadyChecking → InGame), and a REST poll loop for the ticket status after InGame.
No `GameKit.*` package change is needed. This is entirely within the D-15 boundary.

---

## D-15 Compliance Boundary

Every change described in this spec touches ONLY:
- `samples/Platformer3D/wwwroot/js/lobby.js` (new file)
- `samples/Platformer3D/wwwroot/js/signalr.min.js` (new vendored file)
- `samples/Platformer3D/wwwroot/index.html` (edit)
- `samples/Platformer3D/wwwroot/js/game.js` (edit — wire `_matchId` from lobby flow)
- `REUSE.toml` (add annotation for `signalr.min.js`)
- `THIRD-PARTY-NOTICES.md` (add SignalR JS section)

**No `GameKit.*` package source changes. No server-side code changes in `samples/Platformer3D/Program.cs`
or the GameServer assembly. No new migrations. D-15 confirmed: all server-side infrastructure is
already live and passing the 21-06 integration tests.**

---

## Section 1: End-to-End Sequence with Concrete APIs

The following sequence is extracted verbatim from the passing integration tests in
`tests/GameKit.Platformer3D.Integration.Tests/`. Each step lists the exact API call,
payload, and what the client observes.

### Phase A: Guest Sign-In (already working)

```
Step 1: POST /auth/login/guest
  Headers: Content-Type: application/json, X-GameKit-Device: <uuid>
  Body: {}
  Response 200: { accessToken: "<JWT>", refreshToken: "<JWT>" }
  Client action: store accessToken in module memory; refreshToken in localStorage["gk.refresh_token"]
```

Existing `game.js:guestSignIn()` already implements this correctly. No changes needed.

### Phase B: Lobby Creation or Join

**Owner tab (creates the lobby):**

```
Step 2a: POST /api/lobbies
  Headers: Authorization: Bearer <JWT>, Content-Type: application/json
  Body: { maxMembers: 2, ladderId: "<platformerLadderId>" }
  Response 200: {
    lobbyId: "<guid>",
    state: "Open",
    maxMembers: 2,
    regionName: null,
    ladderId: "<platformerLadderId>",
    createdAt: "<iso>"
  }
  Client stores: lobbyId, shows invite code = lobbyId (copy to share with friend)
```

**Joiner tab (joins the existing lobby):**

```
Step 2b: POST /api/lobbies/{lobbyId}/join
  Headers: Authorization: Bearer <JWT>, Content-Type: application/json
  Body: { lobbyId: "<guid>" }
  Response 200: {
    lobbyId: "<guid>",
    state: "ReadyChecking",      ← IMPORTANT: second joiner fills to MaxMembers=2,
    maxMembers: 2,                   triggering Open→ReadyChecking automatically
    memberCount: 2
  }
```

**Key invariant (from `LobbyService.JoinLobbyAsync`):** When the second player joins a
`MaxMembers=2` lobby, the server automatically transitions `Open→ReadyChecking` and broadcasts
`ReceiveStateUpdateAsync({ State: "ReadyChecking" })` via SignalR to all lobby group members.
No explicit "start ready check" call is needed from the client.

**Ladder ID resolution (needed for Step 2a body):**

```
Step 0: GET /demo/ladder-id/platformer
  Response 200: { id: "<guid>", name: "platformer" }
  Client stores: platformerLadderId
```

This demo-only endpoint is already in `Program.cs`. Call it once after sign-in.

### Phase C: SignalR Hub Connection and Lobby Group Subscribe

After REST lobby create/join, connect to SignalR and subscribe to the lobby group.
Both players must do this before marking ready to receive broadcasts.

```
Step 3: WS upgrade to /hubs/lobby?access_token=<JWT>
  (SignalR JS client handles this automatically via AccessTokenFactory)

Step 4: Hub invoke → "JoinLobbyAsync", lobbyId: "<guid>"
  Server: verifies membership in DB, adds connection to SignalR group "lobby:{lobbyId}"
  No return value (void hub method)
```

The `LobbyJwtBearerPostConfigure` already reads `?access_token` from the query string for
the `/hubs/lobby` path — the JS SignalR client's `accessTokenFactory` option sets this correctly.

### Phase D: Ready-Check

```
Step 5: Hub invoke → "MarkReadyAsync", lobbyId: "<guid>"
  Both players call this. Second call triggers the all-ready gate.

Server-side sequence after second MarkReadyAsync:
  1. SERIALIZABLE tx: all members marked ready → lobby.State = InGame
  2. Party created via IPartyService.CreateAsync(owner)
  3. Each non-owner member joined to party via IPartyService.JoinAsync(partyCode, memberId)
  4. IMatchmakingService.EnqueueAsync(ownerId, ladderId, poolName="default", partyId) → ticket
  5. IHubContext broadcasts ReceiveStateUpdateAsync({ LobbyId, State: "InGame" }) to group

Client receives: ReceiveStateUpdateAsync event with State === "InGame"
```

**IMPORTANT:** The `LobbyService.TryStartMatchmakingAsync` call uses `lobbyRow.RegionName ?? _options.DefaultPoolName`
as the pool name. The lobby in the demo is created with `regionName: null`, so it uses
`_options.DefaultPoolName`. The `GameKitLobbyOptions.DefaultPoolName` is `"default"` unless
overridden in config. The matching memory note "Sample matchmaking default pool" confirms:
pool name must be `"default"` (= `null` in the `EnqueueRequest`). The Lobby service handles
this automatically — the client never calls `POST /api/mm/queue` directly in the lobby flow.

### Phase E: Ticket Status Poll (match assignment)

The SignalR `InGame` broadcast does NOT include a ticket ID or session ID. After receiving
`InGame`, each client must independently discover its own ticket ID and then poll for the match.

**Problem:** The `ILobbyClient.ReceiveStateUpdateAsync` event carries only
`{ LobbyId, State, Detail }` — no ticket ID, no session ID. The client does not know its
ticket ID from the hub event.

**Solution (within D-15 boundary, no package change needed):**
The client must call `POST /api/mm/queue` to see if a ticket already exists (it will fail with
`409 Conflict { error: "ticket_active" }` if one exists), OR query the ticket status through a
demo-specific helper. The cleaner approach: add a demo-only endpoint to `Program.cs` that
returns the player's active ticket ID given their JWT.

**Recommended: Add to `samples/Platformer3D/Program.cs`:**

```csharp
// Demo helper — returns the calling player's active matchmaking ticket id (if any).
// Used by the browser client after receiving the InGame lobby broadcast to discover
// the ticket id for the poll loop.
app.MapGet("/demo/my-ticket", async (
    HttpContext ctx,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var playerId))
        return Results.Unauthorized();

    // Find the most recent active ticket (Status 0=Queued, 1=Proposed) for this player.
    var ticket = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(
            db.Set<GameKit.Matchmaking.Entities.MatchmakingTicket>()
              .Where(t => t.Status == GameKit.Matchmaking.Entities.TicketStatus.Queued
                       || t.Status == GameKit.Matchmaking.Entities.TicketStatus.Proposed)
              .Join(db.Set<GameKit.Matchmaking.Entities.PartyMember>(),
                    t => t.PartyId, pm => pm.PartyId, (t, pm) => new { t, pm })
              .Where(x => x.pm.PlayerId == playerId)
              .OrderByDescending(x => x.t.QueuedAt)
              .Select(x => new { x.t.Id }),
            ct);

    return ticket is null
        ? Results.NotFound(new { error = "no_active_ticket" })
        : Results.Ok(new { ticketId = ticket.Id });
}).RequireAuthorization();
```

**Alternative (simpler, no EF join):** After the `InGame` hub event, the client can re-try
`POST /api/mm/queue` with the ladder ID. If the lobby flow already enqueued, the response will
be `409 { error: "ticket_active" }`. The ticket ID is NOT returned in a 409 — so this approach
does not work. The `/demo/my-ticket` endpoint is the correct solution.

**BLOCKER ASSESSMENT:** This is NOT a `GameKit.*` package change. The endpoint lives in
`samples/Platformer3D/Program.cs` — within D-15 boundary. This does require one line of
server-side demo code, but that is explicitly allowed: "touch ONLY `samples/Platformer3D*`."

Once the client has the ticket ID:

```
Step 6a: GET /demo/my-ticket
  Headers: Authorization: Bearer <JWT>
  Response 200: { ticketId: "<guid>" }
  OR 404: { error: "no_active_ticket" } — retry with 500ms delay

Step 6b: GET /api/mm/queue/{ticketId}/status    ← long-poll
  Headers: Authorization: Bearer <JWT>
  Response 200: {
    status: "queued" | "proposed" | "matched" | "cancelled",
    proposalId: "<guid>",  // when status="proposed"
    deadline: "<iso>",     // when status="proposed"
    sessionId: "<guid>"    // when status="matched"
  }
  Note: long-poll endpoint holds the connection for up to LongPollTimeoutSeconds then returns.
  Client: loop until status === "matched" or "cancelled"
```

### Phase F: Proposal Accept (required before "matched")

The matchmaker uses a proposal/accept step (the `BestTimeMatchmakingStrategy.BuildMatchResult`
creates a proposal). Both players must accept before the session is created.

```
Step 7 (when status="proposed"):
  POST /api/mm/proposal/{proposalId}/accept
  Headers: Authorization: Bearer <JWT>, Content-Type: application/json
  Body: { ticketId: "<ticketId>" }
  Response 200: { status: "queued"|"matched", proposalId: "<guid>" }
  Client: resume poll loop after accepting
```

After both accept → next poll returns `status: "matched"` with `sessionId`.

### Phase G: Game Session (existing WS flow, already working)

```
Step 8: WS connect to /ws/game/{sessionId}
  Headers: Authorization: Bearer <JWT>  (set via HTTP upgrade header)
  (game.js:submitRunSummary already implements this)

Step 9-11: run_start → checkpoint(s) → run_finish frames  (existing game.js)

Step 12: receive "validated" or "rejected" frame

Step 13: initGame(sessionId) starts — CHANGED: matchId is now the sessionId from step 6b,
  not from a user-typed input field.
```

### Phase H: Leaderboard Read (post-match display)

**IMPORTANT:** There is NO public player-facing leaderboard REST endpoint. The only leaderboard
endpoint is `GET /admin/api/leaderboard?ladderId=&limit=` which requires admin cookie auth
(`GameKitAdmin` scheme + admin policy). A browser player JWT cannot call it.

**Solution options (both within D-15 boundary):**

**Option A (recommended):** Add a demo-only leaderboard endpoint to `Program.cs`:

```csharp
// Demo leaderboard — anonymous (read-only) leaderboard for the platformer ladder.
app.MapGet("/demo/leaderboard", async (
    GameKit.Rankings.Services.ILeaderboardService svc,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var ladder = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(db.Set<GameKit.Rankings.Entities.Ladder>()
            .Where(l => l.Name == "platformer"), ct);
    if (ladder is null) return Results.NotFound(new { error = "ladder_not_found" });
    var rows = await svc.TopAsync(ladder.Id, 20, seasonId: null, ct);
    return Results.Ok(rows);
});
```

**Option B:** Rely on the admin console (`/admin`) for leaderboard visibility, and in the
browser client show only the ticker-driven rating from polling the player's own data. This
avoids the additional endpoint but reduces the demo's self-contained appeal.

The existing `ILeaderboardService.TopAsync(ladderId, limit, seasonId, ct)` is the correct
service — it returns `IReadOnlyList<LeaderboardRowDto>` with rank, displayName, rating, wins,
losses, draws.

---

## Section 2: SignalR Hub Client Contract for the Browser

### Hub Route
`/hubs/lobby`

### JWT Authentication for SignalR
The `LobbyJwtBearerPostConfigure` reads `?access_token=<JWT>` from the query string for
requests to `/hubs/lobby`. The `@microsoft/signalr` JS client sends this automatically when
`accessTokenFactory` is configured:

```javascript
const conn = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/lobby', {
    accessTokenFactory: () => _accessToken   // the JWT stored in module memory
  })
  .withAutomaticReconnect()
  .build();
```

The `HubConnectionBuilder.withUrl` sends `?access_token=<token>` on the WebSocket upgrade
request. This is the standard SignalR pattern for WebSocket auth in browsers.

### Hub is `[Authorize]`-gated
Unauthenticated connections receive HTTP 401 before the WebSocket is established.

### Client → Server Methods

| Method | JS call | Arguments | Notes |
|--------|---------|-----------|-------|
| `JoinLobbyAsync` | `conn.invoke("JoinLobbyAsync", lobbyId)` | `lobbyId: string (UUID)` | Must call after `StartAsync`. Server verifies DB membership. |
| `MarkReadyAsync` | `conn.invoke("MarkReadyAsync", lobbyId)` | `lobbyId: string (UUID)` | Triggers all-ready gate if all members ready. |
| `SendChatMessageAsync` | `conn.invoke("SendChatMessageAsync", lobbyId, message)` | `lobbyId, message: string` | Optional — not needed for the demo loop. |

### Server → Client Events

| Event | JS handler | Payload type | When fired |
|-------|------------|--------------|------------|
| `ReceiveStateUpdateAsync` | `conn.on("ReceiveStateUpdateAsync", handler)` | `{ LobbyId: string, State: string, Detail: string\|null }` | After any state transition: Open→ReadyChecking, ReadyChecking→InGame |
| `ReceiveChatMessageAsync` | `conn.on("ReceiveChatMessageAsync", handler)` | `(senderId: string, message: string)` | On chat relay |

### `State` values in `ReceiveStateUpdateAsync`

The `LobbyState` enum serializes as its integer value over the wire in the default SignalR
JSON protocol:
- `0` = `Open`
- `1` = `ReadyChecking`
- `2` = `Closed`
- `3` = `InGame`

**CAUTION:** SignalR's default JSON serialization of a C# `enum` sends the **integer** (0/1/2/3),
not the string name. The integration test checks `upd.State == LobbyState.InGame` in C#, which
compares the deserialized enum. The JS client receives the integer. Lobby.js must compare
`upd.State === 3` (not `"InGame"`).

Verify at runtime: if `LobbyStateUpdate` is configured with `System.Text.Json` enum string
serialization, the value would be `"InGame"`. Check `AddJsonOptions` / `AddSignalR` config in
the Lobby builder — the default STJ behavior is numeric. The integration test confirms the
server sends the integer value (the C# `HubProtocolNameAttribute` is not set, so MessagePack
is not in use; default is JSON with numeric enums).

**Safe approach:** In JS, check `upd.State === 3` for InGame, `upd.State === 1` for
ReadyChecking. Add a comment referencing `LobbyState` enum values.

### Reconnect Behavior

`LobbyHub.OnConnectedAsync` re-adds the connection to all lobby groups the player belongs to
by querying `ILobbyService.GetPlayerLobbyIdsAsync`. This means `withAutomaticReconnect()` works
correctly — the hub restores group membership on reconnect without client action.

---

## Section 3: Match-Assignment Mechanism (The Crux)

**Finding: NO lobby hub push for session ID. Client-observable match assignment is via REST poll.**

Traced path:
1. `LobbyHub.MarkReadyAsync` → `ILobbyService.MarkReadyAsync`
2. `LobbyService.TryStartMatchmakingAsync` → `IMatchmakingService.EnqueueAsync` → ticket created
3. Hub broadcasts `ReceiveStateUpdateAsync({ State: 3 })` — lobby is InGame
4. Matchmaker ticker (`BestTimeMatchmakingStrategy.Match`) runs every 500ms
5. When two parties match → proposal created → both must accept via REST
6. On all-accept → session created in DB → `TicketStatusResponse.SessionId` populated
7. Next `GET /api/mm/queue/{ticketId}/status` returns `{ status: "matched", sessionId }`

**There is NO `MatchAssigned` or `SessionCreated` event in `ILobbyClient`.** The hub's
server→client interface has exactly two methods: `ReceiveChatMessageAsync` and
`ReceiveStateUpdateAsync`. Neither carries a session ID.

**In-samples workaround (D-15 compliant):**

```
After ReceiveStateUpdateAsync with State===3 (InGame):
  1. Poll GET /demo/my-ticket until 200 (ticket ID discovered)
  2. Start poll loop on GET /api/mm/queue/{ticketId}/status
  3. When status==="proposed": POST /api/mm/proposal/{proposalId}/accept
  4. When status==="matched": sessionId is the matchId for the WS game session
```

The `/demo/my-ticket` endpoint (described in Section 1, Step 6a) is the only demo-specific
server change needed. It is one endpoint in `samples/Platformer3D/Program.cs` — D-15 compliant.

---

## Section 4: Matchmaking Enqueue and Pool

### How the Lobby Flow Enqueues

In the **lobby flow**, the client does NOT call `POST /api/mm/queue` directly. Instead:
- `LobbyService.TryStartMatchmakingAsync` calls `IMatchmakingService.EnqueueAsync(ownerId, ladderId, poolName, partyId)`
- This is triggered server-side by `MarkReadyAsync` when all members are ready

The client therefore never needs to know the pool name for the lobby flow. The pool is resolved
as `lobbyRow.RegionName ?? _options.DefaultPoolName`. Since lobbies are created with no
`regionName`, the pool is `"default"`.

### Pool Name Nuance

Per the memory note "Sample matchmaking default pool": the default pool is `"default"`. The
`EnqueueRequest.PoolName: null` resolves to `"default"` server-side. The Platformer3D demo
uses this default pool — there is no special pool name.

### Solo Enqueue (alternative path, for reference)

The integration test smoke flow in `EndToEndSmokeTests` uses a **direct solo enqueue** (no
lobby) for simplicity:

```javascript
POST /api/mm/queue
Body: { ladderId: "<platformerLadderId>", poolName: null, partyId: null }
Response 200: { ticketId: "<guid>", status: "queued" }
```

The browser demo uses the **lobby flow** (R9), not direct enqueue. The solo enqueue path is
available as a fallback for testing.

---

## Section 5: Leaderboard Read

### Situation

There is **no public player-facing leaderboard endpoint** in `GameKit.Rankings`. The only
leaderboard query is:

```
GET /admin/api/leaderboard?ladderId=<guid>&limit=<int>
Authorization: GameKitAdmin cookie (not player JWT)
```

This is admin-cookie-auth-gated and cannot be called by a browser with a player JWT.

### Demo Solution

Add a demo-only endpoint to `samples/Platformer3D/Program.cs`:

```
GET /demo/leaderboard
Authorization: None (anonymous) or Bearer JWT (either is fine for a demo)
Response 200: [
  { rank: 1, playerId: "<guid>", displayName: "...", rating: 1234.5, ratingDeviation: 45.2,
    wins: 3, losses: 1, draws: 0, isInPlacement: false, placementMatchesRemaining: 0 },
  ...
]
```

Uses `ILeaderboardService.TopAsync(ladderId, limit, seasonId: null, ct)` directly. No admin
auth, no antiforgery — this is a read-only demo leaderboard.

### When to Show

After the WS session completes and the player receives the `"validated"` frame, the client can:
1. Show the validated run time
2. Fetch `/demo/leaderboard` and render top-N rows
3. Optionally also display the player's own rating (extracted from the leaderboard response by matching `playerId === myPlayerId`)

The `ILeaderboardService` is already registered by `gameKitBuilder.AddRankings()`. The ratings
ticker runs every 1 minute in the demo (`RatingPeriod = TimeSpan.FromMinutes(1)`), so the
leaderboard update may lag by up to 1 minute after the session completes.

---

## Section 6: SignalR JS Client Vendoring Plan

### Version Selection

The server uses `Microsoft.AspNetCore.SignalR.StackExchangeRedis` version `10.0.8` and
`Microsoft.AspNetCore.SignalR.Client` version `10.0.8` (from `Directory.Packages.props`).
The `@microsoft/signalr` npm package ships the browser JS client. The npm package version
aligns with the server version: `@microsoft/signalr@10.0.8` must be used.

**ASSUMPTION [A1]:** `@microsoft/signalr` version `10.0.8` is published on npm. The version
number follows the ASP.NET Core release cadence (.NET 10 = version 10.x). This must be
verified with `npm view @microsoft/signalr versions` before committing.

**Fallback:** If `10.0.8` is not yet on npm, use the latest `8.x` stable version (the JS
client is backward-compatible with the server for the methods used here). The protocol is
negotiated over HTTP so minor version differences are acceptable for hub invocations.

### Bundle to Vendor

The IIFE (Immediately Invoked Function Expression) browser bundle is the correct artifact
for script-tag usage in a non-bundled ES module environment:

```
From npm package @microsoft/signalr (version 10.0.8 or latest compatible):
  dist/browser/signalr.min.js   ← IIFE global, exposes window.signalR
```

However, `game.js` is an ES module (`type="module"`). The IIFE bundle exposes `window.signalR`,
which is accessible from an ES module via the global. Alternatively, use the ESM bundle:

```
  dist/esm/signalr.js           ← ES module (import ... from)
```

**Recommendation:** Use the ESM bundle (`dist/esm/signalr.js`) renamed to `signalr.module.js`
so `lobby.js` can `import * as signalR from '/js/signalr.module.js'` — matching the pattern
already used for `three.module.js`.

### Download Command (one-time, committed to repo)

```bash
# From repo root, in a temporary npm context:
npm pack @microsoft/signalr@10.0.8
tar xzf microsoft-signalr-10.0.8.tgz
cp package/dist/esm/signalr.js \
   samples/Platformer3D/wwwroot/js/signalr.module.js
rm -rf package microsoft-signalr-10.0.8.tgz
```

The file is committed to git. No CDN, no runtime npm call.

### License

`@microsoft/signalr` is licensed MIT. License text is in the package's `LICENSE` file:

```
Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction...
```

This is MIT-compatible with the repo's GPL-3.0-or-later top-level license. The same pattern
as three.js (MIT AND GPL-3.0-or-later applies to the vendored file).

### REUSE.toml Entry

Add to `REUSE.toml` after the three.js annotation:

```toml
# @microsoft/signalr (vendored MIT) — keep in sync with THIRD-PARTY-NOTICES.md
[[annotations]]
path = "samples/Platformer3D/wwwroot/js/signalr.module.js"
precedence = "override"
SPDX-FileCopyrightText = ".NET Foundation and Contributors"
SPDX-License-Identifier = "MIT AND GPL-3.0-or-later"
```

### THIRD-PARTY-NOTICES.md Entry

Add a new section after the three.js section:

```markdown
## @microsoft/signalr

**Purpose:** Browser SignalR client for the Platformer3D lobby/party hub connection
(`/hubs/lobby`). Bundled locally (no CDN) at
`samples/Platformer3D/wwwroot/js/signalr.module.js`.

**Upstream URL:** https://github.com/dotnet/aspnetcore

**npm Package:** `@microsoft/signalr`

**Version vendored:** 10.0.8 (or latest compatible with ASP.NET Core 10.0.x server)

**SPDX-License-Identifier:** `MIT`

**Full verbatim LICENSE text:**

[Copy MIT LICENSE from the npm package at time of download]
```

---

## Section 7: Concrete Task Breakdown for the Executor

### Task 1: Bug A Fix — Auth Screen Hide

**What:** `game.js:DOMContentLoaded` handler hides `#auth-panel` after guest sign-in, but
`#auth-panel` is a child of `#auth-screen`. The screen itself remains visible, rendering the
game canvas below the fold.

**File:** `samples/Platformer3D/wwwroot/js/game.js`

**Change:** In the `btnGuest` click handler:
```javascript
// CURRENT (wrong):
if (authPanel)  authPanel.classList.add('hidden');

// FIX:
const authScreen = document.getElementById('auth-screen');
if (authScreen) authScreen.classList.add('hidden');
```

**Acceptance check:** After clicking "Play as Guest", the `#auth-screen` div disappears and the
`#game-section` fills the full viewport.

---

### Task 2: Vendor @microsoft/signalr

**File:** `samples/Platformer3D/wwwroot/js/signalr.module.js` (new)

**Action:**
1. Run `npm pack @microsoft/signalr@10.0.8` in a temp dir
2. Extract `package/dist/esm/signalr.js` → `samples/Platformer3D/wwwroot/js/signalr.module.js`
3. Verify the file starts with ES module `export` statements (not IIFE)

**Also update:**
- `REUSE.toml` — add annotation (see Section 6)
- `THIRD-PARTY-NOTICES.md` — add section (see Section 6)

**Acceptance check:**
```javascript
// In browser console (after serving):
import('/js/signalr.module.js').then(m => console.log(typeof m.HubConnectionBuilder))
// Should print "function"
```

---

### Task 3: Add Demo-Only Server Endpoints to Program.cs

**File:** `samples/Platformer3D/Program.cs`

**Add two endpoints after the existing `/demo/ladder-id/{name}` endpoint:**

**3a: `/demo/my-ticket`** (described in Section 1, Step 6a)

```csharp
app.MapGet("/demo/my-ticket", async (
    HttpContext ctx,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var playerId))
        return Results.Unauthorized();

    // Find the caller's most recent active ticket (Queued=0 or Proposed=1).
    // Uses EF LINQ — no raw SQL needed; GameKitDbContext has both sets registered
    // via PlatformerTestModelCustomizer pattern in the integration test host, and via
    // the full model in the production host (all packages registered, all tables visible).
    var row = await db.Set<GameKit.Matchmaking.Entities.MatchmakingTicket>()
        .Join(db.Set<GameKit.Matchmaking.Entities.PartyMember>(),
              t => t.PartyId,
              pm => pm.PartyId,
              (t, pm) => new { Ticket = t, Member = pm })
        .Where(x => x.Member.PlayerId == playerId
                 && ((int)x.Ticket.Status == 0 || (int)x.Ticket.Status == 1))
        .OrderByDescending(x => x.Ticket.QueuedAt)
        .Select(x => new { ticketId = x.Ticket.Id })
        .FirstOrDefaultAsync(ct);

    return row is null
        ? Results.NotFound(new { error = "no_active_ticket" })
        : Results.Ok(row);
}).RequireAuthorization();
```

**CAUTION:** `GameKit.Matchmaking.Entities.MatchmakingTicket` and `PartyMember` must be
accessible from the `Platformer3D` project. They are — the csproj references `GameKit.Matchmaking`
(confirmed from the bin/Debug output showing `GameKit.Matchmaking.dll`). The EF sets are
registered via the `GameKitDbContext` model builder when `AddMatchmaking()` is called.

**3b: `/demo/leaderboard`** (described in Section 5)

```csharp
app.MapGet("/demo/leaderboard", async (
    GameKit.Rankings.Services.ILeaderboardService svc,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var ladder = await db.Set<GameKit.Rankings.Entities.Ladder>()
        .Where(l => l.Name == "platformer")
        .FirstOrDefaultAsync(ct);
    if (ladder is null)
        return Results.NotFound(new { error = "ladder_not_found" });
    var rows = await svc.TopAsync(ladder.Id, limit: 20, seasonId: null, ct);
    return Results.Ok(rows);
});
// Anonymous — read-only demo leaderboard, no auth required.
```

**Acceptance check for 3a:** After marking ready + InGame broadcast, curl with player JWT
returns `{ ticketId: "<guid>" }`.

**Acceptance check for 3b:** After a full match loop, curl returns JSON array with
`rating` values different from 1000.0.

---

### Task 4: Create lobby.js

**File:** `samples/Platformer3D/wwwroot/js/lobby.js` (new file)

This is the new ES module that wires party/lobby UI to the SignalR hub and matchmaking poll.

**Exported API** (consumed by `game.js`):

```javascript
// Called after guestSignIn() succeeds, passing the JWT from game.js module scope.
// Returns a Promise<string> that resolves to the sessionId when a match is found.
// Rejects on abort or timeout.
export async function runLobbyFlow(getAccessToken)

// Exported for index.html DOMContentLoaded bootstrap (optional — can be internal)
export function initLobbyUI(getAccessToken)
```

**Full lobby.js pseudocode contract:**

```javascript
import * as signalR from '/js/signalr.module.js';

// ── State ─────────────────────────────────────────────────────────────
let _conn = null;          // HubConnection
let _lobbyId = null;       // current lobby
let _ticketId = null;      // matchmaking ticket
let _pollAbort = null;     // AbortController for poll loop

// ── Entry point ───────────────────────────────────────────────────────
export async function runLobbyFlow(getAccessToken) {
  // 1. Resolve platformer ladder ID
  const ladderResp = await fetch('/demo/ladder-id/platformer');
  const { id: ladderId } = await ladderResp.json();

  // 2. Enable UI controls (Join + Ready buttons become active)
  enableLobbyControls(getAccessToken, ladderId);

  // 3. Return a Promise that resolves to sessionId when matched
  return new Promise((resolve, reject) => {
    _matchResolve = resolve;
    _matchReject = reject;
  });
}

// ── Create lobby (owner tab) ──────────────────────────────────────────
async function createLobby(getAccessToken, ladderId) {
  const resp = await authFetch('/api/lobbies', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ maxMembers: 2, ladderId }),
  }, getAccessToken);
  const body = await resp.json();
  _lobbyId = body.lobbyId;
  showLobbyCode(_lobbyId);  // Display UUID as invite code
  await connectHub(getAccessToken);
}

// ── Join lobby (joiner tab) ───────────────────────────────────────────
async function joinLobby(getAccessToken, inviteCode) {
  // inviteCode IS the lobbyId (UUID) — displayed by the owner tab
  _lobbyId = inviteCode.trim();
  const resp = await authFetch(`/api/lobbies/${_lobbyId}/join`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ lobbyId: _lobbyId }),
  }, getAccessToken);
  if (!resp.ok) throw new Error(`join failed (${resp.status})`);
  await connectHub(getAccessToken);
}

// ── SignalR hub connection ────────────────────────────────────────────
async function connectHub(getAccessToken) {
  _conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/lobby', {
      accessTokenFactory: () => getAccessToken()
    })
    .withAutomaticReconnect()
    .build();

  _conn.on('ReceiveStateUpdateAsync', handleStateUpdate);

  await _conn.startAsync();
  await _conn.invoke('JoinLobbyAsync', _lobbyId);

  setLobbyStatus('Connected. Click Ready when you are ready to race.');
  enableReadyButton(true);
}

// ── Ready ─────────────────────────────────────────────────────────────
async function markReady(getAccessToken) {
  enableReadyButton(false);
  setLobbyStatus('Sending ready...');
  await _conn.invoke('MarkReadyAsync', _lobbyId);
  setLobbyStatus('Ready sent. Waiting for all players...');
}

// ── Hub event handler ─────────────────────────────────────────────────
async function handleStateUpdate(upd) {
  // upd.State is an integer: 0=Open, 1=ReadyChecking, 2=Closed, 3=InGame
  if (upd.State === 1) {
    setLobbyStatus('Ready check started — click Ready!');
    enableReadyButton(true);
  } else if (upd.State === 3) {
    setLobbyStatus('All ready! Entering matchmaking...');
    await startMatchPoll(getAccessToken);  // captured in closure
  }
}

// ── Match poll ────────────────────────────────────────────────────────
async function startMatchPoll(getAccessToken) {
  // Step 1: discover ticket ID (retry with 500ms delay until found)
  _ticketId = await pollForTicketId(getAccessToken);

  // Step 2: poll ticket status until matched
  _pollAbort = new AbortController();
  while (true) {
    const resp = await authFetch(
      `/api/mm/queue/${_ticketId}/status`,
      { signal: _pollAbort.signal },
      getAccessToken
    );
    if (!resp.ok) { /* handle 404/error */ break; }
    const body = await resp.json();

    if (body.status === 'proposed' && body.proposalId) {
      setLobbyStatus('Match proposed! Accepting...');
      await authFetch(`/api/mm/proposal/${body.proposalId}/accept`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ticketId: _ticketId }),
      }, getAccessToken);
      setLobbyStatus('Accepted. Waiting for opponent...');
    } else if (body.status === 'matched' && body.sessionId) {
      setLobbyStatus(`Matched! Session: ${body.sessionId}`);
      _matchResolve(body.sessionId);  // resolve the runLobbyFlow Promise
      return;
    } else if (body.status === 'cancelled') {
      setLobbyStatus('Matchmaking cancelled. Try again.');
      _matchReject(new Error('cancelled'));
      return;
    }
    // 'queued' — long-poll returned, loop immediately
  }
}

async function pollForTicketId(getAccessToken, maxAttempts = 15) {
  for (let i = 0; i < maxAttempts; i++) {
    const resp = await authFetch('/demo/my-ticket', {}, getAccessToken);
    if (resp.ok) {
      const body = await resp.json();
      return body.ticketId;
    }
    await new Promise(r => setTimeout(r, 500));
  }
  throw new Error('no_active_ticket: lobby enqueue did not complete within 7.5s');
}
```

**Acceptance check:** Two browser tabs complete full flow from "Create Lobby" → "Join" →
"Ready" (both) → InGame broadcast → ticket poll → proposal accept → "matched" → `sessionId`
returned.

---

### Task 5: Update index.html

**File:** `samples/Platformer3D/wwwroot/index.html`

**Changes:**
1. Add "Create Lobby" button to the lobby panel (currently only Join is there)
2. Add lobby status text element for feedback
3. The game script tag already exists — no new script tag needed (lobby.js is imported by
   game.js as an ES module)

**Suggested panel additions:**

```html
<!-- Add inside .lobby-panel: -->
<div class="lobby-row">
  <button id="btn-create-lobby" disabled>Create Party</button>
  <span id="lobby-code-display" class="muted"></span>
</div>
<!-- The existing invite-code-input / btn-join-lobby row stays -->
<!-- The existing btn-ready row stays -->
<div class="lobby-row">
  <span id="lobby-status" class="muted">Sign in first.</span>
</div>
```

---

### Task 6: Update game.js Integration Point

**File:** `samples/Platformer3D/wwwroot/js/game.js`

**Changes:**
1. Import `runLobbyFlow` from `lobby.js`
2. After `guestSignIn()` succeeds, call `runLobbyFlow(getToken)` which returns a Promise
3. When the Promise resolves with `sessionId`, call `initGame(sessionId)`

```javascript
// Add at top of game.js:
import { runLobbyFlow } from '/js/lobby.js';

// Replace the DOMContentLoaded handler's guest sign-in callback:
btnGuest.addEventListener('click', async () => {
  btnGuest.disabled = true;
  authError.textContent = '';
  try {
    const payload = await guestSignIn();

    // Fix Bug A: hide auth-screen, not just auth-panel
    const authScreen = document.getElementById('auth-screen');
    if (authScreen) authScreen.classList.add('hidden');
    if (gameSection) gameSection.classList.remove('hidden');

    // Run lobby flow — returns sessionId when matched
    // Existing match-id-input provides a bypass for direct testing
    const manualMatchId = document.getElementById('match-id-input')?.value?.trim();
    if (manualMatchId) {
      // Direct mode (existing behavior for solo testing)
      await initGame(manualMatchId);
    } else {
      // Lobby mode — party + matchmaking → session
      const getToken = () => _accessToken;  // closure over module-scope var
      const sessionId = await runLobbyFlow(getToken);
      await initGame(sessionId);
    }
  } catch (err) {
    authError.textContent = err.message ?? 'Sign-in or matchmaking failed.';
    btnGuest.disabled = false;
  }
});
```

**Acceptance check:** With `match-id-input` blank, the full lobby → match → game flow runs
automatically after sign-in. With a UUID in the input field, the existing solo bypass still works.

---

## Section 8: D-15 Compliance Confirmation

| Change | Location | D-15? |
|--------|----------|-------|
| Bug A fix (`authScreen` hide) | `samples/Platformer3D/wwwroot/js/game.js` | Compliant |
| Vendor `signalr.module.js` | `samples/Platformer3D/wwwroot/js/signalr.module.js` | Compliant |
| REUSE.toml signalR annotation | `REUSE.toml` | Compliant (D-15 explicitly allows) |
| THIRD-PARTY-NOTICES.md section | `THIRD-PARTY-NOTICES.md` | Compliant (D-15 explicitly allows) |
| New `lobby.js` | `samples/Platformer3D/wwwroot/js/lobby.js` | Compliant |
| `index.html` UI additions | `samples/Platformer3D/wwwroot/index.html` | Compliant |
| `game.js` import + flow change | `samples/Platformer3D/wwwroot/js/game.js` | Compliant |
| `/demo/my-ticket` endpoint | `samples/Platformer3D/Program.cs` | Compliant (samples/* only) |
| `/demo/leaderboard` endpoint | `samples/Platformer3D/Program.cs` | Compliant (samples/* only) |

**No changes to:**
- Any `src/GameKit.*` package source
- Any public API in any GameKit package
- Any Core migration
- `TicTacToeDuel*` sample

**D-15 CONFIRMED: all implementation is within `samples/Platformer3D*` and the two REUSE
tracking files.**

### No Blockers Identified

Every piece of the flow is achievable within the D-15 boundary:
- The SignalR `InGame` push is live and working (proven by 21-06 LobbyToMatchTests)
- The ticket poll endpoint exists and works (proven by EndToEndSmokeTests)
- The proposal/accept flow is live
- The WS game session is live and the JS client already implements it
- The only missing pieces are the JS lobby client, the bug fix, and two demo-only helper endpoints

---

## Section 9: Two-Tab Demo Viability

**Confirmed viable.** The integration tests (`EndToEndSmokeTests.ConcurrentParties_EachFormExactlyOneMatch`)
run concurrent players in the same host and prove no cross-contamination.

**Single-machine caveats:**

1. **Tab isolation for access tokens:** Each tab needs its own guest account (different JWT/sub).
   The current `game.js` stores `_accessToken` in module memory (not localStorage), so two
   tabs in the same browser window are independent — each tab's module scope is separate.
   Refresh tokens stored in `localStorage["gk.refresh_token"]` would collide if both tabs
   use the same browser profile. This is an existing limitation documented in the index.html
   banner: "Demo-only client: the refresh token is stored in localStorage." For the two-tab
   demo, use one regular tab + one incognito tab to avoid the localStorage collision.

2. **Lobby invite code:** The owner tab displays the `lobbyId` (UUID) as the invite code.
   The joiner pastes it into the `#invite-code-input` field. This is the "share a code"
   pattern.

3. **WebSocket connections:** Both tabs connect independently to `/hubs/lobby`. The Redis
   SignalR backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) ensures both connections
   receive group broadcasts even if they hit different ASP.NET Core instances (on a
   single-instance demo, the backplane is present but irrelevant).

4. **Matchmaker ticker:** 500ms tick interval. The two-tab demo should form a match within
   ~1s of both players marking ready (both fresh guests → cold-start bracket → any opponent
   matches).

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `@microsoft/signalr@10.0.8` is published on npm | Section 6 | Use latest 8.x instead; JS client is backward-compatible |
| A2 | `LobbyState` enum serializes as integer (not string) over SignalR JSON | Section 2 | Compare by string "InGame" in JS instead of integer 3 |
| A3 | `GameKit.Matchmaking.Entities.MatchmakingTicket` and `PartyMember` are accessible via `GameKitDbContext` in the Platformer3D host | Section 7 Task 3a | If not registered, use raw SQL instead of LINQ join |
| A4 | `ILeaderboardService` is registered in DI from `gameKitBuilder.AddRankings()` | Section 5 | It is — confirmed by RankingsBuilderExtensions pattern |

---

## Sources

### Primary (HIGH confidence — read from actual source code)
- `src/GameKit.Lobby/Hubs/ILobbyClient.cs` — server→client event contract
- `src/GameKit.Lobby/Hubs/LobbyHub.cs` — client→server methods + JWT query-string auth
- `src/GameKit.Lobby/Hubs/LobbyStateUpdate.cs` — broadcast payload shape
- `src/GameKit.Lobby/Services/LobbyService.cs` — MarkReadyAsync → TryStartMatchmakingAsync → hub broadcast
- `src/GameKit.Lobby/Services/ILobbyService.cs` — service contract
- `src/GameKit.Lobby/Http/LobbyEndpoints.cs` — REST endpoint shapes
- `src/GameKit.Lobby/LobbyJwtBearerPostConfigure.cs` — query-string JWT extraction
- `src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs` — hub route `/hubs/lobby`
- `src/GameKit.Lobby/Entities/LobbyState.cs` — enum values (integer 0-3)
- `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` — enqueue, status, accept paths
- `src/GameKit.Matchmaking/Http/Contracts/TicketStatusResponse.cs` — status response shape
- `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs` — enqueue request shape
- `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs` — leaderboard endpoint (admin only)
- `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` — leaderboard row shape
- `tests/GameKit.Platformer3D.Integration.Tests/Lobby/LobbyToMatchTests.cs` — proven sequence
- `tests/GameKit.Platformer3D.Integration.Tests/Smoke/EndToEndSmokeTests.cs` — full loop proof
- `tests/GameKit.Platformer3D.Integration.Tests/PlatformerTestApp.cs` — ConnectLobbyHub pattern
- `samples/Platformer3D/Program.cs` — host wiring, demo endpoints, pool config
- `samples/Platformer3D/wwwroot/js/game.js` — existing client (auth, WS, Bug A location)
- `samples/Platformer3D/wwwroot/index.html` — current UI structure
- `samples/TicTacToeDuel/wwwroot/matchmaking.html` — poll loop reference pattern
- `REUSE.toml` — existing annotation pattern for three.js
- `THIRD-PARTY-NOTICES.md` — existing MIT vendored-library format
- `Directory.Packages.props` — SignalR server version `10.0.8`
