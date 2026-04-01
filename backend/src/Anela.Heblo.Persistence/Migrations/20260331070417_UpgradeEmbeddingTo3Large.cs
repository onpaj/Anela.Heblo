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
            // Switch to text-embedding-3-large model. Using 1536 dims (truncated from 3072) — pgvector HNSW index limit is 2000 dimensions.
            // Recreates HNSW index to ensure clean state after model switch.
            // HNSW index must be dropped and recreated because it is dimension-bound.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS dbo.idx_kb_chunks_embedding;
                ALTER TABLE dbo."KnowledgeBaseChunks" ALTER COLUMN "Embedding" TYPE vector(1536);
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
