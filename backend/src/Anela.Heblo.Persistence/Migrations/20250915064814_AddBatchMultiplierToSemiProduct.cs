using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchMultiplierToSemiProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManufactureOrderSemiProducts_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderSemiProducts");

            migrationBuilder.AddColumn<decimal>(
                name: "BatchMultiplier",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderSemiProducts_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                column: "ManufactureOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManufactureOrderSemiProducts_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderSemiProducts");

            migrationBuilder.DropColumn(
                name: "BatchMultiplier",
                schema: "public",
                table: "ManufactureOrderSemiProducts");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderSemiProducts_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                column: "ManufactureOrderId");
        }
    }
}
