using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class GetPurchaseStockAnalysisHandlerTests
{
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, _loggerMock.Object);
    }

    private static MaterialStockSnapshot MakeSnapshot(
        string productCode,
        string productName,
        MaterialProductType type,
        decimal available = 0m,
        decimal ordered = 0m,
        decimal stockMinSetup = 0m,
        int optimalStockDaysSetup = 0,
        string? supplierName = null,
        string minimalOrderQuantity = "",
        double consumptionInPeriod = 0,
        MaterialPurchaseSnapshot? lastPurchase = null)
    {
        var effective = available + ordered;
        return new MaterialStockSnapshot
        {
            ProductCode = productCode,
            ProductName = productName,
            ProductNameNormalized = productName.NormalizeForSearch(),
            ProductType = type,
            SupplierName = supplierName,
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
    public async Task Handle_ValidRequest_ReturnsAnalysisResponse()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = DateTime.UtcNow.AddMonths(-6),
            ToDate = DateTime.UtcNow,
            StockStatus = StockStatusFilter.All,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().NotBeEmpty();
        response.TotalCount.Should().Be(2);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(10);
        response.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_FilterByCriticalStatus_ReturnsOnlyCriticalItems()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            StockStatus = StockStatusFilter.Critical,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        Assert.All(response.Items, item => Assert.Equal(StockSeverity.Critical, item.Severity));
    }

    [Fact]
    public async Task Handle_OnlyConfiguredFilter_ReturnsOnlyConfiguredItems()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            OnlyConfigured = true,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        Assert.All(response.Items, item => Assert.True(item.IsConfigured));
    }

    [Fact]
    public async Task Handle_SearchTerm_FiltersItemsBySearchTerm()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SearchTerm = "MAT001",
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);
        response.Items[0].ProductCode.Should().Be("MAT001");
    }

    [Fact]
    public async Task Handle_InvalidDateRange_ReturnsError()
    {
        var request = new GetPurchaseStockAnalysisRequest
        {
            FromDate = DateTime.UtcNow,
            ToDate = DateTime.UtcNow.AddDays(-1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        result.Params.Should().ContainKey("FromDate");
        result.Params.Should().ContainKey("ToDate");
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        var snapshots = CreateManyTestSnapshots(25);
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 2,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Count.Should().Be(10);
        response.TotalCount.Should().Be(25);
        response.PageNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_SortByStockEfficiency_ReturnsSortedItems()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            SortBy = StockAnalysisSortBy.StockEfficiency,
            SortDescending = true,
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        var efficiencies = response.Items.Select(i => i.StockEfficiencyPercentage).ToList();
        efficiencies.Should().BeEquivalentTo(efficiencies.OrderByDescending(e => e));
    }

    [Fact]
    public async Task Handle_WithOrderedStock_PopulatesEffectiveStockCorrectly()
    {
        var snapshots = new List<MaterialStockSnapshot>
        {
            MakeSnapshot(
                "MAT001",
                "Material with Ordered Stock",
                MaterialProductType.Material,
                available: 50m,
                ordered: 100m,
                stockMinSetup: 20m,
                optimalStockDaysSetup: 30,
                supplierName: "Supplier A",
                minimalOrderQuantity: "100")
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);

        var item = response.Items[0];
        item.AvailableStock.Should().Be(50);
        item.OrderedStock.Should().Be(100);
        item.EffectiveStock.Should().Be(150);
    }

    [Fact]
    public async Task Handle_WithoutOrderedStock_PopulatesEffectiveStockAsAvailable()
    {
        var snapshots = new List<MaterialStockSnapshot>
        {
            MakeSnapshot(
                "MAT002",
                "Material without Ordered Stock",
                MaterialProductType.Material,
                available: 75m,
                ordered: 0m,
                stockMinSetup: 20m,
                optimalStockDaysSetup: 30,
                supplierName: "Supplier B",
                minimalOrderQuantity: "50")
        };

        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Should().HaveCount(1);

        var item = response.Items[0];
        item.AvailableStock.Should().Be(75);
        item.OrderedStock.Should().Be(0);
        item.EffectiveStock.Should().Be(75);
    }

    [Fact]
    public async Task Handle_ExportTrue_BypassesPaginationAndReturnsAllFilteredItems()
    {
        var snapshots = CreateManyTestSnapshots(25);
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new GetPurchaseStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10,
            IsExport = true
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Items.Count.Should().Be(25, "IsExport=true should return all items, ignoring PageSize");
        response.TotalCount.Should().Be(25);
    }

    private List<MaterialStockSnapshot> CreateTestSnapshots()
    {
        return new List<MaterialStockSnapshot>
        {
            MakeSnapshot(
                "MAT001",
                "Material 1",
                MaterialProductType.Material,
                available: 10m,
                stockMinSetup: 50m,
                optimalStockDaysSetup: 30,
                supplierName: "Supplier A",
                minimalOrderQuantity: "100",
                lastPurchase: new MaterialPurchaseSnapshot
                {
                    Date = DateTime.UtcNow.AddDays(-30),
                    SupplierName = "Supplier A",
                    Amount = 100m,
                    UnitPrice = 10m,
                    TotalPrice = 1000m,
                }),
            MakeSnapshot(
                "GOD001",
                "Goods 1",
                MaterialProductType.Goods,
                available: 100m,
                stockMinSetup: 20m,
                optimalStockDaysSetup: 14,
                supplierName: "Supplier B",
                minimalOrderQuantity: "50",
                consumptionInPeriod: 15)
        };
    }

    private List<MaterialStockSnapshot> CreateManyTestSnapshots(int count)
    {
        var items = new List<MaterialStockSnapshot>();

        for (int i = 0; i < count; i++)
        {
            items.Add(MakeSnapshot(
                productCode: $"MAT{i:D3}",
                productName: $"Material {i}",
                type: i % 2 == 0 ? MaterialProductType.Material : MaterialProductType.Goods,
                available: i * 10m,
                stockMinSetup: i * 5m,
                optimalStockDaysSetup: i % 3 == 0 ? 0 : 30,
                supplierName: $"Supplier {i}",
                minimalOrderQuantity: (i * 10).ToString()));
        }

        return items;
    }
}