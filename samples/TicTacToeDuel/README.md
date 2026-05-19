# Tic-Tac-Toe Duel — GameKit Phase 2 Sample

Executable demo that exercises **both** `GameKit.Core` (Phase 1 — persistence + session
lifecycle) **and** `GameKit.Auth` (Phase 2 — JWT issuance + refresh rotation +
Steam/Discord/Guest/Password providers) from a single ASP.NET Core app.

The HTML client at `wwwroot/index.html` drives `/auth/*` + `/demo/games/*` directly
from the browser so the full flow (guest → upgrade → play → logout) is visible
end-to-end.

## Prerequisites

- .NET 10 SDK (the repo pins `SDK 10.0.106` via `global.json`)
- Docker (for the Postgres + Redis stack defined in the repo's `docker-compose.yml`)
- OpenSSL (any recent version — used by `scripts/gen-test-rsa-pem.sh`)

## Run

```bash
# 1. Generate a throwaway RSA key pair for local JWT signing/validation.
./scripts/gen-test-rsa-pem.sh

# 2. Start Postgres + Redis.
docker compose up -d

# 3. Run the sample (listens on http://localhost:5000 per Properties/launchSettings.json).
dotnet run --project samples/TicTacToeDuel
# then open http://localhost:5000
```

On first start the `GameKit` + `GameKit.Auth` migrations run under advisory locks and
the `gamekit` schema is created. This is expected and takes a few seconds.

## What it demonstrates

- `AddGameKit().AddAuth(...)` fluent composition with JWT + Steam + Discord options
  read from `appsettings.Development.json`
- Strict middleware ordering:
  `UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → Map*`
  (RESEARCH §8.12 #6 — deviating causes authenticated endpoints to 401 even with a
  valid Bearer token)
- The full `/auth/*` HTTP surface (`/login/guest`, `/login/password`, `/register`,
  `/refresh`, `/logout`, `/me`) driven from a browser with `Authorization: Bearer`
  + `X-GameKit-Device` headers
- Client-side 401 → `/auth/refresh` → retry-once token-rotation UX
- D-12 guest-upgrade-in-place (register while authenticated as guest promotes the
  same player id — no duplicate Player row)
- Phase-1 game loop (`POST /demo/games`, `POST /demo/games/{id}/moves`,
  `GET /demo/games/{id}`) with `IPlayerDisplayNameResolver` rendering

## Phase 2: Authentication

The HTML client stores JWTs in `localStorage` and carries two request headers on every
`/auth/*` and `/demo/*` call:

- `Authorization: Bearer <access_token>`
- `X-GameKit-Device: <uuid>` — generated once per browser via `crypto.randomUUID()`
  and persisted in `localStorage`

The old `POST /demo/players/register` endpoint (Phase 1) has been **removed**. Player
creation happens exclusively through `GameKit.Auth`:

| Method | Path                          | Notes                                                                 |
| ------ | ----------------------------- | --------------------------------------------------------------------- |
| POST   | `/auth/login/guest`           | No body — issues an anonymous JWT                                     |
| POST   | `/auth/register`              | `{ username, password }` — upgrades caller's guest JWT if present (D-12) |
| POST   | `/auth/login/password`        | `{ username, password }`                                              |
| POST   | `/auth/refresh`               | `{ refreshToken }` + `X-GameKit-Device` header                        |
| POST   | `/auth/logout`                | `{ refreshToken }` — revokes the caller's family                      |
| GET    | `/auth/me`                    | Requires Bearer JWT                                                   |
| GET    | `/auth/challenge/{provider}`  | 302 to Steam OpenID OP or Discord OAuth2                              |
| GET    | `/auth/callback/{provider}`   | Server-side OpenID / OAuth2 assertion verification                    |

### Client token storage (localStorage) — security note

The HTML client stores JWTs in `localStorage` so the browser-reload UX works.
**This is XSS-vulnerable** — if any script executing on the page can read
`localStorage`, the access + refresh tokens are stolen. Acceptable trade-off for a
demo; production apps should pick one of:

- HTTP-only `Secure` + `SameSite=Strict` cookies set server-side (changes the API
  contract — `GameKit.Auth` returns tokens in the response body for portability, so
  your shell endpoint would re-wrap them into a `Set-Cookie` header).
- A native token cache (Android/iOS keychain, Electron preload script).
- A Service-Worker-mediated split: refresh in a background context, access-only in
  the page's JS.

A yellow banner at the top of `index.html` surfaces this to anyone who opens the
sample so the trade-off is visible, not implicit.

### Signing-key hygiene

`appsettings.Development.json` references
`samples/TicTacToeDuel/keys/dev-priv.pem` + `dev-pub.pem`.

- **Never** commit a real private key to git. `samples/TicTacToeDuel/keys/.gitignore`
  excludes `*.pem`; `scripts/gen-test-rsa-pem.sh` prints a "local development only"
  warning on every run.
- In production, the private key file **must** be mode `0600` and owned by the
  process user. Anyone who reads this file can forge tokens indistinguishable from
  the server's.
- Rotate: generate a new `Kid`, switch issuance to the new key, and keep the old
  public key in the validator's `IssuerSigningKeys` collection for the
  refresh-token lifetime (30 days by default) so existing sessions still verify.

### Customizing the egress allow-list

`GameKit.Auth` only talks to hosts on
`GameKitAuthOptions.AllowedProviderHosts`. Defaults cover Steam + Discord
(`steamcommunity.com`, `api.steampowered.com`, `discord.com`, `discordapp.com`);
if you proxy OAuth through another host, add it:

```csharp
.AddAuth(auth =>
{
    // ... existing options ...
    auth.AllowedProviderHosts.Add("id.internal.example.com");
});
```

An off-list outbound call throws `EgressViolationException` — loud failure, not
silent leak (T-02-14 mitigation).

### Optional: wire Discord OAuth

`appsettings.Development.json` ships `DISCORD_CLIENT_ID_PLACEHOLDER` +
`DISCORD_CLIENT_SECRET_PLACEHOLDER` by default. With those placeholders, the Discord
authentication scheme **skips registration** at startup so `/auth/challenge/discord`
returns 400 `unknown_provider` instead of throwing. To exercise the Discord flow
locally:

1. Create a Discord application at <https://discord.com/developers/applications>.
2. Under **OAuth2 → General**, add redirect URL `http://localhost:5000/auth/callback/discord`.
3. Replace the placeholders in `appsettings.Development.json`:

   ```json
   "Discord": {
     "ClientId": "<real client id>",
     "ClientSecret": "<real client secret>",
     "CallbackPath": "/auth/callback/discord"
   }
   ```

The guest and password providers need no external credentials.

### Optional: wire Steam OpenID

Steam OpenID 2.0 does not require a client secret — the OP (`steamcommunity.com/openid`)
verifies assertions server-side via `check_authentication`. The realm (`Steam.Realm`
in `appsettings.Development.json`) must match the scheme + host the browser reaches
your app on (defaults to `http://localhost:5000/`). `Steam.ApiKey` is optional; set it
from <https://steamcommunity.com/dev/apikey> if you want Steam's `GetPlayerSummaries`
to resolve display names.

## Admin UI

The sample mounts the GameKit Admin console at `/admin`. Bootstrap the first admin before signing in.

### Bootstrap

With Postgres running (via `docker-compose up`), run:

```bash
dotnet gamekit admin create -u root -p choose-a-strong-password
```

The first admin created on an empty `admin_users` table is automatically promoted to **superadmin** regardless of the `--role` flag. This resolves the chicken-and-egg "who creates the first admin" bootstrap.

After running the command, start the sample:

```bash
dotnet run --project samples/TicTacToeDuel
```

Browse to `https://localhost:5001/admin/login` and sign in.

### Production vs development behavior

- **Production:** an unauthenticated request to `/admin` returns **404** (ROADMAP SC #2). The admin UI only becomes reachable after sign-in. In Production, if no superadmin is configured, the app fails to start with a clear error pointing at `dotnet gamekit admin create`.
- **Development/Staging:** an unauthenticated request redirects to `/admin/login`. The login page shows an inline "No admin configured yet" state when `admin_users` is empty, with the same bootstrap hint.

### What you can do

Signed in as an admin, you can:
- **Players** — search by id, display name, or `provider:external_id`
- **Player detail** — ban (writes audit log), unban, GDPR delete (superadmin-only)
- **Audit log** — view every admin action with before/after JSON
- **Health** — Postgres + Redis connectivity + recent error rate
- **Admins** (superadmin-only) — list + create + delete admin accounts

Rankings (`Rank adjust`, `End season`) verbs are now functional — Phase 4 ships `GameKit.Rankings`.
Matchmaking (`Queue depth`) remains a placeholder until Phase 5.

### Security posture

- Admin cookie scheme is **separate** from the player JWT scheme — a valid player token cannot authenticate into `/admin/*` (ROADMAP SC #6).
- **Strict CSP** with per-request nonce on every `/admin/*` response.
- **Antiforgery** required on every mutation (POST / DELETE / PATCH).
- Admin login is **rate-limited** at 5 attempts / minute / IP.
- Banned players cannot sign in via any provider; their refresh-token family is revoked on the next refresh attempt.

See `.planning/phases/03-admin-ui/03-CONTEXT.md` for the full Phase 3 decision log.

## Rankings (Phase 4)

`GameKit.Rankings` is wired in `Program.cs` with one default ladder named `"main"` (Glicko-2,
rating period 1 hour, soft-regress on season end). The ticker runs in the background and drains
batched rating updates every hour.

### Issue a service token (required for session completion)

Match servers report results via `POST /api/sessions/{id}/complete` using a service token
(not a player JWT). Issue one before running your game server:

```bash
dotnet gamekit service-token issue --name tic-tac-toe-server \
  --connection-string "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev"
```

Pass the token as `Authorization: Bearer <service-token>` alongside an `Idempotency-Key` header
(any stable string, e.g. the game session id) in the `POST /api/sessions/{id}/complete` body:

```json
{
  "sessionId": "<session-id>",
  "results": [
    { "playerId": "<player-a>", "result": "win",  "team": 0, "score": 1 },
    { "playerId": "<player-b>", "result": "loss", "team": 1, "score": 0 }
  ]
}
```

The ticker will pick up the session on its next drain and update Glicko-2 ratings.

### GDPR export

Players can export their own data bundle:

```
GET /api/players/{id}/export
Authorization: Bearer <player-jwt>
```

Superadmins can export any player's data via the admin API:

```
GET /admin/api/players/{id}/export
Cookie: .AspNetCore.GameKitAdmin=<admin-cookie>
```

Both paths share one REPEATABLE READ snapshot (D-17) and enforce a 25 MB cap (D-18).

### Admin rank-adjust and end-season

In the Admin console, open the command palette (Ctrl+K) and search "Rank adjust" or
"End season" — both verbs are now functional under the Superadmin policy. Rank-adjust
opens a dialog that updates a player's rating on a specific ladder (SERIALIZABLE tx + audit
row). End-season archives the current season's leaderboard and starts a new one.

## Endpoints used

**Phase 2 `/auth/*`** — see table above.

**Phase 1 `/demo/*`** (game loop):

| Method | Path                      | Body                       | Returns       |
| ------ | ------------------------- | -------------------------- | ------------- |
| POST   | `/demo/games`             | `{ playerXId, playerOId }` | full game     |
| POST   | `/demo/games/{id}/moves`  | `{ playerId, row, col }`   | updated state |
| GET    | `/demo/games/{id}`        | —                          | current state |

`/api/players` (from `MapGameKit()`) now requires a valid Bearer JWT — Phase 2's
`UseGameKitAuth()` wires the `JwtBearer` handler.

## Troubleshooting

- **Startup error `Missing GameKit:Auth:Jwt:PrivateKeyPemPath` / file not found:**
  Run `./scripts/gen-test-rsa-pem.sh` first. The path in `appsettings.Development.json`
  is relative to the repo root; `dotnet run --project samples/TicTacToeDuel` runs with
  the sample directory as the content root, but the relative path walks up to the
  repo root where the `samples/TicTacToeDuel/keys/` directory lives.
- **Port 5432 already in use:** override `ConnectionStrings:GameKit` in
  `appsettings.Development.json` or stop your existing Postgres.
- **Migrations run at first startup:** expected; the advisory-lock migration runner
  serializes the schema change for both `Core` and `Auth` packages.
- **`401` on every `/api/players` or `/auth/me` call even after login:** check the
  middleware order in `Program.cs` matches
  `UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → Map*`.
- **`EgressViolationException` in logs:** you're calling an external host not on the
  allow-list. Either add the host (see "Customizing the egress allow-list" above) or
  check that `SteamOptions.Realm` / `DiscordOptions.ClientId` point at the intended
  service.

---

GPL-3.0-or-later — see repo root `LICENSE`.
