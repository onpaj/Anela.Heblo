### task: success-path-tests

Depends on: build-test-harness. Add success-path tests for sync and async (FR-1, FR-2), each with `[Fact]`:
1. `Execute_SucceedsFirstAttempt_RecordsNoRetryMetrics` / `ExecuteAsync_...`: operation returns a value on first call; assert the result, that the operation ran once, and that capture contains no `db.retry.success` and no `db.retry.failure`.
2. `Execute_SucceedsOnSecondAttempt_RecordsRetrySuccessWithAttempts2` / `ExecuteAsync_...`: operation throws `TimeoutException` on the first call and returns on the second; assert the result, exactly one `db.retry.success` measurement of value 1 with tag `total_attempts` == 2, and no failure metric.

Call signature: `sut.Execute<int, int>(0, (_, _) => ..., null)` and `await sut.ExecuteAsync<int, int>(0, (_, _, _) => Task.FromResult(...), null, CancellationToken.None)`.

Done when: the four tests pass. Commit message: `test(resilience): PollyExecutionStrategy success-path tests`.

