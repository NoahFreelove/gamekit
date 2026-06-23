<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Migration operations (DR-07)

This document is the canonical reference for operating GameKit migrations in production:
per-package application ordering, idempotent-script generation with `gamekit migrations apply
--dry-run`, the `Down()` `NotSupportedException` policy and its build-time enforcement, the
migration timestamp ordering rule, and the restore-from-backup rollback path.

---

## Per-package application ordering

GameKit ships **six** independently-versioned migration sets, each with its own EF Core history
table and its own Postgres advisory lock. Apply them in this canonical order:

| Order | Package              | History table                  | Advisory lock key |
|-------|----------------------|--------------------------------|-------------------|
| 1     | `GameKit.Core`       | `gamekit.__ef_migrations_core`       | `1800940027`   |
| 2     | `GameKit.Auth`       | `gamekit.__ef_migrations_auth`       | `-298890956`   |
| 3     | `GameKit.Admin.UI`   | `gamekit.__ef_migrations_admin`      | `-2101739634`  |
| 4     | `GameKit.Rankings`   | `gamekit.__ef_migrations_rankings`   | `-156812172`   |
| 5     | `GameKit.Matchmaking`| `gamekit.__ef_migrations_matchmaking`| `388956820`    |
| 6     | `GameKit.Lobby`      | `gamekit.__ef_migrations_lobby`      | `12178347`     |

`GameKit.Presence` has no EF migrations — it is Redis-only.

**Why this order matters:** Core defines the base `players` and `game_sessions` tables that all
other packages reference via FK. Auth adds identity and credential tables (which reference
`players`). Admin adds audit and admin-user tables. Rankings and Matchmaking add their own
tables with FK references to players. Lobby depends on sessions. Applying in the wrong order
produces FK-violation errors at migration time.

**Timestamp ordering rule:** Each package's latest migration timestamp must be lexicographically
greater than the previous package's latest timestamp. This is enforced by
`MigrationTimestampTests.PackageMigrations_LatestTimestamp_AreInCorrectOrder` (Plan 02) in
`tests/GameKit.Core.Tests/`. A new migration that violates the ordering fails CI immediately.
When you need to add a migration to an upstream package and the timestamp would be less than a
downstream package's latest, add a no-op ordering-marker migration to all downstream packages
with ascending timestamps (same pattern as the `*DrOrderingMarker` migrations added in Plan 02).

**Advisory locks:** Every package's migration hosted service holds a Postgres advisory lock
(session-scoped) while `Database.Migrate()` runs. This serializes concurrent app-instance
starts — the second instance waits and retries rather than racing on `CREATE TABLE`.
Advisory lock keys are computed as `hashtext('gamekit.<package>.migrations')::bigint` and
verified pairwise-distinct by each package's `*AdvisoryLockKeyTests` integration test.

---

## Pending migration count: `gamekit migrations list`

To see applied and pending migration counts for all packages without executing any DDL:

```bash
gamekit migrations list \
    --connection-string "Host=prod-postgres.internal;Database=gamekit;Username=gamekit_owner;Password=$OWNER_PW"
```

Output is a table with columns: Order, Package, Applied, Pending. Pending migrations are
highlighted in yellow. A non-zero Pending count for any package means the package's hosted
service will apply those migrations on the next app start (or you can apply them proactively
with `gamekit migrations apply`).

You can also pass the connection string via environment variable:

```bash
export GAMEKIT_MIGRATIONS_CONNECTION="Host=...;Database=gamekit;..."
gamekit migrations list
```

**What this calls:** For each package in canonical order, `migrations list` calls
`context.Database.GetAppliedMigrationsAsync()` and `context.Database.GetPendingMigrationsAsync()`
against the per-package history table. It does **not** apply migrations or modify the schema.

---

## Idempotent-script generation: `gamekit migrations apply --dry-run`

To generate idempotent SQL for all packages without executing any DDL:

```bash
gamekit migrations apply \
    --connection-string "Host=prod-postgres.internal;Database=gamekit;Username=gamekit_owner;Password=$OWNER_PW" \
    --dry-run
```

The command prints the SQL for all six packages to stdout, prefixed with a section header per
package. The generated SQL wraps each migration statement in EF Core's history-table
idempotency guards (checks the `__ef_migrations_*` history table before applying each step),
so it is safe to inspect, diff, and apply manually via `psql`.

**Critically: `--dry-run` executes zero DDL.** It calls
`IMigrator.GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` — a text-generation-only
API that does not open a transaction or touch the database schema (T-17-03-01 mitigation).

Use this output to:
- **Code-review schema changes** before a deployment.
- **Apply migrations as a CI pre-deploy step** via `psql`, without the app having DDL rights.
- **Audit** what migrations will run when the app starts.

### Live apply

Without `--dry-run`, `gamekit migrations apply` applies pending migrations across all six
packages in canonical order using the advisory-lock-serialized runner:

```bash
gamekit migrations apply \
    --connection-string "Host=prod-postgres.internal;Database=gamekit;Username=gamekit_owner;Password=$OWNER_PW"
```

This is the recommended pre-deploy step when the runtime app user (`gamekit_app`) does not
have DDL permissions (the standard security posture). The legacy `gamekit migrate` command
applies only the Core package and is retained for backwards compatibility.

---

## Down() `NotSupportedException` policy

**GameKit migration `Down()` methods throw `NotSupportedException` by policy.** There is no
in-place schema rollback. The canonical rollback path is restore-from-backup.

Every `Down()` method in every GameKit package migration file contains exactly:

```csharp
/// <inheritdoc />
protected override void Down(MigrationBuilder migrationBuilder)
{
    // DR-04: Destructive rollback is not supported. Restore from backup.
    // See docs/runbooks/postgres-backup-restore.md.
    throw new NotSupportedException(
        "Migration rollback via Down() is disabled in GameKit. " +
        "Restore from a Postgres backup instead. " +
        "See docs/runbooks/postgres-backup-restore.md.");
}
```

**Rationale:** EF Core's `Down()` reversal is inherently destructive for schema changes that
delete columns or drop tables — it irrecoverably destroys data. GameKit's per-package migration
boundary discipline means that a rollback would have to be coordinated across multiple packages
with cross-package FK relationships. This is operationally fragile. The supported recovery path
(restore from a `pg_dump` backup taken before the migration) is safer, auditable, and testable.

### GK0003 build-time enforcement

The `GK0003` Roslyn analyzer (`src/GameKit.Build/`) enforces the policy at compile time.
It fires as a **build error** on any `Down()` method in a `Migration` subclass whose body is
not exactly `{ throw new NotSupportedException(...); }`:

```
error GK0003: Migration Down() method 'SomePackage.Migrations.SomeMigration.Down' must
throw NotSupportedException. GameKit policy (DR-04): rollback via Down() is not supported.
Restore from backup instead.
```

This fires on every `dotnet build -warnaserror`, locally and in CI — before any tests run.
Empty `Down()` bodies also trigger `GK0003` because they silently no-op instead of throwing.

The analyzer is tested by `MigrationDownAnalyzerTests` in `tests/GameKit.Build.Tests/`.

---

## Rollback procedure

**There is no in-place schema rollback.** The supported recovery path for a bad migration is:

1. **Stop the app fleet** — prevent new migrations from running on startup.
2. **Restore from the pre-migration Postgres backup** — see
   [docs/runbooks/postgres-backup-restore.md](runbooks/postgres-backup-restore.md).
3. **Apply the correct migration** — either fix the migration and re-deploy, or roll forward
   with a new corrective migration.

For **non-destructive** migrations (added columns, new tables, new indexes with no data loss),
the preferred "rollback" is to **roll forward** — write a new migration that reverts the schema
change. This keeps the migration history monotonic and the audit story clear.

Per-package boundary discipline means a bad `GameKit.Matchmaking` migration cannot have
affected `GameKit.Core` tables — the failure is always scoped to one package.

### Pre-migration backup checklist

Before applying any migration to production:

```bash
# 1. Take a Postgres backup.
gamekit db backup \
    --connection-string "..." \
    --output /srv/backups/pre-migration-$(date -u +%Y%m%d-%H%M).pgdump

# 2. Preview the migration SQL.
gamekit migrations apply --connection-string "..." --dry-run > /tmp/migration-preview.sql

# 3. Review /tmp/migration-preview.sql — check for unexpected DDL.

# 4. Apply.
gamekit migrations apply --connection-string "..."

# 5. Confirm pending count is 0.
gamekit migrations list --connection-string "..."
```

---

## Migration timestamp ordering

The canonical ordering rule — each package's latest migration timestamp must be
lexicographically greater than the previous package's latest — is enforced by:

```
tests/GameKit.Core.Tests/MigrationTimestampTests.cs
  PackageMigrations_LatestTimestamp_AreInCorrectOrder   [Fact]
  AllPackages_HaveAtLeastOneMigration                   [Fact]
```

These are fast unit tests (no containers, no database) using reflection to scan each package
assembly. Run them to confirm the ordering is intact after adding any new migration:

```bash
dotnet test tests/GameKit.Core.Tests --filter "MigrationTimestamp" -p:NuGetAudit=false
```

The test message on failure includes the colliding timestamps and instructs you to add a
no-op ordering-marker migration to the upstream package.

---

## DR round-trip test

The full dump-destroy-restore-health-check cycle is validated by:

```bash
dotnet test tests/GameKit.DR.Tests \
    --filter "Category=DisasterRecovery" \
    -p:NuGetAudit=false
```

This test (Plan 05, `DrRoundTripTests`) uses Testcontainers to:
1. Apply all six package migrations against a fresh Postgres container.
2. Seed a test player record.
3. Run `pg_dump` inside the container via `IContainer.ExecAsync`.
4. Destroy the container.
5. Start a fresh Postgres container.
6. Run `pg_restore` inside the fresh container.
7. Assert `GET /health/ready` returns HTTP 200.

Run this test before deploying any release that follows a schema-altering migration.

---

## Migration design rules (for contributors)

1. **Never touch another package's tables.** The `*MigrationModelCustomizer` in each package
   calls `ExcludeFromMigrations` on every upstream-package entity type.
2. **Always use the `gamekit` schema.** Do not introduce sibling schemas.
3. **Add `CONCURRENTLY` indexes via raw SQL.** Use
   `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY ...")` for large-table indexes.
4. **Migration files are immutable once shipped.** Write a new migration on top; never edit a
   migration that has been applied to production.
5. **Timestamp prefix is sort order.** Use UTC with six trailing zeros for cross-package
   safety (e.g. `20260516000000_MatchmakingInitial`). Two migrations in the same package with
   colliding timestamps cause non-deterministic application order.
6. **Every `Down()` must throw `NotSupportedException`.** GK0003 enforces this at build time.

---

## Related runbooks

- [`docs/runbooks/postgres-backup-restore.md`](runbooks/postgres-backup-restore.md) — the rollback target.
- [`docs/ops/migrations-runbook.md`](ops/migrations-runbook.md) — per-package history tables, advisory locks, troubleshooting.
- [`docs/ops/disaster-recovery.md`](ops/disaster-recovery.md) — overview index for all backup runbooks.
