using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingTransactionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_imported_marketing_transactions_ImportedAt",
                schema: "dbo",
                table: "imported_marketing_transactions",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_imported_marketing_transactions_IsSynced_False",
                schema: "dbo",
                table: "imported_marketing_transactions",
                column: "IsSynced",
                filter: "\"IsSynced\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_imported_marketing_transactions_ImportedAt",
                schema: "dbo",
                table: "imported_marketing_transactions");

            migrationBuilder.DropIndex(
                name: "IX_imported_marketing_transactions_IsSynced_False",
                schema: "dbo",
                table: "imported_marketing_transactions");
        }
    }
}
