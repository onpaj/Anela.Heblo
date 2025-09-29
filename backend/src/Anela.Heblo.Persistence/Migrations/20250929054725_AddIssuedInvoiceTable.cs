using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssuedInvoice",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedInvoice", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoice_Code_Unique",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoice_InvoiceDate",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "InvoiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoice_LastSyncTime",
                schema: "dbo",
                table: "IssuedInvoice",
                column: "LastSyncTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssuedInvoice",
                schema: "dbo");
        }
    }
}
