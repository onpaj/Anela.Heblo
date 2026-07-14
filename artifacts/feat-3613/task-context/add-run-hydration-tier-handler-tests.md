### task: add-run-hydration-tier-handler-tests

**Goal**

Add a new unit test class `RunHydrationTierHandlerTests` covering all four response paths of `RunHydrationTierHandler.Handle(...)` (not-found/no-enabled-tasks, successful hydration, cancellation, unexpected exception), closing the coverage gap flagged in CI run #28968007617 (17.9% line coverage on this file). This is test-only work — no production code should change unless a test reveals an actual defect in `RunHydrationTierHandler.cs`, in which case fix only that defect and note it, without further refactoring.

**Files to create**

- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs` (new file, new folder — `Application/BackgroundRefresh/` does not exist yet)

**Files to read for context (do not modify unless a bug is found)**

- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierHandler.cs` — handler under test
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/RunHydrationTier/RunHydrationTierResponse.cs`
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/IBackgroundRefreshTaskRegistry.cs` — mocked dependency
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskConfiguration.cs` (or sibling file defining the class) — plain class with `required` init props: `TaskId` (string), `InitialDelay`/`RefreshInterval` (TimeSpan), `Enabled` (bool), `HydrationTier` (int)
- `backend/test/Anela.Heblo.Tests/Application/Packaging/GetOrderTrackingNumberHandlerTests.cs` — template for the `MakeSut()` tuple-factory convention
- `backend/test/Anela.Heblo.Tests/Application/Packaging/GetPackageLabelPdfHandlerTests.cs` — template for the `Mock<ILogger<T>>` + local `VerifyLogged(...)` helper pattern

**Handler behavior (verified from source — use this as ground truth)**

```csharp
public async Task<RunHydrationTierResponse> Handle(RunHydrationTierRequest request, CancellationToken cancellationToken)
{
    var tasksInTier = _taskRegistry.GetRegisteredTasks()
        .Where(t => t.HydrationTier == request.Tier && t.Enabled)
        .OrderBy(t => t.TaskId)
        .ToList();

    if (tasksInTier.Count == 0)
        return new RunHydrationTierResponse { NotFound = true, ErrorMessage = $"No enabled tasks found for tier {request.Tier}" };

    _logger.LogInformation("Manual hydration of tier {Tier} requested ({TaskCount} tasks)", request.Tier, tasksInTier.Count);

    try
    {
        foreach (var task in tasksInTier)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _taskRegistry.ForceRefreshAsync(task.TaskId, cancellationToken);
        }
        return new RunHydrationTierResponse { TaskCount = tasksInTier.Count };
    }
    catch (OperationCanceledException)
    {
        return new RunHydrationTierResponse { Cancelled = true };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Manual hydration of tier {Tier} failed", request.Tier);
        return new RunHydrationTierResponse { Success = false, ErrorMessage = "An unexpected error occurred during tier hydration" };
    }
}
```

`RunHydrationTierResponse : BaseResponse` — `Success` defaults to `true`, `NotFound`/`Cancelled` default to `false`, `ErrorMessage` defaults to `null`, `TaskCount` defaults to `0`.

**Steps**

1. Create the folder `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/` and the file `RunHydrationTierHandlerTests.cs` with namespace `Anela.Heblo.Tests.Application.BackgroundRefresh`.

2. Add usings for: `Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier`, `Anela.Heblo.Xcc.Services.BackgroundRefresh`, `FluentAssertions`, `Microsoft.Extensions.Logging`, `Moq`, `Xunit` (match the `<Using Include="Xunit" />` global-usings convention already in the test project — no explicit `using Xunit;` needed if it's global, but check the sibling file for whether it's included explicitly; mirror whatever `GetOrderTrackingNumberHandlerTests.cs` does).

3. Add a private static `MakeSut()` factory, mirroring `GetOrderTrackingNumberHandlerTests`'s tuple-factory shape but using a `Mock<ILogger<RunHydrationTierHandler>>` (per arch-review Decision 1 — do **not** use `NullLogger`):

   ```csharp
   private static (RunHydrationTierHandler Sut, Mock<IBackgroundRefreshTaskRegistry> Registry, Mock<ILogger<RunHydrationTierHandler>> Logger) MakeSut()
   {
       var registry = new Mock<IBackgroundRefreshTaskRegistry>();
       var logger = new Mock<ILogger<RunHydrationTierHandler>>();
       var sut = new RunHydrationTierHandler(registry.Object, logger.Object);
       return (sut, registry, logger);
   }
   ```

4. Add a local `VerifyLogged` helper copied from `GetPackageLabelPdfHandlerTests.cs`:

   ```csharp
   private static void VerifyLogged(Mock<ILogger<RunHydrationTierHandler>> logger, LogLevel level, Times times) =>
       logger.Verify(
           l => l.Log(
               level,
               It.IsAny<EventId>(),
               It.IsAny<It.IsAnyType>(),
               It.IsAny<Exception?>(),
               (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
           times);
   ```

5. Add `[Fact] Handle_ReturnsNotFound_WhenNoEnabledTasksInTier`:
   - Set up `registry.Setup(r => r.GetRegisteredTasks())` to return an empty list.
   - Call `sut.Handle(new RunHydrationTierRequest { Tier = 2 }, default)`.
   - Assert `response.NotFound == true`; `response.ErrorMessage` is non-empty and contains `"2"` (the requested tier); `response.TaskCount == 0`; `response.Cancelled == false`.
   - Assert `registry.Verify(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never)`.

6. Add a second sub-case in the same or a separate `[Fact]` (e.g. `Handle_ReturnsNotFound_WhenTasksInTierAreAllDisabled`) to exercise the `.Where(... && t.Enabled)` filter specifically: seed the registry with one or more `RefreshTaskConfiguration` for the requested tier with `Enabled = false`, and assert the same `NotFound`/`ForceRefreshAsync-Never` outcome as step 5.

7. Add `[Fact] Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully`:
   - Seed the registry with 2 enabled `RefreshTaskConfiguration` entries for the requested tier (distinct `TaskId`s, e.g. `"task-a"`, `"task-b"`; `InitialDelay`/`RefreshInterval` = `TimeSpan.Zero`) plus 1 enabled entry for a different tier (e.g. `HydrationTier = 99` when the request tier is e.g. `1`).
   - Set up `registry.Setup(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask)`.
   - Assert `response.TaskCount == 2`; `response.NotFound == false`; `response.Cancelled == false`; `response.Success == true`.
   - Assert `ForceRefreshAsync` was called once for each of the two in-tier task IDs (`registry.Verify(r => r.ForceRefreshAsync("task-a", ...), Times.Once)` and same for `"task-b"`), and never called for the other-tier task's ID.
   - Optionally assert `VerifyLogged(logger, LogLevel.Information, Times.Once())`.

8. Add `[Fact] Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown`:
   - Seed the registry with at least 1 enabled task for the requested tier.
   - Set up `ForceRefreshAsync` to `.ThrowsAsync(new OperationCanceledException())`.
   - Call `Handle` with a normal (non-cancelled) `CancellationToken` (e.g. `default`) and assert the call completes without throwing.
   - Assert `response.Cancelled == true`; `response.Success == true`.

9. Add `[Fact] Handle_ReturnsCancelled_WhenTokenAlreadyCancelled`:
   - Seed the registry with 2+ enabled tasks for the requested tier.
   - Create a `CancellationTokenSource`, call `.Cancel()` on it before invoking `Handle`, and pass its `.Token` (a real, already-cancelled token — do not mock this).
   - Assert `response.Cancelled == true`.
   - Assert `registry.Verify(r => r.ForceRefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never)` (the `ThrowIfCancellationRequested()` check fires before the first `ForceRefreshAsync` call in the loop).

10. Add `[Fact] Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException`:
    - Seed the registry with at least 1 enabled task for the requested tier.
    - Set up `ForceRefreshAsync` to `.ThrowsAsync(new InvalidOperationException("boom"))`.
    - Assert `response.Success == false`; `response.ErrorMessage == "An unexpected error occurred during tier hydration"` (exact match); `response.Cancelled == false`; `response.NotFound == false`.
    - Assert `VerifyLogged(logger, LogLevel.Error, Times.Once())`.
    - Assert the original exception message (`"boom"`) does not appear in `response.ErrorMessage` (confirms no leakage).

11. Keep all tests self-contained per NFR-3: call `MakeSut()` fresh in every `[Fact]`, no shared fields/state, no `Task.Delay`, no real timers.

12. If any test reveals a genuine discrepancy from the documented handler behavior above (e.g. a flag not set as expected), fix only that specific line in `RunHydrationTierHandler.cs` and note the fix in your summary — do not otherwise modify or refactor the handler.

**Acceptance criteria**

- File exists at `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/RunHydrationTierHandlerTests.cs` with namespace `Anela.Heblo.Tests.Application.BackgroundRefresh`.
- No `.csproj` changes were made (test project already references `Anela.Heblo.Application`, `Anela.Heblo.Xcc`, `Moq`, `FluentAssertions`, `Microsoft.Extensions.Logging.Abstractions`).
- At least these test methods exist and pass:
  - `Handle_ReturnsNotFound_WhenNoEnabledTasksInTier`
  - a disabled-tasks sub-case (e.g. `Handle_ReturnsNotFound_WhenTasksInTierAreAllDisabled`)
  - `Handle_ReturnsTaskCount_WhenAllTasksCompleteSuccessfully`
  - `Handle_ReturnsCancelled_WhenOperationCanceledExceptionThrown`
  - `Handle_ReturnsCancelled_WhenTokenAlreadyCancelled`
  - `Handle_ReturnsFailure_WhenForceRefreshThrowsUnexpectedException`
- The exception-path test verifies `LogError` was invoked via `Mock<ILogger<RunHydrationTierHandler>>` + `VerifyLogged` (not `NullLogger`), per arch-review Decision 1 — this is a hard requirement, not optional.
- Run `dotnet build backend/Anela.Heblo.sln` (or the relevant `.csproj`) from the repo root — must succeed with no new errors/warnings introduced by this file.
- Run `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~RunHydrationTierHandlerTests` — all tests in the new class must pass.
- Run the full `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` to confirm no regressions in the rest of the suite.
- No production code changes outside `RunHydrationTierHandler.cs`, and any change to that file is limited to a single verified defect fix (expected: none needed).
