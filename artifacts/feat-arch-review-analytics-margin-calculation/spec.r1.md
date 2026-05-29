```markdown
# Specification: Extract Single-Product Margin Calculation Helper

## Summary
Consolidate the duplicated per-product margin formula (`revenue = units × SellingPrice`, `cost = units × (SellingPrice − MarginAmount)`, `margin = revenue − cost`) into a single reusable method on `IMarginCalculator`. Three call sites currently re-implement this formula inline, and at least one has already drifted semantically from the others. Update all call sites to use the new method so the business definition of margin lives in exactly one place.

## Background
The Analytics module has an `IMarginCalculator` / `MarginCalculator` service that already centralizes margin aggregation for the `GetProductMarginSummary` use case. However, three other locations in the same module re-implement the single-product margin formula inline:

1. `GetMarginReportHandler.ProcessProductsForReport()` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs:113-117`)
2. `GetProductMarginAnalysisHandler.CalculateProductMargins()` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs:134-138`)
3. `ReportBuilderService.BuildMonthlyBreakdown()` (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs:43-46`)

The three copies have already drifted semantically: `GetMarginReportHandler` sums **all** of `product.SalesHistory` (unfiltered by the request period), whereas `GetProductMarginAnalysisHandler` filters by date range first. Any future change to the margin definition (e.g. a different cost component) would require finding and updating each copy with the risk of further drift.

The algebraic identity `units × SellingPrice − units × (SellingPrice − MarginAmount) = units × MarginAmount` is preserved by the new helper. The helper is a pure refactor — it must not change observed outputs for the period-filtered call sites — except for `GetMarginReportHandler`, where the pre-existing "all-history" behavior is a latent bug that this work corrects (see FR-2 below).

## Functional Requirements

### FR-1: Add single-product margin method to `IMarginCalculator`
Add a method on `IMarginCalculator` that computes `AnalysisMarginData` for a single product given a pre-filtered sequence of `SalesDataPoint`s:

```csharp
AnalysisMarginData CalculateForProduct(
    AnalyticsProduct product,
    IEnumerable<SalesDataPoint> salesInPeriod);
```

The implementation must compute:
- `UnitsSold = sum(salesInPeriod.AmountB2B + salesInPeriod.AmountB2C)` cast to `int`
- `Revenue = (decimal)UnitsSold * product.SellingPrice`
- `Cost = (decimal)UnitsSold * (product.SellingPrice - product.MarginAmount)`
- `Margin = Revenue - Cost`
- `MarginPercentage = Revenue > 0 ? (Margin / Revenue) * 100 : 0`

**Acceptance criteria:**
- `IMarginCalculator.CalculateForProduct(product, salesInPeriod)` returns `AnalysisMarginData` matching the formulas above.
- Method is a pure function: no I/O, no DI on additional services, no state.
- When `salesInPeriod` is empty: `UnitsSold = 0`, `Revenue = 0`, `Cost = 0`, `Margin = 0`, `MarginPercentage = 0`.
- New unit tests in `MarginCalculatorTests` cover: (a) non-empty sales producing expected values, (b) empty sales producing zeros, (c) `SellingPrice = 0` producing `MarginPercentage = 0`, (d) mixed B2B+B2C sales summed correctly.

### FR-2: Update `GetMarginReportHandler` to use `IMarginCalculator`
Inject `IMarginCalculator` into `GetMarginReportHandler` and replace the inline formula at lines 113-117 with a call to `CalculateForProduct`. The handler must pass **only the sales filtered to the request period** (`product.SalesHistory.Where(s => s.Date >= startDate && s.Date <= endDate)`), bringing it into alignment with `GetProductMarginAnalysisHandler`.

**Acceptance criteria:**
- `GetMarginReportHandler` constructor accepts `IMarginCalculator`.
- Inline computation of `revenue`, `cost`, `margin`, `marginPercentage`, and `totalSales` is removed from `ProcessProductsForReport`.
- The handler now filters `SalesHistory` by `startDate`/`endDate` before passing to `CalculateForProduct` (correcting the pre-existing bug where unfiltered history was summed).
- Existing `GetMarginReportHandlerTests` still pass; any test that asserts on "all history" behavior is updated to reflect the corrected period-filtered semantics, with the change noted in the PR description.
- The check at `HasSalesInPeriod` (line 154) is retained as an early-skip for products with zero sales in the period.

### FR-3: Update `GetProductMarginAnalysisHandler` to use `IMarginCalculator`
Inject `IMarginCalculator` into `GetProductMarginAnalysisHandler` and replace the private `CalculateProductMargins` method (lines 128-148) with a call to `CalculateForProduct`. Remove the private method.

**Acceptance criteria:**
- `GetProductMarginAnalysisHandler` constructor accepts `IMarginCalculator`.
- `private static AnalysisMarginData CalculateProductMargins(...)` is deleted.
- Filtering `SalesHistory` by date range happens at the call site (as it does today) before invoking `CalculateForProduct`.
- Existing `GetProductMarginAnalysisHandlerTests` continue to pass without semantic changes.

### FR-4: Update `ReportBuilderService.BuildMonthlyBreakdown` to use `IMarginCalculator`
Inject `IMarginCalculator` into `ReportBuilderService` and update `BuildMonthlyBreakdown` to call `CalculateForProduct` once per month, using sales filtered to that month. The fields `Revenue`, `Cost`, `MarginAmount`, and `UnitsSold` on `MonthlyMarginBreakdown` are populated from the returned `AnalysisMarginData`.

**Acceptance criteria:**
- `ReportBuilderService` constructor accepts `IMarginCalculator`.
- The inline formula at lines 43-46 is removed.
- Each month's breakdown is computed by passing the month-filtered sales to `CalculateForProduct`.
- Existing `ReportBuilderServiceTests` (if any) continue to pass; values per month are identical to current behavior.

### FR-5: DI registration unchanged
`IMarginCalculator` is already registered in `AnalyticsModule.AddAnalyticsModule()` as `Scoped`. No registration changes are required. The two newly-injecting consumers (`GetMarginReportHandler`, `GetProductMarginAnalysisHandler`) are auto-registered by MediatR; `ReportBuilderService` is already registered as `Scoped`.

**Acceptance criteria:**
- No edits to `AnalyticsModule.cs` are needed.
- App boots successfully (`dotnet build` + integration smoke).

## Non-Functional Requirements

### NFR-1: Performance
The refactor must not regress measured throughput of any of the three use cases. `CalculateForProduct` is O(n) over the supplied sales sequence — same as the inline code it replaces. The method must enumerate `salesInPeriod` exactly once (no double-iteration via repeated `.Sum()` calls).

### NFR-2: Correctness / Determinism
Outputs for `GetProductMarginAnalysis` and `ReportBuilderService.BuildMonthlyBreakdown` must be bit-identical to current behavior for the same inputs. Outputs for `GetMarginReport` change only insofar as sales outside the requested period are no longer counted (the bug fix in FR-2).

### NFR-3: Testability
The new method must be unit-testable without DI infrastructure or mocks. Existing handler tests should not require new mocks for `IMarginCalculator` beyond a simple substitute, since the production implementation is a pure function and can be used directly in tests via `new MarginCalculator()`.

## Data Model
No data model changes. The refactor uses existing types:
- `Anela.Heblo.Domain.Features.Analytics.AnalyticsProduct`
- `Anela.Heblo.Domain.Features.Analytics.SalesDataPoint`
- `Anela.Heblo.Application.Features.Analytics.Contracts.AnalysisMarginData`

## API / Interface Design
**Interface change** (additive only):
```csharp
public interface IMarginCalculator
{
    // existing members unchanged ...

    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);
}
```

**Constructor signature changes:**
- `GetMarginReportHandler(IAnalyticsRepository, IProductFilterService, IReportBuilderService, IMarginCalculator)`
- `GetProductMarginAnalysisHandler(IAnalyticsRepository, IReportBuilderService, IMarginCalculator)`
- `ReportBuilderService(IMarginCalculator)`

No public HTTP API surface changes. No DTO changes. No frontend impact.

## Dependencies
- `Anela.Heblo.Application.Features.Analytics.Services.IMarginCalculator` (already exists, already DI-registered)
- `Anela.Heblo.Application.Features.Analytics.Contracts.AnalysisMarginData` (existing type)
- `Anela.Heblo.Domain.Features.Analytics.AnalyticsProduct` / `SalesDataPoint` (existing types)

No new NuGet packages. No new external services.

## Out of Scope
- Consolidating the `M0/M1/M2`-level margin formulas (`product.M{n}Amount × totalSales`) that appear in `MarginCalculator.CalculateAsync` and `GetProductMarginSummaryHandler.CalculateTotalMarginForLevel`. Those use a different formula (per-level margin amount, not selling-price-minus-cost) and are a separate consolidation opportunity.
- Changing the `AnalysisMarginData` shape or adding new fields.
- Touching the `SafeMarginCalculator` in the Catalog module — it serves a different purpose (catalog-level margin computation from cost components) and is not duplicating the analytics formula.
- Frontend changes; all consumers receive the same DTO shape.
- Performance optimization beyond avoiding double-enumeration of the sales sequence.

## Open Questions
None.

## Status: COMPLETE
```