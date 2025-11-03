using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameAccountingPrescriptionToAccountingTemplateCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AccountingPrescription",
                table: "ClassificationRules",
                newName: "AccountingTemplateCode");

            migrationBuilder.RenameColumn(
                name: "AccountingPrescription",
                table: "ClassificationHistory",
                newName: "AccountingTemplateCode");

            migrationBuilder.AlterColumn<DateTime>(
                name: "InvoiceDate",
                table: "ClassificationHistory",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AccountingTemplateCode",
                table: "ClassificationRules",
                newName: "AccountingPrescription");

            migrationBuilder.RenameColumn(
                name: "AccountingTemplateCode",
                table: "ClassificationHistory",
                newName: "AccountingPrescription");

            migrationBuilder.AlterColumn<DateTime>(
                name: "InvoiceDate",
                table: "ClassificationHistory",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);
        }
    }
}
