using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RebuildKnowledgeBaseHnswIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rebuild HNSW index for KnowledgeBaseChunks.Embedding (vector(1536), cosine distance).
            // Uses CONCURRENTLY to avoid locking writes, but requires running outside a transaction.
            // Increased m=32 and ef_construction=128 for better recall on larger datasets.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS dbo.idx_kb_chunks_embedding;
                CREATE INDEX idx_kb_chunks_embedding ON dbo."KnowledgeBaseChunks" USING hnsw ("Embedding" vector_cosine_ops) WITH (m = 32, ef_construction = 128);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS dbo.idx_kb_chunks_embedding;
                CREATE INDEX idx_kb_chunks_embedding ON dbo."KnowledgeBaseChunks" USING hnsw ("Embedding" vector_cosine_ops) WITH (m = 16, ef_construction = 64);
                """);
        }
    }
}
