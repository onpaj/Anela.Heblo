using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropSmartsuppSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartsuppSyncState",
                schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartsuppSyncState",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastSyncStartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUpdatedAtSeen = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartsuppSyncState", x => x.Id);
                    table.CheckConstraint("CK_SmartsuppSyncState_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "SmartsuppSyncState",
                columns: new[] { "Id", "LastSyncStartedAt", "LastUpdatedAtSeen" },
                values: new object[] { 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null });
        }
    }
}
