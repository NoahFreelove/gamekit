// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Entities;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Auth.Data.Configurations;

/// <summary>EF configuration for <see cref="PlayerIdentity"/> — maps to <c>gamekit.player_identities</c>.</summary>
internal sealed class PlayerIdentityConfiguration : IEntityTypeConfiguration<PlayerIdentity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerIdentity> b)
    {
        b.ToTable("player_identities");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.Provider).IsRequired().HasMaxLength(16);
        b.Property(p => p.ExternalId).IsRequired().HasMaxLength(64);
        b.Property(p => p.DisplayName).HasMaxLength(64);
        b.Property(p => p.AvatarUrl).HasMaxLength(512);
        b.Property(p => p.Metadata).HasColumnType("jsonb");
        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.UpdatedAt).IsRequired();

        // UNIQUE(provider, external_id) — the D-14 race anchor (AUTH-13 success criterion #4).
        b.HasIndex(p => new { p.Provider, p.ExternalId }).IsUnique();
        b.HasIndex(p => p.PlayerId);

        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
