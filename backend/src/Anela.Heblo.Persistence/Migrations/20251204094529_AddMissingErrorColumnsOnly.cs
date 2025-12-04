using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingErrorColumnsOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Error_Code",
                schema: "dbo",
                table: "IssuedInvoiceSyncData",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error_Field",
                schema: "dbo",
                table: "IssuedInvoiceSyncData",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Error_Code",
                schema: "dbo",
                table: "IssuedInvoiceSyncData");

            migrationBuilder.DropColumn(
                name: "Error_Field",
                schema: "dbo",
                table: "IssuedInvoiceSyncData");
        }
    }
}
