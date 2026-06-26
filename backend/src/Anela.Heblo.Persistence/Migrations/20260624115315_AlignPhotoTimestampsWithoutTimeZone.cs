using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignPhotoTimestampsWithoutTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert timestamptz -> timestamp (without time zone), interpreting the stored
            // instant as UTC so the naive value matches the application's UTC convention
            // regardless of the database session timezone.
            migrationBuilder.Sql(
                "ALTER TABLE public.\"Photos\" " +
                "ALTER COLUMN \"TakenAt\" TYPE timestamp USING \"TakenAt\" AT TIME ZONE 'UTC';");

            migrationBuilder.Sql(
                "ALTER TABLE public.\"Photos\" " +
                "ALTER COLUMN \"LastAutoTaggedAt\" TYPE timestamp USING \"LastAutoTaggedAt\" AT TIME ZONE 'UTC';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to timestamptz, interpreting the naive timestamp as UTC.
            migrationBuilder.Sql(
                "ALTER TABLE public.\"Photos\" " +
                "ALTER COLUMN \"TakenAt\" TYPE timestamp with time zone USING \"TakenAt\" AT TIME ZONE 'UTC';");

            migrationBuilder.Sql(
                "ALTER TABLE public.\"Photos\" " +
                "ALTER COLUMN \"LastAutoTaggedAt\" TYPE timestamp with time zone USING \"LastAutoTaggedAt\" AT TIME ZONE 'UTC';");
        }
    }
}
