using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReformatManufactureOrderLotToWwYy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reformat existing manufacture-order lots from the old wwyyyyMM format
            // (week + 4-digit year + month, 8 digits) to the new wwyy format
            // (week + 2-digit year). Keep the week (chars 1-2) and the last two digits
            // of the year (chars 5-6), dropping the century and the month.
            // Only rows matching the old 8-digit numeric pattern are converted;
            // manually entered / non-conforming lots are left untouched.
            migrationBuilder.Sql(
                "UPDATE public.\"ManufactureOrderSemiProducts\" " +
                "SET \"LotNumber\" = substring(\"LotNumber\" from 1 for 2) || substring(\"LotNumber\" from 5 for 2) " +
                "WHERE \"LotNumber\" ~ '^\\d{8}$';");

            migrationBuilder.Sql(
                "UPDATE public.\"ManufactureOrderProducts\" " +
                "SET \"LotNumber\" = substring(\"LotNumber\" from 1 for 2) || substring(\"LotNumber\" from 5 for 2) " +
                "WHERE \"LotNumber\" ~ '^\\d{8}$';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the wwyyyyMM -> wwyy transform is lossy
            // (month and century are discarded) and cannot be reliably reversed.
        }
    }
}
