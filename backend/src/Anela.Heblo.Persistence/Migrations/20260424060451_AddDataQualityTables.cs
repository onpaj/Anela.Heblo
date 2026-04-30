using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataQualityTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dqt_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_type = table.Column<int>(type: "integer", nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: false),
                    date_to = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    trigger_type = table.Column<int>(type: "integer", nullable: false),
                    total_checked = table.Column<int>(type: "integer", nullable: false),
                    total_mismatches = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dqt_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_dqt_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dqt_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_code = table.Column<string>(type: "text", nullable: false),
                    mismatch_type = table.Column<int>(type: "integer", nullable: false),
                    shoptet_value = table.Column<string>(type: "text", nullable: true),
                    flexi_value = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_dqt_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_dqt_results_dqt_runs_dqt_run_id",
                        column: x => x.dqt_run_id,
                        principalTable: "dqt_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dqt_runs_test_type_started_at",
                table: "dqt_runs",
                columns: new[] { "test_type", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_dqt_results_dqt_run_id",
                table: "invoice_dqt_results",
                column: "dqt_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_dqt_results_invoice_code",
                table: "invoice_dqt_results",
                column: "invoice_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_dqt_results");

            migrationBuilder.DropTable(
                name: "dqt_runs");
        }
    }
}
