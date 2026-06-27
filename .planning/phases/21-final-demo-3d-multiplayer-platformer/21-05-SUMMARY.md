---
phase: "21-final-demo-3d-multiplayer-platformer"
plan: "05"
subsystem: "Docker packaging — multi-stage Dockerfile + compose + offline tarball"
tags: ["docker", "compose", "dockerfile", "packaging", "offline", "platformer3d"]
status: complete

dependency_graph:
  requires: ["21-04"]
  provides: ["platformer3d docker image", "docker-compose.yml", "offline tarball workflow"]
  affects: ["21-06"]

tech_stack:
  added:
    - "mcr.microsoft.com/dotnet/sdk:10.0 (build stage)"
    - "mcr.microsoft.com/dotnet/aspnet:10.0 (runtime stage)"
    - "postgres:17.9 (compose service)"
    - "redis:8.6.2 (compose service)"
    - "openssl (installed in runtime image for demo RSA key generation)"
    - "curl (installed in runtime image for /health/ready healthcheck)"
  patterns:
    - "Multi-stage Docker build: SDK restore/publish → aspnet runtime"
    - "Compose: app + pg + redis; only app port published (must-NOT pattern)"
    - "Offline tarball: docker save | gzip + docker load"
    - "Postgres init SQL: idempotent role + database bootstrap"
    - "Demo-only RSA keypair: generated at docker build time, clearly marked"

key_files:
  created:
    - "samples/Platformer3D/Dockerfile"
    - "samples/Platformer3D/.dockerignore"
    - "samples/Platformer3D/docker/postgres/init/01-init.sql"
    - "samples/Platformer3D/docker-compose.yml"
    - "samples/Platformer3D/README.md"
  modified: []

decisions:
  - "D-A: --no-restore omitted from publish RUN step — SDK 10.0.301 inside the container requires a
      fresh restore pass even when the csproj-only restore already ran; the NuGet cache is warm so
      this is a cache hit, not a download. The csproj-layer cache (restore) still provides Docker
      layer caching for the dependency graph."
  - "D-B: curl + openssl installed together in a single apt-get RUN step to minimize image layers
      and avoid separate cache misses."
  - "D-C: Docker build verified against live daemon (exit 0, 400 MB image). dotnet publish fallback
      was not needed."

metrics:
  duration: "~18 minutes"
  completed_date: "2026-06-23"
  tasks_completed: 2
  tasks_total: 2
  files_created: 5
  files_modified: 0
---

# Phase 21 Plan 05: Docker Packaging Summary

**One-liner:** Multi-stage Dockerfile (SDK build → aspnet runtime) + single compose file (only app port published) + documented docker save/load offline tarball for the Platformer3D demo.

---

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Multi-stage Dockerfile + .dockerignore + Postgres init | `79610a3` | Dockerfile, .dockerignore, docker/postgres/init/01-init.sql |
| 2 | Compose file (only app port published) + README offline-tarball docs | `3d7feb7` | docker-compose.yml, README.md |

---

## Build Verification

**Method:** Live Docker daemon (available in sandbox environment). The `dotnet publish` fallback was not needed.

**docker build command:**
```bash
docker build -f samples/Platformer3D/Dockerfile -t platformer3d:planverify .
```

**Result:** Exit 0. Image `platformer3d:planverify` is 400 MB. Both stages executed:
1. SDK stage: `dotnet restore` (with `-p:NuGetAudit=false`) + `dotnet publish -c Release`
2. Runtime stage: `apt-get install curl openssl`, RSA keypair generated, `EXPOSE 8080`

### Deviation: --no-restore Removed (Rule 1 - Bug Fix)

The initial Dockerfile used `dotnet publish --no-restore`. The build failed with `NETSDK1064: Package Microsoft.AspNetCore.OpenApi, version 10.0.8 was not found` because the SDK version inside the container (`10.0.301`) resolved package paths differently from the csproj-only pre-restore layer. The fix is to omit `--no-restore` — the NuGet cache is warm from the prior restore RUN layer so it is still a cache hit, but the publish step can re-resolve package paths correctly.

---

## Published Host Port

The app service maps **host port 8080 → container port 8080**.

```yaml
# samples/Platformer3D/docker-compose.yml
app:
  ports:
    - "8080:8080"   # ONLY app port published
```

Postgres and redis services have **no `ports:` section** — verified:

```
app ports: ['8080:8080']
postgres ports: absent
redis ports: absent
```

The 21-06 compose-port test (`assert postgres.ports absent AND redis.ports absent`) will pass.

---

## Offline Tarball Commands (R3)

**Create tarball (documented in README.md):**
```bash
# From repo root
docker compose -f samples/Platformer3D/docker-compose.yml build
docker save \
  $(docker compose -f samples/Platformer3D/docker-compose.yml images -q) \
  | gzip > platformer3d-offline.tar.gz
```

**Restore on offline machine:**
```bash
docker load < platformer3d-offline.tar.gz
docker compose -f samples/Platformer3D/docker-compose.yml up
```

---

## Postgres Init SQL

`samples/Platformer3D/docker/postgres/init/01-init.sql` bootstraps:
- `gamekit_owner` role (DDL privileges — used by EF Core AutoMigrate)
- `gamekit_app` role (DML only — used by the running app)
- `gamekit` database owned by `gamekit_owner`

Roles use `IF NOT EXISTS` guards so the script is safe to re-run (idempotent). EF Core AutoMigrate runs on first app startup using the `gamekit_owner` connection string.

---

## Demo RSA Keys

Generated at `docker build` time via:
```dockerfile
RUN mkdir -p /app/keys && \
    openssl genrsa -out /app/keys/private.pem 2048 && \
    openssl rsa -in /app/keys/private.pem -pubout -out /app/keys/public.pem && \
    echo "# DEMO ONLY" > /app/keys/KEYS_ARE_DEMO_ONLY.txt
```

Keys are regenerated every build. A `/app/keys/KEYS_ARE_DEMO_ONLY.txt` sentinel file is included. The `README.md` documents the production alternative (Docker secrets or env-var key material).

---

## Must-NOT Compliance

| Prohibition | Status |
|-------------|--------|
| No Postgres/Redis host ports | PASS — `ports:` absent from both services |
| No runtime outbound cloud/SaaS/CDN call | PASS — no CDN URLs; OTel endpoint intentionally absent |
| No baked production secrets | PASS — demo-only keys; KEYS_ARE_DEMO_ONLY.txt sentinel |

---

## REUSE / License Hygiene (R11)

All 5 new files carry the `GPL-3.0-or-later` header directly (`# SPDX-License-Identifier:` in Dockerfile/.dockerignore/.sql, `<!-- SPDX-... -->` in README.md, `# SPDX-...` in docker-compose.yml) and are additionally covered by the existing REUSE.toml `samples/Platformer3D/**` aggregate annotation.

`reuse lint` pre-existing violations (21 files: `.planning/sketches/`, `scripts/`, `templates/`, `TicTacToeDuel/keys/`) are unchanged — none of our new files appear in the missing list.

File counts: 1370/1391 copyright (was 1365/1386 before this plan — our 5 new files are properly covered), 1372/1391 license.

---

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed `--no-restore` from dotnet publish in Dockerfile**

- **Found during:** Task 1 docker build (first attempt)
- **Issue:** `docker build` failed with `NETSDK1064: Package Microsoft.AspNetCore.OpenApi, version 10.0.8 was not found`. The SDK version inside the container (`10.0.301`) requires a re-resolve pass even when restore already ran in a prior layer. The RESEARCH.md sketch included `--no-restore` as an optimization.
- **Fix:** Removed `--no-restore` from the publish `RUN` step. The NuGet restore cache layer is still effective (cache hit), so build performance is essentially unchanged.
- **Files modified:** `samples/Platformer3D/Dockerfile`
- **Commit:** `79610a3` (updated in-place before first task commit)

No other deviations. Both tasks executed exactly as planned.

---

## Known Stubs

None. The packaging files are complete. The 21-06 smoke test will validate `docker compose up` → `/health/ready` 200 end-to-end.

---

## Threat Surface Scan

No new threat surfaces introduced beyond those already catalogued in the plan's threat model:

| Flag | File | Description |
|------|------|-------------|
| T-21-16 (mitigated) | docker-compose.yml | postgres/redis have no `ports:` — internal network only |
| T-21-17 (mitigated) | Dockerfile, docker-compose.yml | No CDN/cloud URLs; OTel exporter not configured |
| T-21-18 (mitigated) | Dockerfile | Pinned official MCR base images |
| T-21-19 (mitigated) | Dockerfile | Demo-only RSA keys with KEYS_ARE_DEMO_ONLY.txt sentinel |

---

## Self-Check: PASSED

Files exist:
- `samples/Platformer3D/Dockerfile` — FOUND
- `samples/Platformer3D/.dockerignore` — FOUND
- `samples/Platformer3D/docker/postgres/init/01-init.sql` — FOUND
- `samples/Platformer3D/docker-compose.yml` — FOUND
- `samples/Platformer3D/README.md` — FOUND

Commits exist:
- `79610a3` — FOUND (Task 1)
- `3d7feb7` — FOUND (Task 2)
