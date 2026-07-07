### task: wire-top-product-sorter-into-handler

Adds `ITopProductSorter` as a new constructor dependency on `GetProductMarginSummaryHandler`, removes the now-redundant `ApplySorting` private method, and updates the call site in `GenerateTopProducts`. Updates both handler-construction call sites in `GetProductMarginSummaryHandlerTests.cs` to pass a real (non-mocked) `TopProductSorter` instance, matching the existing pattern for `MarginCalculator`/`MonthlyBreakdownGenerator`. This is the final task — after it, `GetProductMarginSummaryHandler.cs` contains only orchestration (`Handle`, `GenerateTopProducts`, `CalculateTotalMarginForLevel`).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

Context — after the previous two tasks, the handler file currently reads (relevant parts):

```csharp
public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _timeWindowParser = timeWindowParser;
    }
    // ... Handle(...) unchanged ...

    private List<TopProductDto> GenerateTopProducts(MarginCalculationResult calculationResult, ProductGroupingMode groupingMode, string? sortBy, bool sortDescending, MarginLevel marginLevel)
    {
        var topProductsWithData = calculationResult.GroupTotals
            .Select(kvp => { /* ... unchanged, uses _marginCalculator.GetGroupAggregatedMarginData(products) ... */ })
            .ToList();

        // Apply sorting
        var sortedProducts = ApplySorting(topProductsWithData, sortBy, sortDescending);

        // Add rank after sorting
        for (int i = 0; i < sortedProducts.Count; i++)
        {
            sortedProducts[i].Rank = i + 1;
        }

        return sortedProducts;
    }

    private List<TopProductDto> ApplySorting(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        // ... the full 13-branch switch, unchanged from before this refactor started ...
    }

    private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, MarginLevel marginLevel)
    {
        return products.Sum(p =>
            (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)
            * _marginCalculator.GetMarginAmountForLevel(p, marginLevel));
    }
}
```

- [ ] **Step 1: Write the failing test (constructor signature)**

In `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`, add a field and wire it into the constructor. Change:

```csharp
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
        var timeProvider = new FakeTimeProvider(FrozenNow);
        _timeWindowParser = new TimeWindowParser(timeProvider);
        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            _marginCalculator,
            _monthlyBreakdownGenerator,
            _timeWindowParser);
    }
```

to:

```csharp
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TopProductSorter _topProductSorter;
    private readonly TimeWindowParser _timeWindowParser;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
        _topProductSorter = new TopProductSorter();
        var timeProvider = new FakeTimeProvider(FrozenNow);
        _timeWindowParser = new TimeWindowParser(timeProvider);
        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            _marginCalculator,
            _monthlyBreakdownGenerator,
            _topProductSorter,
            _timeWindowParser);
    }
```

Also update the second construction call site, inside `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`. Change:

```csharp
        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object,
            _timeWindowParser);
```

to:

```csharp
        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object,
            _topProductSorter,
            _timeWindowParser);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"`

Expected: **build error** — `CS1729: 'GetProductMarginSummaryHandler' does not contain a constructor that takes 5 arguments` (the handler's constructor still only takes 4 parameters).

- [ ] **Step 3: Update the handler**

In `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`, change the field declarations and constructor from:

```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _timeWindowParser = timeWindowParser;
    }
```

to:

```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly ITopProductSorter _topProductSorter;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        ITopProductSorter topProductSorter,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _topProductSorter = topProductSorter;
        _timeWindowParser = timeWindowParser;
    }
```

Change the call site inside `GenerateTopProducts` from:

```csharp
        // Apply sorting
        var sortedProducts = ApplySorting(topProductsWithData, sortBy, sortDescending);
```

to:

```csharp
        // Apply sorting
        var sortedProducts = _topProductSorter.Sort(topProductsWithData, sortBy, sortDescending);
```

Delete the entire `ApplySorting` private method (currently between `GenerateTopProducts` and `CalculateTotalMarginForLevel`):

```csharp
    /// <summary>
    /// Applies sorting to the top products list
    /// </summary>
    private List<TopProductDto> ApplySorting(List<TopProductDto> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by TotalMargin descending
            return sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList();
        }

        return sortBy.ToLower() switch
        {
            "groupkey" or "productcode" => sortDescending
                ? products.OrderByDescending(p => p.GroupKey).ToList()
                : products.OrderBy(p => p.GroupKey).ToList(),
            "displayname" or "productname" => sortDescending
                ? products.OrderByDescending(p => p.DisplayName).ToList()
                : products.OrderBy(p => p.DisplayName).ToList(),
            "totalmargin" => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList(),
            // M0-M2 margin levels - amounts
            "m0amount" => sortDescending
                ? products.OrderByDescending(p => p.M0Amount).ToList()
                : products.OrderBy(p => p.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? products.OrderByDescending(p => p.M1Amount).ToList()
                : products.OrderBy(p => p.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? products.OrderByDescending(p => p.M2Amount).ToList()
                : products.OrderBy(p => p.M2Amount).ToList(),
            // M0-M2 margin levels - percentages
            "m0percentage" => sortDescending
                ? products.OrderByDescending(p => p.M0Percentage).ToList()
                : products.OrderBy(p => p.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(p => p.M1Percentage).ToList()
                : products.OrderBy(p => p.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(p => p.M2Percentage).ToList()
                : products.OrderBy(p => p.M2Percentage).ToList(),
            // Pricing
            "sellingprice" => sortDescending
                ? products.OrderByDescending(p => p.SellingPrice).ToList()
                : products.OrderBy(p => p.SellingPrice).ToList(),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(p => p.PurchasePrice).ToList()
                : products.OrderBy(p => p.PurchasePrice).ToList(),
            _ => sortDescending
                ? products.OrderByDescending(p => p.TotalMargin).ToList()
                : products.OrderBy(p => p.TotalMargin).ToList()
        };
    }

```

The handler class now contains only three methods: `Handle`, `GenerateTopProducts`, and `CalculateTotalMarginForLevel`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"`

Expected: PASS — all tests pass, including `Handle_ValidRequest_ReturnsCorrectResponse` (`TotalMargin` still `3000m`, `TopProducts.Count` still `2`) and `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` (now using the real `TopProductSorter` for sorting).

- [ ] **Step 5: Run the full test suite and build**

Run: `dotnet build`

Expected: Build succeeds with no errors or warnings about unused members.

Run: `dotnet test`

Expected: PASS — full backend test suite green (no regressions introduced by the constructor signature change or the removed methods).

Verify the line-count reduction (FR-3 acceptance criterion — file should be well under 150 lines, down from the original 242):

Run: `wc -l backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`

Expected: roughly 130–140 lines (no `GroupMarginData` class, no `CalculateGroupMarginData` method, no `ApplySorting` method).

- [ ] **Step 6: Run `dotnet format` to match repo formatting conventions**

Run: `dotnet format`

Expected: No unexpected changes beyond whitespace/using-order in the files touched by this plan (per project validation rules in `CLAUDE.md`).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "refactor(analytics): use ITopProductSorter in GetProductMarginSummaryHandler"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (move `CalculateGroupMarginData` into `IMarginCalculator.GetGroupAggregatedMarginData`, move `GroupMarginData` to `Services/`, make it `public`) → covered by `add-group-margin-aggregator` (creates `GroupMarginData.cs`, extends `IMarginCalculator`/`MarginCalculator`, adds direct unit tests) and `wire-group-margin-aggregator-into-handler` (removes the private method + internal class from the handler, updates the call site, fixes the one affected mock).
- FR-2 (extract `ApplySorting` into `ITopProductSorter`/`TopProductSorter`, register in `AnalyticsModule.cs`, new ctor dependency) → covered by `add-top-product-sorter` (new service + DI registration + full-coverage tests for all 13 keys × 2 directions + default + fallback + case-insensitivity) and `wire-top-product-sorter-into-handler` (ctor change, removes `ApplySorting`, updates both test call sites).
- FR-3 (handler reduced to orchestration only, no API/contract changes) → verified in `wire-top-product-sorter-into-handler` Step 5 (line-count check) and Step 4 (full test suite green, confirming `GetProductMarginSummaryRequest`/`Response` untouched — no task in this plan touches those files).
- NFR-1/NFR-2 (no performance or security impact) → naturally satisfied; no task changes algorithmic behavior, only code location — confirmed by verbatim-move task steps plus passing pre-existing assertions (`TotalMargin.Should().Be(3000m)` etc.) that pin the exact numeric output.
- Dependencies section's requirement to confirm no other call sites construct the handler directly → confirmed via `grep -rn "new GetProductMarginSummaryHandler" backend/`, documented in the File Map section; only the two known call sites in `GetProductMarginSummaryHandlerTests.cs` exist.
- Out of Scope items (`CalculateTotalMarginForLevel`, other Analytics handlers, Option A static helper) → correctly left untouched; no task modifies `GetProductMarginAnalysisHandler.cs` or `GetMarginReportHandler.cs`, and `TopProductSorter` is DI-registered (Option B), not static.

**2. Placeholder scan:** No "TBD"/"TODO"/"similar to task N" placeholders — every step shows complete, copy-pasteable code. No gaps found.

**3. Type consistency:** `GroupMarginData` (8 decimal properties) is identical across its creation in `add-group-margin-aggregator` and its use in `wire-group-margin-aggregator-into-handler`. `ITopProductSorter.Sort(List<TopProductDto>, string?, bool) : List<TopProductDto>` signature is identical across its creation in `add-top-product-sorter` and its use in `wire-top-product-sorter-into-handler`. Constructor parameter name `topProductSorter` and field `_topProductSorter` are consistent between the handler and its test fixture. No naming drift detected.
