# Implementation: add-getbackgroundrefreshtaskshandler-tests

## What was implemented
Added a new xUnit test class, `GetBackgroundRefreshTasksHandlerTests`, covering `GetBackgroundRefreshTasksHandler.Handle`. The handler was previously untested; this closes that coverage gap by exercising all of its mapping and conditional-logic branches: `NextScheduledRun` computation (disabled task, no execution, in-flight execution, completed execution), full `LastExecution` field mapping, pass-through configuration fields, and independent per-task mapping when multiple tasks are registered.

The task-context file specified the exact test file content (including all 7 test methods, fixture helpers, and assertions) based on a prior read of the handler, its request/response DTOs, `RefreshTaskConfiguration`, `RefreshTaskExecutionLog`, `RefreshTaskExecutionStatus`, and the sibling `RunHydrationTierHandlerTests.cs` style template. That exact content was written verbatim — no deviations were needed.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs` — new file, 7 `[Fact]` tests plus `MakeSut()`, `MakeTaskConfig(...)`, and `MakeExecutionLog(...)` helper methods, following the existing `RunHydrationTierHandlerTests.cs` style (plain xUnit facts, tuple-returning `MakeSut()`, Moq for `IBackgroundRefreshTaskRegistry` and `ILogger<T>`, FluentAssertions for assertions).

## Tests
- `Handle_NextScheduledRunIsNull_WhenTaskIsDisabled` — disabled task always yields `NextScheduledRun == null`, even with a completed last execution.
- `Handle_NextScheduledRunAndLastExecutionAreNull_WhenNoLastExecutionExists` — enabled task with no last execution yields both `NextScheduledRun` and `LastExecution` null.
- `Handle_NextScheduledRunIsNull_WhenLastExecutionHasNotCompleted` — enabled task with an in-flight (not completed) execution yields `NextScheduledRun == null`.
- `Handle_NextScheduledRunEqualsCompletedAtPlusRefreshInterval_WhenTaskEnabledAndLastExecutionCompleted` — enabled task with a completed execution yields `NextScheduledRun == CompletedAt + RefreshInterval` exactly.
- `Handle_MapsLastExecutionFields_WhenLastExecutionExists` — every `LastExecution` field (`TaskId`, `StartedAt`, `CompletedAt`, `Status`, `ErrorMessage`, `Duration`, `Metadata`) is mapped correctly from the source log.
- `Handle_MapsPassThroughFields_FromConfigurationToDto` — `TaskId`, `InitialDelay`, `RefreshInterval`, `Enabled`, `HydrationTier` map unchanged from configuration to DTO.
- `Handle_MapsEachTaskIndependently_WhenMultipleTasksRegistered` — three tasks with different enabled/execution states are mapped independently with no cross-contamination.

All 7 tests pass locally. No production code was touched (test-only addition).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.BackgroundRefresh.GetBackgroundRefreshTasksHandlerTests"
# => Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7

dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.BackgroundRefresh"
# => Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13 (includes existing RunHydrationTierHandlerTests)

dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes --include test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs
# => no output, no formatting changes needed
```

## Notes
- `dotnet format --verify-no-changes` required pointing at the `Anela.Heblo.Tests.csproj` explicitly (no `.sln` exists at `backend/` root — MSBuildWorkspaceFinder errors without a project/solution target). This is a local invocation detail, not a code change.
- No production code changes were required or made; this is a pure test-coverage addition as scoped by the task.
- Followed `MakeTaskConfig`/`MakeExecutionLog` fixture-builder conventions from `RunHydrationTierHandlerTests.cs` for consistency with the sibling test file.

## PR Summary
Adds unit test coverage for `GetBackgroundRefreshTasksHandler`, which previously had no tests. The seven new tests cover the handler's `NextScheduledRun` computation logic (disabled tasks, missing/in-flight/completed executions), full `LastExecution` DTO field mapping, pass-through configuration field mapping, and correct per-task isolation when multiple tasks are registered.

### Changes
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetBackgroundRefreshTasksHandlerTests.cs` — new test file with 7 `[Fact]` tests and supporting fixture helpers

## Status
DONE
