// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Text.Json;
using GameKit.Admin.UI.Entities;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GameKit.Admin.Tests;

/// <summary>
/// Creates <see cref="GameKitDbContext"/> instances configured for the EF Core InMemory provider
/// with value converters for <see cref="JsonDocument"/> properties (the InMemory provider does not
/// support <c>jsonb</c> / <c>JsonDocument</c> natively). Also registers the Admin-side
/// <see cref="AdminUser"/> entity on the model so tests targeting AdminUserService /
/// AdminAuthService can query it. Mirrors the Phase-1 GameKit.Core.Tests factory.
/// </summary>
internal static class TestDbContextFactory
{
    /// <summary>Builds a fresh in-memory DbContext backed by <paramref name="dbName"/>.</summary>
    public static GameKitDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .ReplaceService<IModelCustomizer, InMemoryTestModelCustomizer>()
            .Options;

        return new GameKitDbContext(options);
    }

    /// <summary>
    /// Model customizer for InMemory tests that (a) registers <see cref="AdminUser"/> so
    /// AdminUserService / AdminAuthService can query it, and (b) adds value converters for
    /// all JsonDocument properties on Core entities.
    /// </summary>
    private sealed class InMemoryTestModelCustomizer : ModelCustomizer
    {
        public InMemoryTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            // Apply the Admin entity so AdminAuthService / AdminUserService queries resolve.
            modelBuilder.ApplyConfiguration(new GameKit.Admin.UI.Data.Configurations.AdminUserConfiguration());

            var jsonConverter = new ValueConverter<JsonDocument?, string?>(
                v => v == null ? null : v.RootElement.GetRawText(),
                v => v == null ? null : JsonDocument.Parse(v, default));

            var jsonComparer = new ValueComparer<JsonDocument?>(
                (a, b) => JsonDocumentEquals(a, b),
                v => v == null ? 0 : v.RootElement.GetRawText().GetHashCode(),
                v => v == null ? null : JsonDocument.Parse(v.RootElement.GetRawText(), default));

            modelBuilder.Entity<AdminAuditLog>(b =>
            {
                b.Property(a => a.Before).HasConversion(jsonConverter).Metadata.SetValueComparer(jsonComparer);
                b.Property(a => a.After).HasConversion(jsonConverter).Metadata.SetValueComparer(jsonComparer);
            });

            modelBuilder.Entity<Player>(b =>
            {
                b.Property(p => p.Metadata).HasConversion(jsonConverter).Metadata.SetValueComparer(jsonComparer);
            });

            modelBuilder.Entity<GameSession>(b =>
            {
                b.Property(s => s.Metadata).HasConversion(jsonConverter).Metadata.SetValueComparer(jsonComparer);
            });
        }

        private static bool JsonDocumentEquals(JsonDocument? a, JsonDocument? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            return a.RootElement.GetRawText() == b.RootElement.GetRawText();
        }
    }
}
