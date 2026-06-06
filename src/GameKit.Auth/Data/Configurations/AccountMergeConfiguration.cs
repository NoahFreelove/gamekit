// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Entities;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Auth.Data.Configurations;

/// <summary>EF configuration for <see cref="AccountMerge"/> — maps to <c>gamekit.account_merges</c>.</summary>
internal sealed class AccountMergeConfiguration : IEntityTypeConfiguration<AccountMerge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AccountMerge> b)
    {
        b.ToTable("account_merges");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();

        // Status stored as integer — no HasConversion needed; EF Core maps CLR int-backed enums directly.
        // Project mandatory integer-enum convention (STATE.md Phase 5 / PITFALLS #13).
        b.Property(a => a.Status).IsRequired();
        b.Property(a => a.SourcePlayerId).IsRequired();
        b.Property(a => a.TargetPlayerId).IsRequired();
        b.Property(a => a.RequestedAt).IsRequired();
        b.Property(a => a.CommittedAt);
        b.Property(a => a.RedisCleanedAt);
        b.Property(a => a.ActorId);
        b.Property(a => a.Metadata).HasColumnType("jsonb");

        // UNIQUE(SourcePlayerId): prevents double-merge (SC#1, T-10-02-01).
        // A second concurrent insert with the same SourcePlayerId will receive Postgres 23505,
        // which the service layer maps to MergeResultKind.AlreadyMerged.
        b.HasIndex(a => a.SourcePlayerId).IsUnique();

        // Index on TargetPlayerId for lookups of all merges targeting a given player.
        b.HasIndex(a => a.TargetPlayerId);

        // FK on TargetPlayerId with ON DELETE RESTRICT: the surviving player cannot be GDPR-deleted
        // while a merge record points at it (T-10-02-02). SourcePlayerId is a bare UUID column —
        // no FK — because the source player is soft-deleted (not hard-deleted), but if GDPR erasure
        // later hard-deletes the source row we do not want a FK constraint to block it.
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(a => a.TargetPlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
