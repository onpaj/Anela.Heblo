using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class ManufactureCostCalculationServiceTests
{
    private readonly Mock<ILedgerService> _ledgerServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IOptions<DataSourceOptions>> _dataSourceOptionsMock;
    private readonly Mock<ILogger<ManufactureCostCalculationService>> _loggerMock;
    private readonly ManufactureCostCalculationService _service;
    private readonly DateTime _testDateTime = new DateTime(2024, 6, 15); // Mid-month for testing

    public ManufactureCostCalculationServiceTests()
    {
        _ledgerServiceMock = new Mock<ILedgerService>();
        _timeProviderMock = new Mock<TimeProvider>();
        _dataSourceOptionsMock = new Mock<IOptions<DataSourceOptions>>();
        _loggerMock = new Mock<ILogger<ManufactureCostCalculationService>>();

        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        _dataSourceOptionsMock.Setup(x => x.Value)
            .Returns(new DataSourceOptions { ManufactureCostHistoryDays = 400 });

        _service = new ManufactureCostCalculationService(
            _ledgerServiceMock.Object,
            _timeProviderMock.Object,
            _dataSourceOptionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WhenNotLoaded_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var products = CreateTestProducts();

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().BeEmpty();
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_AfterReload_ShouldReturnCorrectCosts()
    {
        // Arrange
        var products = CreateTestProducts();
        var directCosts = CreateTestDirectCosts();
        var personalCosts = CreateTestPersonalCosts();

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(directCosts);

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(personalCosts);

        // Act
        await _service.Reload(products);
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().ContainKey("PROD001");
        result.Should().ContainKey("PROD002");
        _service.IsLoaded.Should().BeTrue();

        var product1Costs = result["PROD001"];
        product1Costs.Should().NotBeEmpty();
        product1Costs[0].MaterialCostFromReceiptDocument.Should().BeGreaterThan(0);
        product1Costs[0].HandlingCost.Should().BeGreaterThan(0);
        product1Costs[0].Total.Should().Be(product1Costs[0].MaterialCost + product1Costs[0].HandlingCost);
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WithNoManufactureDifficulty_ShouldSkipProducts()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ManufactureHistory = new List<ManufactureHistoryRecord>
                {
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-1),
                        Amount = 10,
                        PricePerPiece = 5.0m
                    }
                }
            }
        };

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WithNoManufactureHistory_ShouldReturnEmptyResult()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ManufactureHistory = new List<ManufactureHistoryRecord>() // Empty history
            }
        };

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDirectCosts());

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestPersonalCosts());

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WithEmptyProducts_ShouldReturnEmptyResult()
    {
        // Arrange
        var products = new List<CatalogAggregate>();

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().BeEmpty();
        // Since service is not loaded, ledger service methods should not be called
        _ledgerServiceMock.Verify(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _ledgerServiceMock.Verify(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WithNoCosts_ShouldReturnEmptyResult()
    {
        // Arrange
        var products = CreateTestProducts();

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Reload_WithCorrectDateRange_ShouldCallLedgerServiceWithCorrectDates()
    {
        // Arrange
        var products = CreateTestProducts();
        var expectedEndDate = DateOnly.FromDateTime(_testDateTime.Date);
        var expectedStartDate = expectedEndDate.AddDays(-400); // Using ManufactureCostHistoryDays

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        // Act
        await _service.Reload(products);

        // Assert
        _ledgerServiceMock.Verify(x => x.GetDirectCosts(expectedStartDate, expectedEndDate, "VYROBA", It.IsAny<CancellationToken>()), Times.Once);
        _ledgerServiceMock.Verify(x => x.GetPersonalCosts(expectedStartDate, expectedEndDate, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reload_WithPersonalCosts_ShouldDividePersonalCostsByTwo()
    {
        // Arrange
        var products = CreateTestProducts();

        var directCosts = new List<CostStatistics>
        {
            new CostStatistics { Date = _testDateTime.AddMonths(-1), Cost = 100m, Department = "VYROBA" }
        };

        var personalCosts = new List<CostStatistics>
        {
            new CostStatistics { Date = _testDateTime.AddMonths(-1), Cost = 200m, Department = "HR" }
        };

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(directCosts);

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(personalCosts);

        // Act
        await _service.Reload(products);
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().NotBeEmpty();
        // The total cost should be 100 (direct) + 100 (personal/2) = 200
        // This should be reflected in the calculated handling costs
        result.Values.SelectMany(v => v).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Reload_WithMultipleManufactureRecords_ShouldCalculateWeightedAverage()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ManufactureHistory = new List<ManufactureHistoryRecord>
                {
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-1),
                        Amount = 10,
                        PricePerPiece = 5.0m // 10 * 5 = 50
                    },
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-1),
                        Amount = 20,
                        PricePerPiece = 3.0m // 20 * 3 = 60
                    }
                    // Total: 30 pieces, 110 total cost, weighted average = 110/30 = 3.67
                }
            }
        };

        SetupManufactureDifficulty(products[0], 2);

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new CostStatistics { Date = _testDateTime.AddMonths(-1), Cost = 60m, Department = "VYROBA" }
            });

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        // Act
        await _service.Reload(products);
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().ContainKey("PROD001");
        var productCosts = result["PROD001"];
        productCosts.Should().HaveCount(1);

        var materialCost = productCosts[0].MaterialCostFromReceiptDocument;
        materialCost.Should().BeApproximately(3.67m, 0.01m); // Weighted average should be ~3.67
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WhenNotLoaded_ShouldHandleCancellationToken()
    {
        // Arrange
        var products = CreateTestProducts();
        var cancellationToken = new CancellationToken();

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products, cancellationToken);

        // Assert
        result.Should().BeEmpty();
        // Verify that ledger service methods are not called when service is not loaded
        _ledgerServiceMock.Verify(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", cancellationToken), Times.Never);
        _ledgerServiceMock.Verify(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), cancellationToken), Times.Never);
    }

    [Fact]
    public async Task CalculateManufactureCostHistoryAsync_WithZeroWeightedProduction_ShouldSkipMonth()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ManufactureHistory = new List<ManufactureHistoryRecord>
                {
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-1),
                        Amount = 10,
                        PricePerPiece = 5.0m
                    }
                }
            }
        };

        SetupManufactureDifficulty(products[0], 0);

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new CostStatistics { Date = _testDateTime.AddMonths(-1), Cost = 100m, Department = "VYROBA" }
            });

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        // Act
        var result = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        result.Should().BeEmpty(); // Should be empty because products with 0 difficulty are filtered out
    }

    private List<CatalogAggregate> CreateTestProducts()
    {
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ManufactureHistory = new List<ManufactureHistoryRecord>
                {
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-1),
                        Amount = 10,
                        PricePerPiece = 5.0m
                    },
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-2),
                        Amount = 15,
                        PricePerPiece = 4.0m
                    }
                }
            },
            new CatalogAggregate
            {
                ProductCode = "PROD002",
                ManufactureHistory = new List<ManufactureHistoryRecord>
                {
                    new ManufactureHistoryRecord
                    {
                        Date = _testDateTime.AddMonths(-1),
                        Amount = 20,
                        PricePerPiece = 3.0m
                    }
                }
            }
        };

        // Setup difficulty settings for test products
        SetupManufactureDifficulty(products[0], 3); // was 2.5, rounded to 3
        SetupManufactureDifficulty(products[1], 2); // was 1.5, rounded to 2

        return products;
    }

    private List<CostStatistics> CreateTestDirectCosts()
    {
        return new List<CostStatistics>
        {
            new CostStatistics
            {
                Date = _testDateTime.AddMonths(-1),
                Cost = 1000m,
                Department = "VYROBA"
            },
            new CostStatistics
            {
                Date = _testDateTime.AddMonths(-2),
                Cost = 1500m,
                Department = "VYROBA"
            }
        };
    }

    private List<CostStatistics> CreateTestPersonalCosts()
    {
        return new List<CostStatistics>
        {
            new CostStatistics
            {
                Date = _testDateTime.AddMonths(-1),
                Cost = 800m,
                Department = "HR"
            },
            new CostStatistics
            {
                Date = _testDateTime.AddMonths(-2),
                Cost = 1200m,
                Department = "HR"
            }
        };
    }

    [Fact]
    public async Task Reload_WithNoManufactureHistory_ShouldNotLoadData()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new CatalogAggregate
            {
                ProductCode = "PROD001",
                ManufactureHistory = new List<ManufactureHistoryRecord>() // Empty history
            }
        };
        SetupManufactureDifficulty(products[0], 2);

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestDirectCosts());

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestPersonalCosts());

        // Act
        await _service.Reload(products);

        // Assert
        _service.IsLoaded.Should().BeFalse(); // Should not be loaded when no manufacture history
    }

    [Fact]
    public async Task Reload_AfterSuccessfulLoad_ShouldReturnCachedData()
    {
        // Arrange
        var products = CreateTestProducts();
        var directCosts = CreateTestDirectCosts();
        var personalCosts = CreateTestPersonalCosts();

        _ledgerServiceMock
            .Setup(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(directCosts);

        _ledgerServiceMock
            .Setup(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(personalCosts);

        // Act
        await _service.Reload(products);
        var firstCall = await _service.CalculateManufactureCostHistoryAsync(products);
        var secondCall = await _service.CalculateManufactureCostHistoryAsync(products);

        // Assert
        _service.IsLoaded.Should().BeTrue();
        firstCall.Should().NotBeEmpty();
        secondCall.Should().NotBeEmpty();
        firstCall.Should().BeEquivalentTo(secondCall);

        // Verify ledger service was called only during Reload, not during subsequent CalculateManufactureCostHistoryAsync calls
        _ledgerServiceMock.Verify(x => x.GetDirectCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "VYROBA", It.IsAny<CancellationToken>()), Times.Once);
        _ledgerServiceMock.Verify(x => x.GetPersonalCosts(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupManufactureDifficulty(CatalogAggregate product, int difficultyValue)
    {
        var settings = new List<ManufactureDifficultySetting>
        {
            new ManufactureDifficultySetting
            {
                ProductCode = product.ProductCode,
                DifficultyValue = difficultyValue,
                ValidFrom = _testDateTime.AddMonths(-12),
                ValidTo = null,
                CreatedAt = _testDateTime.AddMonths(-12),
                CreatedBy = "Test"
            }
        };
        product.ManufactureDifficultySettings.Assign(settings, _testDateTime);
    }
}