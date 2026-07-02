# Implementation: rundqt-handler-dispatch

## What was implemented

Replaced `RunDqtHandler.Handle`'s binary `if (request.TestType == DqtTestType.IssuedInvoiceComparison) { GetRequiredService<IInvoiceDqtJobRunner>() } else { GetRequiredService<IDriftDqtJobRunner>() }` dispatch (inside the existing fire-and-forget `Task.Run`) with resolution via the `IDqtJobRunner` abstraction:

```csharp
var runner = scope.ServiceProvider
    .GetServices<IDqtJobRunner>()
    .SingleOrDefault(r => r.CanHandle(request.TestType))
    ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
await runner.RunAsync(run.Id);
```

`RunDqtHandlerTests.cs` was updated to stub `IEnumerable<IDqtJobRunner>` resolution (`sp.GetService(typeof(IEnumerable<IDqtJobRunner>))`) instead of individually stubbing `IInvoiceDqtJobRunner`, matching how `GetServices<T>()` resolves under the hood. Three new tests were added covering: invoking the invoice runner for an invoice test type, invoking the drift runner for a drift test type, and the "no runner claims `CanHandle`" case (asserting neither runner's `RunAsync` is invoked; the resulting `InvalidOperationException` is thrown inside the fire-and-forget `Task.Run` and is not observable to the caller — a pre-existing, out-of-scope characteristic of the fire-and-forget design, not a regression).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — replaced the binary dispatch with `IDqtJobRunner` resolution as shown above. No other changes (date-range validation, run creation, response shape, outer try/catch all untouched).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — replaced the single `IInvoiceDqtJobRunner` mock with two `IDqtJobRunner` mocks (`_invoiceJobRunnerMock`, `_driftJobRunnerMock`), rewired the `IServiceProvider` mock to resolve `IEnumerable<IDqtJobRunner>`, updated existing tests to reference `_invoiceJobRunnerMock`, and added 3 new test methods: `Handle_InvoiceTestType_InvokesMatchingRunnerOnly`, `Handle_DriftTestType_InvokesMatchingRunnerOnly`, `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked`.

## Tests

- `RunDqtHandlerTests` (7 tests total after this change) — all 4 pre-existing tests pass unmodified in behavior (only the mock field name changed); 3 new tests added as described above.
- Full `Features.DataQuality` namespace test run: 70/70 passed.

## How to verify

```bash
cd /home/user/worktrees/feature-3455-Arch-Review-Dataquality-Rundqthandler-And-Getdqtru
dotnet build Anela.Heblo.sln
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~Features.DataQuality" --no-build
```
Expected: build succeeds (0 errors, only pre-existing warnings unrelated to this change); test run reports `Passed! - Failed: 0, Passed: 70`.

`dotnet format Anela.Heblo.sln --verify-no-changes --include <touched files>` was also run and reported no formatting changes needed.

## Notes

No deviations from the task spec — the actual current content of both files matched what was quoted in the task context verbatim. `GetServices<IDqtJobRunner>()` and `SingleOrDefault` resolved without needing an additional `using System.Linq;` (already covered by existing usings / implicit usings). `artifacts/feat-3455/state.json` was intentionally left out of the commit (orchestrator-managed, out of scope per task constraints).

## Status
DONE
