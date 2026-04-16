// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
    }
}
