// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <summary>
    /// Adds the <c>merged_into_player_id</c> tombstone column and <c>deleted_at</c> column to
    /// <c>gamekit.players</c> (Phase 10 account merge, SC#2). Core is the sole owner of this column
    /// per CLAUDE.md per-package boundary rule (packages never modify Core tables in their migrations).
    /// The self-FK uses <c>ON DELETE SET NULL</c> so that deleting the target player (GDPR) nulls the
    /// tombstone reference rather than blocking the hard-delete with a constraint violation (T-10-01-02).
    /// </summary>
    public partial class AddMergedIntoPlayerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoPlayerId",
                schema: "gamekit",
                table: "players",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "gamekit",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_players_players_MergedIntoPlayerId",
                schema: "gamekit",
                table: "players",
                column: "MergedIntoPlayerId",
                principalSchema: "gamekit",
                principalTable: "players",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
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
