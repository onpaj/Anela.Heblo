---
name: PostgreSQL partial index for active StockUpOperations
description: 12s spike on summary endpoint caused by Seq Scan; fixed with partial index + Contains query rewrite. Two codebase-wide rules established.
type: project
---

# Partial Index for Active StockUpOperations

## Symptom
`GET /api/StockUpOperations/summary` showed a 12.3 s spike in App Insights. Expected p95 ≤ 300 ms.

## Root cause
`StockUpOperations` is dominated by `Completed` rows (the terminal state for every successfully
processed operation). Once `Completed` rows make up the majority of the table, PostgreSQL's planner
determines a sequential scan is cheaper than using `IX_StockUpOperations_State` — the existing
single-column state index covers all states equally and does not help the planner understand that
only a tiny fraction of rows match the `IN (0, 1, 3)` predicate.

**Why:** Planner statistics showed the table was overwhelmingly `Completed`. The existing index was
not selective enough to persuade the planner away from Seq Scan as the table grew.

## Fix
Partial index covering only the active states the summary endpoint actually queries:

```sql
CREATE INDEX CONCURRENTLY "IX_StockUpOperations_State_Active"
    ON public."StockUpOperations" ("SourceType", "State")
    WHERE "State" IN (0, 1, 3);  -- Pending=0, Submitted=1, Failed=3
```

Handler's WHERE clause uses `int[].Contains` so EF emits a literal `IN (0, 1, 3)` that the planner
can match to the partial-index predicate. Chained `||` against an enum can emit parameter-based
equality which is opaque to the planner at plan time.

## Two repo-wide rules this case established

**How to apply:** Apply these rules to any future PostgreSQL migration or EF handler in this codebase.

1. **Any `CREATE INDEX CONCURRENTLY` migration must use `migrationBuilder.Sql(sql, suppressTransaction: true)`.**
   EF Core wraps every migration's `Up` in a transaction by default; PostgreSQL rejects `CONCURRENTLY`
   inside a transaction with `SQLSTATE 25001`. The pre-existing `RebuildKnowledgeBaseHnswIndex`
   migration is missing this and is a latent bug — do not copy-paste from it.

2. **Partial-index predicates must mirror the handler's WHERE shape exactly** (literal `IN`, not `OR`
   chains, not parameters). A single source of truth (`private static readonly int[] ActiveStates`)
   keeps the handler and the migration in lockstep.

## Validation
- `EXPLAIN (FORMAT JSON)` for the summary query must contain the index name and must not contain
  `"Node Type": "Seq Scan"`.
- Integration test `Handle_QueryPlan_DoesNotUseSeqScan` in
  `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs`
  enforces this in CI.

## Related files
- Migration: `backend/src/Anela.Heblo.Persistence/Migrations/*_AddPartialIndexForActiveStockUpOperations.cs`
- Handler: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs`
- Investigation: `docs/investigations/stockupoperations-summary-slow-query.md`
