using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Packages",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PackageNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippingProviderCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShippingProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipmentGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    PackedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PackedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Packages_OrderCode",
                schema: "public",
                table: "Packages",
                column: "OrderCode");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_OrderCode_PackageNumber",
                schema: "public",
                table: "Packages",
                columns: new[] { "OrderCode", "PackageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Packages_PackedAt",
                schema: "public",
                table: "Packages",
                column: "PackedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Packages",
                schema: "public");
        }
    }
}
