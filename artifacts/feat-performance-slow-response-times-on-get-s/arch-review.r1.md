# Architecture Review: Restore `GET StockUpOperations/GetSummary` Endpoint Performance

## Skip Design: true

Backend/operational work only — no UI, no new components, no visual changes. The handler, migration, EF configuration, integration test, and memory gotcha are already in place on this branch. The remaining work is diagnostic capture, verdict-driven corrective action, and post-deploy verification.

## Architectural Fit Assessment

This is a **verification + contingent corrective-action** feature, not a new feature. The architectural shape is identical to the prior fix already implemented on `feat-performance-slow-response-times-on-get-s` (Vertical Slice + MediatR handler + EF Core repository + manual migrations). All scaffolding for the partial-index solution is already committed on this branch:

- Handler at `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs` already uses `ActiveStates.Contains((int)x.State)` (lines 15–20, 41) and emits the timing log (lines 62–68).
- Migration `20260506145627_AddPartialIndexForActiveStockUpOperations.cs` already emits `CREATE INDEX CONCURRENTLY ... WHERE "State" IN (0,1,3)` with `suppressTransaction: true`.
- `StockUpOperationConfiguration.cs:73-75` already declares the index in the EF model snapshot via `HasFilter`.
- `GetStockUpOperationsSummaryIntegrationTests.Handle_QueryPlan_DoesNotUseSeqScan` is in place and asserts the plan references `IX_StockUpOperations_State_Active`.
- `memory/gotchas/postgres-partial-index-active-states.md` records the rules established.

The new 13.2 s spike means one of: the existing code is not yet in production, the planner regressed off the partial index, statistics went stale, or it was a transient. The architectural integration points are unchanged; what differs is which **conditional branch** of FR-4 the engineer takes.

## Proposed Architecture

### Component Overview

```
                           [OPERATOR ACTIONS — no code]
                                       │
                                       ▼
            ┌──────────────────────────────────────────────────────┐
            │ FR-1: Production deployment check                    │
            │   • pg_indexes lookup (partial index present?)       │
            │   • __EFMigrationsHistory lookup (migration applied?)│
            │   • container Git SHA (binary has new handler?)      │
            └──────────────────────────────────────────────────────┘
                                       │
                          NO ▼                ▼ YES
            ┌──────────────────────┐    ┌──────────────────────────┐
            │ Defer FR-2..5;       │    │ FR-2: Capture diagnostics │
            │ alert is pre-deploy. │    │   (5 SQL queries against  │
            │ Treat as expected.   │    │   prod, fill investigation│
            └──────────────────────┘    │   .md — no placeholders)  │
                                        └──────────────────────────┘
                                                       │
                                                       ▼
                              ┌──────────────────────────────────────────┐
                              │ FR-3: Classify root cause (verdict)     │
                              │   (a) plan still Seq Scan                │
                              │   (b) transient (autovacuum/checkpoint)  │
                              │   (c) stale stats                        │
                              │   (d) cold start                         │
                              └──────────────────────────────────────────┘
                                                       │
                  ┌────────────────────┬───────────────┼───────────────┬─────────────────┐
                  ▼                    ▼               ▼               ▼                 ▼
            (a) Deploy            (a.i) Drop      (b) Memory       (c) VACUUM        (d) File
            migration             redundant idx   gotcha entry     (ANALYZE)         warm-up
                                  / INCLUDE /     for transient    + maybe table-    ticket
                                  raise stats     class            level autovac     (out of
                                  target          (no code)        config            scope)
                                  [code]                           (operator SQL)

                            [CODE PATHS — only if FR-4 selects (a)/(a.i) or (c)]
                                       │
                                       ▼
          ┌───────────────────────────────────────────────────────────────┐
          │ GetStockUpOperationsSummaryHandler (unchanged for b/c/d)     │
          │   ↳ optional: (a.i) handler change only if INCLUDE chosen     │
          │ New EF migration (only for a.i if drop/INCLUDE is selected)   │
          │ Integration test fixture updated if IX_State is removed       │
          └───────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
                              FR-5 / FR-7: 7-day post-fix verification
                              (App Insights + appended investigation note)
```

### Key Design Decisions

#### Decision 1: Where the verdict lives
**Options considered:**
- A. Encode verdict in a new code artifact (config, feature flag, runtime decision tree).
- B. Verdict lives in `docs/investigations/stockupoperations-summary-slow-query.md` as plain markdown.

**Chosen approach:** B.

**Rationale:** The verdict is a one-time triage decision tied to one specific App Insights event. Encoding it in code introduces dead config the day after the decision is made. The investigation file is already the established artifact for this problem (it exists and was authored by the prior plan); appending a "Captured: post-fix YYYY-MM-DD" section with the verdict mirrors how the prior incident was documented. No new artifact type is needed.

#### Decision 2: Code change only if FR-3 verdict requires it
**Options considered:**
- A. Always merge a defensive code change with the investigation.
- B. Pure investigation PR if verdict is (b) or (d); code-changing PR only for (a) where the migration isn't deployed, (a.i) where a follow-up index change is required, or (c) where autovacuum config is tuned.

**Chosen approach:** B — strictly minimal, per spec FR-4 ("Apply the smallest corrective action").

**Rationale:** The existing branch already contains the full partial-index fix. If the verdict is (b) transient or (d) cold-start, there is no problem in the code to fix and any speculative change increases risk. If the verdict is (a) (a.i) (c), the corrective action is sharply scoped: redeploy / drop a redundant index / raise statistics / configure autovacuum. Spec amendment item 4 below covers this.

#### Decision 3: If FR-4 selects option (a.i), prefer dropping `IX_StockUpOperations_State` over INCLUDE/statistics tuning
**Options considered (only relevant under (a.i)):**
- i. `DROP INDEX IX_StockUpOperations_State` (force planner to the partial index).
- ii. Raise `default_statistics_target` for the `State` column (give planner finer histograms).
- iii. Add an `INCLUDE` clause covering `(SourceType)` to make the partial index index-only-scan capable.

**Chosen approach:** i, if (a.i) triggers.

**Rationale:** Option (i) is the lowest-risk and most observable: it removes a confirmed-redundant index whose presence is the suspected cause of the regression. The `(State, CreatedAt)` index still covers single-column `State` lookups via leading-column scans; no other handler relies on the bare `State` index. Option (ii) is a system-wide knob and is harder to revert. Option (iii) requires re-creating the partial index (a `CONCURRENTLY` rebuild on a large table) for a marginal gain when the response is just three integers. The prior arch-review (`artifacts/feat-get-stockupoperations-getsummary-took-12/arch-review.r1.md` lines 33, 152) already concluded the bare `State` index is a likely-redundant drop candidate — option (i) executes that deferred follow-up, which is exactly the trigger described in FR-4(a.i).

#### Decision 4: Migration deployment policy under FR-4(a)
**Options considered:**
- A. Wait for the existing migration to be applied via the project's manual migration process.
- B. Add an automated-migration safety net for this case.

**Chosen approach:** A.

**Rationale:** `CLAUDE.md` explicitly states migrations are manual and out of scope for changes here. If FR-1 reveals the migration is not yet in production, the corrective action is to apply it through the existing manual deployment process and verify, not to change the deployment policy.

#### Decision 5: FR-7 observability hardening lives outside the handler
**Options considered:**
- A. Add a custom log property bag inside the handler.
- B. Adjust Application Insights configuration if structured properties aren't surfacing.

**Chosen approach:** B.

**Rationale:** The handler already emits `ElapsedMs`, `SourceType`, `PendingCount`, `SubmittedCount`, `FailedCount` as structured properties in its `LogInformation` call (handler lines 62–68). If those are not queryable in App Insights, the gap is in `ApplicationInsightsLoggerOptions.IncludeScopes` / `TrackException` configuration — not in the handler. Keep the handler invariant; touch only `Program.cs` / `appsettings.{Environment}.json` if needed.

## Implementation Guidance

### Directory / Module Structure

No new modules. Files touched depend on the FR-3 verdict:

```
docs/investigations/
  stockupoperations-summary-slow-query.md           [MODIFY: replace {FILL_IN:...} placeholders
                                                     with real production diagnostics; append
                                                     verdict section + post-fix capture]

memory/gotchas/
  postgres-partial-index-active-states.md           [MODIFY only if verdict is (b)/(c)/(d):
                                                     append "Transient class observed"
                                                     or "Statistics-staleness recurrence"
                                                     subsection]

# CONDITIONAL on FR-3 verdict
backend/src/Anela.Heblo.Persistence/Migrations/
  {timestamp}_DropRedundantStockUpOperationsStateIndex.cs    [NEW: only if FR-4 picks option (a.i)-(i)]
  {timestamp}_DropRedundantStockUpOperationsStateIndex.Designer.cs
  ApplicationDbContextModelSnapshot.cs                       [regenerated]

backend/src/Anela.Heblo.Persistence/Catalog/Stock/
  StockUpOperationConfiguration.cs                  [MODIFY only if (a.i)-(i):
                                                     remove `HasIndex(x => x.State)` declaration
                                                     at line 58-59 to keep snapshot in sync]

backend/test/Anela.Heblo.Tests/Features/Catalog/
  GetStockUpOperationsSummaryIntegrationTests.cs    [MODIFY only if (a.i)-(i):
                                                     remove IX_StockUpOperations_State from
                                                     fixture DDL (line 68-69) AND add an
                                                     assertion that planner still picks
                                                     the partial index without the bare index
                                                     present]

backend/src/Anela.Heblo.API/  or  appsettings.{Env}.json
  (only if FR-7 surface gap)                        [MODIFY: enable AI scope / property
                                                     enrichment; no log message changes]
```

If the verdict is (b) — transient — **no source code, no migration, no test change**. Only the investigation file and the memory gotcha file are modified.

### Interfaces and Contracts

No changes:

- HTTP route, request DTO, response DTO, auth surface — all preserved (NFR-2).
- `IStockUpOperationRepository.GetAll()` — unchanged.
- The handler's `ActiveStates` array is the single source of truth (handler lines 15–20). The partial-index SQL literal `(0, 1, 3)` in `20260506145627_AddPartialIndexForActiveStockUpOperations.cs:29` must continue to match.
- If a new migration drops `IX_StockUpOperations_State` under (a.i), its `Down` must recreate the index using `migrationBuilder.CreateIndex` (standard, no `CONCURRENTLY` needed for the recreate — but if the table is large in prod, use `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_StockUpOperations_State\" ...", suppressTransaction: true)` to honor the codebase rule established in `memory/gotchas/postgres-partial-index-active-states.md`).

### Data Flow

For `GET /api/StockUpOperations/summary?sourceType=GiftPackageManufacture` (unchanged):

1. Controller binds `sourceType` and dispatches `GetStockUpOperationsSummaryRequest` via MediatR.
2. Handler builds `IQueryable` from `IStockUpOperationRepository.GetAll()`, applies `Where(x => ActiveStates.Contains((int)x.State))`, then optional `Where(x => x.SourceType == request.SourceType.Value)`, then `GroupBy(State).Select(count)`.
3. EF Core emits literal `WHERE "State" IN (0, 1, 3)` against the partial-index predicate.
4. Planner picks `IX_StockUpOperations_State_Active` → Index Scan / Bitmap Index Scan / Index Only Scan; never `Seq Scan`. **This is the property FR-5 must continue to demonstrate.**
5. Three integers returned; handler maps to `PendingCount`, `SubmittedCount`, `FailedCount` with `?? 0` fallback.
6. Handler logs structured properties; App Insights surfaces them for the FR-7 KQL query.

The only **data-flow change** that can result from this feature is FR-4(a.i)-(i): dropping `IX_StockUpOperations_State`. After that drop, step 4's plan must still pick the partial index — this is exactly what the strengthened integration-test assertion verifies.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-1 check skipped; team re-investigates a problem that's already fixed but not deployed. | High | Hard-block FR-2 onward behind FR-1's three concrete checks (pg_indexes, __EFMigrationsHistory, container Git SHA). Spec already encodes this; enforce in PR description. |
| FR-3 verdict picked without evidence (anchoring on "must be the partial index again"). | High | Investigation file requires actual EXPLAIN output, dead_pct, last_(auto)analyze, index sizes, and an App Insights restart-event check. The verdict box has four mutually exclusive options and the file cannot be committed with `{FILL_IN: …}` markers. |
| FR-4(a.i)-(i) drops `IX_StockUpOperations_State` and a hidden caller regresses. | Medium | Grep all handlers using `State` equality / ORDER BY / Where on `StockUpOperations` before the drop. `(State, CreatedAt)` covers leading-column `State` scans, but verify against `IStockUpOperationRepository` callers (`GetByStateAsync`, etc.). The strengthened integration test must assert the partial-index plan in the **absence** of the bare index. |
| Migration to drop `IX_StockUpOperations_State` re-introduces the `CONCURRENTLY`-in-transaction bug. | Medium | Codebase rule already documented in `memory/gotchas/postgres-partial-index-active-states.md`. The new migration MUST use `migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_StockUpOperations_State\";", suppressTransaction: true)` per existing precedent in `20260506145627_AddPartialIndexForActiveStockUpOperations.cs`. |
| `VACUUM (ANALYZE)` under FR-4(c) blocks writers if run with `FULL`. | Medium | Spec specifies `VACUUM (ANALYZE)`, not `VACUUM FULL`. Document that distinction explicitly in the corrective-action note appended to the investigation file. |
| FR-7 App Insights structured-properties gap diagnosed as missing properties when it's actually a sampling / log-level config issue. | Medium | Verify by issuing a fresh request immediately after deploy and querying `traces | where timestamp > ago(5m) and message contains "GetStockUpOperationsSummary"`; only adjust config if properties are persistently missing on at least 3 invocations. |
| 7-day verification window (FR-5) is interrupted by an unrelated deploy that re-triggers cold-start latency. | Low | Document the deploy timeline in the investigation file's post-fix section. If a cold-start outlier appears post-deploy, classify it as (d) and document — do not re-open FR-3. |
| Operator captures production data containing row-level PII (DocumentNumber etc.) and commits it to the investigation file. | Low | NFR-2 already mandates aggregates only. The five SQL queries in the investigation document (lines 97–127) are aggregate / catalog queries by construction; PR review must reject any literal data row in the markdown. |

## Specification Amendments

The spec is already correct on the architectural surface. Three minor clarifications to strengthen the conditional implementation paths:

1. **FR-4(a.i) needs a concrete preference order.** As written, the three sub-options ((i) drop redundant index, (ii) raise `default_statistics_target`, (iii) INCLUDE clause) are listed as equally weighted with "pick the lowest-risk option and document why." Amend FR-4(a.i) to state the recommended order: **(i) first**, because the `(State, CreatedAt)` index covers leading-column `State` scans and the prior arch-review already flagged `IX_StockUpOperations_State` as a deferred drop candidate. Options (ii) and (iii) only enter if (i) fails to produce the desired plan.

2. **FR-6 acceptance criterion is incomplete for the (a.i)-(i) path.** The current text says "a new assertion confirms the partial index is still selected even when the catch-all index is absent." Make it concrete: add `Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex` — a new `[Fact]` that mirrors the existing plan-check test but executes `DROP INDEX "IX_StockUpOperations_State"` before the EXPLAIN, asserting `IX_StockUpOperations_State_Active` is still picked. Place it next to the existing `Handle_QueryPlan_DoesNotUseSeqScan` (`GetStockUpOperationsSummaryIntegrationTests.cs:191`).

3. **FR-7 is silent on the configuration surface to inspect.** Add: "If structured properties are missing in App Insights, inspect `backend/src/Anela.Heblo.API/Program.cs` (Application Insights registration) and `appsettings.{Environment}.json` (`ApplicationInsights:EnableAdaptiveSampling`, `Logging:ApplicationInsights:LogLevel`). Do not change the handler's log call."

The spec's core requirements (FR-1 through FR-7) and NFR-1 through NFR-5 are otherwise sound and aligned with the existing codebase patterns.

## Prerequisites

Before any implementation work begins:

- **FR-1 gate must pass first.** Run the three production checks (pg_indexes lookup for `IX_StockUpOperations_State_Active`, `__EFMigrationsHistory` lookup for `AddPartialIndexForActiveStockUpOperations`, container Git SHA verification). If any fail, FR-2 onward is deferred and the corrective action is "deploy the pending branch" — there is no architectural decision left to make.
- **Read-only production database role** for the FR-2 diagnostic queries (NFR-2). The investigation document already has the five canonical queries (lines 97–127).
- **App Insights query access** for FR-3 (cold-start verdict requires checking application-restart events on the timeline) and FR-5 (7-day percentile verification) and FR-7 (custom-dimensions presence).
- **No infrastructure, OpenAPI client, or frontend changes.** If the verdict is (a.i)-(i), a single new EF migration is the only schema change — generated and committed per the existing manual-migration policy in `CLAUDE.md`.
- **Branch is on `feat-performance-slow-response-times-on-get-s`** with the partial-index code already committed (verified in this review). All conditional code paths build on the current branch state; do not re-implement the partial-index fix.
- **`gh` CLI configured** for any follow-up issues (FR-4(b) recurrence threshold, FR-4(c) autovacuum config, FR-4(d) warm-up ticket).