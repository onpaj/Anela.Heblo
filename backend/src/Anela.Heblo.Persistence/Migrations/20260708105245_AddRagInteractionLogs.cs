using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRagInteractionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RagInteractionLogs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Feature = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Question = table.Column<string>(type: "text", nullable: false),
                    ExpandedQuery = table.Column<string>(type: "text", nullable: true),
                    TopK = table.Column<int>(type: "integer", nullable: false),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    RetrievedChunksJson = table.Column<string>(type: "jsonb", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<string>(type: "text", nullable: true),
                    Topic = table.Column<string>(type: "text", nullable: true),
                    SentAnswer = table.Column<string>(type: "text", nullable: true),
                    WasEdited = table.Column<bool>(type: "boolean", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrecisionScore = table.Column<int>(type: "integer", nullable: true),
                    StyleScore = table.Column<int>(type: "integer", nullable: true),
                    FeedbackComment = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagInteractionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RagInteractionLogs_ConversationId",
                schema: "public",
                table: "RagInteractionLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_RagInteractionLogs_Feature_CreatedAt",
                schema: "public",
                table: "RagInteractionLogs",
                columns: new[] { "Feature", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RagInteractionLogs_UserId",
                schema: "public",
                table: "RagInteractionLogs",
                column: "UserId");

            // Migrate existing KnowledgeBase question logs (incl. feedback) into the unified table.
            // Rich RAG columns (chunks, expanded query, rendered prompt, sent-answer) are unavailable
            // for these historical rows, so they get empty/NULL defaults. Feature = 0 = KnowledgeBase.
            migrationBuilder.Sql(
                """
                INSERT INTO public."RagInteractionLogs"
                    ("Id", "Feature", "CreatedAt", "UserId", "Question", "ExpandedQuery", "TopK",
                     "SourceCount", "RetrievedChunksJson", "SystemPrompt", "Answer", "ConversationId",
                     "Topic", "SentAnswer", "WasEdited", "SentAt", "PrecisionScore", "StyleScore",
                     "FeedbackComment", "DurationMs")
                SELECT
                    "Id", 0, "CreatedAt", "UserId", "Question", NULL, "TopK",
                    "SourceCount", '[]', '', "Answer", NULL,
                    NULL, NULL, NULL, NULL, "PrecisionScore", "StyleScore",
                    "FeedbackComment", "DurationMs"
                FROM public."KnowledgeBaseQuestionLogs";
                """);

            migrationBuilder.DropTable(
                name: "KnowledgeBaseQuestionLogs",
                schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeBaseQuestionLogs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    FeedbackComment = table.Column<string>(type: "text", nullable: true),
                    PrecisionScore = table.Column<int>(type: "integer", nullable: true),
                    Question = table.Column<string>(type: "text", nullable: false),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    StyleScore = table.Column<int>(type: "integer", nullable: true),
                    TopK = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseQuestionLogs", x => x.Id);
                });

            // Restore KnowledgeBase rows from the unified table before dropping it.
            migrationBuilder.Sql(
                """
                INSERT INTO public."KnowledgeBaseQuestionLogs"
                    ("Id", "Answer", "CreatedAt", "DurationMs", "FeedbackComment", "PrecisionScore",
                     "Question", "SourceCount", "StyleScore", "TopK", "UserId")
                SELECT
                    "Id", "Answer", "CreatedAt", "DurationMs", "FeedbackComment", "PrecisionScore",
                    "Question", "SourceCount", "StyleScore", "TopK", "UserId"
                FROM public."RagInteractionLogs"
                WHERE "Feature" = 0;
                """);

            migrationBuilder.DropTable(
                name: "RagInteractionLogs",
                schema: "public");
        }
    }
}
