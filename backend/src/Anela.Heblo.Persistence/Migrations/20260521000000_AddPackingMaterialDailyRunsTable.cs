using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingMaterialDailyRunsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_PackingMaterialDailyRuns_Date",
                schema: "public",
                table: "PackingMaterialDailyRuns",
                column: "Date",
                unique: true);

            // Backfill: one row per distinct AutomaticConsumption date from PackingMaterialLogs
            // LogType = 2 is AutomaticConsumption
            migrationBuilder.Sql(
                """
                INSERT INTO public."PackingMaterialDailyRuns" ("Date", "ProcessedAt", "MaterialsProcessed")
                SELECT "Date", MIN("CreatedAt"), 0
                FROM public."PackingMaterialLogs"
                WHERE "LogType" = 2
                GROUP BY "Date";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackingMaterialDailyRuns",
                schema: "public");
        }
    }
}
