## Telemetry

App Insights detected `GET StockUpOperations/GetSummary` taking ~12.3 seconds (1 occurrence in last 24h):
- **Endpoint**: `GET /api/StockUpOperations/summary`
- **Avg duration**: 12,261 ms
- **Count**: 1

## Analysis

`GetStockUpOperationsSummaryHandler` (`GetStockUpOperationsSummaryHandler.cs:21`) runs a GROUP BY count query filtering on states `Pending`, `Submitted`, and `Failed`:

```csharp
var query = _repository.GetAll()
    .Where(x => x.State == Pending || x.State == Submitted || x.State == Failed);

var counts = await query
    .GroupBy(x => x.State)
    .Select(g => new { State = g.Key, Count = g.Count() })
    .ToListAsync(cancellationToken);
```

The `StockUpOperations` table has an index on `State` (`IX_StockUpOperations_State`, `StockUpOperationConfiguration.cs:57`) and a composite index on `(State, CreatedAt)`. In theory this query should be very fast.

However, a 12-second execution time could indicate:
1. **Table growth** — if the table has accumulated a large number of completed records, PostgreSQL may choose a sequential scan over the index if the active states (Pending/Submitted/Failed) are a small fraction
2. **Index bloat** — dead tuples from frequent updates to `State` may have caused index bloat
3. **Lock contention** — high write throughput on the table during manufacture operations could cause read latency
4. **Single occurrence** — may be a transient cold-start or connection pool event

## Suggested Actions

- Check the current row count and state distribution in `StockUpOperations`: run `SELECT state, COUNT(*) FROM "StockUpOperations" GROUP BY state`
- Run `EXPLAIN ANALYZE` on the query to verify the index is being used
- If table has grown large with mostly `Completed` records, consider:
  - Adding a partial index on `State` for active states only: `CREATE INDEX ... WHERE state IN (0, 1, 2)`
  - Running `VACUUM ANALYZE "StockUpOperations"` to update statistics and reclaim dead tuples
- Monitor for recurrence — if this is a one-off, no action may be needed