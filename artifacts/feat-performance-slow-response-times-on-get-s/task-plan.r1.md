# Restore StockUpOperations GetSummary Endpoint Performance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-verify that the previously implemented partial-index fix for `GET /api/StockUpOperations/summary` is live in production, capture fresh diagnostics for the 13,215 ms spike alert, classify root cause, apply the minimum corrective action, and demonstrate p95 ≤ 300 ms / p99 ≤ 1 000 ms / zero >10 000 ms for 7 days.

**Architecture:** No new modules. The partial-index fix is already committed on this branch (handler + migration + EF model + integration test + memory gotcha). The remaining work is (1) production verification, (2) diagnostic capture into `docs/investigations/stockupoperations-summary-slow-query.md` (replace all `{FILL_IN: …}` placeholders), (3) verdict-driven corrective branch — most likely either "wait for deploy" (a), "drop redundant `IX_StockUpOperations_State`" (a.i), "transient — document and move on" (b), "`VACUUM (ANALYZE)`" (c), or "cold start — file separate ticket" (d), (4) Application Insights `LogLevel` entry so the handler's structured timing log surfaces for FR-7, (5) post-fix 7-day verification appended to the investigation file.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql provider), PostgreSQL 16, MediatR, xUnit, Testcontainers (`Testcontainers.PostgreSql`), MockQueryable + Moq, Microsoft.Extensions.Logging.ApplicationInsights, Azure Application Insights (KQL queries via `traces` and `requests` tables), `gh` CLI for follow-up issues.

**Spec amendments locked in (per `arch-review.r1.md` § Specification Amendments):**
- FR-4(a.i) preference order is fixed: **(i) drop `IX_StockUpOperations_State` first**; (ii) `default_statistics_target` and (iii) INCLUDE clause are fallbacks only if (i) fails to produce the desired plan.
- FR-6 acceptance under (a.i)-(i) requires a concrete new test `Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex` that `DROP`s `IX_StockUpOperations_State` inside the test fixture and re-asserts the partial index is still picked.
- FR-7 silent-property fix targets `backend/src/Anela.Heblo.API/appsettings.{Environment}.json` `Logging:ApplicationInsights:LogLevel` (and confirms `Program.cs` AI registration is sufficient); the handler's log call is invariant.

---

## File Structure

Files this plan may touch, by task. **Conditional** files depend on the FR-3 verdict.

```
docs/investigations/
  stockupoperations-summary-slow-query.md                                   [MODIFY: replace {FILL_IN}
                                                                             placeholders with real prod
                                                                             diagnostics + append verdict
                                                                             + append post-fix capture]

memory/gotchas/
  postgres-partial-index-active-states.md                                   [MODIFY conditional on
                                                                             verdict (b)/(c)/(d):
                                                                             append "Transient class
                                                                             observed", "Statistics
                                                                             staleness", or
                                                                             "Cold-start spike"
                                                                             subsection]

backend/src/Anela.Heblo.API/
  appsettings.Production.json                                               [MODIFY for FR-7: add
                                                                             "Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary"
                                                                             = "Information" entry
                                                                             under Logging.ApplicationInsights.LogLevel]
  appsettings.Staging.json                                                  [MODIFY for FR-7 (same key
                                                                             addition so staging can be
                                                                             used to verify pre-prod)]

# CONDITIONAL on verdict (a.i)-(i): drop redundant bare State index
backend/src/Anela.Heblo.Persistence/Migrations/
  {timestamp}_DropRedundantStockUpOperationsStateIndex.cs                   [NEW: raw SQL DROP INDEX
                                                                             CONCURRENTLY IF EXISTS,
                                                                             suppressTransaction: true]
  {timestamp}_DropRedundantStockUpOperationsStateIndex.Designer.cs          [NEW: EF-scaffolded]
  ApplicationDbContextModelSnapshot.cs                                      [REGENERATED by EF]

backend/src/Anela.Heblo.Persistence/Catalog/Stock/
  StockUpOperationConfiguration.cs                                          [MODIFY for (a.i)-(i):
                                                                             remove HasIndex(x => x.State)
                                                                             at lines 58-59 so snapshot
                                                                             matches DB]

backend/test/Anela.Heblo.Tests/Features/Catalog/
  GetStockUpOperationsSummaryIntegrationTests.cs                            [MODIFY for (a.i)-(i):
                                                                             remove IX_StockUpOperations_State
                                                                             from fixture DDL (lines 68-69)
                                                                             AND add new test
                                                                             Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex]
```

If the verdict is (b) — transient — **no source code, no migration, no test changes.** Only the investigation file and the memory gotcha file change.

If the verdict is (d) — cold start — **no source code, no migration, no test changes.** Only the investigation file and the memory gotcha file change, plus a separate GitHub issue is filed.

If the verdict is (c) — stale stats — **no source code, no migration, no test changes.** Only a one-off `VACUUM (ANALYZE)` is run in production and the investigation file is updated.

The FR-7 `appsettings.*.json` change is unconditional and is safe to ship regardless of verdict — it merely makes the existing structured log queryable in App Insights.

---

## Task 0: Pre-flight — production deployment check (FR-1 gate)

**Files:** none modified — operator runs queries against production read-only role and records output verbatim in scratch text. The output is consumed by Task 1.

This task is a hard gate. If any of the three checks fail, the alert is treated as pre-deploy expected behavior, Tasks 1–4 are deferred until the migration + binary are deployed, and the only follow-up action is to deploy the existing branch through the standard manual deployment process per `CLAUDE.md`.

- [ ] **Step 1: Verify the partial index exists in production**

Run against the production PostgreSQL instance with read-only credentials:

```sql
SELECT indexname
FROM pg_indexes
WHERE tablename = 'StockUpOperations'
  AND indexname = 'IX_StockUpOperations_State_Active';
```

Expected: one row returned with `indexname = IX_StockUpOperations_State_Active`. If zero rows → FR-1 check (a) FAILS.

- [ ] **Step 2: Verify the migration is recorded in `__EFMigrationsHistory`**

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%AddPartialIndexForActiveStockUpOperations';
```

Expected: one row with `MigrationId = '20260506145627_AddPartialIndexForActiveStockUpOperations'`. If zero rows → FR-1 check (b) FAILS.

- [ ] **Step 3: Verify the deployed container Git SHA contains the new handler**

```bash
# From the operator's terminal (Azure CLI session against the production Web App)
az webapp config container show \
  --name <production-webapp-name> \
  --resource-group <production-resource-group> \
  --query "[?name=='DOCKER_CUSTOM_IMAGE_NAME'].value | [0]" -o tsv
```

Expected output format: `<dockerhub-org>/anela-heblo:<git-sha>` where `<git-sha>` is reachable from the current branch via `git merge-base --is-ancestor <git-sha> HEAD && echo "deployed binary contains handler fix"`. If the SHA is older than commit `bca5e54` (the commit that introduced `ActiveStates.Contains((int)x.State)` on this branch — adjust to the actual commit SHA on `feat-performance-slow-response-times-on-get-s` where `GetStockUpOperationsSummaryHandler.cs:15-20` first appears, verifiable via `git log --oneline backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs`) → FR-1 check (c) FAILS.

- [ ] **Step 4: Record the gate verdict**

In a scratch text file (do NOT commit yet), capture:

```
FR-1 gate captured: {YYYY-MM-DD HH:MM UTC}
  Check (a) partial index present in pg_indexes: PASS / FAIL
  Check (b) migration row in __EFMigrationsHistory: PASS / FAIL
  Check (c) deployed binary contains new handler (Git SHA = ...): PASS / FAIL
  Overall gate: PASS (proceed to Task 1) / FAIL (defer Tasks 1-4 until deploy)
```

- [ ] **Step 5: If gate FAILS — exit this plan and trigger the manual deploy path**

Stop here. The corrective action is "apply the existing branch via the standard manual deployment process (`CLAUDE.md` § Project facts)." Re-run Task 0 after the deploy completes. No code change is required from this plan in that case.

- [ ] **Step 6: If gate PASSES — proceed to Task 1**

No commit at the end of this task — the captured output is consumed by Task 1.

---

## Task 1: Capture fresh production diagnostics (FR-2)

**Files:**
- Modify: `docs/investigations/stockupoperations-summary-slow-query.md` (replace every `{FILL_IN: …}` placeholder)

This task converts the placeholder-laden investigation file into a real diagnostic record for the 13,215 ms spike. After this task the file must contain no `{FILL_IN: …}` markers.

- [ ] **Step 1: Run Query 1 — state distribution**

```sql
SELECT "State", COUNT(*) FROM public."StockUpOperations" GROUP BY "State" ORDER BY "State";
```

Capture the four-row output (or however many distinct State values exist). Required values: Pending(0), Submitted(1), Completed(2), Failed(3). Also compute `Total rows = sum of all counts`.

- [ ] **Step 2: Run Query 2 — unfiltered EXPLAIN ANALYZE**

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT "State", COUNT(*)
FROM public."StockUpOperations"
WHERE "State" IN (0, 1, 3)
GROUP BY "State";
```

Capture the full multi-line plan output verbatim. Identify the **top scan node** type: one of `Seq Scan`, `Bitmap Index Scan`, `Index Scan`, or `Index Only Scan`. If `Index Scan` / `Bitmap Index Scan` / `Index Only Scan`, also capture the index name on the node (must be `IX_StockUpOperations_State_Active` for a healthy plan).

- [ ] **Step 3: Run Query 3 — `SourceType`-filtered EXPLAIN ANALYZE (dashboard call shape)**

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT "State", COUNT(*)
FROM public."StockUpOperations"
WHERE "State" IN (0, 1, 3)
  AND "SourceType" = 0
GROUP BY "State";
```

Capture verbatim. Same plan-node classification.

- [ ] **Step 4: Run Query 4 — `pg_stat_user_tables` snapshot**

```sql
SELECT relname, n_live_tup, n_dead_tup,
       round(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_pct,
       last_vacuum, last_autovacuum, last_analyze, last_autoanalyze
FROM pg_stat_user_tables
WHERE relname = 'StockUpOperations';
```

Capture the single row.

- [ ] **Step 5: Run Query 5 — index sizes**

```sql
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid)) AS size
FROM pg_stat_user_indexes
WHERE relname = 'StockUpOperations'
ORDER BY indexrelname;
```

Capture every row. Verify `IX_StockUpOperations_State_Active` appears with a size (confirms FR-1 step 1 from a different angle).

- [ ] **Step 6: Pull App Insights context for the 13.2 s spike timestamp**

In the Azure portal → Application Insights resource → Logs, run:

```kusto
requests
| where name == "GET StockUpOperations/GetSummary" or url has "/api/StockUpOperations/summary"
| where duration > 10000
| project timestamp, duration, resultCode, customDimensions, cloud_RoleInstance, operation_Id
| order by timestamp desc
| take 20
```

Capture the single 13,215 ms row's timestamp and `cloud_RoleInstance`. Then check for application restarts around that timestamp:

```kusto
traces
| where message has "Application started" or message has "Now listening on"
| where timestamp between (datetime('<spike-timestamp>') - 5m .. datetime('<spike-timestamp>') + 1m)
| project timestamp, message, cloud_RoleInstance
| order by timestamp desc
```

If any restart row falls within ~60 s **before** the spike on the same `cloud_RoleInstance`, that is strong evidence for verdict (d) — cold start.

- [ ] **Step 7: Replace every `{FILL_IN: …}` placeholder in the investigation file**

Open `docs/investigations/stockupoperations-summary-slow-query.md` and:

1. Update **line 7** "Captured by:" to your operator initials and today's date (`2026-05-16` or whenever this task actually runs).
2. Update the **state distribution table (lines 12–15)** with the real counts from Step 1. Add a `Total rows: N` line below at the place currently marked `Total rows: {FILL_IN: n}`.
3. Replace the **unfiltered EXPLAIN block (lines 21–24)** with the raw Step 2 output. Update line 25 `Plan node observed: …` to the actual top scan node (e.g. `Index Only Scan using IX_StockUpOperations_State_Active`).
4. Replace the **filtered EXPLAIN block (lines 29–32)** with Step 3 output and line 33 plan-node entry.
5. Replace the **`pg_stat_user_tables` snapshot (lines 36–42)** with Step 4 values.
6. Replace the **index sizes table (lines 47–50)** with Step 5 values; if `IX_StockUpOperations_State_Active` is present, add a fifth row for it.
7. The "Recommended actions" checklist boxes at lines 68–69 must be either checked (`[x]`) or struck through with a note — no placeholders left.
8. Leave the "Post-fix verification" section (lines 71–91) as-is for now. Tasks 4/5 will fill it.

- [ ] **Step 8: Verify no placeholders remain**

```bash
grep -n "FILL_IN" docs/investigations/stockupoperations-summary-slow-query.md
```

Expected: **no output** (zero matches). If any line still contains `FILL_IN`, return to Step 7 and resolve it.

- [ ] **Step 9: Commit the diagnostic capture**

```bash
git add docs/investigations/stockupoperations-summary-slow-query.md
git commit -m "docs: capture stockupoperations-summary diagnostics for 13.2s spike"
```

---

## Task 2: Classify the root cause and record the verdict (FR-3)

**Files:**
- Modify: `docs/investigations/stockupoperations-summary-slow-query.md` (append a `## Verdict (2026-05-16)` section)

The verdict is a one-time triage decision that drives Task 4's branch. The four options are mutually exclusive.

- [ ] **Step 1: Apply the decision rules from the diagnostics**

Walk down this decision tree in order. Stop at the first match.

| Evidence | Verdict |
|---|---|
| Task 1 Step 2 or Step 3 plan-node shows `Seq Scan` **and** the partial index exists per Task 0 Step 1 | (a) — planner regressed off the partial index → go to (a.i) |
| Task 1 Step 2 or Step 3 plan-node shows `Seq Scan` **and** the partial index is **missing** | (a) — fix is not deployed → Task 0 already exits the plan; if you got here, re-run Task 0 |
| Task 1 Step 6 reveals an application restart within ~60 s before the spike on the same `cloud_RoleInstance` | (d) — cold start |
| Task 1 Step 4 shows `last_analyze` and `last_autoanalyze` both older than 24 h **and** `dead_pct > 20` | (c) — stale statistics |
| All of the above are negative; plan-nodes are healthy on re-run; spike has not recurred | (b) — transient (autovacuum / checkpoint / lock contention) |

- [ ] **Step 2: Append the verdict section to the investigation file**

At the bottom of `docs/investigations/stockupoperations-summary-slow-query.md`, append (replace bracketed bits with your actual values):

```markdown

## Verdict (2026-05-16)

**Selected:** (b) — transient (autovacuum/checkpoint/lock contention)

**Justification:** Partial index `IX_StockUpOperations_State_Active` is present (Task 0 Step 1 returned one row). EXPLAIN re-runs (Task 1 Steps 2–3) show `Index Only Scan using IX_StockUpOperations_State_Active` with execution time ≤ 5 ms. `pg_stat_user_tables` reports `dead_pct = X%` with `last_autoanalyze = <recent timestamp>`. No App Insights restart event within 60 s of the 13.2 s spike. The single spike is therefore consistent with a one-off concurrent autovacuum/checkpoint event on the table.

**Action under FR-4:** path (b) — no code change. Update `memory/gotchas/postgres-partial-index-active-states.md` with the transient class observed. Document the spike timestamp here.

**Spike event timestamp (UTC):** {spike timestamp from Task 1 Step 6}
```

Use the same template structure (Selected / Justification / Action / Spike timestamp) for verdicts (a)/(c)/(d) — adjust the prose to match the actual evidence.

- [ ] **Step 3: Commit the verdict**

```bash
git add docs/investigations/stockupoperations-summary-slow-query.md
git commit -m "docs: classify stockupoperations-summary spike as <verdict-letter>"
```

---

## Task 3: Surface the handler's structured log in App Insights (FR-7)

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.Production.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Staging.json`

The handler at `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs:62-68` already emits a structured `LogInformation` with `ElapsedMs`, `SourceType`, `PendingCount`, `SubmittedCount`, `FailedCount` properties. In production, the `Logging:ApplicationInsights:LogLevel.Default` is `Warning` (see `appsettings.Production.json:19`), and no explicit entry exists for `Anela.Heblo.Application.Features.Catalog`, so the information log is filtered out before reaching App Insights. Add a category-specific entry that bumps this single namespace to `Information`. **Do not change the handler's log call.**

This change is unconditional — it is safe regardless of FR-3 verdict and is required by FR-7 acceptance.

- [ ] **Step 1: Add the production LogLevel entry**

Edit `backend/src/Anela.Heblo.API/appsettings.Production.json` — find the `Logging.ApplicationInsights.LogLevel` block (currently lines 17–27). Insert a new entry **after** `"Anela.Heblo.API": "Information"` so the block reads:

```json
    "ApplicationInsights": {
      "LogLevel": {
        "Default": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.IdentityModel": "None",
        "Anela.Heblo.API": "Information",
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary": "Information",
        "Anela.Heblo.Application.Features.UserManagement": "Information",
        "Anela.Heblo.Application.Features.UserManagement.Services.GraphService": "Information",
        "Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers": "Information"
      }
    }
```

- [ ] **Step 2: Add the same entry to staging settings**

Edit `backend/src/Anela.Heblo.API/appsettings.Staging.json`. Locate the `Logging.ApplicationInsights.LogLevel` block (use Grep to find the file structure first if unsure):

```bash
# Verify the block exists and find its line range
```

Use Read on `backend/src/Anela.Heblo.API/appsettings.Staging.json` to locate the block, then insert the same `"Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary": "Information"` line. If the staging file has no `ApplicationInsights` block under `Logging`, add the full structure with this single entry next to `Default`.

- [ ] **Step 3: Validate JSON syntax**

```bash
python3 -m json.tool backend/src/Anela.Heblo.API/appsettings.Production.json > /dev/null && \
python3 -m json.tool backend/src/Anela.Heblo.API/appsettings.Staging.json > /dev/null && \
echo "JSON OK"
```

Expected: `JSON OK`. Any other output indicates a syntax error to fix.

- [ ] **Step 4: Build the backend to confirm no config-schema breakage**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.Production.json backend/src/Anela.Heblo.API/appsettings.Staging.json
git commit -m "chore: surface GetStockUpOperationsSummary structured log in App Insights"
```

- [ ] **Step 6: Post-deploy — verify the log appears in App Insights**

This step runs **after the appsettings change is deployed** to staging or production. Trigger the endpoint by hitting `GET /api/StockUpOperations/summary` from the UI or with `curl`, wait ~5 minutes for App Insights ingestion, then in the Azure portal Logs tab run:

```kusto
traces
| where timestamp > ago(10m)
| where message has "GetStockUpOperationsSummary completed in"
| project timestamp, message, customDimensions
| take 10
```

Expected: at least one row, with `customDimensions` containing `ElapsedMs`, `SourceType`, `PendingCount`, `SubmittedCount`, `FailedCount` keys (as structured-log scopes). If `customDimensions` is empty, see arch-review § Decision 5 — the next debug step is `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs` (specifically `EnableAdaptiveSampling = true` may be dropping the row; consider adding `"GetStockUpOperationsSummary completed in"` to `CostOptimizedTelemetryProcessor` exclusions if the trace is being sampled out).

If FR-7 surface gap persists across at least three observed invocations, file a follow-up issue and link from the investigation document; do not block FR-5 verification on it.

---

## Task 4: Apply the corrective action per the verdict (FR-4)

The single sub-task below the engineer runs depends entirely on the FR-3 verdict from Task 2 Step 1. Execute exactly one of Tasks 4a, 4ai, 4b, 4c, or 4d.

### Task 4a: Verdict = (a), partial index missing — apply existing migration

**Files:** none in this repo. The migration `20260506145627_AddPartialIndexForActiveStockUpOperations` already exists. The corrective action is operational.

- [ ] **Step 1: Apply the migration to production using the project's manual migration process**

Per `CLAUDE.md` § Project facts ("Database migrations are manual"), follow the existing runbook. The migration uses `CREATE INDEX CONCURRENTLY` with `suppressTransaction: true`, so applying it on a live system is safe — it must complete without blocking writers.

```bash
# Operator runs (from a workstation with prod DB credentials in a secret manager):
dotnet ef database update --project backend/src/Anela.Heblo.Persistence \
                          --startup-project backend/src/Anela.Heblo.API \
                          --connection "<production-connection-string>" \
                          20260506145627_AddPartialIndexForActiveStockUpOperations
```

Expected output: `Applying migration '20260506145627_AddPartialIndexForActiveStockUpOperations'.` then `Done.`. No error, no leftover `INVALID` index.

- [ ] **Step 2: Verify the index was created and is not `INVALID`**

```sql
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'StockUpOperations'
  AND indexname = 'IX_StockUpOperations_State_Active';

-- Also confirm not INVALID
SELECT i.relname, x.indisvalid, x.indisready
FROM pg_class i
JOIN pg_index x ON x.indexrelid = i.oid
WHERE i.relname = 'IX_StockUpOperations_State_Active';
```

Expected: one row with `indisvalid = t AND indisready = t`. If `indisvalid = f`, drop with `DROP INDEX CONCURRENTLY "IX_StockUpOperations_State_Active";` and re-run Step 1.

- [ ] **Step 3: Re-run Task 1 Steps 2–3 (EXPLAIN ANALYZE) to confirm the planner now uses the partial index**

The top scan node must be `Index Only Scan` / `Index Scan` / `Bitmap Index Scan` using `IX_StockUpOperations_State_Active`. If still `Seq Scan` → escalate to Task 4ai (drop the redundant `IX_StockUpOperations_State`).

- [ ] **Step 4: Append a "Captured: post-fix" section to the investigation file**

Append below the verdict section in `docs/investigations/stockupoperations-summary-slow-query.md`:

```markdown

## Captured: post-fix 2026-05-16

**Action taken:** Applied migration `20260506145627_AddPartialIndexForActiveStockUpOperations` to production at {timestamp UTC}.

**Post-fix EXPLAIN (unfiltered):**
\`\`\`
{paste plan output from Task 4a Step 3}
\`\`\`

**Post-fix EXPLAIN (`SourceType = 0` filter):**
\`\`\`
{paste plan output from Task 4a Step 3}
\`\`\`

**Plan node observed:** Index Only Scan using IX_StockUpOperations_State_Active (or Index Scan / Bitmap Index Scan)

7-day verification — see Task 5.
```

- [ ] **Step 5: Commit**

```bash
git add docs/investigations/stockupoperations-summary-slow-query.md
git commit -m "docs: stockupoperations-summary post-fix capture (migration deployed)"
```

Proceed to Task 5.

---

### Task 4ai: Verdict = (a), partial index present but planner still picks Seq Scan — drop redundant `IX_StockUpOperations_State`

Per arch-review § Decision 3, dropping the bare `State` index forces the planner away from it and onto the partial index. The `(State, CreatedAt)` composite (`IX_StockUpOperations_State_CreatedAt`) covers leading-column `State` equality scans, so `IStockUpOperationRepository.GetByStateAsync` (which executes `WHERE State = @state ORDER BY CreatedAt DESC`, file `backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationRepository.cs:24-30`) remains optimally indexed.

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantStockUpOperationsStateIndex.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantStockUpOperationsStateIndex.Designer.cs` (EF-scaffolded)
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (EF-regenerated)
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs:57-59` (remove `HasIndex(x => x.State)`)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs:68-69` (remove fixture DDL for bare State index, add new test)

- [ ] **Step 1: Verify no other handler depends on the bare `IX_StockUpOperations_State` index**

```bash
# Confirm GetByStateAsync (the only equality+order callsite) uses State, CreatedAt
grep -rn "GetByStateAsync\|GetFailedOperationsAsync" backend/src
```

Verify all callers are read-only state lookups. The repository method `GetByStateAsync` (file `StockUpOperationRepository.cs:24-30`) is `WHERE State = @state ORDER BY CreatedAt DESC` — index `IX_StockUpOperations_State_CreatedAt` covers this with the leading-column scan. No callsite uses the bare `IX_StockUpOperations_State` index uniquely.

If a hidden caller is found that issues `WHERE State = @x` without an `ORDER BY` (and therefore might prefer the smaller bare-State index), document it in the investigation file and reconsider — but for the current codebase no such caller exists.

- [ ] **Step 2: Remove the `HasIndex(x => x.State)` declaration from the EF model**

Edit `backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs`. Delete lines 57–59:

```csharp
        // Index for filtering by state
        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_StockUpOperations_State");
```

So lines 51–67 read (after edit):

```csharp
        // CRITICAL: UNIQUE constraint on DocumentNumber - Layer 1 protection
        builder.HasIndex(x => x.DocumentNumber)
            .IsUnique()
            .HasDatabaseName("IX_StockUpOperations_DocumentNumber_Unique");

        // Composite index for source tracking
        builder.HasIndex(x => new { x.SourceType, x.SourceId })
            .HasDatabaseName("IX_StockUpOperations_Source");

        // Index for failed operations queries (also covers leading-column State equality lookups)
        builder.HasIndex(x => new { x.State, x.CreatedAt })
            .HasDatabaseName("IX_StockUpOperations_State_CreatedAt");
```

Keep the partial-index declaration (lines 69–75) untouched.

- [ ] **Step 3: Scaffold the migration**

```bash
dotnet ef migrations add DropRedundantStockUpOperationsStateIndex \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

This creates three files in `backend/src/Anela.Heblo.Persistence/Migrations/`:
- `{timestamp}_DropRedundantStockUpOperationsStateIndex.cs`
- `{timestamp}_DropRedundantStockUpOperationsStateIndex.Designer.cs`
- updates `ApplicationDbContextModelSnapshot.cs`

The scaffolded `Up` will look like:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "IX_StockUpOperations_State",
        schema: "public",
        table: "StockUpOperations");
}
```

This is **not safe** for production — `DropIndex` runs inside the migration transaction, and on a large table a non-`CONCURRENTLY` drop briefly blocks writers. Per `memory/gotchas/postgres-partial-index-active-states.md` Rule 1, override the scaffold with `migrationBuilder.Sql(..., suppressTransaction: true)`.

- [ ] **Step 4: Rewrite the migration to use `DROP INDEX CONCURRENTLY` with `suppressTransaction: true`**

Open the newly created `{timestamp}_DropRedundantStockUpOperationsStateIndex.cs` and replace the body so it reads:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRedundantStockUpOperationsStateIndex : Migration
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
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS public."IX_StockUpOperations_State";
                """,
                suppressTransaction: true);
        }

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
```

The `.Designer.cs` and `ApplicationDbContextModelSnapshot.cs` files remain as EF scaffolded them — those track the model state, not the SQL.

- [ ] **Step 5: Remove the bare-State index from the integration-test fixture DDL**

Edit `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs`. Locate lines 68–69 inside the `cmd.CommandText` heredoc:

```csharp
                CREATE INDEX IF NOT EXISTS "IX_StockUpOperations_State"
                    ON public."StockUpOperations" ("State");
```

Delete those two lines (and the surrounding blank line so the fixture stays clean). After this edit the DDL block in the test creates only: the table, the unique DocumentNumber index, the Source composite, the State_CreatedAt composite, and the partial active-states index. The bare State index is absent — matching production after the drop migration.

- [ ] **Step 6: Add the new `Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex` test (FR-6 spec amendment 2)**

Append a new `[Fact]` to `GetStockUpOperationsSummaryIntegrationTests.cs`, immediately after the existing `Handle_QueryPlan_DoesNotUseSeqScan` test (which currently ends at line 231).

```csharp
    [Fact]
    public async Task Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex()
    {
        // Production parity test (FR-6): after the drop migration runs in prod, the bare
        // IX_StockUpOperations_State index is gone. Verify the planner still picks the
        // partial index for the summary query in that configuration. Because the fixture
        // already creates the schema without the bare index (see InitializeAsync), we
        // explicitly DROP it here in case a previous test created it, then run the same
        // assertions as Handle_QueryPlan_DoesNotUseSeqScan.

        for (var i = 0; i < 970; i++)
        {
            var op = new StockUpOperation($"DOC-NB-C-{i:D6}", $"P{i}", 1, StockUpSourceType.GiftPackageManufacture, i);
            op.MarkAsCompleted(DateTime.UtcNow);
            _context.Set<StockUpOperation>().Add(op);
        }
        for (var i = 0; i < 30; i++)
        {
            _context.Set<StockUpOperation>().Add(
                new StockUpOperation($"DOC-NB-A-{i:D6}", $"PA{i}", 1, StockUpSourceType.GiftPackageManufacture, 20000 + i));
        }
        await _context.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        // Defensive: ensure the bare State index does not exist in this test's universe.
        await using (var drop = conn.CreateCommand())
        {
            drop.CommandText = "DROP INDEX IF EXISTS public.\"IX_StockUpOperations_State\";";
            await drop.ExecuteNonQueryAsync();
        }

        // ANALYZE so the planner has fresh statistics.
        await using (var analyze = conn.CreateCommand())
        {
            analyze.CommandText = "ANALYZE public.\"StockUpOperations\";";
            await analyze.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            EXPLAIN (FORMAT JSON)
            SELECT "State", COUNT(*)
            FROM public."StockUpOperations"
            WHERE "State" IN (0, 1, 3)
            GROUP BY "State";
            """;
        var planJson = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.DoesNotContain("\"Node Type\": \"Seq Scan\"", planJson);
        Assert.Contains("IX_StockUpOperations_State_Active", planJson);
        Assert.DoesNotContain("IX_StockUpOperations_State\"", planJson); // bare-State index must NOT be picked
    }
```

- [ ] **Step 7: Run the integration tests and confirm all five pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetStockUpOperationsSummaryIntegrationTests" \
  --logger "console;verbosity=detailed"
```

Expected: `Passed!  - Failed: 0, Passed: 5, Skipped: 0`. (The four originals plus the new one.)

If a test fails because the testcontainer keeps a cached image of an old fixture, run `docker container prune -f && docker image prune -f` and retry.

- [ ] **Step 8: Run the existing unit tests and confirm they still pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetStockUpOperationsSummaryHandlerTests"
```

Expected: all green. No changes were made to the handler, so this should be unaffected.

- [ ] **Step 9: Build and format the backend**

```bash
dotnet build backend/Anela.Heblo.sln && dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: `Build succeeded` with `0 Error(s)` and `dotnet format` exits 0. If format fails, run `dotnet format backend/Anela.Heblo.sln` to auto-fix and re-verify.

- [ ] **Step 10: Apply the drop migration to production**

```bash
dotnet ef database update --project backend/src/Anela.Heblo.Persistence \
                          --startup-project backend/src/Anela.Heblo.API \
                          --connection "<production-connection-string>"
```

Expected: `Applying migration '{timestamp}_DropRedundantStockUpOperationsStateIndex'.` then `Done.`. No error.

- [ ] **Step 11: Verify the bare index is gone and the partial index remains valid**

```sql
SELECT indexname FROM pg_indexes
WHERE tablename = 'StockUpOperations'
ORDER BY indexname;
```

Expected rows (alphabetical):
- `IX_StockUpOperations_DocumentNumber_Unique`
- `IX_StockUpOperations_Source`
- `IX_StockUpOperations_State_Active`  *(partial, healthy)*
- `IX_StockUpOperations_State_CreatedAt`
- `StockUpOperations_pkey`

`IX_StockUpOperations_State` (bare) must be **absent**.

- [ ] **Step 12: Re-run Task 1 Steps 2–3 EXPLAIN to confirm the partial index is now picked**

Top scan node must be `Index Only Scan` / `Index Scan` / `Bitmap Index Scan` on `IX_StockUpOperations_State_Active`. Append the post-fix EXPLAIN output and `## Captured: post-fix 2026-05-16` section to the investigation file (mirror the structure in Task 4a Step 4 but mention the drop migration instead).

- [ ] **Step 13: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/*DropRedundantStockUpOperationsStateIndex* \
        backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs \
        backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryIntegrationTests.cs \
        docs/investigations/stockupoperations-summary-slow-query.md
git commit -m "perf: drop redundant IX_StockUpOperations_State to force planner onto partial index"
```

Proceed to Task 5.

---

### Task 4b: Verdict = (b), transient — document and move on

**Files:**
- Modify: `memory/gotchas/postgres-partial-index-active-states.md` (append "Transient class observed" subsection)
- Modify: `docs/investigations/stockupoperations-summary-slow-query.md` (post-fix section already added in Task 2 Step 2)

- [ ] **Step 1: Append the transient-class subsection to the memory gotcha file**

Open `memory/gotchas/postgres-partial-index-active-states.md`. Append a new section at the end:

```markdown

## Recurrences

### 2026-05-16 — Transient spike (13.2 s)

A single `GET /api/StockUpOperations/summary` invocation observed at {spike timestamp UTC} took 13,215 ms. Investigation (`docs/investigations/stockupoperations-summary-slow-query.md` § Verdict (2026-05-16)) showed:

- Partial index `IX_StockUpOperations_State_Active` present and `indisvalid = t`.
- Re-run EXPLAIN showed `Index Only Scan using IX_StockUpOperations_State_Active`, execution time ≤ 5 ms.
- No App Insights restart event within 60 s of the spike.
- `pg_stat_user_tables` snapshot: `dead_pct = X%`, `last_autoanalyze = <recent>`.

**Classified as:** transient — most likely concurrent autovacuum or PostgreSQL checkpoint contention on a hot row in the partial index.

**Action:** none. If recurrences exceed 1/week, re-evaluate the App Insights alert threshold (separate ops ticket, out of scope for the partial-index work).
```

- [ ] **Step 2: Verify no source code, migration, or test was touched in this branch**

```bash
git status
```

Expected: only `memory/gotchas/postgres-partial-index-active-states.md` and `docs/investigations/stockupoperations-summary-slow-query.md` shown as modified (Task 1 + Task 2 + Task 4b changes), plus the `appsettings.*.json` changes from Task 3. No `.cs` files outside Task 3's config changes.

- [ ] **Step 3: Commit**

```bash
git add memory/gotchas/postgres-partial-index-active-states.md
git commit -m "docs: record transient spike class for stockupoperations-summary"
```

Proceed to Task 5.

---

### Task 4c: Verdict = (c), stale statistics — run `VACUUM (ANALYZE)`

**Files:**
- Modify: `memory/gotchas/postgres-partial-index-active-states.md` (append "Statistics-staleness recurrence" subsection)
- Modify: `docs/investigations/stockupoperations-summary-slow-query.md` (append post-vacuum `pg_stat_user_tables` snapshot)

- [ ] **Step 1: Run `VACUUM (ANALYZE)` in a low-traffic window**

```sql
-- Run from a maintenance session. NOT VACUUM FULL — that takes an exclusive lock and blocks writers.
VACUUM (ANALYZE) public."StockUpOperations";
```

`VACUUM (ANALYZE)` (without `FULL`) does not block concurrent writers or readers — it only takes a `ShareUpdateExclusiveLock` which conflicts only with other vacuum/analyze/DDL. It also refreshes the planner statistics needed for partial-index selection.

- [ ] **Step 2: Re-run Task 1 Step 4 — `pg_stat_user_tables`**

```sql
SELECT relname, n_live_tup, n_dead_tup,
       round(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_pct,
       last_vacuum, last_autovacuum, last_analyze, last_autoanalyze
FROM pg_stat_user_tables
WHERE relname = 'StockUpOperations';
```

Expected: `dead_pct` near 0, `last_vacuum` and `last_analyze` updated to now.

- [ ] **Step 3: Re-run Task 1 Steps 2–3 (EXPLAIN ANALYZE) to confirm the partial index is now picked**

Same expectations as Task 4a Step 3 — top scan node must reference `IX_StockUpOperations_State_Active`, no `Seq Scan`.

- [ ] **Step 4: Append the post-vacuum snapshot to the investigation file**

```markdown

## Captured: post-fix 2026-05-16 (VACUUM ANALYZE)

**Action taken:** Ran `VACUUM (ANALYZE) public."StockUpOperations";` at {timestamp UTC}.

**pg_stat_user_tables after vacuum:**
{paste row from Step 2}

**Post-vacuum EXPLAIN (unfiltered):**
\`\`\`
{paste plan output from Step 3}
\`\`\`

**Plan node observed:** Index Only Scan using IX_StockUpOperations_State_Active (or Index Scan / Bitmap Index Scan)

If statistics staleness recurs (last_autoanalyze drifts > 24 h again), file a follow-up to set table-level `autovacuum_analyze_scale_factor` for `StockUpOperations`.

7-day verification — see Task 5.
```

- [ ] **Step 5: Append a recurrence subsection to the memory gotcha file**

Open `memory/gotchas/postgres-partial-index-active-states.md`. Append:

```markdown

## Recurrences

### 2026-05-16 — Statistics staleness

A 13,215 ms `GET /api/StockUpOperations/summary` spike correlated with `last_autoanalyze` drifting > 24 h with `dead_pct = X%`. Resolved by one-off `VACUUM (ANALYZE) public."StockUpOperations"`. If this recurs, consider table-level autovacuum tuning:

\`\`\`sql
ALTER TABLE public."StockUpOperations"
    SET (autovacuum_analyze_scale_factor = 0.02, autovacuum_vacuum_scale_factor = 0.05);
\`\`\`

That change is **not** applied here — it is left for a follow-up if recurrence is observed.
```

- [ ] **Step 6: Commit**

```bash
git add docs/investigations/stockupoperations-summary-slow-query.md \
        memory/gotchas/postgres-partial-index-active-states.md
git commit -m "docs: stockupoperations-summary VACUUM ANALYZE post-fix capture"
```

Proceed to Task 5.

---

### Task 4d: Verdict = (d), cold start — file separate warm-up ticket

**Files:**
- Modify: `memory/gotchas/postgres-partial-index-active-states.md` (append "Cold-start spike" subsection)

- [ ] **Step 1: File the warm-up follow-up issue**

```bash
gh issue create \
  --title "perf(api): investigate Web App cold-start latency on first request" \
  --label "performance" \
  --body "$(cat <<'EOF'
## Background

A 13,215 ms `GET /api/StockUpOperations/summary` invocation on {YYYY-MM-DD HH:MM UTC} was classified as a cold-start spike (see `docs/investigations/stockupoperations-summary-slow-query.md` § Verdict). The application restarted {N} seconds before the slow request on the same `cloud_RoleInstance`.

## Out of scope for the partial-index fix

The partial-index work on branch `feat-performance-slow-response-times-on-get-s` addressed the planner-driven Seq Scan. Cold-start latency is a separate concern — likely first-request DB-pool warm-up, EF model build, and Application Insights initialization.

## Suggested next steps

- Enable Azure Web App "Always On" if not already set.
- Add a warm-up endpoint hit to `WEBSITE_WARMUP_PATH`.
- Inspect connection-pool `MinPoolSize` in `appsettings.Production.json` `Database` block.

## Acceptance

First-request p95 after a Web App restart ≤ 2 s.
EOF
)"
```

Capture the resulting issue URL in your scratch file — it goes into the gotcha entry.

- [ ] **Step 2: Append the cold-start subsection to the memory gotcha file**

```markdown

## Recurrences

### 2026-05-16 — Cold-start spike

A 13,215 ms `GET /api/StockUpOperations/summary` invocation correlated with an Azure Web App restart on the same `cloud_RoleInstance` within ~{N} seconds before the request. Partial-index plan was healthy on re-run.

**Action:** no schema/code change; warm-up work tracked separately at {issue URL from Step 1}.
```

- [ ] **Step 3: Commit**

```bash
git add memory/gotchas/postgres-partial-index-active-states.md
git commit -m "docs: record cold-start spike class for stockupoperations-summary"
```

Proceed to Task 5.

---

## Task 5: 7-day post-fix verification (FR-5)

**Files:**
- Modify: `docs/investigations/stockupoperations-summary-slow-query.md` (append `## 7-day verification 2026-05-23` section after 7 days have elapsed since the corrective action)

The endpoint must meet NFR-1 latency targets for at least 7 continuous days after the corrective action from Task 4.

- [ ] **Step 1: Schedule a follow-up 7 days after Task 4 completion**

This task is intentionally calendar-bound. The operator who completes Task 4 records the completion timestamp in the investigation file and schedules a recurring reminder for `{completion timestamp} + 7 days` to run the verification queries below.

- [ ] **Step 2: Run the Application Insights percentile query**

In the Azure portal → Application Insights → Logs, with the time range set to **"Last 7 days"** starting at the Task 4 completion timestamp:

```kusto
requests
| where timestamp between (datetime('<task-4-completion>') .. datetime('<task-4-completion>') + 7d)
| where name == "GET StockUpOperations/GetSummary" or url has "/api/StockUpOperations/summary"
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99),
    max_duration = max(duration),
    count = count(),
    over_10s = countif(duration > 10000)
```

Expected:
- `p50 ≤ 100`
- `p95 ≤ 300`
- `p99 ≤ 1000`
- `over_10s == 0`

If any of these fail, the corrective action did not hold — re-open the verdict at Task 2 and restart from Task 1.

- [ ] **Step 3: Verify the FR-7 structured log is still arriving and queryable**

Repeat Task 3 Step 6's KQL query against the last 7 days. Expected: at least one `traces` row per day, with `customDimensions.ElapsedMs`, `customDimensions.PendingCount`, `customDimensions.SubmittedCount`, `customDimensions.FailedCount` populated. If the dimensions are missing, fix that before claiming FR-5 done.

- [ ] **Step 4: Append the 7-day verification section to the investigation file**

```markdown

## 7-day verification 2026-05-23

**Window:** {start timestamp UTC} → {end timestamp UTC}

**App Insights percentiles:**
| Metric | Target | Observed |
|---|---|---|
| p50 | ≤ 100 ms | {observed} ms |
| p95 | ≤ 300 ms | {observed} ms |
| p99 | ≤ 1000 ms | {observed} ms |
| max | < 10000 ms | {observed} ms |
| invocations > 10 s | 0 | {observed} |

**Outcome:** PASS (all NFR-1 targets met) / FAIL (re-open verdict)

**Structured log surfacing (FR-7):** confirmed via `traces | where message has "GetStockUpOperationsSummary completed in"` query — N rows observed in the 7-day window with full customDimensions payload.
```

- [ ] **Step 5: Commit and close**

```bash
git add docs/investigations/stockupoperations-summary-slow-query.md
git commit -m "docs: stockupoperations-summary 7-day post-fix verification complete"
```

The feature is complete when this commit lands.

---

## Task 6: Final regression check (FR-6)

Run unconditionally before merge. Must produce green tests regardless of which Task 4 branch was taken.

**Files:** none modified — verification only.

- [ ] **Step 1: Run the full StockUpOperationsSummary test surface**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetStockUpOperationsSummary" \
  --logger "console;verbosity=normal"
```

Expected counts by Task 4 path:
- 4a / 4b / 4c / 4d (no test changes): `Passed: <existing count>, Failed: 0, Skipped: 0` (4 integration tests + the unit tests in `GetStockUpOperationsSummaryHandlerTests`).
- 4ai (added one new integration test): one more passing test than the baseline.

Zero skipped is mandatory per FR-6 — investigate any `Skipped` count before declaring success.

- [ ] **Step 2: Run the full backend build and format check**

```bash
dotnet build backend/Anela.Heblo.sln && dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: `Build succeeded` with `0 Error(s)`. `dotnet format` exits 0.

- [ ] **Step 3: No commit needed**

This task is verification only. If everything passes, the implementation is ready for PR.

---

## Self-Review Notes

**Spec coverage:**
- FR-1: Task 0
- FR-2: Task 1
- FR-3: Task 2
- FR-4: Task 4 (branches 4a / 4ai / 4b / 4c / 4d, one chosen)
- FR-5: Task 5
- FR-6: Task 6 (with Task 4ai adding the spec-amended `Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex` test when relevant)
- FR-7: Task 3 (LogLevel config) + Task 5 Step 3 (verify post-deploy)
- NFR-1: verified by Task 5 Step 2 percentile query
- NFR-2: enforced in Task 1 by requiring read-only credentials and aggregate-only queries
- NFR-3: Task 4ai migration uses `DROP INDEX CONCURRENTLY` + `suppressTransaction: true` per the established codebase rule
- NFR-4: handler's `ActiveStates` array is untouched; the migration's literal `(0, 1, 3)` is documented inline
- NFR-5: post-fix capture in the investigation file is mandatory in every Task 4 branch

**Placeholder scan:** All steps contain concrete file paths, commands, and code. `{spike timestamp}` and `{timestamp}` markers are user-supplied values captured at execution time (not unresolved planning placeholders).

**Type consistency:**
- `IX_StockUpOperations_State_Active` (partial index) is referenced identically across handler comment, migration SQL, EF `HasFilter`, integration-test DDL, EXPLAIN-assertion test, and gotcha file.
- `IX_StockUpOperations_State` (bare index, target of the drop) is named identically in `StockUpOperationConfiguration.cs:58-59` (removed), migration SQL (DROP), test fixture (removed at line 68-69), and the new `Handle_QueryPlan_DoesNotUseSeqScan_WithoutBareStateIndex` defensive `DROP IF EXISTS`.
- `ActiveStates` array values `(0, 1, 3)` literal-match `Pending=0, Submitted=1, Failed=3` from `StockUpOperationState` enum in every file that references them.
