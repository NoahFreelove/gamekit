// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands;

/// <summary>
/// CLI command: <c>gamekit service-token list</c> (D-06 / RANK-11). Lists all service tokens with
/// their name, creation timestamp, optional expiry, and revocation status. Never prints the
/// <c>TokenHash</c> — it is deliberately excluded from the output (T-04-04-RT hash-leakage prevention).
/// </summary>
/// <remarks>
/// Exit codes: <c>0</c> success.
/// <para/>
/// Connection string: <c>--connection-string</c> flag or <c>GAMEKIT_CONNECTION</c> env var.
/// </remarks>
internal sealed class ServiceTokenListCommand : AsyncCommand<ServiceTokenListCommand.Settings>
{
    /// <summary>Command-line settings for <c>gamekit service-token list</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Postgres connection string. Falls back to env <c>GAMEKIT_CONNECTION</c>.</summary>
        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string. Falls back to GAMEKIT_CONNECTION env var.")]
        public string? ConnectionString { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var conn = settings.ConnectionString
            ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
            return Fail("No connection string. Pass --connection-string or set GAMEKIT_CONNECTION.");

        var dbOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(conn)
            .ReplaceService<IModelCustomizer, RankingsCliModelCustomizer>()
            .Options;
        await using var ctx = new GameKitDbContext(dbOpts);

        var tokens = await ctx.Set<ServiceToken>()
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        if (tokens.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No service tokens found.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Created");
        table.AddColumn("Expires");
        table.AddColumn("Status");

        foreach (var t in tokens)
        {
            var status = t.RevokedAt is not null
                ? $"[red]Revoked {t.RevokedAt:yyyy-MM-dd}[/]"
                : t.ExpiresAt.HasValue && t.ExpiresAt.Value < DateTimeOffset.UtcNow
                    ? "[yellow]Expired[/]"
                    : "[green]Active[/]";

            var expires = t.ExpiresAt.HasValue ? t.ExpiresAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ") : "-";

            table.AddRow(
                Markup.Escape(t.Name),
                t.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                expires,
                status);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static int Fail(string msg, int exitCode = 1)
    {
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {msg}");
        return exitCode;
    }
}
