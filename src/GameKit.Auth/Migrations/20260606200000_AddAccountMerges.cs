using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountMerges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_merges",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RedisCleanedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_merges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_merges_players_TargetPlayerId",
                        column: x => x.TargetPlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // UNIQUE index on SourcePlayerId: prevents double-merge (SC#1, T-10-02-01).
            // A second concurrent INSERT with the same SourcePlayerId will receive Postgres 23505.
            migrationBuilder.CreateIndex(
                name: "IX_account_merges_SourcePlayerId",
                schema: "gamekit",
                table: "account_merges",
                column: "SourcePlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_merges_TargetPlayerId",
                schema: "gamekit",
                table: "account_merges",
                column: "TargetPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_merges",
                schema: "gamekit");
        }
    }
}
