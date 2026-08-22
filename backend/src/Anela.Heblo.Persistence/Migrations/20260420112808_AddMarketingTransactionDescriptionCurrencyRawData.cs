using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingTransactionDescriptionCurrencyRawData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "dbo",
                table: "imported_marketing_transactions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "dbo",
                table: "imported_marketing_transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawData",
                schema: "dbo",
                table: "imported_marketing_transactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "dbo",
                table: "imported_marketing_transactions");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "dbo",
                table: "imported_marketing_transactions");

            migrationBuilder.DropColumn(
                name: "RawData",
                schema: "dbo",
                table: "imported_marketing_transactions");
        }
    }
}
