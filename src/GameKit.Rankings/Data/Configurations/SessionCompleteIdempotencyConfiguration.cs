// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Rankings.Data.Configurations;

/// <summary>EF configuration for <see cref="SessionCompleteIdempotency"/> — maps to <c>gamekit.session_complete_idempotency</c>.</summary>
internal sealed class SessionCompleteIdempotencyConfiguration : IEntityTypeConfiguration<SessionCompleteIdempotency>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SessionCompleteIdempotency> b)
    {
        b.ToTable("session_complete_idempotency");

        // Composite PK: (session_id, idempotency_key) — uniqueness per session + key pair.
        b.HasKey(i => new { i.SessionId, i.IdempotencyKey });

        // RequestBodyHash: SHA-256 hex, 64 chars.
        b.Property(i => i.RequestBodyHash).IsRequired().HasMaxLength(64);

        // CachedResponse: full serialized response body (bytea).
        b.Property(i => i.CachedResponse).IsRequired();

        b.Property(i => i.CreatedAt).IsRequired();

        // FK → game_sessions (ON DELETE CASCADE) — deleting a session removes idempotency rows.
        b.HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(i => i.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
