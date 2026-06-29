using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdCampaigns",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    PlatformCampaignId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Objective = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DailyBudget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    LifetimeBudget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdSyncLogs",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CampaignsSynced = table.Column<int>(type: "integer", nullable: false),
                    AdSetsSynced = table.Column<int>(type: "integer", nullable: false),
                    AdsSynced = table.Column<int>(type: "integer", nullable: false),
                    MetricRowsSynced = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdSyncLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdAdSets",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformAdSetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DailyBudget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdAdSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdAdSets_AdCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalSchema: "dbo",
                        principalTable: "AdCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ads",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformAdId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ads_AdAdSets_AdSetId",
                        column: x => x.AdSetId,
                        principalSchema: "dbo",
                        principalTable: "AdAdSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdDailyMetrics",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    Spend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Conversions = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdDailyMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdDailyMetrics_Ads_AdId",
                        column: x => x.AdId,
                        principalSchema: "dbo",
                        principalTable: "Ads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdAdSets_CampaignId",
                schema: "dbo",
                table: "AdAdSets",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_AdAdSets_PlatformAdSetId",
                schema: "dbo",
                table: "AdAdSets",
                column: "PlatformAdSetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaigns_Platform_PlatformCampaignId",
                schema: "dbo",
                table: "AdCampaigns",
                columns: new[] { "Platform", "PlatformCampaignId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdDailyMetrics_AdId_Date",
                schema: "dbo",
                table: "AdDailyMetrics",
                columns: new[] { "AdId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ads_AdSetId",
                schema: "dbo",
                table: "Ads",
                column: "AdSetId");

            migrationBuilder.CreateIndex(
                name: "IX_Ads_PlatformAdId",
                schema: "dbo",
                table: "Ads",
                column: "PlatformAdId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdSyncLogs_Platform_StartedAt",
                schema: "dbo",
                table: "AdSyncLogs",
                columns: new[] { "Platform", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdDailyMetrics",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AdSyncLogs",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Ads",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AdAdSets",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AdCampaigns",
                schema: "dbo");
        }
    }
}
