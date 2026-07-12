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
/// CLI command: <c>gamekit service-token revoke</c> (D-06 / RANK-11). Sets <c>RevokedAt</c> on the
/// named service token, preventing it from being used for authentication. Idempotent: revoking an
/// already-revoked token returns exit code 0.
/// </summary>
/// <remarks>
/// Exit codes: <c>0</c> success (or already revoked), <c>1</c> missing required input,
/// <c>4</c> named token not found.
/// <para/>
/// Connection string: <c>--connection-string</c> flag or <c>GAMEKIT_CONNECTION</c> env var.
/// </remarks>
internal sealed class ServiceTokenRevokeCommand : AsyncCommand<ServiceTokenRevokeCommand.Settings>
{
    /// <summary>Command-line settings for <c>gamekit service-token revoke</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Name / label of the service token to revoke. Required.</summary>
        [CommandOption("-n|--name <NAME>")]
        [Description("Label of the service token to revoke.")]
        public string? Name { get; init; }

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

        var name = settings.Name
            ?? (Console.IsInputRedirected ? null : AnsiConsole.Ask<string>("Token name:"));
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Token name is required. Pass --name or provide one interactively.", exitCode: 1);

        var dbOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(conn)
            .ReplaceService<IModelCustomizer, RankingsCliModelCustomizer>()
            .Options;
        await using var ctx = new GameKitDbContext(dbOpts);

        var token = await ctx.Set<ServiceToken>()
            .FirstOrDefaultAsync(t => t.Name == name);

        if (token is null)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] No service token named '[bold]{name}[/]' found.");
            return 4;
        }

        if (token.RevokedAt is null)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync();
            AnsiConsole.MarkupLine($"[green]OK[/] - service token '[bold]{name}[/]' revoked.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]WARN:[/] service token '[bold]{name}[/]' was already revoked at {token.RevokedAt:O}.");
        }

        return 0;
    }

    private static int Fail(string msg, int exitCode = 1)
    {
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {msg}");
        return exitCode;
    }
}
