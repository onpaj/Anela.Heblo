# Specification: Optimize StockUpOperations GetSummary Endpoint Performance

## Summary
The `GET /api/StockUpOperations/summary` endpoint exhibited a 12.3-second response time in production telemetry, despite operating on a table with relevant indexes. This specification covers the investigation, root-cause confirmation, and remediation work needed to bring response time consistently below the agreed performance target and to prevent recurrence as the `StockUpOperations` table grows.

## Background
`GetStockUpOperationsSummaryHandler` (`GetStockUpOperationsSummaryHandler.cs:21`) returns a count grouped by `State` for active operations (`Pending`, `Submitted`, `Failed`). The query is backed by `IX_StockUpOperations_State` and a composite `(State, CreatedAt)` index defined in `StockUpOperationConfiguration.cs:57`. The endpoint is consumed by dashboard/UI flows that surface manufacture-queue health, so it is called frequently and its latency is user-visible.

Application Insights detected a single 12,261 ms invocation in the last 24 hours. Although a single sample, the magnitude (≈1000× expected) indicates a real degradation pathway — most likely one of:
1. PostgreSQL choosing a sequential scan over the index because active states are a small fraction of an otherwise large, mostly-`Completed` table.
2. Index bloat caused by frequent `State` updates as operations move through their lifecycle.
3. Read latency caused by lock contention with concurrent writes during manufacture activity.
4. A transient event (cold start, connection pool warm-up, autovacuum, checkpoint).

We need to confirm which mechanism is responsible, fix it, and ensure the endpoint is robust to continued table growth.

## Functional Requirements

### FR-1: Reproduce and characterize the slow query
Capture the production state of `StockUpOperations` and the query plan before changing anything.

**Acceptance criteria:**
- Row count and state distribution are recorded: `SELECT state, COUNT(*) FROM "StockUpOperations" GROUP BY state`.
- `EXPLAIN (ANALYZE, BUFFERS)` output is captured for the exact query the handler issues, including parameter values matching the active-state filter.
- Dead-tuple ratio and last (auto)vacuum/analyze timestamps are captured from `pg_stat_user_tables` for `StockUpOperations`.
- Index health is captured: size of `IX_StockUpOperations_State` and the `(State, CreatedAt)` index from `pg_relation_size` / `pgstattuple` (if available).
- Findings are written to `docs/integrations/` or `memory/gotchas/` so the diagnosis is preserved.

### FR-2: Restore baseline query performance
Apply the smallest set of changes that brings the query plan back to an index scan and the latency back under target.

**Acceptance criteria:**
- After remediation, `EXPLAIN ANALYZE` shows an index-driven plan (Index Scan / Index Only Scan / Bitmap Index Scan) for the active-state filter, with no Seq Scan on `StockUpOperations`.
- p95 latency for `GET /api/StockUpOperations/summary` measured over a representative window in App Insights is below the NFR-1 target.
- The single observed 12 s spike does not recur for at least 7 days of normal traffic after deployment.

### FR-3: Add a partial index for active-state lookups (if confirmed beneficial)
If FR-1 confirms that active states are a small minority of rows and the planner is rejecting the existing index, introduce a partial index covering only the active states.

**Acceptance criteria:**
- A new EF Core migration adds a partial index equivalent to `CREATE INDEX IX_StockUpOperations_State_Active ON "StockUpOperations" (state) WHERE state IN (0, 1, 2)` (Pending, Submitted, Failed — exact integer values must match the `StockUpOperationState` enum).
- The migration is reversible (`Down` drops the index).
- The handler query continues to work without source code changes (the partial index is selected automatically by the planner because the `WHERE` clause matches), OR the handler is updated to a form the planner can match against the partial index — whichever produces the index plan in `EXPLAIN ANALYZE`.
- The pre-existing `IX_StockUpOperations_State` and composite `(State, CreatedAt)` indexes are evaluated: keep them only if other queries depend on them, otherwise drop them in the same migration to avoid write amplification.
- Migration is documented as **manual** in line with project conventions (database migrations are not automated in deployment).

### FR-4: Address index/table bloat (if confirmed)
If FR-1 shows significant bloat or stale statistics, run remediation and confirm improvement.

**Acceptance criteria:**
- `VACUUM (ANALYZE) "StockUpOperations"` is run against the affected database (production and any other environments showing bloat).
- Post-vacuum dead-tuple ratio is recorded and shows clear improvement.
- If autovacuum tuning is the root cause, table-level autovacuum settings (`autovacuum_vacuum_scale_factor`, `autovacuum_analyze_scale_factor`) are adjusted via a migration or DBA runbook entry, and the change is documented.

### FR-5: Endpoint behavior remains unchanged
Performance work must not alter the contract or semantics of the endpoint.

**Acceptance criteria:**
- Response shape, status codes, and authorization requirements of `GET /api/StockUpOperations/summary` are identical before and after the change.
- The set of states counted (`Pending`, `Submitted`, `Failed`) is unchanged.
- States with zero matching rows are represented in the response in the same way as today (no new omissions or additions).
- Existing automated tests covering the handler pass without modification, except where assertions reflect non-functional improvements.

### FR-6: Regression coverage
Lock in the fix with tests that would catch a regression to a sequential-scan plan or to a wrong result.

**Acceptance criteria:**
- An integration test (against the real test database) seeds a representative mix of states and asserts the handler returns the correct counts for `Pending`, `Submitted`, and `Failed`.
- A unit test asserts that only the three active states are filtered and counted (no off-by-one against the enum values).
- All tests pass via `dotnet build` + `dotnet test` for affected projects.

### FR-7: Observability
Make future regressions visible without waiting for a 12-second spike.

**Acceptance criteria:**
- The handler emits a structured log or metric with the elapsed time of the database query (or the existing telemetry already captures this — verify and document).
- A note is added to `memory/gotchas/` describing the symptom, root cause, and the fix, so future investigations have a starting point.

## Non-Functional Requirements

### NFR-1: Performance
- p50 latency of `GET /api/StockUpOperations/summary` ≤ 100 ms under normal production load.
- p95 latency ≤ 300 ms.
- p99 latency ≤ 1 s. The previously observed 12 s outlier must not recur under normal load (excluding genuine cold starts and infrastructure incidents).
- The fix must remain valid as the `StockUpOperations` table grows by at least 10× from its current row count without re-tuning.

### NFR-2: Security
- No change to authentication or authorization on the endpoint; existing policies are preserved.
- Diagnostic SQL (state-distribution query, `EXPLAIN ANALYZE`) is run by an authorized operator against production with read-level credentials. Output captured for analysis must not include row-level operation data — only counts and plans.
- Migrations are reviewed before being applied to production, in keeping with the manual-migration policy.

### NFR-3: Reliability and operational safety
- The migration that adds (or drops) indexes must use `CREATE INDEX CONCURRENTLY` / `DROP INDEX CONCURRENTLY` so that production writes are not blocked. EF Core migrations should emit raw SQL for this (`migrationBuilder.Sql("CREATE INDEX CONCURRENTLY ...")`) since EF's default `CreateIndex` does not use `CONCURRENTLY`.
- Index creation must be idempotent or guarded so re-running the migration in any environment does not fail.
- `VACUUM` operations should be run during a low-traffic window if the operator has discretion.

### NFR-4: Maintainability
- Any new index is named per existing convention (`IX_StockUpOperations_*`).
- The reason for the partial index (active-state-only filter) is documented in a one-line comment above the migration's `Up` method.

## Data Model
No schema changes to entity types. The change is limited to PostgreSQL-level indexes on the existing `StockUpOperations` table.

Relevant entity surface (unchanged):
- `StockUpOperation` with column `State` of type `StockUpOperationState` (enum stored as integer).
- `StockUpOperationState` values referenced by this query: `Pending` (0), `Submitted` (1), `Failed` (2). The remaining states (e.g., `Completed`) are excluded by the active-state filter.

Index inventory after this work (target state, subject to FR-1 findings):
- Keep: composite `(State, CreatedAt)` index — used by listing/sorting queries elsewhere (verify before removing).
- Add: `IX_StockUpOperations_State_Active` — partial index on `(State)` where `state IN (0, 1, 2)`.
- Possibly drop: `IX_StockUpOperations_State` — redundant if the partial index plus the composite covers all access patterns. Drop only after grepping for queries that filter on `State` without composite ordering.

## API / Interface Design
No API surface changes.

- **Endpoint:** `GET /api/StockUpOperations/summary` (unchanged).
- **Request:** No parameters (unchanged).
- **Response:** Same DTO, same field names, same shape (unchanged).
- **Auth:** Unchanged.

## Dependencies
- PostgreSQL (production database).
- EF Core migrations infrastructure (existing).
- Application Insights for verifying post-deploy latency.
- Access to a production-like dataset (or production read replica) to run `EXPLAIN ANALYZE` and confirm the diagnosis.
- Operator access to run `VACUUM ANALYZE` if bloat is the cause.

## Out of Scope
- Caching the summary response in memory or via a distributed cache. Considered but explicitly deferred — the underlying query should be fast on its own.
- Materialized views or denormalized counters for `StockUpOperations`.
- Broader review of other handlers in the StockUpOperations module.
- Changing the set of states considered "active" by the summary endpoint.
- Automating database migrations in the deployment pipeline (project policy keeps these manual).
- Front-end changes to the consuming dashboard.

## Open Questions
None.

## Status: COMPLETE