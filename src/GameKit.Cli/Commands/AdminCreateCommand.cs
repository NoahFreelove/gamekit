// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Data.Configurations;
using GameKit.Admin.UI.Entities;
using GameKit.Auth;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands;

/// <summary>
/// CLI command: <c>gamekit admin create</c> (D-08 — ADMIN-11). Creates a first / Nth admin user
/// via an interactive + flag-driven hybrid. The first admin (when <c>admin_users</c> is empty)
/// is auto-promoted to <c>superadmin</c> regardless of the <c>--role</c> flag so there is always
/// an operator who can recover from lockout by provisioning more admins.
/// </summary>
/// <remarks>
/// Exit codes: <c>0</c> success, <c>2</c> validation or conflict failure.
/// <para/>
/// Environment variables (defence-in-depth for CI / docker-entrypoint):
/// <list type="bullet">
///   <item><c>GAMEKIT_CONNECTION</c> — fallback when <c>--connection-string</c> is omitted.</item>
///   <item><c>GAMEKIT_ADMIN_PASSWORD</c> — fallback when <c>--password</c> is omitted AND stdin is redirected.</item>
/// </list>
/// </remarks>
internal sealed class AdminCreateCommand : AsyncCommand<AdminCreateCommand.Settings>
{
    /// <summary>Command-line settings for <c>gamekit admin create</c>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Username (3-32 chars, case-insensitive). If omitted, prompts on stdin.</summary>
        [CommandOption("-u|--username <USERNAME>")]
        [Description("Username (3-32 chars, case-insensitive). Prompted when omitted.")]
        public string? Username { get; init; }

        /// <summary>Password. Minimum 8 chars. If omitted on a TTY, prompted without echo.</summary>
        [CommandOption("-p|--password <PASSWORD>")]
        [Description("Password (>= 8 chars). Prompted without echo when omitted on a TTY. Falls back to env GAMEKIT_ADMIN_PASSWORD in non-TTY contexts.")]
        public string? Password { get; init; }

        /// <summary>
        /// Role (<c>admin</c> or <c>superadmin</c>). Ignored when no admin exists — the first
        /// admin is always auto-promoted to <c>superadmin</c>.
        /// </summary>
        [CommandOption("-r|--role <ROLE>")]
        [Description("Role: admin or superadmin. Ignored for the first admin (auto-promoted to superadmin).")]
        [DefaultValue("admin")]
        public string Role { get; init; } = "admin";

        /// <summary>Explicit Postgres connection string. Falls back to env <c>GAMEKIT_CONNECTION</c>.</summary>
        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string (gamekit_owner role recommended).")]
        public string? ConnectionString { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Connection string: flag > env > fail.
        var conn = settings.ConnectionString
            ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
            return Fail("No connection string. Pass --connection-string or set GAMEKIT_CONNECTION.");

        // Username: flag > interactive prompt.
        var username = settings.Username
            ?? (Console.IsInputRedirected
                ? string.Empty
                : AnsiConsole.Ask<string>("Username:"));
        if (string.IsNullOrWhiteSpace(username))
            return Fail("Username is required. Pass --username or provide one interactively.");

        // Password: flag > env > interactive prompt. In non-TTY (CI / piped stdin) contexts where
        // neither --password nor GAMEKIT_ADMIN_PASSWORD is set, fail loudly — Console.ReadKey
        // against a redirected stdin does NOT mask and leaks the password into the reader's
        // buffer (RESEARCH landmine #8).
        var password = settings.Password
            ?? Environment.GetEnvironmentVariable("GAMEKIT_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            if (Console.IsInputRedirected)
                return Fail("Password prompt requires an interactive terminal. Pass --password or set GAMEKIT_ADMIN_PASSWORD.");
            password = ReadPasswordMasked();
        }

        // Validate.
        if (username.Length is < 3 or > 32)
            return Fail("Username must be 3-32 chars.");
        if (password.Length < 8)
            return Fail("Password must be at least 8 chars.");
        if (settings.Role is not (AdminRoles.Admin or AdminRoles.Superadmin))
            return Fail($"Role must be '{AdminRoles.Admin}' or '{AdminRoles.Superadmin}'.");

        // Build a DbContext wired via ReplaceService<IModelCustomizer, AdminCliModelCustomizer>.
        // Rationale (GSD Rule-3 blocking): EF Core caches the runtime model GLOBALLY per
        // GameKitDbContext type across every service provider in the process, so if any other
        // AddGameKit-based container in the same AppDomain created a context WITHOUT registering
        // IModelBuilderExtension<AdminModelBuilderExtension>, that extension-less model is cached
        // and subsequent AddGameKit+TryAddEnumerable containers reuse it (the cache key does not
        // include the application service provider). Symptom: the CLI tests (which run
        // PostgresFixture-driven Core migration helpers in the same process) crash with
        // "Cannot create a DbSet for 'AdminUser'...". The customizer path bypasses the
        // ApplicationServiceProvider resolution entirely — mirrors the AdminTestHost pattern
        // established in plan 03-06 (deviation #3 in 03-06-SUMMARY).
        //
        // The CLI only needs AdminUser + Core's Player/GameSession/SessionParticipant/AdminAuditLog
        // surface; Auth's entities are never queried from this command, so applying Admin on top
        // of Core's default configurations is sufficient.
        var dbOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(conn!)
            .ReplaceService<IModelCustomizer, AdminCliModelCustomizer>()
            .Options;
        await using var dbCtx = new GameKitDbContext(dbOpts);

        var hasher = new BCryptPasswordHasher(new GameKitAuthOptions());
        var ids = new UuidV7IdGenerator();
        IClock clock = new SystemClock();

        // AUTO-PROMOTE first admin to superadmin (closes the "how does the first admin exist"
        // chicken-and-egg per D-08).
        var zeroAdmins = !await dbCtx.Set<AdminUser>().AnyAsync().ConfigureAwait(false);
        var effectiveRole = zeroAdmins ? AdminRoles.Superadmin : settings.Role;

        // Pre-check uniqueness. A concurrent creator could still race us, so the Postgres unique
        // index on admin_users.username is the final gate; we surface its 23505 as a friendly
        // "username already exists" message (exit 2) rather than a raw stack trace.
        if (await dbCtx.Set<AdminUser>().AnyAsync(a => a.Username == username).ConfigureAwait(false))
            return Fail($"Username '{username}' already exists.");

        var hash = hasher.Hash(password);
        var admin = new AdminUser
        {
            Id = ids.NewId(),
            Username = username,
            PasswordHash = hash,
            Role = effectiveRole,
            CreatedAt = clock.UtcNow,
            LastLoginAt = null,
            FailedLoginCount = 0,
            LockedUntil = null,
        };
        dbCtx.Set<AdminUser>().Add(admin);

        try
        {
            await dbCtx.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (TryFindUniqueViolation(ex))
        {
            return Fail($"Username '{username}' already exists.");
        }

        AnsiConsole.MarkupLine("[green]OK[/] - admin created.");
        AnsiConsole.MarkupLine($"  Username: [bold]{username}[/]");
        AnsiConsole.MarkupLine(
            $"  Role:     [bold]{effectiveRole}[/]{(zeroAdmins ? " (auto-promoted - first admin)" : string.Empty)}");
        AnsiConsole.MarkupLine($"  Hash prefix: [dim]{hash[..Math.Min(8, hash.Length)]}...[/]");
        return 0;
    }

    /// <summary>
    /// Reads a password from the interactive console without echoing characters. Emits <c>*</c>
    /// for each pressed key and supports backspace. Must only be called when
    /// <see cref="Console.IsInputRedirected"/> is <c>false</c> — otherwise <see cref="Console.ReadKey(bool)"/>
    /// leaks the plaintext to the calling process.
    /// </summary>
    private static string ReadPasswordMasked()
    {
        var sb = new StringBuilder();
        Console.Write("Password: ");
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Length--;
                Console.Write("\b \b");
                continue;
            }
            if (!char.IsControl(k.KeyChar))
            {
                sb.Append(k.KeyChar);
                Console.Write('*');
            }
        }
        return sb.ToString();
    }

    /// <summary>Walks an <see cref="Exception"/> chain looking for a Postgres <c>23505</c> unique-violation.</summary>
    private static bool TryFindUniqueViolation(Exception? ex)
    {
        for (var i = 0; i < 8 && ex is not null; i++)
        {
            if (ex is Npgsql.PostgresException { SqlState: "23505" }) return true;
            ex = ex.InnerException;
        }
        return false;
    }

    /// <summary>Emits a red error line and returns the validation exit code.</summary>
    private static int Fail(string msg)
    {
        AnsiConsole.MarkupLine($"[red]ERROR:[/] {msg}");
        return 2;
    }

    /// <summary>
    /// Runtime <see cref="IModelCustomizer"/> used by <c>gamekit admin create</c> to build a
    /// <see cref="GameKitDbContext"/> whose model contains Core's entities (via the base
    /// <see cref="RelationalModelCustomizer"/> call — which invokes
    /// <c>GameKitDbContext.OnModelCreating</c> and its
    /// <c>ApplyConfigurationsFromAssembly(Core)</c>) plus <see cref="AdminUserConfiguration"/>
    /// applied directly on top. Mirrors the AdminTestHost <c>AdminRuntimeQueryCustomizer</c>
    /// from plan 03-06 — same rationale: EF caches the <see cref="GameKitDbContext"/> model
    /// globally across service providers in the same process, so relying on the
    /// <c>ApplicationServiceProvider</c> path to wire <c>AdminModelBuilderExtension</c>
    /// is unreliable across mixed-container test harnesses. Applying Admin directly makes the
    /// model self-contained.
    /// </summary>
    internal sealed class AdminCliModelCustomizer : RelationalModelCustomizer
    {
        /// <summary>Constructs the customizer; forwards dependencies to the base.</summary>
        public AdminCliModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        /// <inheritdoc />
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.ApplyConfiguration(new AdminUserConfiguration());
        }
    }
}
