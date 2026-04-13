using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameFlexiDocToDoc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FlexiDocMaterialIssueForSemiProduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocMaterialIssueForSemiProduct");

            migrationBuilder.RenameColumn(
                name: "FlexiDocMaterialIssueForSemiProductDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocMaterialIssueForSemiProductDate");

            migrationBuilder.RenameColumn(
                name: "FlexiDocSemiProductReceipt",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocSemiProductReceipt");

            migrationBuilder.RenameColumn(
                name: "FlexiDocSemiProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocSemiProductReceiptDate");

            migrationBuilder.RenameColumn(
                name: "FlexiDocSemiProductIssueForProduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocSemiProductIssueForProduct");

            migrationBuilder.RenameColumn(
                name: "FlexiDocSemiProductIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocSemiProductIssueForProductDate");

            migrationBuilder.RenameColumn(
                name: "FlexiDocMaterialIssueForProduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocMaterialIssueForProduct");

            migrationBuilder.RenameColumn(
                name: "FlexiDocMaterialIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocMaterialIssueForProductDate");

            migrationBuilder.RenameColumn(
                name: "FlexiDocProductReceipt",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocProductReceipt");

            migrationBuilder.RenameColumn(
                name: "FlexiDocProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "DocProductReceiptDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DocMaterialIssueForSemiProduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocMaterialIssueForSemiProduct");

            migrationBuilder.RenameColumn(
                name: "DocMaterialIssueForSemiProductDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocMaterialIssueForSemiProductDate");

            migrationBuilder.RenameColumn(
                name: "DocSemiProductReceipt",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocSemiProductReceipt");

            migrationBuilder.RenameColumn(
                name: "DocSemiProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocSemiProductReceiptDate");

            migrationBuilder.RenameColumn(
                name: "DocSemiProductIssueForProduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocSemiProductIssueForProduct");

            migrationBuilder.RenameColumn(
                name: "DocSemiProductIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocSemiProductIssueForProductDate");

            migrationBuilder.RenameColumn(
                name: "DocMaterialIssueForProduct",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocMaterialIssueForProduct");

            migrationBuilder.RenameColumn(
                name: "DocMaterialIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocMaterialIssueForProductDate");

            migrationBuilder.RenameColumn(
                name: "DocProductReceipt",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocProductReceipt");

            migrationBuilder.RenameColumn(
                name: "DocProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders",
                newName: "FlexiDocProductReceiptDate");
        }
    }
}
