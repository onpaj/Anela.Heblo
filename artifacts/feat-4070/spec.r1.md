# Specification: Npgsql connect-path cancellation bursts (OperationCanceledException@NpgsqlConnector.ConnectAsync)

## Summary
Telemetry shows 132 `OperationCanceledException` at `NpgsqlConnector.ConnectAsync` in two one-minute bursts (2026-09-01 07:45Z, 2026-09-04 04:30Z), 72 of them from `GET /health/ready`. The fixed 3 s ceiling matches `Database:Resilience:TotalTimeBudget` (Polly per-attempt timeout), so these are Polly-cancelled connection attempts during a transient DB-connectivity blip. Request impact was minimal; the work is to make the readiness probe resilient to a short blip, make the signal diagnosable, and avoid chasing raw exception counts.

## Background
- `TotalTimeBudget` was reduced 10 s -> 3 s (appsettings, Staging, Production) in the #3971/#3779 work; `DbResiliencePipelineProvider` builds `AddRetry(...).AddTimeout(TotalTimeBudget)` so the timeout is per attempt and cancels in-flight `ConnectAsync`, producing `OperationCanceledException` / "didn't complete within the allowed timeout of '00:00:03'".
- `/health/ready` runs the `AddNpgSql` check (shared `NpgsqlDataSource`) plus two schema checks tagged `db`; it has no retry. The Dockerfile `HEALTHCHECK` uses `/health` (no DB), so the container itself is not restarted by a DB blip; whether the Azure Web App probe targets `/health/ready` is unverified.
- Existing observability: `NpgsqlConnectionInterceptor` (pool-wait metric, `ConnectionFailed` logging), alert rules in `docs/architecture/infrastructure.md`.
- Related: #3971 (write-path), docs/routines/telemetry-anomaly/2026-07-30-npgsql-cancellation-pool-correlation.md.

## Functional Requirements

### FR-1: Readiness probe tolerates a short DB blip
`/health/ready` must not report Unhealthy for a single failed/cancelled DB connect attempt; it retries (bounded, e.g. 1 retry, short delay) before failing, and total probe time stays below typical probe timeouts.
**Acceptance criteria:**
- Unit test: DB check that fails once then succeeds yields Healthy.
- Unit test: persistent failure yields Unhealthy within the bounded time.
- Liveness (`/health/live`) and `/health` remain DB-independent (unchanged).

### FR-2: Cancellation exceptions are classified correctly
Confirm (test) that a Polly `TimeoutRejectedException` wrapping/caused by `OperationCanceledException` from `ConnectAsync` is retried by `TransientErrorClassifier`, while caller-initiated cancellation (request aborted token) is NOT retried.
**Acceptance criteria:**
- Tests in `Persistence/Resilience` cover both cases; fix classifier only if a test fails.

### FR-3: Diagnosable evidence
Document a KQL confirm/refute query (server-side vs. pool-side cause for the two bursts, split by caller incl. `/health/ready`) in `docs/integrations/db-connection-health-kql.md`; record that raw `ConnectAsync` exception count is not the success metric (use `db.retry.success`/`db.retry.failure`, request failure rate), consistent with the 2026-07-30 note.
**Acceptance criteria:**
- Doc contains the query and the interpretation guidance.

### FR-4: Timeout/pool tuning only on evidence
No change to `TotalTimeBudget`, `MaxPoolSize` or Npgsql `Timeout` unless FR-3 evidence shows app-side exhaustion; the decision is recorded in the doc.

## Non-Functional Requirements
### NFR-1: Performance
Readiness probe worst case <= ~8 s; no added load on the pool in steady state.
### NFR-2: Security
No new endpoints or data exposure.

## Data Model
None.

## API / Interface Design
No API change. `/health/ready` response shape unchanged.

## Dependencies
`AspNetCore.HealthChecks.NpgSql`, Polly (already referenced), Application Insights access for FR-3 (manual).

## Out of Scope
Postgres server-side/Azure metric inspection (manual ops step), changing Hangfire connection limits, #3971 write-path work.

## Open Questions
None.

## Status: COMPLETE
