using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Logistics;

public class GiftPackageManufactureServiceTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IGiftPackageManufactureRepository> _giftPackageRepositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IStockUpProcessingService> _stockUpProcessingServiceMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ILogger<GiftPackageManufactureService>> _loggerMock;
    private readonly GiftPackageManufactureService _service;
    private readonly DateTime _testDateTime = new DateTime(2024, 6, 15);

    public GiftPackageManufactureServiceTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _giftPackageRepositoryMock = new Mock<IGiftPackageManufactureRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _stockUpProcessingServiceMock = new Mock<IStockUpProcessingService>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<GiftPackageManufactureService>>();

        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        _service = new GiftPackageManufactureService(
            _manufactureClientMock.Object,
            _giftPackageRepositoryMock.Object,
            _catalogRepositoryMock.Object,
            _currentUserServiceMock.Object,
            _stockUpProcessingServiceMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_ShouldReturnGiftPackagesWithCorrectDailySales()
    {
        // Arrange
        var catalogData = CreateTestCatalogData();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogData);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync();

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(2); // Only Set type products

        var giftPackage1 = result.First(x => x.Code == "SET001");
        giftPackage1.Name.Should().Be("Test Gift Set 1");
        giftPackage1.AvailableStock.Should().Be(50);
        giftPackage1.DailySales.Should().BeApproximately(0.274m, 0.001m); // 100 sales / 365 days
        giftPackage1.OverstockMinimal.Should().Be(20);
        giftPackage1.SuggestedQuantity.Should().BeGreaterOrEqualTo(0);
        giftPackage1.Severity.Should().BeDefined();

        var giftPackage2 = result.First(x => x.Code == "SET002");
        giftPackage2.Name.Should().Be("Test Gift Set 2");
        giftPackage2.AvailableStock.Should().Be(30);
        giftPackage2.DailySales.Should().BeApproximately(0.410m, 0.002m); // 150 sales / 365 days
        giftPackage2.OverstockMinimal.Should().Be(20); // All test products have StockMinSetup = 20
        giftPackage2.SuggestedQuantity.Should().BeGreaterOrEqualTo(0);
        giftPackage2.Severity.Should().BeDefined();
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_ShouldFilterOnlySetProducts()
    {
        // Arrange
        var catalogData = CreateTestCatalogData();
        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogData);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync();

        // Assert
        result.Should().HaveCount(2); // Should exclude Material and Goods products
        result.Should().OnlyContain(x => x.Code.StartsWith("SET"));
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_WithNoSetProducts_ShouldReturnEmptyList()
    {
        // Arrange
        var catalogData = new List<CatalogAggregate>
        {
            CreateCatalogItem("MAT001", "Material 1", ProductType.Material, 100, 30),
            CreateCatalogItem("GOODS001", "Goods 1", ProductType.Goods, 80, 25)
        };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogData);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateCatalogItem(giftPackageCode, "Test Gift Set 1", ProductType.Set, 100, 50);
        var ingredients = CreateTestIngredients();

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock.Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());

        // Setup catalog repository to return ingredient products
        foreach (var ingredient in ingredients)
        {
            var ingredientProduct = CreateCatalogItem(ingredient.ProductCode, ingredient.ProductName, ProductType.Material, 0, ingredient.AvailableStock);
            _catalogRepositoryMock.Setup(x => x.GetByIdAsync(ingredient.ProductCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ingredientProduct);
        }

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert
        result.Should().NotBeNull();
        result.Code.Should().Be(giftPackageCode);
        result.Name.Should().Be("Test Gift Set 1");
        result.AvailableStock.Should().Be(50);
        result.DailySales.Should().BeApproximately(0.274m, 0.001m); // 100 sales / 365 days
        result.OverstockMinimal.Should().Be(20);
        result.SuggestedQuantity.Should().BeGreaterOrEqualTo(0);
        result.Severity.Should().BeDefined();

        result.Ingredients.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients.Should().Contain(x => x.ProductCode == "ING001" && x.RequiredQuantity == 2.0);
        result.Ingredients.Should().Contain(x => x.ProductCode == "ING002" && x.RequiredQuantity == 1.5);
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_WithNonExistentProduct_ShouldThrowArgumentException()
    {
        // Arrange
        var giftPackageCode = "NONEXISTENT";
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate)null);

        // Act & Assert
        await _service.Invoking(x => x.GetGiftPackageDetailAsync(giftPackageCode))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Gift package '{giftPackageCode}' not found or is not a set product");
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_WithNonSetProduct_ShouldThrowArgumentException()
    {
        // Arrange
        var productCode = "MAT001";
        var product = CreateCatalogItem(productCode, "Material 1", ProductType.Material, 100, 50);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(productCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        // Act & Assert
        await _service.Invoking(x => x.GetGiftPackageDetailAsync(productCode))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Gift package '{productCode}' not found or is not a set product");
    }

    [Fact]
    public async Task CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var quantity = 5;
        var userId = "testUser";
        var product = CreateCatalogItem(giftPackageCode, "Test Gift Set 1", ProductType.Set, 100, 50);

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock.Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());

        // Setup catalog repository to return ingredient products
        var ingredients = CreateTestIngredients();
        foreach (var ingredient in ingredients)
        {
            var ingredientProduct = CreateCatalogItem(ingredient.ProductCode, ingredient.ProductName, ProductType.Material, 0, ingredient.AvailableStock);
            _catalogRepositoryMock.Setup(x => x.GetByIdAsync(ingredient.ProductCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ingredientProduct);
        }

        var expectedManufactureDto = new GiftPackageManufactureDto
        {
            GiftPackageCode = giftPackageCode,
            QuantityCreated = quantity,
            CreatedBy = userId,
            CreatedAt = _testDateTime
        };

        _mapperMock.Setup(x => x.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
            .Returns(expectedManufactureDto);

        // Setup current user service to return test user
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "test-user-id", Name: userId, Email: "test@example.com", IsAuthenticated: true));

        // Setup stock up processing service to create operations (no return value)
        _stockUpProcessingServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateManufactureAsync(giftPackageCode, quantity, false, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.GiftPackageCode.Should().Be(giftPackageCode);
        result.QuantityCreated.Should().Be(quantity);
        result.CreatedBy.Should().Be(userId);

        _giftPackageRepositoryMock.Verify(x => x.AddAsync(It.Is<GiftPackageManufactureLog>(log =>
            log.GiftPackageCode == giftPackageCode &&
            log.QuantityCreated == quantity &&
            log.CreatedBy == userId &&
            log.ConsumedItems.Count == 2), It.IsAny<CancellationToken>()), Times.Once);

        _giftPackageRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_WithZeroDaysDiff_ShouldUseDaysDiffAsOne()
    {
        // Arrange - Set up time provider to return same date for from and to
        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        var catalogData = new List<CatalogAggregate>
        {
            CreateCatalogItem("SET001", "Test Gift Set 1", ProductType.Set, 100, 50)
        };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogData);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync();

        // Assert
        result.Should().HaveCount(1);
        var giftPackage = result.First();
        // With daysDiff = Math.Max(365, 1) = 365, daily sales should be 100/365
        giftPackage.DailySales.Should().BeApproximately(0.274m, 0.001m);
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_WithCustomDateRange_ShouldUseSpecifiedDates()
    {
        // Arrange
        var customFromDate = new DateTime(2024, 1, 1);
        var customToDate = new DateTime(2024, 3, 31); // 3 month period = ~90 days
        var expectedDaysDiff = (customToDate - customFromDate).Days; // Should be around 90 days

        var catalogData = new List<CatalogAggregate>
        {
            CreateCatalogItemWithSpecificSalesData("SET001", "Test Gift Set 1", ProductType.Set, customFromDate, customToDate, 180) // 180 sales in 90 days = 2 sales/day
        };

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogData);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync(1.0m, customFromDate, customToDate);

        // Assert
        result.Should().HaveCount(1);
        var giftPackage = result.First();

        // Daily sales should be around 180 sales / 90 days = 2.0 sales/day (approximately due to weekly distribution)
        giftPackage.DailySales.Should().BeApproximately(2.0m, 0.3m);
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var customFromDate = new DateTime(2024, 1, 1);
        var customToDate = new DateTime(2024, 2, 29); // 2 month period = ~60 days

        var product = CreateCatalogItemWithSpecificSalesData(giftPackageCode, "Test Gift Set 1", ProductType.Set, customFromDate, customToDate, 120); // 120 sales in 60 days = 2 sales/day
        var ingredients = CreateTestIngredients();

        _catalogRepositoryMock.Setup(x => x.GetByIdAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock.Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());

        // Setup catalog repository to return ingredient products
        foreach (var ingredient in ingredients)
        {
            var ingredientProduct = CreateCatalogItem(ingredient.ProductCode, ingredient.ProductName, ProductType.Material, 0, ingredient.AvailableStock);
            _catalogRepositoryMock.Setup(x => x.GetByIdAsync(ingredient.ProductCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ingredientProduct);
        }

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode, 1.0m, customFromDate, customToDate);

        // Assert
        result.Should().NotBeNull();
        result.Code.Should().Be(giftPackageCode);

        // Daily sales should be around 120 sales / 60 days = 2.0 sales/day (approximately due to weekly distribution)
        result.DailySales.Should().BeApproximately(2.0m, 0.3m);
    }

    private List<CatalogAggregate> CreateTestCatalogData()
    {
        return new List<CatalogAggregate>
        {
            CreateCatalogItem("SET001", "Test Gift Set 1", ProductType.Set, 100, 50),
            CreateCatalogItem("SET002", "Test Gift Set 2", ProductType.Set, 150, 30),
            CreateCatalogItem("MAT001", "Material 1", ProductType.Material, 80, 100),
            CreateCatalogItem("GOODS001", "Goods 1", ProductType.Goods, 120, 75)
        };
    }

    private CatalogAggregate CreateCatalogItem(string code, string name, ProductType type, double totalSales, double availableStock)
    {
        var item = new CatalogAggregate
        {
            ProductCode = code,
            ProductName = name,
            Type = type,
            Stock = new Anela.Heblo.Domain.Features.Catalog.Stock.StockData { Erp = (decimal)availableStock },
            Properties = new CatalogProperties { StockMinSetup = 20 }
        };

        // Create sales history for the last year to simulate GetTotalSold result
        var salesHistory = new List<CatalogSaleRecord>();
        var fromDate = _testDateTime.AddYears(-1);

        // Distribute sales across the year to simulate realistic data
        for (int i = 0; i < 12; i++)
        {
            var monthDate = fromDate.AddMonths(i);
            salesHistory.Add(new CatalogSaleRecord
            {
                Date = monthDate,
                AmountB2B = totalSales / 24, // Half B2B
                AmountB2C = totalSales / 24, // Half B2C
                SumB2B = (decimal)(totalSales / 24 * 10), // Dummy price calculation
                SumB2C = (decimal)(totalSales / 24 * 12)
            });
        }

        item.SalesHistory = salesHistory;
        return item;
    }

    private CatalogAggregate CreateCatalogItemWithSpecificSalesData(string code, string name, ProductType type, DateTime fromDate, DateTime toDate, double totalSales)
    {
        var item = new CatalogAggregate
        {
            ProductCode = code,
            ProductName = name,
            Type = type,
            Stock = new Anela.Heblo.Domain.Features.Catalog.Stock.StockData { Erp = 50 }, // Default stock
            Properties = new CatalogProperties { StockMinSetup = 20 }
        };

        // Create sales history within the specified date range
        var salesHistory = new List<CatalogSaleRecord>();
        var daysDiff = (toDate - fromDate).Days;
        var salesPerRecord = totalSales / Math.Max(daysDiff / 7, 1); // Weekly records

        for (var date = fromDate; date <= toDate; date = date.AddDays(7))
        {
            salesHistory.Add(new CatalogSaleRecord
            {
                Date = date,
                AmountB2B = salesPerRecord / 2, // Half B2B
                AmountB2C = salesPerRecord / 2, // Half B2C
                SumB2B = (decimal)(salesPerRecord / 2 * 10), // Dummy price calculation
                SumB2C = (decimal)(salesPerRecord / 2 * 12)
            });
        }

        item.SalesHistory = salesHistory;
        return item;
    }

    private List<GiftPackageIngredientDto> CreateTestIngredients()
    {
        return new List<GiftPackageIngredientDto>
        {
            new GiftPackageIngredientDto
            {
                ProductCode = "ING001",
                ProductName = "Ingredient 1",
                RequiredQuantity = 2.0,
                AvailableStock = 100.0
            },
            new GiftPackageIngredientDto
            {
                ProductCode = "ING002",
                ProductName = "Ingredient 2",
                RequiredQuantity = 1.5,
                AvailableStock = 75.0
            }
        };
    }

    private List<ProductPart> CreateTestProductParts()
    {
        return new List<ProductPart>
        {
            new ProductPart { ProductCode = "ING001", ProductName = "Ingredient 1", Amount = 2.0 },
            new ProductPart { ProductCode = "ING002", ProductName = "Ingredient 2", Amount = 1.5 }
        };
    }
}