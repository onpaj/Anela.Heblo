# Implementation: synchronous-runner-validation

## What was implemented
Added a synchronous pre-check to `RunDqtHandler.Handle` that verifies an `IDqtJobRunner` is registered for the requested `DqtTestType` *before* persisting a `DqtRun` and before starting the fire-and-forget `Task.Run`. If no runner can handle the requested test type, the handler now returns `Success = false` / `ErrorCode = ErrorCodes.DqtUnsupportedTestType` immediately, with no `DqtRun` ever created. This closes the gap where an unsupported test type previously returned `200 OK` with a run ID that was silently left stuck in `Running` state forever (the `InvalidOperationException` thrown by the `?? throw` inside the fire-and-forget lambda happens before `RunAsync`'s own try/catch, so it was swallowed).

The fire-and-forget `Task.Run` body itself (including its own `?? throw new InvalidOperationException(...)` safety net) is intentionally left unchanged — wrapping it in its own try/catch is a separate follow-up task (`fire-and-forget-safety-net`), not part of this one.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — added a `using (var validationScope = _scopeFactory.CreateScope())` block that checks `GetServices<IDqtJobRunner>().Any(r => r.CanHandle(request.TestType))` and returns the `DqtUnsupportedTestType` error synchronously when no runner matches, before the `DqtRun` is created/persisted.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — replaced `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked` (which only documented the old buggy behavior — asserting `Success == true` despite no runner being available) with `Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting`, which asserts the new synchronous-rejection behavior: `Success == false`, `ErrorCode == ErrorCodes.DqtUnsupportedTestType`, `DqtRunId == null`, and that `_repository.AddAsync` and both runners' `RunAsync` are never called.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — covers the new synchronous rejection path plus regression coverage for the existing valid-request, invalid-date-range, repository-throws, and per-test-type runner-dispatch paths.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests"
dotnet build Anela.Heblo.sln
```
Both commands were run and pass clean (7/7 tests passed; build succeeded with 0 errors).

## Notes
No deviations from the task-context spec. `ErrorCodes.DqtUnsupportedTestType` already existed (value 2204, used elsewhere in `GetDqtRunDetailHandler`), so no new error code was added.

## PR Summary
Fixes the first half of issue #3973: `RunDqtHandler` now rejects `POST /api/data-quality/runs` requests for a `DqtTestType` with no registered `IDqtJobRunner` synchronously, before any `DqtRun` is persisted or any fire-and-forget work starts. Previously such a request returned `200 OK` with a run ID that got stuck in `Running` state forever, because the runner-lookup failure was thrown from inside the fire-and-forget `Task.Run` and silently swallowed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — synchronous runner-availability pre-check before persisting the run
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — updated test to assert the corrected behavior

## Status
DONE
