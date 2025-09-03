using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixTransportBoxDateTimeColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufactureDifficultyHistory");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StateDate",
                schema: "public",
                table: "TransportBoxStateLog",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DateAdded",
                schema: "public",
                table: "TransportBoxItem",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastStateChanged",
                schema: "public",
                table: "TransportBox",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ManufactureDifficultySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DifficultyValue = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureDifficultySettings", x => x.Id);
                    table.CheckConstraint("CK_ManufactureDifficultySettings_ValidDates", "\"ValidFrom\" IS NULL OR \"ValidTo\" IS NULL OR \"ValidFrom\" < \"ValidTo\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureDifficultySettings_ProductCode",
                table: "ManufactureDifficultySettings",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureDifficultySettings_ProductCode_Validity",
                table: "ManufactureDifficultySettings",
                columns: new[] { "ProductCode", "ValidFrom", "ValidTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufactureDifficultySettings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StateDate",
                schema: "public",
                table: "TransportBoxStateLog",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DateAdded",
                schema: "public",
                table: "TransportBoxItem",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastStateChanged",
                schema: "public",
                table: "TransportBox",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ManufactureDifficultyHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DifficultyValue = table.Column<int>(type: "integer", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureDifficultyHistory", x => x.Id);
                    table.CheckConstraint("CK_ManufactureDifficultyHistory_ValidDates", "\"ValidFrom\" IS NULL OR \"ValidTo\" IS NULL OR \"ValidFrom\" < \"ValidTo\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureDifficultyHistory_ProductCode",
                table: "ManufactureDifficultyHistory",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureDifficultyHistory_ProductCode_Validity",
                table: "ManufactureDifficultyHistory",
                columns: new[] { "ProductCode", "ValidFrom", "ValidTo" });
        }
    }
}
