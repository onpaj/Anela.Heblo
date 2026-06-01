using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveTablesToDboSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "JournalEntryTags",
                newName: "JournalEntryTags",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntryTagAssignments",
                newName: "JournalEntryTagAssignments",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntryProducts",
                newName: "JournalEntryProducts",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "JournalEntries",
                newName: "JournalEntries",
                newSchema: "dbo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "JournalEntryTags",
                schema: "dbo",
                newName: "JournalEntryTags");

            migrationBuilder.RenameTable(
                name: "JournalEntryTagAssignments",
                schema: "dbo",
                newName: "JournalEntryTagAssignments");

            migrationBuilder.RenameTable(
                name: "JournalEntryProducts",
                schema: "dbo",
                newName: "JournalEntryProducts");

            migrationBuilder.RenameTable(
                name: "JournalEntries",
                schema: "dbo",
                newName: "JournalEntries");
        }
    }
}
