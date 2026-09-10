### task: synchronous-runner-validation

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs:40-77`
- Modify (existing test, update assertions): `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs:193-222`

- [ ] **Step 1: Update the existing test that documents today's bug to assert the new, correct behavior**

Replace the entire `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked` test (lines 193-222 of `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs`) with:

```csharp
    [Fact]
    public async Task Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting()
    {
        // Arrange: simulate "no IDqtJobRunner registered for this TestType" by making both
        // mocks explicitly reject StockWriteBackReconciliation (overrides the constructor's
        // default wiring — Moq uses the most recently configured matching setup).
        _invoiceJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.StockWriteBackReconciliation)).Returns(false);
        _driftJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.StockWriteBackReconciliation)).Returns(false);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.StockWriteBackReconciliation,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert: rejected synchronously before any DqtRun is ever created.
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtUnsupportedTestType, response.ErrorCode);
        Assert.Null(response.DqtRunId);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it fails against current code**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests.Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting"`
Expected: FAIL — `Assert.False(response.Success)` fails because current code returns `Success = true` (the bug: the run is persisted and the exception is swallowed inside the fire-and-forget task).

- [ ] **Step 3: Implement the synchronous pre-check in `RunDqtHandler.Handle`**

Replace the body of `Handle` in `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` (lines 29-77) with:

```csharp
    public async Task<RunDqtResponse> Handle(RunDqtRequest request, CancellationToken cancellationToken)
    {
        if (request.DateFrom > request.DateTo)
        {
            return new RunDqtResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.DqtInvalidDateRange
            };
        }

        try
        {
            using (var validationScope = _scopeFactory.CreateScope())
            {
                var hasRunner = validationScope.ServiceProvider
                    .GetServices<IDqtJobRunner>()
                    .Any(r => r.CanHandle(request.TestType));

                if (!hasRunner)
                {
                    return new RunDqtResponse
                    {
                        Success = false,
                        ErrorCode = ErrorCodes.DqtUnsupportedTestType
                    };
                }
            }

            var run = DqtRun.Start(request.TestType, request.DateFrom, request.DateTo, DqtTriggerType.Manual, _timeProvider.GetUtcNow().DateTime);
            await _repository.AddAsync(run, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Fire-and-forget in a dedicated scope — the HTTP request scope is disposed
            // before RunAsync completes, so capturing _jobRunner directly would cause
            // ObjectDisposedException on the DbContext.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider
                    .GetServices<IDqtJobRunner>()
                    .SingleOrDefault(r => r.CanHandle(request.TestType))
                    ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
                await runner.RunAsync(run.Id);
            }, CancellationToken.None);

            _logger.LogInformation("DQT run {DqtRunId} started for {TestType} from {DateFrom} to {DateTo}",
                run.Id, run.TestType, run.DateFrom, run.DateTo);

            return new RunDqtResponse
            {
                DqtRunId = run.Id,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting DQT run");
            return new RunDqtResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception
            };
        }
    }
```

Note: this step intentionally leaves the fire-and-forget `Task.Run` body exactly as it was (no try/catch yet) — that is added in the next task. This step only adds the pre-check and the `using (var validationScope = ...)` block above it.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests.Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting"`
Expected: PASS

- [ ] **Step 5: Run the full `RunDqtHandlerTests` suite to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests"`
Expected: All tests PASS (including `Handle_ValidRequest_SavesRunAndReturnsId`, `Handle_DateFromAfterDateTo_ReturnsInvalidDateRangeError`, `Handle_SameDateFromAndTo_Succeeds`, `Handle_RepositoryThrows_ReturnsExceptionError`, `Handle_InvoiceTestType_InvokesMatchingRunnerOnly`, `Handle_DriftTestType_InvokesMatchingRunnerOnly`, and the new/renamed test from Step 1).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs
git commit -m "fix(data-quality): reject RunDqt requests for unsupported test types synchronously"
```

---

