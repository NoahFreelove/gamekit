# GameKit.Cli — Concepts

## What It Does

`GameKit.Cli` is a Spectre.Console.Cli-based command-line tool (`gamekit`) for day-to-day
operations: applying EF Core migrations across all packages, managing admin users, issuing
and revoking service-account bearer tokens, and wrapping `pg_dump` / `pg_restore` for
database backup and restore. It reads connection strings from standard `appsettings.json` /
environment variables — the same configuration the host application uses.

## Command Surface

```
gamekit migrate                  — Apply Core migrations (backwards-compat shorthand)
gamekit migrations list          — List applied + pending migrations across all 6 packages
gamekit migrations apply         — Apply pending migrations in canonical order (--dry-run for SQL preview)
gamekit admin create             — Create an admin user (interactive or flag-driven; first admin auto-superadmin)
gamekit service-token issue      — Mint a service-account bearer token (raw token printed once)
gamekit service-token revoke     — Revoke a service-account bearer token by name
gamekit service-token list       — List service-account tokens (names + status, never the hash)
gamekit db backup                — Backup Postgres via pg_dump (--format=custom); optionally triggers Redis BGSAVE
gamekit db restore               — Restore Postgres from a pg_dump custom-format file via pg_restore
```

Migrations are applied in canonical package order: Core → Auth → Admin.UI → Rankings →
Matchmaking → Lobby. The `migrations apply --dry-run` flag prints the idempotent SQL without
executing any DDL — safe to run in production to preview pending changes.

## No Public Interfaces

`GameKit.Cli` exposes **no public interfaces for consumers to implement**. It is a
`dotnet tool` entrypoint, not a library. The extension point is the Spectre.Console.Cli
`CommandApp` itself — consumers who need custom commands add their own commands in their own
executable by referencing `Spectre.Console.Cli` directly.

## Adding Custom Commands

If you want to extend the `gamekit` tool with game-specific commands, create your own
Spectre.Console.Cli `CommandApp` and add a dependency on `GameKit.Cli`'s command classes
directly, or write a separate tool that wraps the GameKit commands:

```csharp
// In your own CLI project:
var app = new CommandApp();
app.Configure(config =>
{
    // Add your game-specific commands:
    config.AddCommand<DeployReleaseCommand>("deploy");
    config.AddCommand<SeedLaddersCommand>("seed-ladders");
    // GameKit CLI commands require their own DI setup —
    // compose from GameKit.Cli's service registration helpers instead.
});
return await app.RunAsync(args);
```

## Security Notes

- Database passwords are passed via the `PGPASSWORD` environment variable. GameKit.Cli never
  accepts passwords as CLI arguments to avoid shell history leakage.
- `pg_dump` and `pg_restore` binaries must be on the operator's `PATH`. They are not bundled
  with the tool.
- Service-account tokens are printed **once** at issuance time. The raw token is never
  re-displayable — only a SHA-256 hash is stored. Store issued tokens securely.

## Installation

`GameKit.Cli` ships as a NuGet package that is installed as a dotnet local or global tool:

```bash
# Local tool manifest (recommended for CI + teams):
dotnet tool install GameKit.Cli

# Or globally:
dotnet tool install -g GameKit.Cli
```

After installation, configure the connection string(s) the same way as the host app
(environment variables, `appsettings.json`, or `GAMEKIT_CONNECTION_STRING`).

## Library-vs-Consumer Responsibility Line

| GameKit.Cli owns | Consumer owns |
|------------------|---------------|
| Migration orchestration (canonical order, dry-run) | Migration scheduling in CI/CD |
| Admin user bootstrap (first admin → superadmin) | Admin credential security + rotation |
| Service-token issuance + revocation | Service-token storage + distribution to game servers |
| DB backup/restore wrappers (pg_dump / pg_restore) | pg_dump/pg_restore binaries on `PATH`; backup storage location |
| Spectre.Console.Cli command surface | Custom game-specific CLI commands (own project) |

## See Also

- [API reference](../api/GameKit.Cli.yml) — full member-level docs.
- [docs/ops/migrations-runbook.md](../ops/migrations-runbook.md) — migration operations.
- [docs/runbooks/postgres-backup-restore.md](../runbooks/postgres-backup-restore.md) — backup/restore runbook.
