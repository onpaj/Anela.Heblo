using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeErpOrderNumberToSeparateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ErpOrderNumber",
                schema: "public",
                table: "ManufactureOrders",
                newName: "ErpOrderNumberSemiproduct");

            migrationBuilder.AddColumn<string>(
                name: "ErpOrderNumberProduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErpOrderNumberProduct",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.RenameColumn(
                name: "ErpOrderNumberSemiproduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "ErpOrderNumber");
        }
    }
}
