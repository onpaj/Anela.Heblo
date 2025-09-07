using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixTransportBoxExtraProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, update existing NULL values in ExtraProperties with '{}'
            migrationBuilder.Sql(@"UPDATE public.""TransportBox"" SET ""ExtraProperties"" = '{}' WHERE ""ExtraProperties"" IS NULL");

            // Then alter the column to be NOT NULL with '{}' default
            migrationBuilder.AlterColumn<string>(
                name: "ExtraProperties",
                schema: "public",
                table: "TransportBox",
                type: "text",
                nullable: false,
                defaultValueSql: "'{}'",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // Update ConcurrencyStamp default value to use UUID generator
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
                oldDefaultValueSql: "gen_random_uuid()::text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExtraProperties",
                schema: "public",
                table: "TransportBox",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'{}'");

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
                oldDefaultValueSql: "gen_random_uuid()::text");
        }
    }
}
