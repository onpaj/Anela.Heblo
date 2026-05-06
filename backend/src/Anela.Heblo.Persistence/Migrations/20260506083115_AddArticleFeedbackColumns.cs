using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleFeedbackColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedbackComment",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrecisionScore",
                table: "Articles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StyleScore",
                table: "Articles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Articles_PrecisionScore",
                table: "Articles",
                column: "PrecisionScore",
                filter: "\"PrecisionScore\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_PrecisionScore",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "FeedbackComment",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "PrecisionScore",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "StyleScore",
                table: "Articles");
        }
    }
}
