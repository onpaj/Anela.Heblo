using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeEmbeddingTo3Large : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Upgrade embedding column from vector(1536) to vector(3072) for text-embedding-3-large model.
            // HNSW index must be dropped and recreated because it is dimension-bound.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS dbo.idx_kb_chunks_embedding;
                ALTER TABLE dbo."KnowledgeBaseChunks" ALTER COLUMN "Embedding" TYPE vector(3072);
                CREATE INDEX idx_kb_chunks_embedding ON dbo."KnowledgeBaseChunks" USING hnsw ("Embedding" vector_cosine_ops) WITH (m = 16, ef_construction = 64);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS dbo.idx_kb_chunks_embedding;
                ALTER TABLE dbo."KnowledgeBaseChunks" ALTER COLUMN "Embedding" TYPE vector(1536);
                CREATE INDEX idx_kb_chunks_embedding ON dbo."KnowledgeBaseChunks" USING hnsw ("Embedding" vector_cosine_ops) WITH (m = 16, ef_construction = 64);
                """);
        }
    }
}
