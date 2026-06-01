using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartsuppContactsAndExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                schema: "public",
                table: "SmartsuppMessages",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFirstReply",
                schema: "public",
                table: "SmartsuppMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOffline",
                schema: "public",
                table: "SmartsuppMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReply",
                schema: "public",
                table: "SmartsuppMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PageUrl",
                schema: "public",
                table: "SmartsuppMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseTime",
                schema: "public",
                table: "SmartsuppMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubType",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerId",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerName",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                schema: "public",
                table: "SmartsuppMessages",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "VisitorId",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactId",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Domain",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtId",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                schema: "public",
                table: "SmartsuppConversations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOffline",
                schema: "public",
                table: "SmartsuppConversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsServed",
                schema: "public",
                table: "SmartsuppConversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocationCity",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationCode",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationCountry",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationIp",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Referer",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                schema: "public",
                table: "SmartsuppConversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariablesJson",
                schema: "public",
                table: "SmartsuppConversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitorId",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmartsuppContacts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    BannedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BannedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GdprApproved = table.Column<bool>(type: "boolean", nullable: false),
                    TagsJson = table.Column<string>(type: "text", nullable: true),
                    PropertiesJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppContacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppMessages_ConversationId_SubType",
                schema: "public",
                table: "SmartsuppMessages",
                columns: new[] { "ConversationId", "SubType" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppConversations_ContactId",
                schema: "public",
                table: "SmartsuppConversations",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppContacts_Email",
                schema: "public",
                table: "SmartsuppContacts",
                column: "Email");

            migrationBuilder.AddForeignKey(
                name: "FK_SmartsuppConversations_SmartsuppContacts_ContactId",
                schema: "public",
                table: "SmartsuppConversations",
                column: "ContactId",
                principalSchema: "public",
                principalTable: "SmartsuppContacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SmartsuppConversations_SmartsuppContacts_ContactId",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropTable(
                name: "SmartsuppContacts",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_SmartsuppMessages_ConversationId_SubType",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropIndex(
                name: "IX_SmartsuppConversations_ContactId",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "AgentId",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "IsFirstReply",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "IsOffline",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "IsReply",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "PageUrl",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "ResponseTime",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "SubType",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "TriggerId",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "TriggerName",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "VisitorId",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "ContactId",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "Domain",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "ExtId",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "IsOffline",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "IsServed",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "LocationCity",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "LocationCode",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "LocationCountry",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "LocationIp",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "Referer",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VariablesJson",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VisitorId",
                schema: "public",
                table: "SmartsuppConversations");
        }
    }
}
