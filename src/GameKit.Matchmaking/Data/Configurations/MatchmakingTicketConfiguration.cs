// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Matchmaking.Entities;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Matchmaking.Data.Configurations;

/// <summary>EF configuration for <see cref="MatchmakingTicket"/> — maps to <c>gamekit.matchmaking_tickets</c>.</summary>
/// <remarks>
/// <para>
/// Analytics-only async-write table (MATCH-02). Redis remains source of truth for the live
/// queue (MATCH-04); this row is drained from an in-memory <c>Channel&lt;TicketEvent&gt;</c>
/// by the analytics drain service (CONTEXT.md D-15).
/// </para>
/// <para>
/// <see cref="MatchmakingTicket.Status"/> uses integer enum storage (Phase 5 mandatory) — NO
/// <c>HasConversion&lt;string&gt;()</c>.
/// </para>
/// <para>
/// Index on <c>(LadderId, PoolName, Status)</c> supports the reconciler sweep that detects
/// abandoned non-terminal tickets (MATCH-06; Plan 05-06).
/// </para>
/// <para>
/// The <see cref="MatchmakingTicket.PartyId"/> FK is restrict-delete (NOT cascade) — CONTEXT.md
/// D-04: cancelling a ticket leaves the party row intact. <see cref="MatchmakingTicket.SessionId"/>
/// FK is set-null so a deleted session does not orphan the ticket.
/// </para>
/// </remarks>
internal sealed class MatchmakingTicketConfiguration : IEntityTypeConfiguration<MatchmakingTicket>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchmakingTicket> b)
    {
        b.ToTable("matchmaking_tickets");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.PartyId);                              // Guid? — solo enqueue has no party
        b.Property(t => t.LadderId).IsRequired();
        b.Property(t => t.PoolName).IsRequired().HasMaxLength(64);

        // Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
        b.Property(t => t.Status).IsRequired();

        // Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
        // DEFAULT 0 (Normal) — existing tickets receive TicketType = 0 via migration DEFAULT clause.
        b.Property(t => t.TicketType).IsRequired();

        b.Property(t => t.QueuedAt).IsRequired();
        b.Property(t => t.TerminalAt);
        b.Property(t => t.SessionId);

        // Reconciler sweep index — finds non-terminal tickets per (ladder, pool, status).
        b.HasIndex(t => new { t.LadderId, t.PoolName, t.Status })
            .HasDatabaseName("idx_matchmaking_tickets_ladder_pool_status");

        // FK → parties (ON DELETE SET NULL — party survival is independent of ticket lifecycle, D-04).
        // PartyId is NULLABLE; cancelling a party should not cascade-delete analytics rows.
        b.HasOne<Party>()
            .WithMany()
            .HasForeignKey(t => t.PartyId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK → ladders (ON DELETE RESTRICT — ladders cannot be deleted while tickets reference them).
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(t => t.LadderId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK → game_sessions (ON DELETE SET NULL — deleting a session leaves the ticket analytics row).
        b.HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
