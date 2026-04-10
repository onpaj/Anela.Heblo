using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexiDocFieldsToManufactureOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlexiDocMaterialIssueForProduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlexiDocMaterialIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlexiDocMaterialIssueForSemiProduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlexiDocMaterialIssueForSemiProductDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlexiDocProductReceipt",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlexiDocProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlexiDocSemiProductIssueForProduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlexiDocSemiProductIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlexiDocSemiProductReceipt",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlexiDocSemiProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlexiDocMaterialIssueForProduct",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocMaterialIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocMaterialIssueForSemiProduct",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocMaterialIssueForSemiProductDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocProductReceipt",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocSemiProductIssueForProduct",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocSemiProductIssueForProductDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocSemiProductReceipt",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "FlexiDocSemiProductReceiptDate",
                schema: "public",
                table: "ManufactureOrders");
        }
    }
}
