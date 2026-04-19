// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Data.Configurations;
using GameKit.Auth.Entities;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Admin.UI.Data;

/// <summary>
/// <see cref="IModelCustomizer"/> used whenever a <c>GameKitDbContext</c> is built for
/// <b>Admin-migration purposes</b> — both at design time by <see cref="AdminDesignTimeDbContextFactory"/>
/// and at runtime/test time when applying the Admin migration against an existing Core+Auth-initialized
/// database. It (1) runs the base relational pipeline + Core's <c>OnModelCreating</c>, (2) applies
/// the Admin entity configuration directly (no DI required — avoids the
/// <c>GameKitModelCustomizer</c> DI-resolution gap that EF's internal service provider does not
/// bridge when contexts are built ad-hoc), and (3) excludes every Core and Auth entity from
/// migrations so the Admin migration diff emits only the <c>admin_users</c> table.
/// </summary>
/// <remarks>
/// Pattern follows <see cref="GameKit.Auth.Data.AuthMigrationModelCustomizer"/>. The exclusion list
/// is one entry longer than Auth's customizer because Admin must exclude both Core (4 entities)
/// AND Auth (3 entities) — Auth's customizer only excludes Core.
/// </remarks>
public sealed class AdminMigrationModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer with the EF-internal dependencies tuple.</summary>
    public AdminMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Admin entity configurations directly — bypass DI because the migration-context
        // factory path does not wire app services into customizer constructor injection.
        modelBuilder.ApplyConfiguration(new AdminUserConfiguration());

        // Exclude every Core entity from the Admin migration — Core's migration already owns those
        // tables. Mirrors AuthMigrationModelCustomizer's Core-exclusion list (PITFALLS #3).
        var coreEntityTypes = new[]
        {
            typeof(Player),
            typeof(GameSession),
            typeof(SessionParticipant),
            typeof(AdminAuditLog),
        };

        // Exclude every Auth entity from the Admin migration — Auth's migration owns these.
        // This list is unique to the Admin customizer (Auth's customizer only excludes Core).
        var authEntityTypes = new[]
        {
            typeof(PlayerIdentity),
            typeof(PlayerCredential),
            typeof(RefreshToken),
        };

        foreach (var type in coreEntityTypes)
            ExcludeEntity(modelBuilder, type);
        foreach (var type in authEntityTypes)
            ExcludeEntity(modelBuilder, type);
    }

    private static void ExcludeEntity(ModelBuilder modelBuilder, System.Type type)
    {
        var entity = modelBuilder.Model.FindEntityType(type);
        if (entity is null) return;
        var tableName = entity.GetTableName()!;
        var schema = entity.GetSchema();
        modelBuilder.Entity(type).ToTable(tableName, schema, t => t.ExcludeFromMigrations());
    }
}
