// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Rankings.Migrations
{
    /// <inheritdoc />
    public partial class RankingsDecayPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) Add the three new columns to player_ranks (SC#5 schema freeze — RANK-15 / RANK-16).
            // PascalCase quoted identifiers — Postgres folds unquoted identifiers to lowercase (STATE.md 03-02).
            migrationBuilder.Sql(@"
                ALTER TABLE gamekit.player_ranks
                    ADD COLUMN ""LastDecayAt"" timestamp with time zone,
                    ADD COLUMN ""PlacementMatchesRemaining"" integer NOT NULL DEFAULT 10,
                    ADD COLUMN ""IsInPlacement"" boolean NOT NULL DEFAULT true;");

            // (2) Existing-player data-fixup: players with any game history are NOT in placement.
            // Pitfall 2 from RESEARCH.md: without this, long-time v1 players appear "unranked"
            // on upgrade. The condition Wins>0 OR Losses>0 OR Draws>0 covers all ranked play.
            migrationBuilder.Sql(@"
                UPDATE gamekit.player_ranks
                SET ""IsInPlacement"" = false, ""PlacementMatchesRemaining"" = 0
                WHERE ""Wins"" > 0 OR ""Losses"" > 0 OR ""Draws"" > 0;");

            // (3) Decay candidate index: (LadderId, LastMatchAt) WHERE IsInPlacement = false.
            // Filters placement players who have no LastMatchAt and should not be decay candidates.
            migrationBuilder.Sql(@"
                CREATE INDEX idx_player_ranks_decay_candidates
                ON gamekit.player_ranks (""LadderId"", ""LastMatchAt"")
                WHERE ""IsInPlacement"" = false;");
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
