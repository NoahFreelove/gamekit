// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Cli.Commands;
using GameKit.Cli.Commands.Db;
using GameKit.Cli.Commands.Migrations;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("gamekit");
    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply GameKit Core migrations against the configured Postgres (backwards-compatible shorthand; for all packages use 'gamekit migrations apply').");

    config.AddBranch("admin", admin =>
    {
        admin.SetDescription("Admin operations (superadmin bootstrap, admin CRUD).");
        admin.AddCommand<AdminCreateCommand>("create")
            .WithDescription("Create an admin user (interactive or flag-driven). First admin auto-promoted to superadmin.");
    });

    config.AddBranch("service-token", st =>
    {
        st.SetDescription("Service-account bearer token operations (issue, revoke, list).");
        st.AddCommand<ServiceTokenIssueCommand>("issue")
            .WithDescription("Mint a new service-account bearer token. Raw token printed once — store it securely.");
        st.AddCommand<ServiceTokenRevokeCommand>("revoke")
            .WithDescription("Revoke a service-account bearer token by name.");
        st.AddCommand<ServiceTokenListCommand>("list")
            .WithDescription("List all service-account bearer tokens (names, dates, status — never the hash).");
    });

    // Plan 17-04: DR-06 — db backup/restore branch wrapping pg_dump/pg_restore.
    // Binaries must be on the operator's PATH; they are not bundled with GameKit.
    // Password is passed via PGPASSWORD env var, never as a CLI argument (T-17-04-02).
    config.AddBranch("db", db =>
    {
        db.SetDescription("Database backup and restore helpers (wraps pg_dump/pg_restore). " +
            "Prerequisite: pg_dump and pg_restore must be on the operator's PATH.");
        db.AddCommand<DbBackupCommand>("backup")
            .WithDescription("Backup Postgres to a file via pg_dump (--format=custom). " +
                "Optionally issues a Redis BGSAVE. pg_dump must be on the operator's PATH.");
        db.AddCommand<DbRestoreCommand>("restore")
            .WithDescription("Restore Postgres from a pg_dump custom-format file via pg_restore. " +
                "Requires explicit --database to prevent accidental restore into the wrong DB. " +
                "pg_restore must be on the operator's PATH.");
    });

    // Plan 17-03: DR-04 / DR-05 — unified migrations branch covering all 6 GameKit packages.
    // The legacy top-level 'migrate' command is left untouched for backwards compatibility.
    config.AddBranch("migrations", migrations =>
    {
        migrations.SetDescription("Multi-package migration status and apply tooling (DR-04/DR-05). Covers all 6 GameKit packages in canonical order: Core, Auth, Admin, Rankings, Matchmaking, Lobby.");
        migrations.AddCommand<MigrationsListCommand>("list")
            .WithDescription("List applied and pending migration counts per package in recommended application order.");
        migrations.AddCommand<MigrationsApplyCommand>("apply")
            .WithDescription("Apply pending migrations across all packages. Use --dry-run to print idempotent SQL without executing any DDL.");
    });
});
return await app.RunAsync(args);
