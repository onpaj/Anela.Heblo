# Specification: Restore `GET StockUpOperations/GetSummary` Endpoint Performance

## Summary
The `GET /api/StockUpOperations/summary` endpoint recorded a 13,215 ms invocation in the last 24 hours, exceeding the 10,000 ms alert threshold. A partial-index fix was previously implemented in branch `feat-performance-slow-response-times-on-get-s` (migration `20260506145627_AddPartialIndexForActiveStockUpOperations` + handler rewrite to literal `IN (0,1,3)`); this spec validates that the prior fix is live in production, diagnoses why a 13 s spike still occurred, and prescribes corrective action.

## Background
The endpoint returns counts of `StockUpOperations` rows grouped by state (Pending=0, Submitted=1, Failed=3 — Completed=2 is the high-volume terminal state and is intentionally excluded). It is invoked by dashboards and indicator components (`StockUpOperationStatusIndicator`, `StockOperationsPage`) and must respond quickly enough to feel instant in the UI.

Prior investigation (`docs/investigations/stockupoperations-summary-slow-query.md`, `docs/superpowers/plans/2026-05-06-stockupoperations-summary-performance.md`) identified the root cause as the PostgreSQL planner choosing a Seq Scan over the `StockUpOperations` table because Completed rows dominate and the existing `IX_StockUpOperations_State` index is not selective enough. The fix added a partial index `IX_StockUpOperations_State_Active ON (SourceType, State) WHERE State IN (0,1,3)` and rewrote the handler to emit a literal `IN (0,1,3)` so the predicate matches the index filter.

The new alert (avg/max 13,215 ms, single occurrence in 24 h) indicates that either (a) the prior fix has not yet reached production, (b) the planner is occasionally bypassing the partial index, or (c) a secondary cause (lock contention, autovacuum/checkpoint, cold start) is producing isolated long-tail latency. The fix is already committed on the current branch but the placeholder fields in `docs/investigations/stockupoperations-summary-slow-query.md` are still filled with `{FILL_IN: …}` markers, indicating production diagnostics have not yet been captured for this branch.

## Functional Requirements

### FR-1: Confirm deployment state of the prior fix in production
Before re-investigating, verify whether the partial-index migration is actually present in the production database.

**Acceptance criteria:**
- `SELECT indexname FROM pg_indexes WHERE tablename = 'StockUpOperations' AND indexname = 'IX_StockUpOperations_State_Active';` returns one row in production.
- `SELECT * FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%AddPartialIndexForActiveStockUpOperations';` returns one row in production.
- The currently deployed application binary contains the handler version that uses `ActiveStates.Contains((int)x.State)` (verifiable via Git SHA on the running container).
- If any of the above checks fail, FR-2 onwards are deferred until the migration and binary are deployed; the slow-response alert is treated as expected pre-deploy behavior.

### FR-2: Capture fresh production diagnostics for the new alert
The existing investigation document still has `{FILL_IN: …}` placeholders. Re-run all five diagnostic queries against production for the current alert and overwrite the document with real values.

**Acceptance criteria:**
- `docs/investigations/stockupoperations-summary-slow-query.md` contains no `{FILL_IN: …}` placeholders after this task.
- Document includes both pre-fix (if the fix is not yet deployed) and post-fix EXPLAIN plans, and the date/operator of capture.
- The state-distribution table is populated with actual row counts, including `Completed` count (the dominant row to confirm hypothesis).
- `pg_stat_user_tables` snapshot includes `dead_pct`, last_vacuum/autovacuum/analyze/autoanalyze timestamps.
- Index sizes are recorded for all five indexes (including `IX_StockUpOperations_State_Active` if present).
- Plan node observed for both unfiltered and `SourceType`-filtered variants is recorded (Seq Scan vs Bitmap Index Scan vs Index Scan vs Index Only Scan).

### FR-3: Determine root cause of the 13.2 s spike
Based on the FR-2 diagnostics, classify the spike against one of these mutually exclusive categories and document the verdict in the investigation file.

**Acceptance criteria:**
- One of the following is selected as the primary cause, with a 2–3 sentence justification:
  - (a) Partial index is missing or the planner is not using it (plan still shows Seq Scan).
  - (b) Partial index is used but autovacuum / checkpoint / lock contention caused the one-off spike (plan looks correct on re-measurement, system metrics show concurrent activity at the spike timestamp).
  - (c) Statistics are stale and the planner mis-estimates row counts (last_analyze older than ~24 h with significant churn).
  - (d) Cold start / connection pool warm-up after a Web App restart (App Insights shows an app restart event within ~1 minute before the slow call).
- The verdict drives the action plan in FR-4.

### FR-4: Apply the smallest corrective action that resolves the verdict
Based on the FR-3 verdict, the team executes exactly one of the corrective paths below.

**Acceptance criteria:**
- If (a): the migration is deployed and FR-5 verification passes. If the migration is already deployed and the plan still skips the index, file a follow-up to either (i) `DROP INDEX IX_StockUpOperations_State` to force the planner toward the partial index, (ii) raise `default_statistics_target` for the `State` column, or (iii) introduce a covering INCLUDE clause. Pick the lowest-risk option and document why.
- If (b): no code change; the spike is accepted as a transient and the engineer files a short note in `memory/gotchas/postgres-partial-index-active-states.md` describing the transient class observed. If recurrences exceed 1/week, the alert threshold is re-evaluated separately (out of scope here).
- If (c): run `VACUUM (ANALYZE) public."StockUpOperations"` in a low-traffic window and append the new `pg_stat_user_tables` snapshot to the investigation. If recurrent, file a follow-up to configure table-level `autovacuum_analyze_scale_factor`.
- If (d): no schema/code change; document in the investigation, and file a separate ticket for application warm-up improvements (out of scope here).

### FR-5: Verify the endpoint meets latency baseline after corrective action
Whichever path FR-4 selects, the endpoint must demonstrably meet the latency targets in NFR-1 for at least 7 days.

**Acceptance criteria:**
- Post-fix EXPLAIN plan for both unfiltered and `?sourceType=` variants shows `Index Scan` / `Index Only Scan` / `Bitmap Index Scan` referencing `IX_StockUpOperations_State_Active` and does **not** show `Seq Scan`.
- 7-day App Insights p95 latency for `GET /api/StockUpOperations/summary` ≤ 300 ms.
- 7-day p99 latency ≤ 1,000 ms.
- Zero occurrences of >10,000 ms during the 7-day window.
- Post-fix observations appended to `docs/investigations/stockupoperations-summary-slow-query.md` under a `## Captured: post-fix {YYYY-MM-DD}` heading.

### FR-6: Maintain regression coverage
The existing `Handle_QueryPlan_DoesNotUseSeqScan` integration test (`backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs`) must continue to pass and remain unskipped in CI.

**Acceptance criteria:**
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetStockUpOperationsSummaryIntegrationTests"` returns 4 passing tests, none skipped.
- If FR-4 drops `IX_StockUpOperations_State` (option a.i), the integration test fixture is updated accordingly and a new assertion confirms the partial index is still selected even when the catch-all index is absent.
- If FR-4 changes the handler in any way, unit tests in `GetStockUpOperationsSummaryHandlerTests` are updated to keep `Handle_CompletedState_IsExcludedAndFailedIsIncluded` green.

### FR-7: Observability — emit and verify the timing log
The handler already emits a structured information log with elapsed ms and per-state counts. Confirm that App Insights captures this log so that future spikes can be triaged without re-running diagnostic SQL.

**Acceptance criteria:**
- The query `traces | where message contains "GetStockUpOperationsSummary completed in"` in App Insights returns recent entries with `ElapsedMs`, `SourceType`, `PendingCount`, `SubmittedCount`, `FailedCount` custom dimensions populated.
- If the custom dimensions are not surfaced, adjust logger configuration (Application Insights `EnableInternalLogging` or scope enrichment) so that they are queryable. No change to the log message itself.

## Non-Functional Requirements

### NFR-1: Performance
- p50 ≤ 100 ms
- p95 ≤ 300 ms
- p99 ≤ 1,000 ms
- Zero invocations >10,000 ms over any rolling 7-day window
- Targets measured against App Insights `requests` for `GET /api/StockUpOperations/summary`

### NFR-2: Security
- No change to authentication/authorization surface. `[Authorize]` remains on `StockUpOperationsController`.
- Production diagnostic queries must use a read-only role; no row-level operation data (DocumentNumber, ProductCode, etc.) may be captured in the investigation document — only aggregate counts, EXPLAIN output, and system catalog rows.
- Error responses must not expose raw exception messages. The existing handler returns `"An unexpected error occurred."` in the public error dictionary while logging full exception detail server-side; preserve this behavior.

### NFR-3: Reliability
- Any new index migration uses `migrationBuilder.Sql(..., suppressTransaction: true)` with `CREATE INDEX CONCURRENTLY IF NOT EXISTS` (and `DROP INDEX CONCURRENTLY IF EXISTS` in `Down`) to avoid blocking writes and to be re-runnable.
- Migrations must be deployable while the application is serving traffic; no schema changes that lock the table for more than ~1 s.
- `dotnet ef database update` forward and backward across the new migration must complete without error and without leaving any `INVALID` index.

### NFR-4: Maintainability
- Single source of truth for the active-state set: `private static readonly int[] ActiveStates` in the handler, computed via `(int)StockUpOperationState.X` casts. Partial-index SQL contains the literal integers `(0,1,3)` next to a comment naming each enum member.
- Any new migration follows the existing naming convention `{timestamp}_AddXxx.cs` and includes a comment block in `Up` explaining the `suppressTransaction: true` rationale.
- Updates to `memory/gotchas/postgres-partial-index-active-states.md` record any new findings so the next responder doesn't repeat the investigation.

### NFR-5: Operability
- Manual migrations are documented per project convention. The PR description (if any new migration is introduced) must call out the manual deployment step and any rollback procedure.
- Investigation document is the canonical artifact for post-incident review; it must be updated even when no code change is required (transient verdict).

## Data Model

No schema changes to existing entities. Only an additional index on the existing `StockUpOperations` table:

- `StockUpOperations` (existing)
  - PK: `Id` (int)
  - Indexed columns: `DocumentNumber` (unique), `(SourceType, SourceId)`, `State`, `(State, CreatedAt)`, **and** the partial index `(SourceType, State) WHERE State IN (0,1,3)` named `IX_StockUpOperations_State_Active`.
  - Enum `StockUpOperationState`: `Pending=0`, `Submitted=1`, `Completed=2`, `Failed=3`.
  - Enum `StockUpSourceType`: `TransportBox`, `GiftPackageManufacture`.
  - The dominant state by row count is `Completed`. The active states queried by the summary endpoint (`Pending`, `Submitted`, `Failed`) together constitute a small minority of rows — this is what makes the partial index efficient.

If FR-4 selects option (a.i), `IX_StockUpOperations_State` is dropped in a follow-up migration. No other index changes.

## API / Interface Design

No changes to the public API surface. For reference:

- `GET /api/StockUpOperations/summary?sourceType={StockUpSourceType?}`
  - Auth: required (`[Authorize]`)
  - Response: `GetStockUpOperationsSummaryResponse { PendingCount: int, SubmittedCount: int, FailedCount: int, TotalInQueue: int, Success: bool, ... }`
  - Behavior: returns aggregated counts for active states only. `Completed` is never counted.
  - Optional `sourceType` filter narrows results to one source.

No changes to other endpoints on `StockUpOperationsController`.

## Dependencies
- PostgreSQL 16 (production) — supports `CREATE INDEX CONCURRENTLY` and partial indexes.
- EF Core 8 (Npgsql provider) — must honor `suppressTransaction: true`.
- Azure Application Insights — source for the latency alert and post-fix verification.
- Azure Web App for Containers — deployment surface.
- Existing test infrastructure: xUnit, Testcontainers.PostgreSql (used by `GetStockUpOperationsSummaryIntegrationTests`).
- `gh` CLI for any PR follow-up (per project convention).

No new external services, libraries, or features are added.

## Out of Scope
- Frontend changes to `StockUpOperationStatusIndicator`, `StockOperationsPage`, or any consumer of the summary endpoint.
- Caching or pagination of the summary response (it returns three integers; neither applies meaningfully).
- Re-architecting the `StockUpOperation` state machine or splitting active vs. terminal storage.
- Refactoring `GetStockUpOperationsHandler` (the listing handler) — that is a separate endpoint with its own performance profile.
- Tuning App Insights alert thresholds (separate ops ticket if recurrences are transient).
- Application warm-up / connection pool sizing changes (separate ticket if FR-3 verdict is cold-start).
- Dropping `IX_StockUpOperations_State` — explicitly deferred per the prior plan; only revisited if FR-4 selects option (a.i).
- Auto-running EF migrations on deploy — manual migration policy remains unchanged per `CLAUDE.md`.

## Open Questions
None.

## Status: COMPLETE