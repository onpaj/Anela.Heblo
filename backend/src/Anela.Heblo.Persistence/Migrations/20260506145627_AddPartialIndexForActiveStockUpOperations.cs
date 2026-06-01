using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartialIndexForActiveStockUpOperations : Migration
    {
        // Active states: Pending=0, Submitted=1, Failed=3. Completed=2 is excluded — that is the
        // high-volume terminal state the summary endpoint never queries. Keeping it out of the
        // index keeps the index small and cheap to maintain.
        //
        // suppressTransaction: true is required. PostgreSQL rejects CREATE INDEX CONCURRENTLY
        // inside a transaction block (SQLSTATE 25001). EF Core wraps each migration's Up in a
        // transaction by default; passing suppressTransaction: true causes the migration runner
        // to commit before issuing this statement.
        //
        // IF NOT EXISTS / IF EXISTS keep both directions idempotent (re-running is safe).
        // If a CONCURRENTLY build fails partway and leaves an INVALID index, recover with:
        //   DROP INDEX CONCURRENTLY "IX_StockUpOperations_State_Active";
        // and re-run the migration.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_StockUpOperations_State_Active"
                    ON public."StockUpOperations" ("SourceType", "State")
                    WHERE "State" IN (0, 1, 3);
                """,
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_StockUpOperations_State_Active";
                """,
                suppressTransaction: true);
        }
    }
}
