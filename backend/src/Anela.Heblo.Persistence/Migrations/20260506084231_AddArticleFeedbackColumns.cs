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
            migrationBuilder.AlterColumn<string>(
                name: "ValidationNote",
                table: "ArticleSources",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "ArticleSources",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StyleGuideItemPath",
                table: "Articles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StyleGuideDriveId",
                table: "Articles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

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

            migrationBuilder.AlterColumn<string>(
                name: "ValidationNote",
                table: "ArticleSources",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "ArticleSources",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StyleGuideItemPath",
                table: "Articles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StyleGuideDriveId",
                table: "Articles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
