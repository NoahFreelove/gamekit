<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Container deployment (Docker + Kubernetes)

The shipped `docker-compose.yml` IS the canonical container recipe for the
Postgres + Redis stack. Your GameKit-consuming app — the ASP.NET Core process that
calls `services.AddGameKit().AddAuth().AddRankings()...` — is your own container;
this doc walks the operator through composing the two.

---

## docker-compose: the stack as shipped

The repo-root `docker-compose.yml` provisions `postgres:17.9` + `redis:8.6.2` with
the GameKit-specific knobs (3-role bootstrap, AOF on, healthchecks). Read it once
before extending it:

```bash
cat docker-compose.yml
```

Key shape:

```yaml
services:
  postgres:
    image: postgres:17.9
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres_bootstrap_dev_only   # OVERRIDE in production
      POSTGRES_DB: postgres
    volumes:
      - gamekit-postgres-data:/var/lib/postgresql/data
      - ./docker/postgres/init:/docker-entrypoint-initdb.d:ro
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d gamekit"]
      interval: 10s
    shm_size: "256mb"

  redis:
    image: redis:8.6.2
    command:
      - "redis-server"
      - "--appendonly"
      - "yes"
      - "--appendfsync"
      - "everysec"
      - "--maxmemory-policy"
      - "noeviction"
      # ...
    volumes:
      - gamekit-redis-data:/data
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
```

The init script bind-mount (`./docker/postgres/init:/docker-entrypoint-initdb.d:ro`)
is what creates the 3 roles + database + schema on first volume init. See
[`postgres-roles.md`](postgres-roles.md) for the role layout.

---

## Composing your app on top

Your GameKit-consuming app is just another `service:` in compose. Two patterns
work; pick the one that matches your CI:

### Pattern A — multi-stage Dockerfile

`Dockerfile` (sibling of your `MyGame.csproj`):

```dockerfile
# syntax=docker/dockerfile:1.6
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy and restore first for layer caching.
COPY *.sln .
COPY src/MyGame/MyGame.csproj src/MyGame/
RUN dotnet restore src/MyGame/MyGame.csproj

# Copy sources, publish.
COPY . .
RUN dotnet publish src/MyGame/MyGame.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime image — ASP.NET Core 10 only (no SDK).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user — GameKit does not need root and writing files as root in a
# container is a privilege-escalation hazard.
RUN groupadd -r -g 10001 mygame && useradd -r -u 10001 -g mygame mygame
USER mygame

COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyGame.dll"]
```

`docker-compose.override.yml` (sits alongside the GameKit-shipped `docker-compose.yml`):

```yaml
services:
  mygame:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "5000:8080"
    environment:
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__GameKit: "Host=postgres;Port=5432;Database=gamekit;Username=gamekit_app;Password=${GAMEKIT_DB_APP_PASSWORD}"
      ConnectionStrings__Redis:   "redis:6379"
      GameKit__Auth__Jwt__SigningKeyPath: "/var/lib/mygame/jwt-priv.pem"
      GameKit__Auth__Jwt__ValidationKeyPath: "/var/lib/mygame/jwt-pub.pem"
    volumes:
      - ./secrets/jwt:/var/lib/mygame:ro
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
```

Bring the whole stack up:

```bash
docker compose up -d
docker compose logs -f mygame
```

`depends_on: condition: service_healthy` is load-bearing — without it, your app
starts racing the Postgres init script and fails its first migration with
`role "gamekit_app" does not exist` or similar.

### Pattern B — single-image deploy + external DB

If you run Postgres + Redis on managed/dedicated hosts (RDS, ElastiCache, your own
bare-metal cluster), only your app needs containerizing:

```bash
docker build -t mygame:1.0.0 .
docker run -d \
    --name mygame \
    --restart unless-stopped \
    -p 5000:8080 \
    -e ASPNETCORE_URLS=http://+:8080 \
    -e ConnectionStrings__GameKit="Host=prod-postgres.internal;Database=gamekit;Username=gamekit_app;Password=$GAMEKIT_DB_APP_PASSWORD" \
    -e ConnectionStrings__Redis="prod-redis.internal:6379" \
    -v /srv/mygame/jwt:/var/lib/mygame:ro \
    mygame:1.0.0
```

---

## Production-readiness checklist for docker-compose

The shipped compose file is dev-tuned. Before pointing it at production:

| Concern              | Dev default                                              | Production change                                                                 |
|----------------------|----------------------------------------------------------|-----------------------------------------------------------------------------------|
| Postgres password    | `postgres_bootstrap_dev_only` (literal in compose)       | Inject via `.env` or secrets manager; never commit                                |
| Postgres role passwords | `gamekit_owner_dev` / `gamekit_app_dev` / `gamekit_reader_dev` (literal in init script) | Re-provision per [`postgres-roles.md`](postgres-roles.md)                         |
| Port `5432:5432`     | Published to host (convenient for `psql` from laptop)    | Remove the host-published port; keep Postgres on the compose-internal network only |
| Port `6379:6379`     | Published to host                                        | Same — remove the host port; access Redis only from app containers                |
| `restart: unless-stopped` | OK                                                  | OK; consider `always` if you want Postgres up even after a docker daemon restart  |
| Image tags           | Pinned (`postgres:17.9`, `redis:8.6.2`)                  | Keep pinned; never use `:latest`                                                  |
| Volumes              | Named volumes (`gamekit-postgres-data`)                  | Switch to bind-mounts under your backup directory                                 |
| `shm_size: "256mb"`  | Postgres-recommended minimum for parallel queries        | Tune upward (`1g`+) if `EXPLAIN ANALYZE` reports temp-file spillage on big joins  |
| Health checks        | `pg_isready` + `redis-cli ping`                          | Keep; add a third for your app (`GET /health` once you implement it)              |

Concrete production `docker-compose.prod.yml`:

```yaml
services:
  postgres:
    image: postgres:17.9
    restart: always
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD_FILE: /run/secrets/postgres_superuser
      POSTGRES_DB: postgres
    volumes:
      - /srv/gamekit/postgres-data:/var/lib/postgresql/data
      - ./docker/postgres/init:/docker-entrypoint-initdb.d:ro
    # NOTE: no 'ports:' block — Postgres is only reachable from the compose network.
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d gamekit"]
      interval: 10s
    shm_size: "1gb"
    secrets:
      - postgres_superuser

  redis:
    image: redis:8.6.2
    restart: always
    command:
      - "redis-server"
      - "--appendonly"
      - "yes"
      - "--appendfsync"
      - "everysec"
      - "--maxmemory-policy"
      - "noeviction"
      - "--maxmemory"
      - "8gb"
    volumes:
      - /srv/gamekit/redis-data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s

secrets:
  postgres_superuser:
    file: /etc/gamekit/postgres-superuser.txt
```

---

## Kubernetes

GameKit has no Kubernetes-specific code path; the same .NET app runs unchanged.
A minimal `Deployment` + `Service` + `ConfigMap`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: mygame-config
data:
  ASPNETCORE_URLS: "http://+:8080"
  ConnectionStrings__Redis: "redis.gamekit.svc.cluster.local:6379"
---
apiVersion: v1
kind: Secret
metadata:
  name: mygame-secrets
type: Opaque
stringData:
  ConnectionStrings__GameKit: "Host=postgres.gamekit.svc.cluster.local;Database=gamekit;Username=gamekit_app;Password=REDACTED"
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mygame
spec:
  replicas: 3
  selector:
    matchLabels: { app: mygame }
  template:
    metadata:
      labels: { app: mygame }
    spec:
      containers:
      - name: mygame
        image: registry.example.com/mygame:1.0.0
        imagePullPolicy: IfNotPresent
        ports:
        - containerPort: 8080
        envFrom:
        - configMapRef: { name: mygame-config }
        - secretRef:    { name: mygame-secrets }
        volumeMounts:
        - name: jwt-keys
          mountPath: /var/lib/mygame
          readOnly: true
        readinessProbe:
          httpGet: { path: /health, port: 8080 }
          initialDelaySeconds: 5
        livenessProbe:
          httpGet: { path: /health, port: 8080 }
          initialDelaySeconds: 30
        resources:
          requests: { cpu: "250m", memory: "256Mi" }
          limits:   { cpu: "1000m", memory: "1Gi"   }
      volumes:
      - name: jwt-keys
        secret:
          secretName: mygame-jwt-keys
---
apiVersion: v1
kind: Service
metadata:
  name: mygame
spec:
  type: ClusterIP
  selector: { app: mygame }
  ports:
  - port: 80
    targetPort: 8080
```

**Multi-replica caveat:** the matchmaking ticker uses a Redis distributed lock
(`SET NX PX`) for leader election, so running multiple replicas of your app is
safe — only one replica's ticker thread holds the lock at a time. Migrations also
serialize via Postgres advisory locks (see [`migrations-runbook.md`](migrations-runbook.md)),
so multiple replicas starting simultaneously will not collide on `CREATE TABLE`.

---

## Common mistakes to avoid

- **Re-publishing Postgres / Redis ports in production.** The shipped compose
  file maps `5432:5432` + `6379:6379` for laptop convenience. In production the
  databases should be reachable **only** from the compose network — never from
  the public internet.
- **Using `:latest` tags.** GameKit pins Postgres 17.9 and Redis 8.6.2 because
  these are the versions exercised in CI. Floating to `:latest` introduces an
  untested-version risk.
- **Skipping `depends_on: condition: service_healthy`.** Without it, your app
  starts before Postgres has run its init script and migrations fail with
  cryptic role-not-found errors.
- **Mounting the Postgres data volume across containers.** Two Postgres servers
  pointed at the same `PGDATA` corrupts the WAL. Each Postgres instance gets
  its own volume.
- **Putting the JWT private key in the image.** Build it into a Secret /
  Kubernetes Secret / bind-mount; never `COPY jwt-priv.pem .` in the Dockerfile.

---

## Related runbooks

- [`bare-metal.md`](bare-metal.md) — same stack without containers.
- [`postgres-roles.md`](postgres-roles.md) — the 3-role bootstrap script the
  postgres container runs on first start.
- [`redis-aof.md`](redis-aof.md) — why the Redis flags are what they are.
- [`jwt-keys.md`](jwt-keys.md) — how to provision the signing keys mounted into
  the container.
