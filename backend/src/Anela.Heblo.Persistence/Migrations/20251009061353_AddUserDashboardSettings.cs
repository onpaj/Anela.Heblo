using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDashboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufactureOrderAuditLogs",
                schema: "public");

            migrationBuilder.CreateTable(
                name: "UserDashboardSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    VisibleTiles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TileOrder = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDashboardSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardSettings_UserId",
                table: "UserDashboardSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDashboardSettings");

            migrationBuilder.CreateTable(
                name: "ManufactureOrderAuditLogs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NewValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OldValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp", nullable: false),
                    User = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrderAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufactureOrderAuditLogs_ManufactureOrders_ManufactureOrde~",
                        column: x => x.ManufactureOrderId,
                        principalSchema: "public",
                        principalTable: "ManufactureOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderAuditLogs_Action",
                schema: "public",
                table: "ManufactureOrderAuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderAuditLogs_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderAuditLogs",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderAuditLogs_Timestamp",
                schema: "public",
                table: "ManufactureOrderAuditLogs",
                column: "Timestamp");
        }
    }
}
