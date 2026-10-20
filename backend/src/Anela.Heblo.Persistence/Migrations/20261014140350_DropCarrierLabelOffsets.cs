using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropCarrierLabelOffsets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierLabelOffsets",
                schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierLabelOffsets",
                schema: "public",
                columns: table => new
                {
                    Carrier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OffsetXMm = table.Column<int>(type: "integer", nullable: false),
                    OffsetYMm = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierLabelOffsets", x => x.Carrier);
                });
        }
    }
}
