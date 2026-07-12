// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Core.Data.Configurations;

/// <summary>EF Core fluent configuration for <see cref="Player"/>. Maps to the <c>gamekit.players</c> table.</summary>
internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Player> b)
    {
        b.ToTable("players");

        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever(); // UUIDv7 assigned by IIdGenerator at service layer, not by DB

        b.Property(p => p.DisplayName).IsRequired().HasMaxLength(64);
        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.LastSeenAt);
        b.Property(p => p.IsBanned).IsRequired().HasDefaultValue(false);
        b.Property(p => p.BannedAt);
        b.Property(p => p.BanReason).HasMaxLength(500);
        b.Property(p => p.Metadata).HasColumnType("jsonb");

        b.Property(p => p.MergedIntoPlayerId);
        b.Property(p => p.DeletedAt);

        // Self-referential FK: merged_into_player_id → players.id ON DELETE SET NULL.
        // If the target player is later GDPR-deleted, the tombstone reference becomes NULL
        // rather than blocking the hard-delete with a constraint violation.
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.MergedIntoPlayerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Note: deliberately NO index on DisplayName (non-unique, mutable, volume-heavy).
        // Phase 3 admin search will add a functional/trigram index if needed.
    }
}
