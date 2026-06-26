using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierCoolingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierCoolingSettings",
                schema: "public",
                columns: table => new
                {
                    Carrier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DeliveryHandling = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Cooling = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierCoolingSettings", x => new { x.Carrier, x.DeliveryHandling });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierCoolingSettings",
                schema: "public");
        }
    }
}
