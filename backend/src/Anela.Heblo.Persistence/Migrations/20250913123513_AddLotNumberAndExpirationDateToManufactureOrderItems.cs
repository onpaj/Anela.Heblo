using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotNumberAndExpirationDateToManufactureOrderItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                schema: "public",
                table: "ManufactureOrderProducts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                schema: "public",
                table: "ManufactureOrderProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                schema: "public",
                table: "ManufactureOrderSemiProducts");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                schema: "public",
                table: "ManufactureOrderSemiProducts");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                schema: "public",
                table: "ManufactureOrderProducts");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                schema: "public",
                table: "ManufactureOrderProducts");
        }
    }
}
