using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.Sales;
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
            .Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultProducts);

        _service = new SalesCostCalculationService(
            _ledgerServiceMock.Object,
            _catalogRepositoryMock.Object,
            _timeProviderMock.Object,
            _dataSourceOptionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithValidData_ReturnsCorrectCostAllocation()
    {
        // Arrange
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2); // Two products
        
        // Check ProductA costs (sold 100 units total in Jan 2024)
        result.Should().ContainKey("PROD001");
        var productACosts = result["PROD001"];
        productACosts.Should().HaveCount(1);
        productACosts[0].Date.Should().Be(new DateTime(2024, 1, 1));
        productACosts[0].Cost.Should().Be(500m); // (1000 total cost / 200 total units) * 100 units = 500

        // Check ProductB costs (sold 100 units total in Jan 2024)
        result.Should().ContainKey("PROD002");
        var productBCosts = result["PROD002"];
        productBCosts.Should().HaveCount(1);
        productBCosts[0].Date.Should().Be(new DateTime(2024, 1, 1));
        productBCosts[0].Cost.Should().Be(500m); // (1000 total cost / 200 total units) * 100 units = 500
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithEmptyProducts_ReturnsEmptyResult()
    {
        // Arrange
        var emptyProducts = new List<CatalogAggregate>();

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithNoSalesHistory_ReturnsEmptyResult()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>())
        };
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithNoCosts_ReturnsEmptyResult()
    {
        // Arrange
        var products = CreateTestProducts();
        SetupLedgerServiceMocks(new List<CostStatistics>());

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithZeroUnitsSold_SkipsMonth()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 15),
                    ProductCode = "PROD001",
                    AmountTotal = 0, // Zero units sold
                    SumTotal = 0m
                }
            })
        };
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty(); // No results because month with zero sales is skipped
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithUnevenSalesDistribution_AllocatesProportionally()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 15),
                    ProductCode = "PROD001",
                    AmountTotal = 150, // 75% of total sales
                    SumTotal = 1500m
                }
            }),
            CreateTestProduct("PROD002", new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 20),
                    ProductCode = "PROD002",
                    AmountTotal = 50, // 25% of total sales
                    SumTotal = 500m
                }
            })
        };
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        
        // ProductA should get 75% of costs: (1000 / 200) * 150 = 750
        result["PROD001"][0].Cost.Should().Be(750m);
        
        // ProductB should get 25% of costs: (1000 / 200) * 50 = 250
        result["PROD002"][0].Cost.Should().Be(250m);
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithMultipleMonths_CalculatesEachMonthSeparately()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 15),
                    ProductCode = "PROD001",
                    AmountTotal = 100,
                    SumTotal = 1000m
                },
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 2, 15),
                    ProductCode = "PROD001",
                    AmountTotal = 50,
                    SumTotal = 500m
                }
            })
        };

        var costs = new List<CostStatistics>
        {
            new CostStatistics { Date = new DateTime(2024, 1, 15), Cost = 1000m, Department = "SKLAD" },
            new CostStatistics { Date = new DateTime(2024, 2, 15), Cost = 2000m, Department = "MARKETING" }
        };
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        
        var productCosts = result["PROD001"];
        productCosts.Should().HaveCount(2);
        
        // January: 1000 cost / 100 units = 10 per unit * 100 units = 1000
        var januaryCost = productCosts.Single(c => c.Date.Month == 1);
        januaryCost.Cost.Should().Be(1000m);
        
        // February: 2000 cost / 50 units = 40 per unit * 50 units = 2000
        var februaryCost = productCosts.Single(c => c.Date.Month == 2);
        februaryCost.Cost.Should().Be(2000m);
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithSalesOutsideDateRange_FiltersCorrectly()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateTestProduct("PROD001", new List<CatalogSaleRecord>
            {
                // Sale within range
                new CatalogSaleRecord
                {
                    Date = new DateTime(2024, 1, 15),
                    ProductCode = "PROD001",
                    AmountTotal = 100,
                    SumTotal = 1000m
                },
                // Sale outside range (too old)
                new CatalogSaleRecord
                {
                    Date = _fixedCurrentTime.AddDays(-500), // Outside 400 day range
                    ProductCode = "PROD001",
                    AmountTotal = 200,
                    SumTotal = 2000m
                }
            })
        };
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        
        var productCosts = result["PROD001"];
        productCosts.Should().HaveCount(1); // Only one month within range
        productCosts[0].Cost.Should().Be(1000m); // Cost for 100 units only
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithLedgerServiceException_ReturnsEmptyResult()
    {
        // Arrange
        var products = CreateTestProducts();
        var exception = new Exception("Ledger service error");
        
        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_WithCachedData_ReturnsCachedResult()
    {
        // Arrange
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // First call to populate cache
        await _service.Reload();

        // Reset mock to verify it's not called again
        _ledgerServiceMock.Reset();

        // Act
        var result = await _service.CalculateSalesCostHistoryAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        
        // Verify ledger service was not called (using cached data)
        _ledgerServiceMock.Verify(
            x => x.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reload_WithValidData_UpdatesCacheAndSetsIsLoaded()
    {
        // Arrange
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Assert initial state
        _service.IsLoaded.Should().BeFalse();

        // Act
        await _service.Reload();

        // Assert
        _service.IsLoaded.Should().BeTrue();
        
        // Verify subsequent calls use cache
        var result = await _service.CalculateSalesCostHistoryAsync();
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Reload_WithEmptyData_DoesNotUpdateCache()
    {
        // Arrange
        var products = CreateTestProducts();
        SetupLedgerServiceMocks(new List<CostStatistics>()); // No costs

        // Act
        await _service.Reload();

        // Assert
        _service.IsLoaded.Should().BeFalse(); // Should not be loaded when no data
    }

    [Fact]
    public async Task Reload_WithException_DoesNotUpdateCacheAndThrows()
    {
        // Arrange
        var products = CreateTestProducts();
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

    [Theory]
    [InlineData("SKLAD")]
    [InlineData("MARKETING")]
    public async Task CalculateSalesCostHistoryAsync_CallsCorrectDepartments(string department)
    {
        // Arrange
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        // Act
        await _service.CalculateSalesCostHistoryAsync();

        // Assert
        _ledgerServiceMock.Verify(
            x => x.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), department, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CalculateSalesCostHistoryAsync_UsesCorrectDateRange()
    {
        // Arrange
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        SetupLedgerServiceMocks(costs);

        var expectedStartDate = _fixedCurrentTime.Date.AddDays(-400);
        var expectedEndDate = _fixedCurrentTime.Date;

        // Act
        await _service.CalculateSalesCostHistoryAsync();

        // Assert
        _ledgerServiceMock.Verify(
            x => x.GetDirectCosts(expectedStartDate, expectedEndDate, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
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

    [Fact]
    public async Task GetCostsAsync_WithValidProductCodes_ReturnsCorrectMonthlyCosts()
    {
        // Arrange
        var productCodes = new List<string> { "PROD001", "PROD002" };
        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 2, 28);

        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        
        SetupLedgerServiceMocks(costs);
        SetupCatalogRepositoryMocks(products);

        // Act
        var result = await _service.GetCostsAsync(productCodes, dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        result.Should().ContainKey("PROD002");
        
        // Verify PROD001 has costs for January
        var prod001Costs = result["PROD001"];
        prod001Costs.Should().HaveCount(1);
        prod001Costs.First().Month.Should().Be(new DateTime(2024, 1, 1));
        prod001Costs.First().Cost.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetCostsAsync_WithSingleProductCode_ReturnsCorrectResult()
    {
        // Arrange
        var productCodes = new List<string> { "PROD001" };
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        
        SetupLedgerServiceMocks(costs);
        var singleProduct = products.First(p => p.ProductCode == "PROD001");
        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(singleProduct);

        // Act
        var result = await _service.GetCostsAsync(productCodes);

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCostsAsync_WithNullProductCodes_ReturnsAllProducts()
    {
        // Arrange
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        
        SetupLedgerServiceMocks(costs);
        SetupCatalogRepositoryMocks(products);

        // Act
        var result = await _service.GetCostsAsync(null);

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        result.Should().ContainKey("PROD002");
    }

    [Fact]
    public async Task GetCostsAsync_WithEmptyProductCodes_ReturnsAllProducts()
    {
        // Arrange
        var productCodes = new List<string>();
        var products = CreateTestProducts();
        var costs = CreateTestCosts();
        
        SetupLedgerServiceMocks(costs);
        SetupCatalogRepositoryMocks(products);

        // Act
        var result = await _service.GetCostsAsync(productCodes);

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("PROD001");
        result.Should().ContainKey("PROD002");
    }

    private void SetupCatalogRepositoryMocks(List<CatalogAggregate> products)
    {
        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);
    }
}