// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Cli.Commands;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Spectre.Console.Cli;
using Xunit;

namespace GameKit.Cli.Tests;

/// <summary>
/// Integration tests for <c>gamekit admin create</c> (plan 03-11, ADMIN-11).
/// Exercises every D-08 behaviour against a live Postgres via Testcontainers:
/// <list type="bullet">
///   <item>First admin on an empty <c>admin_users</c> is auto-promoted to <c>superadmin</c>
///         regardless of the <c>--role</c> flag.</item>
///   <item>Subsequent admins honour the <c>--role</c> flag.</item>
///   <item>Password shorter than 8 chars returns exit code 2.</item>
///   <item>Duplicate username returns exit code 2.</item>
///   <item>Missing <c>--password</c> flag + <c>Console.IsInputRedirected == true</c>
///         returns exit code 2 with the "non-TTY" guard message.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class AdminCreateCommandTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    // Each test gets its own fresh connection string (new database name) so tests do not
    // step on each other's admin_users rows and so the first-admin auto-promotion path is
    // reproducible per fact.
    private string _cs = string.Empty;

    public AdminCreateCommandTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyAllMigrationsAsync(_cs);

        // Clear any env var leakage between tests that might have survived from earlier test
        // runs or the calling shell; each test sets what it needs explicitly.
        Environment.SetEnvironmentVariable("GAMEKIT_CONNECTION", null);
        Environment.SetEnvironmentVariable("GAMEKIT_ADMIN_PASSWORD", null);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FirstAdmin_IsAutoPromoted_ToSuperadmin_DespiteRoleFlag()
    {
        // Arrange: fresh DB + Core/Auth/Admin migrations applied, admin_users empty.

        // Act: run `admin create -u root -p hunter2hunter2 -r admin -c <cs>`.
        var app = BuildCommandApp();
        var exit = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "root",
            "-p", "hunter2hunter2",
            "-r", "admin",
            "-c", _cs,
        });

        // Assert: exit 0 + the row has role = superadmin (auto-promotion overrode --role admin).
        Assert.Equal(0, exit);
        var role = await FetchRoleAsync(_cs, "root");
        Assert.Equal("superadmin", role);
    }

    [Fact]
    public async Task SecondAdmin_HonoursRoleFlag_WhenSuperadminAlreadyExists()
    {
        // Arrange: seed one superadmin via the first-admin path.
        var app = BuildCommandApp();
        var exit1 = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "root",
            "-p", "hunter2hunter2",
            "-c", _cs,
        });
        Assert.Equal(0, exit1);

        // Act: second invocation with --role admin.
        var exit2 = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "bob",
            "-p", "hunter2hunter2",
            "-r", "admin",
            "-c", _cs,
        });

        // Assert: exit 0 + bob's role = admin (auto-promotion did NOT fire; admin_users non-empty).
        Assert.Equal(0, exit2);
        var role = await FetchRoleAsync(_cs, "bob");
        Assert.Equal("admin", role);
    }

    [Fact]
    public async Task ShortPassword_ReturnsExitCode2()
    {
        // Act: run with a 5-char password.
        var app = BuildCommandApp();
        var exit = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "root",
            "-p", "short",
            "-c", _cs,
        });

        // Assert: exit 2 (validation failure) + no row inserted.
        Assert.Equal(2, exit);
        var role = await FetchRoleAsync(_cs, "root");
        Assert.Null(role);
    }

    [Fact]
    public async Task DuplicateUsername_ReturnsExitCode2()
    {
        // Arrange: seed root.
        var app = BuildCommandApp();
        var exit1 = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "root",
            "-p", "hunter2hunter2",
            "-c", _cs,
        });
        Assert.Equal(0, exit1);

        // Act: create another admin with the same username.
        var exit2 = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "root",
            "-p", "adifferentpassword",
            "-c", _cs,
        });

        // Assert: exit 2; only one row in admin_users.
        Assert.Equal(2, exit2);
        var count = await FetchAdminCountAsync(_cs);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MissingPasswordFlag_WithRedirectedStdin_ReturnsExitCode2()
    {
        // Arrange: the xUnit test host always runs with stdin redirected, so
        // Console.IsInputRedirected == true here. This is the scenario the non-TTY guard
        // was designed for (RESEARCH landmine #8): refuse to fall back to Console.ReadKey
        // when stdin is piped, because ReadKey against a redirected stdin leaks plaintext.
        Assert.True(Console.IsInputRedirected,
            "Precondition for this test: xUnit test-host stdin must be redirected.");
        Environment.SetEnvironmentVariable("GAMEKIT_ADMIN_PASSWORD", null);

        // Act: invoke with --username but no --password and no GAMEKIT_ADMIN_PASSWORD.
        var app = BuildCommandApp();
        var exit = await app.RunAsync(new[]
        {
            "admin", "create",
            "-u", "root",
            "-c", _cs,
        });

        // Assert: exit 2 + no row inserted.
        Assert.Equal(2, exit);
        var role = await FetchRoleAsync(_cs, "root");
        Assert.Null(role);
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="CommandApp"/> identical to Program.cs so tests exercise the same
    /// branch topology end operators see on the CLI.
    /// </summary>
    private static CommandApp BuildCommandApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("gamekit");
            config.AddBranch("admin", admin =>
            {
                admin.AddCommand<AdminCreateCommand>("create");
            });
        });
        return app;
    }

    /// <summary>
    /// Creates a fresh, isolated database on the same Postgres container and returns its
    /// owner connection string. Test isolation: each test gets its own DB so admin_users
    /// mutations never bleed across facts (and the first-admin auto-promotion path is
    /// reproducible).
    /// </summary>
    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_cli_" + Guid.NewGuid().ToString("N")[..12];

        // Bootstrap role (postgres) can CREATE DATABASE. The init scripts have already
        // created gamekit_owner/app/reader roles at the container level.
        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync().ConfigureAwait(false);
            await using (var createDb = bootstrap.CreateCommand())
            {
                createDb.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
                await createDb.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        // Swap the database name on the owner connection string.
        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString)
        {
            Database = dbName,
        };

        // The citext + gamekit schema are in the per-container init scripts mounted by the
        // fixture, but those only run against the template / initial database. Add them here
        // for our freshly-minted DB.
        await using (var freshConn = new NpgsqlConnection(builder.ConnectionString))
        {
            await freshConn.OpenAsync().ConfigureAwait(false);
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return builder.ConnectionString;
    }

    /// <summary>Applies Core + Auth + Admin migrations in order to the supplied database.</summary>
    private static async Task ApplyAllMigrationsAsync(string cs)
    {
        // Pass 1 — Core migrations via the runtime AddGameKit path.
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.AutoMigrate = false;
        });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx).ConfigureAwait(false);
        }

        // Pass 2 — Auth migrations (separate context; AuthMigrationModelCustomizer).
        await using (var authCtx = BuildAuthMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                authCtx, AuthMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }

        // Pass 3 — Admin migrations (separate context; AdminMigrationModelCustomizer).
        await using (var adminCtx = BuildAdminMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                adminCtx, AdminMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
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
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }

    private static async Task<string?> FetchRoleAsync(string cs, string username)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Role\" FROM gamekit.admin_users WHERE \"Username\" = @u";
        cmd.Parameters.AddWithValue("u", username);
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return result as string;
    }

    private static async Task<long> FetchAdminCountAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM gamekit.admin_users";
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return (long)(result ?? 0L);
    }
}
