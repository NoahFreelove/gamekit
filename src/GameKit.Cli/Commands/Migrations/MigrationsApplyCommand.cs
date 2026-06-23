// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands.Migrations;

/// <summary>
/// CLI command: <c>gamekit migrations apply</c>. Applies pending migrations across all 6
/// GameKit packages in canonical order. With <c>--dry-run</c>, prints idempotent SQL for
/// all packages without executing any DDL (T-17-03-01). Satisfies DR-05.
/// </summary>
internal sealed class MigrationsApplyCommand : AsyncCommand<MigrationsApplyCommand.Settings>
{
    /// <summary>CLI arguments for <c>gamekit migrations apply</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Explicit connection string. Falls back to env <c>GAMEKIT_MIGRATIONS_CONNECTION</c>
        /// then <c>GAMEKIT_CONNECTION</c>.
        /// </summary>
        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string (gamekit_owner role recommended for migrations).")]
        public string? ConnectionString { get; init; }

        /// <summary>
        /// When set, prints the idempotent SQL script for all pending migrations without
        /// executing any DDL. Safe to run against a production database to preview changes.
        /// T-17-03-01: GenerateScript only generates text — no MigrateAsync is called.
        /// </summary>
        [CommandOption("--dry-run")]
        [Description("Print idempotent SQL for all packages without executing any DDL.")]
        [DefaultValue(false)]
        public bool DryRun { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var conn = settings.ConnectionString
            ?? Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION");

        if (string.IsNullOrWhiteSpace(conn))
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] No connection string. Pass --connection-string or set GAMEKIT_MIGRATIONS_CONNECTION / GAMEKIT_CONNECTION.");
            return 2;
        }

        if (settings.DryRun)
            return await ExecuteDryRunAsync(conn).ConfigureAwait(false);

        return await ExecuteApplyAsync(conn).ConfigureAwait(false);
    }

    /// <summary>
    /// Dry-run path: calls <c>IMigrator.GenerateScript(idempotent)</c> per package, printing
    /// the SQL to stdout. CRITICAL: never calls <c>MigrateAsync</c> or <c>MigrateWithLockAsync</c>.
    /// Zero DDL is executed — T-17-03-01 mitigation.
    /// </summary>
    private static async Task<int> ExecuteDryRunAsync(string conn)
    {
        AnsiConsole.MarkupLine("[grey]Dry-run mode: generating idempotent SQL (no DDL will be executed).[/]");
        AnsiConsole.MarkupLine("");

        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, conn);

            // GenerateScript is synchronous text generation — it does NOT open a transaction
            // or execute DDL. The await here is only to satisfy async context uniformity
            // (GetPendingMigrationsAsync used for informational header).
            var pending = await ctx.Database.GetPendingMigrationsAsync().ConfigureAwait(false);
            var pendingList = new System.Collections.Generic.List<string>(pending);

            // Section header for readability
            Console.WriteLine($"-- ============================================================");
            Console.WriteLine($"-- Package: {pkg.DisplayName} ({pendingList.Count} pending migration(s))");
            Console.WriteLine($"-- ============================================================");

            // T-17-03-01: GenerateScript returns text only — no DDL execution.
            var migrator = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            var sql = migrator.GenerateScript(
                fromMigration: null,
                toMigration: null,
                MigrationsSqlGenerationOptions.Idempotent);

            Console.WriteLine(sql);
        }

        AnsiConsole.MarkupLine("[green]Dry-run complete.[/] Review the SQL above, then run without --dry-run to apply.");
        return 0;
    }

    /// <summary>
    /// Live apply path: applies pending migrations for each package in canonical order using
    /// the advisory-lock-serialized runner (T-17-03-03 mitigation — same audited path as MigrateCommand).
    /// </summary>
    private static async Task<int> ExecuteApplyAsync(string conn)
    {
        AnsiConsole.MarkupLine("[grey]Applying pending migrations across all GameKit packages in canonical order...[/]");

        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            AnsiConsole.MarkupLine($"[grey]  [{pkg.CanonicalOrder}/6] {pkg.DisplayName}...[/]");
            await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, conn);
            await MigrationRunner.MigrateWithLockAsync(ctx, pkg.AdvisoryLockKey, CancellationToken.None)
                .ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]  [{pkg.CanonicalOrder}/6] {pkg.DisplayName} — OK[/]");
        }

        AnsiConsole.MarkupLine("[green]All packages up to date.[/]");
        return 0;
    }
}
