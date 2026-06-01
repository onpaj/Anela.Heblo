using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCodeRenameNameToMaterialNameInPurchaseOrderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                schema: "dbo",
                table: "PurchaseOrderLines");

            migrationBuilder.RenameColumn(
                name: "Name",
                schema: "dbo",
                table: "PurchaseOrderLines",
                newName: "MaterialName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaterialName",
                schema: "dbo",
                table: "PurchaseOrderLines",
                newName: "Name");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "dbo",
                table: "PurchaseOrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
