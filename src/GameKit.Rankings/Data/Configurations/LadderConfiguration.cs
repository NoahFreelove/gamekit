// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="Ladder"/> — maps to <c>gamekit.ladders</c>.</summary>
internal sealed class LadderConfiguration : IEntityTypeConfiguration<Ladder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Ladder> b)
    {
        b.ToTable("ladders");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.Name).IsRequired().HasColumnType("citext");
        b.Property(l => l.Algorithm).IsRequired().HasMaxLength(64);
        b.Property(l => l.IsActive).IsRequired();
        b.Property(l => l.Config).HasColumnType("jsonb");
        b.Property(l => l.CreatedAt).IsRequired();

        b.HasIndex(l => l.Name).IsUnique();
    }
}
