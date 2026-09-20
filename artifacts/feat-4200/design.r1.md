# Design: Move stock-analysis business logic out of GetPurchaseStockAnalysisHandler

## Component Design

### `IStockAnalysisCalculator` / `StockAnalysisCalculator`

Location (unchanged): `backend/src/Anela.Heblo.Application/Features/Purchase/Services/{IStockAnalysisCalculator.cs, StockAnalysisCalculator.cs}`

Responsibility grows from "pure stock math" to "full per-item stock analysis, filtering, sorting, and summarization for the Purchase stock-analysis use case." Registration unchanged (`services.AddScoped<IStockAnalysisCalculator, StockAnalysisCalculator>()` in `PurchaseModule.cs`).

**New constructor dependency:** `IStockSeverityCalculator` (already registered as scoped in `PurchaseModule.cs`; lifetime-compatible with `IStockAnalysisCalculator`, also scoped).

```csharp
public class StockAnalysisCalculator : IStockAnalysisCalculator
{
    private readonly IStockSeverityCalculator _stockSeverityCalculator;

    public StockAnalysisCalculator(IStockSeverityCalculator stockSeverityCalculator)
    {
        _stockSeverityCalculator = stockSeverityCalculator;
    }

    // existing, unchanged:
    public double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock) { ... }
    public double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq) { ... }

    // new — moved from GetPurchaseStockAnalysisHandler.AnalyzeStockItem, verbatim logic
    public StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate) { ... }

    // new — moved from GetPurchaseStockAnalysisHandler.GetLastPurchaseInfo, verbatim logic
    // private helper, only caller is AnalyzeItem
    private LastPurchaseInfoDto? GetLastPurchaseInfo(MaterialStockSnapshot item) { ... }

    // new — moved from GetPurchaseStockAnalysisHandler.ShouldIncludeItem, verbatim logic,
    // wrapped in a List<T>.Where(...) the same way ItemFilterService.FilterItems does
    public List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request) { ... }
    private bool ShouldIncludeItem(StockAnalysisItemDto item, GetPurchaseStockAnalysisRequest request) { ... }

    // new — moved from GetPurchaseStockAnalysisHandler.SortItems, verbatim logic
    public List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending) { ... }

    // new — moved from GetPurchaseStockAnalysisHandler.CalculateSummary, verbatim logic
    public StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate) { ... }
}
```

`ShouldIncludeItem` is kept as a private helper inside `FilterItems` (mirrors today's shape — the handler currently exposes `ShouldIncludeItem` as its own private method called from a `.Where()` inline in `Handle()`; here it becomes a private helper called from `FilterItems`'s own `.Where()`). This keeps the single-item predicate independently readable/testable-by-proxy through `FilterItems`, without adding a fifth public method the interface doesn't need.

### `GetPurchaseStockAnalysisHandler`

Location (unchanged): `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`

Responsibility shrinks to: date-range defaulting/validation, fetch, category filter (`MaterialCategoryResolver.Matches`), delegate to `IStockAnalysisCalculator` for analyze/filter/sort/summary, apply the free-text search-term filter (unchanged, stays inline — see arch-review Decision 4), paginate, assemble `GetPurchaseStockAnalysisResponse`.

**Constructor shrinks from 5 to 4 dependencies** — `IStockSeverityCalculator` is removed (moves to `StockAnalysisCalculator`):

```csharp
public GetPurchaseStockAnalysisHandler(
    IMaterialCatalogService materialCatalog,
    IStockAnalysisCalculator stockAnalysisCalculator,
    ILogger<GetPurchaseStockAnalysisHandler> logger,
    TimeProvider timeProvider)
```

`Handle()` body (structure, not full logic — logic inside each step is unchanged from today):

```csharp
public async Task<GetPurchaseStockAnalysisResponse> Handle(
    GetPurchaseStockAnalysisRequest request,
    CancellationToken cancellationToken)
{
    // 1. date defaulting/validation — unchanged

    var snapshots = await _materialCatalog.GetStockAnalysisSnapshotsAsync(fromDate, toDate, cancellationToken);

    // 2. category filter + per-item analysis, delegated to the calculator
    var allAnalysisItems = snapshots
        .Where(s => MaterialCategoryResolver.Matches(s.ProductCode, request.MaterialCategory))
        .Select(s => _stockAnalysisCalculator.AnalyzeItem(s, fromDate, toDate))
        .ToList();

    // 3. status/configured filter, delegated
    var analysisItems = _stockAnalysisCalculator.FilterItems(allAnalysisItems, request);

    // 4. search-term filter — unchanged, stays inline in the handler
    if (!string.IsNullOrWhiteSpace(request.SearchTerm)) { /* unchanged */ }

    // 5. sort, delegated
    analysisItems = _stockAnalysisCalculator.SortItems(analysisItems, request.SortBy, request.SortDescending);

    // 6. pagination — unchanged

    // 7. summary — delegated, computed from allAnalysisItems (NOT analysisItems) — see Data Schemas invariant below
    var summary = _stockAnalysisCalculator.CalculateSummary(allAnalysisItems, fromDate, toDate);

    // 8. response assembly — unchanged
}
```

### Test components

- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` (585 lines) and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` (104 lines): existing handler-level tests continue to pass. Their setup must drop the `IStockSeverityCalculator` mock/stub from the `GetPurchaseStockAnalysisHandler` constructor call and, where a test's scenario depends on severity classification, supply that behavior through the `IStockAnalysisCalculator` fixture instead (either a real `StockAnalysisCalculator` wired with a real or stubbed `IStockSeverityCalculator`, or a test double for `IStockAnalysisCalculator` that returns pre-built `StockAnalysisItemDto`s with the desired `Severity`, whichever pattern the existing test fixture already favors — this is a mechanical test-setup change, not a new test-design decision).
- New file: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs` — focused unit tests directly against `StockAnalysisCalculator`, covering (mirroring spec FR-1..FR-4 acceptance criteria):
  - `AnalyzeItem`: dailyConsumption/daysUntilStockout/optimalStock computation, delegation to `CalculateStockEfficiency`/`CalculateRecommendedOrderQuantity`/`DetermineStockSeverity`, `LastPurchase` mapping (including the null-`LastPurchase` case), `IsConfigured` derivation.
  - `FilterItems`: each `StockStatusFilter` value in isolation, `OnlyConfigured` combined with a status filter, the default/no-filter case.
  - `SortItems`: each `StockAnalysisSortBy` value ascending and descending, including the default-fallback-to-`StockEfficiency` case for an unrecognized/default enum value.
  - `CalculateSummary`: all six counts, `TotalInventoryValue` (including the `LastPurchase == null` → `UnitPrice` treated as 0 case), `AnalysisPeriodStart`/`End` passthrough.

## Data Schemas

No schema changes — all types below are unchanged in shape; listed here only to confirm which ones the moved code touches.

**Unchanged input:** `MaterialStockSnapshot` (from `IMaterialCatalogService.GetStockAnalysisSnapshotsAsync`).

**Unchanged output DTOs** (all remain classes per project convention, not records):
- `StockAnalysisItemDto` — `ProductCode`, `ProductName`, `ProductNameNormalized`, `ProductType`, `AvailableStock`, `OrderedStock`, `EffectiveStock`, `MinStockLevel`, `OptimalStockLevel`, `ConsumptionInPeriod`, `DailyConsumption`, `DaysUntilStockout`, `StockEfficiencyPercentage`, `Severity`, `MinimalOrderQuantity`, `LastPurchase`, `Supplier`, `RecommendedOrderQuantity`, `IsConfigured`.
- `LastPurchaseInfoDto` — `Date`, `SupplierName`, `Amount`, `UnitPrice`, `TotalPrice`.
- `StockAnalysisSummaryDto` — `TotalProducts`, `CriticalCount`, `LowStockCount`, `OptimalCount`, `OverstockedCount`, `NotConfiguredCount`, `TotalInventoryValue`, `AnalysisPeriodStart`, `AnalysisPeriodEnd`.
- `GetPurchaseStockAnalysisRequest` — `FromDate`, `ToDate`, `MaterialCategory`, `StockStatus`, `OnlyConfigured`, `SearchTerm`, `SortBy`, `SortDescending`, `PageNumber`, `PageSize`, `IsExport`.
- `GetPurchaseStockAnalysisResponse` — `Items`, `TotalCount`, `PageNumber`, `PageSize`, `Summary`, plus the base error-response shape (`ErrorCodes`/params dictionary) used on the invalid-date-range path.

**Invariant to preserve exactly** (carried from arch-review Risk table, restated here as a design-level data-flow contract because it is the single highest-risk regression): `CalculateSummary` must always be called with the **post-category-filter, pre-status-filter, pre-search-filter** item list (`allAnalysisItems` in the code above) — never the paginated or status/search-filtered `analysisItems` list. `Summary.TotalProducts`/`CriticalCount`/etc. describe the full analyzed set for the selected material category and date range, independent of what the user is currently filtering or searching for on screen.

No new API endpoint, no new request/response contract, no OpenAPI regeneration needed (per `docs/development/api-client-generation.md` — the generated client is unaffected since no public request/response DTO shape changed).
