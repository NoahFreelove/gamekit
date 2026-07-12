// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="LadderSeason"/> — maps to <c>gamekit.ladder_seasons</c>.</summary>
internal sealed class LadderSeasonConfiguration : IEntityTypeConfiguration<LadderSeason>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LadderSeason> b)
    {
        b.ToTable("ladder_seasons");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.SeasonNumber).IsRequired();
        b.Property(s => s.StartedAt).IsRequired();

        // Unique constraint: (ladder_id, season_number) — season numbers are per-ladder monotonic.
        b.HasIndex(s => new { s.LadderId, s.SeasonNumber }).IsUnique();

        // FK → ladders (ON DELETE CASCADE) — deleting a ladder removes its season history.
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(s => s.LadderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
