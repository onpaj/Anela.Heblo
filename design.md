# Design: Optimize StockUpOperations GetSummary Endpoint Performance

## Component Design

### `GetStockUpOperationsSummaryHandler` — modified

**File:** `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs`

**Changes:**

1. Add `ILogger<GetStockUpOperationsSummaryHandler>` constructor parameter.

2. Define a private constant for the active-state filter so the handler and the partial-index predicate share a single source of truth:

```csharp
private static readonly int[] ActiveStates =
{
    (int)StockUpOperationState.Pending,   // 0
    (int)StockUpOperationState.Submitted, // 1
    (int)StockUpOperationState.Failed     // 3
};
```

3. Replace the three chained `||` comparisons with `ActiveStates.Contains((int)x.State)`. EF Core translates `int[].Contains(...)` to a literal `IN (0, 1, 3)` in SQL, giving the planner the proof it needs to match the partial-index predicate. The chained `OR` variant with enum-typed parameters can emit `state = $1 OR state = $2 OR state = $3` with opaque parameters, which the planner may not recognize as implying the index predicate.

4. Wrap `ToListAsync` in a `Stopwatch` and emit a structured log entry after the query completes (see Telemetry Log Entry under Data Schemas).

**Resulting query shape (EF → SQL):**

```sql
SELECT "State", COUNT(*)
FROM "StockUpOperations"
WHERE "State" IN (0, 1, 3)
  [AND "SourceType" = $1]   -- only when request.SourceType is provided
GROUP BY "State"
```

**Interface unchanged:** `IRequestHandler<GetStockUpOperationsSummaryRequest, GetStockUpOperationsSummaryResponse>` — no signature or response-shape change.

---

### `StockUpOperationConfiguration` — modified

**File:** `backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs`

Add one `HasIndex` declaration so the EF model snapshot stays in sync with the database. EF will scaffold a `CreateIndex` call in the generated migration; that body must be replaced with raw SQL before the migration is applied (see migration below).

```csharp
builder.HasIndex(x => new { x.SourceType, x.State })
    .HasDatabaseName("IX_StockUpOperations_State_Active")
    .HasFilter("\"State\" IN (0, 1, 3)");
```

**Existing indexes — disposition:**

| Index | Action |
|---|---|
| `IX_StockUpOperations_DocumentNumber_Unique` | Keep — uniqueness constraint |
| `IX_StockUpOperations_Source` | Keep — used by `GetBySourceAsync` |
| `IX_StockUpOperations_State_CreatedAt` | Keep — used by listing queries with `ORDER BY CreatedAt` |
| `IX_StockUpOperations_State` | Keep in this PR; evaluate dropping in a follow-up migration after one deploy cycle of post-fix telemetry confirms no other query path depends on it |

---

### `AddPartialIndexForActiveStockUpOperations` migration — new

**File:** `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddPartialIndexForActiveStockUpOperations.cs`

EF scaffolding produces `CreateIndex` / `DropIndex` calls. Replace both bodies with raw SQL using `suppressTransaction: true`, because PostgreSQL rejects `CREATE INDEX CONCURRENTLY` inside a transaction block (`SQLSTATE 25001`). EF Core wraps each migration's `Up` in a transaction by default; passing `suppressTransaction: true` to `MigrationBuilder.Sql()` causes the runner to commit first.

**`Up` method:**

```csharp
// Active states: Pending=0, Submitted=1, Failed=3. Completed=2 is excluded intentionally.
// suppressTransaction required: PostgreSQL rejects CONCURRENTLY inside a transaction (SQLSTATE 25001).
// IF NOT EXISTS makes this idempotent — safe to re-run in any environment.
migrationBuilder.Sql(
    """
    CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_StockUpOperations_State_Active"
        ON "StockUpOperations" ("SourceType", "State")
        WHERE "State" IN (0, 1, 3);
    """,
    suppressTransaction: true);
```

**`Down` method:**

```csharp
migrationBuilder.Sql(
    """
    DROP INDEX CONCURRENTLY IF EXISTS "IX_StockUpOperations_State_Active";
    """,
    suppressTransaction: true);
```

**`IF NOT EXISTS` / `IF EXISTS`** makes both directions idempotent (NFR-3). If index creation fails partway and leaves an `INVALID` index, drop it with `DROP INDEX CONCURRENTLY "IX_StockUpOperations_State_Active"` before retrying.

**Deployment order:** deploy the application binary first (the `Contains`-based query is correct without the index, just slower); apply the migration in a subsequent low-traffic window. No readiness gate is required.

---

### `GetStockUpOperationsSummaryHandlerTests` — modified

**File:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryHandlerTests.cs`

Add one test to lock in the correct enum-to-integer mapping and catch a regression if `ActiveStates` is updated incorrectly:

```
Handle_CompletedState_IsExcludedFromCounts
  — seeds one operation per state (Pending, Submitted, Completed, Failed) with no SourceType filter
  — asserts PendingCount=1, SubmittedCount=1, FailedCount=1, and that Completed is not reflected
    in any count field (total PendingCount + SubmittedCount + FailedCount = 3, not 4)
```

The existing three tests continue to serve as behavioral coverage and require no modification.

---

### `GetStockUpOperationsSummaryIntegrationTests` — new

**File:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs`

Integration test against the real PostgreSQL test database (Testcontainers or the project's fixture DB). The existing `MockQueryable`-based unit tests execute against an in-memory list and cannot detect SQL translation or index-use regressions.

| Test | Seeds | Asserts |
|---|---|---|
| `Handle_MixedStates_ReturnsCorrectCounts` | 2 Pending, 1 Submitted, 1 Failed, 2 Completed | PendingCount=2, SubmittedCount=1, FailedCount=1; Success=true |
| `Handle_WithSourceTypeFilter_CountsOnlyMatchingSource` | 1 Pending (GiftPackageManufacture), 1 Pending (TransportBox) | PendingCount=1, SubmittedCount=0, FailedCount=0 when sourceType=GiftPackageManufacture |
| `Handle_NoActiveOperations_ReturnsZeroCounts` | 3 Completed only | PendingCount=0, SubmittedCount=0, FailedCount=0; Success=true |

After applying the migration, optionally assert that `EXPLAIN (FORMAT JSON)` for the query does not contain a `Seq Scan` node on `StockUpOperations`.

---

### Diagnostic investigation document — new

**File:** `docs/investigations/stockupoperations-summary-slow-query.md`

Captures FR-1 output before any code is changed. Structure:

```markdown
## Captured: {date}

### State distribution
SELECT state, COUNT(*) FROM "StockUpOperations" GROUP BY state;

### EXPLAIN (ANALYZE, BUFFERS) output
{paste full plan text}

### pg_stat_user_tables snapshot
n_live_tup: , n_dead_tup: , dead_ratio: %
last_vacuum: , last_autovacuum: , last_analyze: , last_autoanalyze:

### Index sizes
IX_StockUpOperations_State: {bytes}
IX_StockUpOperations_State_CreatedAt: {bytes}

### Root cause assessment
{seq scan due to planner statistics / index bloat / lock contention / transient event}
```

---

### Gotcha document — new

**File:** `memory/gotchas/postgres-partial-index-active-states.md`

Records the symptom (12 s spike on summary endpoint), the confirmed root cause, the fix (partial index on `(SourceType, State)` + `Contains` query rewrite), and the `suppressTransaction: true` requirement for any `CONCURRENTLY` migration in this codebase.

---

## Data Schemas

### PostgreSQL index inventory (target state after migration)

```sql
-- NEW — partial index; covers both filtered (?sourceType=...) and unfiltered summary queries
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_StockUpOperations_State_Active"
    ON "StockUpOperations" ("SourceType", "State")
    WHERE "State" IN (0, 1, 3);
-- Pending=0, Submitted=1, Failed=3  (Completed=2 excluded — the high-volume terminal state)

-- KEPT unchanged
-- IX_StockUpOperations_DocumentNumber_Unique   UNIQUE ON ("DocumentNumber")
-- IX_StockUpOperations_Source                  ON ("SourceType", "SourceId")
-- IX_StockUpOperations_State_CreatedAt         ON ("State", "CreatedAt")

-- DEFERRED — evaluate dropping IX_StockUpOperations_State after confirming
-- no other query path depends on it (grep for queries filtering on State alone)
```

### API shape (unchanged)

**Request**

```
GET /api/StockUpOperations/summary?sourceType={StockUpSourceType}
```

| Parameter | Type | Required |
|---|---|---|
| `sourceType` | `StockUpSourceType` (query string) | No |

**Response — `GetStockUpOperationsSummaryResponse`**

| Field | Type | Notes |
|---|---|---|
| `pendingCount` | `int` | 0 when no matching rows |
| `submittedCount` | `int` | 0 when no matching rows |
| `failedCount` | `int` | 0 when no matching rows |
| `totalInQueue` | `int` | Computed: `pendingCount + submittedCount` |
| `success` | `bool` | `false` with error fields on exception |

### Telemetry log entry (new)

Emitted by the handler after each successful database call at `LogLevel.Information`:

```
"GetStockUpOperationsSummary completed in {ElapsedMs}ms
 [SourceType={SourceType}, Pending={PendingCount}, Submitted={SubmittedCount}, Failed={FailedCount}]"
```

Structured properties captured by Application Insights:

| Property | Type |
|---|---|
| `ElapsedMs` | `long` |
| `SourceType` | `string \| null` |
| `PendingCount` | `int` |
| `SubmittedCount` | `int` |
| `FailedCount` | `int` |

### `StockUpOperationState` enum (reference, unchanged)

| Member | Integer value | Included in partial index |
|---|---|---|
| `Pending` | 0 | Yes |
| `Submitted` | 1 | Yes |
| `Completed` | 2 | **No** |
| `Failed` | 3 | Yes |
