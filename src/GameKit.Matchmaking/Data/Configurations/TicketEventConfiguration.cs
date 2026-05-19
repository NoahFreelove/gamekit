// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Matchmaking.Data.Configurations;

/// <summary>EF configuration for <see cref="TicketEvent"/> — maps to <c>gamekit.ticket_events</c>.</summary>
/// <remarks>
/// <para>
/// <see cref="TicketEvent.EventType"/> uses integer enum storage (Phase 5 mandatory) — NO
/// <c>HasConversion&lt;string&gt;()</c>. Numeric values mirror <see cref="TicketStatus"/>
/// per CONTEXT.md D-18.
/// </para>
/// <para>
/// <see cref="TicketEvent.Payload"/> is C# <c>string?</c> mapped to Postgres <c>jsonb</c> —
/// the application emits already-serialized JSON. The column is sparse, append-only, and
/// not queried (matches the CLAUDE.md "metadata JSONB columns" constraint).
/// </para>
/// </remarks>
internal sealed class TicketEventConfiguration : IEntityTypeConfiguration<TicketEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TicketEvent> b)
    {
        b.ToTable("ticket_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.TicketId).IsRequired();

        // Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
        b.Property(e => e.EventType).IsRequired();

        b.Property(e => e.OccurredAt).IsRequired();
        b.Property(e => e.Payload).HasColumnType("jsonb");

        // Index for per-ticket event-stream queries (e.g. timeline reconstruction).
        b.HasIndex(e => e.TicketId);

        // FK → matchmaking_tickets (ON DELETE CASCADE — events live and die with the ticket).
        b.HasOne<MatchmakingTicket>()
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
