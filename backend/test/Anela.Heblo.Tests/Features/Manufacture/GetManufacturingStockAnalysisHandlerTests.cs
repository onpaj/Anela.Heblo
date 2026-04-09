using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufacturingStockAnalysisHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ITimePeriodCalculator> _timePeriodCalculatorMock;
    private readonly Mock<IConsumptionRateCalculator> _consumptionCalculatorMock;
    private readonly Mock<IProductionActivityAnalyzer> _productionAnalyzerMock;
    private readonly Mock<IManufactureSeverityCalculator> _severityCalculatorMock;
    private readonly Mock<IManufactureAnalysisMapper> _mapperMock;
    private readonly Mock<IItemFilterService> _filterServiceMock;
    private readonly Mock<ILogger<GetManufacturingStockAnalysisHandler>> _loggerMock;
    private readonly GetManufacturingStockAnalysisHandler _handler;

    public GetManufacturingStockAnalysisHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timePeriodCalculatorMock = new Mock<ITimePeriodCalculator>();
        _consumptionCalculatorMock = new Mock<IConsumptionRateCalculator>();
        _productionAnalyzerMock = new Mock<IProductionActivityAnalyzer>();
        _severityCalculatorMock = new Mock<IManufactureSeverityCalculator>();
        _mapperMock = new Mock<IManufactureAnalysisMapper>();
        _filterServiceMock = new Mock<IItemFilterService>();
        _loggerMock = new Mock<ILogger<GetManufacturingStockAnalysisHandler>>();

        _handler = new GetManufacturingStockAnalysisHandler(
            _catalogRepositoryMock.Object,
            _timePeriodCalculatorMock.Object,
            _consumptionCalculatorMock.Object,
            _productionAnalyzerMock.Object,
            _severityCalculatorMock.Object,
            _mapperMock.Object,
            _filterServiceMock.Object,
            _loggerMock.Object);
    }


    [Fact]
    public async Task Handle_NoFinishedProductsFound_ReturnsManufacturingDataNotAvailableError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest();
        var emptyCatalogItems = new List<CatalogAggregate>();

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-30), DateTime.Today) });

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyCatalogItems);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ManufacturingDataNotAvailable);
        response.Params.Should().ContainKey("reason");
        response.Params!["reason"].Should().Contain("No finished products available for analysis");
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessfulResponse()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.PreviousQuarter,
            PageNumber = 1,
            PageSize = 10
        };

        var catalogItems = CreateTestCatalogItems();
        var analysisItems = CreateTestAnalysisItems();
        var summary = CreateTestSummary();

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-90), DateTime.Today) });

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        // Mock all the services needed for the actual analysis
        _consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<(DateTime, DateTime)>>()))
            .Returns(5.0);

        _consumptionCalculatorMock.Setup(x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), It.IsAny<double>()))
            .Returns(20.0);

        _severityCalculatorMock.Setup(x => x.CalculateOverstockPercentage(It.IsAny<double>(), It.IsAny<int>()))
            .Returns(66.7);

        _severityCalculatorMock.Setup(x => x.CalculateSeverity(It.IsAny<CatalogAggregate>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(ManufacturingStockSeverity.Adequate);

        _productionAnalyzerMock.Setup(x => x.IsInActiveProduction(It.IsAny<IEnumerable<ManufactureHistoryRecord>>(), It.IsAny<int>()))
            .Returns(false);

        _mapperMock.Setup(x => x.MapToDto(It.IsAny<CatalogAggregate>(), It.IsAny<ManufacturingStockSeverity>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .Returns((CatalogAggregate item, ManufacturingStockSeverity severity, double dailyRate, double salesInPeriod, double stockDaysAvail, double overstock, bool inProduction) =>
                new ManufacturingStockItemDto
                {
                    Code = item.ProductCode,
                    Name = item.ProductName,
                    Severity = severity,
                    DailySalesRate = dailyRate,
                    StockDaysAvailable = stockDaysAvail,
                    IsConfigured = true
                });

        _filterServiceMock.Setup(x => x.FilterItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<GetManufacturingStockAnalysisRequest>()))
            .Returns((List<ManufacturingStockItemDto> items, GetManufacturingStockAnalysisRequest req) => items);

        _filterServiceMock.Setup(x => x.SortItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<ManufacturingStockSortBy>(), It.IsAny<bool>()))
            .Returns((List<ManufacturingStockItemDto> items, ManufacturingStockSortBy sortBy, bool desc) => items);

        _filterServiceMock.Setup(x => x.CalculateSummary(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
            .Returns(summary);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Items.Should().NotBeNull();
        response.Summary.Should().NotBeNull();
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(10);
    }

    private List<CatalogAggregate> CreateTestCatalogItems()
    {
        return new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PRD001",
                ProductName = "Product 1",
                Type = ProductType.Product,
                Stock = new StockData { Erp = 100, Eshop = 0, Transport = 0, Reserve = 0 },
                Properties = new CatalogProperties { StockMinSetup = 50, OptimalStockDaysSetup = 30 }
            },
            new CatalogAggregate
            {
                ProductCode = "PRD002",
                ProductName = "Product 2",
                Type = ProductType.Product,
                Stock = new StockData { Erp = 200, Eshop = 0, Transport = 0, Reserve = 0 },
                Properties = new CatalogProperties { StockMinSetup = 100, OptimalStockDaysSetup = 45 }
            }
        };
    }

    private List<ManufacturingStockItemDto> CreateTestAnalysisItems()
    {
        return new List<ManufacturingStockItemDto>
        {
            new ManufacturingStockItemDto
            {
                Code = "PRD001",
                Name = "Product 1",
                CurrentStock = 100,
                DailySalesRate = 5.0,
                StockDaysAvailable = 20,
                OptimalDaysSetup = 30,
                Severity = ManufacturingStockSeverity.Minor,
                IsConfigured = true
            },
            new ManufacturingStockItemDto
            {
                Code = "PRD002",
                Name = "Product 2",
                CurrentStock = 200,
                DailySalesRate = 3.0,
                StockDaysAvailable = 66.7,
                OptimalDaysSetup = 45,
                Severity = ManufacturingStockSeverity.Adequate,
                IsConfigured = true
            }
        };
    }

    [Fact]
    public async Task Handle_ExportTrue_BypassesPaginationAndReturnsAllFilteredItems()
    {
        // Arrange — 25 items, PageSize = 5 → without export only 5 would be returned
        var catalogItems = Enumerable.Range(1, 25)
            .Select(i => new CatalogAggregate
            {
                ProductCode = $"PRD{i:D3}",
                ProductName = $"Product {i}",
                Type = ProductType.Product,
                Stock = new StockData { Erp = i * 10m },
                Properties = new CatalogProperties { OptimalStockDaysSetup = 30 }
            })
            .ToList();

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-30), DateTime.Today) });

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        _consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<(DateTime, DateTime)>>()))
            .Returns(1.0);
        _consumptionCalculatorMock.Setup(x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), It.IsAny<double>()))
            .Returns(20.0);
        _severityCalculatorMock.Setup(x => x.CalculateOverstockPercentage(It.IsAny<double>(), It.IsAny<int>())).Returns(0.0);
        _severityCalculatorMock.Setup(x => x.CalculateSeverity(It.IsAny<CatalogAggregate>(), It.IsAny<double>(), It.IsAny<double>())).Returns(ManufacturingStockSeverity.Adequate);
        _productionAnalyzerMock.Setup(x => x.IsInActiveProduction(It.IsAny<IEnumerable<ManufactureHistoryRecord>>(), It.IsAny<int>())).Returns(false);
        _mapperMock.Setup(x => x.MapToDto(It.IsAny<CatalogAggregate>(), It.IsAny<ManufacturingStockSeverity>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .Returns((CatalogAggregate item, ManufacturingStockSeverity sev, double dr, double sp, double sd, double op, bool ip) =>
                new ManufacturingStockItemDto { Code = item.ProductCode, Name = item.ProductName });
        _filterServiceMock.Setup(x => x.FilterItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<GetManufacturingStockAnalysisRequest>()))
            .Returns((List<ManufacturingStockItemDto> items, GetManufacturingStockAnalysisRequest req) => items);
        _filterServiceMock.Setup(x => x.SortItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<ManufacturingStockSortBy>(), It.IsAny<bool>()))
            .Returns((List<ManufacturingStockItemDto> items, ManufacturingStockSortBy sortBy, bool desc) => items);
        _filterServiceMock.Setup(x => x.CalculateSummary(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
            .Returns(new ManufacturingStockSummaryDto());

        var request = new GetManufacturingStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 5,
            IsExport = true
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Items.Count.Should().Be(25, "IsExport=true should return all items, ignoring PageSize");
        response.TotalCount.Should().Be(25);
    }

    private ManufacturingStockSummaryDto CreateTestSummary()
    {
        return new ManufacturingStockSummaryDto
        {
            TotalProducts = 2,
            CriticalCount = 0,
            MajorCount = 0,
            MinorCount = 1,
            AdequateCount = 1,
            UnconfiguredCount = 0,
            AnalysisPeriodStart = DateTime.Today.AddDays(-90),
            AnalysisPeriodEnd = DateTime.Today,
            ProductFamilies = new List<string>()
        };
    }

    [Fact]
    public async Task Handle_StockCalculation_UsesTotalInsteadOfAvailable()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        // Create a test catalog item with distinct Available and Reserve amounts
        var catalogItems = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "TEST001",
                ProductName = "Test Product",
                Type = ProductType.Product,
                Stock = new StockData
                {
                    Erp = 100m,       // Available = 100
                    Eshop = 50m,
                    Transport = 10m,
                    Reserve = 25m,     // Reserve = 25, so Total = 135
                    PrimaryStockSource = StockSource.Erp
                },
                Properties = new CatalogProperties
                {
                    OptimalStockDaysSetup = 30
                }
            }
        };

        // Set up mocks
        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-30), DateTime.Today) });

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        decimal capturedStockAmount = 0;

        // Capture the stock amount passed to CalculateStockDaysAvailable
        _consumptionCalculatorMock.Setup(x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), It.IsAny<double>()))
            .Callback<decimal, double>((stock, rate) => capturedStockAmount = stock)
            .Returns(20.0);

        _consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<(DateTime, DateTime)>>()))
            .Returns(5.0);

        _severityCalculatorMock.Setup(x => x.CalculateOverstockPercentage(It.IsAny<double>(), It.IsAny<int>()))
            .Returns(0.0);

        _severityCalculatorMock.Setup(x => x.CalculateSeverity(It.IsAny<CatalogAggregate>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(ManufacturingStockSeverity.Adequate);

        _productionAnalyzerMock.Setup(x => x.IsInActiveProduction(It.IsAny<IEnumerable<ManufactureHistoryRecord>>(), It.IsAny<int>()))
            .Returns(false);

        _mapperMock.Setup(x => x.MapToDto(It.IsAny<CatalogAggregate>(), It.IsAny<ManufacturingStockSeverity>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .Returns(new ManufacturingStockItemDto { Code = "TEST001", Reserve = 25 });

        _filterServiceMock.Setup(x => x.FilterItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<GetManufacturingStockAnalysisRequest>()))
            .Returns((List<ManufacturingStockItemDto> items, GetManufacturingStockAnalysisRequest req) => items);

        _filterServiceMock.Setup(x => x.SortItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<ManufacturingStockSortBy>(), It.IsAny<bool>()))
            .Returns((List<ManufacturingStockItemDto> items, ManufacturingStockSortBy sortBy, bool desc) => items);

        _filterServiceMock.Setup(x => x.CalculateSummary(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
            .Returns(new ManufacturingStockSummaryDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert - Verify that Stock.Total (135) was used instead of Stock.Available (110)
        capturedStockAmount.Should().Be(135m, "Handler should use Stock.Total (Available + Reserve) = 110 + 25 = 135");

        // Verify the consumption calculator was called with the total stock
        _consumptionCalculatorMock.Verify(
            x => x.CalculateStockDaysAvailable(135m, 5.0),
            Times.Once,
            "Should call CalculateStockDaysAvailable with total stock including reserve");
    }

    [Fact]
    public async Task Handle_SalesMultiplierDefault_PassesUnmodifiedDailyRateToCalculations()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SalesMultiplier = 1.0
        };

        var catalogItems = CreateTestCatalogItems();

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-30), DateTime.Today) });

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        double capturedDailyRate = 0;

        _consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<(DateTime, DateTime)>>()))
            .Returns(5.0);

        _consumptionCalculatorMock.Setup(x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), It.IsAny<double>()))
            .Callback<decimal, double>((stock, rate) => capturedDailyRate = rate)
            .Returns(20.0);

        _severityCalculatorMock.Setup(x => x.CalculateOverstockPercentage(It.IsAny<double>(), It.IsAny<int>())).Returns(0.0);
        _severityCalculatorMock.Setup(x => x.CalculateSeverity(It.IsAny<CatalogAggregate>(), It.IsAny<double>(), It.IsAny<double>())).Returns(ManufacturingStockSeverity.Adequate);
        _productionAnalyzerMock.Setup(x => x.IsInActiveProduction(It.IsAny<IEnumerable<ManufactureHistoryRecord>>(), It.IsAny<int>())).Returns(false);
        _mapperMock.Setup(x => x.MapToDto(It.IsAny<CatalogAggregate>(), It.IsAny<ManufacturingStockSeverity>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .Returns(new ManufacturingStockItemDto { Code = "PRD001" });
        _filterServiceMock.Setup(x => x.FilterItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<GetManufacturingStockAnalysisRequest>()))
            .Returns((List<ManufacturingStockItemDto> items, GetManufacturingStockAnalysisRequest req) => items);
        _filterServiceMock.Setup(x => x.SortItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<ManufacturingStockSortBy>(), It.IsAny<bool>()))
            .Returns((List<ManufacturingStockItemDto> items, ManufacturingStockSortBy sortBy, bool desc) => items);
        _filterServiceMock.Setup(x => x.CalculateSummary(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
            .Returns(new ManufacturingStockSummaryDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert - multiplier of 1.0 should not change the daily rate (5.0 * 1.0 = 5.0)
        capturedDailyRate.Should().Be(5.0, "SalesMultiplier=1.0 should not alter the daily sales rate");
    }

    [Fact]
    public async Task Handle_SalesMultiplierTwo_DoublesDailyRateAndSalesInPeriod()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            PageNumber = 1,
            PageSize = 10,
            SalesMultiplier = 2.0
        };

        var catalogItems = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PRD001",
                ProductName = "Product 1",
                Type = ProductType.Product,
                Stock = new StockData { Erp = 100m },
                Properties = new CatalogProperties { OptimalStockDaysSetup = 30 }
            }
        };

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriodRanges(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(new List<(DateTime, DateTime)> { (DateTime.Today.AddDays(-30), DateTime.Today) });

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        double capturedDailyRate = 0;

        _consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<IEnumerable<CatalogSaleRecord>>(), It.IsAny<IReadOnlyList<(DateTime, DateTime)>>()))
            .Returns(5.0);

        _consumptionCalculatorMock.Setup(x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), It.IsAny<double>()))
            .Callback<decimal, double>((stock, rate) => capturedDailyRate = rate)
            .Returns(10.0);

        _severityCalculatorMock.Setup(x => x.CalculateOverstockPercentage(It.IsAny<double>(), It.IsAny<int>())).Returns(0.0);
        _severityCalculatorMock.Setup(x => x.CalculateSeverity(It.IsAny<CatalogAggregate>(), It.IsAny<double>(), It.IsAny<double>())).Returns(ManufacturingStockSeverity.Critical);
        _productionAnalyzerMock.Setup(x => x.IsInActiveProduction(It.IsAny<IEnumerable<ManufactureHistoryRecord>>(), It.IsAny<int>())).Returns(false);
        _mapperMock.Setup(x => x.MapToDto(It.IsAny<CatalogAggregate>(), It.IsAny<ManufacturingStockSeverity>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .Returns(new ManufacturingStockItemDto { Code = "PRD001" });
        _filterServiceMock.Setup(x => x.FilterItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<GetManufacturingStockAnalysisRequest>()))
            .Returns((List<ManufacturingStockItemDto> items, GetManufacturingStockAnalysisRequest req) => items);
        _filterServiceMock.Setup(x => x.SortItems(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<ManufacturingStockSortBy>(), It.IsAny<bool>()))
            .Returns((List<ManufacturingStockItemDto> items, ManufacturingStockSortBy sortBy, bool desc) => items);
        _filterServiceMock.Setup(x => x.CalculateSummary(It.IsAny<List<ManufacturingStockItemDto>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
            .Returns(new ManufacturingStockSummaryDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert - multiplier of 2.0 should double the rate from 5.0 to 10.0
        capturedDailyRate.Should().Be(10.0, "SalesMultiplier=2.0 should double the daily sales rate (5.0 * 2.0 = 10.0)");

        // Verify CalculateStockDaysAvailable was called with the doubled rate
        _consumptionCalculatorMock.Verify(
            x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), 10.0),
            Times.Once,
            "Should call CalculateStockDaysAvailable with doubled rate");
    }
}