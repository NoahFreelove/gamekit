// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using GameKit.Rankings.Entities;
using LobbyEntity = GameKit.Lobby.Entities.Lobby;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Lobby.Data.Configurations;

/// <summary>EF configuration for <see cref="LobbyEntity"/> — maps to <c>gamekit.lobbies</c>.</summary>
/// <remarks>
/// <para>
/// <see cref="LobbyEntity.State"/> uses integer enum storage (Phase 5 mandatory; CLAUDE.md
/// §Constraints) — NO <c>HasConversion&lt;string&gt;()</c>.
/// </para>
/// <para>
/// <see cref="LobbyEntity.OwnerId"/> and <see cref="LobbyEntity.LadderId"/> both use <c>ON DELETE SET NULL</c>
/// so a lobby persists when the owner or ladder is removed.
/// </para>
/// </remarks>
internal sealed class LobbyConfiguration : IEntityTypeConfiguration<LobbyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LobbyEntity> b)
    {
        b.ToTable("lobbies");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();

        // Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
        b.Property(l => l.State).IsRequired();

        b.Property(l => l.MaxMembers).IsRequired();
        b.Property(l => l.RegionName);
        b.Property(l => l.CreatedAt).IsRequired();
        b.Property(l => l.UpdatedAt).IsRequired();

        // FK → players ON DELETE SET NULL (owner leaves; lobby persists)
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK → ladders ON DELETE SET NULL (ladder removed; lobby persists)
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(l => l.LadderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
