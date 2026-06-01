using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartsuppTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartsuppConversations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactAvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsUnread = table.Column<bool>(type: "boolean", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastMessagePreview = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartsuppSyncState",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastUpdatedAtSeen = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastSyncStartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppSyncState", x => x.Id);
                    table.CheckConstraint("CK_SmartsuppSyncState_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "SmartsuppMessages",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartsuppMessages_SmartsuppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "public",
                        principalTable: "SmartsuppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "SmartsuppSyncState",
                columns: new[] { "Id", "LastSyncStartedAt", "LastUpdatedAtSeen" },
                values: new object[] { 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppConversations_Status_LastMessageAt",
                schema: "public",
                table: "SmartsuppConversations",
                columns: new[] { "Status", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppMessages_ConversationId_CreatedAt",
                schema: "public",
                table: "SmartsuppMessages",
                columns: new[] { "ConversationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartsuppMessages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SmartsuppSyncState",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SmartsuppConversations",
                schema: "public");
        }
    }
}
