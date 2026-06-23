// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands.Migrations;

/// <summary>
/// CLI command: <c>gamekit migrations list</c>. Prints the applied and pending migration
/// counts for each of the 6 GameKit packages in canonical application order
/// (Core → Auth → Admin → Rankings → Matchmaking → Lobby). Satisfies DR-04.
/// </summary>
internal sealed class MigrationsListCommand : AsyncCommand<MigrationsListCommand.Settings>
{
    /// <summary>CLI arguments for <c>gamekit migrations list</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Explicit connection string. Falls back to env <c>GAMEKIT_MIGRATIONS_CONNECTION</c>
        /// then <c>GAMEKIT_CONNECTION</c> (mirrors the existing <c>migrate</c> command).
        /// </summary>
        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string (gamekit_owner role recommended for migrations).")]
        public string? ConnectionString { get; init; }
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

        var rows = new List<(string Package, int Order, int Applied, int Pending, string? Error)>();
        int exitCode = 0;

        foreach (var pkg in PackageMigrationContextFactory.Packages)
        {
            try
            {
                await using var ctx = PackageMigrationContextFactory.BuildContext(pkg, conn);
                var applied = await ctx.Database.GetAppliedMigrationsAsync().ConfigureAwait(false);
                var pending = await ctx.Database.GetPendingMigrationsAsync().ConfigureAwait(false);

                var appliedList = new System.Collections.Generic.List<string>(applied);
                var pendingList = new System.Collections.Generic.List<string>(pending);

                rows.Add((pkg.DisplayName, pkg.CanonicalOrder, appliedList.Count, pendingList.Count, null));
            }
            catch (Exception ex)
            {
                rows.Add((pkg.DisplayName, pkg.CanonicalOrder, 0, 0, ex.Message));
                exitCode = 1;
            }
        }

        // Render as a Spectre table
        var table = new Table()
            .AddColumn("[bold]Order[/]")
            .AddColumn("[bold]Package[/]")
            .AddColumn("[bold]Applied[/]")
            .AddColumn("[bold]Pending[/]");

        foreach (var (package, order, applied, pending, error) in rows)
        {
            if (error is not null)
            {
                table.AddRow(
                    order.ToString(),
                    $"[red]{package}[/]",
                    "[red]ERR[/]",
                    $"[red]{Markup.Escape(error)}[/]");
            }
            else
            {
                var pendingCell = pending > 0
                    ? $"[yellow]{pending}[/]"
                    : $"[green]{pending}[/]";
                table.AddRow(order.ToString(), package, applied.ToString(), pendingCell);
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[grey]Recommended application order:[/] Core → Auth → Admin → Rankings → Matchmaking → Lobby");

        return exitCode;
    }
}
