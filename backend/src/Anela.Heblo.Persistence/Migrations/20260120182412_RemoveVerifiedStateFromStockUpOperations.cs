using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveVerifiedStateFromStockUpOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Update existing Verified records (state = 2) to old Completed value (state = 3)
            migrationBuilder.Sql(@"
                UPDATE ""StockUpOperations""
                SET ""State"" = 3
                WHERE ""State"" = 2;
            ");

            // Step 2: Update state values to new enum (Completed = 2, Failed = 3)
            migrationBuilder.Sql(@"
                UPDATE ""StockUpOperations""
                SET ""State"" = 2
                WHERE ""State"" = 3;

                UPDATE ""StockUpOperations""
                SET ""State"" = 3
                WHERE ""State"" = 4;
            ");

            // Step 3: Remove VerifiedAt column
            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                schema: "public",
                table: "StockUpOperations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add VerifiedAt column back
            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                schema: "public",
                table: "StockUpOperations",
                type: "timestamp",
                nullable: true);

            // Step 2: Restore old state values
            migrationBuilder.Sql(@"
                UPDATE ""StockUpOperations""
                SET ""State"" = 4
                WHERE ""State"" = 3;

                UPDATE ""StockUpOperations""
                SET ""State"" = 3
                WHERE ""State"" = 2;
            ");
        }
    }
}
