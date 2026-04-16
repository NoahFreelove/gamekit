# GameKit

GPL-licensed, self-hostable .NET 10 game-services library. Composable NuGet packages for auth, player management, matchmaking, rankings, sessions, and presence. Install only what you need.

**License:** GPL-3.0-or-later
**Runtime:** .NET 10 LTS
**Storage:** Postgres (via EF Core 10 + Npgsql), Redis (via StackExchange.Redis)

## Core Value

A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.

## What GameKit Is Not

GameKit explicitly does not provide, and will never provide:

- **No AI / LLM integrations** of any kind (moderation, matchmaking, content gen, telemetry analysis).
- **No cloud-only / SaaS dependencies** — library runs air-gapped.
- **No hosted / paid components** — all functionality is GPL and free, always; no upsell tier.
- **No telemetry / phone-home** — library does not collect or transmit usage data.
- **No game-server hosting / orchestration** — use Agones, Multiplay, or custom.
- **No real-time game communication (netcode)** — use Mirror, Fish-Net, WebSockets, or custom.
- **No inventory / economy / progression systems** — game dev owns these, FK into `gamekit.players(id)`.
- **No analytics pipeline** — operator brings their own (ClickHouse, BigQuery, etc.).
- **No DDoS mitigation** — network/edge concern.
- **No game-specific anti-cheat** — engine/game concern.
- **No billing / entitlements** — storefronts own this.
- **No real-time chat / voice chat** — use SignalR, Mirror, engine netcode, or Vivox.
- **No MySQL / SQL Server providers** — Postgres-only for v1.

## Packages

| Package | Purpose |
|---------|---------|
| `GameKit.Core` | Entities, DbContext, fluent builder, GDPR, rate-limiting helpers |
| `GameKit.Auth` | Identity, credentials, JWT, OAuth providers (Steam, Discord, Guest, Password) |
| `GameKit.Rankings` | Ladders, player ranks, Glicko-2 default algorithm, seasonal leaderboards |
| `GameKit.Matchmaking` | Redis-backed queue, tickets, parties, background matcher |
| `GameKit.Presence` | Online/offline/in-match status (Redis-only, no Postgres) |
| `GameKit.Admin.UI` | Blazor Server admin panel (player search, bans, queue depth, health) |

## Database Roles (OPS-09)

GameKit's Postgres schema is `gamekit`. FK direction discipline: **only `public` -> `gamekit` allowed** (your application tables may FK into `gamekit.players(id)`; never the reverse). The shipped `docker-compose.yml` provisions three roles:

| Role | Purpose |
|------|---------|
| `gamekit_owner` | Migrations (DDL) — used by `gamekit migrate` and `UseGameKit()` auto-migrate |
| `gamekit_app` | Runtime DML — GameKit HTTP API backend |
| `gamekit_reader` | Game server reads — SELECT-only on `gamekit.*` |

## Quick Start

```bash
# Requires .NET SDK 10.0.106+ (pinned via global.json)
dotnet restore && dotnet build
```

## Status

Pre-release. Phase 1 (Foundation) in progress.

## License

This project is licensed under the GNU General Public License v3.0 or later — see the [LICENSE](LICENSE) file for details.
