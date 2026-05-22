using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PackingMaterialId1",
                schema: "public",
                table: "PackingMaterialLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GiftSettings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ThresholdCzk = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackingMaterialDailyRuns",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MaterialsProcessed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingMaterialDailyRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialLogs_PackingMaterialId1",
                schema: "public",
                table: "PackingMaterialLogs",
                column: "PackingMaterialId1");

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialDailyRuns_Date",
                schema: "public",
                table: "PackingMaterialDailyRuns",
                column: "Date",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackingMaterialLogs_PackingMaterials_PackingMaterialId1",
                schema: "public",
                table: "PackingMaterialLogs",
                column: "PackingMaterialId1",
                principalSchema: "public",
                principalTable: "PackingMaterials",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackingMaterialLogs_PackingMaterials_PackingMaterialId1",
                schema: "public",
                table: "PackingMaterialLogs");

            migrationBuilder.DropTable(
                name: "GiftSettings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PackingMaterialDailyRuns",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_PackingMaterialLogs_PackingMaterialId1",
                schema: "public",
                table: "PackingMaterialLogs");

            migrationBuilder.DropColumn(
                name: "PackingMaterialId1",
                schema: "public",
                table: "PackingMaterialLogs");
        }
    }
}
