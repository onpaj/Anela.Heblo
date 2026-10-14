using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Logistics;

public class GiftPackageManufactureServiceTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IGiftPackageManufactureRepository> _giftPackageRepositoryMock;
    private readonly Mock<ILogisticsCatalogSource> _catalogSourceMock;
    private readonly Mock<ILogisticsStockOperationService> _stockOperationServiceMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ILogger<GiftPackageManufactureService>> _loggerMock;
    private readonly GiftPackageManufactureService _service;
    private readonly DateTime _testDateTime = new DateTime(2024, 6, 15);

    public GiftPackageManufactureServiceTests()
    {
        _manufactureClientMock = new Mock<IManufactureClient>();
        _giftPackageRepositoryMock = new Mock<IGiftPackageManufactureRepository>();
        _catalogSourceMock = new Mock<ILogisticsCatalogSource>();
        _stockOperationServiceMock = new Mock<ILogisticsStockOperationService>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<GiftPackageManufactureService>>();

        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        // CreateManufactureAsync/DisassembleGiftPackageAsync now wrap their writes in
        // _giftPackageRepository.ExecuteInTransactionAsync(...). Moq does not invoke a delegate
        // parameter on an unstubbed call (it returns a completed Task<TResult> with a default
        // result instead), so every return-type overload actually used must be stubbed to run
        // the delegate, or the wrapped method body never executes.
        _giftPackageRepositoryMock
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageManufactureDto>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<GiftPackageManufactureDto>>, CancellationToken>(
                (operation, ct) => operation(ct));

        _giftPackageRepositoryMock
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>, CancellationToken>(
                (operation, ct) => operation(ct));

        _service = new GiftPackageManufactureService(
            _manufactureClientMock.Object,
            _giftPackageRepositoryMock.Object,
            _catalogSourceMock.Object,
            _stockOperationServiceMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_ShouldReturnGiftPackagesWithCorrectDailySales()
    {
        // Arrange
        var setProducts = CreateTestGiftPackageItems();
        _catalogSourceMock
            .Setup(x => x.GetGiftPackageSetsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(setProducts);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync();

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(2);

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
        giftPackage2.OverstockMinimal.Should().Be(20);
        giftPackage2.SuggestedQuantity.Should().BeGreaterOrEqualTo(0);
        giftPackage2.Severity.Should().BeDefined();
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_WithNoSetProducts_ShouldReturnEmptyList()
    {
        // Arrange
        _catalogSourceMock
            .Setup(x => x.GetGiftPackageSetsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogisticsGiftPackageItem>());

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
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());

        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", WarehouseStock = 100m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", WarehouseStock = 75m },
            });

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
        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogisticsGiftPackageItem?)null);

        // Act & Assert
        await _service.Invoking(x => x.GetGiftPackageDetailAsync(giftPackageCode))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Gift package '{giftPackageCode}' not found or is not a set product");
    }

    [Fact]
    public async Task CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var quantity = 5;
        var userId = "testUser";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());

        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", WarehouseStock = 100m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", WarehouseStock = 75m },
            });

        var expectedManufactureDto = new GiftPackageManufactureDto
        {
            GiftPackageCode = giftPackageCode,
            QuantityCreated = quantity,
            CreatedBy = userId,
            CreatedAt = _testDateTime
        };

        _mapperMock.Setup(x => x.Map<GiftPackageManufactureDto>(It.IsAny<GiftPackageManufactureLog>()))
            .Returns(expectedManufactureDto);

        _stockOperationServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateManufactureAsync(giftPackageCode, quantity, false, userId, CancellationToken.None);

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
    public async Task DisassembleGiftPackageAsync_WithZeroQuantity_ThrowsArgumentExceptionBeforeAnyRepositoryCall()
    {
        await _service.Invoking(x => x.DisassembleGiftPackageAsync("SET001", 0, "tester", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        _giftPackageRepositoryMock.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisassembleGiftPackageAsync_WithQuantityExceedingAvailableStock_ThrowsInvalidOperationExceptionBeforeAnyRepositoryCall()
    {
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", WarehouseStock = 50m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", WarehouseStock = 50m },
            });

        await _service.Invoking(x => x.DisassembleGiftPackageAsync(giftPackageCode, 999, "tester", CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        _giftPackageRepositoryMock.Verify(
            x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GiftPackageDisassemblyDto>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_WithZeroDaysDiff_ShouldUseDaysDiffAsOne()
    {
        // Arrange
        _timeProviderMock.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_testDateTime, TimeSpan.Zero));

        var setProducts = new List<LogisticsGiftPackageItem>
        {
            CreateGiftPackageItem("SET001", "Test Gift Set 1", 100, 50)
        };

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageSetsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(setProducts);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync();

        // Assert
        result.Should().HaveCount(1);
        var giftPackage = result.First();
        giftPackage.DailySales.Should().BeApproximately(0.274m, 0.001m);
    }

    [Fact]
    public async Task GetAvailableGiftPackagesAsync_WithCustomDateRange_ShouldUseSpecifiedDates()
    {
        // Arrange
        var customFromDate = new DateTime(2024, 1, 1);
        var customToDate = new DateTime(2024, 3, 31);
        var expectedDaysDiff = (customToDate - customFromDate).Days; // ~90 days

        var setProducts = new List<LogisticsGiftPackageItem>
        {
            CreateGiftPackageItemWithSales("SET001", "Test Gift Set 1", 180, 50) // 180 sales / 90 days = 2/day
        };

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageSetsAsync(customFromDate, customToDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(setProducts);

        // Act
        var result = await _service.GetAvailableGiftPackagesAsync(1.0m, customFromDate, customToDate);

        // Assert
        result.Should().HaveCount(1);
        var giftPackage = result.First();
        giftPackage.DailySales.Should().BeApproximately(2.0m, 0.1m);
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var customFromDate = new DateTime(2024, 1, 1);
        var customToDate = new DateTime(2024, 2, 29); // ~60 days

        var product = CreateGiftPackageItemWithSales(giftPackageCode, "Test Gift Set 1", 120, 50); // 120 / 60 = 2/day

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, customFromDate, customToDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> codes, CancellationToken _) =>
                (IReadOnlyDictionary<string, LogisticsCatalogItem>)codes.ToDictionary(
                    code => code,
                    code => new LogisticsCatalogItem { ProductCode = code, WarehouseStock = 50m }));

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode, 1.0m, customFromDate, customToDate);

        // Assert
        result.Should().NotBeNull();
        result.Code.Should().Be(giftPackageCode);
        result.DailySales.Should().BeApproximately(2.0m, 0.1m);
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_CallsGetCatalogItemsAsyncOncePerInvocation()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> codes, CancellationToken _) =>
                (IReadOnlyDictionary<string, LogisticsCatalogItem>)codes.ToDictionary(
                    code => code,
                    code => new LogisticsCatalogItem { ProductCode = code, WarehouseStock = 50m }));

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        _catalogSourceMock.Verify(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _catalogSourceMock.Verify(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, LogisticsCatalogItem>)new Dictionary<string, LogisticsCatalogItem>());

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert — missing ingredients get zero stock and null image
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients.Should().OnlyContain(i => i.AvailableStock == 0.0 && i.Image == null);
    }

    private List<LogisticsGiftPackageItem> CreateTestGiftPackageItems()
    {
        return new List<LogisticsGiftPackageItem>
        {
            CreateGiftPackageItem("SET001", "Test Gift Set 1", 100, 50),
            CreateGiftPackageItem("SET002", "Test Gift Set 2", 150, 30),
        };
    }

    private LogisticsGiftPackageItem CreateGiftPackageItem(string code, string name, int totalSoldInPeriod, decimal availableStock)
    {
        return new LogisticsGiftPackageItem
        {
            ProductCode = code,
            ProductName = name,
            AvailableStock = availableStock,
            TotalSoldInPeriod = totalSoldInPeriod,
            StockMinSetup = 20,
            OptimalStockDaysSetup = 30,
        };
    }

    private LogisticsGiftPackageItem CreateGiftPackageItemWithSales(string code, string name, int totalSoldInPeriod, decimal availableStock)
    {
        return new LogisticsGiftPackageItem
        {
            ProductCode = code,
            ProductName = name,
            AvailableStock = availableStock,
            TotalSoldInPeriod = totalSoldInPeriod,
            StockMinSetup = 20,
            OptimalStockDaysSetup = 30,
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

    [Fact]
    public async Task GetGiftPackageDetailAsync_ReportsIngredientWarehouseStockNotAvailableStock()
    {
        // Arrange - ING001 has 184 pcs in the warehouse and 165 pcs sitting in the manufacture
        // warehouse; only the 184 can actually be consumed by a gift package.
        var giftPackageCode = "SET001";
        ArrangeGiftPackage(giftPackageCode, warehouseStockByCode: new Dictionary<string, decimal>
        {
            ["ING001"] = 184m,
            ["ING002"] = 580m,
        });

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode, 1.0m, null, null, CancellationToken.None);

        // Assert
        result.Ingredients.Should().NotBeNull();
        result.Ingredients!.Single(i => i.ProductCode == "ING001").AvailableStock.Should().Be(184d);
    }

    [Fact]
    public async Task CreateManufactureAsync_WithInsufficientWarehouseStock_ThrowsBeforeAnyStockOperation()
    {
        // Arrange - ING001 needs 2 per package, so 100 packages need 200 but only 184 are in stock.
        var giftPackageCode = "SET001";
        ArrangeGiftPackage(giftPackageCode, warehouseStockByCode: new Dictionary<string, decimal>
        {
            ["ING001"] = 184m,
            ["ING002"] = 580m,
        });

        // Act
        var act = () => _service.CreateManufactureAsync(giftPackageCode, 100, allowStockOverride: false, "tester", CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<InsufficientStockException>();
        exception.Which.Message.Should().Contain("ING001");
        exception.Which.Message.Should().NotContain("ING002",
            "ING002 has 580 pcs against the 150 the run needs - only genuine shortages belong in the message");

        _stockOperationServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _giftPackageRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<GiftPackageManufactureLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateManufactureAsync_WithSufficientWarehouseStock_CreatesStockOperations()
    {
        // Arrange - 50 packages need 100 of ING001 and 75 of ING002; both are covered.
        var giftPackageCode = "SET001";
        ArrangeGiftPackage(giftPackageCode, warehouseStockByCode: new Dictionary<string, decimal>
        {
            ["ING001"] = 184m,
            ["ING002"] = 580m,
        });

        // Act
        await _service.CreateManufactureAsync(giftPackageCode, 50, allowStockOverride: false, "tester", CancellationToken.None);

        // Assert - two ingredient consumptions plus the package production
        _stockOperationServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task CreateManufactureAsync_WithStockOverride_ProceedsDespiteInsufficientStock()
    {
        // Arrange
        var giftPackageCode = "SET001";
        ArrangeGiftPackage(giftPackageCode, warehouseStockByCode: new Dictionary<string, decimal>
        {
            ["ING001"] = 184m,
            ["ING002"] = 580m,
        });

        // Act
        await _service.CreateManufactureAsync(giftPackageCode, 100, allowStockOverride: true, "tester", CancellationToken.None);

        // Assert
        _stockOperationServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public async Task CreateManufactureAsync_WithNonPositiveQuantity_ThrowsArgumentException(int quantity, bool allowStockOverride)
    {
        // Arrange - the quantity guard applies even under allowStockOverride, which only waives
        // the availability check.
        var giftPackageCode = "SET001";
        ArrangeGiftPackage(giftPackageCode, warehouseStockByCode: new Dictionary<string, decimal>
        {
            ["ING001"] = 184m,
            ["ING002"] = 580m,
        });

        // Act
        var act = () => _service.CreateManufactureAsync(giftPackageCode, quantity, allowStockOverride, "tester", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _stockOperationServiceMock.Verify(
            x => x.CreateOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void ArrangeGiftPackage(string giftPackageCode, Dictionary<string, decimal> warehouseStockByCode)
    {
        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50));
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(warehouseStockByCode.ToDictionary(
                kv => kv.Key,
                kv => new LogisticsCatalogItem { ProductCode = kv.Key, WarehouseStock = kv.Value }));
    }
}
