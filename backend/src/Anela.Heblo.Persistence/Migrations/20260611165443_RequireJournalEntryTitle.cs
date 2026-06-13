using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireJournalEntryTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill NULL or empty/whitespace titles before making the column NOT NULL.
            // "Bez názvu" matches the string already rendered by the frontend for null-title
            // entries (JournalList.tsx: title || "Bez názvu"), so migrated rows are visually
            // unchanged for users.
            migrationBuilder.Sql(@"
                UPDATE public.""JournalEntries""
                SET ""Title"" = 'Bez názvu'
                WHERE ""Title"" IS NULL OR trim(""Title"") = '';
            ");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "public",
                table: "JournalEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "public",
                table: "JournalEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
