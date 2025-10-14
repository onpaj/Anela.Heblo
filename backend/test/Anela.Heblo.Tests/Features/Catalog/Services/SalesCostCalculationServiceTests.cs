using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

/// <summary>
/// Unit tests for SalesCostCalculationService with comprehensive cost calculation scenarios
/// </summary>
public class SalesCostCalculationServiceTests
{
    private readonly Mock<ILedgerService> _ledgerServiceMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IOptions<DataSourceOptions>> _dataSourceOptionsMock;
    private readonly Mock<ILogger<SalesCostCalculationService>> _loggerMock;
    private readonly SalesCostCalculationService _service;
    private readonly DateTime _fixedCurrentTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public SalesCostCalculationServiceTests()
    {
        _ledgerServiceMock = new Mock<ILedgerService>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _dataSourceOptionsMock = new Mock<IOptions<DataSourceOptions>>();
        _loggerMock = new Mock<ILogger<SalesCostCalculationService>>();

        // Setup default options
        var dataSourceOptions = new DataSourceOptions
        {
            ManufactureCostHistoryDays = 400
        };
        _dataSourceOptionsMock.Setup(x => x.Value).Returns(dataSourceOptions);

        // Setup fixed time provider
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedCurrentTime);

        // Setup default catalog repository mock
        var defaultProducts = CreateTestProducts();
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultProducts);

        _service = new SalesCostCalculationService(
            _catalogRepositoryMock.Object,
            _ledgerServiceMock.Object,
            _timeProviderMock.Object,
            _dataSourceOptionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetCostsAsync_WithValidData_ReturnsCorrectCostAllocation()
    {
        // Arrange
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.GetCostsAsync(null, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2); // Two products

        // Check ProductA costs (sold 100 units total in Jan 2024)
        result.Should().ContainKey("PROD001");
        var productACosts = result["PROD001"];
        productACosts.Should().HaveCount(1);
        productACosts[0].Month.Should().Be(new DateTime(2024, 1, 1));
        productACosts[0].Cost.Should().Be(5m); // Cost per unit: (1000 total cost / 200 total units) = 5

        // Check ProductB costs (sold 100 units total in Jan 2024)
        result.Should().ContainKey("PROD002");
        var productBCosts = result["PROD002"];
        productBCosts.Should().HaveCount(1);
        productBCosts[0].Month.Should().Be(new DateTime(2024, 1, 1));
        productBCosts[0].Cost.Should().Be(5m); // Cost per unit: (1000 total cost / 200 total units) = 5
    }

    [Fact]
    public async Task GetCostsAsync_WithEmptyProducts_ReturnsEmptyResult()
    {
        // Arrange
        var emptyProducts = new List<CatalogAggregate>();
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyProducts);

        // Act
        var result = await _service.GetCostsAsync(null, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCostsAsync_WithNoSalesHistory_ReturnsEmptyResult()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>())
        };
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await _service.GetCostsAsync(null, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCostsAsync_WithNoCosts_ReturnsEmptyResult()
    {
        // Arrange
        SetupLedgerServiceMocks(new List<CostStatistics>());

        // Act
        var result = await _service.GetCostsAsync(null, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCostsAsync_WithSpecificProductCodes_ReturnsFilteredResults()
    {
        // Arrange
        var productCodes = new List<string> { "PROD001" };
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.GetCostsAsync(productCodes, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        result.Should().NotContainKey("PROD002");
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCostsAsync_WithDateRange_FiltersCorrectly()
    {
        // Arrange
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act - Request only January 2024
        var result = await _service.GetCostsAsync(null, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        result.Should().ContainKey("PROD002");

        // Both products should have January data
        var prod001Costs = result["PROD001"];
        prod001Costs.Should().HaveCount(1);
        prod001Costs[0].Month.Should().Be(new DateTime(2024, 1, 1));
    }

    [Fact]
    public void IsLoaded_InitialState_ReturnsFalse()
    {
        // Assert
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task Reload_WithValidData_SetsIsLoadedTrue()
    {
        // Arrange
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        await _service.Reload();

        // Assert
        _service.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task Reload_WithNoData_DoesNotSetIsLoaded()
    {
        // Arrange
        SetupLedgerServiceMocks(new List<CostStatistics>());

        // Act
        await _service.Reload();

        // Assert
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task Reload_WithException_DoesNotUpdateCacheAndThrows()
    {
        // Arrange
        var exception = new Exception("Reload error");
        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await _service.Invoking(s => s.Reload())
            .Should().ThrowAsync<Exception>()
            .WithMessage("Reload error");

        _service.IsLoaded.Should().BeFalse();
    }

    private List<CatalogAggregate> CreateTestProducts()
    {
        return new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 15),
                    ProductCode = "PROD001",
                    AmountTotal = 100,
                    SumTotal = 1000m
                }
            }),
            CreateTestProduct("PROD002", new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 20),
                    ProductCode = "PROD002",
                    AmountTotal = 100,
                    SumTotal = 1000m
                }
            })
        };
    }

    private CatalogAggregate CreateTestProduct(string productCode, List<CatalogSaleRecord> salesHistory)
    {
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = $"Test Product {productCode}",
            SalesHistory = salesHistory
        };

        return product;
    }

    private List<CostStatistics> CreateTestCosts()
    {
        return new List<CostStatistics>
        {
            new CostStatistics { Date = new DateTime(2024, 1, 15), Cost = 500m, Department = "SKLAD" },
            new CostStatistics { Date = new DateTime(2024, 1, 20), Cost = 500m, Department = "MARKETING" }
        };
    }

    private void SetupLedgerServiceMocks(List<CostStatistics> costs)
    {
        var warehouseCosts = costs.Where(c => c.Department == "SKLAD").ToList();
        var marketingCosts = costs.Where(c => c.Department == "MARKETING").ToList();

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(warehouseCosts);

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(marketingCosts);
    }
}