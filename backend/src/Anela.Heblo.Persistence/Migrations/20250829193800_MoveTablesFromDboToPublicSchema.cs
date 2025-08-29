using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveTablesFromDboToPublicSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.RenameTable(
                name: "TransportBoxStateLog",
                schema: "dbo",
                newName: "TransportBoxStateLog",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "TransportBoxItem",
                schema: "dbo",
                newName: "TransportBoxItem",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "TransportBox",
                schema: "dbo",
                newName: "TransportBox",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PurchaseOrders",
                schema: "dbo",
                newName: "PurchaseOrders",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderLines",
                schema: "dbo",
                newName: "PurchaseOrderLines",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderHistory",
                schema: "dbo",
                newName: "PurchaseOrderHistory",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "JournalEntryTags",
                schema: "dbo",
                newName: "JournalEntryTags",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "JournalEntryTagAssignments",
                schema: "dbo",
                newName: "JournalEntryTagAssignments",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "JournalEntryProducts",
                schema: "dbo",
                newName: "JournalEntryProducts",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "JournalEntries",
                schema: "dbo",
                newName: "JournalEntries",
                newSchema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.RenameTable(
                name: "TransportBoxStateLog",
                schema: "public",
                newName: "TransportBoxStateLog",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "TransportBoxItem",
                schema: "public",
                newName: "TransportBoxItem",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "TransportBox",
                schema: "public",
                newName: "TransportBox",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PurchaseOrders",
                schema: "public",
                newName: "PurchaseOrders",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderLines",
                schema: "public",
                newName: "PurchaseOrderLines",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PurchaseOrderHistory",
                schema: "public",
                newName: "PurchaseOrderHistory",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntryTags",
                schema: "public",
                newName: "JournalEntryTags",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntryTagAssignments",
                schema: "public",
                newName: "JournalEntryTagAssignments",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntryProducts",
                schema: "public",
                newName: "JournalEntryProducts",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntries",
                schema: "public",
                newName: "JournalEntries",
                newSchema: "dbo");
        }
    }
}
