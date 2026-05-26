<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Migrations runbook

GameKit ships **per-package** EF Core migrations: every package
(`GameKit.Core`, `GameKit.Auth`, `GameKit.Admin.UI`, `GameKit.Rankings`,
`GameKit.Matchmaking`) owns its own history table and its own Postgres advisory
lock. The migration constraint (`CLAUDE.md`) is that a package's migrations
**only add new tables or FK references** — they never modify another package's
tables.

This runbook explains the moving parts and what to do when one breaks.

---

## Per-package history tables

Each package writes its `__EFMigrationsHistory` to a uniquely-named table inside
the `gamekit` schema, so the EF Core migration tracker for one package cannot
accidentally collide with another's. The five tables are:

| Package              | History table             | Constants source                                                |
|----------------------|---------------------------|-----------------------------------------------------------------|
| `GameKit.Core`       | `__ef_migrations_core`     | `src/GameKit.Core/Data/GameKitMigrationConstants.cs`            |
| `GameKit.Auth`       | `__ef_migrations_auth`     | `src/GameKit.Auth/Data/AuthMigrationConstants.cs`               |
| `GameKit.Admin.UI`   | `__ef_migrations_admin`    | `src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs`          |
| `GameKit.Rankings`   | `__ef_migrations_rankings` | `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs`       |
| `GameKit.Matchmaking`| `__ef_migrations_matchmaking` | `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` |

`GameKit.Presence` has **no** history table — it is Redis-only, no EF entities.

You can inspect what has been applied at any time:

```sql
SELECT * FROM gamekit.__ef_migrations_core      ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_auth      ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_admin     ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_rankings  ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_matchmaking ORDER BY "MigrationId";
```

Each row records one migration's ID + the EF Core product version that applied it.

---

## Per-package advisory locks

To prevent two app instances starting simultaneously from racing each other on
`CREATE TABLE`, every per-package migration hosted service grabs a Postgres
advisory lock before running. The lock keys are **live-verified** against
Postgres 17.9 (each key is `hashtext('gamekit.<package>.migrations')::bigint` —
the runtime fetches the constant from the C# source, but you can re-derive any
of them with `SELECT hashtext('gamekit.core.migrations')::bigint;` in psql).

| Package              | Advisory lock key | Negative? | Defined in                                                  |
|----------------------|-------------------|-----------|-------------------------------------------------------------|
| `GameKit.Core`       | **1800940027**    | no        | `src/GameKit.Core/Data/GameKitMigrationConstants.cs`        |
| `GameKit.Auth`       | **-298890956**    | yes       | `src/GameKit.Auth/Data/AuthMigrationConstants.cs`           |
| `GameKit.Admin.UI`   | **-2101739634**   | yes       | `src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs`      |
| `GameKit.Rankings`   | **-156812172**    | yes       | `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs`   |
| `GameKit.Matchmaking`| **388956820**     | no        | `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` |

Negative keys are valid — Postgres' `hashtext()` returns an `int4` which is sign-
preserved into `bigint`; advisory locks accept any 64-bit signed integer. The
five keys are **pairwise-distinct**, asserted at test time by each package's
`*AdvisoryLockKeyTests` integration test (e.g.
`MatchmakingAdvisoryLockKeyTests.cs` Test B confirms `388956820` is not equal
to any of the four prior keys).

You can observe a held lock in real time:

```sql
SELECT pid,
       locktype,
       classid,        -- upper 32 bits of the advisory key
       objid,          -- lower 32 bits of the advisory key
       (classid::bigint << 32) | (objid::bigint & 4294967295)
           AS reconstructed_key,
       mode,
       granted
FROM pg_locks
WHERE locktype = 'advisory';
```

If a migration is in flight, you will see one row per package whose hosted
service is currently running. If a deploy hung, you can look up the holding
backend's PID and check what query it is stuck on:

```sql
SELECT pid, state, wait_event_type, wait_event, query
FROM pg_stat_activity
WHERE pid = <pid_from_pg_locks>;
```

---

## Migration boot order

Migrations are applied by per-package `IHostedService`s registered by the
package's `Add*()` extension method. Run order follows the order the consumer
called them:

```csharp
// Program.cs in the consumer app
services.AddGameKit(...)               // registers GameKitMigrationHostedService (Core)
        .AddAuth(...)                  // registers AuthMigrationHostedService
        .AddRankings(...)              // registers RankingsMigrationHostedService
        .AddMatchmaking(...)           // registers MatchmakingMigrationHostedService
        .AddPresence(...);             // no migration HS — Redis only
```

`IHost.StartAsync` calls `StartAsync` on the hosted services in registration
order, so Core runs first, then Auth, then Rankings, then Matchmaking. Each
package's hosted service:

1. Opens a Postgres connection as `gamekit_owner`.
2. Calls `SELECT pg_try_advisory_lock(<package-key>)` — non-blocking try.
3. If the lock is taken (another instance is mid-migration), retries with
   exponential backoff until the lock is free.
4. Calls `Database.Migrate()` against the package's `DbContext`.
5. Releases the advisory lock.

`GameKit.Admin.UI`'s `AdminMigrationHostedService` runs whenever the admin
package is registered. Its position in the boot order depends on where in the
fluent chain `MapGameKitAdmin()` is called.

**The `GameKitVersionAssertionHostedService` runs BEFORE any migration hosted
service** — registered by `AddGameKit()` first so version mismatches fail-fast
before Postgres is even touched. See `GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs`.

---

## Applying migrations out-of-band

For deployments where the runtime app should not have DDL permissions (the
recommended posture — the app runs as `gamekit_app`, which cannot `CREATE
TABLE`), use the `gamekit` CLI to apply migrations as `gamekit_owner` as a
pre-deploy step:

```bash
# Install the CLI once on the deploy host.
dotnet tool install --global GameKit.Cli

# Apply Core migrations.
gamekit migrate \
    -c "Host=prod-postgres.internal;Database=gamekit;Username=gamekit_owner;Password=$OWNER_PW"
```

The CLI calls into `GameKit.Core`'s migration runner directly. Per-package
migrations for `Auth` / `Admin.UI` / `Rankings` / `Matchmaking` still run from
the app's hosted services on startup (the CLI does not currently know about
sibling packages). The recommended deploy sequence:

```bash
# 1. Apply Core migrations as gamekit_owner (CLI).
gamekit migrate -c "$OWNER_CONN"

# 2. Rolling-restart the app fleet. Each replica's hosted services apply
#    the sibling-package migrations; per-package advisory locks serialize.
sudo systemctl restart mygame.service
```

For zero-DDL-at-runtime operators, the alternative is to:

- Run a one-shot migration container (image == app image; entrypoint runs each
  package's `Database.Migrate()` then exits) as a Kubernetes Job before
  rolling the Deployment.
- Use a CI step that boots a transient app instance with `gamekit_owner`
  credentials, waits for `IHost.StartAsync` to complete, then shuts down. The
  hosted services apply all five package migrations during that window.

---

## What to do when a migration fails

### Symptom A — "another deploy is holding the advisory lock"

Log line shape:

```
WARN  GameKit.Auth.Data.AuthMigrationHostedService: Advisory lock (-298890956) currently held; retrying in 2s...
```

A previous deploy left the lock held — usually because the process was killed
mid-migration. Postgres advisory locks are session-scoped: the moment the
holding backend disconnects, the lock releases. Check whether the holder is a
zombie:

```sql
SELECT pl.pid, pa.state, pa.application_name, pa.query, pa.backend_start
FROM pg_locks pl
JOIN pg_stat_activity pa USING (pid)
WHERE pl.locktype = 'advisory';
```

If the `state` is `idle` and `application_name` matches a stopped process, the
backend is genuinely stuck — disconnect it:

```sql
SELECT pg_terminate_backend(<pid>);
```

The advisory lock releases instantly; the retrying hosted service grabs it on
its next backoff tick.

### Symptom B — `relation "gamekit.something" already exists`

This means EF Core thinks the migration has not been applied (no row in the
history table), but the table is already there. Two causes:

1. **Hand-rolled `CREATE TABLE` in the past.** Someone created the table with
   `psql` outside the migration system. Recover by manually inserting the
   history row:

   ```sql
   INSERT INTO gamekit.__ef_migrations_auth ("MigrationId", "ProductVersion")
   VALUES ('20260417000000_AuthInitial', '10.0.6');
   ```

2. **History table corruption.** Rare — usually after a partial restore. Check
   that the table exists and is well-formed:

   ```sql
   SELECT * FROM gamekit.__ef_migrations_auth;
   ```

   If the table is empty but the production tables exist, restore the history
   from a backup (`pg_restore -t '__ef_migrations_*' ...`) — never re-create
   the production schema from scratch in a panic.

### Symptom C — `permission denied for schema gamekit`

The migration hosted service is running with `gamekit_app` credentials instead
of `gamekit_owner`. Check `ConnectionStrings:GameKit` — it should resolve to
the owner role for migration runs. If you have split-credential deployments
(app uses `gamekit_app`; migrations use `gamekit_owner` via a one-shot job),
make sure the migration job has its own connection-string env var.

### Symptom D — `permission denied for table game_sessions` (or any other table) at runtime

This is **not** a migration failure — it is the role layout working as designed.
The app is trying to write through a connection that does not have INSERT/UPDATE
permission (typically `gamekit_reader` mistakenly wired to the app process).
See [`postgres-roles.md`](postgres-roles.md) for the grant matrix.

### Symptom E — migration history table missing entirely

The CLI rebuilds an empty history. Run the bootstrap migration:

```bash
gamekit migrate -c "$OWNER_CONN"
```

If you are recovering from a botched restore where some tables exist but the
history table does not, **do not** just re-run migrations — EF Core will try
to `CREATE TABLE` on tables that already exist (Symptom B). Manually insert
each migration ID into the history table first, then re-run.

---

## Rolling back a migration

EF Core's `Database.Migrate()` does not support automatic rollbacks. To roll
back, you either:

- Generate the SQL for the previous state and apply it by hand (slow, error-
  prone, but reversible).
- Restore from the pre-migration backup (the recommended path). See
  [`disaster-recovery.md`](disaster-recovery.md).

For non-destructive migrations (added columns, new tables, new indices) the
safest "rollback" is to **roll forward** — deploy a new migration that drops
the offending change. This keeps the migration history monotonic, which keeps
the audit story clear.

Per-package migration boundary discipline (`CLAUDE.md`) means a botched
`GameKit.Matchmaking` migration cannot have affected `GameKit.Core` tables —
the failure is scoped to one package. Restore only that package's tables (or
roll forward only that package).

---

## Migration design rules (for contributors)

If you are writing a new migration in a GameKit package, follow these rules
(they are also the reason the per-package boundary holds):

1. **Never touch another package's tables.** The `*MigrationModelCustomizer`
   in each package calls `ExcludeFromMigrations` on every prior-package entity
   type — adding a new entity in a downstream package does not retroactively
   change upstream migrations.
2. **Always use the package's schema-qualified table name.** The default schema
   is `gamekit`; do not invent siblings.
3. **Index migrations should run `CONCURRENTLY` where possible.** EF Core 10
   supports this via `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY ...")`.
   Postgres advisory locks do not block concurrent index builds because they
   are session-scoped.
4. **Migration files are immutable once shipped.** Never edit a migration
   that has been applied to a production database — write a new migration
   on top.
5. **The 14-digit timestamp prefix is sort order.** EF Core applies migrations
   in lexicographic order. Two migrations in the same package with colliding
   timestamps cause non-deterministic application order — use UTC and append
   six trailing zeros for cross-package safety (e.g.
   `20260516000000_MatchmakingInitial`).

---

## Operational checks

```bash
# 1. Which migrations have been applied in each package?
psql "$OWNER_CONN" -c '\dt gamekit.__ef_migrations_*'
for tbl in core auth admin rankings matchmaking; do
    echo "=== $tbl ==="
    psql "$OWNER_CONN" -c "SELECT * FROM gamekit.__ef_migrations_$tbl ORDER BY \"MigrationId\";"
done

# 2. Is anyone currently holding a migration lock?
psql "$OWNER_CONN" -c "
SELECT pid, classid, objid, mode, granted, query_start
FROM pg_locks pl
JOIN pg_stat_activity pa USING (pid)
WHERE pl.locktype = 'advisory';
"

# 3. What's the size of the schema? (sanity check before / after a migration)
psql "$OWNER_CONN" -c "
SELECT n.nspname AS schema, c.relname AS table, pg_size_pretty(pg_total_relation_size(c.oid))
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'gamekit' AND c.relkind = 'r'
ORDER BY pg_total_relation_size(c.oid) DESC;
"
```

---

## Related runbooks

- [`postgres-roles.md`](postgres-roles.md) — role split; why migrations run as
  `gamekit_owner`.
- [`disaster-recovery.md`](disaster-recovery.md) — restoring from a pre-
  migration backup.
- [`bare-metal.md`](bare-metal.md) / [`container.md`](container.md) — where
  the migration step sits in a deploy.
