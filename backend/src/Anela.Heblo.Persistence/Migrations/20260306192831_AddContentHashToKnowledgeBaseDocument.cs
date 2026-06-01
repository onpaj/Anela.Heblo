using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashToKnowledgeBaseDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                schema: "dbo",
                table: "KnowledgeBaseDocuments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_ContentHash",
                schema: "dbo",
                table: "KnowledgeBaseDocuments",
                column: "ContentHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseDocuments_ContentHash",
                schema: "dbo",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                schema: "dbo",
                table: "KnowledgeBaseDocuments");
        }
    }
}
