using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufactureOrderConditionsReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufactureOrderConditionsReadings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    InnerTemperature = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    InnerHumidity = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    OuterTemperature = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    OuterHumidity = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrderConditionsReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufactureOrderConditionsReadings_ManufactureOrders_Manufa~",
                        column: x => x.ManufactureOrderId,
                        principalSchema: "public",
                        principalTable: "ManufactureOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderConditionsReadings_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderConditionsReadings",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderConditionsReadings_ManufactureOrderId_Stage",
                schema: "public",
                table: "ManufactureOrderConditionsReadings",
                columns: new[] { "ManufactureOrderId", "Stage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufactureOrderConditionsReadings",
                schema: "public");
        }
    }
}
