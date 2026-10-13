### task: add-severitycalculator-dependency-and-analyzeitem

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`

- [ ] **Step 1: Write the failing test for `AnalyzeItem`**

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class StockAnalysisCalculatorTests
{
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly StockAnalysisCalculator _calculator;

    public StockAnalysisCalculatorTests()
    {
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _calculator = new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object);
    }

    private static MaterialStockSnapshot MakeSnapshot(
        string productCode = "MAT001",
        string productName = "Test Material",
        decimal available = 100m,
        decimal ordered = 0m,
        decimal stockMinSetup = 10m,
        int optimalStockDaysSetup = 30,
        string minimalOrderQuantity = "",
        double consumptionInPeriod = 60,
        MaterialPurchaseSnapshot? lastPurchase = null)
    {
        var effective = available + ordered;
        return new MaterialStockSnapshot
        {
            ProductCode = productCode,
            ProductName = productName,
            ProductNameNormalized = productName.NormalizeForSearch(),
            ProductType = MaterialProductType.Material,
            SupplierName = "Acme",
            MinimalOrderQuantity = minimalOrderQuantity,
            IsMinStockConfigured = stockMinSetup > 0,
            IsOptimalStockConfigured = optimalStockDaysSetup > 0,
            Stock = new MaterialStockLevels
            {
                Available = available,
                Ordered = ordered,
                EffectiveStock = effective,
            },
            StockMinSetup = stockMinSetup,
            OptimalStockDaysSetup = optimalStockDaysSetup,
            ConsumptionInPeriod = consumptionInPeriod,
            LastPurchase = lastPurchase,
        };
    }

    [Fact]
    public void AnalyzeItem_ComputesDailyConsumptionAndDaysUntilStockout()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var snapshot = MakeSnapshot(available: 100m, ordered: 0m, consumptionInPeriod: 60);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 1, 31); // 30-day period

        var result = _calculator.AnalyzeItem(snapshot, fromDate, toDate);

        result.DailyConsumption.Should().Be(60d / 30d);
        result.DaysUntilStockout.Should().Be((int)(100d / (60d / 30d)));
        result.ProductCode.Should().Be("MAT001");
        result.Severity.Should().Be(StockSeverity.Optimal);
    }

    [Fact]
    public void AnalyzeItem_ZeroConsumption_DaysUntilStockoutIsNull()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var snapshot = MakeSnapshot(consumptionInPeriod: 0);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.DaysUntilStockout.Should().BeNull();
    }

    [Fact]
    public void AnalyzeItem_NoLastPurchase_MapsNullLastPurchase()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.NotConfigured);

        var snapshot = MakeSnapshot(lastPurchase: null);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.LastPurchase.Should().BeNull();
    }

    [Fact]
    public void AnalyzeItem_WithLastPurchase_MapsAllFields()
    {
        _stockSeverityCalculatorMock
            .Setup(x => x.DetermineStockSeverity(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var lastPurchase = new MaterialPurchaseSnapshot
        {
            Date = new DateTime(2024, 1, 15),
            SupplierName = "Acme",
            Amount = 50m,
            UnitPrice = 12.5,
            TotalPrice = 625,
        };
        var snapshot = MakeSnapshot(lastPurchase: lastPurchase);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.LastPurchase.Should().NotBeNull();
        result.LastPurchase!.Date.Should().Be(lastPurchase.Date);
        result.LastPurchase.SupplierName.Should().Be("Acme");
        result.LastPurchase.Amount.Should().Be(50d);
        result.LastPurchase.UnitPrice.Should().Be(12.5);
        result.LastPurchase.TotalPrice.Should().Be(625);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (methods/constructor don't exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockAnalysisCalculatorTests"`
Expected: build error — `StockAnalysisCalculator` has no constructor taking `IStockSeverityCalculator` and no `AnalyzeItem` method.

- [ ] **Step 3: Add `IStockSeverityCalculator` constructor dependency and `AnalyzeItem` to the interface**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs` — add after `CalculateRecommendedOrderQuantity`:

```csharp
    /// <summary>
    /// Analyzes a single material stock snapshot, computing consumption, stockout, efficiency,
    /// severity, and recommended order quantity for the given period.
    /// </summary>
    /// <param name="item">The material stock snapshot to analyze</param>
    /// <param name="fromDate">Start of the analysis period</param>
    /// <param name="toDate">End of the analysis period</param>
    /// <returns>The fully-computed analysis item</returns>
    StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate);
```

Add the required `using` at the top of the file:
```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
```

- [ ] **Step 4: Implement `AnalyzeItem` in `StockAnalysisCalculator`, moved verbatim from the handler**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs` — add the constructor and the two methods (`AnalyzeItem`, private `GetLastPurchaseInfo`), moved verbatim from `GetPurchaseStockAnalysisHandler.cs` lines 94–164 (only the receiver of `_stockSeverityCalculatorMock`/`_stockAnalysisCalculator` calls changes from cross-class calls to local calls):

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Service responsible for calculating stock analysis metrics such as efficiency and recommended order quantity.
/// </summary>
public class StockAnalysisCalculator : IStockAnalysisCalculator
{
    private readonly IStockSeverityCalculator _stockSeverityCalculator;

    public StockAnalysisCalculator(IStockSeverityCalculator stockSeverityCalculator)
    {
        _stockSeverityCalculator = stockSeverityCalculator;
    }

    // ... existing CalculateStockEfficiency, CalculateRecommendedOrderQuantity unchanged ...

    public StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        var consumption = item.ConsumptionInPeriod;
        var dailyConsumption = consumption / (double)daysDiff;

        int? daysUntilStockout = null;
        if (dailyConsumption > 0)
        {
            daysUntilStockout = (int)((double)item.Stock.EffectiveStock / dailyConsumption);
        }

        var minStock = item.StockMinSetup;
        var optimalStockDays = item.OptimalStockDaysSetup;
        var optimalStock = optimalStockDays > 0 ? dailyConsumption * (double)optimalStockDays : 0;

        var stockEfficiency = CalculateStockEfficiency((double)item.Stock.EffectiveStock, (double)minStock, optimalStock);
        var severity = _stockSeverityCalculator.DetermineStockSeverity((double)item.Stock.EffectiveStock, (double)minStock, optimalStock, item.IsMinStockConfigured, item.IsOptimalStockConfigured);

        var lastPurchase = GetLastPurchaseInfo(item);

        var recommendedQuantity = CalculateRecommendedOrderQuantity(
            (double)item.Stock.Available,
            optimalStock,
            (double)minStock,
            item.MinimalOrderQuantity);

        return new StockAnalysisItemDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = item.ProductType.ToString(),
            AvailableStock = (double)item.Stock.Available,
            OrderedStock = (double)item.Stock.Ordered,
            EffectiveStock = (double)item.Stock.EffectiveStock,
            MinStockLevel = (double)minStock,
            OptimalStockLevel = optimalStock,
            ConsumptionInPeriod = consumption,
            DailyConsumption = dailyConsumption,
            DaysUntilStockout = daysUntilStockout,
            StockEfficiencyPercentage = stockEfficiency,
            Severity = severity,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            LastPurchase = lastPurchase,
            Supplier = item.SupplierName,
            RecommendedOrderQuantity = recommendedQuantity,
            IsConfigured = item.IsMinStockConfigured || item.IsOptimalStockConfigured
        };
    }

    private LastPurchaseInfoDto? GetLastPurchaseInfo(MaterialStockSnapshot item)
    {
        var lastPurchase = item.LastPurchase;

        if (lastPurchase == null)
        {
            return null;
        }

        return new LastPurchaseInfoDto
        {
            Date = lastPurchase.Date,
            SupplierName = lastPurchase.SupplierName,
            Amount = (double)lastPurchase.Amount,
            UnitPrice = lastPurchase.UnitPrice,
            TotalPrice = lastPurchase.TotalPrice
        };
    }
}
```

Note: `CalculateStockEfficiency`/`CalculateRecommendedOrderQuantity` calls inside `AnalyzeItem` are now unqualified local calls (`this.` implicit) instead of `_stockAnalysisCalculator.CalculateStockEfficiency(...)` — this is the only mechanical change from the original handler code besides the receiver of `DetermineStockSeverity`.

- [ ] **Step 5: Remove `AnalyzeStockItem` and `GetLastPurchaseInfo` from the handler; update handler constructor**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`:
- Delete the private `AnalyzeStockItem` method (lines 94–145) and the private `GetLastPurchaseInfo` method (lines 147–164).
- Remove the `IStockSeverityCalculator _stockSeverityCalculator` field and its constructor parameter.
- Change the constructor to:
```csharp
    public GetPurchaseStockAnalysisHandler(
        IMaterialCatalogService materialCatalog,
        IStockAnalysisCalculator stockAnalysisCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger,
        TimeProvider timeProvider)
    {
        _materialCatalog = materialCatalog;
        _stockAnalysisCalculator = stockAnalysisCalculator;
        _logger = logger;
        _timeProvider = timeProvider;
    }
```
- In `Handle()`, change:
```csharp
            .Select(s => AnalyzeStockItem(s, fromDate, toDate))
```
to:
```csharp
            .Select(s => _stockAnalysisCalculator.AnalyzeItem(s, fromDate, toDate))
```
(leave every other line of `Handle()` as-is for now — `ShouldIncludeItem`, `SortItems`, `CalculateSummary` are still private handler methods at this point and are handled in Tasks 2–4).

- [ ] **Step 6: Fix the two existing handler test fixtures to match the new constructors**

Edit `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs:30`, change:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, new StockAnalysisCalculator(), _loggerMock.Object, _timeProviderMock.Object);
```
to:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object), _loggerMock.Object, _timeProviderMock.Object);
```

Edit `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs:30-34`, change:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(
            _materialCatalogMock.Object,
            _stockSeverityCalculatorMock.Object,
            new StockAnalysisCalculator(),
            _loggerMock.Object,
            _timeProviderMock.Object);
```
to:
```csharp
        _handler = new GetPurchaseStockAnalysisHandler(
            _materialCatalogMock.Object,
            new StockAnalysisCalculator(_stockSeverityCalculatorMock.Object),
            _loggerMock.Object,
            _timeProviderMock.Object);
```

- [ ] **Step 7: Run tests to verify everything passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"`
Expected: PASS — all `StockAnalysisCalculatorTests`, `GetPurchaseStockAnalysisHandlerTests`, `GetPurchaseStockAnalysisHandlerDiacriticsTests` green.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs
git commit -m "refactor(purchase): move AnalyzeStockItem into StockAnalysisCalculator"
```

---
