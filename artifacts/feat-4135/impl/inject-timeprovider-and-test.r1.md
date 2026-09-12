# Implementation: inject-timeprovider-and-test

## What was implemented

`GetPurchaseStockAnalysisHandler` no longer reads the wall clock directly. A
`TimeProvider` is injected as the last constructor parameter and stored in a
`private readonly TimeProvider _timeProvider` field. The two `DateTime.UtcNow`
reads that produced the default date-range fallback in `Handle` are replaced by
a single `var now = _timeProvider.GetUtcNow().UtcDateTime;` read, so both ends
of the default window derive from one consistent instant instead of two separate
clock reads.

This matches the pattern already used by `CreatePurchaseOrderHandler` /
`CreatePurchaseOrderHandlerTests`. `TimeProvider.System` is already registered in
DI (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:135`),
so the new dependency resolves without any registration change.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
  — added `_timeProvider` field + `TimeProvider timeProvider` ctor parameter
  (appended last, existing parameters and assignments untouched); `Handle` now
  computes `now` once from the provider and uses it for both `fromDate` and
  `toDate` fallbacks. No `DateTime.UtcNow` / `DateTime.Now` reference remains in
  the file (verified by grep, exit 1).
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`
  — added `Mock<TimeProvider> _timeProviderMock` + `static readonly DateTimeOffset
  FixedNow = new(2024, 8, 2, 14, 30, 22, TimeSpan.Zero)`, wired into the single
  `_handler` construction site; added the new default-fallback test.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`
  — same `Mock<TimeProvider>` + `FixedNow` wiring for its own `_handler`
  construction site. **Deviation from the task plan** (see Notes).

## Tests

- `GetPurchaseStockAnalysisHandlerTests.Handle_NullDates_DefaultsToOneYearWindowFromTimeProvider`
  (new) — sends a request with `FromDate`/`ToDate` null and asserts
  `Summary.AnalysisPeriodStart == FixedNow.UtcDateTime.AddYears(-1)` and
  `Summary.AnalysisPeriodEnd == FixedNow.UtcDateTime`. Deterministic: no
  wall-clock dependency, so it cannot flake at a year boundary.
- All 17 pre-existing tests in `GetPurchaseStockAnalysisHandlerTests` and the 9
  `GetPurchaseStockAnalysisHandlerDiacriticsTests` theory cases were left
  behaviourally unchanged — only the shared construction sites were edited.

## How to verify

```bash
dotnet build Anela.Heblo.sln                                   # 0 Error(s)
dotnet format Anela.Heblo.sln --verify-no-changes              # exit 0, no violations
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetPurchaseStockAnalysisHandler"
```

Observed results:

- `dotnet build Anela.Heblo.sln` — `0 Error(s)`, 259 warnings, all pre-existing
  (none reference either touched file).
- `dotnet format Anela.Heblo.sln --verify-no-changes` — exit 0, no output.
- Filtered run — `Passed! - Failed: 0, Passed: 27, Skipped: 0, Total: 27`
  (18 facts in the main class incl. the new one + 9 diacritics theory cases).
- New test alone — `Passed! - Failed: 0, Passed: 1, Total: 1`.
- Full project — `Failed: 105, Passed: 6859, Skipped: 4, Total: 6968`. Every one
  of the 105 failures is `System.ArgumentException : Docker is either not running
  or misconfigured` thrown from `PostgresSharedContainerFixture..ctor()` — the
  Testcontainers-backed integration tests, which cannot run in this sandbox (no
  Docker daemon). Zero failures reference `StockAnalysis`; the single failure
  inside `Features.Purchase` is
  `PurchaseOrderRepositoryHistorySqlShapeTests.GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable`,
  same Docker cause. Pre-existing environment limitation, not a regression.

## Notes

- **Deviation from the task plan:** the plan asserted
  `GetPurchaseStockAnalysisHandlerTests` held the only construction site for the
  handler. It does not — `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs:25`
  constructs it too, and leaving it alone would have broken the build. It was
  updated with the identical `Mock<TimeProvider>` + `FixedNow` pattern rather
  than `TimeProvider.System`, since that class's tests also exercise the
  null-date fallback path (their request sets no `FromDate`/`ToDate`), so a fixed
  clock keeps them deterministic too.
- No DI registration change was needed; `TimeProvider.System` is already
  registered at API composition root.
- No DTO, contract, controller, or frontend change — the handler's observable
  behaviour is unchanged apart from the two fallbacks now sharing one instant.

## PR Summary

`GetPurchaseStockAnalysisHandler` computed its default analysis window from two
separate `DateTime.UtcNow` reads, which made the fallback path untestable and
left a (tiny) window where the two reads could straddle a tick. It now takes an
injected `TimeProvider` and reads the clock once, matching the pattern
`CreatePurchaseOrderHandler` already uses.

The handler's two test classes were updated to construct it with a
`Mock<TimeProvider>` pinned to a fixed instant, and a new test asserts the
default window is exactly `[now - 1 year, now]` against that fixed clock.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` — `TimeProvider` field + ctor parameter; single `now` read replaces both `DateTime.UtcNow` calls
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` — mocked `TimeProvider` with a fixed `DateTimeOffset`; new `Handle_NullDates_DefaultsToOneYearWindowFromTimeProvider` test
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` — same mocked `TimeProvider` wiring for its handler construction site

## Status
DONE
