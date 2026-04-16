using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <inheritdoc />
    public partial class CoreInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamekit");

            migrationBuilder.CreateTable(
                name: "admin_audit_log",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Before = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    After = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "game_sessions",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "players",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsBanned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    BannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BanReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "session_participants",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Team = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    RatingBefore = table.Column<double>(type: "double precision", nullable: true),
                    RatingAfter = table.Column<double>(type: "double precision", nullable: true),
                    RatingDelta = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_participants_game_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gamekit",
                        principalTable: "game_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_session_participants_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_log_ActorId",
                schema: "gamekit",
                table: "admin_audit_log",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_log_CreatedAt",
                schema: "gamekit",
                table: "admin_audit_log",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_log_TargetType_TargetId",
                schema: "gamekit",
                table: "admin_audit_log",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_CreatedAt",
                schema: "gamekit",
                table: "game_sessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_LadderId_CreatedAt",
                schema: "gamekit",
                table: "game_sessions",
                columns: new[] { "LadderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_PlayerId",
                schema: "gamekit",
                table: "session_participants",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_SessionId",
                schema: "gamekit",
                table: "session_participants",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit_log",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "session_participants",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "game_sessions",
                schema: "gamekit");

            migrationBuilder.DropTable(
                name: "players",
                schema: "gamekit");
        }
    }
}
