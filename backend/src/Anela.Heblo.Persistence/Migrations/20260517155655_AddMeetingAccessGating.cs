using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingAccessGating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessLevel",
                schema: "public",
                table: "MeetingTranscripts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Private");

            migrationBuilder.CreateTable(
                name: "MeetingAccessGrants",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingTranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UserDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    GrantedByUserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingAccessGrants_MeetingTranscripts_MeetingTranscriptId",
                        column: x => x.MeetingTranscriptId,
                        principalSchema: "public",
                        principalTable: "MeetingTranscripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingTranscripts_AccessLevel",
                schema: "public",
                table: "MeetingTranscripts",
                column: "AccessLevel");

            migrationBuilder.CreateIndex(
                name: "UX_MeetingAccessGrants_TranscriptId_UserEmail",
                schema: "public",
                table: "MeetingAccessGrants",
                columns: new[] { "MeetingTranscriptId", "UserEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeetingAccessGrants",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_MeetingTranscripts_AccessLevel",
                schema: "public",
                table: "MeetingTranscripts");

            migrationBuilder.DropColumn(
                name: "AccessLevel",
                schema: "public",
                table: "MeetingTranscripts");
        }
    }
}
