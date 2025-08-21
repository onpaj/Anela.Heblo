using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryProductFamilies",
                columns: table => new
                {
                    JournalEntryId = table.Column<int>(type: "integer", nullable: false),
                    ProductCodePrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryProductFamilies", x => new { x.JournalEntryId, x.ProductCodePrefix });
                    table.ForeignKey(
                        name: "FK_JournalEntryProductFamilies_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryProducts",
                columns: table => new
                {
                    JournalEntryId = table.Column<int>(type: "integer", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryProducts", x => new { x.JournalEntryId, x.ProductCode });
                    table.ForeignKey(
                        name: "FK_JournalEntryProducts_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryTagAssignments",
                columns: table => new
                {
                    JournalEntryId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryTagAssignments", x => new { x.JournalEntryId, x.TagId });
                    table.ForeignKey(
                        name: "FK_JournalEntryTagAssignments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalEntryTagAssignments_JournalEntryTags_TagId",
                        column: x => x.TagId,
                        principalTable: "JournalEntryTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CreatedByUserId",
                table: "JournalEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryDate",
                table: "JournalEntries",
                column: "EntryDate");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_IsDeleted_EntryDate",
                table: "JournalEntries",
                columns: new[] { "IsDeleted", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryProductFamilies_ProductCodePrefix",
                table: "JournalEntryProductFamilies",
                column: "ProductCodePrefix");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryProducts_ProductCode",
                table: "JournalEntryProducts",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryTagAssignments_TagId",
                table: "JournalEntryTagAssignments",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryTags_Name",
                table: "JournalEntryTags",
                column: "Name",
                unique: true);

            // Insert default tags
            migrationBuilder.InsertData(
                table: "JournalEntryTags",
                columns: new[] { "Name", "Color", "CreatedAt", "CreatedByUserId" },
                values: new object[,]
                {
                    { "Nákup", "#EF4444", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { "Marketing", "#3B82F6", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { "Výroba", "#10B981", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { "Receptura", "#F59E0B", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                    { "Akce", "#DC2626", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove default tags
            migrationBuilder.DeleteData(
                table: "JournalEntryTags",
                keyColumn: "CreatedByUserId",
                keyValue: "system");

            migrationBuilder.DropTable(
                name: "JournalEntryProductFamilies");

            migrationBuilder.DropTable(
                name: "JournalEntryProducts");

            migrationBuilder.DropTable(
                name: "JournalEntryTagAssignments");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "JournalEntryTags");
        }
    }
}
