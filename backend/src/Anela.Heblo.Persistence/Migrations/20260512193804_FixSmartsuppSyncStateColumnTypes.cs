using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixSmartsuppSyncStateColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "SmartsuppSyncState",
                newName: "SmartsuppSyncState",
                newSchema: "public");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastUpdatedAtSeen",
                schema: "public",
                table: "SmartsuppSyncState",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastSyncStartedAt",
                schema: "public",
                table: "SmartsuppSyncState",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.Sql("""
                INSERT INTO public."SmartsuppSyncState" ("Id", "LastSyncStartedAt", "LastUpdatedAtSeen")
                VALUES (1, TIMESTAMP '2024-01-01 00:00:00', NULL)
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartsuppSyncState_SingleRow",
                schema: "public",
                table: "SmartsuppSyncState",
                sql: "\"Id\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartsuppSyncState_SingleRow",
                schema: "public",
                table: "SmartsuppSyncState");

            migrationBuilder.DeleteData(
                schema: "public",
                table: "SmartsuppSyncState",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.RenameTable(
                name: "SmartsuppSyncState",
                schema: "public",
                newName: "SmartsuppSyncState");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastUpdatedAtSeen",
                table: "SmartsuppSyncState",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastSyncStartedAt",
                table: "SmartsuppSyncState",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");
        }
    }
}
