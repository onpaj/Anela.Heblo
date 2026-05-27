using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductIngredientOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductIngredientOrders",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParentProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IngredientProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductIngredientOrders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductIngredientOrders_ParentProductCode",
                schema: "public",
                table: "ProductIngredientOrders",
                column: "ParentProductCode");

            migrationBuilder.CreateIndex(
                name: "UX_ProductIngredientOrders_Parent_Ingredient",
                schema: "public",
                table: "ProductIngredientOrders",
                columns: new[] { "ParentProductCode", "IngredientProductCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductIngredientOrders",
                schema: "public");
        }
    }
}
