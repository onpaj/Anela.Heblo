using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRagInteractionLogTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ProblemTags",
                schema: "public",
                table: "RagInteractionLogs",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "ProductTags",
                schema: "public",
                table: "RagInteractionLogs",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "RecommendedProductCodes",
                schema: "public",
                table: "RagInteractionLogs",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateIndex(
                name: "IX_RagInteractionLogs_ProblemTags",
                schema: "public",
                table: "RagInteractionLogs",
                column: "ProblemTags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_RagInteractionLogs_ProductTags",
                schema: "public",
                table: "RagInteractionLogs",
                column: "ProductTags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RagInteractionLogs_ProblemTags",
                schema: "public",
                table: "RagInteractionLogs");

            migrationBuilder.DropIndex(
                name: "IX_RagInteractionLogs_ProductTags",
                schema: "public",
                table: "RagInteractionLogs");

            migrationBuilder.DropColumn(
                name: "ProblemTags",
                schema: "public",
                table: "RagInteractionLogs");

            migrationBuilder.DropColumn(
                name: "ProductTags",
                schema: "public",
                table: "RagInteractionLogs");

            migrationBuilder.DropColumn(
                name: "RecommendedProductCodes",
                schema: "public",
                table: "RagInteractionLogs");
        }
    }
}
