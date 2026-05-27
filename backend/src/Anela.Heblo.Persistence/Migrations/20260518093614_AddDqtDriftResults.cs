using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDqtDriftResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DqtDriftResults",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DqtRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestType = table.Column<int>(type: "integer", nullable: false),
                    EntityKey = table.Column<string>(type: "text", nullable: false),
                    MismatchCode = table.Column<int>(type: "integer", nullable: false),
                    HebloValue = table.Column<string>(type: "text", nullable: true),
                    ShoptetValue = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DqtDriftResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DqtDriftResults_DqtRunId",
                schema: "public",
                table: "DqtDriftResults",
                column: "DqtRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DqtDriftResults_TestType_EntityKey",
                schema: "public",
                table: "DqtDriftResults",
                columns: new[] { "TestType", "EntityKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DqtDriftResults",
                schema: "public");
        }
    }
}
