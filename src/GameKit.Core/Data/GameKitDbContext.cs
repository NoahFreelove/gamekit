// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Data;

/// <summary>
/// The single, fully-owned GameKit <see cref="DbContext"/>. Registered in DI via
/// <c>AddDbContext&lt;GameKitDbContext&gt;</c>. Sibling GameKit packages contribute entities
/// via <see cref="IModelBuilderExtension"/> rather than subclassing this context.
/// </summary>
/// <remarks>
/// Per CORE-02, this is not a base class — it is the one context every sibling package shares.
/// Design-time migration generation uses <c>CoreDesignTimeFactory</c>; runtime
/// registration is handled by <c>AddGameKit(...)</c> (Plan 05). Per-package migrations + their
/// history tables are disambiguated via <c>MigrationsAssembly</c> + <c>MigrationsHistoryTable</c>
/// options on the Npgsql configuration.
/// </remarks>
public sealed class GameKitDbContext : DbContext
{
    /// <summary>Constructs the context with the supplied options. Standard EF pooling-friendly signature.</summary>
    /// <remarks>
    /// When the context is built via <c>AddDbContext&lt;GameKitDbContext&gt;((sp, opts) =&gt; opts.
    /// UseApplicationServiceProvider(sp))</c> (the DI runtime path), <see cref="OnModelCreating"/>
    /// resolves the registered <see cref="IModelBuilderExtension"/> collection from the app service
    /// provider and applies sibling-package entity configurations. When the context is constructed
    /// directly (design-time factories, ad-hoc migration contexts), no application service provider
    /// is attached, the extension lookup returns null, and the model stays Core-only — matching
    /// the per-package migration boundary.
    /// </remarks>
    public GameKitDbContext(DbContextOptions<GameKitDbContext> options) : base(options)
    {
    }

    /// <summary>Players owned by GameKit (CORE-06 REVISED per D-13).</summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>Game sessions tracked by GameKit (CORE-07).</summary>
    public DbSet<GameSession> GameSessions => Set<GameSession>();

    /// <summary>Per-session participation records (CORE-08).</summary>
    public DbSet<SessionParticipant> SessionParticipants => Set<SessionParticipant>();

    /// <summary>Admin audit log entries (CORE-09).</summary>
    public DbSet<AdminAuditLog> AdminAuditLog => Set<AdminAuditLog>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(GameKitMigrationConstants.SchemaName);

        // Picks up every IEntityTypeConfiguration<T> in this assembly
        // (PlayerConfiguration, GameSessionConfiguration, SessionParticipantConfiguration, AdminAuditLogConfiguration).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GameKitDbContext).Assembly);

        // Resolve sibling-package model-builder extensions from the application service provider
        // when one is attached (runtime DI path uses UseApplicationServiceProvider(sp) —
        // wired by AddGameKit). Migration and design-time paths construct the context directly
        // without an app provider, so this lookup returns null and the model stays Core-only —
        // preserving the per-package migration boundary (PITFALLS #3).
        var appProvider = this.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider;
        if (appProvider is not null)
        {
            foreach (var extension in appProvider.GetServices<IModelBuilderExtension>())
                extension.ApplyTo(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }
}
