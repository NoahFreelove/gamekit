---
phase: quick-260607-bri
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs
  - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
  - tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs
  - src/GameKit.Auth/Services/AccountMergeService.cs
  - tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs
  - .planning/milestones/v2.0-MILESTONE-AUDIT.md
autonomous: true
requirements: [LOBBY-06, AUTH-23]

must_haves:
  truths:
    - "A consumer who calls AddLobby() without registering IConnectionMultiplexer gets a clear, actionable error naming the missing service and how to fix it (not a cryptic 'No service for type ...' message)."
    - "AccountMergeService.MergeAsync re-points the source player's lobby_members rows onto the target inside the SERIALIZABLE transaction."
    - "When source and target are both members of the SAME lobby, the merge deletes the source's duplicate lobby_members row (dedup) instead of violating UNIQUE(LobbyId, PlayerId) — the merge succeeds and the target keeps exactly one membership row."
    - "GameKit.Auth still has NO ProjectReference to GameKit.Lobby (lobby_members is mutated via raw parameterized SQL through the shared GameKitDbContext)."
  artifacts:
    - path: "src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs"
      provides: "Fail-fast InvalidOperationException with actionable Redis-registration guidance"
      contains: "GetService<IConnectionMultiplexer>"
    - path: "src/GameKit.Auth/Services/AccountMergeService.cs"
      provides: "lobby_members dedup + re-point step in MergeTransactionBodyAsync"
      contains: "gamekit.lobby_members"
  key_links:
    - from: "src/GameKit.Auth/Services/AccountMergeService.cs"
      to: "gamekit.lobby_members"
      via: "Database.ExecuteSqlAsync raw SQL (DELETE dup, then UPDATE re-point)"
      pattern: "lobby_members"
    - from: "src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs"
      to: "consumer DI"
      via: "GetService + null-check throwing InvalidOperationException"
      pattern: "GetService<IConnectionMultiplexer>"
---

<objective>
Close two v2.0 backlog tech-debt items from `.planning/milestones/v2.0-MILESTONE-AUDIT.md` as two atomic code commits plus a doc-only commit.

- **W-1 (GameKit.Lobby):** `AddLobby()` mandates a Redis backplane (LOBBY-06) but a consumer who forgets to register `IConnectionMultiplexer` gets a cryptic "No service for type 'StackExchange.Redis.IConnectionMultiplexer' has been registered." at startup. Replace `GetRequiredService` with `GetService` + a clear, actionable `InvalidOperationException`, and document the Redis requirement in the public `AddLobby()` XML docs.
- **W-2 (GameKit.Auth account merge):** `AccountMergeService.MergeTransactionBodyAsync` re-points 11 FK targets but not `gamekit.lobby_members.PlayerId` (Lobby shipped in Phase 11, after account-merge in Phase 10). Add a `lobby_members` re-point step using raw parameterized SQL (exactly like the existing party_members Step 11) with same-lobby dedup to respect UNIQUE(LobbyId, PlayerId).

Purpose: Eliminate the documented backlog gaps so v2.0 ships with a clean tech-debt list. W-2 in particular is a correctness gap — a merged source player would leave stale `lobby_members` rows pointing at a tombstoned player.

Output: Two surgical code changes (W-1 message, W-2 SQL step), integration test coverage for both, and the MILESTONE-AUDIT frontmatter updated to mark W-1/W-2 resolved.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
</execution_context>

<context>
@./CLAUDE.md
@.planning/STATE.md
@.planning/milestones/v2.0-MILESTONE-AUDIT.md

<interfaces>
<!-- Verified from the codebase during planning. Use these directly — no exploration needed. -->

lobby_members schema (gamekit.lobby_members — created by the GameKit.Lobby migration):
  Columns (quoted, Postgres case-sensitive): "Id" (uuid PK), "LobbyId" (uuid), "PlayerId" (uuid),
  "Ready" (bool), "JoinedAt" (timestamptz). UNIQUE INDEX on ("LobbyId", "PlayerId").
  FKs: "LobbyId" → gamekit.lobbies("Id") ON DELETE CASCADE; "PlayerId" → gamekit.players("Id") ON DELETE CASCADE.

lobbies schema (gamekit.lobbies) — relevant columns observed in BackplaneTests.SeedSharedLobbyAsync:
  "Id" (uuid PK), "OwnerId" (uuid), "LadderId" (uuid), "State" (int), "MaxMembers" (int),
  "CreatedAt" (timestamptz), "UpdatedAt" (timestamptz).

AccountMergeService (src/GameKit.Auth/Services/AccountMergeService.cs):
  - All cross-package table mutations use `await _ctx.Database.ExecuteSqlAsync($"""...""", ct)` with
    interpolated parameters (e.g. {sourcePlayerId}). EF turns interpolated values into bound parameters.
  - Step 11 (party_members) is the precedent for "re-point a Matchmaking-owned table via raw SQL".
  - Step 6 (player_credentials) is the precedent for "dedup before re-point" (delete source row when
    target already occupies the unique slot, else re-point).
  - The class-level <remarks> currently lists the cross-package tables as:
    "(player_ranks, pending_rating_updates, season_rank_archive, party_members, parties, decline_history)"
    in TWO places (the <para> at ~line 45 and the transaction-body comment block at ~line 257).

LobbyRedisBackplanePostConfigure (src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs):
  - internal sealed; PostConfigure(string? name, RedisOptions options) currently does
    `var mux = _sp.GetRequiredService<IConnectionMultiplexer>();` then sets options.ConnectionFactory.

LobbyBuilderExtensions.AddLobby (src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs):
  - public static IGameKitBuilder AddLobby(this IGameKitBuilder builder, Action<GameKitLobbyOptions>? configure = null)
  - Has a <summary> bullet list (item describing the SignalR Redis backplane already mentions
    LobbyRedisBackplanePostConfigure / LOBBY-06).
</interfaces>

<test_seeding_pattern>
<!-- CRITICAL for W-2 tests. The AccountMerge test project does NOT reference GameKit.Lobby and does
     NOT apply the Lobby migration, so gamekit.lobby_members does NOT exist in that test DB by default. -->

Existing precedent (tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs):
  - `SeedSamePartyAsync` seeds gamekit.parties + gamekit.party_members via raw `ctx.Database.ExecuteSqlAsync`
    INSERTs — NO entity dependency on Matchmaking. BUT the party_members TABLE exists because
    TestHelpers.ApplyMigrations applies the Matchmaking migration.
  - lobby_members has no such migration in this test project. So the test MUST create the
    gamekit.lobbies + gamekit.lobby_members tables itself via raw DDL before seeding rows.

Required test helper shape (raw DDL, mirrors the production schema documented in <interfaces>):
  CREATE TABLE IF NOT EXISTS gamekit.lobbies (
    "Id" uuid PRIMARY KEY, "OwnerId" uuid NOT NULL, "LadderId" uuid NULL,
    "State" int NOT NULL DEFAULT 0, "MaxMembers" int NOT NULL DEFAULT 8,
    "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL);
  CREATE TABLE IF NOT EXISTS gamekit.lobby_members (
    "Id" uuid PRIMARY KEY,
    "LobbyId" uuid NOT NULL REFERENCES gamekit.lobbies("Id") ON DELETE CASCADE,
    "PlayerId" uuid NOT NULL REFERENCES gamekit.players("Id") ON DELETE CASCADE,
    "Ready" boolean NOT NULL DEFAULT false, "JoinedAt" timestamptz NOT NULL);
  CREATE UNIQUE INDEX IF NOT EXISTS "IX_lobby_members_LobbyId_PlayerId"
    ON gamekit.lobby_members ("LobbyId", "PlayerId");

  Notes:
  - Use IF NOT EXISTS everywhere — TestHelpers.ApplyMigrations and these DDL calls may run across
    multiple tests against the shared PostgresFixture database.
  - The "PlayerId" FK to gamekit.players matters: the merge re-points only AFTER players are tombstoned
    (soft-delete, DeletedAt set; the row still exists), so the FK stays satisfied. Seed real player rows
    via the existing SeedTwoPlayersAsync helper, then seed a lobby + lobby_members rows referencing them.
  - The merge service's W-2 raw SQL references column NAMES only ("PlayerId", "LobbyId"); it does NOT
    require the LobbyMember entity to be in the EF model. So MergeTestRuntimeQueryCustomizer needs NO change.

DO NOT add a ProjectReference from the AccountMerge test project to GameKit.Lobby. Build the tables via raw DDL.
</test_seeding_pattern>
</context>

<tasks>

<task type="auto">
  <name>Task 1 (commit 1 — W-1): Fail-fast clear error when AddLobby() has no IConnectionMultiplexer</name>
  <files>
    src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs
    src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
    tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs
  </files>
  <action>
In `LobbyRedisBackplanePostConfigure.PostConfigure`, replace `_sp.GetRequiredService<IConnectionMultiplexer>()` with `_sp.GetService<IConnectionMultiplexer>()`. When the result is null, throw an `InvalidOperationException` with an actionable message that: (a) states GameKit.Lobby requires a registered `IConnectionMultiplexer` because LOBBY-06 mandates the SignalR Redis backplane (Azure SignalR is not supported), and (b) tells the consumer to register one BEFORE calling AddLobby(), naming the concrete pattern `services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("<your-redis-connection-string>"))`. Keep the change surgical — only the resolution + null-guard; the `options.ConnectionFactory = _ => Task.FromResult(mux);` assignment stays unchanged once `mux` is confirmed non-null. Update the class `<remarks>` (the `<para>` about IConnectionMultiplexer being consumer-provided) to note that a missing multiplexer now fails fast at startup with a clear message.

In `LobbyBuilderExtensions.AddLobby`, update the public `<summary>` XML doc: add an `<item>` (or extend the existing SignalR/backplane `<item>`) stating that AddLobby() REQUIRES a consumer-registered `IConnectionMultiplexer` (LOBBY-06) and that a missing registration fails fast at startup with a clear, actionable message. Per the CLAUDE.md public-API rule (CS1591, -warnaserror) keep all public XML docs complete and well-formed. Do not change the AddLobby() registration body — the message lives in the post-configurator.

In `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` (or a small new test class file in the same project if you prefer cleaner separation — `RedisRequirementTests.cs`), add a fast no-Docker test: construct a `LobbyRedisBackplanePostConfigure` against a `ServiceProvider` that has NO `IConnectionMultiplexer` registered (a minimal `new ServiceCollection().BuildServiceProvider()` is sufficient since the post-configurator only resolves IConnectionMultiplexer), call `PostConfigure(null, new RedisOptions())`, assert it throws `InvalidOperationException`, and assert the message contains the actionable substrings (e.g. "IConnectionMultiplexer" and "AddLobby"). This is a service-collection/unit-shaped test — it does NOT need Testcontainers. If you add a new test file, do not introduce a new xUnit collection; keep it free of fixture dependencies so it runs standalone.
  </action>
  <verify>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet build src/GameKit.Lobby/GameKit.Lobby.csproj -warnaserror 2>&1 | tail -5</automated>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet test tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj --filter "FullyQualifiedName~Redis|DisplayName~Redis" 2>&1 | tail -15</automated>
  </verify>
  <done>
GameKit.Lobby builds clean under -warnaserror (no CS1591). The new no-Redis test passes and asserts an InvalidOperationException whose message names IConnectionMultiplexer and how to register it before AddLobby(). Existing BackplaneTests still pass. Commit as: `fix(lobby): fail fast with clear message when IConnectionMultiplexer is unregistered (W-1)`.
  </done>
</task>

<task type="auto">
  <name>Task 2 (commit 2 — W-2): Re-point lobby_members in AccountMergeService with same-lobby dedup + tests</name>
  <files>
    src/GameKit.Auth/Services/AccountMergeService.cs
    tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs
  </files>
  <action>
In `AccountMergeService.MergeTransactionBodyAsync`, add a new lobby_members re-point step grouped with the other Matchmaking/cross-package raw-SQL re-points (logically adjacent to Step 11 party_members; place it as a new clearly-commented step — its exact step number is your call, e.g. "STEP 11b" or renumber, just keep the comments coherent). The step is raw parameterized SQL via `_ctx.Database.ExecuteSqlAsync($"""...""", ct)` — GameKit.Auth must NOT add a ProjectReference to GameKit.Lobby (would risk a dependency cycle; lobby_members is reached purely by table/column name, exactly like party_members in Step 11).

Two statements, both inside the existing SERIALIZABLE tx, to respect UNIQUE(LobbyId, PlayerId) — mirror the player_credentials dedup precedent (Step 6) and the season_rank_archive conflict passes (Step 10):
  (1) DELETE the source player's lobby_members rows for any lobby where the TARGET is already a member (dedup — prevents the UNIQUE violation when both share a lobby). Shape: DELETE FROM gamekit.lobby_members WHERE "PlayerId" = {sourcePlayerId} AND "LobbyId" IN (SELECT "LobbyId" FROM gamekit.lobby_members WHERE "PlayerId" = {targetPlayerId}).
  (2) UPDATE the source's REMAINING (source-only) lobby_members rows, re-pointing "PlayerId" to {targetPlayerId}. Shape: UPDATE gamekit.lobby_members SET "PlayerId" = {targetPlayerId} WHERE "PlayerId" = {sourcePlayerId}.
Both use interpolated parameters (EF binds them). Add a clear comment block explaining: lobby membership is ephemeral and has no audit purpose (cite LobbyMember.cs remarks), so dedup-then-repoint is the correct resolution — UNLIKE party_members which uses an abort pre-check (Step 3) because parties carry matchmaking implications.

Update the class-level XML `<remarks>` cross-package table list to include lobby_members. There are TWO occurrences of the list "(player_ranks, pending_rating_updates, season_rank_archive, party_members, parties, decline_history)" — the `<para>` near line 45 and the transaction-body comment block near line 257. Add `lobby_members` to BOTH so the documentation stays accurate (CLAUDE.md public-API discipline; the `<remarks>` is on a public-facing documented type).

In `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs`, add a private helper `EnsureLobbyTablesAsync(GameKitDbContext ctx)` that creates `gamekit.lobbies` and `gamekit.lobby_members` via raw DDL with `IF NOT EXISTS` (use the exact schema in the plan's <test_seeding_pattern> block — column names/types/UNIQUE index/FKs must match production). Add a `SeedLobbyMemberAsync(ctx, lobbyId, playerId, ownerId)` helper (raw INSERT into lobbies if needed + lobby_members) mirroring the style of `SeedSamePartyAsync`. Then add two `[Fact]` integration tests in the existing `AccountMergeServiceTests` class (so they reuse `BuildProvider` + `TestHelpers.ApplyMigrations` + the PostgresFixture/RedisFixture):
  (a) "W-2: simple lobby_members re-point — source in lobby A only → target inherits membership": seed two players, create lobby A owned by/containing only the source, run MergeAsync, assert lobby_members has exactly one row for lobby A now pointing at the TARGET and zero rows for the source.
  (b) "W-2: same-lobby dedup — source + target both in lobby B → source row deleted, target's single row remains, no UNIQUE violation": seed two players, create lobby B containing BOTH source and target, run MergeAsync, assert the merge succeeds (no 23505), lobby_members has exactly ONE row for lobby B pointing at the target, and zero rows for the source.
Query lobby_members in assertions via raw SQL (`ctx.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM gamekit.lobby_members WHERE ...")`) — do NOT add a LobbyMember entity or a GameKit.Lobby ProjectReference. Call `EnsureLobbyTablesAsync` after `TestHelpers.ApplyMigrations` and before seeding, in each new test.
  </action>
  <verify>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet build src/GameKit.Auth/GameKit.Auth.csproj -warnaserror 2>&1 | tail -5</automated>
    <automated>cd /home/noah/Desktop/projects/gamekit && grep -rn "GameKit.Lobby" src/GameKit.Auth/GameKit.Auth.csproj && echo "UNEXPECTED LOBBY REF" || echo "OK: no Lobby ProjectReference in GameKit.Auth"</automated>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet test tests/GameKit.Auth.AccountMerge.Integration.Tests/GameKit.Auth.AccountMerge.Integration.Tests.csproj --filter "DisplayName~W-2|DisplayName~lobby" 2>&1 | tail -20</automated>
  </verify>
  <done>
GameKit.Auth builds clean under -warnaserror. The grep confirms GameKit.Auth has NO ProjectReference to GameKit.Lobby. Both new W-2 integration tests pass against real Postgres (Testcontainers): the simple re-point inherits membership on the target, and the same-lobby case dedups the source row with no UNIQUE(LobbyId, PlayerId) violation. The class `<remarks>` lists lobby_members in both occurrences. Commit as: `fix(auth): re-point lobby_members on account merge with same-lobby dedup (W-2)`.
  </done>
</task>

<task type="auto">
  <name>Task 3 (commit 3 — docs): Mark W-1 and W-2 resolved in the v2.0 milestone audit + full affected-suite gate</name>
  <files>
    .planning/milestones/v2.0-MILESTONE-AUDIT.md
  </files>
  <action>
Before editing docs, run the FULL affected-package test suites (project-memory full-suite gate — do not rely on the filtered runs from Tasks 1–2). Run, in order, the complete suites for: GameKit.Lobby.Integration.Tests, GameKit.Auth.AccountMerge.Integration.Tests, and GameKit.Auth.Integration.Tests (merge-adjacent — exercises the same AccountMergeService code paths and Auth migration stack). These need Docker/Testcontainers running. All three must be green before marking the items resolved.

Then update `.planning/milestones/v2.0-MILESTONE-AUDIT.md`:
- In the `tech_debt:` frontmatter, mark the W-1 (phase 11-gamekit-lobby) and W-2 (phase 10-account-merge) items as RESOLVED. Either remove the two items from the active backlog list and add a short resolved-note, or prefix each with "RESOLVED 2026-06-07 (quick 260607-bri): " and keep them for the record — choose the convention that keeps the frontmatter valid YAML. Leave all OTHER tech_debt items (W-3, W-4, nyquist flags, deferred UAT) untouched.
- In the body "Tech Debt" section, update items 1 (W-2) and 2 (W-1) to note they are now resolved (reference quick 260607-bri), or move them to a brief "Resolved" subsection.
- If you flipped both blocking-ish correctness items, leave the milestone `status:` field as-is unless it is now inaccurate; do not invent a new status value.

This is a doc-only change. Do NOT touch any source or test files in this task. The orchestrator may fold this into the final docs commit; if committed separately, use: `docs(v2.0): mark W-1/W-2 backlog items resolved (quick 260607-bri)`.
  </action>
  <verify>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet test tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj 2>&1 | tail -8</automated>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet test tests/GameKit.Auth.AccountMerge.Integration.Tests/GameKit.Auth.AccountMerge.Integration.Tests.csproj 2>&1 | tail -8</automated>
    <automated>cd /home/noah/Desktop/projects/gamekit && dotnet test tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj 2>&1 | tail -8</automated>
    <automated>cd /home/noah/Desktop/projects/gamekit && grep -niE "W-1|W-2" .planning/milestones/v2.0-MILESTONE-AUDIT.md | head</automated>
  </verify>
  <done>
All three full affected-package suites (Lobby integration, AccountMerge integration, Auth integration) pass green. The MILESTONE-AUDIT.md frontmatter and body mark W-1 and W-2 as resolved (referencing quick 260607-bri) with valid YAML and no other tech_debt items disturbed.
  </done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| consumer DI → GameKit.Lobby startup | Consumer-provided service graph; a missing IConnectionMultiplexer must surface as a clear, intentional error rather than a leaked framework exception. |
| account-merge actor → GameKitDbContext (raw SQL) | The merge runs as a superadmin operation; sourcePlayerId/targetPlayerId reach raw SQL — must stay parameterized. |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-bri-01 | Information Disclosure | LobbyRedisBackplanePostConfigure error message | mitigate | New InvalidOperationException states the missing service + remediation only — no secrets, no connection strings echoed. Message is static guidance text. |
| T-bri-02 | Tampering | AccountMergeService lobby_members raw SQL | mitigate | Use `ExecuteSqlAsync($"""...""")` interpolation so sourcePlayerId/targetPlayerId are EF-bound parameters (never string-concatenated). Matches all existing Steps 9–11. No SQL injection surface. |
| T-bri-03 | Denial of Service | same-lobby UNIQUE(LobbyId, PlayerId) violation aborting merge | mitigate | DELETE-then-UPDATE dedup inside the SERIALIZABLE tx guarantees the re-point never hits 23505; the whole step is covered by the same MaxRetries/40001 retry ladder as the rest of the tx body. |
| T-bri-SC | Tampering | npm/pip/cargo installs | accept | No package-manager installs in this plan — only edits to existing source/test files. No new dependencies. |
</threat_model>

<verification>
- `dotnet build src/GameKit.Lobby/GameKit.Lobby.csproj -warnaserror` and `dotnet build src/GameKit.Auth/GameKit.Auth.csproj -warnaserror` both clean (CS1591 enforced — all changed/new public surface stays documented).
- `grep "GameKit.Lobby" src/GameKit.Auth/GameKit.Auth.csproj` returns nothing (no new ProjectReference).
- Full affected-package suites green: GameKit.Lobby.Integration.Tests, GameKit.Auth.AccountMerge.Integration.Tests, GameKit.Auth.Integration.Tests (project-memory full-suite gate; needs Docker/Testcontainers).
- No new EF migrations added (W-2 is runtime SQL against existing tables; W-1 is a code-only message). Confirm no files added under any `*/Migrations/` directory.
</verification>

<success_criteria>
- W-1: A no-Redis consumer of AddLobby() receives an InvalidOperationException naming IConnectionMultiplexer + how to register it before AddLobby(); AddLobby() XML docs state the Redis requirement; new test asserts the message.
- W-2: AccountMergeService re-points source lobby_members onto the target inside the SERIALIZABLE tx, dedups on same-lobby UNIQUE(LobbyId, PlayerId), and does so via raw parameterized SQL with NO GameKit.Lobby ProjectReference; two integration tests prove simple re-point and same-lobby dedup.
- The class-level `<remarks>` cross-package table list includes lobby_members in both occurrences.
- v2.0-MILESTONE-AUDIT.md marks W-1 and W-2 resolved; no other tech_debt items altered.
- Structured as commits: (1) W-1 lobby, (2) W-2 auth+tests, (3) docs (foldable).
</success_criteria>

<output>
Create `.planning/quick/260607-bri-close-w1-w2-backlog-gaps/260607-bri-SUMMARY.md` when done.
</output>
