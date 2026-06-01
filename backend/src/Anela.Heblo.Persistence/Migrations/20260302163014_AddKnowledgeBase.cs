using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseDocuments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseChunks",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseChunks_KnowledgeBaseDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "dbo",
                        principalTable: "KnowledgeBaseDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Add vector column manually (EF Core does not support vector(1536) type mapping with Npgsql 8.x)
            migrationBuilder.Sql(
                """
                ALTER TABLE dbo."KnowledgeBaseChunks" ADD COLUMN "Embedding" vector(1536);
                CREATE INDEX idx_kb_chunks_embedding ON dbo."KnowledgeBaseChunks" USING hnsw ("Embedding" vector_cosine_ops) WITH (m = 16, ef_construction = 64);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseChunks_DocumentId",
                schema: "dbo",
                table: "KnowledgeBaseChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_SourcePath",
                schema: "dbo",
                table: "KnowledgeBaseDocuments",
                column: "SourcePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseDocuments_Status",
                schema: "dbo",
                table: "KnowledgeBaseDocuments",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeBaseChunks",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseDocuments",
                schema: "dbo");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
