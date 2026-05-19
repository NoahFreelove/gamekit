// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Matchmaking.Data.Configurations;

/// <summary>EF configuration for <see cref="DeclineHistory"/> — maps to <c>gamekit.decline_history</c>.</summary>
/// <remarks>
/// <para>
/// Per-player cooldown-tracking row (CONTEXT.md D-08, 3→15→30 min ladder). The index on
/// <c>(PlayerId, DeclinedAt DESC)</c> supports rolling-window queries — the lockout check at
/// the <c>POST /api/mm/queue</c> endpoint scans recent rows by player.
/// </para>
/// <para>
/// <see cref="DeclineHistory.ProposalId"/> is <see cref="System.Guid"/> at the C# level but
/// stored as <c>text</c> at the SQL level (RESEARCH §Decision 8) — proposals live in Redis
/// with a TTL and have no Postgres row to FK against. Durable storage of the id lets analytics
/// correlate declines back to a specific proposal after the Redis hash expires.
/// </para>
/// </remarks>
internal sealed class DeclineHistoryConfiguration : IEntityTypeConfiguration<DeclineHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DeclineHistory> b)
    {
        b.ToTable("decline_history");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.PlayerId).IsRequired();
        b.Property(d => d.DeclinedAt).IsRequired();

        // ProposalId stored as text (RESEARCH §Decision 8 — no FK to ephemeral Redis proposal).
        b.Property(d => d.ProposalId).IsRequired().HasColumnType("text");

        // Cooldown rolling-window index: scan recent rows per player, newest first.
        b.HasIndex(d => new { d.PlayerId, d.DeclinedAt })
            .HasDatabaseName("idx_decline_history_player_declined_at")
            .IsDescending(false, true);

        // FK → players (ON DELETE CASCADE — GDPR-erased players' cooldown rows are removed).
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(d => d.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
