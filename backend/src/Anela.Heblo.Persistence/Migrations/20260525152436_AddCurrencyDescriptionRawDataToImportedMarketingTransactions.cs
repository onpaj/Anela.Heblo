using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyDescriptionRawDataToImportedMarketingTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Currency: add as nullable so backfill can populate existing rows.
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "public",
                table: "ImportedMarketingTransactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            // Backfill existing rows. All historical ImportedMarketingTransactions rows
            // are CZK-billed (verify with SELECT DISTINCT "Platform" on prod before deploy).
            migrationBuilder.Sql(
                "UPDATE public.\"ImportedMarketingTransactions\" SET \"Currency\" = 'CZK' WHERE \"Currency\" IS NULL;");

            // Enforce NOT NULL. New inserts must supply Currency explicitly.
            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                schema: "public",
                table: "ImportedMarketingTransactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);

            // Description: nullable, no backfill needed.
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "public",
                table: "ImportedMarketingTransactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // RawData: nullable, no backfill needed.
            migrationBuilder.AddColumn<string>(
                name: "RawData",
                schema: "public",
                table: "ImportedMarketingTransactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawData",
                schema: "public",
                table: "ImportedMarketingTransactions");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "public",
                table: "ImportedMarketingTransactions");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "public",
                table: "ImportedMarketingTransactions");
        }
    }
}
