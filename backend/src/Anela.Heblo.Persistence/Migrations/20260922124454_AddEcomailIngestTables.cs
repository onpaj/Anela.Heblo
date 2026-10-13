using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEcomailIngestTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EcomailAutomationMonths",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PipelineId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Send = table.Column<int>(type: "integer", nullable: false),
                    Open = table.Column<int>(type: "integer", nullable: false),
                    Click = table.Column<int>(type: "integer", nullable: false),
                    Unsub = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcomailAutomationMonths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EcomailAutomationSnapshots",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PipelineId = table.Column<int>(type: "integer", nullable: false),
                    CapturedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Triggered = table.Column<int>(type: "integer", nullable: false),
                    Ended = table.Column<int>(type: "integer", nullable: false),
                    Send = table.Column<int>(type: "integer", nullable: false),
                    Open = table.Column<int>(type: "integer", nullable: false),
                    Click = table.Column<int>(type: "integer", nullable: false),
                    Unsub = table.Column<int>(type: "integer", nullable: false),
                    Bounce = table.Column<int>(type: "integer", nullable: false),
                    Conversions = table.Column<int>(type: "integer", nullable: false),
                    ConversionsValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcomailAutomationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EcomailCampaigns",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FromEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CampaignType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Recipients = table.Column<int>(type: "integer", nullable: false),
                    Inject = table.Column<int>(type: "integer", nullable: false),
                    Delivery = table.Column<int>(type: "integer", nullable: false),
                    Open = table.Column<int>(type: "integer", nullable: false),
                    TotalOpen = table.Column<int>(type: "integer", nullable: false),
                    Click = table.Column<int>(type: "integer", nullable: false),
                    TotalClick = table.Column<int>(type: "integer", nullable: false),
                    Unsub = table.Column<int>(type: "integer", nullable: false),
                    Bounce = table.Column<int>(type: "integer", nullable: false),
                    Spam = table.Column<int>(type: "integer", nullable: false),
                    Conversions = table.Column<int>(type: "integer", nullable: false),
                    ConversionsValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcomailCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EcomailPipelines",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ListId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcomailPipelines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EcomailAutomationMonths_PipelineId_Year_Month",
                schema: "public",
                table: "EcomailAutomationMonths",
                columns: new[] { "PipelineId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EcomailAutomationSnapshots_PipelineId_CapturedOn",
                schema: "public",
                table: "EcomailAutomationSnapshots",
                columns: new[] { "PipelineId", "CapturedOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EcomailCampaigns_ParentId",
                schema: "public",
                table: "EcomailCampaigns",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_EcomailCampaigns_SentAt",
                schema: "public",
                table: "EcomailCampaigns",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_EcomailCampaigns_Status_CampaignType",
                schema: "public",
                table: "EcomailCampaigns",
                columns: new[] { "Status", "CampaignType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EcomailAutomationMonths",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EcomailAutomationSnapshots",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EcomailCampaigns",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EcomailPipelines",
                schema: "public");
        }
    }
}
