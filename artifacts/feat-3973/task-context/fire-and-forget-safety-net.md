### task: fire-and-forget-safety-net

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` (the `Task.Run` block added/kept by `synchronous-runner-validation`)
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` (add one new test)

- [ ] **Step 1: Write a failing test proving the fire-and-forget task now records failure instead of swallowing it**

Add this test to `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` (after `Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting`):

```csharp
    [Fact]
    public async Task Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun()
    {
        // Arrange: both runners pass CanHandle at the synchronous pre-check (so the run IS
        // persisted), but the fire-and-forget task's own lookup throws — simulating a runner
        // deregistered/misbehaving between the pre-check and the background task running.
        // We force this by having the scope factory return a *second*, different scope on the
        // second CreateScope() call (the pre-check consumes the first) whose service provider
        // has an empty runner list.
        var run = default(DqtRun);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun r, CancellationToken _) => { run = r; return r; });
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => run);
        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var emptyScopeMock = new Mock<IServiceScope>();
        var emptyProviderMock = new Mock<IServiceProvider>();
        emptyProviderMock.Setup(sp => sp.GetService(typeof(IEnumerable<IDqtJobRunner>)))
            .Returns(new List<IDqtJobRunner>());
        emptyProviderMock.Setup(sp => sp.GetService(typeof(IDqtRunRepository)))
            .Returns(_repositoryMock.Object);
        emptyScopeMock.Setup(s => s.ServiceProvider).Returns(emptyProviderMock.Object);

        var callCount = 0;
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                // First call: the synchronous pre-check scope — return the normal wired scope
                // so the pre-check sees a matching runner and the run gets persisted.
                var scopeMock = new Mock<IServiceScope>();
                var providerMock = new Mock<IServiceProvider>();
                providerMock.Setup(sp => sp.GetService(typeof(IEnumerable<IDqtJobRunner>)))
                    .Returns(new List<IDqtJobRunner> { _invoiceJobRunnerMock.Object });
                scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
                return scopeMock.Object;
            }
            // Second call: the fire-and-forget task's own scope — empty runner list, so its
            // internal lookup throws InvalidOperationException before RunAsync is reached.
            return emptyScopeMock.Object;
        });

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);
        await Task.Delay(100); // allow the fire-and-forget Task.Run to run its catch block

        // Assert: Handle() itself still reports success (the run was legitimately accepted —
        // the failure happens asynchronously), but the run is now recorded as Failed instead
        // of being stuck in Running forever with no diagnostic trail.
        Assert.True(response.Success);
        Assert.NotNull(run);
        Assert.Equal(DqtRunStatus.Failed, run!.Status);
        Assert.Contains("IssuedInvoiceComparison", run.ErrorMessage);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
```

- [ ] **Step 2: Run the test to verify it fails against current code**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests.Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun"`
Expected: FAIL — `run!.Status` is still `Running` (`DqtRunStatus.Running`, not `Failed`) because the exception inside `Task.Run` is currently swallowed with no catch block.

- [ ] **Step 3: Wrap the fire-and-forget `Task.Run` body in try/catch with a scoped repository**

In `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`, replace the `_ = Task.Run(...)` block added in the previous task with:

```csharp
            // Fire-and-forget in a dedicated scope — the HTTP request scope is disposed
            // before RunAsync completes, so capturing _jobRunner directly would cause
            // ObjectDisposedException on the DbContext. The try/catch below is a safety net:
            // the synchronous pre-check above should already guarantee a runner exists, but if
            // that check and this lookup ever diverge, this ensures the run is marked Failed
            // instead of being silently stuck in Running forever.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var runner = scope.ServiceProvider
                        .GetServices<IDqtJobRunner>()
                        .SingleOrDefault(r => r.CanHandle(request.TestType))
                        ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
                    await runner.RunAsync(run.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DQT run {DqtRunId} ({TestType}) failed before RunAsync was reached", run.Id, request.TestType);
                    var scopedRepository = scope.ServiceProvider.GetRequiredService<IDqtRunRepository>();
                    var scopedRun = await scopedRepository.GetByIdAsync(run.Id, CancellationToken.None);
                    scopedRun?.Fail(ex.Message, _timeProvider.GetUtcNow().DateTime);
                    await scopedRepository.SaveChangesAsync(CancellationToken.None);
                }
            }, CancellationToken.None);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests.Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun"`
Expected: PASS

- [ ] **Step 5: Run the full `RunDqtHandlerTests` suite to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests"`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs
git commit -m "fix(data-quality): fail DqtRun instead of swallowing exceptions in the fire-and-forget job task"
```

---

