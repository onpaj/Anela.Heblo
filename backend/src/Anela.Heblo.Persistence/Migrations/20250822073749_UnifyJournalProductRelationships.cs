using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyJournalProductRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, migrate existing data from JournalEntryProductFamilies to JournalEntryProducts
            migrationBuilder.Sql(@"
                INSERT INTO ""JournalEntryProducts"" (""JournalEntryId"", ""ProductCode"", ""CreatedAt"")
                SELECT ""JournalEntryId"", ""ProductCodePrefix"", ""CreatedAt""
                FROM ""JournalEntryProductFamilies""
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""JournalEntryProducts"" jep 
                    WHERE jep.""JournalEntryId"" = ""JournalEntryProductFamilies"".""JournalEntryId"" 
                    AND jep.""ProductCode"" = ""JournalEntryProductFamilies"".""ProductCodePrefix""
                );
            ");

            // Drop the old table
            migrationBuilder.DropTable(
                name: "JournalEntryProductFamilies");

            // Rename column to reflect its new purpose as prefix
            migrationBuilder.RenameColumn(
                name: "ProductCode",
                table: "JournalEntryProducts",
                newName: "ProductCodePrefix");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProductCodePrefix",
                table: "JournalEntryProducts",
                newName: "ProductCode");

            migrationBuilder.CreateTable(
                name: "JournalEntryProductFamilies",
                columns: table => new
                {
                    JournalEntryId = table.Column<int>(type: "integer", nullable: false),
                    ProductCodePrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryProductFamilies", x => new { x.JournalEntryId, x.ProductCodePrefix });
                    table.ForeignKey(
                        name: "FK_JournalEntryProductFamilies_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryProductFamilies_ProductCodePrefix",
                table: "JournalEntryProductFamilies",
                column: "ProductCodePrefix");
        }
    }
}
