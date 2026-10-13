using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketingPerformanceMonths",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    RetailOrderCount = table.Column<int>(type: "integer", nullable: false),
                    RetailRevenueWithVat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    WholesaleOrderCount = table.Column<int>(type: "integer", nullable: false),
                    WholesaleRevenueWithVat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SkippedEurInvoiceCount = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RevenueComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CostsComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingPerformanceMonths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingPerformanceChannelCosts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonthId = table.Column<int>(type: "integer", nullable: false),
                    ChannelCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CostWithoutVat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    InvoiceCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingPerformanceChannelCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingPerformanceChannelCosts_MarketingPerformanceMonths~",
                        column: x => x.MonthId,
                        principalSchema: "public",
                        principalTable: "MarketingPerformanceMonths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingPerformanceChannelCosts_MonthId_ChannelCode",
                schema: "public",
                table: "MarketingPerformanceChannelCosts",
                columns: new[] { "MonthId", "ChannelCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingPerformanceMonths_Year_Month",
                schema: "public",
                table: "MarketingPerformanceMonths",
                columns: new[] { "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketingPerformanceChannelCosts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MarketingPerformanceMonths",
                schema: "public");
        }
    }
}
