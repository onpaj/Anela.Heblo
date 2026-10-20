# Implementation: rewire-handler-and-register-shapers

## What was implemented

Rewired `GetDqtRunDetailHandler` to dispatch result-shaping through the
`IEnumerable<IDqtResultShaper>` abstraction introduced in the two prior tasks
(`implement-invoice-result-shaper`, `implement-drift-result-shaper`), replacing
the handler's hardcoded `if`/`is ... or ...` chain over `DqtTestType`. Also
registered the two concrete shapers (`InvoiceDqtResultShaper`,
`DriftDqtResultShaper`) as `IDqtResultShaper` in `DataQualityModule`, and
replaced the handler's unit tests to mock `IDqtResultShaper` instead of the
repository/mapper calls the handler no longer makes directly.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` — constructor now takes `IEnumerable<IDqtResultShaper> shapers`; `Handle` resolves the matching shaper via `_shapers.SingleOrDefault(s => s.CanHandle(run.TestType))`, returns `ErrorCodes.DqtUnsupportedTestType` when none matches, and otherwise builds the response's `Run` via `_mapper` and delegates result population to `shaper.ShapeAsync(...)`. The `NotSupportedException` catch-mapping to `ErrorCodes.DqtUnsupportedTestType` is preserved for defense in depth even though the new dispatch never throws it directly.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — added `services.AddScoped<IDqtResultShaper, InvoiceDqtResultShaper>();` and `services.AddScoped<IDqtResultShaper, DriftDqtResultShaper>();` immediately after the existing `IDqtJobRunner` registrations.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` — replaced in full: the SUT is constructed with a single mocked `IDqtResultShaper` (`Mock<IDqtResultShaper> _shaperMock`) instead of the previous repository/mapper-only setup. Test cases:
  - `Handle_RunNotFound_ReturnsNotFoundError` — unchanged behavior/assertions.
  - `Handle_RunExists_ReturnsMappedDetail` — now sets up `_shaperMock.CanHandle(IssuedInvoiceComparison) == true` and has `ShapeAsync` populate `response.Results` via callback, asserting the response is the same reference the shaper wrote.
  - `Handle_DriftTestType_ReturnsMappedDriftResults` (Theory over the 4 drift test types) — same pattern, `ShapeAsync` callback sets `DriftResults`/`TotalDriftResults`.
  - `Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError` — uses `(DqtTestType)999`; the default `Mock<IDqtResultShaper>.CanHandle` returns `false`, so no shaper matches, the handler returns `DqtUnsupportedTestType`, and the test verifies `ShapeAsync` was never called.

## Tests

`backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` — 4 tests covering not-found, invoice-comparison dispatch, all 4 drift test types (Theory), and the unsupported-type fail-fast path (including a `Times.Never` verification that `ShapeAsync` isn't invoked when no shaper matches).

Ran the full `DataQuality` test slice (`--filter FullyQualifiedName~DataQuality`), which also covers `InvoiceDqtResultShaperTests`, `DriftDqtResultShaperTests`, `InvoiceDqtJobRunnerTests`, and `DriftDqtJobRunnerTests` from the prior two tasks: **155 passed, 0 failed**.

## How to verify

```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DataQuality
```
Expected: `Passed! - Failed: 0, Passed: 155, ...`

Full solution build:
```
dotnet build Anela.Heblo.sln
```
Expected: `0 Error(s)` (93 pre-existing warnings, none introduced by this change).

Format check (scoped to the files this task touched, since two unrelated
pre-existing files elsewhere in the test suite — `MarketingPerformance/GetMarketingPerformanceComparisonHandlerTests.cs`
and `GetMarketingPerformanceMonthsHandlerTests.cs`, from an earlier merged PR #4235 — already fail
`dotnet format --verify-no-changes` on this branch and are out of scope for a surgical change):
```
dotnet format Anela.Heblo.sln --verify-no-changes --include \
  backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs \
  backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs
```
Expected: no output (clean).

## Notes

- Left the pre-existing `dotnet format --verify-no-changes` whitespace violations in
  `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/GetMarketingPerformanceComparisonHandlerTests.cs`
  and `GetMarketingPerformanceMonthsHandlerTests.cs` untouched — they predate this
  branch (introduced in merged PR #4235) and are unrelated to this task's scope;
  fixing them would violate the "surgical changes" rule in CLAUDE.md. Flagging here
  per that same rule ("if you notice unrelated dead code / issues, mention it —
  don't fix it").
- Followed the task-context file's exact code verbatim for the handler, module
  registration, and test file (Steps 1, 3, 4). No deviations.
- This was the last of the three planned developer tasks for this feature; all
  three tasks (`implement-invoice-result-shaper`, `implement-drift-result-shaper`,
  `rewire-handler-and-register-shapers`) are now implemented on this branch.

## PR Summary
Completed the `GetDqtRunDetailHandler` refactor from a hardcoded `DqtTestType` `if`/`or`-chain to a strategy-pattern dispatch over `IEnumerable<IDqtResultShaper>`. The handler now resolves the single shaper whose `CanHandle(run.TestType)` returns true, builds the response's `Run` mapping itself, and delegates all result-population (Results / DriftResults / TotalDriftResults) to `shaper.ShapeAsync(...)`. Both concrete shapers (`InvoiceDqtResultShaper`, `DriftDqtResultShaper`) are registered as `IDqtResultShaper` in DI. A 5th test type now needs one new `IDqtResultShaper` implementation plus one DI line — zero handler changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` — constructor takes `IEnumerable<IDqtResultShaper>`; `Handle` dispatches via `CanHandle`/`ShapeAsync` instead of a `DqtTestType` branch chain
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — registered `InvoiceDqtResultShaper` and `DriftDqtResultShaper` as `IDqtResultShaper`
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` — rewritten to mock `IDqtResultShaper` instead of the repository/mapper calls the handler no longer makes directly; all 4 test cases preserved with equivalent assertions

## Status
DONE
