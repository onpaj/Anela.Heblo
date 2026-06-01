using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightToleranceToManufactureOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WeightDifference",
                schema: "public",
                table: "ManufactureOrders",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeightWithinTolerance",
                schema: "public",
                table: "ManufactureOrders",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightDifference",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "WeightWithinTolerance",
                schema: "public",
                table: "ManufactureOrders");
        }
    }
}
