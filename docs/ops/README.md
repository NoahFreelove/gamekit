<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# GameKit production-ops guide

This directory is the operator's reference for deploying GameKit in production.
The recipes are deliberately **opinionated and concrete** — every page cites the
canonical config file (`docker-compose.yml`, `docker/postgres/init/01-roles.sql`,
`scripts/gen-test-rsa-pem.sh`) it derives from, and prefers copy-pasteable shell
+ SQL commands over abstract guidance.

GameKit's design constraint (`CLAUDE.md`) is **self-hosted, zero cloud-service
dependencies**. Every page here works against hardware you control; nothing
assumes AWS / Azure / GCP / SaaS APIs at runtime.

---

## Reading order

If this is your first GameKit deployment, read in this order:

1. **[`postgres-roles.md`](postgres-roles.md)** — understand the 3-role split
   (`gamekit_owner` / `gamekit_app` / `gamekit_reader`) before you provision
   anything. Every other doc assumes the role layout is in place.
2. **[`redis-aof.md`](redis-aof.md)** — Redis AOF + memory-policy contract.
   Explains why the shipped flags (`--appendonly yes --appendfsync everysec
   --maxmemory-policy noeviction`) are non-negotiable.
3. Pick the deployment recipe that matches your topology:
   - **[`container.md`](container.md)** — Docker / docker-compose / Kubernetes.
     The shipped `docker-compose.yml` IS the canonical recipe; this doc walks
     through extending it for production.
   - **[`bare-metal.md`](bare-metal.md)** — Postgres + Redis + your ASP.NET app
     directly on Linux hosts, with systemd + nginx/Caddy.
   - **[`air-gapped.md`](air-gapped.md)** — deploy with no internet egress.
     Covers offline NuGet feeds, mirrored container images, and the auth-
     without-Steam/Discord posture.
4. **[`jwt-keys.md`](jwt-keys.md)** — RSA signing-key generation, storage
   hardening, and the rotation procedure. Read before going live.
5. **[`migrations-runbook.md`](migrations-runbook.md)** — per-package
   migration history tables + advisory locks. Read once now; reference when a
   deploy hangs.
6. **[`disaster-recovery.md`](disaster-recovery.md)** — backup + restore for
   Postgres + Redis, plus the GameKit-specific concerns (ticker pause,
   idempotency replay, refresh-token sanity, JWT key out-of-band backup).

---

## Recipe index

| Doc                                                  | One-line summary                                                                              |
|------------------------------------------------------|------------------------------------------------------------------------------------------------|
| [`bare-metal.md`](bare-metal.md)                     | Install Postgres 17.9 + Redis 8.6.2 + your app on Linux; systemd unit + nginx/Caddy TLS.       |
| [`container.md`](container.md)                       | Compose your app on top of the shipped `docker-compose.yml`; Kubernetes Deployment/Service.    |
| [`air-gapped.md`](air-gapped.md)                     | Offline NuGet feed, mirrored container images, Guest+Password-only auth (no Steam/Discord).   |
| [`postgres-roles.md`](postgres-roles.md)             | The 3-role bootstrap (`gamekit_owner` / `gamekit_app` / `gamekit_reader`) + password rotation. |
| [`redis-aof.md`](redis-aof.md)                       | AOF (`appendonly yes`, `appendfsync everysec`) + `maxmemory-policy noeviction` rationale.      |
| [`jwt-keys.md`](jwt-keys.md)                         | RSA 2048 key generation, file-mode hardening, `kid` rotation, emergency rotation.              |
| [`disaster-recovery.md`](disaster-recovery.md)       | `pg_dump`/`pg_restore` cadence, Redis AOF backup, restore drills, ticker-pause window.         |
| [`migrations-runbook.md`](migrations-runbook.md)     | Per-package `__ef_migrations_*` history + advisory locks; debugging stuck migrations.          |
| [`multi-replica.md`](multi-replica.md)               | Shared Data Protection key ring + Redis SignalR backplane + sticky sessions for multi-replica. |

---

## What is NOT in this guide

GameKit does not own:

- **Game-server hosting / orchestration** — use Agones, Multiplay, or your own
  fleet manager.
- **DDoS mitigation** — network-edge concern.
- **Anti-cheat** — engine / game concern.
- **Real-time netcode** — use Mirror, Fish-Net, WebSockets, or custom.
- **Storefront / billing / entitlements** — storefronts own this.

For those concerns consult the relevant vendor's runbooks; nothing here will
help.

---

## Conventions used in this guide

- **Shell snippets** assume Debian 12 / Ubuntu 24.04 LTS as the example
  distro. Adapt paths and package manager commands for RHEL / Arch / etc.
- **Connection strings** use `prod-postgres.internal` and `prod-redis.internal`
  as placeholder hostnames; substitute your own.
- **Passwords + secrets** are shown as shell variables (`$GAMEKIT_DB_APP_PASSWORD`)
  to make clear that they should never be inlined into config files.
- **SQL** assumes Postgres 17.9; SQL syntax is identical for 16 + 17 but other
  versions are not currently supported.
- **YAML indentation** is 2 spaces (matches the shipped `docker-compose.yml`).
- **The literal version pins** (`postgres:17.9`, `redis:8.6.2`, `aspnetcore-runtime-10.0`)
  match what CI exercises against. Floating past them is at the operator's
  risk; pin upgrades go through `Directory.Packages.props` and a full
  release-train rebuild (see `CLAUDE.md` MinVer note).

---

## Feedback

This is operator-targeted prose. If something in here is wrong, ambiguous, or
contradicts what GameKit actually does, file an issue against the repo with the
URL of the offending section. Documentation gaps are bugs.
