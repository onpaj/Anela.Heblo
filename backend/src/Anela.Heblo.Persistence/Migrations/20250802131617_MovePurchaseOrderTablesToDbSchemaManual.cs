using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovePurchaseOrderTablesToDbSchemaManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create dbo schema if it doesn't exist
            migrationBuilder.EnsureSchema("dbo");

            // Move tables from public to dbo schema
            migrationBuilder.RenameTable(
                name: "PurchaseOrders",
                newName: "PurchaseOrders",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderLines",
                newName: "PurchaseOrderLines",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderHistory",
                newName: "PurchaseOrderHistory",
                newSchema: "dbo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Move tables back from dbo to public schema
            migrationBuilder.RenameTable(
                name: "PurchaseOrders",
                schema: "dbo",
                newName: "PurchaseOrders");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderLines",
                schema: "dbo",
                newName: "PurchaseOrderLines");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderHistory",
                schema: "dbo",
                newName: "PurchaseOrderHistory");
        }
    }
}
