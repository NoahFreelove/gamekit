// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands;

/// <summary>CLI command: <c>gamekit migrate</c>. Applies pending migrations with advisory-lock serialization.</summary>
internal sealed class MigrateCommand : AsyncCommand<MigrateCommand.Settings>
{
    /// <summary>CLI arguments.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Explicit connection string. Falls back to env <c>GAMEKIT_MIGRATIONS_CONNECTION</c> then <c>GAMEKIT_CONNECTION</c>.</summary>
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

        var services = new ServiceCollection();
        services.AddGameKit(opts =>
        {
            opts.MigrationsConnectionString = conn;
            opts.ConnectionString = conn;
            opts.AutoMigrate = false;
        });

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        AnsiConsole.MarkupLine("[grey]Applying GameKit.Core migrations (advisory-lock serialized)...[/]");
        await MigrationRunner.MigrateWithLockAsync(ctx, CancellationToken.None).ConfigureAwait(false);

        AnsiConsole.MarkupLine("[green]OK — migrations up to date.[/]");
        return 0;
    }
}
