// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Auth.Migrations
{
    /// <summary>
    /// DR-05/DR-07 ordering anchor: zero-DDL migration that advances the Auth package's
    /// latest migration timestamp to <c>20260623000000</c>, ensuring the canonical
    /// application order Core(20260622) &lt; Auth(20260623) holds lexicographically.
    /// No schema changes — empty Up(), DR-04-compliant Down().
    /// </summary>
    [Migration("20260623000000_DrOrderingMarker")]
    public partial class DrOrderingMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Zero-DDL ordering anchor for DR-05 — no EnsureSchema, no Sql, no CreateTable.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DR-04: Destructive rollback is not supported. Restore from backup — see docs/runbooks/postgres-backup-restore.md.
            throw new NotSupportedException(
                "Migration rollback via Down() is disabled in GameKit. Restore from a Postgres backup instead. " +
                "See docs/runbooks/postgres-backup-restore.md.");
        }
    }
}
