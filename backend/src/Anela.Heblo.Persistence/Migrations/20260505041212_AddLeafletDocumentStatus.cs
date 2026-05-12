using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeafletDocumentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IndexedAt",
                table: "LeafletDocuments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "LeafletDocuments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "processing");

            migrationBuilder.CreateIndex(
                name: "IX_LeafletDocuments_Status",
                table: "LeafletDocuments",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeafletDocuments_Status",
                table: "LeafletDocuments");

            migrationBuilder.DropColumn(
                name: "IndexedAt",
                table: "LeafletDocuments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LeafletDocuments");
        }
    }
}
