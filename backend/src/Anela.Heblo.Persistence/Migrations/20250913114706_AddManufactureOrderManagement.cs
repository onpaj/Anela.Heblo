using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufactureOrderManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufactureOrders",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp", nullable: false),
                    CreatedByUser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResponsiblePerson = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SemiProductPlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProductPlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StateChangedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    StateChangedByUser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManufactureOrderAuditLogs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp", nullable: false),
                    User = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrderAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufactureOrderAuditLogs_ManufactureOrders_ManufactureOrde~",
                        column: x => x.ManufactureOrderId,
                        principalSchema: "public",
                        principalTable: "ManufactureOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManufactureOrderNotes",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    CreatedByUser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrderNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufactureOrderNotes_ManufactureOrders_ManufactureOrderId",
                        column: x => x.ManufactureOrderId,
                        principalSchema: "public",
                        principalTable: "ManufactureOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManufactureOrderProducts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SemiProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrderProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufactureOrderProducts_ManufactureOrders_ManufactureOrder~",
                        column: x => x.ManufactureOrderId,
                        principalSchema: "public",
                        principalTable: "ManufactureOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManufactureOrderSemiProducts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManufactureOrderId = table.Column<int>(type: "integer", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufactureOrderSemiProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufactureOrderSemiProducts_ManufactureOrders_ManufactureO~",
                        column: x => x.ManufactureOrderId,
                        principalSchema: "public",
                        principalTable: "ManufactureOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderAuditLogs_Action",
                schema: "public",
                table: "ManufactureOrderAuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderAuditLogs_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderAuditLogs",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderAuditLogs_Timestamp",
                schema: "public",
                table: "ManufactureOrderAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderNotes_CreatedAt",
                schema: "public",
                table: "ManufactureOrderNotes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderNotes_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderNotes",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderProducts_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderProducts",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderProducts_ProductCode",
                schema: "public",
                table: "ManufactureOrderProducts",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderProducts_SemiProductCode",
                schema: "public",
                table: "ManufactureOrderProducts",
                column: "SemiProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrders_CreatedDate",
                schema: "public",
                table: "ManufactureOrders",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrders_OrderNumber",
                schema: "public",
                table: "ManufactureOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrders_ResponsiblePerson",
                schema: "public",
                table: "ManufactureOrders",
                column: "ResponsiblePerson");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrders_State",
                schema: "public",
                table: "ManufactureOrders",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderSemiProducts_ManufactureOrderId",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                column: "ManufactureOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufactureOrderSemiProducts_ProductCode",
                schema: "public",
                table: "ManufactureOrderSemiProducts",
                column: "ProductCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufactureOrderAuditLogs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ManufactureOrderNotes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ManufactureOrderProducts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ManufactureOrderSemiProducts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ManufactureOrders",
                schema: "public");
        }
    }
}
