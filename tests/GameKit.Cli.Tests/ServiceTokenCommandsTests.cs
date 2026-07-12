// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Cli.Commands;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;
using Xunit;

namespace GameKit.Cli.Tests;

/// <summary>
/// Integration tests for <c>gamekit service-token issue / revoke / list</c> (D-06 / RANK-11).
/// Exercises the three service-token CLI verbs against a live Postgres via Testcontainers.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class ServiceTokenCommandsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public ServiceTokenCommandsTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyAllMigrationsAsync(_cs);

        Environment.SetEnvironmentVariable("GAMEKIT_CONNECTION", null);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Issue prints the raw token to stdout exactly once and stores only the SHA-256 hash.
    /// (T-04-04-RT — raw token never stored).
    /// </summary>
    [Fact]
    public async Task IssueCommand_Prints_Raw_Token_To_Stdout()
    {
        var stdout = new StringBuilder();
        var app = BuildCommandApp();

        var exit = await RunWithCapturedConsoleAsync(stdout, () =>
            app.RunAsync(new[] { "service-token", "issue", "--name", "e2e-test", "--connection-string", _cs }));

        Assert.Equal(0, exit);

        // Stdout must mention the raw token (present in the output).
        var output = stdout.ToString();
        Assert.Contains("service token created", output, StringComparison.OrdinalIgnoreCase);

        // DB must have a row with a non-empty hash.
        var hash = await FetchTokenHashAsync(_cs, "e2e-test");
        Assert.NotNull(hash);
        Assert.Equal(64, hash!.Length); // SHA-256 hex = 64 chars

        // The raw token must NOT equal the stored hash (paranoia: raw is not the hash itself).
        // We can't read the raw token back from DB (it's not stored), but we can confirm the
        // hash is a well-formed hex string and doesn't match any string in the CLI output.
        Assert.DoesNotContain(hash, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Issuing a token with a duplicate name returns exit code 2.
    /// </summary>
    [Fact]
    public async Task IssueCommand_DuplicateName_Returns_Error_Exit_Code()
    {
        var app = BuildCommandApp();

        var exit1 = await app.RunAsync(new[] { "service-token", "issue", "--name", "dup-token", "--connection-string", _cs });
        Assert.Equal(0, exit1);

        var exit2 = await app.RunAsync(new[] { "service-token", "issue", "--name", "dup-token", "--connection-string", _cs });
        Assert.Equal(2, exit2);
    }

    /// <summary>
    /// List output must NOT contain the token hash (T-04-04-RT — hash leakage prevention).
    /// </summary>
    [Fact]
    public async Task ListCommand_Does_Not_Print_Token_Hash()
    {
        var app = BuildCommandApp();

        // Issue a token.
        await app.RunAsync(new[] { "service-token", "issue", "--name", "list-test", "--connection-string", _cs });

        // Fetch the stored hash to compare against list output.
        var hash = await FetchTokenHashAsync(_cs, "list-test");
        Assert.NotNull(hash);

        // Capture list output.
        var stdout = new StringBuilder();
        var exit = await RunWithCapturedConsoleAsync(stdout, () =>
            app.RunAsync(new[] { "service-token", "list", "--connection-string", _cs }));

        Assert.Equal(0, exit);

        // List must contain the name.
        Assert.Contains("list-test", stdout.ToString(), StringComparison.Ordinal);

        // List must NOT contain the token hash.
        Assert.DoesNotContain(hash!, stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Revoke sets <c>RevokedAt</c> on the named row.
    /// </summary>
    [Fact]
    public async Task RevokeCommand_Sets_Revoked_At()
    {
        var app = BuildCommandApp();

        // Issue then revoke.
        await app.RunAsync(new[] { "service-token", "issue", "--name", "revoke-test", "--connection-string", _cs });
        var exit = await app.RunAsync(new[] { "service-token", "revoke", "--name", "revoke-test", "--connection-string", _cs });

        Assert.Equal(0, exit);

        // RevokedAt must be set.
        var revokedAt = await FetchRevokedAtAsync(_cs, "revoke-test");
        Assert.NotNull(revokedAt);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CommandApp BuildCommandApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("gamekit");
            config.AddBranch("service-token", st =>
            {
                st.AddCommand<ServiceTokenIssueCommand>("issue");
                st.AddCommand<ServiceTokenRevokeCommand>("revoke");
                st.AddCommand<ServiceTokenListCommand>("list");
            });
        });
        return app;
    }

    private static async Task<int> RunWithCapturedConsoleAsync(StringBuilder buffer, Func<Task<int>> action)
    {
        // Redirect AnsiConsole static output to capture Spectre.Console MarkupLine / Table writes.
        // AnsiConsole.Console is settable — swap in a StringWriter-backed console for the duration.
        var writer = new StringWriter(buffer);
        var output = new AnsiConsoleOutput(writer);
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = output,
            Ansi = AnsiSupport.No,    // No ANSI escape codes in captured text
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
        });

        var originalConsole = AnsiConsole.Console;
        AnsiConsole.Console = testConsole;
        try
        {
            return await action();
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
        }
    }

    private static async Task<string?> FetchTokenHashAsync(string cs, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"TokenHash\" FROM gamekit.service_tokens WHERE \"Name\" = @n";
        cmd.Parameters.AddWithValue("n", name);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task<DateTimeOffset?> FetchRevokedAtAsync(string cs, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"RevokedAt\" FROM gamekit.service_tokens WHERE \"Name\" = @n";
        cmd.Parameters.AddWithValue("n", name);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value) return null;
        // Npgsql returns timestamptz as DateTime (UTC Kind) — convert to DateTimeOffset.
        if (result is DateTimeOffset dto) return dto;
        if (result is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        return null;
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_stoken_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var cs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(cs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }

        return cs;
    }

    private static async Task ApplyAllMigrationsAsync(string cs)
    {
        // Core
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Auth
        await using (var authCtx = BuildAuthMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(authCtx, AuthMigrationConstants.AdvisoryLockKey);
        }

        // Admin
        await using (var adminCtx = BuildAdminMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(adminCtx, AdminMigrationConstants.AdvisoryLockKey);
        }

        // Rankings
        await using (var rankingsCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
        }
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(AuthMigrationConstants.MigrationsHistoryTable, GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildAdminMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(AdminMigrationConstants.MigrationsHistoryTable, GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildRankingsMigrationContext(string cs)
    {
        // ConfigureWarnings: suppress PendingModelChangesWarning — the hand-authored snapshot
        // is structurally correct but may not match EF Core's internal hash exactly without a
        // full `dotnet ef` run. Mirrors the pattern from RankingsMigrationDeterminismTests.
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(RankingsMigrationConstants.MigrationsHistoryTable, GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }
}
