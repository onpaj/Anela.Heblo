### task: failure-path-tests

Depends on: build-test-harness. Add failure-path tests for sync and async (FR-3):
1. `Execute_FailsAfterRetries_SetsDataRecordsFailureLogsAndRethrowsSameInstance` / `ExecuteAsync_...`: pipeline of 3 attempts, operation always throws the same `TimeoutException` instance. Use `Assert.Throws`/`ThrowsAsync` (or FluentAssertions `Should().Throw`) and assert `BeSameAs(thrown)`; `ex.Data["Anela.DbRetryAttempts"]` equals 3; exactly one `db.retry.failure` measurement with tags `exception.type` == `typeof(TimeoutException).FullName` and `total_attempts` == 3; no `db.retry.success`; error log verified once with that exception.
2. `Execute_NonRetryableFailureOnFirstAttempt_SetsAttempts1` / `ExecuteAsync_...`: operation throws `InvalidOperationException`; assert attempts data == 1, failure metric tags `exception.type` == `System.InvalidOperationException` and `total_attempts` == 1, same instance rethrown, error logged.

Done when: the four tests pass and the whole class passes with the filter above. Commit message: `test(resilience): PollyExecutionStrategy failure-path tests`.

