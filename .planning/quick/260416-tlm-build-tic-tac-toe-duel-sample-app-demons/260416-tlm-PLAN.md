---
phase: 260416-tlm-quick
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - samples/SampleGame/                                  # directory rename -> samples/TicTacToeDuel/
  - samples/TicTacToeDuel/TicTacToeDuel.csproj            # renamed from SampleGame.csproj, RootNamespace + AssemblyName updated
  - samples/TicTacToeDuel/Program.cs                      # rewritten: demo endpoints + static files
  - samples/TicTacToeDuel/appsettings.json                # unchanged content, carried over
  - samples/TicTacToeDuel/appsettings.Development.json    # unchanged content, carried over
  - samples/TicTacToeDuel/Game/TicTacToeBoard.cs          # NEW: 3x3 board domain model (pure C#, no deps)
  - samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs  # NEW: JsonDocument <-> board (de)serialization
  - samples/TicTacToeDuel/Http/DemoEndpoints.cs           # NEW: /demo/players/register + /demo/games + /demo/games/{id} + /demo/games/{id}/moves
  - samples/TicTacToeDuel/Http/DemoContracts.cs           # NEW: request/response DTOs
  - samples/TicTacToeDuel/wwwroot/index.html              # NEW: minimal HTML client (vanilla JS, no frameworks)
  - samples/TicTacToeDuel/README.md                       # NEW: how-to-run instructions
  - GameKit.sln                                           # UPDATE: rename project reference + display name; GUID stays
autonomous: true
requirements: []   # quick task — not tied to a roadmap requirement id

must_haves:
  truths:
    - "Running `dotnet run --project samples/TicTacToeDuel` (with docker compose stack up) starts the app on http://localhost:5000 without error."
    - "POSTing to /demo/players/register with {displayName:\"Alice\"} inserts a row into gamekit.players via the GameKit EF stack and returns 200 with the new player id."
    - "POSTing to /demo/games with two valid player ids creates a GameSession (State=Active), two SessionParticipant rows (Team=0 for X, Team=1 for O), and an initial 3x3 empty board persisted into GameSession.Metadata as JSON."
    - "POSTing to /demo/games/{id}/moves with {playerId, row, col} rejects illegal moves (cell occupied / wrong turn / session not Active / out-of-bounds) with 400, and accepts a legal move by updating Metadata and toggling whose-turn."
    - "A winning move transitions the GameSession to Completed (via existing Complete(IClock.UtcNow)) and records SessionParticipant.Result=Win/Loss; a full board with no winner transitions to Completed with both participants Result=Draw."
    - "GET /demo/games/{id} returns current board state, whose-turn, state enum, and both participants' display names (resolved via IPlayerDisplayNameResolver)."
    - "Opening http://localhost:5000 serves a static HTML page that can register two players, start a game, and play it to completion by clicking cells — no page reload, no framework build step."
    - "Every new .cs / .html file carries an SPDX GPL-3.0-or-later header in the form used by the rest of the repo."
    - "No file under src/ or tests/ is modified; dotnet restore adds no new NuGet dependencies beyond what the prior SampleGame had."
    - "dotnet build GameKit.sln succeeds after the rename (no dangling refs to SampleGame), and `dotnet run --project samples/TicTacToeDuel` stays up long enough to serve at least one request."
  artifacts:
    - path: "samples/TicTacToeDuel/TicTacToeDuel.csproj"
      provides: "Renamed project with RootNamespace=TicTacToeDuel, AssemblyName=TicTacToeDuel, IsPackable=false, ProjectReference to GameKit.Core unchanged."
      contains: "TicTacToeDuel"
    - path: "samples/TicTacToeDuel/Program.cs"
      provides: "WebApplication that calls AddGameKit(...), UseGameKit(), UseStaticFiles, UseDefaultFiles, and maps demo endpoints."
      min_lines: 20
    - path: "samples/TicTacToeDuel/Game/TicTacToeBoard.cs"
      provides: "Pure domain model: 3x3 Cell[,] with None/X/O enum, ApplyMove(row,col,mark) validation, Winner detection (rows/cols/diagonals), IsDraw detection, WhoseTurn based on move count."
      contains: "class TicTacToeBoard"
    - path: "samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs"
      provides: "Static ToJsonDocument(board) + FromJsonDocument(doc) helpers using System.Text.Json."
      contains: "TicTacToeBoardSerializer"
    - path: "samples/TicTacToeDuel/Http/DemoEndpoints.cs"
      provides: "MapDemo() extension registering POST /demo/players/register, POST /demo/games, GET /demo/games/{id}, POST /demo/games/{id}/moves — each commented with 'TEMPORARY — will be replaced by GameKit.Auth in Phase 2' above the register endpoint."
      contains: "/demo/players/register"
    - path: "samples/TicTacToeDuel/Http/DemoContracts.cs"
      provides: "RegisterPlayerRequest, CreateGameRequest, MoveRequest, GameStateResponse DTOs."
      contains: "record"
    - path: "samples/TicTacToeDuel/wwwroot/index.html"
      provides: "Single page with: two 'register player' buttons, a 'start game' button, a 3x3 clickable grid, a status line showing whose turn / winner. Uses fetch() to hit /demo/* endpoints."
      min_lines: 60
    - path: "samples/TicTacToeDuel/README.md"
      provides: "Run instructions: `docker compose up -d` -> `dotnet run --project samples/TicTacToeDuel` -> open http://localhost:5000; explains this is a Phase-1 demo and the register endpoint is temporary."
      min_lines: 15
    - path: "GameKit.sln"
      provides: "Project reference updated from 'SampleGame' + 'samples\\SampleGame\\SampleGame.csproj' to 'TicTacToeDuel' + 'samples\\TicTacToeDuel\\TicTacToeDuel.csproj'."
      contains: "TicTacToeDuel"
  key_links:
    - from: "samples/TicTacToeDuel/Http/DemoEndpoints.cs (POST /demo/games)"
      to: "GameKitDbContext.GameSessions + SessionParticipants"
      via: "IIdGenerator for ids, IClock for CreatedAt/StartedAt, GameSession.Start(now)"
      pattern: "db\\.GameSessions\\.Add|db\\.SessionParticipants\\.Add"
    - from: "samples/TicTacToeDuel/Http/DemoEndpoints.cs (POST /demo/games/{id}/moves)"
      to: "GameSession.Metadata (JsonDocument) via TicTacToeBoardSerializer"
      via: "Load session -> deserialize board -> ApplyMove -> reserialize -> SaveChangesAsync"
      pattern: "TicTacToeBoardSerializer\\.(To|From)JsonDocument"
    - from: "samples/TicTacToeDuel/Http/DemoEndpoints.cs (terminal-state handling)"
      to: "GameSession.Complete(IClock.UtcNow)"
      via: "Invoked when TicTacToeBoard.Winner != None or IsDraw"
      pattern: "session\\.Complete\\("
    - from: "samples/TicTacToeDuel/Http/DemoEndpoints.cs (GET /demo/games/{id})"
      to: "IPlayerDisplayNameResolver"
      via: "Constructor-injected; resolves each participant's display name (handles deleted-player tombstone for free)."
      pattern: "IPlayerDisplayNameResolver"
    - from: "samples/TicTacToeDuel/wwwroot/index.html"
      to: "/demo/* endpoints"
      via: "fetch() with JSON body; 9 cells wired to POST /demo/games/{id}/moves"
      pattern: "fetch\\('/demo/"
    - from: "samples/TicTacToeDuel/Program.cs"
      to: "UseStaticFiles + UseDefaultFiles"
      via: "Must be ordered before UseGameKit() so wwwroot/index.html serves at /"
      pattern: "UseDefaultFiles|UseStaticFiles"
    - from: "GameKit.sln"
      to: "samples/TicTacToeDuel/TicTacToeDuel.csproj"
      via: "Solution file edit: project name + relative path updated; ProjectGuid retained so IDE state survives"
      pattern: "TicTacToeDuel\\.csproj"
---

<objective>
Build the Tic-Tac-Toe Duel sample — rename `samples/SampleGame` to `samples/TicTacToeDuel`, add a tiny demo API (temporary register endpoint, create-game, move, get-state) that exercises the Phase 1 GameKit.Core surface (AddGameKit, GameKitDbContext, GameSession/SessionParticipant, IClock, IIdGenerator, IPlayerDisplayNameResolver, JsonDocument metadata), and ship a zero-framework static HTML + vanilla-JS client. End result: a human can open http://localhost:5000, register two players, start a game, and play it through.

Purpose: give the newly-completed Phase 1 foundation an executable, visual proof of life — exercising real Postgres persistence, real session state transitions, and the metadata JSONB column — without waiting on Phase 2 (Auth). This doubles as the reference "how does a game integrate GameKit.Core?" sample.

Output: a working, self-contained sample project under `samples/TicTacToeDuel/`, a solution reference update, and a README explaining how to run it.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md
@samples/SampleGame/Program.cs
@samples/SampleGame/SampleGame.csproj
@samples/SampleGame/appsettings.json
@samples/SampleGame/appsettings.Development.json
@src/GameKit.Core/Entities/Player.cs
@src/GameKit.Core/Entities/GameSession.cs
@src/GameKit.Core/Entities/SessionParticipant.cs
@src/GameKit.Core/Entities/GameSessionState.cs
@src/GameKit.Core/Data/GameKitDbContext.cs
@src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
@src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs
@src/GameKit.Core/Http/PlayerEndpoints.cs
@src/GameKit.Core/GameKitOptions.cs
@src/GameKit.Core/Services/ICurrentPlayer.cs
@src/GameKit.Core/Services/PlayerDisplayNameResolver.cs
@GameKit.sln

<interfaces>
<!-- Extracted from the codebase so the executor does not have to explore. -->

From src/GameKit.Core/Entities/Player.cs:
```csharp
public sealed class Player {
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }
    public JsonDocument? Metadata { get; set; }
}
```

From src/GameKit.Core/Entities/GameSession.cs:
```csharp
public sealed class GameSession {
    public Guid Id { get; set; }
    public GameSessionState State { get; set; } = GameSessionState.Pending;
    public Guid? LadderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public JsonDocument? Metadata { get; set; }           // <-- board state lives here
    public void Start(DateTimeOffset now);                 // Pending -> Active
    public void Complete(DateTimeOffset now);              // Active  -> Completed
    public void Cancel(DateTimeOffset now);
    public void Abandon(DateTimeOffset now);
}
public enum GameSessionState { Pending=0, Active=1, Completed=2, Cancelled=3, Abandoned=4 }
```

From src/GameKit.Core/Entities/SessionParticipant.cs:
```csharp
public sealed class SessionParticipant {
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? PlayerId { get; set; }         // nullable post-GDPR
    public int Team { get; set; }                // 0 = X, 1 = O for this demo
    public SessionResult? Result { get; set; }   // Win/Loss/Draw (enum lives in Entities)
    public int? Score { get; set; }
    public double? RatingBefore { get; set; }
    public double? RatingAfter { get; set; }
    public double? RatingDelta { get; set; }
}
```

From src/GameKit.Core/Data/GameKitDbContext.cs:
```csharp
public sealed class GameKitDbContext : DbContext {
    public DbSet<Player> Players { get; }
    public DbSet<GameSession> GameSessions { get; }
    public DbSet<SessionParticipant> SessionParticipants { get; }
    public DbSet<AdminAuditLog> AdminAuditLog { get; }
}
```

From src/GameKit.Core/Services/IClock.cs / IIdGenerator.cs (registered in AddGameKit):
```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IIdGenerator { Guid NewId(); }
```

From src/GameKit.Core/Services/IPlayerDisplayNameResolver.cs:
```csharp
public interface IPlayerDisplayNameResolver {
    ValueTask<string> ResolveAsync(Guid? playerId, CancellationToken ct = default);
}
```

From src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs:
```csharp
IApplicationBuilder UseGameKit(this IApplicationBuilder app);  // runs migrations under advisory lock + UseAuthorization
IEndpointRouteBuilder MapGameKit(this IEndpointRouteBuilder routes);  // maps GET /api/players (RequireAuthorization — 401 in Phase 1)
```

Phase-1 caveat: `/api/players` has `.RequireAuthorization()` and there is no authentication handler, so it returns 401. The `/demo/*` endpoints deliberately do NOT call `RequireAuthorization` — that is the whole point of the TEMPORARY marker.
</interfaces>

Phase-1 persistence quirk (from STATE.md): `GameKitDbContext` uses `ValueGeneratedNever` for Ids, so every insert MUST call `IIdGenerator.NewId()` before `Add`. `CreatedAt` must be set from `IClock.UtcNow`.

Threat model note: the `/demo/players/register` endpoint is INTENTIONALLY unauthenticated for a Phase-1 local-demo only. The handler comment and the README must both say so explicitly so the pattern is not copied into production code. No production auth deferral to future phase is created here — Phase 2 scope already owns authenticated player registration (GameKit.Auth).

</context>

<tasks>

<task type="auto">
  <name>Task 1: Rename SampleGame -> TicTacToeDuel (filesystem + csproj + solution)</name>
  <files>
    samples/SampleGame/ -> samples/TicTacToeDuel/ (directory move)
    samples/TicTacToeDuel/TicTacToeDuel.csproj (renamed from SampleGame.csproj, contents updated)
    GameKit.sln (project reference updated)
  </files>
  <action>
Perform the rename end-to-end so the solution builds afterwards:

1. `git mv samples/SampleGame samples/TicTacToeDuel` (preserves history; note: bin/obj may need to be ignored/excluded — they already are via root .gitignore).
2. `git mv samples/TicTacToeDuel/SampleGame.csproj samples/TicTacToeDuel/TicTacToeDuel.csproj`.
3. Edit `samples/TicTacToeDuel/TicTacToeDuel.csproj` to update:
   - `<RootNamespace>SampleGame</RootNamespace>` -> `<RootNamespace>TicTacToeDuel</RootNamespace>`
   - `<AssemblyName>SampleGame</AssemblyName>` -> `<AssemblyName>TicTacToeDuel</AssemblyName>`
   - Keep `<IsPackable>false</IsPackable>` and the ProjectReference to `..\..\src\GameKit.Core\GameKit.Core.csproj`.
   - Do NOT add any new `<PackageReference>`. The sample compiles against only what GameKit.Core transitively provides (EF Core, ASP.NET Core shared framework, System.Text.Json).
4. Edit `GameKit.sln` to update the SampleGame project entry (the one with GUID `{50625367-18F0-4D1B-8FD7-3E6C9812F7CF}`):
   - Change the project's display name from `"SampleGame"` to `"TicTacToeDuel"`.
   - Change the relative path from `"samples\SampleGame\SampleGame.csproj"` to `"samples\TicTacToeDuel\TicTacToeDuel.csproj"`.
   - Preserve the ProjectGuid (`{50625367-18F0-4D1B-8FD7-3E6C9812F7CF}`) and its `ProjectConfigurationPlatforms` / `NestedProjects` entries unchanged — this keeps IDE state intact.
5. Do not touch `appsettings*.json`; their connection strings are reused as-is.
6. Delete the existing `Program.cs` content — Task 2 rewrites it. Leave the file in place so git diffs are coherent; write a `// placeholder — replaced in Task 2` comment plus an empty `return;`-style no-op if needed, OR just let Task 2 overwrite it in full.

Afterwards run `dotnet build GameKit.sln` and it must succeed. If the build fails because of stale `bin/obj` in the renamed directory, `rm -rf samples/TicTacToeDuel/bin samples/TicTacToeDuel/obj` and rebuild.
  </action>
  <verify>
    <automated>test -d samples/TicTacToeDuel && test -f samples/TicTacToeDuel/TicTacToeDuel.csproj && ! test -e samples/SampleGame && grep -q 'TicTacToeDuel' GameKit.sln && ! grep -q 'SampleGame' GameKit.sln && dotnet build GameKit.sln -c Debug --nologo -v quiet</automated>
  </verify>
  <done>
    - Directory `samples/SampleGame` no longer exists; `samples/TicTacToeDuel/TicTacToeDuel.csproj` does.
    - `GameKit.sln` has no remaining `SampleGame` string and builds clean.
    - No source code under `src/` or `tests/` changed.
  </done>
</task>

<task type="auto">
  <name>Task 2: Build the tic-tac-toe domain model + demo API endpoints</name>
  <files>
    samples/TicTacToeDuel/Game/TicTacToeBoard.cs
    samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs
    samples/TicTacToeDuel/Http/DemoContracts.cs
    samples/TicTacToeDuel/Http/DemoEndpoints.cs
    samples/TicTacToeDuel/Program.cs
  </files>
  <action>
Implement the game logic and demo endpoints entirely inside the sample project. GameKit.Core is game-agnostic and MUST NOT be modified.

**Every new file must start with the repo-standard SPDX header:**
```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### 2a. `Game/TicTacToeBoard.cs` (pure domain, no I/O, no EF, no ASP.NET)

Namespace: `TicTacToeDuel.Game`.

- `enum Mark { None = 0, X = 1, O = 2 }`
- `enum BoardOutcome { InProgress, XWins, OWins, Draw }`
- `public sealed class TicTacToeBoard`:
  - `Mark[,] Cells { get; }` — 3x3, fills with `Mark.None` by default.
  - `int MoveCount { get; private set; }`
  - `Mark WhoseTurn => (MoveCount % 2 == 0) ? Mark.X : Mark.O` (X always moves first).
  - `BoardOutcome Outcome { get; private set; } = BoardOutcome.InProgress` — recomputed after each `ApplyMove`.
  - `static TicTacToeBoard NewEmpty()`.
  - `ApplyMove(int row, int col, Mark mark)` — validation:
    - throw `ArgumentOutOfRangeException` if row/col not in 0..2.
    - throw `InvalidOperationException("cell occupied")` if `Cells[row,col] != None`.
    - throw `InvalidOperationException("not your turn")` if `mark != WhoseTurn`.
    - throw `InvalidOperationException("game over")` if `Outcome != InProgress`.
    - otherwise assign, bump MoveCount, recompute Outcome.
  - Outcome detection: check 3 rows, 3 cols, 2 diagonals for a filled triplet of the same non-None mark; otherwise if `MoveCount == 9` -> Draw; otherwise InProgress.
- `Mark` <-> `Team` mapping convention (used by callers): Team 0 = X, Team 1 = O.

Comment-document both public types with XML doc comments — this is a GPL-library-adjacent sample, keep the convention.

### 2b. `Game/TicTacToeBoardSerializer.cs`

Namespace: `TicTacToeDuel.Game`. Pure `static class`.

- `JsonDocument ToJsonDocument(TicTacToeBoard board)` — serializes to:
  ```json
  { "v": 1, "cells": [[0,0,0],[0,0,0],[0,0,0]], "moveCount": 0, "outcome": "InProgress" }
  ```
  where cells are `int` (0/1/2 from the Mark enum) for compact on-disk form.
- `TicTacToeBoard FromJsonDocument(JsonDocument doc)` — rehydrates. Throws `InvalidDataException` on shape mismatch.
- Use `System.Text.Json.JsonSerializer` + `JsonSerializer.SerializeToDocument` (available in net10). Do NOT add new NuGet deps.

### 2c. `Http/DemoContracts.cs`

Namespace: `TicTacToeDuel.Http`. Record DTOs:

- `record RegisterPlayerRequest(string DisplayName);`
- `record RegisterPlayerResponse(Guid Id, string DisplayName);`
- `record CreateGameRequest(Guid PlayerXId, Guid PlayerOId);`
- `record MoveRequest(Guid PlayerId, int Row, int Col);`
- `record ParticipantView(Guid? PlayerId, int Team, string DisplayName, string? Result);`
- `record GameStateResponse(Guid Id, string State, int[][] Cells, string WhoseTurn, string Outcome, ParticipantView[] Participants);`

### 2d. `Http/DemoEndpoints.cs`

Namespace: `TicTacToeDuel.Http`. `public static class DemoEndpoints` with `MapDemo(this IEndpointRouteBuilder routes)` extension. Route group `/demo`, tagged "TicTacToeDuel.Demo".

Endpoints (all minimal APIs, JSON bodies, no RequireAuthorization — deliberately anonymous for the demo):

**POST `/demo/players/register`** — TEMPORARY (comment it loudly):
```
// TEMPORARY DEMO ENDPOINT — will be replaced by GameKit.Auth in Phase 2.
// Inserts a Player row directly. No password, no OAuth, no rate-limiting.
// DO NOT copy this pattern into production code.
```
Handler (pseudocode-shaped — write the real C#):
  - Validate `DisplayName` non-empty, length 1..50. 400 on failure.
  - `var id = ids.NewId();`
  - `db.Players.Add(new Player { Id = id, DisplayName = req.DisplayName, CreatedAt = clock.UtcNow });`
  - `await db.SaveChangesAsync(ct);`
  - Return `Results.Ok(new RegisterPlayerResponse(id, req.DisplayName))`.
  - On unique-violation / db failure surface `Results.Problem(...)` with 500.

**POST `/demo/games`** — create a session.
  - Validate: `PlayerXId != PlayerOId`. Both players must exist (`db.Players.AnyAsync(p => p.Id == id)`). 400 / 404 otherwise.
  - Construct `GameSession { Id = ids.NewId(), CreatedAt = clock.UtcNow }`.
  - Call `session.Start(clock.UtcNow)` so state becomes Active and StartedAt is set.
  - Serialize an empty board into `session.Metadata` via `TicTacToeBoardSerializer.ToJsonDocument(TicTacToeBoard.NewEmpty())`.
  - Create two `SessionParticipant` rows: (Team=0, PlayerId=PlayerXId) and (Team=1, PlayerId=PlayerOId), each with `Id = ids.NewId()`, `SessionId = session.Id`.
  - Add + SaveChanges.
  - Return 201 with `GameStateResponse` (see GET handler for shape; reuse a shared mapper).

**POST `/demo/games/{id:guid}/moves`**
  - Load session with `db.GameSessions.FindAsync(id, ct)`. 404 if missing.
  - If `session.State != GameSessionState.Active` -> 400 "game not active".
  - Load its two `SessionParticipant` rows. Determine which Team (0/1) the submitted `PlayerId` belongs to. If PlayerId is not a participant -> 400 "not a participant".
  - Translate Team -> Mark (0 -> X, 1 -> O).
  - Deserialize `session.Metadata` via `TicTacToeBoardSerializer.FromJsonDocument`. If `Metadata` is null -> 500 "board missing".
  - Call `board.ApplyMove(row, col, mark)`. Catch `InvalidOperationException` / `ArgumentOutOfRangeException` -> 400 with the exception message (bounded — these are attacker-irrelevant messages from our own code).
  - Reserialize `board` back into `session.Metadata`.
  - If `board.Outcome` is `XWins` or `OWins` or `Draw`:
    - Set each participant's `Result` on the tracked entities (Team 0 = X, Team 1 = O; Win/Loss/Draw accordingly).
    - Call `session.Complete(clock.UtcNow)`.
  - SaveChanges.
  - Return the updated `GameStateResponse` (200).

**GET `/demo/games/{id:guid}`**
  - Load session + participants. 404 on miss.
  - Resolve each participant's display name via `IPlayerDisplayNameResolver.ResolveAsync(participant.PlayerId, ct)`.
  - Deserialize board; build `int[3][]` of cells for the response.
  - Return `GameStateResponse` with `State`, `WhoseTurn`, `Outcome`, and participant views.

Shared private helper `BuildResponseAsync(GameSession, SessionParticipant[], IPlayerDisplayNameResolver, CancellationToken)` avoids duplication across POST-games / POST-moves / GET-game handlers.

Use constructor injection / handler-parameter injection for:
- `GameKitDbContext db`
- `IClock clock`
- `IIdGenerator ids`
- `IPlayerDisplayNameResolver names`
- `CancellationToken ct`

Error shape: use `Results.BadRequest(new { error = "..." })` consistently; use `Results.NotFound(new { error = "..." })` for 404s. No exception leakage beyond the handler body.

### 2e. Rewrite `samples/TicTacToeDuel/Program.cs`

Required pipeline (order matters — static files BEFORE `UseGameKit` so `/` resolves to `wwwroot/index.html`):

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Builder;
using TicTacToeDuel.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});

var app = builder.Build();

app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
app.UseStaticFiles();    // serves everything under wwwroot/

app.UseGameKit();
app.MapGameKit();        // preserves /api/players (Phase 1 auth-gated)
app.MapDemo();           // adds /demo/* (anonymous — demo only)

app.Run();
```

If `IHttpContextAccessor` or minimal-API inference complains about missing `using` directives, add them (`using Microsoft.AspNetCore.Http;` etc.) — no new NuGet refs.
  </action>
  <verify>
    <automated>dotnet build GameKit.sln -c Debug --nologo -v quiet 2>&1 | tee /tmp/gk-build.log && grep -q 'Build succeeded' /tmp/gk-build.log && grep -l 'SPDX-License-Identifier: GPL-3.0-or-later' samples/TicTacToeDuel/Program.cs samples/TicTacToeDuel/Game/TicTacToeBoard.cs samples/TicTacToeDuel/Game/TicTacToeBoardSerializer.cs samples/TicTacToeDuel/Http/DemoContracts.cs samples/TicTacToeDuel/Http/DemoEndpoints.cs && grep -q 'TEMPORARY' samples/TicTacToeDuel/Http/DemoEndpoints.cs && ! grep -rq 'PackageReference' samples/TicTacToeDuel/TicTacToeDuel.csproj</automated>
  </verify>
  <done>
    - `dotnet build` is green.
    - All 5 new .cs files exist, compile, and carry the GPL SPDX header.
    - `DemoEndpoints.cs` contains the "TEMPORARY" comment above `/demo/players/register`.
    - The board enforces: cell-occupied, not-your-turn, out-of-bounds, game-over.
    - On terminal outcome, the session transitions to Completed via `GameSession.Complete(clock.UtcNow)` and each participant's `Result` is set.
    - `Program.cs` calls `UseDefaultFiles`/`UseStaticFiles` before `UseGameKit`, and `MapDemo` after `MapGameKit`.
    - No new PackageReference was added to the csproj.
    - No file under `src/` or `tests/` was modified.
  </done>
</task>

<task type="auto">
  <name>Task 3: Minimal HTML client + README, and smoke-test it end-to-end</name>
  <files>
    samples/TicTacToeDuel/wwwroot/index.html
    samples/TicTacToeDuel/README.md
  </files>
  <action>
### 3a. `samples/TicTacToeDuel/wwwroot/index.html`

Single static page. No build step. No frameworks. Plain `<script>` block with vanilla JS.

First line: `<!-- SPDX-License-Identifier: GPL-3.0-or-later -->` (HTML-comment style so the license-check CI treats the file consistently; follow whatever pattern tests/license checks already apply to `.html` files in the repo — if the existing license-check script only covers .cs files, this comment is harmless).

Layout:
- Title: "Tic-Tac-Toe Duel — GameKit Phase 1 Demo".
- Small banner that reads "DEV DEMO — /demo/players/register is temporary; Phase 2 replaces it with GameKit.Auth."
- A "Players" section with two rows, each containing:
  - A text input for display name (placeholder "Player X" / "Player O").
  - A "Register" button that POSTs to `/demo/players/register`, stores the returned id in a hidden span, and swaps the button label to "Registered: <id>".
- A "Start game" button, disabled until both players are registered. On click: POST `/demo/games` with both ids; on success, render the game.
- A 3x3 grid of `<button class="cell">` (inline CSS: 80px x 80px, font-size 2em). Each cell has `data-row` / `data-col`. Clicks call `POST /demo/games/{id}/moves` with the active player (determined client-side from whoseTurn returned by the server). After each response, re-render the grid from `response.Cells` and update a status line.
- Status line shows: whose turn (by display name), or the outcome ("Alice wins!" / "Draw.") once terminal.
- "New game" button appears once outcome is terminal — posts another `/demo/games` with the same two player ids.

JS patterns:
- `async function registerPlayer(n)`, `async function startGame()`, `async function makeMove(r, c)`, `function render(state)`.
- On fetch error, show a red status line with the error body (server returns `{error: "..."}`). Do not `alert()`.
- Keep it short — aim for well under 250 lines total including inline CSS + JS.

### 3b. `samples/TicTacToeDuel/README.md`

Sections:

- Header: `# Tic-Tac-Toe Duel — GameKit Phase 1 Sample`
- One-paragraph purpose (executable demo of Phase 1; not a tutorial on building a game).
- **Prerequisites:** .NET 10 SDK, Docker (for Postgres + Redis via the repo's `docker-compose.yml`).
- **Run:**
  ```
  docker compose up -d
  dotnet run --project samples/TicTacToeDuel
  # then open http://localhost:5000
  ```
- **What it demonstrates:** registering Players, creating GameSession + SessionParticipant rows, mutating a JSONB-backed board via GameSession.Metadata, driving a session through the Pending -> Active -> Completed lifecycle, resolving display names via IPlayerDisplayNameResolver (so deleted players render as "Deleted Player").
- **Explicitly NOT a Phase-1 concern:** authentication. `POST /demo/players/register` is deliberately unauthenticated and will be removed/replaced by `GameKit.Auth` in Phase 2. Do not ship this pattern.
- **Endpoints used:**
  - `POST /demo/players/register` `{displayName}` -> `{id, displayName}`
  - `POST /demo/games` `{playerXId, playerOId}` -> full game state
  - `POST /demo/games/{id}/moves` `{playerId, row, col}` -> updated state
  - `GET  /demo/games/{id}` -> current state
- **Troubleshooting:** port clash with Postgres (change `ConnectionStrings:GameKit`), initial migrations running at first startup (normal), `401` on `/api/players` (expected — auth gate for Phase 2).
- Ends with a one-line license notice: "GPL-3.0-or-later — see repo root LICENSE."

### 3c. Smoke test the whole stack (manual via the verify command)

No test project is added (samples don't ship tests). The verification command below spins the app up in-background with the docker-compose stack already running, hits the three endpoints in sequence with curl, asserts the expected shape, then shuts the app down.

Important: this smoke test assumes `docker compose up -d` has already been run (the user does this per the README). If Postgres is not reachable, skip the smoke test and report that as a partial — the build-time verify (Task 2) is authoritative for acceptance.
  </action>
  <verify>
    <automated>test -f samples/TicTacToeDuel/wwwroot/index.html && test -f samples/TicTacToeDuel/README.md && grep -q 'Tic-Tac-Toe Duel' samples/TicTacToeDuel/README.md && grep -q 'docker compose up -d' samples/TicTacToeDuel/README.md && grep -q 'http://localhost:5000' samples/TicTacToeDuel/README.md && grep -qi 'temporary' samples/TicTacToeDuel/README.md && grep -q 'fetch' samples/TicTacToeDuel/wwwroot/index.html && grep -q '/demo/' samples/TicTacToeDuel/wwwroot/index.html && dotnet build GameKit.sln -c Debug --nologo -v quiet</automated>
  </verify>
  <done>
    - `wwwroot/index.html` exists with inline CSS + JS, uses only `fetch()` against `/demo/*`, has no `<script src="http...">` (no CDN — fully offline-capable demo).
    - `README.md` exists with the run instructions, temporary-endpoint warning, endpoint list, and license line.
    - Full solution still builds clean.
    - A manual smoke run (docker compose up -d; dotnet run --project samples/TicTacToeDuel; open http://localhost:5000) lets a human register two players and play a full game to a terminal outcome (Win or Draw).
  </done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Browser -> `/demo/*` | Untrusted JSON crosses into an **intentionally anonymous** endpoint surface. All inputs are re-validated on the server (display name length, participant-belongs-to-session, board coordinates, legal-move check). |
| `/demo/*` handlers -> GameKitDbContext | Crosses into the same EF surface GameKit.Core already validates. We rely on the entity invariants (GameSession state machine throws on illegal transitions; participant FKs are enforced by the Core migration). |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-ttt-01 | Spoofing | `POST /demo/players/register` (anyone can create a player, no auth) | **accept (demo-only)** | Endpoint is documented as TEMPORARY in the handler comment AND README. Phase 2 (GameKit.Auth) replaces it with authenticated registration. Sample lives outside the shipped NuGet surface — consumers never import it. |
| T-ttt-02 | Tampering | `POST /demo/games/{id}/moves` with a non-participant `playerId` | mitigate | Handler loads `SessionParticipant` rows and verifies submitted `playerId` matches one of them before translating to a Mark. Non-match -> 400. |
| T-ttt-03 | Tampering | Illegal board moves (out-of-bounds, occupied cell, wrong turn, after game-over) | mitigate | `TicTacToeBoard.ApplyMove` throws; handler catches and returns 400 with a short message. The board is re-derived server-side from the DB on every move (no client-provided board state is trusted). |
| T-ttt-04 | Repudiation | Demo writes player + session rows with no actor id | accept | AdminAuditLog exists only for admin-initiated actions (GDPR delete etc.). Gameplay actions are not audited in Phase 1. Documented. |
| T-ttt-05 | Information Disclosure | `/demo/games/{id}` reveals both players' display names and ids to anyone who knows the session id | accept | Session ids are UUIDv7 (unguessable ~ UUIDv4 entropy for the random half). Demo UX intentionally exposes opponent names — that is what a game backend does. No PII beyond display name. |
| T-ttt-06 | Denial of Service | Unbounded registrations / game creations via the anonymous endpoints | accept (demo-only) | Phase 1 already registers `GameKitRateLimitPolicies` via `AddGameKit`. We do not attach it to `/demo/*` on purpose — rate limiting is Phase 2's concern on the real endpoints. The sample is for local dev. |
| T-ttt-07 | Elevation of Privilege | Static files served from `wwwroot` escaping the directory | mitigate | Standard `UseStaticFiles` only serves `wwwroot/`. No user-supplied paths are ever used to select files. |
| T-ttt-08 | Information Disclosure | Exception messages leaked to the browser from a deep EF failure (e.g. FK violation) | mitigate | All handlers return `Results.Problem` / `Results.BadRequest` with controlled messages. Dev environment's `UseDeveloperExceptionPage` is ASP.NET default and is acceptable for a local demo. Documented as local-only. |

Phase-1 residual acceptance (T-ttt-01, T-ttt-04, T-ttt-05, T-ttt-06) is explicit: the sample is a local-only demo of Phase 1 internals, not a shipped service surface.
</threat_model>

<verification>
After all 3 tasks:

- `dotnet build GameKit.sln` succeeds without warnings beyond the ones already present on master.
- No `SampleGame` string remains anywhere in the repo (`grep -rn SampleGame` returns empty outside `.planning/` history).
- The sample's csproj contains zero `<PackageReference>` entries (still only the `<ProjectReference>` to GameKit.Core).
- Manual end-to-end run:
  1. `docker compose up -d`
  2. `dotnet run --project samples/TicTacToeDuel`
  3. Open `http://localhost:5000`.
  4. Register two players.
  5. Click "Start game".
  6. Play until X wins / O wins / Draw.
  7. Status line shows the correct outcome and the "New game" button appears.
- After step 6, confirm via psql: `SELECT id, state FROM gamekit.game_sessions ORDER BY created_at DESC LIMIT 1;` returns `state = 'Completed'`.
- `/api/players` still returns 401 (Phase 1 auth gate remains intact — sample did not accidentally disable it).
</verification>

<success_criteria>
- `samples/TicTacToeDuel/` exists; `samples/SampleGame/` does not.
- A human can play a full game of tic-tac-toe through the browser at `http://localhost:5000`, backed by real Postgres persistence via GameKit.Core.
- Board state lives in `GameSession.Metadata` as JSON; illegal moves are rejected server-side; terminal state triggers `GameSession.Complete` and records Win/Loss/Draw per participant.
- Nothing under `src/` or `tests/` was touched; no NuGet dep was added.
- Every new file carries the GPL SPDX header, and the TEMPORARY register-endpoint warning is present both in code and in the README.
</success_criteria>

<output>
After completion, create `.planning/quick/260416-tlm-build-tic-tac-toe-duel-sample-app-demons/260416-tlm-SUMMARY.md` summarizing files added, files removed, the endpoint surface, how to run, and the Phase-2 follow-ups (replace `/demo/players/register` with `GameKit.Auth`; attach rate limiting; add `/api/sessions` as the proper non-demo surface).
</output>
