# Implementation: implement-drift-result-shaper

## What was implemented

Added `DriftDqtResultShaper`, the second concrete `IDqtResultShaper` implementation. Unlike
`InvoiceDqtResultShaper` (which handles exactly one `DqtTestType` value), this shaper derives
`CanHandle` from its injected `IEnumerable<IDriftDqtComparer>` collection — mirroring the
existing `DriftDqtJobRunner.CanHandle` pattern — so it transparently covers all four drift test
types (`ProductPairing`, `StockWriteBackReconciliation`, `LotSumVsErpStock`,
`PriceComparison`) without a hardcoded enum list. `ShapeAsync` fetches the paginated drift
results via `IDqtRunRepository.GetDriftResultsAsync`, maps them to `DqtDriftResultDto` via
`IMapper`, and sets `response.DriftResults` / `response.TotalDriftResults`, leaving
`response.Results` untouched (that field belongs to the invoice shaper's output).

Per this task's scope, `GetDqtRunDetailHandler` and `DataQualityModule` DI registration are
**not** touched — that is the `rewire-handler-and-register-shapers` task.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtResultShaper.cs` —
  new class implementing `IDqtResultShaper`; `CanHandle` returns true for any `DqtTestType`
  matched by an injected `IDriftDqtComparer.TestType`; `ShapeAsync` maps drift results from the
  repository onto `response.DriftResults` / `response.TotalDriftResults`.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtResultShaperTests.cs` — new test
  file (written first, per TDD step in the task context), verbatim per the task plan.

## Tests

- `DriftDqtResultShaperTests`:
  - `CanHandle_DerivesFromInjectedComparers_NotAHardcodedList` (`[Theory]`, 5 cases) — true for
    all 4 drift `DqtTestType` values when a matching comparer is injected, false for
    `IssuedInvoiceComparison`.
  - `ShapeAsync_MapsDriftResultsAndTotalOntoResponse_AndLeavesResultsUntouched` (`[Fact]`) —
    verifies the mapped drift list and total are assigned (`Assert.Same` / `Assert.Equal`), and
    that `response.Results` stays empty.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DriftDqtResultShaperTests
# Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

## Notes

- Followed the task context's code verbatim (interface already existed from
  `implement-invoice-result-shaper`; implementation and test file both match the plan's
  snippets exactly) — mechanical, single-purpose task with a complete spec.
- `DqtDriftResultDto` already had an `AutoMapper` map
  (`CreateMap<DqtDriftResult, DqtDriftResultDto>()` in `DataQualityMappingProfile.cs`), and
  `IDqtRunRepository.GetDriftResultsAsync` already existed on the repository interface, so no
  additional infrastructure changes were needed.
- `GetDqtRunDetailHandler` and `DataQualityModule` DI wiring are deliberately left untouched —
  `DriftDqtResultShaper` is not yet registered in DI and not yet called by the handler. That is
  explicitly the `rewire-handler-and-register-shapers` task's job, per the task plan.

## PR Summary
Adds `DriftDqtResultShaper`, the second `IDqtResultShaper` implementation (after
`InvoiceDqtResultShaper`), covering all four drift-comparison test types by deriving
`CanHandle` from the injected `IDriftDqtComparer` collection rather than a hardcoded enum list.
The handler itself is not modified yet — that happens in `rewire-handler-and-register-shapers`,
once both shapers exist.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtResultShaper.cs` — new implementation
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtResultShaperTests.cs` — new tests

## Status
DONE
