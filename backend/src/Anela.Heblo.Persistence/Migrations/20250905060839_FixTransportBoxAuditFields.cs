using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixTransportBoxAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, update existing NULL values in ConcurrencyStamp with a generated GUID
            migrationBuilder.Sql(@"UPDATE public.""TransportBox"" SET ""ConcurrencyStamp"" = gen_random_uuid()::text WHERE ""ConcurrencyStamp"" IS NULL");

            // Then alter the column to be NOT NULL with empty string default
            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                schema: "public",
                table: "TransportBox",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationTime",
                schema: "public",
                table: "TransportBox",
                type: "timestamp without time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationTime",
                schema: "public",
                table: "TransportBox",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                schema: "public",
                table: "TransportBox",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValueSql: "gen_random_uuid()::text");
        }
    }
}
