<!--
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 GameKit contributors
-->

# Platformer3D — GameKit Demo

A 3D multiplayer time-trial platformer that demonstrates the full GameKit stack
in a single self-hosted deployment:

- **Guest sign-in** (no email / OAuth required)
- **Lobby + ready-check** (SignalR, 1v1 parties)
- **Matchmaking** (custom `BestTimeMatchmakingStrategy` — best-time proximity bracket)
- **Timed 3D run** (three.js WebGL client, authoritative server result via WebSocket)
- **Custom ranking** (`TimeMarginRankingAlgorithm` — fixed-delta Elo, exact tie = draw)
- **Admin console** (`/admin`) — live players / matches / sessions / leaderboard

All assets are vendored locally. The stack makes **zero runtime outbound cloud
or CDN calls** and needs **zero cloud credentials**.

---

## Quick Start (Docker Compose — one command)

```bash
# From the repo root:
docker compose -f samples/Platformer3D/docker-compose.yml up --build
```

Wait for all three services to become healthy, then verify the app is ready:

```bash
curl http://localhost:8080/health/ready
# Expected: 200 OK with body {"status":"Healthy"} or similar
```

Open `http://localhost:8080` in a Chromium / Firefox browser to play.

Open `http://localhost:8080/admin` for the admin console.

> **Note:** The first startup runs EF Core AutoMigrate, which may take 10–20 s.
> The app service's `start_period: 60s` healthcheck window covers this.

### Port mapping

| Service  | Internal | Published to host | Notes                          |
|----------|----------|-------------------|--------------------------------|
| `app`    | 8080     | **8080**          | HTTP — game client + API       |
| `postgres` | 5432   | —                 | Internal only (must-NOT)       |
| `redis`  | 6379     | —                 | Internal only (must-NOT)       |

Postgres and Redis are reachable **only** by the `app` container over the
internal Docker bridge network. They are never exposed to the host.

---

## Offline Tarball (air-gapped transfer)

To transfer the entire demo to a machine without internet access:

### 1. Build and save all images

```bash
# From the repo root — build the app image first
docker compose -f samples/Platformer3D/docker-compose.yml build

# Save all images (app + postgres + redis) to a compressed tarball
docker save \
  $(docker compose -f samples/Platformer3D/docker-compose.yml images -q) \
  | gzip > platformer3d-offline.tar.gz
```

### 2. Transfer and load on the offline machine

```bash
# Transfer platformer3d-offline.tar.gz to the target machine, then:
docker load < platformer3d-offline.tar.gz

# Copy the docker-compose.yml and docker/ init directory to the target machine,
# then start the stack (no internet access required):
docker compose -f samples/Platformer3D/docker-compose.yml up
```

The tarball contains the app image, `postgres:17.9`, and `redis:8.6.2`.

---

## Development Setup (without Docker)

For local development with a host-installed Postgres and Redis:

```bash
# Generate dev RSA keypair (one-time setup):
mkdir -p samples/Platformer3D/keys
openssl genrsa -out samples/Platformer3D/keys/dev-priv.pem 2048
openssl rsa -in samples/Platformer3D/keys/dev-priv.pem -pubout \
    -out samples/Platformer3D/keys/dev-pub.pem

# Run (reads appsettings.Development.json — points at localhost Postgres port 5433):
dotnet run --project samples/Platformer3D/Platformer3D.csproj
```

> `appsettings.Development.json` uses port `5433` to avoid conflicting with a
> developer's local Postgres on the default port 5432.

---

## Architecture Overview

```
Browser (WebGL / three.js)
  +-- GET /                 → wwwroot/index.html (static, no CDN)
  +-- GET /js/three.module.js → wwwroot/js/ (vendored MIT)
  +-- POST /auth/login/guest → GuestOAuthProvider → JWT + refresh
  +-- SignalR /hubs/lobby   → party invite, ready-check
  +-- POST /api/mm/queue    → matchmaking ticker
  +-- WebSocket /ws/game/{matchId} → PlatformerGameServerService (IHostedService)
  +-- GET /admin/*          → Blazor Server admin console

Docker network (internal only):
  app → postgres:5432  (EF Core, GameKit schema)
  app → redis:6379     (matchmaking queue, SignalR backplane, presence)
```

---

## DEMO ONLY — Security Notice

- **RSA keys:** Generated fresh during `docker build` and baked into the image.
  These keys are single-use demo artifacts — do not rely on them for security.
  Production deployments must inject key material via Docker secrets or environment
  variables.
- **Database passwords:** The passwords in `docker-compose.yml` (`demo_owner_pw`,
  `demo_app_pw`) are demo-grade. Do not use in production.
- **Guest tokens:** Stored in browser `localStorage` in the demo client. This is
  intentionally insecure and documented as a demo pattern in `wwwroot/index.html`.

---

## License

GPL-3.0-or-later. See `LICENSE` at the repo root.

three.js is vendored under the MIT license. See `THIRD-PARTY-NOTICES.md` at the
repo root for the full license text.
