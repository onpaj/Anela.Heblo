# Architecture Review: Optimize StockUpOperations GetSummary Endpoint Performance

## Architectural Fit Assessment

The change fits cleanly into existing patterns (Vertical Slice + MediatR handler + EF Core repository + manual migrations). No new modules, services, or boundaries are introduced. The work is concentrated in three places: a single migration file, optionally the handler query shape, and the test project. The proposal aligns with two existing precedents in this codebase:

- `20260417120000_RebuildKnowledgeBaseHnswIndex.cs` already uses `migrationBuilder.Sql(...)` for non-default index DDL — the same mechanism is needed here for `CONCURRENTLY`.
- `20260415100000_AddPlannedDateIndexToManufactureOrders.cs` shows the standard naming and reversibility conventions for index-only migrations.

**Two critical mismatches with reality were found in `spec.r1.md` and must be amended before any code is written** (see Specification Amendments). These are not optional.

## Proposed Architecture

### Component Overview

```
HTTP GET /api/StockUpOperations/summary
        │
        ▼
StockUpOperationsController.GetSummary(sourceType?)         [unchanged]
        │  MediatR dispatch
        ▼
GetStockUpOperationsSummaryHandler                         [query shape may change]
        │  IStockUpOperationRepository.GetAll() → IQueryable
        ▼
EF Core LINQ → Npgsql SQL
        │
        ▼
PostgreSQL "StockUpOperations"
        ├── (existing) IX_StockUpOperations_DocumentNumber_Unique   — keep
        ├── (existing) IX_StockUpOperations_Source                  — keep (used by GetBySourceAsync)
        ├── (existing) IX_StockUpOperations_State                   — evaluate, likely drop
        ├── (existing) IX_StockUpOperations_State_CreatedAt         — keep (used by GetByStateAsync ORDER BY CreatedAt)
        └── (new)      IX_StockUpOperations_State_Active            — partial, predicate must match handler's WHERE
```

### Key Design Decisions

#### Decision 1: Partial index predicate construction
**Options considered:**
- A. Partial index on `(State)` filtered by the active enum values.
- B. Partial index on `(State, SourceType)` filtered by the active enum values, since the handler also accepts an optional `SourceType` filter.
- C. Computed column `IsActive` plus a regular index.

**Chosen approach:** B — partial index on `(SourceType, State) WHERE State IN (0, 1, 3)`.

**Rationale:** The handler (`GetStockUpOperationsSummaryHandler.cs:27-29`) applies an optional `SourceType` filter that the brief and spec missed. Indexing only `State` would force a heap re-check for every row when `SourceType` is supplied — typical for the dashboard. Leading with `SourceType` keeps the index useful for the unfiltered call (Postgres can scan all leaf pages of the partial index regardless of leading-column equality, since the partial predicate already restricts the row set to active states only). The grouping is on `State`, so trailing it preserves the GROUP BY pushdown.

#### Decision 2: Raw SQL migration vs EF `CreateIndex`
**Options considered:**
- A. `migrationBuilder.CreateIndex(...)` — EF generates standard DDL.
- B. `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY ... IF NOT EXISTS ...")`.

**Chosen approach:** B, with `suppressTransaction: true` on the `Sql()` call.

**Rationale:** EF Core wraps each migration's `Up` in a transaction by default; PostgreSQL rejects `CREATE INDEX CONCURRENTLY` inside a transaction block (`SQLSTATE 25001`). EF Core's `MigrationBuilder.Sql(string sql, bool suppressTransaction = false)` overload exists exactly for this case — passing `true` causes the migration runner to commit before running the statement. The HNSW index migration (`20260417120000_RebuildKnowledgeBaseHnswIndex.cs`) uses `migrationBuilder.Sql()` without `suppressTransaction:true`; that is technically a latent bug for that migration and must NOT be copy-pasted here. Use `IF NOT EXISTS` / `IF EXISTS` for idempotency per NFR-3.

#### Decision 3: Handler query stays parameter-free for predicate matching
**Options considered:**
- A. Leave handler as-is and rely on the planner to match `state IN (...)` to the partial index predicate.
- B. Rewrite the handler's `Where` to use `Contains` against an explicit `int[] activeStates = { 0, 1, 3 }` so EF emits a literal `IN (0, 1, 3)` matching the partial-index predicate exactly.

**Chosen approach:** B.

**Rationale:** Postgres only uses a partial index when the planner can prove the query's WHERE clause implies the index predicate. EF Core's translation of three chained `||` comparisons against an enum can emit `state = $1 OR state = $2 OR state = $3` with parameter placeholders — the planner will not always recognize this as implying `state IN (0, 1, 3)` because parameters are opaque at plan time (depending on plan caching mode). A `Contains` against a constant array translates more reliably to a literal `IN` and gives the planner the proof it needs. This must be confirmed via `EXPLAIN ANALYZE` per FR-2.

## Implementation Guidance

### Directory / Module Structure

Files to add or modify (no new directories):

```
backend/src/Anela.Heblo.Persistence/Migrations/
  YYYYMMDDhhmmss_AddPartialIndexForActiveStockUpOperations.cs        [NEW]
  YYYYMMDDhhmmss_AddPartialIndexForActiveStockUpOperations.Designer.cs [NEW, generated]
  ApplicationDbContextModelSnapshot.cs                                [REGENERATED by EF]

backend/src/Anela.Heblo.Persistence/Catalog/Stock/
  StockUpOperationConfiguration.cs                                    [MODIFY: add HasIndex with HasFilter]

backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/
  GetStockUpOperationsSummaryHandler.cs                               [MODIFY: rewrite WHERE, add timing log]

backend/test/Anela.Heblo.Tests/Features/Catalog/
  GetStockUpOperationsSummaryHandlerTests.cs                          [MODIFY: add Failed=3 enum-correctness test]
  GetStockUpOperationsSummaryIntegrationTests.cs                      [NEW: real-DB plan + result test]

memory/gotchas/
  postgres-partial-index-active-states.md                             [NEW: per FR-7]

docs/integrations/ OR docs/investigations/
  postgres-stockupoperations-summary-investigation.md                 [NEW: per FR-1 capture]
```

The configuration must declare the index so the model snapshot stays in sync with the database — even though the actual DDL runs via raw SQL. Pattern:

```csharp
builder.HasIndex(x => new { x.SourceType, x.State })
    .HasDatabaseName("IX_StockUpOperations_State_Active")
    .HasFilter("\"State\" IN (0, 1, 3)");
```

When EF generates the migration it will emit a standard `CreateIndex` call; replace that body with the raw-SQL pair before applying.

### Interfaces and Contracts

- **No public contract changes.** `GetStockUpOperationsSummaryRequest`, `GetStockUpOperationsSummaryResponse`, route, auth, and DTO shape all unchanged (FR-5 holds).
- **`IStockUpOperationRepository.GetAll()` unchanged.** Continues to return `IQueryable<StockUpOperation>`.
- **Handler internal contract:** active-state set is defined once as `private static readonly int[] ActiveStates = { (int)StockUpOperationState.Pending, (int)StockUpOperationState.Submitted, (int)StockUpOperationState.Failed };` to keep the partial-index predicate and the handler in lockstep. Cast through `(int)` rather than hardcoding `{0, 1, 3}` to prevent silent breakage if enum values are ever renumbered.

### Data Flow

For `GET /api/StockUpOperations/summary?sourceType=GiftPackageManufacture`:

1. Controller binds `sourceType` and dispatches `GetStockUpOperationsSummaryRequest` via MediatR.
2. Handler builds `IQueryable` from repository, applies `Where(x => ActiveStates.Contains((int)x.State))`, conditionally appends `Where(x => x.SourceType == request.SourceType.Value)`, then `GroupBy(State).Select(count)`.
3. EF Core translates to: `SELECT "State", COUNT(*) FROM "StockUpOperations" WHERE "State" IN (0,1,3) AND "SourceType" = $1 GROUP BY "State"`.
4. PostgreSQL planner matches `WHERE "State" IN (0,1,3)` to the partial-index predicate, uses `IX_StockUpOperations_State_Active`, performs an Index Scan or Bitmap Index Scan, applies the `SourceType` equality on the smaller row set, and returns up to 3 grouped rows.
5. Handler maps the 3 (or fewer) rows back to `PendingCount`, `SubmittedCount`, `FailedCount`. Zero-count states remain represented as `0` via `FirstOrDefault(...)?.Count ?? 0` (FR-5).
6. Handler logs elapsed milliseconds with structured properties `{ EndpointName, SourceType, ElapsedMs, ResultCounts }` (FR-7).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Partial-index predicate uses wrong enum values, silently excluding `Failed` and including `Completed`. | **CRITICAL** | Spec amendment below. Single source of truth: cast from enum, never hardcode integers in the migration SQL without a comment naming the enum members. Integration test asserts `Failed` is counted (catches the regression). |
| `CREATE INDEX CONCURRENTLY` fails because EF wraps `Up` in a transaction. | High | Use `migrationBuilder.Sql(sql, suppressTransaction: true)`. Verify on a non-prod DB before applying to production. |
| Partial index built but planner still uses Seq Scan because `state` literals aren't recognized. | High | FR-2 requires `EXPLAIN ANALYZE` confirmation; if Index Scan isn't observed, use `int[].Contains` (Decision 3) and re-verify. |
| Existing `IX_StockUpOperations_State` is silently relied on by other code paths and dropping it regresses them. | Medium | Spec FR-3 already calls for `grep` before drop. Constrain this PR to ADD-only; defer drop to a follow-up migration after observing post-deploy plans. |
| Production DB is bloated and the index alone won't fix latency. | Medium | FR-1 captures `pg_stat_user_tables` first; if `n_dead_tup` ratio is high, run `VACUUM (ANALYZE)` per FR-4 before judging the index's impact. |
| Manual-migration drift (per `memory/gotchas/ef-migration-codebase-drift.md`) — application deployed before migration runs in prod. | Medium | The handler's new query shape (`Contains`) does not require the partial index to function correctly; it only requires it to be fast. Order: deploy app → apply migration (any window). No readiness gate needed. |
| `MockQueryable`-based unit tests give false confidence — they cannot validate SQL or index usage. | Medium | Add a real-DB integration test (FR-6) that asserts both correctness and that `EXPLAIN` output contains `Index Scan` (or use a smoke assertion on response time under a seeded dataset). |
| Concurrent index creation fails partway and leaves an `INVALID` index. | Low | Use `IF NOT EXISTS` and document the recovery (`DROP INDEX CONCURRENTLY` + retry) in the migration's leading comment per NFR-4. |

## Specification Amendments

The following changes to `spec.r1.md` are **mandatory** before implementation. They correct factual errors against the actual codebase.

1. **FR-3 enum values are wrong.** Spec states `WHERE state IN (0, 1, 2)` for `Pending, Submitted, Failed`. The enum (`backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperationState.cs`) defines:
   - `Pending = 0`
   - `Submitted = 1`
   - `Completed = 2`
   - `Failed = 3`

   The correct partial-index predicate is `WHERE "State" IN (0, 1, 3)`. As written, the spec would index `Completed` (the high-volume state we want to exclude) and exclude `Failed`. **Update FR-3 acceptance criteria and the migration SQL accordingly.**

2. **FR-5 omits the `SourceType` filter.** The handler accepts an optional `SourceType` filter (`GetStockUpOperationsSummaryHandler.cs:27-29`, `StockUpOperationsController.cs:111-113`), and the controller exposes `?sourceType=` as a query parameter. Spec's "Request: No parameters (unchanged)" is incorrect. **Amend the API/Interface Design section to document the optional `sourceType` query parameter and add an acceptance criterion that the partial index is effective for both filtered and unfiltered calls.**

3. **NFR-3 is incomplete on transaction handling.** Add: "The migration MUST pass `suppressTransaction: true` to `migrationBuilder.Sql()` because PostgreSQL rejects `CREATE INDEX CONCURRENTLY` inside a transaction block. Validation: running the migration in a fresh dev DB must succeed without `25001`."

4. **FR-3 acceptance criterion on dropping `IX_StockUpOperations_State`** should be tightened: do **not** drop in this PR. The composite `(State, CreatedAt)` index already covers most single-`State` lookups via leading-column scans. Drop should be a separate, observable change after one deploy cycle of post-fix telemetry.

5. **FR-6 needs an integration test, not just a unit test.** The existing test file uses `MockQueryable`, which executes against an in-memory list and cannot detect SQL or index-use regressions. Add: "Integration test must run against the real PostgreSQL test DB (Testcontainers or fixture DB), seed a representative state mix including `Failed`, and assert correct counts. Optionally assert `EXPLAIN`'s plan node is not `Seq Scan`."

## Prerequisites

Before implementation begins:

- **Diagnostic capture (FR-1) must complete first.** Don't write the migration speculatively — confirm the row count, state distribution, query plan, dead-tuple ratio, and last vacuum/analyze timestamps. The captured data determines whether (a) the partial index is the right fix, (b) bloat dominates and `VACUUM (ANALYZE)` alone may suffice, or (c) something else (e.g., connection pool warmup) is responsible. Persist the capture under `docs/investigations/` per the existing convention seen in `docs/investigations/TASK-*.md`.
- **Confirm `suppressTransaction: true` overload availability** in the project's EF Core version (`backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj` package versions). It exists from EF Core 5+; the repo is on .NET 8 / EF Core 8, so this is available — but verify before relying on it.
- **Operator access to production for `EXPLAIN ANALYZE` and (if needed) `VACUUM (ANALYZE)`** with read or owner credentials per NFR-2. No automation introduced.
- **No infrastructure or config changes** (no new env vars, no new services, no new health checks).
- **No frontend or OpenAPI client regeneration** — request and response shape are unchanged. Do not run client generation as part of this PR.