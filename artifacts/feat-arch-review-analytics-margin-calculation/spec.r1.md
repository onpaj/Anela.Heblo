# Specification: Consolidate Per-Product Margin Calculation in Analytics Module

## Summary
The core per-product margin formula (`revenue = units × SellingPrice`, `cost = units × (SellingPrice − MarginAmount)`, `margin = revenue − cost`) is duplicated inline across three Analytics components, has already drifted semantically between them, and bypasses the existing `IMarginCalculator` service. This refactor consolidates the formula into a single method on `IMarginCalculator`, replaces all three inline copies with calls to it, and removes the now-redundant private helper in `GetProductMarginAnalysisHandler`.

## Background
The Analytics module computes per-product margin data in three call sites:

1. `GetMarginReportHandler.ProcessProductsForReport()` (lines 112–116) — sums **all** `SalesHistory` entries on the product, ignoring the request's date range.
2. `GetProductMarginAnalysisHandler.CalculateProductMargins()` (lines 138–146) — filters `SalesHistory` to the request's date range, then sums.
3. `ReportBuilderService.BuildMonthlyBreakdown()` (lines 41–44) — operates on pre-filtered, per-month sales passed in by the caller.

All three implement the same arithmetic with the same fields (`SellingPrice`, `MarginAmount`, `AmountB2B + AmountB2C`). The shared `IMarginCalculator` / `MarginCalculator` already exists in `Application/Features/Analytics/Services/MarginCalculator.cs`, is registered in `AnalyticsModule`, and is used correctly by `GetProductMarginSummaryHandler`. However, it only exposes a *streaming aggregation* method (`CalculateAsync`); it has no single-product entry point, which is why the three handlers/services duplicate the formula instead of reusing it.

Two real consequences of this duplication:

- **Semantic drift already present.** `GetMarginReportHandler` and `GetProductMarginAnalysisHandler` ask the same business question ("margin for a product in a period") but compute different totals because one filters `SalesHistory` and the other does not. The duplication makes this drift invisible at review time.
- **Change amplification.** The formula simplifies to `units × MarginAmount`. Any change to the business definition of margin (different cost component, different unit aggregation, additional fees) requires synchronized edits across three files and the risk of further drift.

## Functional Requirements

### FR-1: Add a single-product margin calculation method to `IMarginCalculator`
Extend the existing `IMarginCalculator` interface (`backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`) with a new method:

```csharp
AnalysisMarginData CalculateForProduct(
    AnalyticsProduct product,
    IEnumerable<SalesDataPoint> salesInPeriod);
```

Implement it once in `MarginCalculator` using the canonical formula:

```csharp
var units = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
var revenue = (decimal)units * product.SellingPrice;
var cost = (decimal)units * (product.SellingPrice - product.MarginAmount);
var margin = revenue - cost;
var marginPercentage = revenue > 0 ? (margin / revenue) * 100m : 0m;
return new AnalysisMarginData { Revenue = revenue, Cost = cost, Margin = margin, MarginPercentage = marginPercentage, UnitsSold = units };
```

The caller is responsible for filtering `SalesHistory` to the desired period before passing it in. This makes the period-filtering decision an explicit, visible call-site concern instead of a hidden behavior of the calculator.

**Acceptance criteria:**
- New method `CalculateForProduct(AnalyticsProduct, IEnumerable<SalesDataPoint>)` exists on both `IMarginCalculator` and `MarginCalculator`.
- Existing `CalculateAsync` / `GetGroupKey` / `GetGroupDisplayName` / `GetMarginAmountForLevel` methods remain unchanged in signature and behavior.
- New method is covered by direct unit tests: empty sales → zero result; mixed B2B/B2C entries → correct sums; `SellingPrice = 0` → zero revenue and `marginPercentage = 0`; large `decimal` values → no overflow.
- `MarginCalculator` continues to be registered as scoped in `AnalyticsModule.AddAnalyticsModule()`.

### FR-2: Replace inline formula in `GetMarginReportHandler` with `IMarginCalculator`
Inject `IMarginCalculator` into `GetMarginReportHandler` (constructor) and replace the inline calculation in `ProcessProductsForReport()` (lines 111–127) with a call to `_marginCalculator.CalculateForProduct(product, salesInPeriod)`.

**Behavior change — intentional.** The current handler sums `product.SalesHistory` without filtering by `request.StartDate / request.EndDate`. This is a latent bug: the existing `HasSalesInPeriod` check is a gate, but once a product passes the gate, sales from *outside* the period are also counted. After this refactor, the handler MUST filter `SalesHistory` to `[StartDate, EndDate]` before passing it to the calculator, matching the behavior of `GetProductMarginAnalysisHandler`.

**Acceptance criteria:**
- `GetMarginReportHandler` constructor takes `IMarginCalculator`.
- The inline revenue/cost/margin/marginPercentage computation in `ProcessProductsForReport()` is removed.
- Per-product sales are filtered to `[request.StartDate, request.EndDate]` before being passed to `CalculateForProduct`.
- Existing `GetMarginReportHandlerTests` are updated to reflect the new (correct) period-filtered totals and the new constructor signature.
- A regression test is added: a product with sales both inside and outside the report period produces totals that count only the in-period sales.

### FR-3: Replace inline formula in `GetProductMarginAnalysisHandler` with `IMarginCalculator`
Inject `IMarginCalculator` into `GetProductMarginAnalysisHandler`, remove the private static `CalculateProductMargins()` method (lines 133–156), and call `_marginCalculator.CalculateForProduct(productData, salesInPeriod)` from `Handle()`.

**Acceptance criteria:**
- `GetProductMarginAnalysisHandler` constructor takes `IMarginCalculator`.
- Private static `CalculateProductMargins` is deleted.
- The handler filters `SalesHistory` to `[request.StartDate, request.EndDate]` before passing to the calculator (preserving today's behavior).
- Existing `GetProductMarginAnalysisHandlerTests` continue to pass with no expected-value changes; the only test edits are to constructor wiring.

### FR-4: Replace inline formula in `ReportBuilderService.BuildMonthlyBreakdown`
Inject `IMarginCalculator` into `ReportBuilderService` and replace the inline revenue/cost/margin computation in `BuildMonthlyBreakdown()` (lines 41–44) with a call to `_marginCalculator.CalculateForProduct(productData, monthSales)`.

The caller of `BuildMonthlyBreakdown` continues to pass `salesData` already filtered to the analysis period; this method then sub-filters per month and delegates the arithmetic.

**Note:** The brief suggests an alternative shape — making the caller pass pre-computed `AnalysisMarginData` per month into a smaller `BuildMonthlyBreakdown` overload. This spec rejects that alternative because (a) it pushes the month-bucketing loop up into the only caller (`GetProductMarginAnalysisHandler`), increasing call-site complexity, and (b) the current shape already accepts `salesData` plus `productData`, so injecting `IMarginCalculator` is the minimal change. The bucketing-by-month logic stays in `ReportBuilderService`; only the per-bucket arithmetic is delegated.

**Acceptance criteria:**
- `ReportBuilderService` constructor takes `IMarginCalculator`; `IReportBuilderService` interface is unchanged.
- The inline `monthlyRevenue / monthlyCost / monthlyMargin` lines are removed.
- `MonthlyMarginBreakdownDto` continues to be populated with the same field semantics as today (verified by existing tests).
- Unit test confirms a month with zero sales produces a row with `UnitsSold = 0`, `Revenue = 0`, `Cost = 0`, `MarginAmount = 0`.

### FR-5: DI registration unchanged
`AnalyticsModule.AddAnalyticsModule()` already registers `IMarginCalculator` as scoped. No new registrations are required. The three components that now depend on `IMarginCalculator` resolve it from the existing scoped registration.

**Acceptance criteria:**
- `AnalyticsModule.cs` requires no edits beyond what the compiler forces (none expected).
- Application starts and the DI container resolves `GetMarginReportHandler`, `GetProductMarginAnalysisHandler`, and `IReportBuilderService` without error (verified by an integration test or smoke run).

## Non-Functional Requirements

### NFR-1: Performance
Refactor must be performance-neutral. The new method does the same arithmetic as the inlined version; the only added cost per call is one virtual dispatch and one IEnumerable enumeration. Both are negligible against the existing per-product LINQ work. No new allocations on hot paths beyond the `AnalysisMarginData` instance already created today.

### NFR-2: Correctness & test coverage
- All existing tests in `backend/test/Anela.Heblo.Tests/Features/Analytics/` continue to pass after updates (test edits limited to constructor wiring and the FR-2 period-filtering correction).
- New direct unit tests for `MarginCalculator.CalculateForProduct` cover: empty input, B2B-only, B2C-only, mixed B2B+B2C, zero `SellingPrice`, zero `MarginAmount`, large-value safety.
- Regression test for FR-2 proves the period-filter fix.

### NFR-3: Backward compatibility
- Public API responses (`GetMarginReportResponse`, `GetProductMarginAnalysisResponse`) are unchanged in shape.
- The only observable behavior change is the FR-2 correction (margin-report totals now exclude out-of-period sales). This is a bug fix, not a contract change.

### NFR-4: Coding standards
- `IMarginCalculator` lives in `Application/Features/Analytics/Services/` — keep it there.
- Follow project rules from `CLAUDE.md`: surgical changes only; no formatting churn outside touched lines; no new comments unless the why is non-obvious.

## Data Model
No schema changes. The refactor relies entirely on existing types:
- `Anela.Heblo.Domain.Features.Analytics.AnalyticsProduct` — source of `SellingPrice`, `MarginAmount`, `SalesHistory`.
- `Anela.Heblo.Domain.Features.Analytics.SalesDataPoint` — source of `AmountB2B`, `AmountB2C`, `Date`.
- `Anela.Heblo.Application.Features.Analytics.Contracts.AnalysisMarginData` — return type, unchanged.

## API / Interface Design

**Modified interface** (additive):
```csharp
// Application/Features/Analytics/Services/MarginCalculator.cs
public interface IMarginCalculator
{
    // ...existing members unchanged...
    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);
}
```

**Modified constructors**:
- `GetMarginReportHandler(IAnalyticsRepository, IProductFilterService, IReportBuilderService, IMarginCalculator)`
- `GetProductMarginAnalysisHandler(IAnalyticsRepository, IReportBuilderService, IMarginCalculator)`
- `ReportBuilderService(IMarginCalculator)`

No HTTP/MediatR contract changes. No frontend changes. No database migration.

## Dependencies
- Existing `IMarginCalculator` / `MarginCalculator` (in-module).
- Existing DI registration in `AnalyticsModule`.
- Existing test fixtures and mocks in `backend/test/Anela.Heblo.Tests/Features/Analytics/`.
- No new NuGet packages.

## Out of Scope
- Catalog module's `SafeMarginCalculator` (`Application/Features/Catalog/Services/SafeMarginCalculator.cs`) — separate concern, different domain, different formula contract. Not touched.
- `GetProductMarginSummaryHandler` — already uses `IMarginCalculator` correctly; the group-level aggregation in `CalculateGroupMarginData` / `CalculateTotalMarginForLevel` is a different shape and stays as-is.
- `MonthlyBreakdownGenerator` (used by `GetProductMarginSummaryHandler`) — not one of the three duplicated sites; out of scope.
- Generalizing `AnalysisMarginData` into a value object or making `IMarginCalculator` work with M0/M1/M2 levels for the single-product path. The current single-product call sites use only `MarginAmount` (the legacy field), not the M0–M2 amounts; preserving that keeps the refactor surgical.
- Frontend changes; OpenAPI client regeneration is not required because no DTOs change.

## Open Questions
None.

## Status: COMPLETE