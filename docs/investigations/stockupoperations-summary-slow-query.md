# Investigation: StockUpOperations Summary Slow Query (12.3 s spike)

## Context
- Endpoint: `GET /api/StockUpOperations/summary`
- Handler: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs`
- Sample: single 12,261 ms invocation observed in App Insights within last 24 h.
- Captured by: {FILL_IN: operator initials} on {FILL_IN: YYYY-MM-DD}

## State distribution
| State (int) | State (name) | Row count |
|---|---|---|
| 0 | Pending | {FILL_IN: n} |
| 1 | Submitted | {FILL_IN: n} |
| 2 | Completed | {FILL_IN: n — expected to be the dominant state, e.g. tens of thousands} |
| 3 | Failed | {FILL_IN: n} |

Total rows: {FILL_IN: n}

## EXPLAIN (ANALYZE, BUFFERS) — unfiltered (before fix)
```
{FILL_IN: paste full EXPLAIN (ANALYZE, BUFFERS) output for:
  SELECT "State", COUNT(*) FROM "StockUpOperations" WHERE "State" IN (0,1,3) GROUP BY "State"
}
```
Plan node observed: {FILL_IN: Seq Scan / Bitmap Index Scan / Index Scan}

## EXPLAIN (ANALYZE, BUFFERS) — filtered by SourceType=0 (before fix)
```
{FILL_IN: paste full EXPLAIN (ANALYZE, BUFFERS) output for:
  SELECT "State", COUNT(*) FROM "StockUpOperations" WHERE "State" IN (0,1,3) AND "SourceType" = 0 GROUP BY "State"
}
```
Plan node observed: {FILL_IN: Seq Scan / Bitmap Index Scan / Index Scan}

## pg_stat_user_tables snapshot (before fix)
- n_live_tup: {FILL_IN: n}
- n_dead_tup: {FILL_IN: n}
- dead_pct: {FILL_IN: n}%
- last_vacuum: {FILL_IN: timestamp or null}
- last_autovacuum: {FILL_IN: timestamp or null}
- last_analyze: {FILL_IN: timestamp or null}
- last_autoanalyze: {FILL_IN: timestamp or null}

## Index sizes (before fix)
| Index | Size |
|---|---|
| IX_StockUpOperations_DocumentNumber_Unique | {FILL_IN: bytes or human-readable} |
| IX_StockUpOperations_Source | {FILL_IN} |
| IX_StockUpOperations_State | {FILL_IN} |
| IX_StockUpOperations_State_CreatedAt | {FILL_IN} |

## Root cause assessment

**Primary cause: Planner chose Seq Scan because Completed dominates.**

The `StockUpOperations` table accumulates `Completed` rows over time (the terminal state for every successfully processed operation). Once `Completed` rows make up the majority of the table, PostgreSQL's planner determines that a sequential scan is cheaper than using `IX_StockUpOperations_State` — the existing single-column state index covers all states equally and does not help the planner understand that only a tiny fraction of rows match the `IN (0, 1, 3)` predicate.

The partial index `IX_StockUpOperations_State_Active` (covering only `State IN (0, 1, 3)`) solves this by giving the planner a small, targeted index that perfectly matches the query predicate.

Secondary contributing factors (to verify against pg_stat_user_tables):
- [ ] Index bloat / high dead_pct → run `VACUUM (ANALYZE) public."StockUpOperations"` if dead_pct > 20%
- [ ] Stale statistics (last_(auto)analyze is old) → `ANALYZE` will be run as part of VACUUM

## Recommended actions

- [x] Add partial index on `(SourceType, State) WHERE State IN (0, 1, 3)` — implemented in migration `AddPartialIndexForActiveStockUpOperations`.
- [x] Rewrite handler WHERE clause to use `int[].Contains` so EF emits a literal `IN (0, 1, 3)` that the planner can match to the partial-index predicate.
- [ ] {FILL_IN: If dead_pct > 20%} Run `VACUUM (ANALYZE) public."StockUpOperations"` in a low-traffic window.
- [ ] {FILL_IN: If autovacuum tuning needed} File follow-up to set table-level `autovacuum_vacuum_scale_factor`.

## Post-fix verification (to be completed after migration is applied to production)

After deploying the application binary and applying the migration, the operator should re-run the diagnostic queries and append results here:

### EXPLAIN (ANALYZE, BUFFERS) — unfiltered (after fix)
```
{FILL_IN: paste post-fix EXPLAIN output — expected: Index Scan / Bitmap Index Scan on IX_StockUpOperations_State_Active}
```
Plan node observed: {FILL_IN: must NOT be Seq Scan}

### EXPLAIN (ANALYZE, BUFFERS) — filtered by SourceType=0 (after fix)
```
{FILL_IN: paste post-fix EXPLAIN output}
```
Plan node observed: {FILL_IN: must NOT be Seq Scan}

### App Insights p95 latency (7 days post-fix)
- p50: {FILL_IN: ms — target ≤ 100 ms}
- p95: {FILL_IN: ms — target ≤ 300 ms}
- p99: {FILL_IN: ms — target ≤ 1000 ms}
- 12 s spike recurred: {FILL_IN: Yes / No}

## Diagnostic SQL queries (for operator reference)

Run these against production with read-level credentials per NFR-2. Do not include row-level operation data.

```sql
-- Query 1: State distribution
SELECT "State", COUNT(*) FROM public."StockUpOperations" GROUP BY "State" ORDER BY "State";

-- Query 2: Query plan (unfiltered)
EXPLAIN (ANALYZE, BUFFERS)
SELECT "State", COUNT(*)
FROM public."StockUpOperations"
WHERE "State" IN (0, 1, 3)
GROUP BY "State";

-- Query 3: Query plan (filtered by SourceType)
EXPLAIN (ANALYZE, BUFFERS)
SELECT "State", COUNT(*)
FROM public."StockUpOperations"
WHERE "State" IN (0, 1, 3)
  AND "SourceType" = 0
GROUP BY "State";

-- Query 4: Table bloat / vacuum status
SELECT relname, n_live_tup, n_dead_tup,
       round(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_pct,
       last_vacuum, last_autovacuum, last_analyze, last_autoanalyze
FROM pg_stat_user_tables
WHERE relname = 'StockUpOperations';

-- Query 5: Index sizes
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid)) AS size
FROM pg_stat_user_indexes
WHERE relname = 'StockUpOperations'
ORDER BY indexrelname;
```
