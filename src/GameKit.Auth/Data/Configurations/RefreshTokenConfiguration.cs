// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Entities;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Auth.Data.Configurations;

/// <summary>EF configuration for <see cref="RefreshToken"/> — maps to <c>gamekit.refresh_tokens</c>.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(r => r.ReplacedByTokenHash).HasMaxLength(64);
        b.Property(r => r.DeviceFingerprint).HasMaxLength(64);
        b.Property(r => r.Provider).IsRequired().HasMaxLength(16);
        b.Property(r => r.IssuedAt).IsRequired();
        b.Property(r => r.ExpiresAt).IsRequired();

        b.HasIndex(r => r.TokenHash).IsUnique();                    // primary lookup
        b.HasIndex(r => new { r.PlayerId, r.RevokedAt });           // "my live refreshes"
        b.HasIndex(r => r.FamilyId);                                // family revoke UPDATE

        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(r => r.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
