using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGraphItemIdToIngestedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeafletDocuments_SourcePath",
                table: "LeafletDocuments");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseDocuments_SourcePath",
                schema: "public",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.AddColumn<string>(
                name: "DriveId",
                table: "LeafletDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GraphItemId",
                table: "LeafletDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriveId",
                schema: "public",
                table: "KnowledgeBaseDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GraphItemId",
                schema: "public",
                table: "KnowledgeBaseDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeafletDocuments_DriveId_GraphItemId",
                table: "LeafletDocuments",
                columns: new[] { "DriveId", "GraphItemId" },
                unique: true,
                filter: "\"GraphItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_DriveId_GraphItemId",
                schema: "public",
                table: "KnowledgeBaseDocuments",
                columns: new[] { "DriveId", "GraphItemId" },
                unique: true,
                filter: "\"GraphItemId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeafletDocuments_DriveId_GraphItemId",
                table: "LeafletDocuments");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseDocuments_DriveId_GraphItemId",
                schema: "public",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropColumn(
                name: "DriveId",
                table: "LeafletDocuments");

            migrationBuilder.DropColumn(
                name: "GraphItemId",
                table: "LeafletDocuments");

            migrationBuilder.DropColumn(
                name: "DriveId",
                schema: "public",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.DropColumn(
                name: "GraphItemId",
                schema: "public",
                table: "KnowledgeBaseDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_LeafletDocuments_SourcePath",
                table: "LeafletDocuments",
                column: "SourcePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_SourcePath",
                schema: "public",
                table: "KnowledgeBaseDocuments",
                column: "SourcePath",
                unique: true);
        }
    }
}
