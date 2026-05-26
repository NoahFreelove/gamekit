<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Bare-metal deployment

GameKit was built to run **without containers** on hardware operators control.
This doc walks through provisioning Postgres 17.9 + Redis 8.6.2 + your
GameKit-consuming app directly on a Linux host, with systemd as the supervisor
and nginx (or Caddy) terminating TLS.

If you prefer Docker / Kubernetes, jump to [`container.md`](container.md). The
underlying Postgres + Redis configuration is the same; only the packaging differs.

---

## Prerequisites

| Component        | Version           | How to install (Debian/Ubuntu)                                                              |
|------------------|-------------------|---------------------------------------------------------------------------------------------|
| OS               | Debian 12 or Ubuntu 24.04 LTS (any modern systemd Linux works) | n/a                                                                                         |
| .NET runtime     | 10.0.x (LTS)      | See ".NET runtime" below — Microsoft package feed                                            |
| Postgres         | 17.9              | `pgdg` apt repo — `https://apt.postgresql.org/pub/repos/apt`                                |
| Redis            | 8.6.2             | `packages.redis.io` apt repo — `https://packages.redis.io/deb`                              |
| OpenSSL          | 3.x (any modern)  | `apt-get install openssl` (usually pre-installed)                                            |
| nginx **or** Caddy | nginx 1.24+ or Caddy 2.7+ | `apt-get install nginx` or follow https://caddyserver.com/docs/install                       |

The repo's `global.json` pins the .NET SDK at `10.0.106`. The runtime version
(`10.0.x` shared framework) is what the deployed host needs — not the SDK. Match
the **major.minor** of the SDK you built with (10.0.x is the only valid choice for
the v1 release train).

---

## .NET runtime

Install the ASP.NET Core 10 runtime (not the SDK — production hosts should not have
the SDK installed):

```bash
# Add Microsoft's package feed (Debian 12 / Ubuntu 24.04).
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O /tmp/ms-prod.deb
sudo dpkg -i /tmp/ms-prod.deb
sudo apt-get update

# Install ASP.NET Core 10 runtime only.
sudo apt-get install -y aspnetcore-runtime-10.0

# Confirm.
dotnet --list-runtimes
# Expect lines starting with:
#   Microsoft.AspNetCore.App 10.0.x ...
#   Microsoft.NETCore.App    10.0.x ...
```

Production hosts only need `aspnetcore-runtime-10.0`. If you also install
`dotnet-runtime-10.0` separately you waste disk; the ASP.NET runtime is a
superset.

---

## Postgres 17.9

```bash
# Add the PostgreSQL apt repo (pgdg).
sudo install -d /usr/share/postgresql-common/pgdg
sudo curl -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    --fail https://www.postgresql.org/media/keys/ACCC4CF8.asc
sudo sh -c 'echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] \
    https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" \
    > /etc/apt/sources.list.d/pgdg.list'
sudo apt-get update

# Install exactly 17.
sudo apt-get install -y postgresql-17

# Confirm and start.
sudo systemctl enable --now postgresql@17-main
sudo -u postgres psql -c "SELECT version();"
# Expect: PostgreSQL 17.9 (or .x for the patch level) ...
```

### GameKit-specific Postgres tuning

The defaults postgres 17 ships are conservative. Edit
`/etc/postgresql/17/main/postgresql.conf`:

```ini
# Listen on the host's private interface only — never on 0.0.0.0 in production.
listen_addresses = '10.0.0.5'                # your private NIC IP

# Memory (tune to ~25% of host RAM for shared_buffers; the rest for work_mem).
shared_buffers          = 4GB                # for a 16 GB host
effective_cache_size    = 12GB
work_mem                = 32MB
maintenance_work_mem    = 512MB

# WAL — adjust if you observe lots of "WAL writer woken up" log spam.
wal_buffers             = 16MB
checkpoint_completion_target = 0.9
max_wal_size            = 4GB
min_wal_size            = 1GB

# Logging — enable slow-query + connection logging.
log_min_duration_statement = 250ms
log_connections         = on
log_disconnections      = on
log_lock_waits          = on
```

Edit `/etc/postgresql/17/main/pg_hba.conf` to restrict access to your private
subnet:

```
# TYPE  DATABASE  USER             ADDRESS            METHOD
host    gamekit   gamekit_owner    10.0.0.0/24        scram-sha-256
host    gamekit   gamekit_app      10.0.0.0/24        scram-sha-256
host    gamekit   gamekit_reader   10.0.0.0/24        scram-sha-256
```

Reload Postgres after edits:

```bash
sudo systemctl reload postgresql@17-main
```

### Provision the 3-role layout

Run `docker/postgres/init/01-roles.sql` (or its production-password equivalent —
see [`postgres-roles.md`](postgres-roles.md)) against the fresh server:

```bash
sudo -u postgres psql -v ON_ERROR_STOP=1 -f /path/to/gamekit/docker/postgres/init/01-roles.sql
```

The script is idempotent — re-running it is safe. After it succeeds, the
`gamekit_owner` + `gamekit_app` + `gamekit_reader` roles exist with the correct
grants, and the `gamekit` database + `gamekit` schema are created.

### Kernel tuning (optional)

For high-throughput workloads (10k+ concurrent connections via a connection
pooler), `/etc/sysctl.d/30-postgres.conf`:

```ini
vm.overcommit_memory = 2
vm.swappiness        = 1
vm.dirty_background_ratio = 5
vm.dirty_ratio       = 10
kernel.shmmax        = 4294967295
```

Apply with `sudo sysctl --system`. The `vm.overcommit_memory = 2` setting is
the Postgres-documented recommendation — without it, an OOM killer episode can
kill the postmaster instead of just a backend.

---

## Redis 8.6.2

```bash
# Add the official Redis apt repo.
sudo apt-get install -y lsb-release curl gpg
curl -fsSL https://packages.redis.io/gpg | sudo gpg --dearmor -o /usr/share/keyrings/redis-archive-keyring.gpg
echo "deb [signed-by=/usr/share/keyrings/redis-archive-keyring.gpg] \
    https://packages.redis.io/deb $(lsb_release -cs) main" \
    | sudo tee /etc/apt/sources.list.d/redis.list
sudo apt-get update

# Install Redis 8.6.2 (or the closest available 8.x — verify with apt-cache madison redis).
sudo apt-get install -y redis-server

# Confirm version.
redis-server --version
# Expect: Redis server v=8.6.2 ...
```

Edit `/etc/redis/redis.conf` so it matches the AOF + memory-policy contract
(detailed rationale in [`redis-aof.md`](redis-aof.md)):

```ini
bind 10.0.0.5                # private NIC only
port 6379
protected-mode yes

appendonly yes
appendfsync everysec
maxmemory 8gb
maxmemory-policy noeviction

# Snapshots in addition to AOF (fast cold-start path).
save 3600 1
save 300 100
save 60 10000

# Persistence directory — give it its own filesystem if possible.
dir /var/lib/redis

# Require a password for any client connection.
requirepass <REDIS_PASSWORD_HERE>
```

Set the password via:

```bash
REDIS_PASSWORD=$(openssl rand -base64 32)
# Persist to your secrets store and update redis.conf 'requirepass' line.
```

Start the service:

```bash
sudo systemctl enable --now redis-server
redis-cli -a "$REDIS_PASSWORD" ping
# Expect: PONG
```

---

## Your GameKit-consuming app

Publish your app for `linux-x64`:

```bash
dotnet publish src/MyGame/MyGame.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /tmp/mygame-publish
```

Copy the published output to the deploy host (`/srv/mygame`), then install a
systemd unit at `/etc/systemd/system/mygame.service`:

```ini
[Unit]
Description=MyGame (GameKit-backed ASP.NET Core service)
After=network-online.target postgresql@17-main.service redis-server.service
Wants=network-online.target
Requires=postgresql@17-main.service redis-server.service

[Service]
Type=notify
User=mygame
Group=mygame
WorkingDirectory=/srv/mygame
ExecStart=/usr/bin/dotnet /srv/mygame/MyGame.dll
Restart=on-failure
RestartSec=5
KillSignal=SIGINT
TimeoutStopSec=30

# Environment — load from EnvironmentFile so secrets live outside the unit.
EnvironmentFile=/etc/mygame/env
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000

# Hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/srv/mygame/logs
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
LockPersonality=true

[Install]
WantedBy=multi-user.target
```

`/etc/mygame/env` (mode `0600`, owned by `mygame:mygame`):

```bash
ConnectionStrings__GameKit=Host=10.0.0.5;Port=5432;Database=gamekit;Username=gamekit_app;Password=<APP_PW>
ConnectionStrings__Redis=10.0.0.5:6379,password=<REDIS_PW>
GameKit__Auth__Jwt__SigningKeyPath=/srv/mygame/keys/jwt-priv.pem
GameKit__Auth__Jwt__ValidationKeyPath=/srv/mygame/keys/jwt-pub.pem
```

Create the system user, deploy, enable:

```bash
sudo useradd --system --home-dir /srv/mygame --shell /usr/sbin/nologin mygame
sudo chown -R mygame:mygame /srv/mygame
sudo chmod 0640 /etc/mygame/env
sudo chown root:mygame /etc/mygame/env

sudo systemctl daemon-reload
sudo systemctl enable --now mygame
sudo systemctl status mygame
journalctl -u mygame -f
```

---

## Reverse proxy (TLS termination)

The ASP.NET process should bind to `127.0.0.1:5000` (or a UNIX socket); a reverse
proxy handles TLS and the public-internet edge.

### nginx

`/etc/nginx/sites-available/mygame`:

```nginx
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name mygame.example.com;

    ssl_certificate     /etc/letsencrypt/live/mygame.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mygame.example.com/privkey.pem;
    ssl_protocols TLSv1.3 TLSv1.2;
    ssl_ciphers HIGH:!aNULL:!MD5;

    client_max_body_size 1m;        # GameKit endpoints are small JSON payloads
    proxy_read_timeout    30s;
    proxy_send_timeout    30s;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_set_header   Connection        "";
    }
}

server {
    listen 80;
    listen [::]:80;
    server_name mygame.example.com;
    return 301 https://$host$request_uri;
}
```

Enable + reload:

```bash
sudo ln -s /etc/nginx/sites-available/mygame /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

For the `X-Forwarded-For` header to be honored by ASP.NET, configure
`ForwardedHeadersOptions` in your `Program.cs` per
[the ASP.NET reverse-proxy guide](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer).

### Caddy (simpler)

`/etc/caddy/Caddyfile`:

```caddyfile
mygame.example.com {
    reverse_proxy 127.0.0.1:5000
    encode gzip
    log {
        output file /var/log/caddy/mygame.log
    }
}
```

Caddy provisions Let's Encrypt certificates automatically — no manual `certbot`
step. Reload with `sudo systemctl reload caddy`.

---

## Operational checks

```bash
# Service health.
sudo systemctl status mygame postgresql@17-main redis-server

# App logs (real-time).
journalctl -u mygame -f

# Postgres reachable from app's perspective.
sudo -u mygame psql -h 10.0.0.5 -U gamekit_app -d gamekit -c "SELECT 1;"

# Redis reachable from app's perspective.
redis-cli -h 10.0.0.5 -a "$REDIS_PASSWORD" ping

# HTTP reachable through nginx/caddy.
curl -sf https://mygame.example.com/openapi/v1.json | head -1
```

---

## Common mistakes to avoid

- **Running the app as root.** Use a system user (`mygame`). The systemd unit's
  hardening directives (`NoNewPrivileges`, `ProtectSystem=strict`) only help if
  the unit's `User=` is non-root.
- **Skipping `pg_hba.conf` hardening.** The default `pg_hba.conf` on a fresh
  postgres install may allow `host all all 0.0.0.0/0 md5` (or similar). Always
  restrict to your private subnet + scram-sha-256.
- **Forgetting `requirepass` on Redis.** A Redis with `bind 0.0.0.0` and no
  password is a worm magnet (look up "Redis crypto miner" — it's an entire
  attack genre).
- **Binding the app to `0.0.0.0:5000`.** Bind to `127.0.0.1:5000` and let nginx
  handle the public edge. Anything on `0.0.0.0` without a firewall is exposed.
- **Installing the SDK on production.** Use `aspnetcore-runtime-10.0` — the SDK
  ships compilers/tools that have no business on a production host.

---

## Related runbooks

- [`postgres-roles.md`](postgres-roles.md) — the 3-role provisioning script.
- [`redis-aof.md`](redis-aof.md) — Redis persistence + memory tuning.
- [`jwt-keys.md`](jwt-keys.md) — RSA key provisioning for `GameKit.Auth`.
- [`disaster-recovery.md`](disaster-recovery.md) — backup + restore.
- [`migrations-runbook.md`](migrations-runbook.md) — how to apply migrations
  during a bare-metal deploy.
