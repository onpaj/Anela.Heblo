using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnsToManufactureOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErpOrderNumber",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManualActionRequired",
                schema: "public",
                table: "ManufactureOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErpOrderNumber",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "ManualActionRequired",
                schema: "public",
                table: "ManufactureOrders");
        }
    }
}
