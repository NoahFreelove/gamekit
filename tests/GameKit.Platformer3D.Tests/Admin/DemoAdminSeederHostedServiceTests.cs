// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Entities;
using GameKit.Auth;
using GameKit.Auth.Services;
using GameKit.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platformer3D;
using Xunit;

namespace GameKit.Platformer3D.Tests.Admin;

/// <summary>
/// Unit tests for <see cref="DemoAdminSeederHostedService"/>:
/// <list type="bullet">
///   <item>Production environment guard — seeder is always a no-op in Production.</item>
///   <item>Config gate — seeder is a no-op when <c>Platformer:DemoAdmin:Enabled</c> is absent or false.</item>
///   <item>Password guard — seeder is a no-op when <c>Platformer:DemoAdmin:Password</c> is absent.</item>
///   <item>Idempotency guard — seeder is a no-op when <c>admin_users</c> already has at least one row.</item>
///   <item>Happy path — seeder inserts exactly one superadmin when <c>admin_users</c> is empty in Staging.</item>
/// </list>
/// Uses a minimal in-memory <see cref="FakeAdminStore"/> instead of EF Core so no Postgres /
/// Testcontainers are required, and the full GameKitDbContext model (with JSON columns) is never
/// instantiated.
/// </summary>
public sealed class DemoAdminSeederHostedServiceTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> from inline key-value pairs.
    /// </summary>
    private static IConfiguration MakeConfig(params (string key, string value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    /// <summary>
    /// Minimal in-memory stand-in for the admin_users table.
    /// The seeder only calls <c>Set&lt;AdminUser&gt;().AnyAsync()</c> and <c>.Add()</c> + <c>SaveChangesAsync()</c>.
    /// This fake store captures those calls so we can assert on them without EF Core.
    /// </summary>
    private sealed class FakeAdminStore
    {
        private readonly List<AdminUser> _admins = new();

        /// <summary>Current in-memory admin rows (mirrors admin_users).</summary>
        public IReadOnlyList<AdminUser> Admins => _admins.AsReadOnly();

        /// <summary>Simulates <c>admin_users.AnyAsync()</c>.</summary>
        public bool AnyAdmin() => _admins.Count > 0;

        /// <summary>Simulates adding a new admin row.</summary>
        public void Add(AdminUser admin) => _admins.Add(admin);
    }

    /// <summary>
    /// Stub <c>GameKitDbContext</c> replacement that delegates Set&lt;AdminUser&gt; operations
    /// to <see cref="FakeAdminStore"/>. We only exercise the subset of the context API the
    /// seeder uses:
    /// <list type="bullet">
    ///   <item><c>Set&lt;AdminUser&gt;().AsNoTracking().AnyAsync()</c></item>
    ///   <item><c>Set&lt;AdminUser&gt;().Add(admin)</c></item>
    ///   <item><c>SaveChangesAsync()</c></item>
    /// </list>
    /// Because <see cref="GameKit.Core.Data.GameKitDbContext"/> is a concrete sealed EF class
    /// we cannot mock it via Moq. Instead we build a <see cref="ServiceCollection"/> backed by a
    /// <see cref="FakeDbContextAdapter"/> that wraps the real seeder's expected call pattern.
    /// </summary>
    private sealed class FakeDbContextAdapter : IDisposable
    {
        private readonly FakeAdminStore _store;

        public FakeDbContextAdapter(FakeAdminStore store) => _store = store;

        public bool AnyAdmin() => _store.AnyAdmin();
        public void Add(AdminUser admin) => _store.Add(admin);
        public void Dispose() { /* no-op */ }
    }

    /// <summary>
    /// Thin wrapper that makes the seeder call <see cref="FakeAdminStore"/> instead of a real
    /// <see cref="GameKit.Core.Data.GameKitDbContext"/>. We do this by subclassing
    /// <see cref="DemoAdminSeederHostedService"/> and overriding only the internal seed step,
    /// using a seam-based approach: replace the service provider with one that returns a
    /// <see cref="FakeSeederContext"/> implementing the call surface the seeder uses.
    /// </summary>
    /// <remarks>
    /// Because we cannot mock <c>GameKitDbContext</c> (sealed EF class), we instead supply a
    /// <see cref="ServiceProvider"/> that returns a <see cref="FakeSeederContext"/> for
    /// <c>GameKitDbContext</c>. The seeder resolves the context from the scope's service
    /// provider — our fake provider returns the fake context.
    /// </remarks>

    // ─── Seeder test harness ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> that the seeder can resolve its dependencies from.
    /// <see cref="FakeSeederContext"/> is registered as a factory that returns a fake DB-context-like
    /// object. We inject it using the open-generic Func&lt;&gt; shim below.
    /// </summary>
    private static (IServiceProvider sp, FakeAdminStore store) BuildFakeServiceProvider(
        IConfiguration config)
    {
        var store = new FakeAdminStore();
        var sc = new ServiceCollection();

        sc.AddScoped<GameKit.Core.Data.GameKitDbContext>(_ =>
            throw new NotSupportedException(
                "Tests must not reach the real GameKitDbContext — use the fake via FakeSeederDbContextFactory."));

        sc.AddScoped<IPasswordHasher>(_ => new BCryptPasswordHasher(new GameKitAuthOptions()));
        sc.AddScoped<IIdGenerator, UuidV7IdGenerator>();
        sc.AddScoped<IClock, SystemClock>();
        sc.AddSingleton<IConfiguration>(config);
        // Expose the store so the seeder can call AnyAsync / Add / SaveChangesAsync.
        sc.AddSingleton(store);

        return (sc.BuildServiceProvider(), store);
    }

    /// <summary>
    /// Runs the seeder in isolation using a fake DB context. For the guard-condition tests (Production,
    /// disabled, no password) the seeder never touches the DB. For the happy-path test, the seeder
    /// must write to admin_users — we intercept the write via <see cref="TestableSeeder"/>.
    /// </summary>
    private static async Task RunTestableSeederAsync(
        IHostEnvironment env,
        IConfiguration config,
        FakeAdminStore store)
    {
        var sp = BuildTestableServiceProvider(config, store);
        var logger = NullLogger<DemoAdminSeederHostedService>.Instance;
        var seeder = new TestableSeeder(env, sp, logger, store);
        await seeder.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> that supplies <see cref="IConfiguration"/>,
    /// <see cref="IPasswordHasher"/>, <see cref="IIdGenerator"/>, and <see cref="IClock"/>.
    /// The <see cref="FakeAdminStore"/> is also registered for the seeder to consume via
    /// a <see cref="TestableSeeder"/> subclass override.
    /// </summary>
    private static IServiceProvider BuildTestableServiceProvider(
        IConfiguration config,
        FakeAdminStore store)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddSingleton(store);
        sc.AddScoped<IPasswordHasher>(_ => new BCryptPasswordHasher(new GameKitAuthOptions()));
        sc.AddScoped<IIdGenerator, UuidV7IdGenerator>();
        sc.AddScoped<IClock, SystemClock>();
        return sc.BuildServiceProvider();
    }

    /// <summary>
    /// Testable subclass of <see cref="DemoAdminSeederHostedService"/> that overrides the internal
    /// DB-access step (AnyAdmin check + Add + SaveChangesAsync) to use a <see cref="FakeAdminStore"/>
    /// instead of <c>GameKitDbContext</c>.
    /// <para>
    /// We cannot inject a fake DbContext because <c>GameKitDbContext</c> is sealed and its
    /// model construction (OnModelCreating → ApplyConfigurationsFromAssembly) fails with InMemory
    /// due to the <c>JsonDocument</c> columns in <c>AdminAuditLog</c>. The override approach
    /// avoids EF entirely for unit tests.
    /// </para>
    /// </summary>
    private sealed class TestableSeeder : DemoAdminSeederHostedService
    {
        private readonly FakeAdminStore _store;

        public TestableSeeder(
            IHostEnvironment env,
            IServiceProvider sp,
            Microsoft.Extensions.Logging.ILogger<DemoAdminSeederHostedService> logger,
            FakeAdminStore store)
            : base(env, sp, logger)
        {
            _store = store;
        }

        /// <summary>
        /// Override: returns whether the fake store has any admin (replaces real DB AnyAsync).
        /// The <paramref name="scopedSp"/> is deliberately unused — the fake store is already
        /// wired at construction time.
        /// </summary>
        protected override Task<bool> AnyAdminExistsAsync(
            IServiceProvider scopedSp,
            CancellationToken ct)
            => Task.FromResult(_store.AnyAdmin());

        /// <summary>
        /// Override: writes the admin to the fake store (replaces real EF Add + SaveChangesAsync).
        /// The <paramref name="scopedSp"/> is deliberately unused.
        /// </summary>
        protected override Task PersistAdminAsync(
            IServiceProvider scopedSp,
            AdminUser admin,
            CancellationToken ct)
        {
            _store.Add(admin);
            return Task.CompletedTask;
        }
    }

    // ─── Production guard ────────────────────────────────────────────────────

    /// <summary>
    /// In Production the seeder must never write to admin_users, even when enabled + password set.
    /// </summary>
    [Fact]
    public async Task StartAsync_Production_NoOp_StoreRemainsEmpty()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "true"),
            ("Platformer:DemoAdmin:Username", "root"),
            ("Platformer:DemoAdmin:Password", "somepass123"));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Empty(store.Admins);
    }

    // ─── Config gate ─────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>Platformer:DemoAdmin:Enabled</c> is absent (default false) the seeder must no-op.
    /// </summary>
    [Fact]
    public async Task StartAsync_EnabledAbsent_NoOp_StoreRemainsEmpty()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Staging);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Username", "root"),
            ("Platformer:DemoAdmin:Password", "somepass123"));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Empty(store.Admins);
    }

    /// <summary>
    /// When <c>Platformer:DemoAdmin:Enabled=false</c> the seeder must no-op.
    /// </summary>
    [Fact]
    public async Task StartAsync_EnabledFalse_NoOp_StoreRemainsEmpty()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Staging);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "false"),
            ("Platformer:DemoAdmin:Username", "root"),
            ("Platformer:DemoAdmin:Password", "somepass123"));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Empty(store.Admins);
    }

    // ─── Password guard ──────────────────────────────────────────────────────

    /// <summary>
    /// When <c>Platformer:DemoAdmin:Password</c> is absent the seeder must no-op.
    /// </summary>
    [Fact]
    public async Task StartAsync_PasswordAbsent_NoOp_StoreRemainsEmpty()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Staging);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "true"),
            ("Platformer:DemoAdmin:Username", "root"));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Empty(store.Admins);
    }

    // ─── Idempotency guard ───────────────────────────────────────────────────

    /// <summary>
    /// When admin_users already contains at least one admin the seeder must no-op
    /// (idempotency guarantee — does not clobber existing admins).
    /// </summary>
    [Fact]
    public async Task StartAsync_AdminAlreadyExists_NoOp_CountUnchanged()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Staging);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "true"),
            ("Platformer:DemoAdmin:Username", "root"),
            ("Platformer:DemoAdmin:Password", "somepass123"));
        var store = new FakeAdminStore();

        // Pre-seed an existing admin.
        store.Add(new AdminUser
        {
            Id = Guid.NewGuid(),
            Username = "existing",
            PasswordHash = "$2a$12$placeholder",
            Role = AdminRoles.Superadmin,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Single(store.Admins);   // still exactly 1 — the seeder did not add a second row
    }

    // ─── Happy path ──────────────────────────────────────────────────────────

    /// <summary>
    /// In Staging, with Enabled=true and a password set, on an empty store the seeder must insert
    /// exactly one admin with <c>Role=superadmin</c>, the configured username, and a verifiable
    /// BCrypt hash.
    /// </summary>
    [Fact]
    public async Task StartAsync_EmptyStore_Staging_SeedsSuperadmin()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Staging);

        const string Username = "root";
        const string Password = "platformer-demo-admin";

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "true"),
            ("Platformer:DemoAdmin:Username", Username),
            ("Platformer:DemoAdmin:Password", Password));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Single(store.Admins);

        var admin = store.Admins[0];
        Assert.Equal(Username, admin.Username);
        Assert.Equal(AdminRoles.Superadmin, admin.Role);
        Assert.NotEmpty(admin.PasswordHash);

        // Verify the hash round-trips correctly via BCrypt.
        var hasher = new BCryptPasswordHasher(new GameKitAuthOptions());
        Assert.True(hasher.Verify(Password, admin.PasswordHash),
            "Password hash stored by seeder must verify against the configured password.");
    }

    /// <summary>
    /// In Development (also non-Production) the seeder must seed when enabled + password set.
    /// </summary>
    [Fact]
    public async Task StartAsync_EmptyStore_Development_SeedsSuperadmin()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "true"),
            ("Platformer:DemoAdmin:Username", "dev-root"),
            ("Platformer:DemoAdmin:Password", "dev-password-123"));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Single(store.Admins);
        Assert.Equal(AdminRoles.Superadmin, store.Admins[0].Role);
    }

    /// <summary>
    /// When <c>Platformer:DemoAdmin:Username</c> is not configured the seeder uses "root" as default.
    /// </summary>
    [Fact]
    public async Task StartAsync_UsernameAbsent_UsesDefaultRoot()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Staging);

        var config = MakeConfig(
            ("Platformer:DemoAdmin:Enabled", "true"),
            ("Platformer:DemoAdmin:Password", "somepass123"));
        var store = new FakeAdminStore();

        await RunTestableSeederAsync(env.Object, config, store);

        Assert.Single(store.Admins);
        Assert.Equal("root", store.Admins[0].Username);
    }
}
