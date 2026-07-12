<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# GameKit Dev Stack

Local development environment: Postgres 17.9 + Redis 8.6.2, both configured with persistence and the three-role database posture GameKit relies on.

> This is a **development** stack. For production recipes (bare-metal, container, air-gapped), see the ops guide (Phase 6).

## Start / stop

```bash
# From repo root
docker compose up -d           # start
docker compose ps              # status
docker compose logs -f postgres  # tail logs
docker compose down            # stop (keeps volumes)
docker compose down -v         # stop + wipe volumes (full reset)
```

## Connection strings

| Role | Connection string | Use |
|------|-------------------|-----|
| `gamekit_owner` | `Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev` | Migrations only (`gamekit migrate`, `UseGameKit()` auto-migrate). |
| `gamekit_app` | `Host=localhost;Port=5432;Database=gamekit;Username=gamekit_app;Password=gamekit_app_dev` | Runtime DML -- GameKit HTTP API backend. |
| `gamekit_reader` | `Host=localhost;Port=5432;Database=gamekit;Username=gamekit_reader;Password=gamekit_reader_dev` | Game-server reads -- SELECT-only on `gamekit.*`. |

Redis: `localhost:6379` (no AUTH in dev; production ops guide covers ACL setup).

> **These passwords are dev-only.** Production deployments MUST set strong passwords and use a secrets manager (HashiCorp Vault, sops-age, Kubernetes Secrets, etc.). Do not ship `docker-compose.yml` verbatim to production.

## Role model (principle of least privilege)

The `docker/postgres/init/01-roles.sql` script provisions three Postgres roles. Default privileges are granted `FOR ROLE gamekit_owner` so future migrations land with correct grants without per-migration `GRANT` statements.

| Role | Grants |
|------|--------|
| `gamekit_owner` | Owns `gamekit` schema. Full DDL + DML on `gamekit.*`. Used only during migrations. |
| `gamekit_app` | `USAGE` on schema `gamekit`. `SELECT, INSERT, UPDATE, DELETE` on all current + future tables in `gamekit.*`. `USAGE, SELECT` on sequences. `EXECUTE` on functions. |
| `gamekit_reader` | `USAGE` on schema `gamekit`. `SELECT` on all current + future tables in `gamekit.*`. No DML. |

Additionally, `REVOKE CREATE ON SCHEMA public FROM PUBLIC` prevents any GameKit role from creating tables in `public` -- enforces schema boundary discipline (OPS-09).

## Redis configuration

| Flag | Value | Why |
|------|-------|-----|
| `--appendonly` | `yes` | Enable AOF -- every write appended to append-only file. |
| `--appendfsync` | `everysec` | fsync AOF once per second -- bounded data loss (max 1s). |
| `--maxmemory-policy` | `noeviction` | Reject writes when maxmemory hit instead of evicting keys -- matchmaking/presence workloads prefer loud failures over silent key loss. |
| `--save` | `3600 1 300 100 60 10000` | RDB snapshots alongside AOF (belt-and-suspenders). |

## Persistence

Named volumes `gamekit-postgres-data` and `gamekit-redis-data` persist data across container lifecycles. To wipe: `docker compose down -v`.

## Postgres version

Pinned to `postgres:17.9` (GA since late 2024). Postgres 18 data-directory changes make upgrades a migration; we revisit once 18 has been GA for ~1 year.

## Redis version / license

Pinned to `redis:8.6.2` (tri-licensed: RSALv2 / SSPLv1 / AGPLv3). Redis runs as a separate process (not linked), so its AGPLv3 image imposes no license obligation on GameKit's Apache-2.0 code. Operators preferring BSD-licensed Redis can override with `redis:7.4.8` in their own compose overlay.

## Known operational tasks

- **Connect as admin for troubleshooting:** `docker exec -it gamekit-postgres psql -U postgres`
- **Check GameKit schema objects:** `docker exec -it gamekit-postgres psql -U gamekit_owner -d gamekit -c '\dn'` and `\dt gamekit.*`
- **Verify role isolation:** `docker exec -it gamekit-postgres psql -U gamekit_reader -d gamekit -c "INSERT INTO gamekit.game_sessions (id, state, created_at) VALUES ('00000000-0000-0000-0000-000000000000', 'pending', now());"` -- expected: `ERROR: permission denied` (SQLSTATE 42501).
