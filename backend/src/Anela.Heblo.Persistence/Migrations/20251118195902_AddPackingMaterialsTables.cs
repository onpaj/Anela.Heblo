using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingMaterialsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackingMaterial",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConsumptionRate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ConsumptionType = table.Column<int>(type: "integer", nullable: false),
                    CurrentQuantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingMaterial", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackingMaterialLog",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackingMaterialId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OldQuantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    NewQuantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    LogType = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingMaterialLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingMaterialLog_PackingMaterial_PackingMaterialId",
                        column: x => x.PackingMaterialId,
                        principalSchema: "dbo",
                        principalTable: "PackingMaterial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterial_Name",
                schema: "dbo",
                table: "PackingMaterial",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialLog_LogType",
                schema: "dbo",
                table: "PackingMaterialLog",
                column: "LogType");

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialLog_MaterialId_Date",
                schema: "dbo",
                table: "PackingMaterialLog",
                columns: new[] { "PackingMaterialId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialLog_PackingMaterialId",
                schema: "dbo",
                table: "PackingMaterialLog",
                column: "PackingMaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackingMaterialLog",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PackingMaterial",
                schema: "dbo");
        }
    }
}
