using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingOutlookSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutlookEventId",
                schema: "public",
                table: "MarketingActions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutlookSyncError",
                schema: "public",
                table: "MarketingActions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutlookSyncStatus",
                schema: "public",
                table: "MarketingActions",
                type: "text",
                nullable: false,
                defaultValue: "NotSynced");

            migrationBuilder.AddColumn<DateTime>(
                name: "OutlookSyncedAt",
                schema: "public",
                table: "MarketingActions",
                type: "timestamp",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActions_OutlookEventId",
                schema: "public",
                table: "MarketingActions",
                column: "OutlookEventId",
                unique: true,
                filter: "\"OutlookEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActions_OutlookSyncStatus",
                schema: "public",
                table: "MarketingActions",
                column: "OutlookSyncStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketingActions_OutlookEventId",
                schema: "public",
                table: "MarketingActions");

            migrationBuilder.DropIndex(
                name: "IX_MarketingActions_OutlookSyncStatus",
                schema: "public",
                table: "MarketingActions");

            migrationBuilder.DropColumn(
                name: "OutlookEventId",
                schema: "public",
                table: "MarketingActions");

            migrationBuilder.DropColumn(
                name: "OutlookSyncError",
                schema: "public",
                table: "MarketingActions");

            migrationBuilder.DropColumn(
                name: "OutlookSyncStatus",
                schema: "public",
                table: "MarketingActions");

            migrationBuilder.DropColumn(
                name: "OutlookSyncedAt",
                schema: "public",
                table: "MarketingActions");
        }
    }
}
