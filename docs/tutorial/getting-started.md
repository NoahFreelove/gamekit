# Getting Started with GameKit

**Time required:** ~15 minutes  
**Prerequisites:** Docker, .NET 10 SDK, OpenSSL

By the end of this tutorial you will have a first authenticated player and a first completed
match running against the TicTacToeDuel sample app — entirely on your local machine, with no
cloud credentials required.

---

## What you will need

| Tool | Version | Why |
|------|---------|-----|
| .NET SDK | 10.0.106 (pinned via `global.json`) | Build + run the sample |
| Docker | Any recent version | Postgres + Redis containers |
| OpenSSL | Any recent version | Generate a throwaway RSA key pair for JWT signing |

---

## Step 1 — Install the `gamekit` project template (local install)

The template is not yet published to NuGet.org. Install it from the repo directly:

```bash
# From the repo root
dotnet new install ./templates/GameKit.Templates
```

Verify it registered:

```bash
dotnet new list | grep gamekit
# Should print: gamekit  GameKit Sample Game
```

You can now scaffold a new GameKit-backed project with:

```bash
dotnet new gamekit --name MyGameBackend
```

The scaffold wires `AddGameKit().AddAuth().AddRankings().AddMatchmaking()` and includes a
`docker-compose.yml` for the Postgres + Redis stack. For this tutorial we use the included
TicTacToeDuel sample instead.

---

## Step 2 — Start the infrastructure (Postgres + Redis)

The TicTacToeDuel sample ships its own `docker-compose.yml` that maps Postgres to host
port **5433** (not 5432, which your local Postgres likely occupies):

```bash
docker compose -f samples/TicTacToeDuel/docker-compose.yml up -d
```

Verify both containers are healthy:

```bash
docker compose -f samples/TicTacToeDuel/docker-compose.yml ps
# postgres: Up (healthy)
# redis:    Up
```

Redis listens on the default port **6379**.

---

## Step 3 — Generate a throwaway RSA key pair

GameKit.Auth signs JWTs with an RSA private key. Run the bundled helper script:

```bash
bash samples/TicTacToeDuel/scripts/gen-test-rsa-pem.sh
```

This creates `samples/TicTacToeDuel/keys/dev-priv.pem` and `keys/dev-pub.pem`. The script
prints a "local development only" warning — that is expected. Never commit these files (the
directory's `.gitignore` already excludes `*.pem`).

In production: generate the key pair on your target host, set file mode `0600`, and configure
the paths in your `appsettings.Production.json` or via environment variables.

---

## Step 4 — Override the Postgres port (5432 → 5433)

`appsettings.Development.json` defaults to `Port=5432` (the standard Postgres port). Because
the sample's docker-compose maps Postgres to **5433** on the host, you need to override the
connection strings before running:

```bash
# Tab A — keep this terminal open; run the sample from here
export ConnectionStrings__GameKit="Host=localhost;Port=5433;Database=gamekit;Username=gamekit_app;Password=gamekit_app_dev"
export ConnectionStrings__GameKitMigrations="Host=localhost;Port=5433;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev"
export ConnectionStrings__Redis="localhost:6379"
```

These environment variable names match the `ConnectionStrings:*` keys in `appsettings.json`.
ASP.NET Core's configuration system merges them in automatically via `__` → `:` substitution.

---

## Step 5 — Run the sample

```bash
dotnet run --project samples/TicTacToeDuel
```

On first start, GameKit runs migrations under advisory locks and creates the `gamekit` schema.
This is expected and takes a few seconds. You will see log lines like:

```
info: GameKit.Core.MigrationRunner[0]
      Applied migration: 20260101000000_InitialCreate
```

The app is ready when you see:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

Leave this process running for the rest of the tutorial.

---

## Step 6 — Create your first authenticated player (Play as Guest)

Open your browser and navigate to:

```
http://localhost:5000
```

Click **Play as Guest**. The browser posts to `POST /auth/login/guest` (no body; the
`X-GameKit-Device` header is set automatically by the page's JavaScript) and receives a JWT
access token. The yellow banner at the top of the page reads:

> **Demo-only client:** tokens are stored in `localStorage` (XSS-vulnerable).

This is intentional for the demo — the in-page callout is accurate. In a production app, store
tokens in HTTP-only `Secure` + `SameSite=Strict` cookies set server-side, in a native keychain,
or in a Service-Worker-mediated split. See `samples/TicTacToeDuel/README.md` §Client token
storage for the full trade-off analysis.

After the guest login, the UI shows your player id and confirms you are authenticated.

---

## Step 7 — Find and complete a match (two-tab demo)

### Open two browser tabs

Open `http://localhost:5000/matchmaking.html` in **two separate tabs**. One regular tab and one
private/incognito window works well — each tab gets its own `localStorage` and therefore its
own guest JWT.

In **each tab**: click **Play as Guest** (if not already signed in from the home page session)
and then click **Find Match**.

Each tab posts `POST /api/mm/queue` with the tictactoe ladder id and `poolName: null` (the
default pool). The in-process matchmaking ticker fires every 500 ms and pairs the two tickets
because they are in the same pool and their default Glicko-2 ratings are within the starting
bracket of 100 rating points.

> **Important:** the enqueue sends `poolName: null` (the "default" pool). A named pool (such as
> `"tictactoe"`) would never form a match because TicTacToeDuel's matchmaking ladder only pairs
> tickets in the `"default"` pool — do not pass a named pool when enqueuing.

### Accept the match

Within about 1 second (one 500 ms ticker cycle), both tabs transition from "Queued" to "Match
Found" and display a 10-second countdown. Click **Accept** in **both** tabs before the window
expires.

- The first accept transitions that player's slot to "Accepted".
- The second accept triggers the all-accepted path and transitions both slots to **Matched**,
  creating a `game_sessions` row in Postgres with a shared `sessionId`.

### Verify readiness

Confirm the app is healthy after the match formed:

```bash
curl -s http://localhost:5000/health/ready
# HTTP 200 — Postgres connectivity, Redis connectivity, and all migration reporters pass.
```

A `200` response confirms the sample is ready and the full tutorial happy-path succeeded.

---

## Step 8 — Optional: explore the Admin console

Bootstrap the first admin user:

```bash
dotnet gamekit admin create -u root -p choose-a-strong-password \
  --connection-string "Host=localhost;Port=5433;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev"
```

Then navigate to `http://localhost:5000/admin/login` and sign in. From the Admin console you
can search players, view audit logs, check the matchmaking queue depth, and monitor Postgres +
Redis health.

---

## Step 9 — Optional: enable the observability stack

If you want traces, metrics, and Grafana dashboards:

```bash
docker compose \
  -f samples/TicTacToeDuel/docker-compose.yml \
  -f samples/TicTacToeDuel/docker-compose.observability.yml \
  up -d
```

This adds the OpenTelemetry Collector, Prometheus, Tempo, and Grafana. Navigate to:

```
http://localhost:3000
```

Log in with `admin / admin` (Grafana's default dev credentials). Pre-provisioned dashboards
for GameKit matchmaking and rankings metrics are available immediately. The sample is already
wired to export traces and metrics to the Collector via `AddGameKitObservability()` in
`Program.cs` — no code changes required.

The OTLP endpoint (`http://otelcol:4317`) is configured via the `GameKit:Observability:OtlpEndpoint`
key. It is set automatically by the observability overlay's environment variables; without the
overlay, the key is absent and the library runs with no exporter (instruments remain active —
any OTel SDK in the host will auto-pick them up).

---

## Endpoints reference

| Method | Path | Notes |
|--------|------|-------|
| POST | `/auth/login/guest` | No body; requires `X-GameKit-Device` header |
| POST | `/auth/register` | `{ username, password }` — upgrades a guest JWT in-place |
| POST | `/auth/login/password` | `{ username, password }` |
| POST | `/auth/refresh` | `{ refreshToken }` + `X-GameKit-Device` header |
| POST | `/auth/logout` | `{ refreshToken }` — revokes the caller's family |
| GET | `/auth/me` | Requires Bearer JWT |
| POST | `/api/mm/queue` | `{ ladderId, poolName: null }` — enqueue for matchmaking |
| GET | `/api/mm/queue/{ticketId}/status` | Long-poll: `queued` / `proposed` / `matched` / `cancelled` |
| POST | `/api/mm/proposal/{proposalId}/accept` | `{ ticketId }` — accept within the 10 s window |
| GET | `/health/ready` | 200 when Postgres + Redis + migrations all pass |
| GET | `/health/live` | 200 when the process is alive (no dependency checks) |

---

## Troubleshooting

**`Missing GameKit:Auth:Jwt:PrivateKeyPemPath` at startup:**
Run `bash samples/TicTacToeDuel/scripts/gen-test-rsa-pem.sh` first. The key files live at
`samples/TicTacToeDuel/keys/` and must exist before the app starts.

**Connection refused on port 5432:**
You forgot to set the `ConnectionStrings__GameKit` environment variable. The default in
`appsettings.Development.json` targets `Port=5432`; the docker-compose stack maps Postgres to
`5433`. Re-read Step 4.

**Match never forms — tabs stay "Queued" indefinitely:**
Verify both tabs are calling the same endpoint and sending `poolName: null`. If the browser
console shows `poolName: "tictactoe"` in the request body, you may be running an older copy of
`matchmaking.html` — pull the latest and rebuild.

**`GET /health/ready` returns 503:**
Check the JSON body for the failing component name. Common causes:
- `migrations-*`: a migration failed at startup — check the application logs.
- `redis`: the Redis container is not running — `docker compose ps`.
- `postgres`: the Postgres container is not running or the connection string is wrong.

**Admin UI returns 404 at `/admin`:**
The app is running in `Production` mode. Set `ASPNETCORE_ENVIRONMENT=Development` or bootstrap
an admin user first with `dotnet gamekit admin create` (Step 8) and try again.

---

## Next steps

- **Add a second auth provider (password login):** `POST /auth/register` while authenticated as
  a guest upgrades the same player in-place — the guest player id is preserved (D-12
  guest-upgrade-in-place).
- **Wire the match result back to Rankings:** after both players finish a tic-tac-toe game, post
  the result to `POST /api/sessions/{sessionId}/complete` with a service token to update
  Glicko-2 ratings.
- **Deploy to production:** see [`docs/ops/`](../ops/README.md) for bare-metal, container, and
  air-gapped deployment recipes including Postgres role setup, JWT key hygiene, and multi-replica
  configuration.
- **Upgrade from v2.0 to v2.1:** see [`docs/upgrade/v2.0-to-v2.1.md`](../upgrade/v2.0-to-v2.1.md)
  for the exact configuration additions (`AddGameKitObservability`, `AddGameKitHealthChecks`,
  `MapGameKitHealth`, and the DrOrdering marker migrations).

---

*Apache-2.0 — see repo root `LICENSE`.*
