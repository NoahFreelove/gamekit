---
phase: 16-multi-replica-hardening
plan: "02"
subsystem: GameKit.Core / GameKit.Matchmaking
tags: [idempotency, migration, split-brain, postgres, matchmaking, SCALE-03]
dependency_graph:
  requires: [16-01]
  provides: [SCALE-03-schema, SCALE-03-write-path]
  affects: [GameKit.Core, GameKit.Matchmaking, tests/GameKit.Matchmaking.Integration.Tests]
tech_stack:
  added: []
  patterns:
    - "ON CONFLICT (col) WHERE predicate DO NOTHING — partial unique index idempotent insert"
    - "ExecuteSqlRawAsync with IEnumerable<object> + CancellationToken overload (avoids ct mis-binding)"
    - "NpgsqlParameter array for parameterised raw SQL"
key_files:
  created:
    - src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs
    - src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.Designer.cs
  modified:
    - src/GameKit.Core/Entities/GameSession.cs
    - src/GameKit.Core/Data/Configurations/GameSessionConfiguration.cs
    - src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Matchmaking/Services/ProposalService.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs
decisions:
  - "Used ON CONFLICT (col) WHERE predicate DO NOTHING (Approach A) over try/catch 23505 (Approach B) — cleaner 0-rows-affected observable signal for 16-04 concurrency test"
  - "EF ExecuteSqlRawAsync IEnumerable<object> overload used explicitly to prevent CancellationToken from being treated as a SQL parameter by the params object[] overload"
  - "Partial unique index WHERE IS NOT NULL must be mirrored in ON CONFLICT clause; plain ON CONFLICT (col) fails when the partial index predicate is not satisfied"
  - "ProposalFields.LadderId is non-nullable Guid (not Guid?) — NpgsqlParameter wraps it directly without HasValue check"
metrics:
  duration: "~8 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  files_changed: 8
status: complete
requirements: [SCALE-03]
---

# Phase 16 Plan 02: AddGameSessionIdempotencyKey Summary

Postgres-level secondary guard against split-brain duplicate match creation: `game_sessions.IdempotencyKey` varchar(128) nullable column with a partial unique index (`WHERE IS NOT NULL`), set at match-formation to the proposal id, inserted via `ON CONFLICT DO NOTHING` in `ProposalService.CreateSessionAsync`.

## What Was Built

### Task 1 — GameSession entity + EF configuration (commit bc1d640)

- Added `public string? IdempotencyKey { get; set; }` to `GameSession` with XML doc explaining the split-brain guard purpose (SCALE-03).
- `GameSessionConfiguration` maps the column `HasMaxLength(128)` and declares a filtered unique index with `HasDatabaseName("uq_game_sessions_idempotency_key")` and `HasFilter("\"IdempotencyKey\" IS NOT NULL")` — filter literal matches the migration SQL exactly to prevent EF pending-model-changes mismatch.

### Task 2 — Core migration (commit 65f415b)

- Hand-authored `20260622000000_AddGameSessionIdempotencyKey.cs` (timestamp follows last Core migration `20260606100000_AddAuditActorIdFk`).
- `Up()`: `AddColumn<string>` (varchar(128), nullable) + raw SQL `CREATE UNIQUE INDEX uq_game_sessions_idempotency_key ... WHERE "IdempotencyKey" IS NOT NULL` via `migrationBuilder.Sql(...)` (EF fluent API cannot express a partial index WHERE clause).
- `Down()`: `DROP INDEX IF EXISTS gamekit."uq_game_sessions_idempotency_key"` then `DropColumn` — non-destructive and reversible.
- `Designer.cs` contains the post-migration target model with the `IdempotencyKey` property and filtered unique index.
- `GameKitDbContextModelSnapshot.cs` updated to match EF config exactly (index name + filter literal identical).
- Follows the `AddSessionParticipationFraction` analog: `partial class`, `#nullable disable`, namespace block (not file-scoped), XML doc citing CLAUDE.md Core-table-ownership boundary rule.

### Task 3 — Idempotent write in ProposalService (commit ab75446)

- `CreateSessionAsync(ProposalFields, CancellationToken)` → `CreateSessionAsync(Guid proposalId, ProposalFields, CancellationToken)` (call site in `AcceptAsync` updated to pass `proposalId`, which is already in scope).
- Sets `idempotencyKey = proposalId.ToString()` when constructing the session.
- Replaces EF `Add + SaveChanges` for the `game_sessions` row with `ExecuteSqlRawAsync` + `ON CONFLICT ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL DO NOTHING`.
- `rowsInserted == 0` → concurrent replica won; resolve existing session id via `FirstOrDefaultAsync` on `IdempotencyKey`; skip participant insert.
- `rowsInserted == 1` → this caller won; insert participants normally via EF SaveChanges.
- `BeforeSessionInsert` chaos seam preserved BEFORE the idempotent INSERT (16-04 split-brain test relies on it).
- All SQL values bound via `NpgsqlParameter` — no string interpolation of ids into SQL (T-16-02-02).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Test stubs lacked RenewLeaseAsync after plan 16-01 ILeaderLease extension**

- **Found during:** Task 3 (test build step)
- **Issue:** `MatchmakingLeaderHealthCheckTests.StubLease` and `ReconcilerSweepTests.StubMatchmakerLease` implement `IMatchmakerLease : ILeaderLease` but were missing `RenewLeaseAsync(CancellationToken)` which `ILeaderLease` added in plan 16-01. Additionally `MatchmakingLeaderHealthCheckTests` was missing `using GameKit.Core.Services` so `LeaseStatus` (moved from Matchmaking to Core in 16-01) could not resolve.
- **Fix:** Added `RenewLeaseAsync` stub returning `false`/`throw NotSupportedException()` respectively; added `using GameKit.Core.Services;` to HealthCheck test file.
- **Files modified:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs`, `tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs`
- **Commit:** ab75446

**2. [Rule 1 - Bug] CancellationToken passed as SQL parameter via params object[] overload**

- **Found during:** Task 3 (ProposalAcceptHappyPathTests first run)
- **Issue:** `ExecuteSqlRawAsync(sql, param1, param2, ..., ct)` uses the `params object[]` overload — `ct` was appended to the parameter array and EF Core tried to map `CancellationToken` to a Postgres type, throwing `InvalidOperationException`.
- **Fix:** Extracted parameters into explicit `object[]` array, used `ExecuteSqlRawAsync(sql, IEnumerable<object>, CancellationToken)` overload so `ct` is correctly passed as the cancellation argument.
- **Files modified:** `src/GameKit.Matchmaking/Services/ProposalService.cs`
- **Commit:** ab75446

**3. [Rule 1 - Bug] ON CONFLICT clause missing WHERE predicate for partial index**

- **Found during:** Task 3 (ProposalAcceptHappyPathTests second run)
- **Issue:** `ON CONFLICT ("IdempotencyKey") DO NOTHING` fails with `42P10: there is no unique or exclusion constraint matching the ON CONFLICT specification` when the only constraint is a partial unique index. Postgres requires the `ON CONFLICT` clause to mirror the index predicate: `ON CONFLICT ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL DO NOTHING`.
- **Fix:** Updated the SQL to include the WHERE clause in the ON CONFLICT target.
- **Files modified:** `src/GameKit.Matchmaking/Services/ProposalService.cs`
- **Commit:** ab75446 (same commit, iterative fix)

## Verification

- `GameKit.Core` builds: passed (exit 0)
- `GameKit.Matchmaking` builds: passed (exit 0)
- `ProposalAcceptHappyPathTests.TwoPlayer_BothAccept_CreatesSession_With_TwoTeams_AndPublishesMatched`: passed
- Acceptance criteria checks: all passed (grep counts verified)

## Known Stubs

None. The idempotency key is set from the real `proposalId` at runtime and the ON CONFLICT path resolves the existing row from Postgres.

## Threat Flags

No new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries beyond what the plan's `<threat_model>` already documents (T-16-02-01 through T-16-02-03).

## Self-Check: PASSED

- `src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs` — FOUND
- `src/GameKit.Core/Entities/GameSession.cs` — FOUND (IdempotencyKey property)
- `src/GameKit.Core/Data/Configurations/GameSessionConfiguration.cs` — FOUND (uq_game_sessions_idempotency_key)
- `src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs` — FOUND (IdempotencyKey in snapshot)
- `src/GameKit.Matchmaking/Services/ProposalService.cs` — FOUND (CreateSessionAsync with Guid proposalId, ON CONFLICT)
- Commits bc1d640, 65f415b, ab75446 — present in git log
