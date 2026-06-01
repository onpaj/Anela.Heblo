using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftPackageManufacturing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gift_package_manufacture_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    gift_package_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity_created = table.Column<int>(type: "integer", nullable: false),
                    stock_override_applied = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gift_package_manufacture_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gift_package_manufacture_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    manufacture_log_id = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity_consumed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gift_package_manufacture_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_gift_package_manufacture_items_gift_package_manufacture_log~",
                        column: x => x.manufacture_log_id,
                        principalTable: "gift_package_manufacture_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gift_package_manufacture_items_manufacture_log_id",
                table: "gift_package_manufacture_items",
                column: "manufacture_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_gift_package_manufacture_items_product_code",
                table: "gift_package_manufacture_items",
                column: "product_code");

            migrationBuilder.CreateIndex(
                name: "ix_gift_package_manufacture_logs_created_at",
                table: "gift_package_manufacture_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_gift_package_manufacture_logs_gift_package_code",
                table: "gift_package_manufacture_logs",
                column: "gift_package_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_package_manufacture_items");

            migrationBuilder.DropTable(
                name: "gift_package_manufacture_logs");
        }
    }
}
