using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseFeedbackColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedbackComment",
                schema: "dbo",
                table: "KnowledgeBaseQuestionLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrecisionScore",
                schema: "dbo",
                table: "KnowledgeBaseQuestionLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StyleScore",
                schema: "dbo",
                table: "KnowledgeBaseQuestionLogs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedbackComment",
                schema: "dbo",
                table: "KnowledgeBaseQuestionLogs");

            migrationBuilder.DropColumn(
                name: "PrecisionScore",
                schema: "dbo",
                table: "KnowledgeBaseQuestionLogs");

            migrationBuilder.DropColumn(
                name: "StyleScore",
                schema: "dbo",
                table: "KnowledgeBaseQuestionLogs");
        }
    }
}
