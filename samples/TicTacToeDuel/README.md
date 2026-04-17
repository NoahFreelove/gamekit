# Tic-Tac-Toe Duel — GameKit Phase 1 Sample

Executable demo of the Phase 1 GameKit.Core surface. This is **not** a tutorial on
building a game — it is a smoke-test backend that exercises real Postgres persistence,
the `GameSession` state machine, the JSONB metadata column, and
`IPlayerDisplayNameResolver` through a tiny browser client.

## Prerequisites

- .NET 10 SDK (the repo pins `SDK 10.0.106` via `global.json`)
- Docker (for the Postgres + Redis stack defined in the repo's `docker-compose.yml`)

## Run

```bash
docker compose up -d
dotnet run --project samples/TicTacToeDuel
# then open http://localhost:5000
```

On first start the GameKit migrations run under an advisory lock and the `gamekit`
schema is created. This is expected and takes a few seconds.

## What it demonstrates

- Registering `Player` rows through `GameKitDbContext`
- Creating `GameSession` + two `SessionParticipant` rows (Team 0 = X, Team 1 = O) with
  ids minted by `IIdGenerator` and timestamps from `IClock`
- Persisting and mutating a 3x3 board in `GameSession.Metadata` (JSONB) across moves
- Driving a session through the `Pending -> Active -> Completed` lifecycle via
  `GameSession.Start(now)` / `GameSession.Complete(now)`
- Recording `SessionResult.Win` / `Loss` / `Draw` per participant on terminal outcome
- Resolving display names via `IPlayerDisplayNameResolver` (so GDPR-deleted players
  automatically render as the configured "Deleted Player" tombstone)

## Explicitly NOT a Phase-1 concern: authentication

`POST /demo/players/register` is **deliberately unauthenticated**. It exists so the
sample can demonstrate session lifecycle without waiting on Phase 2. It will be
removed / replaced by `GameKit.Auth` in Phase 2. **Do not ship this pattern.**

## Endpoints used

| Method | Path | Body | Returns |
|--------|------|------|---------|
| POST | `/demo/players/register` | `{ displayName }` | `{ id, displayName }` |
| POST | `/demo/games` | `{ playerXId, playerOId }` | full game state |
| POST | `/demo/games/{id}/moves` | `{ playerId, row, col }` | updated state |
| GET  | `/demo/games/{id}` | — | current state |

The `/api/players` endpoint from `MapGameKit()` returns `401` — this is intentional
(Phase 1 has no authentication handler; Phase 2's `GameKit.Auth` wires one).

## Troubleshooting

- **Port 5432 already in use:** override `ConnectionStrings:GameKit` in
  `appsettings.Development.json` or stop your existing Postgres.
- **Migrations run at first startup:** expected; the advisory-lock migration runner
  serializes the schema change.
- **`401` on `/api/players`:** expected in Phase 1 — auth gate for Phase 2.

---

GPL-3.0-or-later — see repo root `LICENSE`.
