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
                name: "BankStatements",
                schema: "public",
                newName: "BankStatements",
                newSchema: "dbo");
        }
    }
}
