using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartsuppConversationPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartsuppConversationPresences",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EnteredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppConversationPresences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppConversationPresences_ConversationId_AgentId_Source",
                schema: "public",
                table: "SmartsuppConversationPresences",
                columns: new[] { "ConversationId", "AgentId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartsuppConversationPresences_LastSeenAt",
                schema: "public",
                table: "SmartsuppConversationPresences",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartsuppConversationPresences",
                schema: "public");
        }
    }
}
