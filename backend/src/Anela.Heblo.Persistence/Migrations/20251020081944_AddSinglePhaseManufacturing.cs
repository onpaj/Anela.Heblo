using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSinglePhaseManufacturing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add ManufactureType column with default MultiPhase (0)
            migrationBuilder.AddColumn<int>(
                name: "ManufactureType",
                table: "ManufactureOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Add new PlannedDate column
            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedDate",
                table: "ManufactureOrders",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1900, 1, 1));

            // Copy data from SemiProductPlannedDate to PlannedDate for existing records
            migrationBuilder.Sql(
                "UPDATE \"ManufactureOrders\" SET \"PlannedDate\" = \"SemiProductPlannedDate\"");

            // Make SemiProduct relationship optional by removing NOT NULL constraint if exists
            migrationBuilder.AlterColumn<DateOnly>(
                name: "SemiProductPlannedDate",
                table: "ManufactureOrders",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            // Drop the old columns
            migrationBuilder.DropColumn(
                name: "SemiProductPlannedDate",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "ProductPlannedDate",
                table: "ManufactureOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add back the old columns
            migrationBuilder.AddColumn<DateOnly>(
                name: "SemiProductPlannedDate",
                table: "ManufactureOrders",
                type: "date",
                nullable: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ProductPlannedDate",
                table: "ManufactureOrders",
                type: "date",
                nullable: false);

            // Copy PlannedDate back to SemiProductPlannedDate
            migrationBuilder.Sql(
                "UPDATE \"ManufactureOrders\" SET \"SemiProductPlannedDate\" = \"PlannedDate\", \"ProductPlannedDate\" = \"PlannedDate\"");

            // Drop the new columns
            migrationBuilder.DropColumn(
                name: "ManufactureType",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "PlannedDate",
                table: "ManufactureOrders");
        }
    }
}
