using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDetailsToClassificationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "ClassificationHistory",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ClassificationHistory",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceDate",
                table: "ClassificationHistory",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "ClassificationHistory",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_CompanyName",
                table: "ClassificationHistory",
                column: "CompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_InvoiceNumber",
                table: "ClassificationHistory",
                column: "InvoiceNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassificationHistory_CompanyName",
                table: "ClassificationHistory");

            migrationBuilder.DropIndex(
                name: "IX_ClassificationHistory_InvoiceNumber",
                table: "ClassificationHistory");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "ClassificationHistory");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ClassificationHistory");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                table: "ClassificationHistory");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "ClassificationHistory");
        }
    }
}
