using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingUserSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EntraObjectId",
                schema: "public",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "EntraObjectId",
                schema: "public",
                table: "AppUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<bool>(
                name: "CanPack",
                schema: "public",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "public",
                table: "AppUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Entra");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EntraObjectId",
                schema: "public",
                table: "AppUsers",
                column: "EntraObjectId",
                unique: true,
                filter: "\"EntraObjectId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EntraObjectId",
                schema: "public",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "CanPack",
                schema: "public",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "public",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "EntraObjectId",
                schema: "public",
                table: "AppUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EntraObjectId",
                schema: "public",
                table: "AppUsers",
                column: "EntraObjectId",
                unique: true);
        }
    }
}
