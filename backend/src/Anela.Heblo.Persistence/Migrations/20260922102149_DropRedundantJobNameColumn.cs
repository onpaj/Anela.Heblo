using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRedundantJobNameColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecurringJobConfigurations_JobName",
                schema: "public",
                table: "RecurringJobConfigurations");

            migrationBuilder.DropColumn(
                name: "JobName",
                schema: "public",
                table: "RecurringJobConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobName",
                schema: "public",
                table: "RecurringJobConfigurations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJobConfigurations_JobName",
                schema: "public",
                table: "RecurringJobConfigurations",
                column: "JobName",
                unique: true);
        }
    }
}
