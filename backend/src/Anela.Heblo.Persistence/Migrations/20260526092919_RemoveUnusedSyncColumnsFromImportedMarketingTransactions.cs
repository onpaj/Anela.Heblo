using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedSyncColumnsFromImportedMarketingTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                schema: "public",
                table: "ImportedMarketingTransactions");

            migrationBuilder.DropColumn(
                name: "IsSynced",
                schema: "public",
                table: "ImportedMarketingTransactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                schema: "public",
                table: "ImportedMarketingTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSynced",
                schema: "public",
                table: "ImportedMarketingTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
