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
    public async Task Handle_CustomPeriodWithoutDates_ReturnsInvalidAnalysisParametersError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = null,
            CustomToDate = null
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidAnalysisParameters);
        response.Params.Should().ContainKey("parameters");
        response.Params!["parameters"].Should().Contain("CustomFromDate and CustomToDate are required");
    }

    [Fact]
    public async Task Handle_CustomPeriodWithInvalidDateRange_ReturnsInvalidAnalysisParametersError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = DateTime.Today,
            CustomToDate = DateTime.Today.AddDays(-1) // After from date
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidAnalysisParameters);
        response.Params.Should().ContainKey("parameters");
        response.Params!["parameters"].Should().Contain("FromDate must be before ToDate");
    }

    [Fact]
    public async Task Handle_InvalidPageSize_ReturnsInvalidAnalysisParametersError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            PageSize = 150 // Greater than 100
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidAnalysisParameters);
        response.Params.Should().ContainKey("parameters");
        response.Params!["parameters"].Should().Contain("PageSize must be between 1 and 100");
    }

    [Fact]
    public async Task Handle_InvalidPageNumber_ReturnsInvalidAnalysisParametersError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            PageNumber = 0
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidAnalysisParameters);
        response.Params.Should().ContainKey("parameters");
        response.Params!["parameters"].Should().Contain("PageNumber must be greater than 0");
    }

    [Fact]
    public async Task Handle_NoFinishedProductsFound_ReturnsManufacturingDataNotAvailableError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest();
        var emptyCatalogItems = new List<CatalogAggregate>();

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriod(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns((DateTime.Today.AddDays(-30), DateTime.Today));

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

        _timePeriodCalculatorMock.Setup(x => x.CalculateTimePeriod(It.IsAny<TimePeriodFilter>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns((DateTime.Today.AddDays(-90), DateTime.Today));

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        // Mock all the services needed for the actual analysis
        _consumptionCalculatorMock.Setup(x => x.CalculateDailySalesRate(It.IsAny<List<CatalogSaleRecord>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns(5.0);

        _consumptionCalculatorMock.Setup(x => x.CalculateStockDaysAvailable(It.IsAny<decimal>(), It.IsAny<double>()))
            .Returns(20.0);

        _severityCalculatorMock.Setup(x => x.CalculateOverstockPercentage(It.IsAny<double>(), It.IsAny<int>()))
            .Returns(66.7);

        _severityCalculatorMock.Setup(x => x.CalculateSeverity(It.IsAny<CatalogAggregate>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(ManufacturingStockSeverity.Adequate);

        _productionAnalyzerMock.Setup(x => x.IsInActiveProduction(It.IsAny<List<ManufactureHistoryRecord>>()))
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
}