# Implementation: rundqt-handler-dispatch

## What was implemented

Replaced `RunDqtHandler.Handle`'s binary `if (request.TestType == DqtTestType.IssuedInvoiceComparison) { GetRequiredService<IInvoiceDqtJobRunner>() } else { GetRequiredService<IDriftDqtJobRunner>() }` dispatch (inside the existing fire-and-forget `Task.Run`) with resolution via the new `IDqtJobRunner` abstraction:

```csharp
var runner = scope.ServiceProvider
    .GetServices<IDqtJobRunner>()
    .SingleOrDefault(r => r.CanHandle(request.TestType))
    ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
await runner.RunAsync(run.Id);
```

`RunDqtHandlerTests.cs` was updated to stub `IEnumerable<IDqtJobRunner>` resolution (`sp.GetService(typeof(IEnumerable<IDqtJobRunner>))`) instead of individually stubbing `IInvoiceDqtJobRunner`, matching how `GetServices<T>()` resolves under the hood. Three new tests were added covering: invoking the invoice runner for an invoice test type, invoking the drift runner for a drift test type, and the "no runner claims `CanHandle`" case (asserting neither runner's `RunAsync` is invoked; the resulting `InvalidOperationException` is thrown inside the fire-and-forget `Task.Run` and is not observable to the caller — a pre-existing, out-of-scope characteristic of the fire-and-forget design, not a regression).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — replaced the binary dispatch with `IDqtJobRunner` resolution as shown above. No other changes.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — replaced the single `IInvoiceDqtJobRunner` mock with two `IDqtJobRunner` mocks (`_invoiceJobRunnerMock`, `_driftJobRunnerMock`), rewired the `IServiceProvider` mock to resolve `IEnumerable<IDqtJobRunner>`, updated existing tests to reference `_invoiceJobRunnerMock`, and added 3 new test methods: `Handle_InvoiceTestType_InvokesMatchingRunnerOnly`, `Handle_DriftTestType_InvokesMatchingRunnerOnly`, `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked`.

## Tests

- `RunDqtHandlerTests` (10 tests total after this change) — all existing tests pass unmodified in behavior; 3 new tests added as described above.
- Full `Features.DataQuality` namespace test run: 70/70 passed.

## How to verify

```bash
cd /home/user/worktrees/feature-3455-Arch-Review-Dataquality-Rundqthandler-And-Getdqtru
dotnet build Anela.Heblo.sln
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~Features.DataQuality" --no-build
```
Expected: build succeeds (0 errors — one pre-existing unrelated `MSB3073` warning from the access-matrix generator tool, not caused by this change); test run reports `Passed! - Failed: 0, Passed: 70`.

## Notes

No deviations from the task spec. `using System.Linq` was already implicitly available / `SingleOrDefault`/`GetServices` resolved without needing a new `using` (project uses implicit usings). `artifacts/` changes (state.json) intentionally left out of this commit — orchestrator-managed.

## Status
DONE
