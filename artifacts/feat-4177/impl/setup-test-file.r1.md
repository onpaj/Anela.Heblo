# Implementation: setup-test-file

## What was implemented

Created the test file skeleton for `GetTaskStatusHandlerTests` with the
`MakeSut()`, `MakeTaskConfig()`, and `MakeExecutionLog()` helper methods, as
specified in the task context. No test methods were added yet — that is the
job of the following tasks (`fr1-not-found-test`, `fr2-no-last-execution-test`,
`fr3-happy-path-test`).

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs` — new test class with `MakeSut`, `MakeTaskConfig`, and `MakeExecutionLog` static helper builders, matching the pattern already used by the sibling `GetBackgroundRefreshTasksHandlerTests.cs` in the same directory.

## Tests

No test methods yet — this task only sets up the skeleton and helpers. Confirmed it compiles as part of the existing test project.

## How to verify

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Build succeeded with 0 errors (244 pre-existing warnings, unrelated to this file).

## Notes

No new project references were needed — `Moq` and `FluentAssertions` are
already referenced by `Anela.Heblo.Tests.csproj`. Implementation follows the
task context verbatim.

## PR Summary
Added the skeleton and shared builder helpers (`MakeSut`, `MakeTaskConfig`, `MakeExecutionLog`) for `GetTaskStatusHandlerTests`, the first of four tasks closing the coverage gap on `GetTaskStatusHandler`'s not-found and null-`LastExecution` paths (issue #4177). No test methods yet; verified the skeleton compiles cleanly with the existing test project.

### Changes
- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs` — new file with test class skeleton and builder helpers

## Status
DONE
