// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

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

        // Sibling-package IModelBuilderExtensions are applied by GameKitModelCustomizer AFTER this method,
        // via ReplaceService<IModelCustomizer, GameKitModelCustomizer>().
        base.OnModelCreating(modelBuilder);
    }
}
