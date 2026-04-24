using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveDboTablesToPublicSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "UserDashboardTiles",
                newName: "UserDashboardTiles",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "UserDashboardSettings",
                newName: "UserDashboardSettings",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "StockTakingResults",
                schema: "dbo",
                newName: "StockTakingResults",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "recurring_job_configurations",
                newName: "recurring_job_configurations",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterialLog",
                schema: "dbo",
                newName: "PackingMaterialLog",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "PackingMaterial",
                schema: "dbo",
                newName: "PackingMaterial",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "ManufactureDifficultySettings",
                newName: "ManufactureDifficultySettings",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "KnowledgeBaseQuestionLogs",
                schema: "dbo",
                newName: "KnowledgeBaseQuestionLogs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "KnowledgeBaseDocuments",
                schema: "dbo",
                newName: "KnowledgeBaseDocuments",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "KnowledgeBaseChunks",
                schema: "dbo",
                newName: "KnowledgeBaseChunks",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "IssuedInvoiceSyncData",
                schema: "dbo",
                newName: "IssuedInvoiceSyncData",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "IssuedInvoice",
                schema: "dbo",
                newName: "IssuedInvoice",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "invoice_dqt_results",
                newName: "invoice_dqt_results",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "imported_marketing_transactions",
                schema: "dbo",
                newName: "imported_marketing_transactions",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "GridLayouts",
                newName: "GridLayouts",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "gift_package_manufacture_logs",
                newName: "gift_package_manufacture_logs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "gift_package_manufacture_items",
                newName: "gift_package_manufacture_items",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "dqt_runs",
                newName: "dqt_runs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "ClassificationRules",
                newName: "ClassificationRules",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "ClassificationHistory",
                newName: "ClassificationHistory",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "BankStatements",
                schema: "dbo",
                newName: "BankStatements",
                newSchema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.RenameTable(
                name: "UserDashboardTiles",
                schema: "public",
                newName: "UserDashboardTiles");

            migrationBuilder.RenameTable(
                name: "UserDashboardSettings",
                schema: "public",
                newName: "UserDashboardSettings");

            migrationBuilder.RenameTable(
                name: "StockTakingResults",
                schema: "public",
                newName: "StockTakingResults",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "recurring_job_configurations",
                schema: "public",
                newName: "recurring_job_configurations");

            migrationBuilder.RenameTable(
                name: "PackingMaterialLog",
                schema: "public",
                newName: "PackingMaterialLog",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "PackingMaterial",
                schema: "public",
                newName: "PackingMaterial",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "ManufactureDifficultySettings",
                schema: "public",
                newName: "ManufactureDifficultySettings");

            migrationBuilder.RenameTable(
                name: "KnowledgeBaseQuestionLogs",
                schema: "public",
                newName: "KnowledgeBaseQuestionLogs",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "KnowledgeBaseDocuments",
                schema: "public",
                newName: "KnowledgeBaseDocuments",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "KnowledgeBaseChunks",
                schema: "public",
                newName: "KnowledgeBaseChunks",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "IssuedInvoiceSyncData",
                schema: "public",
                newName: "IssuedInvoiceSyncData",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "IssuedInvoice",
                schema: "public",
                newName: "IssuedInvoice",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "invoice_dqt_results",
                schema: "public",
                newName: "invoice_dqt_results");

            migrationBuilder.RenameTable(
                name: "imported_marketing_transactions",
                schema: "public",
                newName: "imported_marketing_transactions",
                newSchema: "dbo");

            migrationBuilder.RenameTable(
                name: "GridLayouts",
                schema: "public",
                newName: "GridLayouts");

            migrationBuilder.RenameTable(
                name: "gift_package_manufacture_logs",
                schema: "public",
                newName: "gift_package_manufacture_logs");

            migrationBuilder.RenameTable(
                name: "gift_package_manufacture_items",
                schema: "public",
                newName: "gift_package_manufacture_items");

            migrationBuilder.RenameTable(
                name: "dqt_runs",
                schema: "public",
                newName: "dqt_runs");

            migrationBuilder.RenameTable(
                name: "ClassificationRules",
                schema: "public",
                newName: "ClassificationRules");

            migrationBuilder.RenameTable(
                name: "ClassificationHistory",
                schema: "public",
                newName: "ClassificationHistory");

            migrationBuilder.RenameTable(
                name: "BankStatements",
                schema: "public",
                newName: "BankStatements",
                newSchema: "dbo");
        }
    }
}
