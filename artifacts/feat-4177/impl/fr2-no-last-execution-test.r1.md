# Implementation: fr2-no-last-execution-test

## What was implemented
Added a unit test covering FR-2 of `GetTaskStatusHandler`: when a task is
registered in the task registry but `GetLastExecution` returns `null` (never
executed), `Handle` returns `Found = true`, a non-null `Status` with
`LastExecution = null`, and pass-through fields (`TaskId`, `Enabled`,
`RefreshInterval`) mapped from the registered `RefreshTaskConfiguration`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs` — added `Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted`, appended after the existing FR-1 test, using the existing `MakeSut`/`MakeTaskConfig` helpers already present in the file.

## Tests
- `Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted` — registers `task-a` with `Enabled = true` and a 30-minute `RefreshInterval`, stubs `GetLastExecution("task-a")` to return `null`, then asserts `Found == true`, `Status` is not null, `Status.LastExecution` is null, and `TaskId`/`Enabled`/`RefreshInterval` are passed through unchanged from the registered configuration.

## How to verify
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted"
```
Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes
As specified in the task context, the production code (`GetTaskStatusHandler.Handle`) already implements this branch correctly (no bug found) — this is a pure coverage-gap addition. Verified `RefreshTaskStatusDto` property names (`TaskId`, `Enabled`, `RefreshInterval`, `LastExecution`) match exactly what the test asserts.

## PR Summary
Added test coverage for the never-executed branch of `GetTaskStatusHandler.Handle` (FR-2): when a task is registered but has no execution history yet, the handler returns `Found = true` with a status object whose `LastExecution` is null and whose other fields are mapped straight from the task's registered configuration.

### Changes
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs` — added `Handle_ReturnsFoundWithNullLastExecution_WhenTaskRegisteredButNeverExecuted`

## Status
DONE
