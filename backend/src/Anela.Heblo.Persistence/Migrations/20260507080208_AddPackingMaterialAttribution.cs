using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingMaterialAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackingMaterialAllocations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackingMaterialId = table.Column<int>(type: "integer", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AmountPerUnit = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingMaterialAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingMaterialAllocations_PackingMaterials_PackingMaterial~",
                        column: x => x.PackingMaterialId,
                        principalSchema: "public",
                        principalTable: "PackingMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackingMaterialConsumptions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackingMaterialId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ConsumptionType = table.Column<int>(type: "integer", nullable: false),
                    InvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductQuantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingMaterialConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingMaterialConsumptions_PackingMaterials_PackingMateria~",
                        column: x => x.PackingMaterialId,
                        principalSchema: "public",
                        principalTable: "PackingMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialAllocations_MaterialId_ProductCode",
                schema: "public",
                table: "PackingMaterialAllocations",
                columns: new[] { "PackingMaterialId", "ProductCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialConsumptions_Date_InvoiceId",
                schema: "public",
                table: "PackingMaterialConsumptions",
                columns: new[] { "Date", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialConsumptions_Date_MaterialId",
                schema: "public",
                table: "PackingMaterialConsumptions",
                columns: new[] { "Date", "PackingMaterialId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialConsumptions_Date_ProductCode",
                schema: "public",
                table: "PackingMaterialConsumptions",
                columns: new[] { "Date", "ProductCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingMaterialConsumptions_PackingMaterialId",
                schema: "public",
                table: "PackingMaterialConsumptions",
                column: "PackingMaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackingMaterialAllocations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PackingMaterialConsumptions",
                schema: "public");
        }
    }
}
