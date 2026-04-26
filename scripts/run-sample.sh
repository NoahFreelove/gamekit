#!/usr/bin/env bash
# Build + run the TicTacToeDuel sample with the Admin UI mounted at /admin.
# Usage:
#   ./scripts/run-sample.sh             # build + run
#   ./scripts/run-sample.sh --reset-db  # nuke Postgres volume so init scripts re-run
#   ./scripts/run-sample.sh --bootstrap # also create the first admin (root/hunter2hunter2)
#   ./scripts/run-sample.sh --reset-db --bootstrap

set -euo pipefail

cd "$(dirname "$0")/.."

RESET_DB=false
BOOTSTRAP=false
for arg in "$@"; do
    case "$arg" in
        --reset-db)   RESET_DB=true ;;
        --bootstrap)  BOOTSTRAP=true ;;
        *) echo "Unknown flag: $arg" >&2; exit 2 ;;
    esac
done

CONN='Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev'

# 1. Free port 5432 from any host Postgres so the Docker container can bind.
if ss -lntp 2>/dev/null | grep -q '127.0.0.1:5432'; then
    echo "[host-postgres] detected on 127.0.0.1:5432 — attempting to stop"
    sudo systemctl stop postgresql 2>/dev/null || sudo service postgresql stop 2>/dev/null || true
fi

# 2. Reset volume if requested (forces docker/postgres/init/*.sql to re-run on fresh data dir).
if "$RESET_DB"; then
    echo "[docker] resetting Postgres volume"
    docker compose down
    docker volume rm gamekit_gamekit-postgres-data 2>/dev/null || true
fi

# 3. Bring containers up and wait until both are healthy.
echo "[docker] starting Postgres + Redis"
docker compose up -d
echo "[docker] waiting for healthy"
for _ in $(seq 1 30); do
    if [ "$(docker inspect -f '{{.State.Health.Status}}' gamekit-postgres 2>/dev/null)" = "healthy" ] \
        && [ "$(docker inspect -f '{{.State.Health.Status}}' gamekit-redis 2>/dev/null)" = "healthy" ]; then
        break
    fi
    sleep 1
done

# 4. Build everything once so the next dotnet run is incremental.
echo "[build] solution"
dotnet build GameKit.sln -c Debug --nologo --verbosity quiet

# 5. Apply Core migrations via the CLI (Auth + Admin migrations apply at sample startup).
echo "[migrate] core"
dotnet run --project src/GameKit.Cli --no-build -- migrate -c "$CONN"

# 6. Optionally bootstrap the first admin. Sample MUST be started once (step 7) before this
#    will succeed, since AdminMigrationHostedService creates gamekit.admin_users on app boot.
#    The fastest reliable bootstrap pattern: launch the sample headless, wait for migrations,
#    Ctrl-C it, run admin create, then start the sample for real. We do that inline.
if "$BOOTSTRAP"; then
    echo "[bootstrap] booting sample once so AdminMigrationHostedService creates gamekit.admin_users"
    dotnet run --project samples/TicTacToeDuel --no-build > /tmp/gamekit-bootstrap.log 2>&1 &
    SAMPLE_PID=$!
    # Wait for "Application started." in the log (up to 30s).
    for _ in $(seq 1 30); do
        if grep -q "Application started" /tmp/gamekit-bootstrap.log; then break; fi
        sleep 1
    done
    kill "$SAMPLE_PID" 2>/dev/null || true
    wait "$SAMPLE_PID" 2>/dev/null || true

    echo "[bootstrap] creating first admin (root / hunter2hunter2 — superadmin auto-promoted)"
    dotnet run --project src/GameKit.Cli --no-build -- admin create \
        -u root -p "hunter2hunter2" -c "$CONN" || \
        echo "[bootstrap] admin create failed — admin may already exist (skipping)"
fi

# 7. Run the sample in the foreground.
echo
echo "[sample] starting TicTacToeDuel on http://localhost:5000 (Ctrl-C to stop)"
echo "[sample] admin console: http://localhost:5000/admin/login"
echo
exec dotnet run --project samples/TicTacToeDuel --no-build
