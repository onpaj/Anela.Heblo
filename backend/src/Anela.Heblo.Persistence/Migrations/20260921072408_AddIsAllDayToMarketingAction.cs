using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAllDayToMarketingAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAllDay",
                schema: "public",
                table: "MarketingActions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: reproduce the legacy IsDateOnly guess as a one-time value so no
            // existing row's live export behavior changes at the moment this migration
            // runs. Rows with an active OutlookEventId self-correct on their next import
            // cycle (see arch-review.r1.md, Risks and Mitigations).
            migrationBuilder.Sql(@"
                UPDATE ""public"".""MarketingActions""
                SET ""IsAllDay"" = TRUE
                WHERE ""EndDate"" IS NOT NULL
                  AND date_trunc('day', ""StartDate"") = ""StartDate""
                  AND date_trunc('day', ""EndDate"") = ""EndDate"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAllDay",
                schema: "public",
                table: "MarketingActions");
        }
    }
}
