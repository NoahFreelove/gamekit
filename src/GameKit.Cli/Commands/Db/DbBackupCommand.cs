// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;
using StackExchange.Redis;

namespace GameKit.Cli.Commands.Db;

/// <summary>
/// CLI command: <c>gamekit db backup</c>.
/// <para>
/// Backs up a Postgres database by shelling out to <c>pg_dump</c> (which must be on the
/// operator's PATH — it is NOT bundled with GameKit). The Postgres password is passed via
/// <c>PGPASSWORD</c> in the child-process environment, never as a command-line argument,
/// so it is not visible in <c>ps</c> output (T-17-04-02).
/// </para>
/// <para>
/// Optionally, when <c>--redis-connection</c> is supplied, issues a Redis <c>BGSAVE</c>
/// via StackExchange.Redis (no <c>redis-cli</c> binary required). The operator is
/// responsible for copying the resulting RDB file from the Redis data directory.
/// See <c>docs/runbooks/redis-backup-restore.md</c>.
/// </para>
/// </summary>
internal sealed class DbBackupCommand : AsyncCommand<DbBackupCommand.Settings>
{
    /// <summary>CLI arguments for <c>gamekit db backup</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Postgres connection string. Falls back to env <c>GAMEKIT_MIGRATIONS_CONNECTION</c>
        /// then <c>GAMEKIT_CONNECTION</c>.
        /// </summary>
        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string. Falls back to GAMEKIT_MIGRATIONS_CONNECTION / GAMEKIT_CONNECTION.")]
        public string? ConnectionString { get; init; }

        /// <summary>
        /// Absolute path for the output dump file. Must be absolute; must not contain <c>..</c>.
        /// Path traversal is rejected before <c>pg_dump</c> is started (T-17-04-01).
        /// </summary>
        [CommandOption("-o|--output <PATH>")]
        [Description("Absolute path for the pg_dump output file (e.g. /srv/backups/game.pgdump). Required.")]
        public string? OutputPath { get; init; }

        /// <summary>
        /// Optional Redis connection string. When supplied, issues a <c>BGSAVE</c> via
        /// StackExchange.Redis (no redis-cli dependency).
        /// </summary>
        [CommandOption("-r|--redis-connection <CONN>")]
        [Description("Redis connection string (e.g. localhost:6379). When supplied, issues BGSAVE to snapshot Redis.")]
        public string? RedisConnection { get; init; }
    }

    /// <summary>
    /// Builds <see cref="ProcessStartInfo"/> for <c>pg_dump</c> from parsed connection-string components.
    /// This internal method is a seam for unit testing: callers can assert that
    /// <c>PGPASSWORD</c> is in <see cref="ProcessStartInfo.Environment"/> and absent from
    /// <see cref="ProcessStartInfo.Arguments"/> without running the binary.
    /// </summary>
    /// <param name="host">Postgres host.</param>
    /// <param name="port">Postgres port.</param>
    /// <param name="database">Target database name.</param>
    /// <param name="username">Postgres role.</param>
    /// <param name="password">Password — placed in <c>PGPASSWORD</c> env var, never in args (T-17-04-02).</param>
    /// <param name="outputPath">Validated absolute destination path for the dump file.</param>
    /// <returns>A ready-to-start <see cref="ProcessStartInfo"/> for <c>pg_dump</c>.</returns>
    internal static ProcessStartInfo BuildPgDumpStartInfo(
        string host, int port, string database, string username, string? password, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pg_dump",
            // Password intentionally OMITTED from arguments (T-17-04-02)
            Arguments = $"--host={host} --port={port} --username={username} " +
                        $"--format=custom --file={outputPath} {database}",
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
        };

        // T-17-04-02 mitigation: pass password via environment variable, not CLI arg
        if (!string.IsNullOrEmpty(password))
            psi.Environment["PGPASSWORD"] = password;

        return psi;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // 1. Resolve connection string
        var conn = settings.ConnectionString
            ?? Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION");

        if (string.IsNullOrWhiteSpace(conn))
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] No connection string. Pass --connection-string or set GAMEKIT_MIGRATIONS_CONNECTION / GAMEKIT_CONNECTION.");
            return 2;
        }

        // 2. Validate output path (T-17-04-01: path traversal guard)
        if (string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] --output is required. Provide an absolute path for the dump file.");
            return 2;
        }

        if (!BackupPathValidator.IsSafeAbsolutePath(settings.OutputPath))
        {
            AnsiConsole.MarkupLine(
                "[red]ERROR:[/] --output path must be absolute and must not contain '..' segments. " +
                $"Rejected: {Markup.Escape(settings.OutputPath)}");
            return 2;
        }

        // 3. Parse connection string to extract individual components
        NpgsqlConnectionStringBuilder csb;
        try
        {
            csb = new NpgsqlConnectionStringBuilder(conn);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] Could not parse connection string: {Markup.Escape(ex.Message)}");
            return 2;
        }

        var host     = csb.Host     ?? "localhost";
        var port     = csb.Port != 0 ? csb.Port : 5432;
        var database = csb.Database ?? string.Empty;
        var username = csb.Username ?? string.Empty;
        var password = csb.Password;

        if (string.IsNullOrWhiteSpace(database))
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] Connection string does not specify a database name.");
            return 2;
        }

        // 4. Build ProcessStartInfo — password via environment only (T-17-04-02)
        var psi = BuildPgDumpStartInfo(host, port, database, username, password, settings.OutputPath);

        AnsiConsole.MarkupLine($"[grey]Running:[/] pg_dump --host={Markup.Escape(host)} " +
            $"--port={port} --username={Markup.Escape(username)} " +
            $"--format=custom --file={Markup.Escape(settings.OutputPath)} {Markup.Escape(database)}");
        AnsiConsole.MarkupLine("[grey](pg_dump must be on the operator's PATH — it is not bundled with GameKit)[/]");

        // 5. Run pg_dump
        using var proc = Process.Start(psi);
        if (proc is null)
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] Failed to start pg_dump. Ensure pg_dump is on the operator's PATH.");
            return 1;
        }

        var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await proc.WaitForExitAsync().ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] pg_dump exited with code {proc.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(stderr))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(stderr)}[/]");
            return proc.ExitCode;
        }

        AnsiConsole.MarkupLine($"[green]OK — Postgres backup written to:[/] {Markup.Escape(settings.OutputPath)}");

        // 6. Optional Redis BGSAVE (T-17-04-02 not applicable here — no password in CLI args)
        if (!string.IsNullOrWhiteSpace(settings.RedisConnection))
        {
            int redisResult = await BackupRedisAsync(settings.RedisConnection).ConfigureAwait(false);
            if (redisResult != 0)
                return redisResult;
        }

        return 0;
    }

    private static async Task<int> BackupRedisAsync(string redisConnection)
    {
        AnsiConsole.MarkupLine("[grey]Connecting to Redis to issue BGSAVE...[/]");
        try
        {
            await using var mux = await ConnectionMultiplexer.ConnectAsync(redisConnection).ConfigureAwait(false);
            foreach (var endPoint in mux.GetEndPoints())
            {
                var server = mux.GetServer(endPoint);
                if (!server.IsReplica)
                {
                    await server.SaveAsync(SaveType.BackgroundSave).ConfigureAwait(false);
                    var dirConfig = await server.ConfigGetAsync("dir").ConfigureAwait(false);
                    var rdbDir = dirConfig?.Length > 0 ? dirConfig[0].Value : "(unknown)";
                    AnsiConsole.MarkupLine(
                        $"[green]OK — Redis BGSAVE issued to {Markup.Escape(endPoint.ToString() ?? string.Empty)}.[/]");
                    AnsiConsole.MarkupLine(
                        $"[grey]Redis data directory (where RDB is written): {Markup.Escape(rdbDir)}[/]");
                    AnsiConsole.MarkupLine(
                        "[yellow]NOTE:[/] The Redis RDB file is written to the Redis data directory on the Redis host. " +
                        "Copy it manually to your backup destination. " +
                        "See docs/runbooks/redis-backup-restore.md for the full procedure.");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] Redis BGSAVE failed: {Markup.Escape(ex.Message)}");
            return 1;
        }

        return 0;
    }
}
