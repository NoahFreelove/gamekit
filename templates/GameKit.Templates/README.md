<!--
SPDX-License-Identifier: Apache-2.0
Copyright (c) 2026 GameKit contributors
-->

# GameKit.Templates

NuGet template package shipping the `dotnet new gamekit` template — generates a
ready-to-run [GameKit](https://github.com/OWNER/gamekit) sample game backend
(web tier + game-server tier) on the consumer's disk in one command.

## Install

```bash
dotnet new install GameKit.Templates
```

## Use

```bash
dotnet new gamekit -n MyGame
cd MyGame

# Generate dev RSA keys (also attempted automatically by the template
# post-action; Windows without WSL needs this manual run).
./scripts/gen-test-rsa-pem.sh

# Start Postgres + Redis.
docker compose up -d

# Run the web tier (port 5000).
dotnet run --project src/MyGame
```

## Opt-out flags

The template accepts four boolean opt-out flags. Each omits the corresponding
`PackageReference` and the `Add*` / `Map*` calls in `Program.cs`:

| Flag                  | Omits                                              |
|-----------------------|----------------------------------------------------|
| `--skip-auth`         | `GameKit.Auth` (JWT issuance, OAuth, password)     |
| `--skip-rankings`     | `GameKit.Rankings` (Glicko-2, session-complete)    |
| `--skip-matchmaking`  | `GameKit.Matchmaking` (parties + queue + proposal) |
| `--skip-presence`     | `GameKit.Presence` (Redis-TTL heartbeat panel)     |

`GameKit.Core`, `GameKit.OpenApi`, and `GameKit.Admin.UI` are always included.

Example — Core + Auth only:

```bash
dotnet new gamekit -n MyMinimalGame --skip-rankings --skip-matchmaking --skip-presence
```

## Uninstall

```bash
dotnet new uninstall GameKit.Templates
```

## Post-action: dev RSA keypair

The template's post-action runs `./scripts/gen-test-rsa-pem.sh`
via `bash` to produce a throwaway 2048-bit RSA keypair for JWT signing.
`continueOnError: true` is set, so a failed post-action (e.g. Windows without
WSL or OpenSSL) does NOT abort the template instantiation — instead, the
template prints the `manualInstructions` so the user can run the script
themselves. The post-action is idempotent (re-running it overwrites the
`dev-priv.pem` + `dev-pub.pem` files).

## License

Apache-2.0. The template ships content that itself depends on the
GameKit family of packages, all of which are also Apache-2.0-licensed.
