// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands.Db;

/// <summary>
/// CLI command: <c>gamekit db restore</c>.
/// <para>
/// Restores a Postgres database by shelling out to <c>pg_restore</c> (which must be on the
/// operator's PATH — it is NOT bundled with GameKit). The Postgres password is passed via
/// <c>PGPASSWORD</c> in the child-process environment, never as a command-line argument,
/// so it is not visible in <c>ps</c> output (T-17-04-02).
/// </para>
/// <para>
/// The target database is specified via the explicit <c>--database</c> flag rather than
/// being inferred from the connection string alone. This forces the operator to state the
/// restore target explicitly, preventing a silent restore into the wrong database due to a
/// connection-string typo (T-17-04-03).
/// </para>
/// <para>
/// <b>Prerequisite:</b> <c>pg_restore</c> must be on the operator's PATH (installed as part
/// of the Postgres client tools package). GameKit does not bundle or distribute <c>pg_restore</c>.
/// </para>
/// </summary>
internal sealed class DbRestoreCommand : AsyncCommand<DbRestoreCommand.Settings>
{
    /// <summary>CLI arguments for <c>gamekit db restore</c>.</summary>
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
        /// Absolute path to the dump file produced by <c>pg_dump --format=custom</c>.
        /// Must be absolute; must not contain <c>..</c> segments (T-17-04-01).
        /// </summary>
        [CommandOption("-f|--file <PATH>")]
        [Description("Absolute path to the pg_dump custom-format dump file to restore. Required.")]
        public string? FilePath { get; init; }

        /// <summary>
        /// The name of the database to restore into. Required — must be explicit to prevent
        /// accidental restore into the wrong database (T-17-04-03).
        /// </summary>
        [CommandOption("-d|--database <NAME>")]
        [Description("Explicit target database name. Required — prevents silent restore into the wrong DB (T-17-04-03).")]
        public string? Database { get; init; }
    }

    /// <summary>
    /// Builds <see cref="ProcessStartInfo"/> for <c>pg_restore</c> from parsed connection-string components.
    /// This internal method is a seam for unit testing.
    /// </summary>
    internal static ProcessStartInfo BuildPgRestoreStartInfo(
        string host, int port, string database, string username, string? password, string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pg_restore",
            // Password intentionally OMITTED from arguments (T-17-04-02)
            Arguments = $"--host={host} --port={port} --username={username} " +
                        $"--dbname={database} --no-owner --no-privileges {filePath}",
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

        // 2. Validate dump file path (T-17-04-01: path traversal guard)
        if (string.IsNullOrWhiteSpace(settings.FilePath))
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] --file is required. Provide an absolute path to the dump file.");
            return 2;
        }

        if (!BackupPathValidator.IsSafeAbsolutePath(settings.FilePath))
        {
            AnsiConsole.MarkupLine(
                "[red]ERROR:[/] --file path must be absolute and must not contain '..' segments. " +
                $"Rejected: {Markup.Escape(settings.FilePath)}");
            return 2;
        }

        // 3. Require explicit --database (T-17-04-03 mitigation)
        if (string.IsNullOrWhiteSpace(settings.Database))
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] --database is required. Specify the exact target database name to prevent accidental restore into the wrong DB.");
            return 2;
        }

        // 4. Parse connection string for host/port/user/password
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
        var username = csb.Username ?? string.Empty;
        var password = csb.Password;

        // 5. Print resolved target so the operator can confirm before proceeding (T-17-04-03)
        AnsiConsole.MarkupLine("[bold yellow]Restore target:[/]");
        AnsiConsole.MarkupLine($"  Host:     [cyan]{Markup.Escape(host)}:{port}[/]");
        AnsiConsole.MarkupLine($"  Database: [cyan]{Markup.Escape(settings.Database)}[/]");
        AnsiConsole.MarkupLine($"  File:     [cyan]{Markup.Escape(settings.FilePath)}[/]");

        // 6. Build ProcessStartInfo — password via environment only (T-17-04-02)
        var psi = BuildPgRestoreStartInfo(host, port, settings.Database, username, password, settings.FilePath);

        AnsiConsole.MarkupLine($"[grey]Running:[/] pg_restore --host={Markup.Escape(host)} " +
            $"--port={port} --username={Markup.Escape(username)} " +
            $"--dbname={Markup.Escape(settings.Database)} --no-owner --no-privileges {Markup.Escape(settings.FilePath)}");
        AnsiConsole.MarkupLine("[grey](pg_restore must be on the operator's PATH — it is not bundled with GameKit)[/]");

        // 7. Run pg_restore
        using var proc = Process.Start(psi);
        if (proc is null)
        {
            AnsiConsole.MarkupLine("[red]ERROR:[/] Failed to start pg_restore. Ensure pg_restore is on the operator's PATH.");
            return 1;
        }

        var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await proc.WaitForExitAsync().ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[red]ERROR:[/] pg_restore exited with code {proc.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(stderr))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(stderr)}[/]");
            return proc.ExitCode;
        }

        AnsiConsole.MarkupLine($"[green]OK — Postgres restore complete. Database:[/] {Markup.Escape(settings.Database)}");
        return 0;
    }
}
