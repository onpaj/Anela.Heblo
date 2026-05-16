using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingTasksTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeetingTranscripts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaudRecordingId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlaudCreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    RawTranscript = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    ReviewedByUser = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingTranscripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProposedTasks",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingTranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Assignee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalTaskId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsManuallyAdded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposedTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProposedTasks_MeetingTranscripts_MeetingTranscriptId",
                        column: x => x.MeetingTranscriptId,
                        principalSchema: "public",
                        principalTable: "MeetingTranscripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingTranscripts_ReceivedAt",
                schema: "public",
                table: "MeetingTranscripts",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingTranscripts_Status",
                schema: "public",
                table: "MeetingTranscripts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_MeetingTranscripts_PlaudRecordingId",
                schema: "public",
                table: "MeetingTranscripts",
                column: "PlaudRecordingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProposedTasks_MeetingTranscriptId",
                schema: "public",
                table: "ProposedTasks",
                column: "MeetingTranscriptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposedTasks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MeetingTranscripts",
                schema: "public");
        }
    }
}
