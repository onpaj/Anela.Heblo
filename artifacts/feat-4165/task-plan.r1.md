# Stop Exception Swallowing in PackingMaterials Daily-Consumption Handlers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the catch-and-swallow exception handling in `ProcessDailyConsumptionHandler` and `GetDailyConsumptionBreakdownHandler` so that unexpected exceptions propagate — restoring Hangfire's retry contract for the daily job and letting the API's existing global exception handler produce a correct 5xx for the query endpoint.

**Architecture:** Delete the `catch (Exception ex)` blocks (and their now-unneeded `try`) from both handlers, leaving their existing legitimate non-exceptional branches (`!result.WasRun`; the empty-consumptions early return) untouched. No other production code changes — `DailyConsumptionJob`'s existing `catch { LogError; throw; }` and the API's already-registered `app.UseExceptionHandler()` + `AddProblemDetails()` pipeline pick up the propagated exception automatically. Two existing unit tests that assert the old swallow-and-return-false behavior must be rewritten to assert propagation instead, and a new test file covers `DailyConsumptionJob`'s rethrow.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq (`ProcessDailyConsumptionHandlerTests.cs`), hand-written fakes (`MockPackingMaterialRepository`, `MockLogger<T>` in `GetDailyConsumptionBreakdownHandlerTests.cs`), FluentAssertions (Moq-style tests only).

---

### task: fix-process-daily-consumption-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs:20-67`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs:108-151`

- [ ] **Step 1: Rewrite the existing exception test to expect propagation instead of a swallowed response**

Replace the entire `Handle_ReturnsGenericError_WhenServiceThrows` test and its now-unused `VerifyErrorLogged` helper (lines 108-151 of `ProcessDailyConsumptionHandlerTests.cs`) with:

```csharp
    [Fact]
    public async Task Handle_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        var thrown = new InvalidOperationException("secret database connection string");

        var (sut, service, _) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        Func<Task> act = () => sut.Handle(request, CancellationToken.None);

        // Assert — the handler no longer swallows the exception into a Success=false response.
        // DailyConsumptionJob's own catch/throw relies on this to make Hangfire's retry
        // contract work (see DailyConsumptionJobTests.cs, task: add-daily-consumption-job-failure-test).
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(thrown);
    }
```

This deletes the private `VerifyErrorLogged` static method too (its only caller was the test just replaced) — leave the file ending at the new test's closing `}` followed by the class's closing `}` (no trailing helper method).

- [ ] **Step 2: Run the test to confirm it fails against the current (unfixed) handler**

Run: `dotnet test --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests.Handle_PropagatesException_WhenServiceThrows"`
Expected: FAIL — the current handler still catches the exception and returns a `Success = false` response, so `act.Should().ThrowAsync<InvalidOperationException>()` fails because nothing was thrown.

- [ ] **Step 3: Remove the catch-and-swallow block from the handler**

Replace the full `Handle` method body in `ProcessDailyConsumptionHandler.cs` (lines 20-67) with:

```csharp
    public async Task<ProcessDailyConsumptionResponse> Handle(
        ProcessDailyConsumptionRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing daily consumption for {Date}", request.ProcessingDate);

        var result = await _consumptionService.ProcessDailyConsumptionAsync(request.ProcessingDate, cancellationToken);

        if (!result.WasRun)
        {
            return new ProcessDailyConsumptionResponse
            {
                Success = false,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = 0,
                Message = $"Daily consumption for {request.ProcessingDate} was already processed"
            };
        }

        _logger.LogInformation("Successfully processed daily consumption for {Date}", request.ProcessingDate);

        var message = result.MaterialsProcessed > 0
            ? $"Daily consumption successfully processed for {request.ProcessingDate}. {result.MaterialsProcessed} materials updated."
            : $"No invoices found for {request.ProcessingDate} — no materials were updated.";

        return new ProcessDailyConsumptionResponse
        {
            Success = true,
            ProcessedDate = request.ProcessingDate,
            MaterialsProcessed = result.MaterialsProcessed,
            Message = message
        };
    }
```

This removes the `try`/`catch (Exception ex) { ... }` wrapper entirely — there is no longer any exception-handling code in this method, only the two existing informational log calls and the two existing return branches, now unindented one level.

- [ ] **Step 4: Run the whole test file to confirm the new test passes and no existing test regressed**

Run: `dotnet test --filter "FullyQualifiedName~ProcessDailyConsumptionHandlerTests"`
Expected: PASS — all 4 tests (`Handle_ReturnsFailure_WhenAlreadyProcessed`, `Handle_ReturnsSuccess_WhenMaterialsUpdated`, `Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound`, `Handle_PropagatesException_WhenServiceThrows`) pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ProcessDailyConsumptionHandlerTests.cs
git commit -m "fix(packing-materials): propagate exceptions from ProcessDailyConsumptionHandler instead of swallowing them"
```

---

### task: add-daily-consumption-job-failure-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs`

This task depends on `fix-process-daily-consumption-handler` only in narrative order (it demonstrates the end-to-end fix); the test itself mocks `IMediator` directly and does not require the handler change to compile or run, but should be done second so the plan reads in the same order the underlying bug is fixed.

- [ ] **Step 1: Write the new test file (there is no existing test file for this job)**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials.Infrastructure.Jobs;

public class DailyConsumptionJobTests
{
    private const string JobName = "daily-consumption-calculation";

    private static (
        DailyConsumptionJob Sut,
        Mock<IMediator> Mediator,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true)
    {
        var mediator = new Mock<IMediator>();
        var logger = new Mock<ILogger<DailyConsumptionJob>>();
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(JobName, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);

        var sut = new DailyConsumptionJob(mediator.Object, logger.Object, statusChecker.Object);
        return (sut, mediator, statusChecker);
    }

    [Fact]
    public async Task ExecuteAsync_Rethrows_WhenMediatorSendThrows()
    {
        // Arrange
        var thrown = new InvalidOperationException("database unreachable");
        var (sut, mediator, _) = MakeSut();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert — Hangfire's retry contract depends on this: if the handler's exception
        // doesn't reach here and get rethrown, Hangfire marks the run Succeeded and never retries.
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(thrown);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenJobDisabled()
    {
        // Arrange
        var (sut, mediator, _) = MakeSut(jobEnabled: false);

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        mediator.Verify(
            m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenMediatorReturnsSuccess()
    {
        // Arrange
        var (sut, mediator, _) = MakeSut();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                MaterialsProcessed = 3,
                Message = "ok"
            });

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrow_WhenMediatorReturnsFailureWithoutThrowing()
    {
        // Arrange — the legitimate "already processed" outcome: Success=false but no exception.
        // The job must log a warning and return normally, not throw.
        var (sut, mediator, _) = MakeSut();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessDailyConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResponse
            {
                Success = false,
                ProcessedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                MaterialsProcessed = 0,
                Message = "already processed"
            });

        // Act
        Func<Task> act = () => sut.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run the new test file**

Run: `dotnet test --filter "FullyQualifiedName~DailyConsumptionJobTests"`
Expected: PASS — all 4 tests pass. (No handler code is exercised by these tests — `IMediator` is mocked directly — so this passes independent of whether Task 1 has been applied yet; it documents and locks in `DailyConsumptionJob`'s already-correct behavior.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJobTests.cs
git commit -m "test(packing-materials): cover DailyConsumptionJob's rethrow-on-handler-exception contract"
```

---

### task: fix-get-daily-consumption-breakdown-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs:22-65`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs:165-185`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` (add a throw-hook for `GetConsumptionsByDateAsync`, mirroring the existing `SetSaveChangesException`/`_saveChangesException` pattern already in this file)

- [ ] **Step 1: Add a throw-hook to the shared repository fake**

`MockPackingMaterialRepository` already has this exact pattern for `SaveChangesAsync` (`_saveChangesException` field + `SetSaveChangesException` method, lines 11, 28-31, 140-143). Add the equivalent for `GetConsumptionsByDateAsync`, which currently has no way to simulate a repository failure.

In `MockPackingMaterialRepository.cs`, add a new field next to `_saveChangesException` (line 11):

```csharp
    private Exception? _saveChangesException;
    private Exception? _getConsumptionsByDateException;
```

Add a new method next to `SetSaveChangesException` (after line 31):

```csharp
    public void SetGetConsumptionsByDateException(Exception ex)
    {
        _getConsumptionsByDateException = ex;
    }
```

Change `GetConsumptionsByDateAsync` (line 193-194) from:

```csharp
    public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<PackingMaterialConsumption>>(ConsumptionRowsByDate.TryGetValue(date, out var rows) ? rows : new List<PackingMaterialConsumption>());
```

to:

```csharp
    public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        if (_getConsumptionsByDateException != null)
            throw _getConsumptionsByDateException;
        return Task.FromResult<IEnumerable<PackingMaterialConsumption>>(ConsumptionRowsByDate.TryGetValue(date, out var rows) ? rows : new List<PackingMaterialConsumption>());
    }
```

- [ ] **Step 2: Rewrite the existing out-of-range-enum test to expect propagation, and add a repository-throws test**

Replace `GroupBy_OutOfRangeEnumValue_ReturnsFailureResponse` (lines 165-185 of `GetDailyConsumptionBreakdownHandlerTests.cs`) with:

```csharp
    [Fact]
    public async Task GroupBy_OutOfRangeEnumValue_PropagatesException()
    {
        // Arrange: an out-of-range enum value can only occur via an unchecked cast — ASP.NET Core's
        // model binder can never produce one for a real HTTP request, but the handler's switch must
        // still fail loudly (not silently succeed) if it ever receives one, e.g. from a future
        // internal caller. The discard arm of the switch throws ArgumentOutOfRangeException, which
        // now propagates out of Handle instead of being converted into a Success=false response —
        // ASP.NET Core's existing ArgumentExceptionHandler (registered globally) maps ArgumentException
        // and its subclasses to a 400 automatically, so this is still a client-facing 400, just via
        // the standard exception pipeline instead of this handler building the response by hand.
        var repo = BuildRepo(Array.Empty<PackingMaterial>(), new[] { MakeConsumption(1, 5m, invoiceId: "INV-1") });
        var handler = BuildHandler(repo);
        var outOfRangeGroupBy = (ConsumptionGroupBy)99;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.Handle(
                new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = outOfRangeGroupBy },
                CancellationToken.None));

        Assert.Contains("Unhandled GroupBy value", exception.Message);
    }

    [Fact]
    public async Task Handle_PropagatesException_WhenRepositoryThrows()
    {
        // Arrange
        var repo = new MockPackingMaterialRepository();
        var thrown = new InvalidOperationException("database unreachable");
        repo.SetGetConsumptionsByDateException(thrown);
        var handler = BuildHandler(repo);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Material },
                CancellationToken.None));

        Assert.Same(thrown, exception);
    }
```

- [ ] **Step 3: Run the test file to confirm both new/changed tests fail against the current (unfixed) handler**

Run: `dotnet test --filter "FullyQualifiedName~GetDailyConsumptionBreakdownHandlerTests"`
Expected: FAIL on `GroupBy_OutOfRangeEnumValue_PropagatesException` and `Handle_PropagatesException_WhenRepositoryThrows` — the current handler still catches both exceptions and returns a `Success = false` response, so `Assert.ThrowsAsync` fails because nothing was thrown. All other tests in the file still pass.

- [ ] **Step 4: Remove the catch-and-swallow block from the handler**

Replace the full `Handle` method body in `GetDailyConsumptionBreakdownHandler.cs` (lines 22-65) with:

```csharp
    public async Task<GetDailyConsumptionBreakdownResponse> Handle(
        GetDailyConsumptionBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading daily consumption breakdown for {Date} grouped by {GroupBy}", request.Date, request.GroupBy);

        var consumptions = (await _repository.GetConsumptionsByDateAsync(request.Date, cancellationToken)).ToList();

        if (consumptions.Count == 0)
            return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy.ToString() };

        var materials = (await _repository.GetAllWithAllocationsAsync(cancellationToken)).ToList();

        var groups = request.GroupBy switch
        {
            ConsumptionGroupBy.Material => BuildGroupByMaterial(consumptions, materials),
            ConsumptionGroupBy.Product => BuildGroupByProduct(consumptions, materials),
            ConsumptionGroupBy.Order => BuildGroupByOrder(consumptions, materials),
            _ => throw new ArgumentOutOfRangeException(nameof(request.GroupBy), request.GroupBy, "Unhandled GroupBy value.")
        };

        return new GetDailyConsumptionBreakdownResponse
        {
            Success = true,
            Date = request.Date,
            GroupBy = request.GroupBy.ToString(),
            Groups = groups
        };
    }
```

This removes the `try`/`catch (Exception ex) { ... }` wrapper entirely — the three private `BuildGroupBy*` helper methods below it are untouched.

- [ ] **Step 5: Run the whole test file to confirm everything passes**

Run: `dotnet test --filter "FullyQualifiedName~GetDailyConsumptionBreakdownHandlerTests"`
Expected: PASS — every test in the file passes, including the two rewritten/added ones.

- [ ] **Step 6: Full backend validation**

Run, from the repository root (where `Anela.Heblo.sln` lives):

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test --filter "FullyQualifiedName~PackingMaterials"
```

Expected: `dotnet build` succeeds with no errors; `dotnet format --verify-no-changes` reports no files need formatting (if it does, run `dotnet format` and re-stage before committing); `dotnet test` reports all PackingMaterials-scoped tests passing, including every test touched or added across all three tasks in this plan.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs
git commit -m "fix(packing-materials): propagate exceptions from GetDailyConsumptionBreakdownHandler instead of swallowing them"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (`ProcessDailyConsumptionHandler` propagates) → `fix-process-daily-consumption-handler`, Steps 1-4.
- FR-2 (`DailyConsumptionJob` retry contract, test-only) → `add-daily-consumption-job-failure-test`, all steps.
- FR-3 (`GetDailyConsumptionBreakdownHandler` propagates) → `fix-get-daily-consumption-breakdown-handler`, Steps 1-5 (Step 1's repository fake extension is a direct prerequisite for the Step 2 test spec calls for).
- FR-4 (API surfaces 5xx) → no code change required per arch-review.r1.md's confirmed finding that `app.UseExceptionHandler()` + `AddProblemDetails()` already covers this controller; verified by FR-3's own propagation test per arch-review.r1.md's Specification Amendment #3. No separate task needed.
- FR-5 (logging preserved) → satisfied by construction: Steps 3/4 (`fix-process-daily-consumption-handler`) and Step 4 (`fix-get-daily-consumption-breakdown-handler`) keep every pre-existing `_logger.LogInformation` call and only remove the `_logger.LogError` calls that lived inside the deleted catch blocks, per spec FR-5's explicit instruction not to add new handler-level logging.
- NFR-1 (no behavior change on success/legitimate-false paths) → every pre-existing passing test for those paths is left untouched in both handler test files; only the two exception-path tests are rewritten.
- NFR-2 (Hangfire retry policy itself unchanged) → no task touches Hangfire configuration; `add-daily-consumption-job-failure-test` only proves the *path* to the retry policy is restored, per spec.
- Open Questions from spec.r1.md are resolved by arch-review.r1.md, not by new tasks: global exception handling coverage is confirmed to already exist (no task needed); `GetDailyConsumptionBreakdownResponse.Error` is kept as-is, unmodified (no task needed — the spec's Out of Scope already covers this); no broader sweep of other handlers is performed (out of scope, unchanged).

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" phrases; every step shows complete, copy-pasteable code or an exact shell command with its expected output.

**Type consistency:** `ProcessDailyConsumptionRequest`, `ProcessDailyConsumptionResponse`, `GetDailyConsumptionBreakdownRequest`, `GetDailyConsumptionBreakdownResponse`, `ConsumptionGroupBy`, `IRecurringJobStatusChecker.IsJobEnabledAsync(string, CancellationToken, bool)`, and `MockPackingMaterialRepository`'s existing method/field naming conventions are used identically to how they appear in the current source and test files read during architecture review and planning — no invented signatures.
