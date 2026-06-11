using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Analytics.Migrations
{
    /// <inheritdoc />
    public partial class InitialAnalyticsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "flexi_raw");

            migrationBuilder.CreateTable(
                name: "accounting_template",
                schema: "flexi_raw",
                columns: table => new
                {
                    flexi_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_template", x => x.flexi_id);
                });

            migrationBuilder.CreateTable(
                name: "contact",
                schema: "flexi_raw",
                columns: table => new
                {
                    flexi_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    cin = table.Column<string>(type: "text", nullable: true),
                    vatin = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact", x => x.flexi_id);
                });

            migrationBuilder.CreateTable(
                name: "department",
                schema: "flexi_raw",
                columns: table => new
                {
                    flexi_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_department", x => x.flexi_id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entry",
                schema: "flexi_raw",
                columns: table => new
                {
                    flexi_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period = table.Column<string>(type: "text", nullable: true),
                    document_type = table.Column<string>(type: "text", nullable: true),
                    account_debit = table.Column<string>(type: "text", nullable: true),
                    account_credit = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    cost_center = table.Column<string>(type: "text", nullable: true),
                    contact = table.Column<string>(type: "text", nullable: true),
                    accounting_template = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entry", x => x.flexi_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                schema: "flexi_raw",
                columns: table => new
                {
                    entity_name = table.Column<string>(type: "text", nullable: false),
                    watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_status = table.Column<string>(type: "text", nullable: true),
                    last_run_rows_fetched = table.Column<int>(type: "integer", nullable: true),
                    last_run_rows_upserted = table.Column<int>(type: "integer", nullable: true),
                    last_error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => x.entity_name);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entry_account_credit",
                schema: "flexi_raw",
                table: "ledger_entry",
                column: "account_credit");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entry_account_debit",
                schema: "flexi_raw",
                table: "ledger_entry",
                column: "account_debit");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entry_cost_center",
                schema: "flexi_raw",
                table: "ledger_entry",
                column: "cost_center");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entry_entry_date",
                schema: "flexi_raw",
                table: "ledger_entry",
                column: "entry_date");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entry_last_modified",
                schema: "flexi_raw",
                table: "ledger_entry",
                column: "last_modified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_template",
                schema: "flexi_raw");

            migrationBuilder.DropTable(
                name: "contact",
                schema: "flexi_raw");

            migrationBuilder.DropTable(
                name: "department",
                schema: "flexi_raw");

            migrationBuilder.DropTable(
                name: "ledger_entry",
                schema: "flexi_raw");

            migrationBuilder.DropTable(
                name: "sync_state",
                schema: "flexi_raw");
        }
    }
}
