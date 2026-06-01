using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialContainerPurchaseOrderLineId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderLineId",
                schema: "public",
                table: "MaterialContainers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialContainers_PurchaseOrderLineId",
                schema: "public",
                table: "MaterialContainers",
                column: "PurchaseOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialContainers_PurchaseOrderLines_PurchaseOrderLineId",
                schema: "public",
                table: "MaterialContainers",
                column: "PurchaseOrderLineId",
                principalSchema: "public",
                principalTable: "PurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaterialContainers_PurchaseOrderLines_PurchaseOrderLineId",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.DropIndex(
                name: "IX_MaterialContainers_PurchaseOrderLineId",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderLineId",
                schema: "public",
                table: "MaterialContainers");
        }
    }
}
