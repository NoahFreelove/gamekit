// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Matchmaking.Migrations
{
    /// <summary>
    /// Adds <c>TicketType</c> to the Matchmaking-owned <c>matchmaking_tickets</c> table
    /// (MATCH-19 backfill priority). <c>ParticipationFraction</c> on the Core-owned
    /// <c>session_participants</c> table is owned by the Core migration
    /// <c>20260519000000_AddSessionParticipationFraction</c> — not this migration
    /// (CLAUDE.md per-package boundary rule: packages never modify Core tables).
    /// </summary>
    public partial class MatchmakingBackfillRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add TicketType to matchmaking_tickets (MATCH-19 backfill priority).
            // integer NOT NULL DEFAULT 0 — existing tickets = Normal (0) without data fixup.
            migrationBuilder.Sql(@"
                ALTER TABLE gamekit.matchmaking_tickets
                    ADD COLUMN ""TicketType"" integer NOT NULL DEFAULT 0;");
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
