using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedPlaudRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeletedPlaudRecordings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaudRecordingId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    DeletedByUserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedPlaudRecordings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DeletedPlaudRecordings_PlaudRecordingId",
                schema: "public",
                table: "DeletedPlaudRecordings",
                column: "PlaudRecordingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedPlaudRecordings",
                schema: "public");
        }
    }
}
