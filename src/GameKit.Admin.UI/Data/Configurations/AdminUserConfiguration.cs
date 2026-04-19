// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Admin.UI.Data.Configurations;

/// <summary>EF configuration for <see cref="AdminUser"/> — maps to <c>gamekit.admin_users</c>.</summary>
internal sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AdminUser> b)
    {
        b.ToTable("admin_users");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();

        // Case-insensitive uniqueness achieved via citext column type; the citext extension is
        // already installed by the AuthInitial migration prologue — Admin migration MUST NOT
        // recreate it (AdminInitial runs after AuthInitial, guaranteed by the hosted-service
        // registration order).
        b.Property(a => a.Username).IsRequired().HasColumnType("citext").HasMaxLength(32);
        b.Property(a => a.PasswordHash).IsRequired().HasMaxLength(72);   // BCrypt hashes are <=72 chars
        b.Property(a => a.Role).IsRequired().HasMaxLength(16);
        b.Property(a => a.CreatedAt).IsRequired();
        b.Property(a => a.LastLoginAt);
        b.Property(a => a.FailedLoginCount).HasDefaultValue(0);
        b.Property(a => a.LockedUntil);

        // D-06 CHECK constraint: role ∈ {admin, superadmin} — enforced at DB level as defense-in-depth.
        // Column identifier MUST be quoted ("Role") because EF emits PascalCase column names
        // unmodified into Postgres, and Postgres folds unquoted identifiers to lowercase, which
        // would fail with 42703 ("column 'role' does not exist") when the constraint runs.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_admin_users_role",
            "\"Role\" IN ('admin','superadmin')"));

        b.HasIndex(a => a.Username).IsUnique().HasDatabaseName("ix_admin_users_username");

        // NO FK to players — admin_users is a separate identity store (D-06).
    }
}
