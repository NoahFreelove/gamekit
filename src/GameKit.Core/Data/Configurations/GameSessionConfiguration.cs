// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Core.Data.Configurations;

/// <summary>EF Core fluent configuration for <see cref="GameSession"/>. Maps to <c>gamekit.game_sessions</c>.</summary>
internal sealed class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GameSession> b)
    {
        b.ToTable("game_sessions");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();

        // Store the enum as its textual name in Postgres — stable across enum-integer reorderings.
        b.Property(s => s.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        b.Property(s => s.LadderId);
        // LadderId FK lands in Phase 4 when the ladders table arrives. Phase 1 column is a bare nullable Guid.

        b.Property(s => s.CreatedAt).IsRequired();
        b.Property(s => s.StartedAt);
        b.Property(s => s.CompletedAt);
        b.Property(s => s.Metadata).HasColumnType("jsonb");

        // Useful for admin match history + leaderboard recency queries.
        b.HasIndex(s => s.CreatedAt);
        b.HasIndex(s => new { s.LadderId, s.CreatedAt });
    }
}
