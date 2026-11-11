# Specification: Unit tests for PollyExecutionStrategy

## Summary
Add real unit tests for `PollyExecutionStrategy` (Persistence/Infrastructure/Resilience), whose line coverage is 19.4%. Cover the retry-success metric branch and the exception-enrichment/failure-metric catch path for both `Execute` and `ExecuteAsync`.

## Background
`backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs` exists but contains only a placeholder (`expected.Should().BeTrue()`) and tests nothing. The brief says no file exists; the correct action is to replace the placeholder. The brief's snippet also simplifies the signature: the real call is `RecordRetryFailure(string exceptionType, int totalAttempts)` with `ex.GetType().FullName ?? "unknown"`; the catch also logs error `DbTransientRetryExhausted`.

## Functional Requirements

### FR-1: Single-attempt success records no retry metric
**Acceptance criteria:**
- Sync and async: operation succeeds first time, result returned, `db.retry.success` and `db.retry.failure` not recorded.

### FR-2: Multi-attempt success records retry success
**Acceptance criteria:**
- Pipeline retries once (attempt = 2); `db.retry.success` recorded once with tag `total_attempts=2`; result returned; no failure metric.

### FR-3: Failure enriches exception and records failure
**Acceptance criteria:**
- On exhausted/non-retried exception, same exception instance is rethrown (not wrapped), `ex.Data["Anela.DbRetryAttempts"]` equals attempts made, `db.retry.failure` recorded with tags `exception.type` = exception full type name and `total_attempts`, and an error log is emitted.
- Cover both a first-attempt failure (attempts=1) and failure after retries (attempts=N).

### FR-4: Remove placeholder test
Replace the tautological `RetriesOnFailure_ReturnsTrue_ByDesign` with a real assertion on `RetriesOnFailure`.

## Non-Functional Requirements
### NFR-1: Performance
Tests must run in milliseconds; no real delays (zero-delay retry pipeline) and no database.
### NFR-2: Security
None; no production code behaviour change.

## Data Model
None.

## API / Interface Design
No production API change. Metrics observed via `MeterListener`/`IMeterFactory` against meter `Anela.Heblo.Database.Resilience`.

## Dependencies
`ExecutionStrategyDependencies` (EF Core), `IDbResiliencePipelineProvider`, `DbResilienceMetrics`, Polly `ResiliencePipeline`, xUnit, FluentAssertions, existing Moq usage in the test project.

## Out of Scope
Production code changes; changes to pipeline provider tests.

## Open Questions
None blocking.
