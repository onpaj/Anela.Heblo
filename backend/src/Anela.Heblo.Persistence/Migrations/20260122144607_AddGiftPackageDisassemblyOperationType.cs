using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftPackageDisassemblyOperationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "operation_type",
                table: "gift_package_manufacture_logs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_gift_package_manufacture_logs_operation_type",
                table: "gift_package_manufacture_logs",
                column: "operation_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gift_package_manufacture_logs_operation_type",
                table: "gift_package_manufacture_logs");

            migrationBuilder.DropColumn(
                name: "operation_type",
                table: "gift_package_manufacture_logs");
        }
    }
}
