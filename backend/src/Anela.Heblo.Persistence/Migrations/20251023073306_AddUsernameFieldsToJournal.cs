using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameFieldsToJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUsername",
                schema: "public",
                table: "JournalEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUsername",
                schema: "public",
                table: "JournalEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedByUsername",
                schema: "public",
                table: "JournalEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUsername",
                schema: "public",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "DeletedByUsername",
                schema: "public",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ModifiedByUsername",
                schema: "public",
                table: "JournalEntries");
        }
    }
}
