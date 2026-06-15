using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankImportState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankImportStates",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", nullable: false),
                    Account = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastValidImportDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastRunStartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastRunFinishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastRunStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConsecutiveFailureCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankImportStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankImportStates_Account",
                schema: "public",
                table: "BankImportStates",
                column: "Account",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankImportStates",
                schema: "public");
        }
    }
}
