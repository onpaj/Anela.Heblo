using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncBankStatementsAndAddManufactureOrderColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ErpOrderNumberSemiproduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErpOrderNumberProduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErpDiscardResidueDocumentNumber",
                schema: "public",
                table: "ManufactureOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ErpDiscardResidueDocumentNumberDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ErpOrderNumberProductDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ErpOrderNumberSemiproductDate",
                schema: "public",
                table: "ManufactureOrders",
                type: "timestamp",
                nullable: true);

            // BankStatements table already exists in database - no need to create it
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // BankStatements table should not be dropped - it existed before this migration

            migrationBuilder.DropColumn(
                name: "ErpDiscardResidueDocumentNumber",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "ErpDiscardResidueDocumentNumberDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "ErpOrderNumberProductDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.DropColumn(
                name: "ErpOrderNumberSemiproductDate",
                schema: "public",
                table: "ManufactureOrders");

            migrationBuilder.AlterColumn<string>(
                name: "ErpOrderNumberSemiproduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ErpOrderNumberProduct",
                schema: "public",
                table: "ManufactureOrders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
