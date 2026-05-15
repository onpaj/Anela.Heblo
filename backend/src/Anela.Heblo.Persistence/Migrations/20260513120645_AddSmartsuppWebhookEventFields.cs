using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartsuppWebhookEventFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                schema: "public",
                table: "SmartsuppMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedAgentIdsJson",
                schema: "public",
                table: "SmartsuppConversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseType",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedByAgentId",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastClosedAt",
                schema: "public",
                table: "SmartsuppConversations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                schema: "public",
                table: "SmartsuppConversations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingText",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageType",
                schema: "public",
                table: "SmartsuppMessages");

            migrationBuilder.DropColumn(
                name: "AssignedAgentIdsJson",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "CloseType",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "ClosedByAgentId",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "LastClosedAt",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "Rating",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "RatingText",
                schema: "public",
                table: "SmartsuppConversations");
        }
    }
}
