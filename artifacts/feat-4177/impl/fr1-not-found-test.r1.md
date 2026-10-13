# Implementation: fr1-not-found-test

## What was done

Added `Handle_ReturnsNotFound_WhenTaskIdIsNotRegistered` to
`backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`,
exactly as specified in `task-context/fr1-not-found-test.md`.

The test registers only `other-task` in the mocked task registry, requests
status for `missing-task`, and asserts:
- `response.Found` is `false`
- `response.Status` is `null`
- `registry.GetLastExecution(...)` is never called

This is a coverage-only change — `GetTaskStatusHandler.Handle` already
implements the not-found branch correctly, so no production code was
touched.

## Verification

- `dotnet build` (full solution, via the test project): succeeded, 0 errors.
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTaskStatusHandlerTests"`: passed.

## Files changed

- `backend/test/Anela.Heblo.Tests/Application/BackgroundRefresh/GetTaskStatusHandlerTests.cs`
