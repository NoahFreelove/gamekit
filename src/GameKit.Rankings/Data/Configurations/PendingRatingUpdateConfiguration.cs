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
        // CR-04: HasFilter must use the quoted PascalCase column name to match what the
        // migration actually creates (WHERE "AppliedAt" IS NULL). The earlier snake_case
        // form ("applied_at IS NULL") drifted from the on-disk index definition and any
        // subsequent `dotnet ef migrations add` would emit a drop+recreate migration.
        b.HasIndex(p => new { p.LadderId, p.EnqueuedAt })
            .HasDatabaseName("idx_pending_rating_updates_ladder_pending")
            .HasFilter("\"AppliedAt\" IS NULL");

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
