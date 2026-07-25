using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignPhotobankIndexRootTimestampWithoutTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert timestamptz -> timestamp (without time zone), interpreting the stored
            // instant as UTC so the naive value matches the application's UTC convention
            // regardless of the database session timezone.
            migrationBuilder.Sql(
                "ALTER TABLE public.\"PhotobankIndexRoots\" " +
                "ALTER COLUMN \"LastIndexedAt\" TYPE timestamp USING \"LastIndexedAt\" AT TIME ZONE 'UTC';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to timestamptz, interpreting the naive timestamp as UTC.
            migrationBuilder.Sql(
                "ALTER TABLE public.\"PhotobankIndexRoots\" " +
                "ALTER COLUMN \"LastIndexedAt\" TYPE timestamp with time zone USING \"LastIndexedAt\" AT TIME ZONE 'UTC';");
        }
    }
}
