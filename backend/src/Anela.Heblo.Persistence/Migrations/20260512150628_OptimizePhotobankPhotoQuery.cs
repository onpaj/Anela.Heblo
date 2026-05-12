using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizePhotobankPhotoQuery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhotoTags_TagId",
                schema: "public",
                table: "PhotoTags");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoTags_TagId_PhotoId",
                schema: "public",
                table: "PhotoTags",
                columns: new[] { "TagId", "PhotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ModifiedAt_Id",
                schema: "public",
                table: "Photos",
                columns: new[] { "ModifiedAt", "Id" },
                descending: new[] { true, true });

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Photos_PathTrgm"
                    ON public."Photos"
                    USING GIN (LOWER("FolderPath" || '/' || "FileName") gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS public.\"IX_Photos_PathTrgm\";");

            migrationBuilder.DropIndex(
                name: "IX_PhotoTags_TagId_PhotoId",
                schema: "public",
                table: "PhotoTags");

            migrationBuilder.DropIndex(
                name: "IX_Photos_ModifiedAt_Id",
                schema: "public",
                table: "Photos");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoTags_TagId",
                schema: "public",
                table: "PhotoTags",
                column: "TagId");
        }
    }
}
