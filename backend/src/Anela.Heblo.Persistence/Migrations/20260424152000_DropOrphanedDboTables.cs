using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropOrphanedDboTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS dbo.""Jobs"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS dbo.""ScheduledTask"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "DropOrphanedDboTables is irreversible. The dbo.Jobs and dbo.ScheduledTask tables cannot be restored by rollback.");
        }
    }
}
