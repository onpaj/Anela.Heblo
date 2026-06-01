using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SetActualQuantityFromPlanned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update ActualQuantity to PlannedQuantity where ActualQuantity is 0 in ManufactureOrderProducts
            migrationBuilder.Sql(@"
                UPDATE ""ManufactureOrderProducts""
                SET ""ActualQuantity"" = ""PlannedQuantity""
                WHERE ""ActualQuantity"" = 0;
            ");

            // Update ActualQuantity to PlannedQuantity where ActualQuantity is 0 in ManufactureOrderSemiProducts
            migrationBuilder.Sql(@"
                UPDATE ""ManufactureOrderSemiProducts""
                SET ""ActualQuantity"" = ""PlannedQuantity""
                WHERE ""ActualQuantity"" = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration only updates data, no schema changes to rollback
            // Rolling back data updates is not recommended as it would lose information
            // about which records originally had ActualQuantity = 0
        }
    }
}
