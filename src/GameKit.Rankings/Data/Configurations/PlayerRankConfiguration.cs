// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="PlayerRank"/> — maps to <c>gamekit.player_ranks</c>.</summary>
internal sealed class PlayerRankConfiguration : IEntityTypeConfiguration<PlayerRank>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerRank> b)
    {
        b.ToTable("player_ranks");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();

        // EF Core 10 + Npgsql maps `double` CLR → `double precision` natively.
        // The explicit .HasColumnType("double precision") documents intent and is asserted by
        // the schema-introspection test (SchemaTypeAssertions / SC#3).
        b.Property(r => r.Rating).IsRequired().HasColumnType("double precision");
        b.Property(r => r.RatingDeviation).IsRequired().HasColumnType("double precision");
        b.Property(r => r.Volatility).IsRequired().HasColumnType("double precision");
        b.Property(r => r.Wins).IsRequired();
        b.Property(r => r.Losses).IsRequired();
        b.Property(r => r.Draws).IsRequired();

        // Unique constraint: one live rank per player per ladder.
        b.HasIndex(r => new { r.PlayerId, r.LadderId }).IsUnique();

        // Leaderboard hot-path index (RANK-08 / D-23): (ladder_id ASC, rating DESC).
        b.HasIndex(r => new { r.LadderId, r.Rating })
            .HasDatabaseName("idx_player_ranks_ladder_rating")
            .IsDescending(false, true);

        // FK → players (ON DELETE CASCADE) — deleting a player removes all their ranks.
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(r => r.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // New decay + placement columns (RANK-15 / RANK-16 / SC#5 schema freeze).
        b.Property(r => r.LastDecayAt).IsRequired(false);
        b.Property(r => r.PlacementMatchesRemaining).IsRequired().HasDefaultValue(10);
        b.Property(r => r.IsInPlacement).IsRequired().HasDefaultValue(true);

        // FK → ladders (ON DELETE RESTRICT) — cannot delete a ladder with live ranks.
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(r => r.LadderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
