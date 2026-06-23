using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Rankings.Migrations
{
    /// <inheritdoc />
    public partial class RankingsInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamekit");

            // Ensure citext extension is available — used by ladders.name and service_tokens.name.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "ladders",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Config = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastDrainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ladders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "service_tokens",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ladder_seasons",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedByAdminId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ladder_seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ladder_seasons_ladders_LadderId",
                        column: x => x.LadderId,
                        principalSchema: "gamekit",
                        principalTable: "ladders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_rating_updates",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_rating_updates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_rating_updates_game_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gamekit",
                        principalTable: "game_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pending_rating_updates_ladders_LadderId",
                        column: x => x.LadderId,
                        principalSchema: "gamekit",
                        principalTable: "ladders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pending_rating_updates_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "player_ranks",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    RatingDeviation = table.Column<double>(type: "double precision", nullable: false),
                    Volatility = table.Column<double>(type: "double precision", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    Draws = table.Column<int>(type: "integer", nullable: false),
                    LastMatchAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_ranks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_player_ranks_ladders_LadderId",
                        column: x => x.LadderId,
                        principalSchema: "gamekit",
                        principalTable: "ladders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_ranks_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_complete_idempotency",
                schema: "gamekit",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestBodyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CachedResponse = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_complete_idempotency", x => new { x.SessionId, x.IdempotencyKey });
                    table.ForeignKey(
                        name: "FK_session_complete_idempotency_game_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "gamekit",
                        principalTable: "game_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "season_rank_archive",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LadderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    RatingDeviation = table.Column<double>(type: "double precision", nullable: false),
                    Volatility = table.Column<double>(type: "double precision", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    Draws = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_season_rank_archive", x => x.Id);
                    table.ForeignKey(
                        name: "FK_season_rank_archive_ladder_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "gamekit",
                        principalTable: "ladder_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_season_rank_archive_ladders_LadderId",
                        column: x => x.LadderId,
                        principalSchema: "gamekit",
                        principalTable: "ladders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_season_rank_archive_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_ladders_Name",
                schema: "gamekit",
                table: "ladders",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_Name",
                schema: "gamekit",
                table: "service_tokens",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_tokens_TokenHash",
                schema: "gamekit",
                table: "service_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ladder_seasons_LadderId_SeasonNumber",
                schema: "gamekit",
                table: "ladder_seasons",
                columns: new[] { "LadderId", "SeasonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_ranks_PlayerId_LadderId",
                schema: "gamekit",
                table: "player_ranks",
                columns: new[] { "PlayerId", "LadderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_player_ranks_ladder_rating",
                schema: "gamekit",
                table: "player_ranks",
                columns: new[] { "LadderId", "Rating" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_season_rank_archive_LadderId_SeasonId_Rating",
                schema: "gamekit",
                table: "season_rank_archive",
                columns: new[] { "LadderId", "SeasonId", "Rating" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_pending_rating_updates_SessionId",
                schema: "gamekit",
                table: "pending_rating_updates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_pending_rating_updates_PlayerId",
                schema: "gamekit",
                table: "pending_rating_updates",
                column: "PlayerId");

            // Partial index for efficient ticker drain (unapplied rows per ladder).
            // Column names are PascalCase (Npgsql EF Core convention — no snake_case mapping in this project).
            migrationBuilder.Sql(@"
                CREATE INDEX idx_pending_rating_updates_ladder_pending
                ON gamekit.pending_rating_updates (""LadderId"", ""EnqueuedAt"")
                WHERE ""AppliedAt"" IS NULL;");

            // Raw-SQL cross-package FK: game_sessions.LadderId → ladders.Id (Pitfall §4).
            // Column names are PascalCase — confirmed by `\d gamekit.game_sessions` on Postgres 17.9.
            // Core's migration owns the column; Rankings adds ONLY the FK constraint.
            migrationBuilder.Sql(@"ALTER TABLE gamekit.game_sessions ADD CONSTRAINT fk_game_sessions_ladders FOREIGN KEY (""LadderId"") REFERENCES gamekit.ladders(""Id"") ON DELETE SET NULL;");
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
