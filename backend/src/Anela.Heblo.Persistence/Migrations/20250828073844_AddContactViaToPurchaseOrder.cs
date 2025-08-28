using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactViaToPurchaseOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                schema: "dbo",
                table: "TransportBox",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTime",
                schema: "dbo",
                table: "TransportBox",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorId",
                schema: "dbo",
                table: "TransportBox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraProperties",
                schema: "dbo",
                table: "TransportBox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                schema: "dbo",
                table: "TransportBox",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                schema: "dbo",
                table: "TransportBox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContactVia",
                schema: "dbo",
                table: "PurchaseOrders",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                schema: "dbo",
                table: "TransportBox");

            migrationBuilder.DropColumn(
                name: "CreationTime",
                schema: "dbo",
                table: "TransportBox");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                schema: "dbo",
                table: "TransportBox");

            migrationBuilder.DropColumn(
                name: "ExtraProperties",
                schema: "dbo",
                table: "TransportBox");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                schema: "dbo",
                table: "TransportBox");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                schema: "dbo",
                table: "TransportBox");

            migrationBuilder.DropColumn(
                name: "ContactVia",
                schema: "dbo",
                table: "PurchaseOrders");
        }
    }
}
