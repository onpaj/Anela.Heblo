# Implementation: fire-and-forget-safety-net

## What was implemented
Wrapped the body of the fire-and-forget `Task.Run` in `RunDqtHandler.Handle` in a try/catch
safety net. Previously, if the runner lookup inside the background task threw (e.g. a runner
deregistered/misbehaving between the synchronous pre-check and the background task actually
running, or any exception from `RunAsync` itself), the exception was silently swallowed by the
unobserved `Task.Run` and the `DqtRun` was left stuck in `Running` status forever with no
diagnostic trail. Now the catch block logs the error, re-fetches the `DqtRun` via a
freshly-scoped repository (the outer `using var scope` from the fire-and-forget block, which is
still valid), calls `DqtRun.Fail(ex.Message, ...)`, and persists the change with
`SaveChangesAsync`.

The `Handle()` method's synchronous return value is unaffected — it still reports `Success = true`
once the run is legitimately persisted, since the failure this guards against happens
asynchronously after the response has already been returned.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — wrapped the fire-and-forget `Task.Run` body in try/catch; on exception, logs the error and marks the `DqtRun` as `Failed` via a scoped repository instead of leaving it `Running` indefinitely.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — added `Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun`, which forces the pre-check scope and the background-task scope to diverge (second `CreateScope()` call returns an empty runner list) so the background lookup throws, then asserts the run ends up `Failed` with the exception message recorded.

## Tests
- `Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun` (new): asserts `Handle()` still returns `Success = true` (the run was legitimately accepted synchronously), but after the fire-and-forget task runs, `DqtRun.Status == Failed`, `ErrorMessage` contains the test type name, and `SaveChangesAsync` was called at least once for the failure write.
- Full `RunDqtHandlerTests` suite (8 tests) re-run and passing, confirming no regressions in the other fire-and-forget/pre-check scenarios (`Handle_InvoiceTestType_InvokesMatchingRunnerOnly`, `Handle_DriftTestType_InvokesMatchingRunnerOnly`, `Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting`, etc.).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests.Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun"
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~RunDqtHandlerTests"
```
Both commands pass (8/8 for the full suite). `dotnet build ../Anela.Heblo.sln` also succeeds with
0 errors (94 pre-existing warnings, unrelated to this change). `dotnet format` on the two changed
files produced no additional diffs.

## Notes
- **Deviation from the task snippet**: the test snippet in the task context set up
  `_repositoryMock.Setup(r => r.SaveChangesAsync(...)).Returns(Task.CompletedTask)`, but
  `IRepository<TEntity, TKey>.SaveChangesAsync` returns `Task<int>`, not `Task` — this would not
  compile. Changed to `.ReturnsAsync(1)` to match the actual interface signature. No other
  deviations; the handler edit matches the task's snippet verbatim (field names `_logger`,
  `_timeProvider`, `_scopeFactory`, and `DqtRun.Fail(string, DateTime)` all matched exactly what
  was already in the codebase).
- **Environment note (not a code issue)**: in this sandbox, `dotnet test`/`dotnet build` runs
  intermittently stalled indefinitely partway through the build (stuck after the
  `GenerateAccessMatrix` MSBuild target, with MSBuild worker nodes sitting idle at ~1-2% CPU and
  no further output). This reproduced twice with default settings. Passing
  `-p:UseSharedCompilation=false /nodeReuse:false` plus the `MSBUILDDISABLENODEREUSE=1`
  environment variable reliably avoided the stall and builds/tests then completed quickly (build
  cache warm, full solution build in ~26s afterward). This looks like an artifact of this sandbox's
  persistent MSBuild/Roslyn build-server processes rather than anything in this change; flagging in
  case other tasks in this pipeline hit the same stall.

## PR Summary
Adds a safety net around the fire-and-forget background task in `RunDqtHandler`: if the runner
lookup or `RunAsync` call inside the `Task.Run` throws, the `DqtRun` is now marked `Failed` with
the exception message recorded, instead of the exception being silently swallowed and the run
staying `Running` forever with no diagnostic trail. This only affects the rare case where the
synchronous pre-check (which already validates a matching `IDqtJobRunner` exists) and the
background task's own lookup diverge, or where `RunAsync` itself throws.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — wrapped the fire-and-forget `Task.Run` body in try/catch, failing the `DqtRun` and logging on any exception.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — new regression test `Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun` proving the run is recorded as `Failed`.

## Status
DONE_WITH_CONCERNS
