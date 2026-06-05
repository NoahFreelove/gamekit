using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AuthPasswordHashLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AUTH-18: Extend password_hash column from varchar(72) to varchar(512) to accommodate
            // Argon2id encoded strings. BCrypt hashes are 60 chars; Argon2id encoded strings are
            // ~80–120 chars depending on m/t/p/hashLength parameters. 512 provides headroom for
            // future hashers. This is a pure ALTER COLUMN — no data migration, no rows modified.
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "gamekit",
                table: "player_credentials",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(72)",
                oldMaxLength: 72);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "gamekit",
                table: "player_credentials",
                type: "character varying(72)",
                maxLength: 72,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);
        }
    }
}
