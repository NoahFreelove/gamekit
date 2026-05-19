// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Matchmaking.Data.Configurations;

/// <summary>EF configuration for <see cref="Party"/> — maps to <c>gamekit.parties</c>.</summary>
/// <remarks>
/// <para>
/// <see cref="Party.PartyCode"/> uses Postgres <c>citext</c> for case-insensitive uniqueness
/// (Pitfall §9 / CONTEXT.md D-02). The extension is already created by the Phase 2 Auth
/// migration; this configuration does NOT re-run <c>CREATE EXTENSION</c>.
/// </para>
/// <para>
/// <see cref="Party.State"/> uses integer enum storage (Phase 5 mandatory; CONTEXT.md
/// §Established Patterns) — NO <c>HasConversion&lt;string&gt;()</c>.
/// </para>
/// </remarks>
internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Party> b)
    {
        b.ToTable("parties");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        // Case-insensitive party code — citext at the SQL level (Pitfall §9).
        b.Property(p => p.PartyCode).IsRequired().HasColumnType("citext");

        // Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
        b.Property(p => p.State).IsRequired();

        b.Property(p => p.OwnerPlayerId).IsRequired();
        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.ExpiresAt);

        // Unique constraint on PartyCode — Postgres enforces case-insensitive uniqueness via citext.
        b.HasIndex(p => p.PartyCode).IsUnique();

        // FK → players (ON DELETE CASCADE — dropping the owner drops their owned parties).
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.OwnerPlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
