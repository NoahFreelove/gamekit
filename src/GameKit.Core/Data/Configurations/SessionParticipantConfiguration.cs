// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Core.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="SessionParticipant"/>. Maps to <c>gamekit.session_participants</c>.
/// </summary>
/// <remarks>
/// The crucial configuration here is <c>OnDelete(DeleteBehavior.SetNull)</c> on the Player FK — this
/// is the GDPR fan-out rule per design decision D-10. When a player is hard-deleted, rows in this
/// table survive with <c>PlayerId = NULL</c>; opponent sessions remain intact.
/// </remarks>
internal sealed class SessionParticipantConfiguration : IEntityTypeConfiguration<SessionParticipant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SessionParticipant> b)
    {
        b.ToTable("session_participants");

        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.SessionId).IsRequired();
        b.Property(p => p.PlayerId).IsRequired(false); // NULLable — GDPR tombstone target

        b.Property(p => p.Team).IsRequired();
        b.Property(p => p.Result).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.Score);
        b.Property(p => p.RatingBefore);
        b.Property(p => p.RatingAfter);
        b.Property(p => p.RatingDelta);

        // Session relationship: cascade-delete participants when their session is deleted.
        b.HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Player relationship: SET NULL on hard-delete — the GDPR fan-out rule.
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes to support match-history lookups (Phase 3 admin UI) and per-player session enumeration.
        b.HasIndex(p => p.SessionId);
        b.HasIndex(p => p.PlayerId);
    }
}
