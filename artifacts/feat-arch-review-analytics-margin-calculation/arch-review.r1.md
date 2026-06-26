I have enough context. Now writing the architecture review.

# Architecture Review: Consolidate Per-Product Margin Calculation in Analytics Module

## Skip Design: true

Backend refactor only. No UI, no API contract changes, no new visual components.

## Architectural Fit Assessment

The proposal aligns cleanly with the existing Analytics module structure:

- `IMarginCalculator` already lives in `Application/Features/Analytics/Services/` and is the established home for margin arithmetic. Extending it (rather than creating a new helper, static, or extension) keeps a single named seam for "margin formula in Analytics."
- `AnalysisMarginData` (`Application/Features/Analytics/Contracts/`) is already the agreed return shape and is consumed by `IReportBuilderService.BuildProductSummary` — extending `IMarginCalculator` to return it composes naturally with the existing builder pipeline.
- DI: `MarginCalculator` is already registered scoped in `AnalyticsModule.AddAnalyticsModule()`. The three new dependencies (`GetMarginReportHandler`, `GetProductMarginAnalysisHandler`, `ReportBuilderService`) all live in the same module and resolve from the same scope — no cross-module wiring.
- Vertical-slice rules are preserved: no DTO leaves the module, no new interface escapes `Features/Analytics/`, and no record-vs-class concerns (the new method returns the existing class `AnalysisMarginData`).

**One material discrepancy with the spec** — flagged in *Specification Amendments* below. The `CatalogAnalyticsSourceAdapter` already filters `SalesHistory` to `[fromDate, toDate]` at the repository boundary (`CatalogAnalyticsSourceAdapter.cs:34-42, 59-67`), and `AnalyticsProduct.SalesHistory`'s XML doc states *"Sales history only for the requested period (filtered by repository)"*. The spec's claim that `GetMarginReportHandler` has a latent period-filter bug is therefore wrong against the current implementation — the data it receives is already filtered. This changes the framing of FR-2 (see amendments).

## Proposed Architecture

### Component Overview

```
                                    AnalyticsModule (DI scope)
                                              │
   ┌──────────────────────────────────────────┼──────────────────────────────────────┐
   │                                          │                                      │
GetMarginReportHandler        GetProductMarginAnalysisHandler           ReportBuilderService
   │                                          │                                      │
   │  ProcessProductsForReport                │  Handle / CalculateProductMargins    │  BuildMonthlyBreakdown
   │  (per product, in period)                │  (one product, in period)            │  (per month, in period)
   │                                          │                                      │
   └──────────────┬───────────────────────────┴────────────────────┬─────────────────┘
                  │                                                │
                  ▼                                                ▼
              IMarginCalculator.CalculateForProduct(product, salesInPeriod) ─► AnalysisMarginData
                  │
                  └──► single implementation in MarginCalculator
                        units  = sum(B2B + B2C)
                        revenue = units * SellingPrice
                        cost    = units * (SellingPrice − MarginAmount)
                        margin  = revenue − cost
                        marginPct = revenue > 0 ? margin/revenue * 100 : 0
```

`MonthlyBreakdownGenerator` (used by `GetProductMarginSummaryHandler`) and `MarginCalculator.CalculateAsync` are untouched — they operate on the M0/M1/M2 path, not the legacy `MarginAmount` formula.

### Key Design Decisions

#### Decision 1: Extend `IMarginCalculator` vs. introduce a new helper
**Options considered:**
- (A) Extend `IMarginCalculator` with `CalculateForProduct(AnalyticsProduct, IEnumerable<SalesDataPoint>)`.
- (B) Add a static helper or extension method (`MarginMath.For(...)`).
- (C) Add a second narrower interface (e.g. `ISingleProductMarginCalculator`).

**Chosen approach:** (A), as specified.

**Rationale:** `IMarginCalculator` is already the named seam for "Analytics margin arithmetic." Adding a parallel helper or splitting into two interfaces fragments the seam without benefit — every caller already takes a scoped service, every caller already injects from `AnalyticsModule`. A static helper would also block test substitution, which the spec explicitly requires.

#### Decision 2: Caller filters sales by period; calculator does not
**Options considered:**
- (A) Calculator takes pre-filtered `IEnumerable<SalesDataPoint>`.
- (B) Calculator takes `AnalyticsProduct` + `DateTime startDate, DateTime endDate` and filters internally.

**Chosen approach:** (A), as specified.

**Rationale:** Two reasons. First, the repository **already filters `SalesHistory` to the requested period** (`CatalogAnalyticsSourceAdapter.cs:34`). Pushing range parameters into the calculator would re-litigate that contract and invite double-filtering. Second, `ReportBuilderService.BuildMonthlyBreakdown` sub-filters per month — its callers need the slice-then-calculate shape. A single signature that matches both call sites is cleaner than two overloads.

#### Decision 3: Keep `AnalysisMarginData` as a mutable class
**Options considered:**
- (A) Continue returning the existing class.
- (B) Convert `AnalysisMarginData` to an immutable record or readonly struct.

**Chosen approach:** (A).

**Rationale:** Out of scope per the spec; the project rule "DTOs are classes, never records" applies to API-shaped types but `AnalysisMarginData` is an internal contract — still, a conversion now is unrelated churn. Keep the type unchanged.

#### Decision 4: Pre-materialize sales or accept `IEnumerable` directly
**Options considered:**
- (A) Accept `IEnumerable<SalesDataPoint>` and enumerate once inside the method.
- (B) Accept `IReadOnlyCollection<SalesDataPoint>` or `List<SalesDataPoint>`.

**Chosen approach:** (A).

**Rationale:** A single `Sum(s => s.AmountB2B + s.AmountB2C)` enumerates exactly once. Forcing `IReadOnlyCollection` would push `.ToList()` allocations into every call site. Callers already hold filtered `List<SalesDataPoint>` (handlers) or LINQ-filtered enumerations (builder) — `IEnumerable` accepts both without coercion.

## Implementation Guidance

### Directory / Module Structure

No new files. All edits are in-place:

```
backend/src/Anela.Heblo.Application/Features/Analytics/
  Services/
    MarginCalculator.cs                 (modify: add interface member + implementation)
    ReportBuilderService.cs             (modify: inject IMarginCalculator, swap formula)
  UseCases/
    GetMarginReport/
      GetMarginReportHandler.cs         (modify: inject IMarginCalculator, swap formula)
    GetProductMarginAnalysis/
      GetProductMarginAnalysisHandler.cs (modify: inject IMarginCalculator, delete private method)

backend/test/Anela.Heblo.Tests/Features/Analytics/
  MarginCalculatorTests.cs              (NEW: direct unit tests per FR-1 / NFR-2)
  GetMarginReportHandlerTests.cs        (modify: constructor wiring + see amendment below)
  GetProductMarginAnalysisHandlerTests.cs (modify: constructor wiring only)
  ReportBuilderServiceTests.cs          (NEW or modify: zero-sales-month regression test)
```

`AnalyticsModule.cs` requires **no edits**.

### Interfaces and Contracts

```csharp
// Application/Features/Analytics/Services/MarginCalculator.cs

public interface IMarginCalculator
{
    // ...existing members unchanged...

    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);
}

public class MarginCalculator : IMarginCalculator
{
    // ...existing members unchanged...

    public AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod)
    {
        var units = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
        var revenue = (decimal)units * product.SellingPrice;
        var cost = (decimal)units * (product.SellingPrice - product.MarginAmount);
        var margin = revenue - cost;
        var marginPercentage = revenue > 0 ? (margin / revenue) * 100m : 0m;

        return new AnalysisMarginData
        {
            Revenue = revenue,
            Cost = cost,
            Margin = margin,
            MarginPercentage = marginPercentage,
            UnitsSold = units,
        };
    }
}
```

Constructor signatures after refactor:

```csharp
GetMarginReportHandler(
    IAnalyticsRepository, IProductFilterService, IReportBuilderService, IMarginCalculator)

GetProductMarginAnalysisHandler(
    IAnalyticsRepository, IReportBuilderService, IMarginCalculator)

ReportBuilderService(IMarginCalculator)
```

`IReportBuilderService` is **not** changed — only the implementation gains a ctor parameter.

### Data Flow

**Margin report (`GetMarginReportHandler`):**

```
Request → AnalyticsRepository.StreamProductsWithSalesAsync(start, end)
            │ (repository pre-filters SalesHistory to period)
            ▼
        ProductFilterService.FilterProductsAsync
            ▼
        foreach product:
            HasSalesInPeriod(product) → gate
            marginData = _marginCalculator.CalculateForProduct(product, product.SalesHistory)
            reportBuilderService.BuildProductSummary(product, marginData)
            AccumulateCategoryTotals(...)
            overallTotals.Add(marginData)
            ▼
        BuildSuccessResponse → GetMarginReportResponse
```

**Product analysis (`GetProductMarginAnalysisHandler`):**

```
Request → AnalyticsRepository.GetProductAnalysisDataAsync(productId, start, end)
            │ (repository pre-filters SalesHistory to period)
            ▼
        HasSalesInPeriod → gate
        salesInPeriod = product.SalesHistory   ← already filtered; no re-filter needed
        marginData = _marginCalculator.CalculateForProduct(product, salesInPeriod)
            ▼
        if IncludeBreakdown:
            reportBuilderService.BuildMonthlyBreakdown(salesInPeriod, product, start, end)
            ▼ (inside builder)
            foreach month:
                monthSales = salesInPeriod.Where(month match)
                monthData = _marginCalculator.CalculateForProduct(product, monthSales)
            ▼
        BuildSuccessResponse → GetProductMarginAnalysisResponse
```

The defensive `.Where(s => s.Date >= start && s.Date <= end)` in `GetProductMarginAnalysisHandler` (today's lines 103-105, 138-140) becomes redundant given the repository contract; see amendment below.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec asserts an FR-2 "bug fix" that does not exist — `SalesHistory` is already period-filtered at the repository (`CatalogAnalyticsSourceAdapter.cs:34-42`). Implementing FR-2 as written would change no observable behavior but would re-litigate the contract by adding a second filter at the handler level. | High | Drop the "behavior change" framing from FR-2. The handler should still pass `product.SalesHistory` to the calculator; semantics are unchanged. Update the FR-2 regression test to assert that the repository contract is respected, not that the handler filters. See amendment. |
| Drift between the "calculator filters" and "caller filters" mental models could cause future double-filtering when someone adds a non-repository code path. | Medium | Add a one-line XML `<remarks>` on the new `CalculateForProduct` method stating *"caller must pre-filter to the desired period; calculator sums verbatim"*. This is one of the rare cases where a comment captures a non-obvious invariant per CLAUDE.md. |
| `salesInPeriod` enumerated multiple times if callers pass a deferred LINQ query (currently they pass `List<>` or already-materialized sequences, but this could regress). | Low | The implementation enumerates exactly once (`Sum`). Leave the parameter as `IEnumerable<SalesDataPoint>` and document the single-pass contract in the same `<remarks>`. |
| `MarginAmount <= 0` products: the existing `CalculateAsync` skips them; `CalculateForProduct` will compute zero-cost-zero-margin output for them instead. The three callers today already calculate-for-all (they don't gate on `MarginAmount > 0`), so behavior matches today's. | Low | Document the divergence in the new method's `<remarks>`: *"unlike CalculateAsync, does not skip products with MarginAmount ≤ 0; per-product callers report them with zero margin."* |
| Test suite churn: many existing tests construct handlers directly. | Low | Wiring-only edits. Use Moq's `Mock<IMarginCalculator>` returning the canonical `AnalysisMarginData`; existing assertions on response values remain valid. |
| `ReportBuilderService` now has a non-zero-arg constructor; any test that instantiates it directly (not via DI) breaks. | Low | Audit `backend/test/Anela.Heblo.Tests/Features/Analytics/` for direct `new ReportBuilderService()` calls before changing the ctor; update each with `new ReportBuilderService(Mock.Of<IMarginCalculator>())` or a thin in-test fake that delegates to the real `MarginCalculator`. |

## Specification Amendments

**Amendment 1 — FR-2: remove the "behavior change" claim.**
Replace the second paragraph of FR-2 with:

> The current handler passes `product.SalesHistory` directly to the inline formula. `SalesHistory` is already filtered to `[StartDate, EndDate]` by `CatalogAnalyticsSourceAdapter` at the repository boundary (`AnalyticsProduct.SalesHistory` doc-comment: *"Sales history only for the requested period (filtered by repository)"*). The refactor passes the same `product.SalesHistory` to `_marginCalculator.CalculateForProduct(product, product.SalesHistory)`. **No behavior change.** Existing `GetMarginReportHandlerTests` expectations remain valid; only constructor wiring changes.

Drop the "regression test for the period-filter fix" from FR-2's acceptance criteria. If desired, replace it with a repository-level test that proves `StreamProductsWithSalesAsync` returns `SalesHistory` filtered to the requested period (separate concern, may already exist in `AnalyticsRepositoryTests`).

**Amendment 2 — FR-3: drop the redundant defensive filter.**
The `private static CalculateProductMargins` method does `salesInPeriod = product.SalesHistory.Where(s => s.Date >= startDate && s.Date <= endDate).ToList()`. Given Amendment 1's clarification, this filter is dead code. After deleting `CalculateProductMargins`, the call site should be:

```csharp
var marginData = _marginCalculator.CalculateForProduct(productData, productData.SalesHistory);
```

The pre-existing `salesInPeriod` filter inside `BuildSuccessResponse` (line 103-105) for the monthly breakdown branch should also be removed for the same reason — pass `productData.SalesHistory` directly to `BuildMonthlyBreakdown`.

**Amendment 3 — FR-1: clarify the `MarginAmount ≤ 0` semantics.**
Add to FR-1 acceptance criteria: *"`CalculateForProduct` does not gate on `product.MarginAmount > 0`; it computes the canonical formula for any input. (`CalculateAsync` skip-on-non-positive remains unchanged.)"*

**Amendment 4 — FR-1: explicit single-enumeration contract.**
Add to FR-1: *"`salesInPeriod` is enumerated exactly once; callers may pass any `IEnumerable<SalesDataPoint>`. The calculator does not materialize the sequence."*

**Amendment 5 — Out of Scope: add `MarginAmount > 0` parity audit.**
Add to "Out of Scope": *"Reconciling the `MarginAmount > 0` skip in `CalculateAsync` with `CalculateForProduct`'s non-skipping behavior. The two methods serve different shapes (M0/M1/M2 aggregation vs. legacy `MarginAmount` per-product) and the divergence is intentional for this refactor."*

## Prerequisites

None.

- No migrations.
- No infrastructure changes.
- No DI changes (`MarginCalculator` is already registered scoped).
- No NuGet additions.
- No OpenAPI regeneration (no DTOs change).
- No feature flag.

Begin implementation directly against the spec as amended above.