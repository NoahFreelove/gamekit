using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AuthInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamekit");

            // The PlayerCredential.Username column uses Postgres citext for case-insensitive
            // uniqueness. Ensure the extension is installed before any table that references
            // it. Duplicates the Npgsql:PostgresExtension annotation below (which EF uses to
            // emit the same CREATE EXTENSION statement) for defensive auditability.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "player_credentials",
                schema: "gamekit",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_credentials", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_player_credentials_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_identities",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_player_identities_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_player_credentials_Username",
                schema: "gamekit",
                table: "player_credentials",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_identities_PlayerId",
                schema: "gamekit",
                table: "player_identities",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_player_identities_Provider_ExternalId",
                schema: "gamekit",
                table: "player_identities",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_FamilyId",
                schema: "gamekit",
                table: "refresh_tokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_PlayerId_RevokedAt",
                schema: "gamekit",
                table: "refresh_tokens",
                columns: new[] { "PlayerId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                schema: "gamekit",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);
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
