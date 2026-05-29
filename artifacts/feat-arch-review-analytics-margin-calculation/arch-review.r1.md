I have enough context to produce the review. The critical finding: `AnalyticsProduct.SalesHistory` is already pre-filtered by the repository adapter (`CatalogAnalyticsSourceAdapter` lines 35-36), so the spec's claim that `GetMarginReportHandler` sums "all" history is incorrect. I'll flag this as a spec amendment.

```markdown
# Architecture Review: Extract Single-Product Margin Calculation Helper

## Skip Design: true

## Architectural Fit Assessment

The refactor is a clean, low-risk consolidation that aligns with existing patterns:

- **Service abstraction already exists.** `IMarginCalculator` / `MarginCalculator` lives at `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`, is registered as `Scoped` in `AnalyticsModule.AddAnalyticsModule()`, and is already consumed by `GetProductMarginSummaryHandler`. Extending it is the correct home for the new helper — no new service, no new namespace.
- **Vertical-slice boundaries are respected.** All three call sites are inside the Analytics feature module; the helper does not need to cross module boundaries (`AnalysisMarginData`, `AnalyticsProduct`, `SalesDataPoint` are already accessible).
- **DI pattern matches established conventions.** Constructor injection through MediatR auto-registration for the two handlers and existing `Scoped` registration for `ReportBuilderService` — no `AnalyticsModule.cs` edit required, as the spec correctly states.
- **No HTTP / DTO surface change.** `AnalysisMarginData` shape is unchanged; the OpenAPI client regeneration is unaffected; no frontend impact.

**Critical correction to the spec's premise (see Specification Amendments):** the "pre-existing bug" claim in FR-2 is incorrect. `AnalyticsProduct.SalesHistory` is **already pre-filtered to the requested period** by `CatalogAnalyticsSourceAdapter.MapToAnalyticsProduct` (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs:35-36, 60-61`), and this contract is documented on `AnalyticsProduct.SalesHistory` itself (`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs:36-38`). The redundant `.Where(...)` in `GetProductMarginAnalysisHandler.CalculateProductMargins` (lines 130-132) is a no-op against today's adapter. The proposed re-filter in `GetMarginReportHandler` is therefore also a no-op semantically — not a bug fix. The refactor should still happen for consistency and to make the helper API explicit, but the spec must stop framing it as a behavior change.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ Analytics Feature Module                                        │
│                                                                  │
│  Services/                                                       │
│  ┌──────────────────────────────────────────┐                   │
│  │ IMarginCalculator (extended)             │                   │
│  │  + CalculateAsync(stream, ...)           │ existing          │
│  │  + GetGroupKey(...)                      │ existing          │
│  │  + GetGroupDisplayName(...)              │ existing          │
│  │  + GetMarginAmountForLevel(...)          │ existing          │
│  │  + CalculateForProduct(product, sales)   │ NEW (pure)        │
│  └──────────────────────────────────────────┘                   │
│           ▲                  ▲                   ▲              │
│           │                  │                   │              │
│   ┌───────┴───────┐  ┌───────┴────────┐  ┌──────┴──────────┐    │
│   │GetMarginReport│  │GetProductMargin│  │ReportBuilder    │    │
│   │   Handler     │  │AnalysisHandler │  │Service          │    │
│   │ (DI: NEW)     │  │ (DI: NEW)      │  │(DI: NEW)        │    │
│   └───────────────┘  └────────────────┘  └─────────────────┘    │
│      │                    │                     │              │
│      │ per product:       │ per product:        │ per month:   │
│      │ filter→Calculate   │ filter→Calculate    │ filter→      │
│      │ ForProduct         │ ForProduct          │ CalculateFor │
│      │                    │                     │ Product      │
│                                                                  │
│  Pre-existing consumer (NOT touched):                            │
│  ┌──────────────────────────────────────────┐                   │
│  │ GetProductMarginSummaryHandler           │                   │
│  │  (uses M0/M1/M2 path — different formula)│                   │
│  └──────────────────────────────────────────┘                   │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Extend `IMarginCalculator` rather than create a new interface or static helper
**Options considered:**
- (a) Add `CalculateForProduct` to `IMarginCalculator`.
- (b) Create a new `ISingleProductMarginCalculator` interface.
- (c) Add a `public static` extension/helper class with no DI.

**Chosen approach:** (a) — extend the existing interface.

**Rationale:** The existing interface already mixes concerns (`CalculateAsync` streaming aggregator, `GetGroupKey` mapper, `GetMarginAmountForLevel` lookup). It is the established home for "anything margin-shaped" in the Analytics module. Splitting interfaces would create cognitive churn with no clear boundary. A static helper would force test setup to bypass DI, and would break the spec's stated NFR-3 ("can be used directly in tests via `new MarginCalculator()`") because there would be no interface to mock if a test ever needs to. The interface is small enough (4 → 5 members) that growth is not a concern.

#### Decision 2: Caller pre-filters sales; helper does not know about date ranges
**Options considered:**
- (a) `CalculateForProduct(product, IEnumerable<SalesDataPoint> salesInPeriod)` — helper is date-agnostic; caller filters.
- (b) `CalculateForProduct(product, DateTime startDate, DateTime endDate)` — helper filters from `product.SalesHistory`.

**Chosen approach:** (a) — as specified.

**Rationale:** (a) is what the three call sites actually need: `BuildMonthlyBreakdown` filters by month (not by request range), `GetProductMarginAnalysisHandler` filters by request range, and `GetMarginReportHandler` would do the same. Embedding date-range logic in the helper would force `BuildMonthlyBreakdown` to either re-construct fake start/end dates per month or skip the helper for monthly slices. Keeping the helper purely arithmetic also makes it trivially unit-testable without `DateTime` fixtures.

#### Decision 3: Enumerate `salesInPeriod` exactly once
**Options considered:**
- (a) Call `salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C)` once and derive everything else from that single scalar.
- (b) Call `.Sum(...)` separately for `AmountB2B` and `AmountB2C` (two passes).

**Chosen approach:** (a).

**Rationale:** Matches the current inline behavior, satisfies NFR-1 (no double-iteration), and lets callers pass any `IEnumerable<SalesDataPoint>` — including non-rewindable sequences — without surprise. The implementation is a one-liner:
```csharp
var unitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
```
then arithmetic on the scalar.

#### Decision 4: Use the real `MarginCalculator` in handler tests, not a mock
**Options considered:**
- (a) Inject `new MarginCalculator()` in `GetMarginReportHandlerTests` and `GetProductMarginAnalysisHandlerTests`.
- (b) Inject `Mock<IMarginCalculator>` and set up `CalculateForProduct` expectations.

**Chosen approach:** (a).

**Rationale:** `MarginCalculator.CalculateForProduct` is a pure function. Mocking it would force the test to either (i) recompute the expected values (and re-introduce the formula in the test, defeating the consolidation), or (ii) rubber-stamp arbitrary outputs and lose end-to-end coverage. Real instance is strictly better here. Other `IMarginCalculator` members (`CalculateAsync`, etc.) are still unused by these handlers, so there is no surprise.

## Implementation Guidance

### Directory / Module Structure

**Files to modify (existing):**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — add `CalculateForProduct` to interface and class.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — add `IMarginCalculator` constructor parameter; replace lines 113-127 with a call to `_marginCalculator.CalculateForProduct(...)`; pass period-filtered sales (no-op against today's adapter, but explicit at call site). Retain `HasSalesInPeriod` early-skip at line 154.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — add `IMarginCalculator` constructor parameter; delete `CalculateProductMargins` (lines 128-148); call `_marginCalculator.CalculateForProduct(...)` from `Handle` with period-filtered sales (matching the existing `.Where(...)` filter at lines 104-106 used for the monthly breakdown).
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` — add `IMarginCalculator` constructor parameter; in `BuildMonthlyBreakdown` replace the inline formula at lines 43-46 with `_marginCalculator.CalculateForProduct(productData, monthSales)`.

**Files to create:**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs` — new file; **does not exist today** (verified via filesystem search). Contains the new unit tests required by FR-1 (non-empty, empty, `SellingPrice = 0`, mixed B2B+B2C).

**Files NOT to touch:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — no DI changes.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — uses the M0/M1/M2 formula path, explicitly out of scope.
- `backend/src/Anela.Heblo.Application/Features/Catalog/...` — `SafeMarginCalculator` and `CatalogAnalyticsSourceAdapter` are out of scope.
- `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs` — no domain change.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs
public interface IMarginCalculator
{
    // ...existing members unchanged...

    /// <summary>
    /// Computes per-product margin data over a pre-filtered sales sequence.
    /// Pure function: no I/O, no DI on other services, no state.
    /// Enumerates <paramref name="salesInPeriod"/> exactly once.
    /// </summary>
    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);
}
```

**Implementation contract:**
```csharp
public AnalysisMarginData CalculateForProduct(
    AnalyticsProduct product,
    IEnumerable<SalesDataPoint> salesInPeriod)
{
    var unitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
    var revenue   = (decimal)unitsSold * product.SellingPrice;
    var cost      = (decimal)unitsSold * (product.SellingPrice - product.MarginAmount);
    var margin    = revenue - cost;
    var marginPct = revenue > 0 ? (margin / revenue) * 100 : 0;

    return new AnalysisMarginData
    {
        Revenue = revenue,
        Cost = cost,
        Margin = margin,
        MarginPercentage = marginPct,
        UnitsSold = unitsSold
    };
}
```

**Constructor signature changes (final):**
```csharp
public GetMarginReportHandler(
    IAnalyticsRepository analyticsRepository,
    IProductFilterService productFilterService,
    IReportBuilderService reportBuilderService,
    IMarginCalculator marginCalculator);

public GetProductMarginAnalysisHandler(
    IAnalyticsRepository analyticsRepository,
    IReportBuilderService reportBuilderService,
    IMarginCalculator marginCalculator);

public ReportBuilderService(IMarginCalculator marginCalculator);
```

### Data Flow

**`GetMarginReport` flow (per product):**
```
products list
  → for each product:
      → HasSalesInPeriod(product, start, end)  [early skip]
      → salesInPeriod = product.SalesHistory.Where(s => s.Date >= start && s.Date <= end)
      → marginData = _marginCalculator.CalculateForProduct(product, salesInPeriod)
      → _reportBuilderService.BuildProductSummary(product, marginData)
      → accumulate category + overall totals
```

**`GetProductMarginAnalysis` flow:**
```
productData ← repository
  → HasSalesInPeriod check
  → salesInPeriod = productData.SalesHistory.Where(...) [request range]
  → marginData = _marginCalculator.CalculateForProduct(productData, salesInPeriod)
  → BuildSuccessResponse(... marginData ...)
  → if IncludeBreakdown: _reportBuilderService.BuildMonthlyBreakdown(salesInPeriod, productData, ...)
       (the salesInPeriod list is the SAME filter used here today — share the variable)
```

**`ReportBuilderService.BuildMonthlyBreakdown` flow (per month):**
```
for each month between [startDate, endDate]:
  → monthSales = salesData.Where(s => s.Date.Year == month.Year && s.Date.Month == month.Month)
  → marginData = _marginCalculator.CalculateForProduct(productData, monthSales)
  → emit MonthlyMarginBreakdown { Revenue=marginData.Revenue, Cost=marginData.Cost,
                                  MarginAmount=marginData.Margin, UnitsSold=marginData.UnitsSold }
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec mis-describes the FR-2 change as a bug fix; reviewers/PR description could record incorrect history. | Medium | Update spec (see Specification Amendments). PR description must state: "behavior unchanged for all three handlers — `SalesHistory` is already period-filtered upstream by `CatalogAnalyticsSourceAdapter`." |
| Future change to `IAnalyticsProductSource` that stops pre-filtering `SalesHistory` would silently break `GetMarginReport` if we rely on the adapter's filter. | Low | The new code adds an explicit `.Where(...)` at the call site, so it is robust to either contract. This is actually an upside of the refactor — document it in code-review notes, not via a comment. |
| `GetMarginReportHandlerTests` and `GetProductMarginAnalysisHandlerTests` instantiate the handler with the old constructor signature — they will not compile after the refactor. | Low | Add a `new MarginCalculator()` argument in each test's constructor. Per Decision 4, no mock setup required. |
| The `BuildProductSummary` path in `GetMarginReportHandler` currently uses the `marginData.Margin/Revenue/Cost/UnitsSold/MarginPercentage` fields. Helper output must populate all five fields identically. | Low | Spec FR-1 enumerates all five fields. Add a property-by-property assertion in the FR-1 unit test (non-empty case). |
| Double enumeration of `salesInPeriod` if a caller passes an `IEnumerable` that materializes lazily and we iterate twice. | Low | Decision 3: implement with a single `.Sum(...)` call. Add a unit test that passes a one-shot `IEnumerable` (e.g. `YieldingSales()` iterator) to prove single-enumeration. |
| `monthSales` in `BuildMonthlyBreakdown` is currently materialized with `.ToList()` per month before the inline formula. After refactor, pass the `IEnumerable` directly (single Sum). | Low | Pass `monthSales` without `.ToList()` — saves an allocation per month. Document in PR. |

## Specification Amendments

1. **FR-2 framing is wrong; correct it.** The clause "correcting the pre-existing bug where unfiltered history was summed" must be replaced with: "the explicit `Where` filter at the call site is a no-op against the current `CatalogAnalyticsSourceAdapter`, which pre-filters `SalesHistory` to the requested period (see `AnalyticsProduct.SalesHistory` XML doc). The explicit filter is added for clarity and future robustness, not behavior change." The "Background" paragraph claiming `GetMarginReportHandler` "sums **all** of `product.SalesHistory` (unfiltered by the request period)" is incorrect and must be removed.

2. **Update FR-2 acceptance criterion** that currently reads *"any test that asserts on 'all history' behavior is updated to reflect the corrected period-filtered semantics, with the change noted in the PR description"* — there is no such test today (verified in `GetMarginReportHandlerTests.cs`), and there should be no behavior change to assert. Replace with: *"`GetMarginReportHandlerTests` continue to pass without semantic changes; only the constructor invocation is updated."*

3. **Update NFR-2** to remove the carve-out: *"Outputs for `GetMarginReport` change only insofar as sales outside the requested period are no longer counted (the bug fix in FR-2)."* — should become *"Outputs for all three use cases must be bit-identical to current behavior."*

4. **FR-1 test file** — the spec says "New unit tests in `MarginCalculatorTests` cover…". This file does **not** exist in `backend/test/Anela.Heblo.Tests/Features/Analytics/`. Amend FR-1 to: *"Create a new test file `backend/test/Anela.Heblo.Tests/Features/Analytics/MarginCalculatorTests.cs` with the cases listed."* Add one more case: *"(e) helper enumerates `salesInPeriod` exactly once (verified by passing a one-shot `IEnumerable` and asserting no `InvalidOperationException`)."*

5. **FR-4 test gap** — there is no `ReportBuilderServiceTests` file today; the spec's "if any" is correct. No new tests are strictly required, but if any consumer test (e.g. `GetProductMarginAnalysisHandlerTests` with `IncludeBreakdown = true`) covers monthly numbers, it must continue to pass — list it as a required-to-pass test in the PR description so reviewers can spot-check.

6. **PR description must call out** that `BuildMonthlyBreakdown` no longer needs `.ToList()` on `monthSales` — minor allocation reduction, not a behavior change.

## Prerequisites

None. All required types, DI registrations, and modules already exist in `main`. No migrations, no config changes, no infrastructure changes. The work is purely internal refactor + new unit-test file.
```