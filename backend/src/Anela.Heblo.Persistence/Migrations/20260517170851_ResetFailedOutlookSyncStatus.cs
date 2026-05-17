using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResetFailedOutlookSyncStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "MarketingActions"
                SET "OutlookSyncStatus" = 'NotSynced', "OutlookSyncError" = NULL
                WHERE "OutlookSyncStatus" = 'Failed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot restore individual error states — intentionally empty.
        }
    }
}
