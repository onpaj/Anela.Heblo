using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartsuppVisitorCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisitorBrowser",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitorBrowserVersion",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisitorInfoFetchedAt",
                schema: "public",
                table: "SmartsuppConversations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitorOs",
                schema: "public",
                table: "SmartsuppConversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitorUserAgent",
                schema: "public",
                table: "SmartsuppConversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VisitorVisitsCount",
                schema: "public",
                table: "SmartsuppConversations",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisitorBrowser",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VisitorBrowserVersion",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VisitorInfoFetchedAt",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VisitorOs",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VisitorUserAgent",
                schema: "public",
                table: "SmartsuppConversations");

            migrationBuilder.DropColumn(
                name: "VisitorVisitsCount",
                schema: "public",
                table: "SmartsuppConversations");
        }
    }
}
