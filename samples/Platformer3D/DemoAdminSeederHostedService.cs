// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platformer3D;

/// <summary>
/// Startup seeder that creates a demo superadmin when <c>admin_users</c> is empty and
/// <c>Platformer:DemoAdmin:Enabled</c> is <c>true</c>.
/// <para>
/// SECURITY: runs ONLY in non-Production environments. In Production this service is a
/// deliberate no-op — operators use <c>dotnet gamekit admin create</c> to bootstrap admins.
/// Seeding in Production would silently create default credentials, violating the project's
/// no-default-creds posture.
/// </para>
/// <para>
/// Ordering: registered AFTER <c>AdminMigrationHostedService</c> (which creates the
/// <c>admin_users</c> table) so <c>admin_users</c> exists when the seeder runs. Hosted
/// services fire in registration order — do not change the order in <c>Program.cs</c>.
/// </para>
/// </summary>
public class DemoAdminSeederHostedService : IHostedService
{
    private readonly IHostEnvironment _env;
    private readonly IServiceProvider _sp;
    private readonly ILogger<DemoAdminSeederHostedService> _logger;

    /// <summary>Constructs the seeder.</summary>
    /// <param name="env">Host environment — Production short-circuits.</param>
    /// <param name="sp">Root service provider — scoped DbContext created per seed attempt.</param>
    /// <param name="logger">Logger for the DEMO-ONLY warning.</param>
    public DemoAdminSeederHostedService(
        IHostEnvironment env,
        IServiceProvider sp,
        ILogger<DemoAdminSeederHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(logger);
        _env = env;
        _sp = sp;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // SECURITY GUARD: never seed in Production. Production operators use the CLI.
        if (_env.IsProduction())
        {
            _logger.LogDebug(
                "DemoAdminSeeder: skipped (Production — use 'dotnet gamekit admin create').");
            return;
        }

        using var scope = _sp.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Config-gate: seeding only runs when Platformer:DemoAdmin:Enabled = true.
        var enabled = config.GetValue<bool>("Platformer:DemoAdmin:Enabled");
        if (!enabled)
        {
            _logger.LogDebug(
                "DemoAdminSeeder: skipped (Platformer:DemoAdmin:Enabled is false or absent).");
            return;
        }

        var username = config["Platformer:DemoAdmin:Username"] ?? "root";
        var password = config["Platformer:DemoAdmin:Password"];

        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "DemoAdminSeeder: Platformer:DemoAdmin:Enabled=true but Platformer:DemoAdmin:Password " +
                "is not set — skipping seed.");
            return;
        }

        // Idempotency: only seed when admin_users is completely empty.
        var anyAdmin = await AnyAdminExistsAsync(scope.ServiceProvider, cancellationToken)
            .ConfigureAwait(false);

        if (anyAdmin)
        {
            _logger.LogDebug(
                "DemoAdminSeeder: admin_users already has at least one row — skipping seed.");
            return;
        }

        // Hash using the same BCryptPasswordHasher the CLI uses (Auth package default hasher).
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var admin = new AdminUser
        {
            Id = ids.NewId(),
            Username = username,
            // Auto-promote first admin to superadmin (mirrors CLI AdminCreateCommand behavior).
            Role = AdminRoles.Superadmin,
            PasswordHash = hasher.Hash(password),
            CreatedAt = clock.UtcNow,
            LastLoginAt = null,
            FailedLoginCount = 0,
            LockedUntil = null,
        };

        await PersistAdminAsync(scope.ServiceProvider, admin, cancellationToken)
            .ConfigureAwait(false);

        // DEMO-ONLY warning — prominent log so it is impossible to miss in production-like setups.
        _logger.LogWarning(
            "DemoAdminSeeder: seeded DEMO admin '{Username}' (role=superadmin) — " +
            "DEMO ONLY, do NOT use in production. " +
            "Disable via Platformer:DemoAdmin:Enabled=false or switch to " +
            "ASPNETCORE_ENVIRONMENT=Production.",
            username);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ─── Protected seams (overridable for unit testing) ───────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when at least one <see cref="AdminUser"/> row exists.
    /// Virtual so unit tests can inject a fake store without requiring a real
    /// <see cref="GameKitDbContext"/> (which has JSON columns incompatible with InMemory EF).
    /// Resolves the <see cref="GameKitDbContext"/> from <paramref name="scopedSp"/> internally
    /// so overrides can short-circuit before the context is resolved.
    /// </summary>
    protected internal virtual Task<bool> AnyAdminExistsAsync(
        IServiceProvider scopedSp,
        CancellationToken ct)
    {
        var ctx = scopedSp.GetRequiredService<GameKitDbContext>();
        return ctx.Set<AdminUser>().AsNoTracking().AnyAsync(ct);
    }

    /// <summary>
    /// Persists the seeded <see cref="AdminUser"/> to the database.
    /// Virtual so unit tests can capture the write without requiring a real database.
    /// Resolves the <see cref="GameKitDbContext"/> from <paramref name="scopedSp"/> internally
    /// so overrides can short-circuit before the context is resolved.
    /// </summary>
    protected internal virtual async Task PersistAdminAsync(
        IServiceProvider scopedSp,
        AdminUser admin,
        CancellationToken ct)
    {
        var ctx = scopedSp.GetRequiredService<GameKitDbContext>();
        ctx.Set<AdminUser>().Add(admin);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
