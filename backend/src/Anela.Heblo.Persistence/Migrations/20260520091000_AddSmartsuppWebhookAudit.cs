using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartsuppWebhookAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartsuppWebhookAuditEntries",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RemoteIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignatureHeader = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SignatureStatus = table.Column<int>(type: "integer", nullable: false),
                    HeadersJson = table.Column<string>(type: "text", nullable: false),
                    RawBody = table.Column<string>(type: "text", nullable: false),
                    BodySizeBytes = table.Column<int>(type: "integer", nullable: false),
                    EventName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AppId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventTimestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessingError = table.Column<string>(type: "text", nullable: true),
                    ProcessingDurationMs = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false),
                    LastReplayedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastReplayedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppWebhookAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppWebhookAuditEntries_EventName_ReceivedAt",
                schema: "public",
                table: "SmartsuppWebhookAuditEntries",
                columns: new[] { "EventName", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppWebhookAuditEntries_ProcessingStatus_ReceivedAt",
                schema: "public",
                table: "SmartsuppWebhookAuditEntries",
                columns: new[] { "ProcessingStatus", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppWebhookAuditEntries_ReceivedAt",
                schema: "public",
                table: "SmartsuppWebhookAuditEntries",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartsuppWebhookAuditEntries",
                schema: "public");
        }
    }
}
