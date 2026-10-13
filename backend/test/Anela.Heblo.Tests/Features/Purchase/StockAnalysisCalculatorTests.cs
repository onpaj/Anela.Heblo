using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Xcc;
using Xunit;
using FluentAssertions;
using Moq;

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
    public void CalculateStockEfficiency_WhenOptimalStockPositive_ReturnsAvailableOverOptimal()
    {
        // Arrange
        var availableStock = 100.0;
        var minStock = 50.0;
        var optimalStock = 200.0;

        // Act
        var result = _calculator.CalculateStockEfficiency(availableStock, minStock, optimalStock);

        // Assert
        result.Should().Be(50.0);
    }

    [Fact]
    public void CalculateStockEfficiency_WhenOptimalStockNotPositiveAndMinStockPositive_ReturnsAvailableOverMin()
    {
        // Arrange
        var availableStock = 25.0;
        var minStock = 50.0;
        var optimalStock = 0.0;

        // Act
        var result = _calculator.CalculateStockEfficiency(availableStock, minStock, optimalStock);

        // Assert
        result.Should().Be(50.0);
    }

    [Fact]
    public void CalculateStockEfficiency_WhenOptimalAndMinStockNotPositive_ReturnsZero()
    {
        // Arrange
        var availableStock = 25.0;
        var minStock = 0.0;
        var optimalStock = 0.0;

        // Act
        var result = _calculator.CalculateStockEfficiency(availableStock, minStock, optimalStock);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenOptimalAndMinStockNotPositive_ReturnsNull()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 0.0;
        var minStock = 0.0;
        var moq = string.Empty;

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenAvailableStockAtOrAboveOptimal_ReturnsNull()
    {
        // Arrange
        var availableStock = 200.0;
        var optimalStock = 150.0;
        var minStock = 50.0;
        var moq = string.Empty;

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenOptimalStockNotPositive_UsesDoubleMinStockAsTarget()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 0.0;
        var minStock = 30.0; // target = 60
        var moq = string.Empty;

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().Be(50.0); // 60 - 10
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenMoqPresentAndGreaterThanShortfall_ReturnsMoq()
    {
        // Arrange
        var availableStock = 90.0;
        var optimalStock = 100.0; // needed = 10
        var minStock = 50.0;
        var moq = "40";

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().Be(40.0);
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenMoqPresentAndLessThanShortfall_ReturnsShortfall()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 100.0; // needed = 90
        var minStock = 50.0;
        var moq = "20";

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().Be(90.0);
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenMoqNullOrEmptyOrUnparseable_ReturnsRawShortfall()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 100.0; // needed = 90
        var minStock = 50.0;

        // Act
        var resultWithNull = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, null!);
        var resultWithEmpty = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, string.Empty);
        var resultWithUnparseable = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, "not-a-number");

        // Assert
        resultWithNull.Should().Be(90.0);
        resultWithEmpty.Should().Be(90.0);
        resultWithUnparseable.Should().Be(90.0);
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
            UnitPrice = 12.5m,
            TotalPrice = 625m,
        };
        var snapshot = MakeSnapshot(lastPurchase: lastPurchase);

        var result = _calculator.AnalyzeItem(snapshot, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        result.LastPurchase.Should().NotBeNull();
        result.LastPurchase!.Date.Should().Be(lastPurchase.Date);
        result.LastPurchase.SupplierName.Should().Be("Acme");
        result.LastPurchase.Amount.Should().Be(50d);
        result.LastPurchase.UnitPrice.Should().Be(12.5m);
        result.LastPurchase.TotalPrice.Should().Be(625m);
    }

    private static StockAnalysisItemDto MakeItem(StockSeverity severity, bool isConfigured = true) =>
        new()
        {
            ProductCode = "MAT001",
            ProductName = "Test",
            ProductNameNormalized = "test",
            ProductType = "Material",
            Severity = severity,
            IsConfigured = isConfigured,
        };

    [Theory]
    [InlineData(StockStatusFilter.Critical, StockSeverity.Critical, true)]
    [InlineData(StockStatusFilter.Critical, StockSeverity.Low, false)]
    [InlineData(StockStatusFilter.Low, StockSeverity.Low, true)]
    [InlineData(StockStatusFilter.Optimal, StockSeverity.Optimal, true)]
    [InlineData(StockStatusFilter.Overstocked, StockSeverity.Overstocked, true)]
    [InlineData(StockStatusFilter.NotConfigured, StockSeverity.NotConfigured, true)]
    [InlineData(StockStatusFilter.All, StockSeverity.Critical, true)]
    public void FilterItems_StatusFilter_IncludesOnlyMatchingSeverity(StockStatusFilter filter, StockSeverity severity, bool expectedIncluded)
    {
        var items = new List<StockAnalysisItemDto> { MakeItem(severity) };
        var request = new GetPurchaseStockAnalysisRequest { StockStatus = filter, PageNumber = 1, PageSize = 10 };

        var result = _calculator.FilterItems(items, request);

        result.Should().HaveCount(expectedIncluded ? 1 : 0);
    }

    [Fact]
    public void FilterItems_OnlyConfiguredTrue_ExcludesUnconfiguredItems()
    {
        var items = new List<StockAnalysisItemDto> { MakeItem(StockSeverity.Optimal, isConfigured: false) };
        var request = new GetPurchaseStockAnalysisRequest { OnlyConfigured = true, StockStatus = StockStatusFilter.All, PageNumber = 1, PageSize = 10 };

        var result = _calculator.FilterItems(items, request);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterItems_OnlyConfiguredTrue_KeepsConfiguredItemsMatchingStatus()
    {
        var items = new List<StockAnalysisItemDto> { MakeItem(StockSeverity.Critical, isConfigured: true) };
        var request = new GetPurchaseStockAnalysisRequest { OnlyConfigured = true, StockStatus = StockStatusFilter.Critical, PageNumber = 1, PageSize = 10 };

        var result = _calculator.FilterItems(items, request);

        result.Should().HaveCount(1);
    }

    private static StockAnalysisItemDto MakeSortableItem(string code, string name, double available, double consumption, double efficiency, DateTime? lastPurchaseDate) =>
        new()
        {
            ProductCode = code,
            ProductName = name,
            ProductNameNormalized = name.NormalizeForSearch(),
            ProductType = "Material",
            AvailableStock = available,
            ConsumptionInPeriod = consumption,
            StockEfficiencyPercentage = efficiency,
            LastPurchase = lastPurchaseDate.HasValue
                ? new LastPurchaseInfoDto { Date = lastPurchaseDate.Value, SupplierName = "Acme", Amount = 1, UnitPrice = 1, TotalPrice = 1 }
                : null,
        };

    [Fact]
    public void SortItems_ByProductCode_Ascending()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("B", "b", 0, 0, 0, null),
            MakeSortableItem("A", "a", 0, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.ProductCode, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("A", "B");
    }

    [Fact]
    public void SortItems_ByProductCode_Descending_ReversesOrder()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", 0, 0, 0, null),
            MakeSortableItem("B", "b", 0, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.ProductCode, descending: true);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    [Fact]
    public void SortItems_ByAvailableStock()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", available: 50, 0, 0, null),
            MakeSortableItem("B", "b", available: 10, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.AvailableStock, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    [Fact]
    public void SortItems_ByLastPurchaseDate_NullTreatedAsMinValue()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", 0, 0, 0, new DateTime(2024, 6, 1)),
            MakeSortableItem("B", "b", 0, 0, 0, null),
        };

        var result = _calculator.SortItems(items, StockAnalysisSortBy.LastPurchaseDate, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    [Fact]
    public void SortItems_DefaultFallback_SortsByStockEfficiency()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSortableItem("A", "a", 0, 0, efficiency: 80, null),
            MakeSortableItem("B", "b", 0, 0, efficiency: 20, null),
        };

        var result = _calculator.SortItems(items, (StockAnalysisSortBy)999, descending: false);

        result.Select(i => i.ProductCode).Should().ContainInOrder("B", "A");
    }

    private static StockAnalysisItemDto MakeSummaryItem(StockSeverity severity, double effectiveStock, double? unitPrice) =>
        new()
        {
            ProductCode = "MAT",
            ProductName = "Test",
            ProductNameNormalized = "test",
            ProductType = "Material",
            Severity = severity,
            EffectiveStock = effectiveStock,
            LastPurchase = unitPrice.HasValue
                ? new LastPurchaseInfoDto { Date = DateTime.UtcNow, SupplierName = "Acme", Amount = 1, UnitPrice = (decimal)unitPrice.Value, TotalPrice = 1 }
                : null,
        };

    [Fact]
    public void CalculateSummary_CountsEachSeverityBucket()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSummaryItem(StockSeverity.Critical, 1, 1),
            MakeSummaryItem(StockSeverity.Critical, 1, 1),
            MakeSummaryItem(StockSeverity.Low, 1, 1),
            MakeSummaryItem(StockSeverity.Optimal, 1, 1),
            MakeSummaryItem(StockSeverity.Overstocked, 1, 1),
            MakeSummaryItem(StockSeverity.NotConfigured, 1, 1),
        };
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 1, 31);

        var summary = _calculator.CalculateSummary(items, from, to);

        summary.TotalProducts.Should().Be(6);
        summary.CriticalCount.Should().Be(2);
        summary.LowStockCount.Should().Be(1);
        summary.OptimalCount.Should().Be(1);
        summary.OverstockedCount.Should().Be(1);
        summary.NotConfiguredCount.Should().Be(1);
        summary.AnalysisPeriodStart.Should().Be(from);
        summary.AnalysisPeriodEnd.Should().Be(to);
    }

    [Fact]
    public void CalculateSummary_TotalInventoryValue_MissingLastPurchaseTreatedAsZeroUnitPrice()
    {
        var items = new List<StockAnalysisItemDto>
        {
            MakeSummaryItem(StockSeverity.Optimal, effectiveStock: 10, unitPrice: 5),   // 10 * 5 = 50
            MakeSummaryItem(StockSeverity.Optimal, effectiveStock: 10, unitPrice: null), // 10 * 0 = 0
        };

        var summary = _calculator.CalculateSummary(items, DateTime.UtcNow, DateTime.UtcNow);

        summary.TotalInventoryValue.Should().Be(50m);
    }
}
