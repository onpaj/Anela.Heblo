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

