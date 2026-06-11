using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackedByUserIdToPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PackedByUserId",
                schema: "public",
                table: "Packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Packages_PackedByUserId",
                schema: "public",
                table: "Packages",
                column: "PackedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Packages_PackedByUserId",
                schema: "public",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "PackedByUserId",
                schema: "public",
                table: "Packages");
        }
    }
}
