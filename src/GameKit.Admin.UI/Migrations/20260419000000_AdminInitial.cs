using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Admin.UI.Migrations
{
    /// <inheritdoc />
    public partial class AdminInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gamekit");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "admin_users",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_users", x => x.Id);
                    table.CheckConstraint("ck_admin_users_role", "\"Role\" IN ('admin','superadmin')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_users_username",
                schema: "gamekit",
                table: "admin_users",
                column: "Username",
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
