# Task Plan: Unit tests for PollyExecutionStrategy (feat-4604)

Goal: replace the placeholder in `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs` with real unit tests covering `Execute` and `ExecuteAsync`. Test-only change, no production code edits, no process-doc impact (no behaviour change).

Verification commands (run from `backend/`):
- `dotnet build test/Anela.Heblo.Tests`
- `dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~PollyExecutionStrategyTests"`
- `dotnet format --include test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs`

### task: build-test-harness

Rewrite `PollyExecutionStrategyTests.cs` (delete placeholder `RetriesOnFailure_ReturnsTrue_ByDesign`) and add the private harness only.

Steps:
1. Add a private `TestMeterFactory : IMeterFactory` (same shape as the one at the bottom of `DbResiliencePipelineProviderTests.cs`; it creates and tracks `Meter`s and disposes them).
2. Add `MetricCapture : IDisposable` wrapping a `MeterListener` that only enables instruments whose `Meter` is created by this test's factory (filter by meter instance, not global name) and records `(string Name, long Value, Dictionary<string, object?> Tags)` for `db.retry.success` and `db.retry.failure`. Call `listener.Start()`; dispose in the test.
3. Add a private `StubPipelineProvider : IDbResiliencePipelineProvider` returning a supplied `ResiliencePipeline`.
4. Add `CreateSut(int maxAttempts, ...)` returning the `PollyExecutionStrategy`, the `MetricCapture` and a logger mock. Pipeline: `new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = maxAttempts - 1, Delay = TimeSpan.Zero, BackoffType = DelayBackoffType.Constant, ShouldHandle = new PredicateBuilder().Handle<TimeoutException>() }).Build()`. Non-retryable exception for the first-attempt-failure case: `InvalidOperationException`.
5. `ExecutionStrategyDependencies`: create a `DbContext` from `DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString())`; build `new ExecutionStrategyDependencies(new CurrentDbContext(ctx), ((IInfrastructure<IServiceProvider>)ctx).Instance.GetRequiredService<IDbContextOptions>(), Mock.Of<IDiagnosticsLogger<DbLoggerCategory.Infrastructure>>())` (verify the constructor shape against EF Core 8.0.8; adjust if needed). Dispose the context at the end of each test.
6. Logger: `Mock<ILogger<PollyExecutionStrategy>>` with `IsEnabled` returning true so `LogError` calls can be verified via `Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("DbTransientRetryExhausted")), ex, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once)`.
7. Keep one real test: `RetriesOnFailure_IsTrue` asserting `sut.RetriesOnFailure.Should().BeTrue()`.

Done when: file compiles and `RetriesOnFailure_IsTrue` passes. Commit message: `test(resilience): PollyExecutionStrategy test harness`.

### task: success-path-tests

Depends on: build-test-harness. Add success-path tests for sync and async (FR-1, FR-2), each with `[Fact]`:
1. `Execute_SucceedsFirstAttempt_RecordsNoRetryMetrics` / `ExecuteAsync_...`: operation returns a value on first call; assert the result, that the operation ran once, and that capture contains no `db.retry.success` and no `db.retry.failure`.
2. `Execute_SucceedsOnSecondAttempt_RecordsRetrySuccessWithAttempts2` / `ExecuteAsync_...`: operation throws `TimeoutException` on the first call and returns on the second; assert the result, exactly one `db.retry.success` measurement of value 1 with tag `total_attempts` == 2, and no failure metric.

Call signature: `sut.Execute<int, int>(0, (_, _) => ..., null)` and `await sut.ExecuteAsync<int, int>(0, (_, _, _) => Task.FromResult(...), null, CancellationToken.None)`.

Done when: the four tests pass. Commit message: `test(resilience): PollyExecutionStrategy success-path tests`.

### task: failure-path-tests

Depends on: build-test-harness. Add failure-path tests for sync and async (FR-3):
1. `Execute_FailsAfterRetries_SetsDataRecordsFailureLogsAndRethrowsSameInstance` / `ExecuteAsync_...`: pipeline of 3 attempts, operation always throws the same `TimeoutException` instance. Use `Assert.Throws`/`ThrowsAsync` (or FluentAssertions `Should().Throw`) and assert `BeSameAs(thrown)`; `ex.Data["Anela.DbRetryAttempts"]` equals 3; exactly one `db.retry.failure` measurement with tags `exception.type` == `typeof(TimeoutException).FullName` and `total_attempts` == 3; no `db.retry.success`; error log verified once with that exception.
2. `Execute_NonRetryableFailureOnFirstAttempt_SetsAttempts1` / `ExecuteAsync_...`: operation throws `InvalidOperationException`; assert attempts data == 1, failure metric tags `exception.type` == `System.InvalidOperationException` and `total_attempts` == 1, same instance rethrown, error logged.

Done when: the four tests pass and the whole class passes with the filter above. Commit message: `test(resilience): PollyExecutionStrategy failure-path tests`.

### task: final-validation

Depends on: success-path-tests, failure-path-tests.
1. Run `dotnet build` and `dotnet format` for the backend test project; fix any warnings introduced.
2. Run the class tests three times to confirm no flakiness from the shared global `MeterListener` (captures must only include the per-test meter).
3. Confirm no files outside `PollyExecutionStrategyTests.cs` were changed (`git diff --stat`). No process doc update is needed (no production behaviour change).

Done when: build, format and tests are green. Commit only if format changed anything.
