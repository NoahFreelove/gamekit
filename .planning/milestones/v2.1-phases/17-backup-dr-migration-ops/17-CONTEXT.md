# Phase 17: Backup / DR + Migration Ops - Context

**Gathered:** 2026-06-22
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Operators have a verified, CI-proven backup-restore procedure for Postgres + Redis and unified CLI tooling for migration dry-run and status; the restore rehearsal is a committed CI artifact, not just documentation.

**Requirements:** DR-01, DR-02, DR-03, DR-04, DR-05, DR-06, DR-07
**Depends on:** Phase 13 (stable baseline; DR otherwise independent of observability/hardening)
**UI hint:** no — CLI + CI + docs phase. Plan with `--skip-ui`.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices at Claude's discretion (discuss skipped). Use success criteria + existing codebase conventions.

### Success Criteria (what must be TRUE)
1. A CI Testcontainers job completes the full DR round-trip: `pg_dump` → container destroy → `pg_restore` → app starts → `GET /health/ready` returns 200; the job is a committed CI gate (not just a manual script).
2. `gamekit migrations list` prints every installed package's pending-migration count and the correct recommended application order (Core → Auth → Admin → Rankings → Matchmaking → Lobby).
3. `gamekit migrations apply --dry-run` prints idempotent SQL for all pending migrations across all installed packages without executing any DDL.
4. A CI check asserts that every `Down()` method in every migration file contains only `throw new NotSupportedException(...)` — no DROP TABLE, DROP COLUMN, or destructive DDL.
5. A `MigrationTimestampTests` suite asserts each package's latest migration timestamp is lexicographically greater than the previous package's latest timestamp, enforcing per-package application ordering.

</decisions>

<code_context>
## Existing Code Insights

- **GameKit.Cli already exists** (Spectre.Console.Cli per CLAUDE.md) with `src/GameKit.Cli/Commands/MigrateCommand.cs`, plus AdminCreate + ServiceToken commands and `Program.cs`. The `migrations list` and `migrations apply --dry-run` commands should extend this CLI (likely a `migrations` branch/sub-command group). Reuse the existing command registration + DbContext-resolution pattern in MigrateCommand.cs.
- **Migration inventory** (Down() bodies to convert under DR-04): Core 5, Auth 3, Admin.UI 1, Rankings 2, Matchmaking 2, Lobby 0 (~13 total). EF migration `Designer.cs`/`ModelSnapshot.cs` files are NOT migration Down() targets.
- **`/health/ready`** already exists from Phase 14 (`MapGameKitHealth()`), returns 200 once all six `IMigrationReadinessReporter` report ready — the DR round-trip's success assertion.
- **Per-package migration ordering** is already an enforced invariant (advisory-lock ordering, timestamp prefixes). DR-02/DR-05 codify the canonical order Core → Auth → Admin → Rankings → Matchmaking → Lobby.

### CROSS-PHASE RECONCILIATION (DR-04) — important
Existing migrations (including Phase 16's `20260622000000_AddGameSessionIdempotencyKey`) currently have REAL destructive `Down()` bodies (`DropTable`/`DropColumn`/`DropForeignKey`/`DropIndex`) — this was the convention through Phase 16. **DR-04 CHANGES the convention**: every migration `Down()` across ALL packages must be rewritten to contain only `throw new NotSupportedException("...")`. This phase must:
  - Convert every existing migration's `Down()` (all ~13) to `throw new NotSupportedException(...)`.
  - Add the CI/static check that fails the build on any non-conforming `Down()` (the gate must parse `Down()` bodies; allow only the throw statement + optional comment).
  - This is NOT a Phase 16 bug — Phase 16 followed the prior convention correctly; Phase 17 is the convention change.

</code_context>

<specifics>
## Specific Ideas

- **DR tooling is self-hosted/standard**: `pg_dump`/`pg_restore` are stock Postgres tools (GPL-compatible, no cloud). Redis backup = RDB snapshot (`BGSAVE`/`SAVE` or `redis-cli --rdb`) or AOF — documented in runbooks. No SaaS/cloud backup services (honors the no-cloud constraint).
- **DR round-trip CI test** uses Testcontainers (Docker available): spin Postgres, seed, `pg_dump`, destroy container, fresh container, `pg_restore`, start the app/migrations, assert `/health/ready` → 200. Reuse Phase 14/16 Testcontainers fixtures.
- **Runbooks** (`docs/runbooks/`): backup/restore for Postgres + Redis, migration apply procedure. Phase 20 will reference these.
- Build/test affected packages with `-p:NuGetAudit=false` (pre-existing MessagePack NU1903). Ignore stale Core.Integration `Migrate_Twice_Is_Idempotent` (note: it asserts a single migration; after DR work, reconcile or leave as documented pre-existing red).

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
