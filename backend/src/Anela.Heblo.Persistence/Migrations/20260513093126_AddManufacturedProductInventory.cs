using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturedProductInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                schema: "public",
                table: "TransportBoxItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                schema: "public",
                table: "TransportBoxItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceInventoryId",
                schema: "public",
                table: "TransportBoxItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManufacturedProductInventoryItems",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturedProductInventoryItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturedProductInventoryLogs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryItemId = table.Column<int>(type: "integer", nullable: false),
                    ChangeType = table.Column<int>(type: "integer", nullable: false),
                    AmountDelta = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AmountAfter = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    User = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturedProductInventoryLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturedProductInventoryLogs_ManufacturedProductInvento~",
                        column: x => x.InventoryItemId,
                        principalSchema: "public",
                        principalTable: "ManufacturedProductInventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturedProductInventoryItems_ManufactureOrderId",
                schema: "public",
                table: "ManufacturedProductInventoryItems",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturedProductInventoryItems_ProductCode",
                schema: "public",
                table: "ManufacturedProductInventoryItems",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturedProductInventoryItems_ProductCode_LotNumber",
                schema: "public",
                table: "ManufacturedProductInventoryItems",
                columns: new[] { "ProductCode", "LotNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturedProductInventoryLogs_InventoryItemId",
                schema: "public",
                table: "ManufacturedProductInventoryLogs",
                column: "InventoryItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufacturedProductInventoryLogs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ManufacturedProductInventoryItems",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                schema: "public",
                table: "TransportBoxItems");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                schema: "public",
                table: "TransportBoxItems");

            migrationBuilder.DropColumn(
                name: "SourceInventoryId",
                schema: "public",
                table: "TransportBoxItems");
        }
    }
}
