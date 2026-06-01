using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceClassificationFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassificationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AccountingPrescription = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AbraInvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClassificationRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    AccountingPrescription = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationHistory_ClassificationRules_ClassificationRul~",
                        column: x => x.ClassificationRuleId,
                        principalTable: "ClassificationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_AbraInvoiceId",
                table: "ClassificationHistory",
                column: "AbraInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_ClassificationRuleId",
                table: "ClassificationHistory",
                column: "ClassificationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_Result",
                table: "ClassificationHistory",
                column: "Result");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationHistory_Timestamp",
                table: "ClassificationHistory",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRules_Order",
                table: "ClassificationRules",
                column: "Order",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRules_Type_Pattern",
                table: "ClassificationRules",
                columns: new[] { "Type", "Pattern" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassificationHistory");

            migrationBuilder.DropTable(
                name: "ClassificationRules");
        }
    }
}
