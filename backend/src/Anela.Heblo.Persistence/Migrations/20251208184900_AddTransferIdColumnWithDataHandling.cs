using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferIdColumnWithDataHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "CreationTime",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "ExtraProperties",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                schema: "dbo",
                table: "BankStatements");

            // First add the column as nullable
            migrationBuilder.AddColumn<string>(
                name: "TransferId",
                schema: "dbo",
                table: "BankStatements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Update existing records with unique TransferId values based on Id
            migrationBuilder.Sql("UPDATE dbo.\"BankStatements\" SET \"TransferId\" = 'migration-transfer-' || \"Id\" WHERE \"TransferId\" IS NULL;");

            // Now make the column non-nullable
            migrationBuilder.AlterColumn<string>(
                name: "TransferId",
                schema: "dbo",
                table: "BankStatements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_TransferId",
                schema: "dbo",
                table: "BankStatements",
                column: "TransferId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankStatements_TransferId",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "TransferId",
                schema: "dbo",
                table: "BankStatements");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                schema: "dbo",
                table: "BankStatements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTime",
                schema: "dbo",
                table: "BankStatements",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatorId",
                schema: "dbo",
                table: "BankStatements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraProperties",
                schema: "dbo",
                table: "BankStatements",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                schema: "dbo",
                table: "BankStatements",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModifierId",
                schema: "dbo",
                table: "BankStatements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
