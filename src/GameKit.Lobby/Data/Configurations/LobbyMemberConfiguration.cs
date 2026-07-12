// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using LobbyEntity = GameKit.Lobby.Entities.Lobby;
using LobbyMemberEntity = GameKit.Lobby.Entities.LobbyMember;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Lobby.Data.Configurations;

/// <summary>EF configuration for <see cref="LobbyMemberEntity"/> — maps to <c>gamekit.lobby_members</c>.</summary>
/// <remarks>
/// <para>
/// The composite unique constraint on <c>(LobbyId, PlayerId)</c> prevents duplicate-member rows
/// for the same lobby + player pair (LOBBY-02; T-11-02-03).
/// </para>
/// <para>
/// Both FKs use <c>ON DELETE CASCADE</c>: lobby deletion cascades to membership (structural
/// cleanup), and player hard-deletion cascades to lobby membership (GDPR tombstoning).
/// This deviates intentionally from <c>PartyMemberConfiguration</c> which uses
/// <c>Restrict</c> on the player FK — lobby membership has no audit purpose.
/// </para>
/// </remarks>
internal sealed class LobbyMemberConfiguration : IEntityTypeConfiguration<LobbyMemberEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LobbyMemberEntity> b)
    {
        b.ToTable("lobby_members");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.LobbyId).IsRequired();
        b.Property(m => m.PlayerId).IsRequired();
        b.Property(m => m.Ready).IsRequired();
        b.Property(m => m.JoinedAt).IsRequired();

        // Composite unique constraint (LobbyId, PlayerId) — prevents duplicate-member rows.
        b.HasIndex(m => new { m.LobbyId, m.PlayerId }).IsUnique();

        // FK → lobbies ON DELETE CASCADE
        b.HasOne<LobbyEntity>()
            .WithMany(l => l.Members)
            .HasForeignKey(m => m.LobbyId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK → players ON DELETE CASCADE (GDPR: player deletion cascades to membership)
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
