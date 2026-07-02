# Code Review: rundqt-handler-dispatch

## Summary

The implementation matches the task spec exactly, byte-for-byte, in both the production dispatch logic and the test rewrite. `RunDqtHandler.Handle` now resolves the runner via `GetServices<IDqtJobRunner>().SingleOrDefault(r => r.CanHandle(request.TestType))` with a throw on no match, and the test file was rewired for `IEnumerable<IDqtJobRunner>` resolution with the three required new dispatch tests added. Build succeeds with 0 errors and all 7 tests in `RunDqtHandlerTests` (70/70 in the `Features.DataQuality` namespace) pass.

## Review Result: PASS

### task: rundqt-handler-dispatch
**Status:** PASS

## Verification performed

- Read `git show d42c320` diff and the current working-tree files:
  - `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`
  - `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs`
- Confirmed the dispatch block in `RunDqtHandler.cs` (lines 46-54) matches the spec's target exactly:
  ```csharp
  using var scope = _scopeFactory.CreateScope();
  var runner = scope.ServiceProvider
      .GetServices<IDqtJobRunner>()
      .SingleOrDefault(r => r.CanHandle(request.TestType))
      ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
  await runner.RunAsync(run.Id);
  ```
  Uses `SingleOrDefault` (not `FirstOrDefault`), throws `InvalidOperationException` identifying the `TestType` via string interpolation. No other part of `Handle` (date-range check, `DqtRun.Start`, repository calls, logging, `RunDqtResponse` construction, outer `try/catch`) was touched — confirmed via diff (`git show d42c320` shows only the 9-line dispatch block replaced, 15 lines total changed in this file).
- Confirmed `RunDqtHandlerTests.cs` matches the spec's full replacement file content verbatim: two `Mock<IDqtJobRunner>` fields (`_invoiceJobRunnerMock`, `_driftJobRunnerMock`), constructor wiring `sp.GetService(typeof(IEnumerable<IDqtJobRunner>))` to return both mocks with default `CanHandle` behavior mirroring the real runners (invoice mock claims `IssuedInvoiceComparison` only, drift mock claims everything else), the 4 pre-existing tests updated to reference `_invoiceJobRunnerMock`, and 3 new tests added: `Handle_InvoiceTestType_InvokesMatchingRunnerOnly`, `Handle_DriftTestType_InvokesMatchingRunnerOnly`, `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked`.
- Ran `dotnet build Anela.Heblo.sln --nologo -v minimal` from repo root: **0 errors**, 253 pre-existing warnings unrelated to this change (nullability warnings across many unrelated test files; no MSB3073 or DataQuality-related warnings surfaced in this run).
- Ran `dotnet test --filter "FullyQualifiedName~Features.DataQuality" --no-build --nologo -v minimal`: **Passed! Failed: 0, Passed: 70, Total: 70**, confirming all 7 `RunDqtHandlerTests` (4 pre-existing + 3 new) pass alongside the rest of the DataQuality suite.
- Logic correctness: for `IssuedInvoiceComparison`, only the invoice mock's `CanHandle` returns `true` by default wiring, so only its `RunAsync` fires — matches prior behavior of the `if` branch. For `ProductPairing`/`StockWriteBackReconciliation`, only the drift mock's `CanHandle` returns `true`, matching the prior `else` branch (previously `IDriftDqtJobRunner` handled everything non-invoice). The no-match test correctly overrides both mocks' `CanHandle` for `StockWriteBackReconciliation` to `false` and asserts neither `RunAsync` is invoked, with an accurate comment explaining why `Handle`'s own return value is unaffected (exception is swallowed inside the fire-and-forget `Task.Run`, not a regression).

## Docs to Update

None required by this task.

## Overall Notes

No deviations found between spec and implementation. The implementation summary's claims (70/70 tests passing, 0 build errors) were independently verified and confirmed accurate.
