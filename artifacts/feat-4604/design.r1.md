# Design: Unit tests for PollyExecutionStrategy

## Component Design
Single xUnit test class `PollyExecutionStrategyTests` in namespace `Anela.Heblo.Tests.Persistence.Resilience`.

Helpers (private, in-class):
- `CreateSut(int maxRetries, out MetricCapture capture, out TestLogger logger)`: builds the retry pipeline (zero delay, `ShouldHandle` on a test `TimeoutException`/custom exception), stub `IDbResiliencePipelineProvider`, `DbResilienceMetrics` over a test `IMeterFactory`, and `ExecutionStrategyDependencies`.
- `MetricCapture`: wraps `MeterListener` filtered to the meter instance, records (instrument name, value, tags dictionary).

Tests (sync + async variants):
1. `Execute_SucceedsFirstAttempt_RecordsNoRetryMetrics`
2. `Execute_SucceedsOnSecondAttempt_RecordsRetrySuccessWithAttempts2`
3. `Execute_FailsAfterRetries_SetsDataRecordsFailureLogsAndRethrowsSameInstance`
4. `Execute_NonRetryableFailureOnFirstAttempt_SetsAttempts1`
5. `RetriesOnFailure_IsTrue` (real assertion; replaces the placeholder)

## Data Schemas
Metrics: `db.retry.success{total_attempts:int}`, `db.retry.failure{exception.type:string,total_attempts:int}`. Exception data key: `Anela.DbRetryAttempts` (int). Log message template: `DbTransientRetryExhausted attempts={Attempts} exception.type={ExceptionType}`.
