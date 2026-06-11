using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <summary>
    /// Removes any AppUsers rows where EntraObjectId IS NULL.
    /// These rows are data anomalies — they cannot be matched to an Entra identity and
    /// cause InvalidCastException when EF materialises the non-nullable string property.
    /// The ON DELETE CASCADE on UserGroups cleans up related group memberships automatically.
    /// </summary>
    public partial class DeleteAppUsersWithNullEntraObjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM public.""AppUsers""
                WHERE ""EntraObjectId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — deleted rows cannot be restored.
        }
    }
}
