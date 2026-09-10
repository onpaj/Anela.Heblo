# RunDqtHandler Fire-and-Forget Error Swallow Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `RunDqtHandler` from silently losing a `DqtRun` in `Running` state forever when no `IDqtJobRunner` is registered for the requested `DqtTestType`.

**Architecture:** Add a synchronous pre-check in `Handle` that rejects an unsupported `DqtTestType` with the existing `ErrorCodes.DqtUnsupportedTestType` before any `DqtRun` is persisted (primary fix), and wrap the entire fire-and-forget `Task.Run` body in a try/catch that calls `run.Fail(...)` + `SaveChangesAsync` via a repository resolved from the task's own DI scope (defense-in-depth), mirroring the pattern already used by `DriftDqtJobRunner.RunAsync`.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, EF Core (via `IDqtRunRepository`).

---

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

### task: full-validation-gate

**Files:**
- None modified — this task only runs the project's standard validation commands per `CLAUDE.md`.

- [ ] **Step 1: Backend build**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Backend format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: No formatting violations. If violations are reported, run `dotnet format Anela.Heblo.sln` to fix them, then re-stage and amend the relevant commit from the task that introduced the violation.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test Anela.Heblo.sln`
Expected: All tests pass, including the full `Anela.Heblo.Tests` project (not just the `DataQuality` filter used in earlier tasks) — this catches any unexpected interaction with other DQT-adjacent tests (e.g. `GetDqtRunDetailHandlerTests`, which also uses `ErrorCodes.DqtUnsupportedTestType`).

- [ ] **Step 4: Confirm no frontend/API contract drift**

This change does not alter `RunDqtRequest`/`RunDqtResponse` shapes (same fields, only a previously-unused `ErrorCode` value now reachable). No OpenAPI client regeneration or frontend changes are required. Run `git status` to confirm no `frontend/src/api-client` files were touched by `dotnet build`'s auto-generation step; if any were touched unexpectedly, investigate before proceeding — that would indicate an unintended contract change.
