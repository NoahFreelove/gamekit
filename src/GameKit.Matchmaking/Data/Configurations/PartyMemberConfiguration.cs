// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Matchmaking.Data.Configurations;

/// <summary>EF configuration for <see cref="PartyMember"/> — maps to <c>gamekit.party_members</c>.</summary>
/// <remarks>
/// <para>
/// Cross-provider party membership is honored (CONTEXT.md D-05): <see cref="PartyMember.PlayerId"/>
/// FK targets the canonical <c>players.Id</c>, NOT <c>player_identities.Id</c> — Steam-linked
/// and Discord-linked identities share a single <c>Player</c> row from Phase 2's multi-identity
/// model.
/// </para>
/// <para>
/// Unique constraint on <c>(PartyId, PlayerId)</c> prevents duplicate-member rows for the same
/// party + player pair. Single-active-party-per-player is enforced in application code via a
/// SERIALIZABLE transaction (RESEARCH §Decision 12 + §OQ-2).
/// </para>
/// </remarks>
internal sealed class PartyMemberConfiguration : IEntityTypeConfiguration<PartyMember>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PartyMember> b)
    {
        b.ToTable("party_members");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.PartyId).IsRequired();
        b.Property(m => m.PlayerId).IsRequired();
        b.Property(m => m.JoinedAt).IsRequired();

        // Composite uniqueness on (PartyId, PlayerId) — prevents duplicate-member rows.
        b.HasIndex(m => new { m.PartyId, m.PlayerId }).IsUnique();

        // FK → parties (ON DELETE CASCADE — dropping a party drops its members).
        b.HasOne<Party>()
            .WithMany()
            .HasForeignKey(m => m.PartyId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK → players (ON DELETE RESTRICT — GDPR tombstoning is handled at the Player level).
        // Player deletes must dissolve parties first (handled by Plan 05-03 service code).
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
