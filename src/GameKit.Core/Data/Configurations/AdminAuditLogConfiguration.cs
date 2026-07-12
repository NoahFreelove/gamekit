// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GameKit.Core.Entities;

namespace GameKit.Core.Data.Configurations;

/// <summary>EF Core fluent configuration for <see cref="AdminAuditLog"/>. Maps to <c>gamekit.admin_audit_log</c>.</summary>
internal sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AdminAuditLog> b)
    {
        b.ToTable("admin_audit_log");

        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();

        b.Property(a => a.ActorId);
        b.Property(a => a.Action).IsRequired().HasMaxLength(64);
        b.Property(a => a.TargetType).IsRequired().HasMaxLength(64);
        b.Property(a => a.TargetId);
        b.Property(a => a.Before).HasColumnType("jsonb");
        b.Property(a => a.After).HasColumnType("jsonb");
        b.Property(a => a.Reason).HasMaxLength(500);
        b.Property(a => a.CreatedAt).IsRequired();

        // Common admin-UI queries: "show me last 100 entries", "show entries targeting player X".
        b.HasIndex(a => a.CreatedAt);
        b.HasIndex(a => new { a.TargetType, a.TargetId });
        b.HasIndex(a => a.ActorId);

        // No FK on ActorId → players.Id.
        // actor_id stores BOTH player IDs (merge service) AND admin user IDs (admin login, ban,
        // GDPR export, etc.). Admin users are not in the players table, so a strict FK rejects
        // every admin-initiated audit entry with 23503. actor_id remains a bare nullable UUID —
        // see Core migration 20260606100000_AddAuditActorIdFk for the full rationale (Plan 10-04).
    }
}
