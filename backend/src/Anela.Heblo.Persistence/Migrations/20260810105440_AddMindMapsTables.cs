using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMindMapsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MindMaps",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MindMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MindMapMeetings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MindMapId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingTranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MindMapMeetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MindMapMeetings_MeetingTranscripts_MeetingTranscriptId",
                        column: x => x.MeetingTranscriptId,
                        principalSchema: "public",
                        principalTable: "MeetingTranscripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MindMapMeetings_MindMaps_MindMapId",
                        column: x => x.MindMapId,
                        principalSchema: "public",
                        principalTable: "MindMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MindMapVersions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MindMapId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Json = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    TriggerMeetingId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MindMapVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MindMapVersions_MindMaps_MindMapId",
                        column: x => x.MindMapId,
                        principalSchema: "public",
                        principalTable: "MindMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MindMapMeetings_MeetingTranscriptId",
                schema: "public",
                table: "MindMapMeetings",
                column: "MeetingTranscriptId");

            migrationBuilder.CreateIndex(
                name: "UX_MindMapMeetings_MindMapId_MeetingTranscriptId",
                schema: "public",
                table: "MindMapMeetings",
                columns: new[] { "MindMapId", "MeetingTranscriptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MindMapVersions_MindMapId_VersionNumber",
                schema: "public",
                table: "MindMapVersions",
                columns: new[] { "MindMapId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MindMapMeetings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MindMapVersions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MindMaps",
                schema: "public");
        }
    }
}
