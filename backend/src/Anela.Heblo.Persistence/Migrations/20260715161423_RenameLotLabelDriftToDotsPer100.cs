using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameLotLabelDriftToDotsPer100 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename in place (preserves stored values) rather than drop/add.
            migrationBuilder.RenameColumn(
                name: "DriftDotsPer10Labels",
                schema: "public",
                table: "LotLabelCalibrations",
                newName: "DriftDotsPer100Labels");

            migrationBuilder.AlterColumn<int>(
                name: "DriftDotsPer100Labels",
                schema: "public",
                table: "LotLabelCalibrations",
                type: "integer",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3);

            // Preserve the physical drift: a value stored as dots-per-10-labels equals
            // ten times as many dots-per-100-labels.
            migrationBuilder.Sql(
                "UPDATE public.\"LotLabelCalibrations\" SET \"DriftDotsPer100Labels\" = \"DriftDotsPer100Labels\" * 10;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE public.\"LotLabelCalibrations\" SET \"DriftDotsPer100Labels\" = \"DriftDotsPer100Labels\" / 10;");

            migrationBuilder.AlterColumn<int>(
                name: "DriftDotsPer100Labels",
                schema: "public",
                table: "LotLabelCalibrations",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 30);

            migrationBuilder.RenameColumn(
                name: "DriftDotsPer100Labels",
                schema: "public",
                table: "LotLabelCalibrations",
                newName: "DriftDotsPer10Labels");
        }
    }
}
