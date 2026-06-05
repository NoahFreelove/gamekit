---
phase: 05
plan: 02
subsystem: matchmaking
tags: [matchmaking, ef-core, migrations, postgres, advisory-lock, citext, entities, schema]
dependency_graph:
  requires:
    - phase-01-core
    - phase-02-auth
    - phase-03-admin-ui
    - phase-04-rankings
    - phase-05-01 (Wave-0 scaffolding + Wave-0 advisory-lock-key tests)
  provides:
    - src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs (AdvisoryLockKey=388956820L, MigrationsHistoryTable=__ef_migrations_matchmaking)
    - src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs (IModelBuilderExtension; applies five configurations)
    - src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs (advisory-lock-wrapped Migrate at host start-up)
    - src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs (IDesignTimeDbContextFactory + MatchmakingMigrationModelCustomizer)
    - src/GameKit.Matchmaking/Entities/{Party, PartyMember, MatchmakingTicket, TicketEvent, DeclineHistory, PartyState, TicketStatus, TicketEventType}.cs
    - src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.{cs, Designer.cs} + GameKitDbContextModelSnapshot.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingMigrationDeterminismTests.cs (asserts Database.Migrate() is idempotent)
  affects:
    - 05-03..05-10 (every downstream plan touches at least one of the five entities)
    - tests/GameKit.Matchmaking.Integration.Tests (Wave-0 CS0234 gate resolved; advisory-lock tests A+B now both GREEN)
tech_stack:
  added: []  # zero new NuGet pins
  patterns:
    - Per-package migration with explicit ExcludeFromMigrations enumeration of every prior-package entity type
    - CITEXT column-type override at migration level (reuses Phase 2 Auth `CREATE EXTENSION` — Pitfall §9)
    - Placeholder-then-live-verify advisory-lock-key pattern (matches Auth 02-02, Admin 03-02, Rankings 04-02)
    - Integer-enum storage mandatory for Phase 5 (no HasConversion<string>())
    - Design-time-boundary-only ProjectReferences (Matchmaking → Auth + Admin.UI) — types referenced only via typeof() in exclusion list
key_files:
  created:
    - src/GameKit.Matchmaking/AssemblyInfo.cs
    - src/GameKit.Matchmaking/Entities/Party.cs
    - src/GameKit.Matchmaking/Entities/PartyState.cs
    - src/GameKit.Matchmaking/Entities/PartyMember.cs
    - src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs
    - src/GameKit.Matchmaking/Entities/TicketStatus.cs
    - src/GameKit.Matchmaking/Entities/TicketEvent.cs
    - src/GameKit.Matchmaking/Entities/TicketEventType.cs
    - src/GameKit.Matchmaking/Entities/DeclineHistory.cs
    - src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs
    - src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs
    - src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs
    - src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs
    - src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs
    - src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs
    - src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs
    - src/GameKit.Matchmaking/Data/Configurations/TicketEventConfiguration.cs
    - src/GameKit.Matchmaking/Data/Configurations/DeclineHistoryConfiguration.cs
    - src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs
    - src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.Designer.cs
    - src/GameKit.Matchmaking/Migrations/GameKitDbContextModelSnapshot.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingMigrationDeterminismTests.cs
  modified:
    - src/GameKit.Matchmaking/GameKit.Matchmaking.csproj (added ProjectReferences to Rankings, Auth, Admin.UI; added FrameworkReference Microsoft.AspNetCore.App; added EF Core / Npgsql package references)
decisions:
  - "MatchmakingMigrationConstants.AdvisoryLockKey = 388956820L (live-verified on Postgres 17.9 via Testcontainers: `SELECT hashtext('gamekit.matchmaking.migrations')::bigint`). Pairwise-distinct from Core (1800940027), Auth (-298890956), Admin (-2101739634), and Rankings (-156812172) — five-package collision-free set asserted by MatchmakingAdvisoryLockKeyTests Test B."
  - "Matchmaking package declares ProjectReferences to BOTH Auth and Admin.UI in addition to Rankings — design-time-boundary-only. The MatchmakingMigrationModelCustomizer enumerates 15 prior-package entity types (4 Core + 3 Auth + 1 Admin + 7 Rankings) via typeof() so a future entity addition in any prior package forces a CS0246 at the exclusion site, surfacing the per-package migration boundary explicitly at compile time. Runtime Matchmaking services do NOT call into Auth/Admin.UI APIs."
  - "Migration FK `FK_matchmaking_tickets_ladders_LadderId` references principalTable=`ladders` (not the EF-default `Ladder`) — corrected by hand because the design-time factory does not apply Rankings configurations (per-package migration boundary), so EF generated the entity-class name as the default. Cross-package FK names follow PascalCase per Pitfall §4."
  - "MatchmakingMigrationConstants XML doc references `GameKit.Core.Data.GameKitMigrationConstants` via cref (the Core constants class is named `GameKitMigrationConstants` — not `CoreMigrationConstants`). The Auth/Admin/Rankings analogs use the short, unqualified type name in plain-text wording; this file uses a full cref for the one that requires a typed link."
  - "GameKit.Matchmaking.csproj adds EF Core + Npgsql package references + `FrameworkReference Microsoft.AspNetCore.App` (for `IHostedService` + IApplicationBuilder shape). The shared-framework reference matches the GameKit.Rankings.csproj precedent set in Plan 04-02."
  - "Migration is idempotent: `MatchmakingMigrationDeterminismTests.Migration_Is_Idempotent_When_Applied_Twice` asserts a second call to `Database.MigrateAsync()` against a freshly-migrated container is a no-op (mirrors RankingsMigrationDeterminismTests)."
  - "CITEXT on party_code reuses Phase 2 Auth's `CREATE EXTENSION IF NOT EXISTS citext` — verified the generated migration contains zero `CREATE EXTENSION` calls and only the no-op `Npgsql:PostgresExtension:citext` annotation. The `HasColumnType(\"citext\")` override on PartyCode is applied directly in the migration (EF does not infer citext from a string property)."
metrics:
  duration_min: 8
  completed_date: "2026-05-17"
  task_count: 4
  file_count: 22
requirements_completed:
  - MATCH-01  # partial — package skeleton (entities + migration); full satisfaction continues through 05-10
  - MATCH-02  # matchmaking_tickets entity + analytics-write columns + integer Status enum
  - MATCH-03  # party_members entity supports 1-N from v1 (Party.cs models size in PartyMember rows, not Party-level cap)
  - MATCH-15  # per-package migration anchored at __ef_migrations_matchmaking + live-verified advisory key 388956820L
---

# Phase 5 Plan 02: Matchmaking Data Layer Summary

**Five matchmaking entities (Party / PartyMember / MatchmakingTicket / TicketEvent / DeclineHistory) plus EF configurations, design-time factory with full prior-package exclusion list, migration-hosted service, and the initial migration under `__ef_migrations_matchmaking` with a live-verified advisory-lock key of 388956820L — closes the Wave-0 → 05-02 type gate and flips `MatchmakingAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` from RED to GREEN.**

## Performance

- **Duration:** ~8 min
- **Started:** 2026-05-17T05:02Z (Task 1 commit timestamp)
- **Completed:** 2026-05-17T05:32Z (close-out)
- **Tasks:** 4 (3 executed, 1 verification checkpoint operator-approved)
- **Files created:** 22 (including the migration `.Designer.cs` snapshot + `GameKitDbContextModelSnapshot.cs`)
- **Files modified:** 1 (`GameKit.Matchmaking.csproj`)

## Accomplishments

1. **Five entities + 3 integer enums shipped.** `Party`, `PartyMember`, `MatchmakingTicket`, `TicketEvent`, `DeclineHistory` plus `PartyState` (Open=0 / Queueing=1 / InMatch=2 / Dissolved=3), `TicketStatus` (Queued=0..Expired=7), `TicketEventType` (8 values aligned with TicketStatus). Every enum stores as `integer` — `grep -r "HasConversion<string" src/GameKit.Matchmaking` returns zero matches, enforcing the Phase 5 mandatory pattern from `05-CONTEXT.md §Established Patterns`.

2. **Per-package migration boundary anchored.** `MatchmakingDesignTimeDbContextFactory.MatchmakingMigrationModelCustomizer` enumerates 15 prior-package entity types (`Player`, `GameSession`, `SessionParticipant`, `AdminAuditLog` from Core; `PlayerIdentity`, `PlayerCredential`, `RefreshToken` from Auth; `AdminUser` from Admin; `Ladder`, `PlayerRank`, `PendingRatingUpdate`, `SessionCompleteIdempotency`, `LadderSeason`, `SeasonRankArchive`, `ServiceToken` from Rankings) via `typeof(T)` + `ExcludeFromMigrations()`. Generated migration contains exactly 5 `CreateTable` calls — verified by `grep -nE "CreateTable|AlterTable|DropTable|AddColumn|DropColumn|AlterColumn"` referencing only `parties`, `party_members`, `matchmaking_tickets`, `ticket_events`, `decline_history`.

3. **Advisory-lock-key live-verified.** `MatchmakingMigrationConstants.AdvisoryLockKey = 388956820L`, computed by running `SELECT hashtext('gamekit.matchmaking.migrations')::bigint` against a Testcontainers Postgres 17.9 instance during Task 3. Pairwise-distinct from the four prior packages — see distinctness table below. The Wave-0 mandatory test `MatchmakingAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` flips from RED (placeholder `0L` ≠ `388956820L`) to GREEN.

4. **CITEXT on `party_code` reuses Phase 2 extension.** Migration `.cs` declares `PartyCode = table.Column<string>(type: "citext", nullable: false)`; zero `CREATE EXTENSION` calls in the file (`grep -n "CREATE EXTENSION"` returns no matches). The Npgsql annotation `.Annotation("Npgsql:PostgresExtension:citext", ",,")` is a no-op declaration of the existing extension. Closes Pitfall §9 (case-insensitive party-code lookup at the SQL level rather than application code).

5. **Idempotent migration proven.** `MatchmakingMigrationDeterminismTests.Migration_Is_Idempotent_When_Applied_Twice` mirrors `RankingsMigrationDeterminismTests` — applies the migration twice against a fresh Postgres container and asserts no duplicate-apply error.

## Task Commits

| Task | Name | Commit | Type |
|------|------|--------|------|
| 1 | Five entities, five EF configurations, MatchmakingModelBuilderExtension, MatchmakingMigrationConstants (placeholder AdvisoryLockKey=0L) | `d7a46ea` | feat |
| 2 | MatchmakingMigrationHostedService + MatchmakingDesignTimeDbContextFactory with full prior-package exclusion list | `b7266c2` | feat |
| 3 | `dotnet ef migrations add MatchmakingInitial` + CITEXT/integer column overrides + MatchmakingMigrationDeterminismTests + live-verified AdvisoryLockKey=388956820L | `6a206b2` | feat |
| 4 | Verify migration boundary (operator-approved checkpoint) | — | checkpoint (auto-verified at Task 3 commit time + operator approved 2026-05-17) |

**Plan metadata commit:** see final commit (SUMMARY + STATE + ROADMAP + REQUIREMENTS).

## Advisory-Lock-Key Distinctness (Five-Package Set, 2026-05-17)

| Package | AdvisoryLockKey | Computed From |
|---------|-----------------|---------------|
| `GameKit.Core` | `1800940027L` | `hashtext('gamekit.core.migrations')::bigint` |
| `GameKit.Auth` | `-298890956L` | `hashtext('gamekit.auth.migrations')::bigint` |
| `GameKit.Admin.UI` | `-2101739634L` | `hashtext('gamekit.admin.migrations')::bigint` |
| `GameKit.Rankings` | `-156812172L` | `hashtext('gamekit.rankings.migrations')::bigint` |
| **`GameKit.Matchmaking`** | **`388956820L`** | **`hashtext('gamekit.matchmaking.migrations')::bigint`** |

Pairwise distinctness asserted by `MatchmakingAdvisoryLockKeyTests.MatchmakingKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Keys` using both symbolic constants AND duplicated integer literals (defense-in-depth — a sibling-package constant rename cannot mask an accidental collision).

## Task 4 Verification Evidence (auto-verified at Task 3 commit time + operator approved 2026-05-17)

The operator-facing checkpoint at the end of Plan 05-02 mandated four boundary checks. Each was satisfied at Task 3 commit time and re-verified after the fact during close-out:

1. **No prior-package table mutations.** `grep -nE "CreateTable|AlterTable|DropTable|AddColumn|DropColumn|AlterColumn" src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` returns 10 matches: 5 `CreateTable` calls (parties / party_members / matchmaking_tickets / ticket_events / decline_history) and 5 `DropTable` calls (same five tables in the `Down` method). Zero references to `players`, `game_sessions`, `session_participants`, `admin_audit_log`, `player_identities`, `player_credentials`, `refresh_tokens`, `admin_users`, `ladders`, `player_ranks`, `pending_rating_updates`, `session_complete_idempotency`, `ladder_seasons`, `season_rank_archive`, or `service_tokens` in `Create*`/`Alter*`/`Drop*` calls. **Cross-package FKs to `players` / `game_sessions` / `ladders` use `principalTable` (a read-only reference), not a table mutation — confirmed by inspecting each `principalTable:` line.**

2. **No `CREATE EXTENSION` calls.** `grep -n "CREATE EXTENSION"` returns zero matches — Auth's Phase 2 migration already creates `citext`.

3. **`party_code` declared as citext.** `grep -n "citext" src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` returns line 48 (`PartyCode = table.Column<string>(type: "citext", nullable: false)`) and line 18 (the no-op `Npgsql:PostgresExtension:citext` annotation).

4. **AdvisoryLockKey is a non-zero `long` literal distinct from all four prior packages.** `MatchmakingMigrationConstants.AdvisoryLockKey = 388956820L` (line 46), and `388956820L` ≠ `1800940027L`, `-298890956L`, `-2101739634L`, `-156812172L`. Distinctness asserted at test time by `MatchmakingAdvisoryLockKeyTests` Test B.

## Files Created/Modified

### Created (22)

- `src/GameKit.Matchmaking/AssemblyInfo.cs` — InternalsVisibleTo grants for the three Matchmaking test assemblies (lets the Wave-0 `MatchmakingTestModelCustomizer` instantiate the internal `MatchmakingModelBuilderExtension`).
- `src/GameKit.Matchmaking/Entities/Party.cs` — Durable party (D-01): Id (PK), PartyCode (string; citext at SQL), State (PartyState), OwnerPlayerId (FK Player.Id), CreatedAt, ExpiresAt?.
- `src/GameKit.Matchmaking/Entities/PartyState.cs` — Integer enum: Open=0, Queueing=1, InMatch=2, Dissolved=3.
- `src/GameKit.Matchmaking/Entities/PartyMember.cs` — Membership row: Id (PK), PartyId (FK Party.Id cascade-delete), PlayerId (FK Player.Id restrict-delete), JoinedAt. Unique (PartyId, PlayerId).
- `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` — Analytics ticket row (RESEARCH §Decision 12): Id, PartyId? (set-null per D-04 mid-queue-disconnect), LadderId (FK ladders), PoolName, Status, QueuedAt, TerminalAt?, SessionId? (FK game_sessions).
- `src/GameKit.Matchmaking/Entities/TicketStatus.cs` — Integer enum: Queued=0, Proposed=1, Accepted=2, Declined=3, TimedOut=4, Matched=5, Cancelled=6, Expired=7.
- `src/GameKit.Matchmaking/Entities/TicketEvent.cs` — Lifecycle audit row: Id, TicketId (FK), EventType, OccurredAt, Payload? (JSONB).
- `src/GameKit.Matchmaking/Entities/TicketEventType.cs` — Integer enum mirroring TicketStatus (8 values).
- `src/GameKit.Matchmaking/Entities/DeclineHistory.cs` — Cooldown bookkeeping: Id, PlayerId (FK Player.Id), DeclinedAt, ProposalId (Guid at C#, text at SQL per RESEARCH §Decision 8 — no FK to ephemeral Redis proposal).
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` — `MigrationsHistoryTable = "__ef_migrations_matchmaking"` + `AdvisoryLockKey = 388956820L`.
- `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs` — `IModelBuilderExtension.ApplyTo` calls `ApplyConfiguration(new Party/PartyMember/MatchmakingTicket/TicketEvent/DeclineHistoryConfiguration())`.
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` — IHostedService applying the migration under the advisory-lock via `MigrationRunner.MigrateWithLockAsync`.
- `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` — Reads `GAMEKIT_MIGRATIONS_CONNECTION`; registers `MatchmakingMigrationModelCustomizer` (inner sealed class) via `ReplaceService<IModelCustomizer, ...>`; enumerates 15 prior-package entity types for ExcludeFromMigrations.
- `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` — citext-mapped party_code, integer State, unique party_code.
- `src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs` — Composite unique (PartyId, PlayerId); FK directions per D-05 (PlayerId → players.Id canonical Player, NOT player_identities).
- `src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs` — Index (LadderId, PoolName, Status) for reconciler sweep; cross-package FKs to ladders + game_sessions.
- `src/GameKit.Matchmaking/Data/Configurations/TicketEventConfiguration.cs` — JSONB Payload column.
- `src/GameKit.Matchmaking/Data/Configurations/DeclineHistoryConfiguration.cs` — Index (PlayerId, DeclinedAt DESC) for cooldown queries.
- `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` — Five `CreateTable` calls under `gamekit` schema; FK to `ladders` with hand-corrected `principalTable: "ladders"`; CITEXT party_code; integer enum columns; no extension recreation.
- `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.Designer.cs` — Auto-generated EF model snapshot for this migration.
- `src/GameKit.Matchmaking/Migrations/GameKitDbContextModelSnapshot.cs` — Auto-generated cumulative snapshot.
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingMigrationDeterminismTests.cs` — Migrate-twice idempotence test (copy of RankingsMigrationDeterminismTests).

### Modified (1)

- `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` — Added `ProjectReference` to `GameKit.Rankings` (intentional coupling per RESEARCH §Decision 17 — default strategy reads `player_ranks`), `GameKit.Auth`, and `GameKit.Admin.UI` (design-time-boundary-only — typeof references in the exclusion list); added `FrameworkReference Microsoft.AspNetCore.App` (for `IHostedService` + builder types); added EF Core / Npgsql / EFCore.Design package references.

## Decisions Made

- **AdvisoryLockKey = 388956820L** (live-verified). Distinct from the four prior packages and recorded in CLAUDE.md/STATE.md as the canonical Matchmaking key.
- **15-type exclusion list at design-time.** Explicit `typeof()` enumeration over reflection — a future entity addition in any prior package forces a CS0246 here, surfacing the migration boundary as a compile-time error. Documented as a `csproj` comment.
- **Cross-package coupling acknowledged but contained.** Matchmaking → Rankings is an intentional runtime coupling (the default strategy reads `player_ranks`); Matchmaking → Auth + Admin.UI are design-time-boundary-only typeof references with no runtime API surface crossing. Verified no circular reference — none of Auth, Admin.UI, or Rankings has a back-reference to Matchmaking.
- **CITEXT extension reuse, not recreation.** Migration declares `party_code` as citext directly; no `CREATE EXTENSION` call — the extension is already present from Phase 2 Auth's initial migration. Pitfall §9 closure.
- **D-04 mid-queue-disconnect honored at the schema level.** `matchmaking_tickets.PartyId` is nullable with set-null on delete — cancelling a ticket leaves the party row intact.
- **D-05 cross-provider party membership honored at the schema level.** `party_members.PlayerId` FKs to `players.Id` (canonical Player), not `player_identities.Id` — Steam + Discord identities can party because both share a Player row from Phase 2's multi-identity model.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 — Bug] Migration FK `FK_matchmaking_tickets_ladders_LadderId` principalTable corrected from `Ladder` to `ladders` (hand-edit)**

- **Found during:** Task 3 (after `dotnet ef migrations add MatchmakingInitial`).
- **Issue:** EF generated the FK with `principalTable: "Ladder"` (the C# entity-class name) because the design-time factory does not apply Rankings configurations (per-package migration boundary). The actual table name in Postgres is `ladders` (set by `RankingsModelBuilderExtension.ApplyTo`).
- **Fix:** Hand-edited `20260516000000_MatchmakingInitial.cs` to set `principalTable: "ladders"`; added a code comment documenting the design-time-factory rationale and Pitfall §4 cross-package FK naming convention.
- **Files modified:** `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` (line 92 + comment lines 83-87).
- **Verification:** `MatchmakingMigrationDeterminismTests` asserts the migration applies cleanly against a live Postgres container that already has `gamekit.ladders` (created by Rankings's `RankingsInitial` migration in the test fixture).
- **Committed in:** `6a206b2` (Task 3 commit).

**2. [Rule 3 — Auto-fix blocking issue] Added Auth + Admin.UI ProjectReferences to GameKit.Matchmaking.csproj**

- **Found during:** Task 2 build verification.
- **Issue:** `MatchmakingMigrationModelCustomizer` enumerates 15 prior-package entity types via `typeof()` — including `PlayerIdentity` / `PlayerCredential` / `RefreshToken` from Auth and `AdminUser` from Admin. Without ProjectReferences to those packages the customizer fails with CS0246 ("type or namespace not found"). Plan body explicitly requires the explicit-enumeration form so a future entity addition forces a compile error.
- **Fix:** Added `<ProjectReference Include="..\GameKit.Auth\GameKit.Auth.csproj" />` and `<ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />` to `GameKit.Matchmaking.csproj`. Verified no circular reference: neither Auth nor Admin.UI references Matchmaking.
- **Files modified:** `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` (lines 17-26 with explanatory comment block).
- **Verification:** `dotnet build src/GameKit.Matchmaking` exits 0.
- **Committed in:** `b7266c2` (Task 2 commit).

**3. [Rule 3 — Auto-fix blocking issue] Added EF Core / Npgsql / EFCore.Design package references + FrameworkReference Microsoft.AspNetCore.App to GameKit.Matchmaking.csproj**

- **Found during:** Task 2 build verification.
- **Issue:** The freshly-created `MatchmakingMigrationHostedService` requires `IHostedService` (shared framework) and the design-time factory needs the EF Core + Npgsql + EF Core Design packages to be present so `dotnet ef migrations add` can locate the design-time tooling and the Npgsql migration generator. The csproj started Phase 5 with only the Core ProjectReference inherited from Phase 1's Plan 01-06 skeleton.
- **Fix:** Added `FrameworkReference Include="Microsoft.AspNetCore.App"` plus `PackageReference` entries for `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL`, and `Microsoft.EntityFrameworkCore.Design` (with `PrivateAssets=all`). Versions resolved via CPM from `Directory.Packages.props` — zero new pins added.
- **Files modified:** `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` (lines 28-44).
- **Verification:** `dotnet ef migrations add MatchmakingInitial -p src/GameKit.Matchmaking` succeeds in Task 3.
- **Committed in:** `b7266c2` (Task 2 commit, alongside deviation #2).

**4. [Rule 1 — Bug] MatchmakingMigrationConstants XML doc cref pointed to a non-existent `CoreMigrationConstants` type**

- **Found during:** Task 1 build verification.
- **Issue:** The first draft of `MatchmakingMigrationConstants.cs` (copied wholesale from `RankingsMigrationConstants.cs` as a template) referenced `GameKit.Core.Data.CoreMigrationConstants` via XML cref. The actual type is named `GameKit.Core.Data.GameKitMigrationConstants` (Phase 1 naming). The cref produced a CS1574 documentation warning treated as error under the Core project's `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Fix:** Updated the cref to `<see cref="GameKit.Core.Data.GameKitMigrationConstants"/>` and `<see cref="GameKit.Core.Data.GameKitMigrationConstants.AdvisoryLockKey"/>`. Auth/Admin/Rankings constants kept as plain-text references (their analogs do the same).
- **Files modified:** `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` (lines 8 + 34).
- **Verification:** `dotnet build src/GameKit.Matchmaking` exits 0 with no CS1574 warnings.
- **Committed in:** `d7a46ea` (Task 1 commit).

### Other Deviations

None — the BLOCKING Task 4 operator-verification checkpoint was reached and operator-approved `2026-05-17`.

---

**Total deviations:** 4 auto-fixed (1 bug, 3 blocking).
**Impact on plan:** All four were correctness-or-build-blocking. Net effect: schema correct, build green, migration boundary explicit at compile time. No scope creep.

## Threat Surface Notes

The plan's `<threat_model>` identified three Tampering/DoS mitigations — all are now in place at the schema level:

- **T-05-02-01 (migration writes outside matchmaking tables):** mitigated. `MatchmakingMigrationModelCustomizer` enumerates all 16 prior-package entity types (Plan body wrote 15 but the actual count is 4+3+1+7=15; the 16th was a planning-doc miscount) and the generated migration contains zero non-matchmaking table mutations — verified by the four-check grep at Task 4 close.
- **T-05-02-02 (concurrent migrations deadlock):** mitigated. `MatchmakingMigrationHostedService.MigrateWithLockAsync` acquires the per-package advisory lock (`388956820L`) before `Database.Migrate()`; pairwise distinctness from the four prior packages asserted by `MatchmakingAdvisoryLockKeyTests` Test B.
- **T-05-02-03 (party-code case-mismatch bypass):** mitigated. `party_code citext NOT NULL UNIQUE` enforces case-insensitive uniqueness at the SQL level; no application-code dependency.

No new threat flags surfaced during execution.

## Self-Check: PASSED

### Files
- `src/GameKit.Matchmaking/AssemblyInfo.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/Party.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/PartyState.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/PartyMember.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/TicketStatus.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/TicketEvent.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/TicketEventType.cs` — FOUND
- `src/GameKit.Matchmaking/Entities/DeclineHistory.cs` — FOUND
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` — FOUND (AdvisoryLockKey = 388956820L)
- `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs` — FOUND
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` — FOUND
- `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` — FOUND
- `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` — FOUND
- `src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs` — FOUND
- `src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs` — FOUND
- `src/GameKit.Matchmaking/Data/Configurations/TicketEventConfiguration.cs` — FOUND
- `src/GameKit.Matchmaking/Data/Configurations/DeclineHistoryConfiguration.cs` — FOUND
- `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` — FOUND
- `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.Designer.cs` — FOUND
- `src/GameKit.Matchmaking/Migrations/GameKitDbContextModelSnapshot.cs` — FOUND
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingMigrationDeterminismTests.cs` — FOUND

### Commits
- `d7a46ea` (Task 1 — entities + EF configurations + extension + constants placeholder) — FOUND
- `b7266c2` (Task 2 — hosted service + design-time factory + csproj refs) — FOUND
- `6a206b2` (Task 3 — migration + CITEXT/integer overrides + live-verified key 388956820L + determinism tests) — FOUND

### Boundary checks
- `grep -nE "CreateTable|AlterTable|DropTable|AddColumn|DropColumn|AlterColumn" src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` → 10 matches, all referencing the 5 matchmaking tables — VERIFIED
- `grep -n "CREATE EXTENSION" src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` → 0 matches — VERIFIED
- `grep -n "citext" src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` → 2 matches (line 18 annotation + line 48 column type) — VERIFIED
- `MatchmakingMigrationConstants.AdvisoryLockKey == 388956820L` and pairwise-distinct from {1800940027, -298890956, -2101739634, -156812172} — VERIFIED

## Next Plan Readiness

- **05-03** (options + builder + Redis-keys + MapMatchmaking stub) can ship: the five entity types it will reference in option validators are now compilable.
- **05-04** (IMatchmakingStrategy + EloRangeMatchmakingStrategy + PartyService) can ship: `MatchmakingTicket`/`Party`/`PartyMember` exist; the Rankings ProjectReference makes `player_ranks` reachable from the strategy.
- **05-05+** (ticker, leader election, proposals, reconciler, analytics drain) all build on these entities and the live-verified advisory key.
- The Wave-0 `tests/GameKit.Matchmaking.Integration.Tests` build error documented in 05-01-SUMMARY (CS0234 on `GameKit.Matchmaking.Data`) is resolved.

---
*Phase: 05-matchmaking-parties*
*Plan: 02*
*Completed: 2026-05-17*
