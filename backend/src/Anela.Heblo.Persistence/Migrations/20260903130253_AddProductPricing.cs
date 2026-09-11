using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductPrices",
                schema: "public",
                columns: table => new
                {
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PriceWithVat = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPrices", x => x.ProductCode);
                });

            migrationBuilder.CreateTable(
                name: "ProductPriceSyncStates",
                schema: "public",
                columns: table => new
                {
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Target = table.Column<int>(type: "integer", nullable: false),
                    LastPushedPriceWithVat = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LastPushedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RemoteValueAtConflict = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ConflictDetectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceSyncStates", x => new { x.ProductCode, x.Target });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceSyncStates_Status",
                schema: "public",
                table: "ProductPriceSyncStates",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPrices",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ProductPriceSyncStates",
                schema: "public");
        }
    }
}
