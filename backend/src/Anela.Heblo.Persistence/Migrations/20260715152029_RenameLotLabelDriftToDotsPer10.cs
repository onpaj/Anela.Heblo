using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameLotLabelDriftToDotsPer10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DriftEveryNLabels",
                schema: "public",
                table: "LotLabelCalibrations",
                newName: "DriftDotsPer10Labels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DriftDotsPer10Labels",
                schema: "public",
                table: "LotLabelCalibrations",
                newName: "DriftEveryNLabels");
        }
    }
}
