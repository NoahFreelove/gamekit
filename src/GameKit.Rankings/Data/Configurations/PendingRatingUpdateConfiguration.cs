// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="PendingRatingUpdate"/> — maps to <c>gamekit.pending_rating_updates</c>.</summary>
internal sealed class PendingRatingUpdateConfiguration : IEntityTypeConfiguration<PendingRatingUpdate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PendingRatingUpdate> b)
    {
        b.ToTable("pending_rating_updates");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Result).IsRequired().HasMaxLength(16);
        b.Property(p => p.EnqueuedAt).IsRequired();

        // Partial index — ticker drains only unapplied rows per ladder, ordered by enqueue time.
        // CREATE INDEX idx_pending_rating_updates_ladder_pending ON gamekit.pending_rating_updates
        //   (ladder_id, enqueued_at) WHERE applied_at IS NULL;
        b.HasIndex(p => new { p.LadderId, p.EnqueuedAt })
            .HasDatabaseName("idx_pending_rating_updates_ladder_pending")
            .HasFilter("applied_at IS NULL");

        // FK → game_sessions (ON DELETE CASCADE).
        b.HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK → players (ON DELETE SET NULL — Pitfall §12 GDPR safety).
        // PlayerId is NULLABLE; a NULL row is skipped by the ticker.
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK → ladders (ON DELETE RESTRICT).
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(p => p.LadderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
