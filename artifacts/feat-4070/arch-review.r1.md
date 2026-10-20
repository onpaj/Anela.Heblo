# Architecture Review: Npgsql connect-path cancellation bursts

## Skip Design: true
Backend-only (health check wiring, tests, docs); no UI.

## Architectural Fit Assessment
Fits the existing resilience layout: Polly pipeline in `Anela.Heblo.Persistence/Infrastructure/Resilience`, health checks registered in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.AddHealthCheckServices` and mapped in `ApplicationBuilderExtensions.ConfigureHealthCheckEndpoints`. Root cause is design-consistent behaviour: the 3 s per-attempt Polly timeout cancels connects during a DB blip. The DB health check (`AddNpgSql` on the shared `NpgsqlDataSource`) bypasses the Polly pipeline, so it has no retry and fails on the first blip.

## Proposed Architecture

### Component Overview
`/health/ready` -> HealthCheckService -> [existing: AddNpgSql check] replaced/wrapped by a retrying decorator -> NpgsqlDataSource.
Unchanged: Polly `DbResiliencePipelineProvider`, `PollyExecutionStrategy`, `NpgsqlConnectionInterceptor`.

### Key Design Decisions

#### Decision 1: Retry inside the readiness check, not via the EF Polly pipeline
**Options:** (a) raise `TotalTimeBudget`; (b) route health check through the Polly pipeline; (c) small retrying wrapper around the DB health check.
**Chosen:** (c) a thin `RetryingHealthCheck` decorator (1 retry, ~500 ms delay, each attempt bounded by the check's own timeout) applied only to the DB-tagged connectivity check.
**Rationale:** (a) reverses #3971's deliberate tuning and masks nothing; (b) couples health to EF resilience metrics and would inflate `db.retry.*` signals. (c) is local and testable.

#### Decision 2: No pool/timeout tuning without evidence
Per the 2026-07-30 note, `MaxPoolSize`/`TotalTimeBudget` stay unchanged; FR-3 KQL decides.

#### Decision 3: Verify before changing the classifier
Add tests for FR-2; change `TransientErrorClassifier` only if they fail. Caller cancellation (`CancellationToken.IsCancellationRequested`) must never be retried.

## Implementation Guidance

### Directory / Module Structure
- New: `backend/src/Anela.Heblo.API/HealthChecks/RetryingHealthCheck.cs` (or equivalent beside existing `HealthChecks/DataQuality`).
- Edit: `ServiceCollectionExtensions.AddHealthCheckServices` to register the DB check through the decorator.
- Tests: `backend/test/Anela.Heblo.Tests/API/HealthChecks/` and `.../Persistence/Resilience/`.
- Docs: `docs/integrations/db-connection-health-kql.md`; `docs/architecture/infrastructure.md` (note on readiness retry).

### Interfaces and Contracts
Decorator implements `IHealthCheck`, wraps an inner `IHealthCheck` factory; returns first Healthy result, otherwise the last result; honours the passed `CancellationToken`; logs a warning on retry.

### Data Flow
Probe -> decorator attempt 1 -> (fail) delay -> attempt 2 -> result; response body format unchanged (UIResponseWriter).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Retry lengthens probe past platform timeout | Medium | Bounded: 1 retry, short delay, per-attempt timeout; test worst case |
| Masks real outage | Low | Persistent failure still Unhealthy; logs retained |
| Raw exception count stays high | Low | Documented; use retry success/failure metrics |
| Process doc rule (CLAUDE.md) | Low | Verify whether a `docs/processes` doc owns health checks; update/bump `verified_at` and run `python3 scripts/process-docs/check.py index` if CI requires |

## Specification Amendments
None.

## Prerequisites
None. Manual: check Azure Postgres metrics at the two burst times and the Web App health-probe path (ops, outside code).
