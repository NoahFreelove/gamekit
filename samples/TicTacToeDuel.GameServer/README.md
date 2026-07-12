<!--
SPDX-License-Identifier: Apache-2.0
Copyright (c) 2026 GameKit contributors
-->

# TicTacToeDuel.GameServer

A small console process that demonstrates the **production 2-process topology** GameKit
recommends for shipping multiplayer games:

| Tier              | Process                       | Postgres role  | Responsibility                                                                  |
|-------------------|-------------------------------|----------------|----------------------------------------------------------------------------------|
| Web tier          | `samples/TicTacToeDuel/`      | `gamekit_owner`| HTTP API, auth, matchmaking, admin UI; OWNS the database (migrations + writes). |
| Game-server tier  | `samples/TicTacToeDuel.GameServer/` (this) | `gamekit_reader` | Reads matchmaking/player state; calls back into the web tier via HTTP. |

The web tier and the game-server tier are **independent OS processes** in production.
This console app is the minimal reference implementation of the game-server tier.

## What it does

On startup the GameServer:

1. Connects to Postgres as `gamekit_reader` and `SELECT`s on `gamekit.players` —
   proves the read-only role works.
2. Fetches `/openapi/v1.json` from the web tier — proves cross-tier HTTP works.
3. If a service-account JWT + session id are configured, POSTs `/api/sessions/{id}/start`
   against the web tier — demonstrates the game-server-authoritative session-lifecycle
   trigger (D-03 / D-13, Plan 06-05).

## Postgres role separation

The connection string in `appsettings.json` uses `gamekit_reader` / `gamekit_reader_dev`,
matching `docker/postgres/init/01-roles.sql`:

```
ConnectionStrings:GameKit
  = Host=localhost;Port=5432;Database=gamekit;Username=gamekit_reader;Password=gamekit_reader_dev
```

`gamekit_reader` is **granted SELECT** on every table in the `gamekit` schema and is
**denied INSERT / UPDATE / DELETE** by the default-privileges grants in
`docker/postgres/init/01-roles.sql`. The DIST-02 integration test
(`tests/GameKit.Distribution.Integration.Tests/DIST02_GamekitReaderInsertDeniedTests.cs`)
empirically asserts this — attempting INSERT as `gamekit_reader` raises Postgres
SQLSTATE `42501` ("permission denied for table game_sessions").

**Credentials caveat:** the password literal `gamekit_reader_dev` is fine for local
development against the shipped `docker-compose.yml`. Production operators rotate it
via the procedure documented in `docs/ops/postgres-roles.md` (Plan 06-09).

## Running it

```bash
./scripts/run-game-server.sh
```

The script invokes `dotnet run --project samples/TicTacToeDuel.GameServer/` with
`DOTNET_ENVIRONMENT=Development`. It assumes the web tier (`samples/TicTacToeDuel/`)
and the Postgres + Redis containers from `docker-compose.yml` are already running —
typically via `scripts/run-sample.sh` in another terminal.

## Demonstrating /api/sessions/{id}/start

The POST to `/api/sessions/{id}/start` requires a service-account JWT (the
`RequiresServiceToken` policy from Phase 4). The dev workflow:

1. Issue a service token: `dotnet run --project src/GameKit.Cli -- service-token issue …`
2. Have the web tier create a `game_sessions` row (matchmaking flow, or test seed).
3. Edit `samples/TicTacToeDuel.GameServer/appsettings.json` (or set environment variables):

   ```json
   "Services": {
     "WebApi": {
       "ServiceJwt": "<token from step 1>",
       "DemoSessionId": "<session id from step 2>"
     }
   }
   ```

4. Re-run `./scripts/run-game-server.sh`. The GameServer POSTs `/api/sessions/{id}/start`
   and logs the response status. The session transitions to `Active` and the
   `ISessionLifecycleObserver` fan-out fires the Presence in-match transition (D-21).

## Topology notes

* The GameServer has **no `ProjectReference` to any `GameKit.*` runtime package** by
  design. The web API surface is HTTP; the database is Npgsql. This matches real-world
  game-server deployments where the game binary is independent of the web-tier
  assembly graph (cross-platform, cross-language, multiple game instances per host).
* Plan 06-09's `dotnet new gamekit` template clones BOTH the web tier
  (`samples/TicTacToeDuel/`) AND this game-server (`samples/TicTacToeDuel.GameServer/`)
  so newcomers get the full topology end-to-end on `dotnet new gamekit -n MyGame`.
