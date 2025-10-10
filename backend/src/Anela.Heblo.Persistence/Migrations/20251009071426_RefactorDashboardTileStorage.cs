using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorDashboardTileStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TileOrder",
                table: "UserDashboardSettings");

            migrationBuilder.DropColumn(
                name: "VisibleTiles",
                table: "UserDashboardSettings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastModified",
                table: "UserDashboardSettings",
                type: "timestamp",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_UserDashboardSettings_UserId",
                table: "UserDashboardSettings",
                column: "UserId");

            migrationBuilder.CreateTable(
                name: "UserDashboardTiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TileId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDashboardTiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDashboardTiles_UserDashboardSettings_UserId",
                        column: x => x.UserId,
                        principalTable: "UserDashboardSettings",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardTiles_UserId_DisplayOrder",
                table: "UserDashboardTiles",
                columns: new[] { "UserId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardTiles_UserId_TileId",
                table: "UserDashboardTiles",
                columns: new[] { "UserId", "TileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDashboardTiles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_UserDashboardSettings_UserId",
                table: "UserDashboardSettings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastModified",
                table: "UserDashboardSettings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp");

            migrationBuilder.AddColumn<string>(
                name: "TileOrder",
                table: "UserDashboardSettings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VisibleTiles",
                table: "UserDashboardSettings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }
    }
}
