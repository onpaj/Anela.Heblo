# Implementation: getdqtrundetail-handler-dispatch

## What was implemented

Replaced `GetDqtRunDetailHandler.Handle`'s implicit "anything not invoice is drift" fallthrough with an explicit three-branch, fail-fast dispatch:

```csharp
if (run.TestType == DqtTestType.IssuedInvoiceComparison)
{
    return new GetDqtRunDetailResponse { Success = true, Run = ..., Results = ... };
}

if (run.TestType is DqtTestType.ProductPairing or DqtTestType.StockWriteBackReconciliation)
{
    var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(...);
    return new GetDqtRunDetailResponse { Success = true, Run = ..., DriftResults = ..., TotalDriftResults = driftTotal };
}

throw new NotSupportedException($"No result-shaping logic registered for DqtTestType {run.TestType}");
```

The existing outer `catch (Exception ex)` block now maps the error code based on exception type:

```csharp
ErrorCode = ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception
```

`ErrorCodes.DqtUnsupportedTestType = 2204` (added in task 1) is reused here, not re-declared. A new test was added to `GetDqtRunDetailHandlerTests.cs` covering the fail-fast path using an explicit out-of-range `(DqtTestType)999` cast.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` — explicit three-branch dispatch (invoice / known-drift-types / throw `NotSupportedException`), plus the `catch` block `ErrorCode` mapping.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` — added `Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError`, asserting `Success == false`, `ErrorCode == ErrorCodes.DqtUnsupportedTestType`, and `Run == null`.

## Tests

- `GetDqtRunDetailHandlerTests` — existing invoice-path and drift-path tests pass unmodified; new fail-fast test added.
- Full `Features.DataQuality` namespace test run: 71/71 passed.

## How to verify

```bash
cd /home/user/worktrees/feature-3455-Arch-Review-Dataquality-Rundqthandler-And-Getdqtru
dotnet build Anela.Heblo.sln
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~Features.DataQuality" --no-build
```
Expected: build succeeds (0 errors — pre-existing unrelated `MSB3073` access-matrix-generator warning only); test run reports `Passed! - Failed: 0, Passed: 71`.

## Notes

No deviations from the task spec. This was the last of the three planned tasks. `artifacts/` changes (state.json) intentionally left out of this commit — orchestrator-managed.

## Status
DONE
