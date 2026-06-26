using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceMaterialContainerLotIdWithStrings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaterialCode",
                schema: "public",
                table: "MaterialContainers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotCode",
                schema: "public",
                table: "MaterialContainers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE public.""MaterialContainers"" mc
                SET ""MaterialCode"" = l.""MaterialCode"", ""LotCode"" = l.""LotCode""
                FROM public.""Lots"" l
                WHERE mc.""LotId"" = l.""Id"";
            ");

            migrationBuilder.AlterColumn<string>(
                name: "MaterialCode",
                schema: "public",
                table: "MaterialContainers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LotCode",
                schema: "public",
                table: "MaterialContainers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialContainers_Lots_LotId",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.DropIndex(
                name: "IX_MaterialContainers_LotId",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.DropColumn(
                name: "LotId",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialContainers_MaterialCode_LotCode",
                schema: "public",
                table: "MaterialContainers",
                columns: new[] { "MaterialCode", "LotCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialContainers_MaterialCode_CreatedAt",
                schema: "public",
                table: "MaterialContainers",
                columns: new[] { "MaterialCode", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaterialContainers_MaterialCode_CreatedAt",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.DropIndex(
                name: "IX_MaterialContainers_MaterialCode_LotCode",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.AddColumn<int>(
                name: "LotId",
                schema: "public",
                table: "MaterialContainers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE public.""MaterialContainers"" mc
                SET ""LotId"" = l.""Id""
                FROM public.""Lots"" l
                WHERE mc.""MaterialCode"" = l.""MaterialCode"" AND mc.""LotCode"" = l.""LotCode"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialContainers_LotId",
                schema: "public",
                table: "MaterialContainers",
                column: "LotId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialContainers_Lots_LotId",
                schema: "public",
                table: "MaterialContainers",
                column: "LotId",
                principalSchema: "public",
                principalTable: "Lots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "LotCode",
                schema: "public",
                table: "MaterialContainers");

            migrationBuilder.DropColumn(
                name: "MaterialCode",
                schema: "public",
                table: "MaterialContainers");
        }
    }
}
