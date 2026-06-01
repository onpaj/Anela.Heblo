using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceFilterIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoice_IsSynced",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "IsSynced");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoice_ErrorType",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "ErrorType");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoice_CustomerName",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "CustomerName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoice_IsSynced",
                schema: "dbo",
                table: "IssuedInvoice");

            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoice_ErrorType",
                schema: "dbo",
                table: "IssuedInvoice");

            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoice_CustomerName",
                schema: "dbo",
                table: "IssuedInvoice");
        }
    }
}
