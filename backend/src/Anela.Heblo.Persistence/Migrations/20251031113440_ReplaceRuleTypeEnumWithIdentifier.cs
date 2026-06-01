using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRuleTypeEnumWithIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassificationRules_Type_Pattern",
                table: "ClassificationRules");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "ClassificationRules");

            migrationBuilder.AddColumn<string>(
                name: "RuleTypeIdentifier",
                table: "ClassificationRules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRules_RuleTypeIdentifier_Pattern",
                table: "ClassificationRules",
                columns: new[] { "RuleTypeIdentifier", "Pattern" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassificationRules_RuleTypeIdentifier_Pattern",
                table: "ClassificationRules");

            migrationBuilder.DropColumn(
                name: "RuleTypeIdentifier",
                table: "ClassificationRules");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "ClassificationRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRules_Type_Pattern",
                table: "ClassificationRules",
                columns: new[] { "Type", "Pattern" });
        }
    }
}
