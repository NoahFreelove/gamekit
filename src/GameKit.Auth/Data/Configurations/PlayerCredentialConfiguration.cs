// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Entities;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Auth.Data.Configurations;

/// <summary>EF configuration for <see cref="PlayerCredential"/> — maps to <c>gamekit.player_credentials</c>.</summary>
internal sealed class PlayerCredentialConfiguration : IEntityTypeConfiguration<PlayerCredential>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerCredential> b)
    {
        b.ToTable("player_credentials");
        b.HasKey(c => c.PlayerId);
        b.Property(c => c.PlayerId).ValueGeneratedNever();

        // Case-insensitive uniqueness achieved via citext column type; the citext extension is
        // installed by the AuthInitial migration prologue so self-hosting operators don't have to.
        b.Property(c => c.Username).IsRequired().HasMaxLength(32).HasColumnType("citext");
        // AUTH-18: column extended from 72 to 512 to accommodate Argon2id encoded strings.
        // BCrypt hashes are 60 chars; Argon2id encoded strings are ~80–120 chars depending on
        // m/t/p/hashLength params. 512 provides headroom for future hashers (BLAKE3, Bcrypt v3, etc).
        b.Property(c => c.PasswordHash).IsRequired().HasMaxLength(512);
        b.Property(c => c.UpdatedAt).IsRequired();

        b.HasIndex(c => c.Username).IsUnique();   // relies on citext for case-insensitivity

        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(c => c.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
