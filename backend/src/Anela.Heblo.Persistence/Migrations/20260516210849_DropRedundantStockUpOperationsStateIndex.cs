using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRedundantStockUpOperationsStateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drops the bare-State index. Composite index IX_StockUpOperations_State_CreatedAt covers
            // leading-column State equality lookups (used by IStockUpOperationRepository.GetByStateAsync
            // and GetFailedOperationsAsync) via standard btree leading-column scan.
            //
            // The partial index IX_StockUpOperations_State_Active continues to cover the
            // GetStockUpOperationsSummary query (WHERE State IN (0,1,3) [+ optional SourceType]).
            //
            // suppressTransaction: true — PostgreSQL rejects DROP INDEX CONCURRENTLY inside a
            // transaction block (SQLSTATE 25001). See memory/gotchas/postgres-partial-index-active-states.md.
            // IF EXISTS keeps both directions idempotent.
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS public."IX_StockUpOperations_State";
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_StockUpOperations_State"
                    ON public."StockUpOperations" ("State");
                """,
                suppressTransaction: true);
        }
    }
}
