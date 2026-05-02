using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeafletStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeafletDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Filename = table.Column<string>(type: "text", nullable: false),
                    SourcePath = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeafletDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeafletChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeafletChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeafletChunks_LeafletDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "LeafletDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeafletChunks_DocumentId",
                table: "LeafletChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LeafletDocuments_ContentHash",
                table: "LeafletDocuments",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_LeafletDocuments_SourcePath",
                table: "LeafletDocuments",
                column: "SourcePath",
                unique: true);

            migrationBuilder.Sql("ALTER TABLE \"LeafletChunks\" ADD COLUMN \"Embedding\" vector(1536) NOT NULL;");
            migrationBuilder.Sql("CREATE INDEX IX_LeafletChunks_Embedding_HNSW ON \"LeafletChunks\" USING hnsw (\"Embedding\" vector_cosine_ops) WITH (m = 16, ef_construction = 64);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LeafletChunks_Embedding_HNSW"";");

            migrationBuilder.DropTable(
                name: "LeafletChunks");

            migrationBuilder.DropTable(
                name: "LeafletDocuments");
        }
    }
}
