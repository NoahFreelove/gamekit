using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Matchmaking.Migrations
{
    /// <inheritdoc />
    public partial class MatchmakingInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamekit");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "decline_history",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProposalId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decline_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_decline_history_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parties",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyCode = table.Column<string>(type: "citext", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    OwnerPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parties_players_OwnerPlayerId",
                        column: x => x.OwnerPlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matchmaking_tickets",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TerminalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matchmaking_tickets", x => x.Id);
                    // FK to gamekit.ladders (Rankings package, Phase 4). The principalTable
                    // name was corrected from "Ladder" (entity-class default) to "ladders" by
                    // Plan 05-02 Task 3 — EF defaulted to the entity name because the design-time
                    // factory does not apply Rankings configurations (per-package migration
                    // boundary). Cross-package FK names are PascalCase per Pitfall §4.
                    table.ForeignKey(
                        name: "FK_matchmaking_tickets_ladders_LadderId",
                        column: x => x.LadderId,
                        principalSchema: "gamekit",
                        principalTable: "ladders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_matchmaking_tickets_game_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gamekit",
                        principalTable: "game_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_matchmaking_tickets_parties_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "gamekit",
                        principalTable: "parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "party_members",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_party_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_party_members_parties_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "gamekit",
                        principalTable: "parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_party_members_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_events",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_events_matchmaking_tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "gamekit",
                        principalTable: "matchmaking_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_decline_history_player_declined_at",
                schema: "gamekit",
                table: "decline_history",
                columns: new[] { "PlayerId", "DeclinedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_matchmaking_tickets_ladder_pool_status",
                schema: "gamekit",
                table: "matchmaking_tickets",
                columns: new[] { "LadderId", "PoolName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_matchmaking_tickets_PartyId",
                schema: "gamekit",
                table: "matchmaking_tickets",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_matchmaking_tickets_SessionId",
                schema: "gamekit",
                table: "matchmaking_tickets",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_parties_OwnerPlayerId",
                schema: "gamekit",
                table: "parties",
                column: "OwnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_parties_PartyCode",
                schema: "gamekit",
                table: "parties",
                column: "PartyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_party_members_PartyId_PlayerId",
                schema: "gamekit",
                table: "party_members",
                columns: new[] { "PartyId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_party_members_PlayerId",
                schema: "gamekit",
                table: "party_members",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_events_TicketId",
                schema: "gamekit",
                table: "ticket_events",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decline_history",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "party_members",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "ticket_events",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "matchmaking_tickets",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "parties",
                schema: "gamekit");
        }
    }
}
