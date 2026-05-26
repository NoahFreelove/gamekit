<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Postgres role provisioning + rotation

GameKit splits Postgres access across **three** dedicated roles. Every production
deployment must provision them up-front; the shipped `docker-compose.yml` only covers
the dev case.

| Role             | Purpose                                                | Privileges (on schema `gamekit`)                                   |
|------------------|--------------------------------------------------------|--------------------------------------------------------------------|
| `gamekit_owner`  | Migrations only (DDL)                                  | OWNER of database `gamekit` and schema `gamekit`.                  |
| `gamekit_app`    | Runtime DML for the GameKit HTTP API process           | `SELECT, INSERT, UPDATE, DELETE` on every table; `USAGE, SELECT` on sequences; `EXECUTE` on functions. |
| `gamekit_reader` | Game-server tier reads — never writes                  | `SELECT` only on every table.                                      |

The split lets you keep the runtime app process on a credential that **cannot** issue
`CREATE TABLE` / `ALTER` / `DROP`, and lets game-server processes (which only need to
read matchmaking/player state) run on a credential that **cannot** issue `INSERT`. This
is empirically asserted by the DIST-02 integration test
(`tests/GameKit.Distribution.Integration.Tests/DIST02_GamekitReaderInsertDeniedTests.cs`),
which opens a connection as `gamekit_reader` and confirms an `INSERT` raises Postgres
SQLSTATE `42501` ("permission denied for table game_sessions").

> Do not run migrations, or the runtime app, or game-server reads, on the same
> Postgres user. If you collapse the three roles into one you lose the privilege-
> separation guarantees the schema was designed around.

---

## Canonical bootstrap script (dev)

The dev stack ships these roles via `docker/postgres/init/01-roles.sql`, which the
official `postgres:17.9` image executes from `/docker-entrypoint-initdb.d` on first
container start. The script is **idempotent** (DO-blocks guard role creation,
`IF NOT EXISTS` guards schema/database) so it is safe to re-run after a `docker
compose down -v`.

The script is the source of truth — read it directly:

```bash
cat docker/postgres/init/01-roles.sql
```

Key constructs you will adapt for production:

```sql
CREATE ROLE gamekit_owner  LOGIN PASSWORD 'gamekit_owner_dev';
CREATE ROLE gamekit_app    LOGIN PASSWORD 'gamekit_app_dev';
CREATE ROLE gamekit_reader LOGIN PASSWORD 'gamekit_reader_dev';

CREATE DATABASE gamekit OWNER gamekit_owner;
\c gamekit
CREATE SCHEMA IF NOT EXISTS gamekit AUTHORIZATION gamekit_owner;
GRANT USAGE ON SCHEMA gamekit TO gamekit_app, gamekit_reader;

ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT USAGE, SELECT ON SEQUENCES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT ON TABLES TO gamekit_reader;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT EXECUTE ON FUNCTIONS TO gamekit_app;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO gamekit_app, gamekit_reader;
```

The `ALTER DEFAULT PRIVILEGES` lines are the load-bearing part: they make every
**future** table created BY `gamekit_owner` land with the correct grants, so individual
migrations do not need to ship `GRANT` statements. This is why the per-package
migrations (Auth, Admin, Rankings, Matchmaking) never include GRANT/REVOKE in their EF
migration files — the defaults handle it.

The `REVOKE CREATE ON SCHEMA public FROM PUBLIC` line hardens against the
Postgres 14+ default where any role with `USAGE` on `public` can create objects in it.
Without the revoke, the `gamekit_app` role could smuggle tables into `public`
sideways.

---

## Production bootstrap

**Do not copy the dev passwords.** Treat the dev script as a template; for production
generate three independent high-entropy passwords up-front and store them in your
secrets manager (Vault, AWS Secrets Manager, Doppler, sealed `.env` — operator's
choice; GameKit takes no opinion). Example provisioning, run once against a fresh
Postgres 17.9 instance as a superuser (typically `postgres`):

```bash
# Generate three independent passwords (32 bytes base64 each ≈ 256 bits entropy).
OWNER_PW=$(openssl rand -base64 32)
APP_PW=$(openssl rand -base64 32)
READER_PW=$(openssl rand -base64 32)

# Persist them in your secrets store. The example below writes to a sealed file
# managed by ansible-vault / sops / git-crypt — pick the tool you already use.
cat > gamekit-db-creds.env <<EOF
GAMEKIT_DB_OWNER_PASSWORD=$OWNER_PW
GAMEKIT_DB_APP_PASSWORD=$APP_PW
GAMEKIT_DB_READER_PASSWORD=$READER_PW
EOF
chmod 0600 gamekit-db-creds.env

# Apply the bootstrap SQL — same shape as docker/postgres/init/01-roles.sql but with
# the production passwords interpolated. Run as a Postgres superuser.
psql -h prod-postgres.internal -U postgres -d postgres -v ON_ERROR_STOP=1 <<SQL
CREATE ROLE gamekit_owner  LOGIN PASSWORD '$OWNER_PW';
CREATE ROLE gamekit_app    LOGIN PASSWORD '$APP_PW';
CREATE ROLE gamekit_reader LOGIN PASSWORD '$READER_PW';
CREATE DATABASE gamekit OWNER gamekit_owner;
\c gamekit
CREATE SCHEMA IF NOT EXISTS gamekit AUTHORIZATION gamekit_owner;
GRANT USAGE ON SCHEMA gamekit TO gamekit_app, gamekit_reader;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT USAGE, SELECT ON SEQUENCES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT ON TABLES TO gamekit_reader;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT EXECUTE ON FUNCTIONS TO gamekit_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO gamekit_app, gamekit_reader;
SQL
```

After provisioning, the three connection strings your processes consume are:

```
# HTTP API tier (the process that loads GameKit.Core / .Auth / .Rankings / ...)
Host=prod-postgres.internal;Port=5432;Database=gamekit;Username=gamekit_app;Password=$GAMEKIT_DB_APP_PASSWORD

# Migration runner (CI deploy job or the gamekit CLI 'migrate' command)
Host=prod-postgres.internal;Port=5432;Database=gamekit;Username=gamekit_owner;Password=$GAMEKIT_DB_OWNER_PASSWORD

# Game-server tier (read-only — never writes)
Host=prod-postgres.internal;Port=5432;Database=gamekit;Username=gamekit_reader;Password=$GAMEKIT_DB_READER_PASSWORD
```

Inject these via environment variables; never bake them into `appsettings.json` in
source control. The standard ASP.NET configuration provider already binds
`ConnectionStrings__GameKit` → `ConnectionStrings:GameKit`.

---

## Schema-grant audit checklist

Run this against a candidate production database to confirm the grant matrix
matches the design. All four checks should return at least one row matching the
expected pattern.

```sql
-- 1. The three roles exist and can log in.
SELECT rolname, rolcanlogin FROM pg_roles
WHERE rolname IN ('gamekit_owner', 'gamekit_app', 'gamekit_reader');

-- 2. Schema 'gamekit' is owned by gamekit_owner.
SELECT nspname, pg_get_userbyid(nspowner) AS owner
FROM pg_namespace WHERE nspname = 'gamekit';
-- Expect: owner = 'gamekit_owner'

-- 3. gamekit_app has DML on every table in the gamekit schema.
SELECT table_name,
       has_table_privilege('gamekit_app', 'gamekit.' || table_name, 'INSERT') AS can_insert,
       has_table_privilege('gamekit_app', 'gamekit.' || table_name, 'SELECT') AS can_select,
       has_table_privilege('gamekit_app', 'gamekit.' || table_name, 'UPDATE') AS can_update,
       has_table_privilege('gamekit_app', 'gamekit.' || table_name, 'DELETE') AS can_delete
FROM information_schema.tables
WHERE table_schema = 'gamekit';
-- Expect every row: all four columns = TRUE.

-- 4. gamekit_reader is denied INSERT/UPDATE/DELETE on every table.
SELECT table_name,
       has_table_privilege('gamekit_reader', 'gamekit.' || table_name, 'INSERT') AS can_insert,
       has_table_privilege('gamekit_reader', 'gamekit.' || table_name, 'SELECT') AS can_select
FROM information_schema.tables
WHERE table_schema = 'gamekit';
-- Expect every row: can_insert = FALSE, can_select = TRUE.
```

Empirical sibling-of-this-doc: DIST-02 runs check #4 against a Testcontainers Postgres
on every PR build (`DIST02_GamekitReaderInsertDeniedTests.cs`). If you change the
grant matrix, update both the SQL bootstrap **and** the DIST-02 test fixture.

---

## Password rotation

Postgres lets you change a role's password without disconnecting active sessions —
existing connections continue with the old credential until they reconnect. The
rotation flow:

```bash
# 1. Generate a new password.
NEW_APP_PW=$(openssl rand -base64 32)

# 2. Apply it (as Postgres superuser).
psql -h prod-postgres.internal -U postgres -d gamekit -v ON_ERROR_STOP=1 -c \
    "ALTER ROLE gamekit_app PASSWORD '$NEW_APP_PW';"

# 3. Update the secret store with the new value.
#    (Vault / AWS SM / sops / ansible-vault — whichever you use.)

# 4. Rolling-restart the app processes so they re-read the new env var.
#    A blue/green or rolling deploy works; a hard restart works too — connections
#    are short-lived because the Npgsql connection pool recycles them.

# 5. Revoke the old credential ONLY after all app processes have restarted.
#    (Postgres has no "old credential still valid" window — step 2 already
#    invalidated the old password for new connections. Existing connections
#    survive on the open TCP socket; the rolling-restart in step 4 closes them.)
```

Rotate each role independently — do not bundle `gamekit_owner` / `gamekit_app` /
`gamekit_reader` rotations into one window, because the failure mode for each is
different (an owner-password mistake breaks the next deploy; an app-password mistake
takes down the live HTTP API; a reader-password mistake takes down the game-server
fleet).

**Recommended cadence:** 90 days for `gamekit_app` and `gamekit_reader`; on every
breach indicator or operator turnover for `gamekit_owner` (which is touched only by
deploy automation, so rotation pressure is lower).

---

## Common mistakes to avoid

- **Running migrations as `gamekit_app`.** The app role has no DDL privileges by
  design. EF Core will throw `permission denied for schema gamekit` at the first
  `CREATE TABLE`. Switch to the `gamekit_owner` connection for migration runs only.
- **Granting `gamekit_app` ownership of the schema.** This silently re-broadens the
  privilege envelope. `ALTER SCHEMA gamekit OWNER TO gamekit_owner;` to repair.
- **Skipping `REVOKE CREATE ON SCHEMA public FROM PUBLIC`.** The Postgres 14+ default
  allows `gamekit_app` to create tables in `public`. Without the revoke,
  application bugs that synthesize table names from user input can land objects in
  `public` outside the `gamekit` schema boundary.
- **Reusing one role for multiple environments.** Production / staging / preview
  each need independent roles in independent databases. Cross-environment role
  sharing means a leaked staging credential is also a production credential.
- **Forgetting to `GRANT USAGE` after adding a new schema.** GameKit only uses
  schema `gamekit`. If you add your own schemas for game-specific tables (e.g.
  `app_inventory`), grant `USAGE` plus the relevant `DEFAULT PRIVILEGES` to your
  own roles — GameKit's grants stop at the `gamekit` schema boundary.

---

## Related runbooks

- [`migrations-runbook.md`](migrations-runbook.md) — how the per-package migrations
  use the `gamekit_owner` role + advisory locks.
- [`bare-metal.md`](bare-metal.md) and [`container.md`](container.md) — where in the
  deployment flow the roles get provisioned.
- [`disaster-recovery.md`](disaster-recovery.md) — role re-provisioning during a
  restore.
