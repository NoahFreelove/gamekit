// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="ServiceToken"/> — maps to <c>gamekit.service_tokens</c>.</summary>
internal sealed class ServiceTokenConfiguration : IEntityTypeConfiguration<ServiceToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceToken> b)
    {
        b.ToTable("service_tokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();

        // citext: case-insensitive uniqueness (mirrors PlayerCredential.Username pattern).
        b.Property(t => t.Name).IsRequired().HasColumnType("citext");

        // SHA-256 hex digest (64 lower-case chars) — mirrors refresh_tokens.token_hash.
        b.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(t => t.CreatedAt).IsRequired();

        b.HasIndex(t => t.Name).IsUnique();
        b.HasIndex(t => t.TokenHash).IsUnique(); // primary lookup path for auth handler
    }
}
