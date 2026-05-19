// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="SeasonRankArchive"/> — maps to <c>gamekit.season_rank_archive</c>.</summary>
internal sealed class SeasonRankArchiveConfiguration : IEntityTypeConfiguration<SeasonRankArchive>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeasonRankArchive> b)
    {
        b.ToTable("season_rank_archive");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();

        // All three rating columns are double precision (RANK-03 / SC#3).
        b.Property(a => a.Rating).IsRequired().HasColumnType("double precision");
        b.Property(a => a.RatingDeviation).IsRequired().HasColumnType("double precision");
        b.Property(a => a.Volatility).IsRequired().HasColumnType("double precision");
        b.Property(a => a.Wins).IsRequired();
        b.Property(a => a.Losses).IsRequired();
        b.Property(a => a.Draws).IsRequired();
        b.Property(a => a.ArchivedAt).IsRequired();

        // Composite index for archived-season leaderboard queries (D-13): (ladder_id, season_id, rating DESC).
        b.HasIndex(a => new { a.LadderId, a.SeasonId, a.Rating })
            .IsDescending(false, false, true);

        // FK → ladders (ON DELETE RESTRICT) — cannot delete a ladder with archived data.
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(a => a.LadderId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK → ladder_seasons (ON DELETE RESTRICT).
        b.HasOne<LadderSeason>()
            .WithMany()
            .HasForeignKey(a => a.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK → players (ON DELETE SET NULL per GDPR cascade, D-13).
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(a => a.PlayerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
