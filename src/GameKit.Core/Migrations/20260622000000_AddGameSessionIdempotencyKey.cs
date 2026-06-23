// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <summary>
    /// Adds the <c>IdempotencyKey</c> column to <c>gamekit.game_sessions</c> with a partial
    /// unique index (SCALE-03). Core is the sole owner of Core-table schema per CLAUDE.md
    /// per-package boundary rule (packages never modify Core tables in their migrations).
    /// Nullable with partial index allows non-destructive addition to existing rows.
    /// The unique constraint is the Postgres-level secondary guard against split-brain
    /// double-write (RESEARCH §Idempotency Design).
    /// </summary>
    public partial class AddGameSessionIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "gamekit",
                table: "game_sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "uq_game_sessions_idempotency_key"
                    ON gamekit.game_sessions ("IdempotencyKey")
                    WHERE "IdempotencyKey" IS NOT NULL;
                """);
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
