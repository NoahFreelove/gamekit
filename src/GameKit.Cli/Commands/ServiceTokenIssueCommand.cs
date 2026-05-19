// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands;

/// <summary>
/// CLI command: <c>gamekit service-token issue</c> (D-06 / RANK-11). Mints a new service-account
/// bearer token, prints the raw bearer to stdout exactly once (never re-printable after this
/// invocation), and stores only the SHA-256 hex digest in <c>service_tokens</c>.
/// </summary>
/// <remarks>
/// Exit codes: <c>0</c> success, <c>1</c> missing required input, <c>2</c> duplicate name conflict.
/// <para/>
/// Connection string: <c>--connection-string</c> flag or <c>GAMEKIT_CONNECTION</c> env var.
/// </remarks>
internal sealed class ServiceTokenIssueCommand : AsyncCommand<ServiceTokenIssueCommand.Settings>
{
    /// <summary>Command-line settings for <c>gamekit service-token issue</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Name / label for this service token. Required.</summary>
        [CommandOption("-n|--name <NAME>")]
        [Description("Label for this service token (e.g. \"game-server-prod\"). Must be unique.")]
        public string? Name { get; init; }

        /// <summary>Optional expiry as an ISO-8601 duration (e.g. <c>P30D</c>) or absolute UTC datetime.</summary>
        [CommandOption("--expires <DURATION>")]
        [Description("Optional expiry as ISO-8601 duration (e.g. P30D) or UTC datetime (e.g. 2027-01-01T00:00:00Z).")]
        public string? Expires { get; init; }

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

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(settings.Expires))
        {
            if (!TryParseExpiry(settings.Expires, out expiresAt))
                return Fail($"Could not parse expiry '{settings.Expires}'. Use ISO-8601 duration (P30D) or UTC datetime.", exitCode: 1);
        }

        var dbOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(conn)
            .ReplaceService<IModelCustomizer, RankingsCliModelCustomizer>()
            .Options;
        await using var ctx = new GameKitDbContext(dbOpts);

        var ids = new UuidV7IdGenerator();
        IClock clock = new SystemClock();

        var raw = GenerateRaw();
        var hash = Sha256Hex(raw);

        var token = new ServiceToken
        {
            Id = ids.NewId(),
            Name = name,
            TokenHash = hash,
            CreatedAt = clock.UtcNow,
            ExpiresAt = expiresAt,
        };

        ctx.Set<ServiceToken>().Add(token);

        try
        {
            await ctx.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (TryFindUniqueViolation(ex))
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] A service token named '[bold]{name}[/]' already exists.");
            return 2;
        }

        AnsiConsole.MarkupLine("[green]OK[/] - service token created. Copy the raw token NOW; it will not be shown again:");
        AnsiConsole.MarkupLine($"  [bold]{raw}[/]");
        AnsiConsole.MarkupLine($"  [dim]Hash prefix: {hash[..8]}...[/]");
        if (expiresAt.HasValue)
            AnsiConsole.MarkupLine($"  Expires: {expiresAt.Value:O}");

        return 0;
    }

    private static string GenerateRaw()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Sha256Hex(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool TryParseExpiry(string input, out DateTimeOffset? result)
    {
        // Try absolute datetime first.
        if (DateTimeOffset.TryParse(input, out var abs))
        {
            result = abs;
            return true;
        }

        // Try ISO-8601 duration (P30D, PT1H, etc.).
        try
        {
            var duration = System.Xml.XmlConvert.ToTimeSpan(input);
            result = DateTimeOffset.UtcNow.Add(duration);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    private static bool TryFindUniqueViolation(Exception? ex)
    {
        for (var i = 0; i < 8 && ex is not null; i++)
        {
            if (ex is Npgsql.PostgresException { SqlState: "23505" }) return true;
            ex = ex.InnerException;
        }
        return false;
    }

    private static int Fail(string msg, int exitCode = 1)
    {
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {msg}");
        return exitCode;
    }
}
