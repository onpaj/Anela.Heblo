using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufactureOrderPlannedDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrders_PlannedDate",
                schema: "public",
                table: "ManufactureOrders",
                column: "PlannedDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManufactureOrders_PlannedDate",
                schema: "public",
                table: "ManufactureOrders");
        }
    }
}
