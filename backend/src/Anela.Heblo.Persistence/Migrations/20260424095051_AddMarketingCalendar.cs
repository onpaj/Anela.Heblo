using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketingActions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedByUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedByUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedByUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingActionFolderLinks",
                schema: "public",
                columns: table => new
                {
                    MarketingActionId = table.Column<int>(type: "integer", nullable: false),
                    FolderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FolderType = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingActionFolderLinks", x => new { x.MarketingActionId, x.FolderKey });
                    table.ForeignKey(
                        name: "FK_MarketingActionFolderLinks_MarketingActions_MarketingAction~",
                        column: x => x.MarketingActionId,
                        principalSchema: "public",
                        principalTable: "MarketingActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketingActionProducts",
                schema: "public",
                columns: table => new
                {
                    MarketingActionId = table.Column<int>(type: "integer", nullable: false),
                    ProductCodePrefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingActionProducts", x => new { x.MarketingActionId, x.ProductCodePrefix });
                    table.ForeignKey(
                        name: "FK_MarketingActionProducts_MarketingActions_MarketingActionId",
                        column: x => x.MarketingActionId,
                        principalSchema: "public",
                        principalTable: "MarketingActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActionFolderLinks_MarketingActionId",
                schema: "public",
                table: "MarketingActionFolderLinks",
                column: "MarketingActionId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActionProducts_ProductCodePrefix",
                schema: "public",
                table: "MarketingActionProducts",
                column: "ProductCodePrefix");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActions_ActionType",
                schema: "public",
                table: "MarketingActions",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActions_EndDate",
                schema: "public",
                table: "MarketingActions",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActions_IsDeleted_StartDate_EndDate",
                schema: "public",
                table: "MarketingActions",
                columns: new[] { "IsDeleted", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingActions_StartDate",
                schema: "public",
                table: "MarketingActions",
                column: "StartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketingActionFolderLinks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MarketingActionProducts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "MarketingActions",
                schema: "public");
        }
    }
}
