// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Lobby.Data.Migrations
{
    /// <inheritdoc />
    public partial class LobbyInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamekit");

            migrationBuilder.CreateTable(
                name: "lobbies",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    MaxMembers = table.Column<int>(type: "integer", nullable: false),
                    RegionName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lobbies", x => x.Id);
                    // FK to gamekit.players. principalTable corrected from "Player" (entity-class
                    // default) to "players" — design-time factory does not apply Core configurations
                    // (per-package migration boundary). Cross-package FK names are PascalCase per Pitfall §4.
                    table.ForeignKey(
                        name: "FK_lobbies_players_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    // FK to gamekit.ladders (Rankings package, Phase 4). principalTable corrected
                    // from "Ladder" to "ladders" — same cross-package convention as Matchmaking.
                    table.ForeignKey(
                        name: "FK_lobbies_ladders_LadderId",
                        column: x => x.LadderId,
                        principalSchema: "gamekit",
                        principalTable: "ladders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "lobby_members",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LobbyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ready = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lobby_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lobby_members_lobbies_LobbyId",
                        column: x => x.LobbyId,
                        principalSchema: "gamekit",
                        principalTable: "lobbies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lobby_members_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lobbies_LadderId",
                schema: "gamekit",
                table: "lobbies",
                column: "LadderId");

            migrationBuilder.CreateIndex(
                name: "IX_lobbies_OwnerId",
                schema: "gamekit",
                table: "lobbies",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_lobby_members_LobbyId_PlayerId",
                schema: "gamekit",
                table: "lobby_members",
                columns: new[] { "LobbyId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lobby_members_PlayerId",
                schema: "gamekit",
                table: "lobby_members",
                column: "PlayerId");
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
