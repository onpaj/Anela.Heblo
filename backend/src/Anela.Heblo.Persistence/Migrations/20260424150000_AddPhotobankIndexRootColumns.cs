using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotobankIndexRootColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeltaLink",
                schema: "public",
                table: "PhotobankIndexRoots",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriveId",
                schema: "public",
                table: "PhotobankIndexRoots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIndexedAt",
                schema: "public",
                table: "PhotobankIndexRoots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RootItemId",
                schema: "public",
                table: "PhotobankIndexRoots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeltaLink",
                schema: "public",
                table: "PhotobankIndexRoots");

            migrationBuilder.DropColumn(
                name: "DriveId",
                schema: "public",
                table: "PhotobankIndexRoots");

            migrationBuilder.DropColumn(
                name: "LastIndexedAt",
                schema: "public",
                table: "PhotobankIndexRoots");

            migrationBuilder.DropColumn(
                name: "RootItemId",
                schema: "public",
                table: "PhotobankIndexRoots");
        }
    }
}
