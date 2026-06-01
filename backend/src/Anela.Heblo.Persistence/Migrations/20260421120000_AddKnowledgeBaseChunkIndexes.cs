using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseChunkIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_knowledgebase_chunks_document_chunk",
                schema: "dbo",
                table: "KnowledgeBaseChunks",
                columns: new[] { "DocumentId", "ChunkIndex" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledgebase_documents_contenttype",
                schema: "dbo",
                table: "KnowledgeBaseDocuments",
                column: "ContentType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_knowledgebase_chunks_document_chunk",
                schema: "dbo",
                table: "KnowledgeBaseChunks");

            migrationBuilder.DropIndex(
                name: "ix_knowledgebase_documents_contenttype",
                schema: "dbo",
                table: "KnowledgeBaseDocuments");
        }
    }
}
