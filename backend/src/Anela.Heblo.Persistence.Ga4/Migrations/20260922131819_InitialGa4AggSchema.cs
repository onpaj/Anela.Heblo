using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Ga4.Migrations
{
    /// <inheritdoc />
    public partial class InitialGa4AggSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ga4_agg");

            migrationBuilder.CreateTable(
                name: "conversions_daily",
                schema: "ga4_agg",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    channel_group = table.Column<string>(type: "text", nullable: false),
                    transactions = table.Column<long>(type: "bigint", nullable: false),
                    purchase_revenue = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversions_daily", x => new { x.date, x.channel_group });
                });

            migrationBuilder.CreateTable(
                name: "landing_page_daily",
                schema: "ga4_agg",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    landing_page = table.Column<string>(type: "text", nullable: false),
                    sessions = table.Column<long>(type: "bigint", nullable: false),
                    engaged_sessions = table.Column<long>(type: "bigint", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_page_daily", x => new { x.date, x.landing_page });
                });

            migrationBuilder.CreateTable(
                name: "page_daily",
                schema: "ga4_agg",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    page_path = table.Column<string>(type: "text", nullable: false),
                    page_title = table.Column<string>(type: "text", nullable: true),
                    screen_page_views = table.Column<long>(type: "bigint", nullable: false),
                    sessions = table.Column<long>(type: "bigint", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_daily", x => new { x.date, x.page_path });
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                schema: "ga4_agg",
                columns: table => new
                {
                    entity_name = table.Column<string>(type: "text", nullable: false),
                    watermark_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_run_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_status = table.Column<string>(type: "text", nullable: true),
                    last_run_rows_fetched = table.Column<int>(type: "integer", nullable: true),
                    last_run_rows_upserted = table.Column<int>(type: "integer", nullable: true),
                    top_n_per_day = table.Column<int>(type: "integer", nullable: true),
                    last_error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => x.entity_name);
                });

            migrationBuilder.CreateTable(
                name: "traffic_daily",
                schema: "ga4_agg",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    channel_group = table.Column<string>(type: "text", nullable: false),
                    sessions = table.Column<long>(type: "bigint", nullable: false),
                    total_users = table.Column<long>(type: "bigint", nullable: false),
                    new_users = table.Column<long>(type: "bigint", nullable: false),
                    screen_page_views = table.Column<long>(type: "bigint", nullable: false),
                    engaged_sessions = table.Column<long>(type: "bigint", nullable: false),
                    user_engagement_seconds = table.Column<long>(type: "bigint", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_traffic_daily", x => new { x.date, x.channel_group });
                });

            migrationBuilder.CreateTable(
                name: "traffic_monthly",
                schema: "ga4_agg",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    sessions = table.Column<long>(type: "bigint", nullable: false),
                    total_users = table.Column<long>(type: "bigint", nullable: false),
                    new_users = table.Column<long>(type: "bigint", nullable: false),
                    screen_page_views = table.Column<long>(type: "bigint", nullable: false),
                    engaged_sessions = table.Column<long>(type: "bigint", nullable: false),
                    user_engagement_seconds = table.Column<long>(type: "bigint", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_traffic_monthly", x => x.month);
                });

            migrationBuilder.CreateTable(
                name: "traffic_total_daily",
                schema: "ga4_agg",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    sessions = table.Column<long>(type: "bigint", nullable: false),
                    total_users = table.Column<long>(type: "bigint", nullable: false),
                    new_users = table.Column<long>(type: "bigint", nullable: false),
                    screen_page_views = table.Column<long>(type: "bigint", nullable: false),
                    engaged_sessions = table.Column<long>(type: "bigint", nullable: false),
                    user_engagement_seconds = table.Column<long>(type: "bigint", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_traffic_total_daily", x => x.date);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversions_daily_date",
                schema: "ga4_agg",
                table: "conversions_daily",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_landing_page_daily_date",
                schema: "ga4_agg",
                table: "landing_page_daily",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_page_daily_date",
                schema: "ga4_agg",
                table: "page_daily",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_traffic_daily_date",
                schema: "ga4_agg",
                table: "traffic_daily",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversions_daily",
                schema: "ga4_agg");

            migrationBuilder.DropTable(
                name: "landing_page_daily",
                schema: "ga4_agg");

            migrationBuilder.DropTable(
                name: "page_daily",
                schema: "ga4_agg");

            migrationBuilder.DropTable(
                name: "sync_state",
                schema: "ga4_agg");

            migrationBuilder.DropTable(
                name: "traffic_daily",
                schema: "ga4_agg");

            migrationBuilder.DropTable(
                name: "traffic_monthly",
                schema: "ga4_agg");

            migrationBuilder.DropTable(
                name: "traffic_total_daily",
                schema: "ga4_agg");
        }
    }
}
