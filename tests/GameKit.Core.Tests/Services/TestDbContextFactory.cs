// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Text.Json;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GameKit.Core.Tests.Services;

/// <summary>
/// Creates <see cref="GameKitDbContext"/> instances configured for the InMemory provider with
/// value converters for <see cref="JsonDocument"/> properties (unsupported natively by InMemory).
/// </summary>
internal static class TestDbContextFactory
{
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
    /// Model customizer for InMemory tests that adds value converters for all JsonDocument properties.
    /// The InMemory provider does not support jsonb or JsonDocument natively — these converters
    /// serialize to/from string so data operations work in unit tests.
    /// </summary>
    private sealed class InMemoryTestModelCustomizer : ModelCustomizer
    {
        public InMemoryTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

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
