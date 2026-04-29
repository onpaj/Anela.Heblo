# Specification: Fix `GET /api/data-quality/runs` 500 Errors (Missing `dqt_runs` Relation)

## Summary
The Data Quality runs listing endpoint is returning HTTP 500 errors at 7× the baseline rate because EF Core queries are targeting a relation (`dqt_runs`) that no longer exists in the production database — the table was renamed to `DqtRuns` by a later migration that has not been fully reconciled across the production database and running application instances. This spec covers diagnosis, remediation of the immediate incident, and durable safeguards against schema/code drift for future migrations.

## Background
- Feature: Data Quality Runs (`DqtRun` entity), surfaced via `GET /api/data-quality/runs`.
- Migration history:
  - `20260424060451_AddDataQualityTables` — creates table as `dqt_runs` (snake_case, `public` schema).
  - `20260424142720_StandardizeTableNamingToPascalCase` — renames to `DqtRuns` (PascalCase).
- Current code mapping: `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs` → `builder.ToTable("DqtRuns", "public")`.
- Observed in production telemetry (last 24h):
  - **8 × HTTP 500** on `GET DataQuality/GetRuns` vs 7-day baseline ~1.1/day (≈7× spike).
  - **4 × slow-request hits, avg 3,716 ms** on the same endpoint (consistent with error-path latency or query timeout before 500).
- Underlying exception:
  ```
  Npgsql.PostgresException: 42P01: relation "dqt_runs" does not exist
  POSITION: 27
  ```
- Probable causes (one or both):
  1. `StandardizeTableNamingToPascalCase` ran in CI/staging but **not on the production database**; production still has `dqt_runs` while deployed code expects `DqtRuns`.
  2. **Deployment drift**: a previous code revision (mapped to `dqt_runs`) is still being served by some Azure App Service instances after the DB has been renamed (or vice versa).
- Project context: database migrations are **manual** in this repo (not automated by deployment), making code/schema drift a known operational risk.

## Functional Requirements

### FR-1: Diagnose Migration State on Production Database
Establish ground truth for which Data Quality migrations have actually been applied on the production PostgreSQL database before taking corrective action.

**Acceptance criteria:**
- Operator runs and captures the output of:
  ```sql
  SELECT "MigrationId", "ProductVersion"
  FROM "__EFMigrationsHistory"
  WHERE "MigrationId" LIKE '%DataQuality%'
     OR "MigrationId" LIKE '%StandardizeTable%'
  ORDER BY "MigrationId";
  ```
- Operator runs and captures the output of:
  ```sql
  SELECT table_schema, table_name
  FROM information_schema.tables
  WHERE table_schema = 'public'
    AND lower(table_name) IN ('dqt_runs', 'dqtruns');
  ```
- The diagnosis result is one of three documented states:
  - **A**: history shows both migrations applied AND `DqtRuns` exists → code/DB consistent; investigate stale app instances.
  - **B**: history shows only `AddDataQualityTables` AND `dqt_runs` exists → `StandardizeTableNamingToPascalCase` must be applied.
  - **C**: history shows both applied but `dqt_runs` still exists (or both tables exist) → manual intervention/rollback required; escalate.
- Findings are recorded in `memory/context/state.md` and (if novel) `memory/gotchas/`.

### FR-2: Apply Pending Migration to Production (If State B)
If `StandardizeTableNamingToPascalCase` is missing on production, apply it so that the physical schema matches the deployed code.

**Acceptance criteria:**
- The pending migration is applied via the project's standard manual migration procedure (see `docs/development/setup.md`); no destructive `--force` flags are used.
- Post-apply, `__EFMigrationsHistory` contains `20260424142720_StandardizeTableNamingToPascalCase`.
- Post-apply, `information_schema.tables` shows `DqtRuns` (PascalCase) and **no** `dqt_runs`.
- A smoke check `GET /api/data-quality/runs` returns HTTP 200 with a JSON payload (empty list or rows) within < 2,000 ms.
- A rollback note is recorded specifying that, if the apply must be reverted, the inverse rename is `ALTER TABLE "DqtRuns" RENAME TO dqt_runs;` (acknowledging this only restores the table name; data is preserved).

### FR-3: Reconcile Running Application Instances (If State A or after FR-2)
Eliminate the possibility that an older code revision (mapped to `dqt_runs`) is still being served by an Azure App Service instance after the DB has been renamed.

**Acceptance criteria:**
- The current production deployment's container image digest/tag is recorded.
- The image digest is verified to be from the commit that contains `DqtRunConfiguration.ToTable("DqtRuns", "public")`.
- A full rolling restart of the Azure Web App for Containers is performed.
- Post-restart, no `42P01: relation "dqt_runs" does not exist` errors appear in the next 30 minutes of telemetry.

### FR-4: Verification Window
Confirm the fix in production over a sustained observation window before closing the incident.

**Acceptance criteria:**
- For **2 hours** post-fix: zero HTTP 500 responses on `GET /api/data-quality/runs`.
- For **2 hours** post-fix: zero `42P01` PostgresException entries referencing `dqt_runs` in application logs.
- Slow-request telemetry on the endpoint returns to baseline (< 1,000 ms p95).
- A summary (root cause confirmed, action taken, evidence links) is appended to `memory/gotchas/`.

### FR-5: Deployment Smoke Test for Data Quality Schema
Add a lightweight automated check so that future migration/code drift on this entity is detected at deploy-time, not by user-facing 500s.

**Acceptance criteria:**
- A health check or post-deploy smoke test executes a minimal EF Core query against `DqtRuns` (e.g., `_db.DqtRuns.Take(1).ToListAsync()` or equivalent existence probe) on application startup or as a `/health/ready` dependency.
- The check fails fast (returns unhealthy) if the relation does not exist, rather than allowing the app to serve traffic that will 500.
- Failure mode is logged with structured fields: `entity=DqtRun`, `expectedTable=DqtRuns`, `schema=public`, and the underlying exception message.
- The check covers all tables affected by `StandardizeTableNamingToPascalCase` (not only `DqtRuns`) — see Open Questions for scope.
- Unit/integration tests cover both the "table exists" (healthy) and "table missing" (unhealthy) paths.

### FR-6: Migration Runbook Update
Codify the lesson so the next migration does not repeat the drift.

**Acceptance criteria:**
- `docs/development/setup.md` (or the dedicated migrations doc, if present) is updated with:
  - A pre-deploy checklist requiring verification that all pending migrations are applied to production before rolling out application code that depends on them.
  - A post-deploy verification step that hits the new smoke endpoint from FR-5.
  - Explicit guidance on the ordering hazard demonstrated by `AddDataQualityTables` → `StandardizeTableNamingToPascalCase`.

## Non-Functional Requirements

### NFR-1: Performance
- `GET /api/data-quality/runs` p95 latency must return to baseline (< 1,000 ms) after the fix.
- The FR-5 smoke check must add < 50 ms to readiness probe latency in the healthy path.

### NFR-2: Security
- No production credentials, connection strings, or tokens are committed to source control during diagnosis or remediation (CLAUDE.md §1).
- Database queries used for diagnosis (FR-1) are read-only.
- Migration apply (FR-2) is performed via the standard authenticated tooling; no ad-hoc `psql` shell access with embedded credentials.

### NFR-3: Reliability / Operability
- The fix must be deployable without data loss; the rename migration preserves rows.
- The smoke test (FR-5) must fail closed (unhealthy) on schema mismatch, not silently degrade.
- All diagnostic SQL and remediation actions must be reversible or, if not, explicitly flagged in the runbook.

### NFR-4: Observability
- Post-fix, error-rate dashboards for `GET /api/data-quality/runs` should be reviewable; no new dashboards required, but the existing telemetry must show the recovery.

## Data Model
No schema changes are introduced by this spec — only reconciliation of an already-defined schema.

- **Entity**: `DqtRun` (Data Quality run record).
- **EF Core mapping** (current, authoritative): `DqtRuns` table, `public` schema (`DqtRunConfiguration.cs`).
- **Migration chain**:
  - `20260424060451_AddDataQualityTables` — creates `dqt_runs`.
  - `20260424142720_StandardizeTableNamingToPascalCase` — renames `dqt_runs` → `DqtRuns`.
- **Expected production state after fix**: history contains both migrations; physical table is `DqtRuns`; no orphan `dqt_runs` exists.

## API / Interface Design
No public API surface changes.

- **Affected endpoint**: `GET /api/data-quality/runs` (controller method `GetRuns`).
- **Pre-fix behavior**: HTTP 500 + `Npgsql.PostgresException: 42P01: relation "dqt_runs" does not exist`.
- **Post-fix behavior**: HTTP 200 with the existing response contract (unchanged); empty list permitted.
- **New internal interface (FR-5)**: a readiness/health-check probe that queries `DqtRuns`. Whether this is exposed under an existing `/health/ready` endpoint or added as a startup self-check is to be decided by the architect (see Open Questions).

## Dependencies
- **PostgreSQL** production database (Azure-hosted).
- **EF Core** migrations infrastructure already in the repo.
- **Azure Web App for Containers** — for rolling restart in FR-3.
- **Existing telemetry** (Application Insights or equivalent) — for the verification window in FR-4.
- **Manual migration tooling** — current process per `docs/development/setup.md`; this incident does not introduce automated migrations.

## Out of Scope
- Automating database migrations as part of the deployment pipeline (separate, larger initiative; explicitly listed as manual in CLAUDE.md).
- Reverting the PascalCase naming standardization or revisiting the naming convention itself.
- Broader audit of every renamed table from `StandardizeTableNamingToPascalCase` beyond what FR-5 requires for smoke coverage.
- Adding new fields, indices, or query capabilities to `DqtRun` / `DqtRuns`.
- UI/frontend changes — the endpoint contract is unchanged, so the React app requires no modifications.
- Backfilling or repairing any historical run rows (the rename is non-destructive; data is intact).

## Open Questions
1. **Diagnosis result**: Which of states A / B / C from FR-1 actually applies in production? FR-2 vs FR-3 scope depends on the answer.
2. **Smoke check surface (FR-5)**: Should the probe be added to an existing `/health` / `/health/ready` endpoint (preferred if one exists), or run as a startup self-check that prevents the app from going healthy until satisfied? Need architect confirmation of the current health-check infrastructure.
3. **Smoke check scope**: Should FR-5 cover *only* `DqtRuns` (minimal, scoped to this incident) or *all* tables touched by `StandardizeTableNamingToPascalCase` (broader regression protection)? Recommendation: minimal for this PR, with a follow-up to broaden.
4. **Stale-instance evidence**: Is there an authoritative way to confirm that all Azure App Service instances are running the same image digest (e.g., via Azure CLI / portal), or do we rely on a forced rolling restart as the safe default?
5. **Migration apply authority**: Who runs the production migration (FR-2) — the developer, an ops process, or via a one-shot Hangfire/admin task? The repo's current convention is "manual"; the exact actor and command should be confirmed before execution.
6. **Incident window**: Is the 8-failure spike still ongoing, or has it self-resolved? If self-resolved, that may indicate state A (drifted instance now drained) and shifts emphasis from FR-2 to FR-3 + FR-5.
7. **Assumption**: This spec assumes the production database is the single source of truth and that the rename migration is safe to apply with current data volumes. If `dqt_runs`/`DqtRuns` contains a non-trivial row count, FR-2 should include a row-count snapshot before/after to prove no data loss.