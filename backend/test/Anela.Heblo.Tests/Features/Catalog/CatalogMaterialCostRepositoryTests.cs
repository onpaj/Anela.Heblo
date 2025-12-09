using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogMaterialCostRepositoryTests
{
    private readonly Mock<ICatalogRepository> _mockCatalogRepository;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<ILogger<CatalogMaterialCostRepository>> _mockLogger;
    private readonly CatalogMaterialCostRepository _repository;
    private readonly DateTime _fixedCurrentTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public CatalogMaterialCostRepositoryTests()
    {
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockLogger = new Mock<ILogger<CatalogMaterialCostRepository>>();

        // Setup fixed current time for predictable date calculations
        _mockTimeProvider.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(_fixedCurrentTime));

        _repository = new CatalogMaterialCostRepository(
            _mockCatalogRepository.Object,
            _mockTimeProvider.Object,
            _mockLogger.Object);
    }

    #region Test Data Builders

    private CatalogAggregate CreateCatalogProduct(string productCode, string productName = "Test Product")
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            ErpId = 1,
            Type = ProductType.Material
        };
    }

    private ManufactureHistoryRecord CreateManufactureRecord(DateTime date, double amount, decimal priceTotal, string productCode = "TEST001")
    {
        return new ManufactureHistoryRecord
        {
            Date = date,
            Amount = amount,
            PriceTotal = priceTotal,
            PricePerPiece = amount > 0 ? priceTotal / (decimal)amount : 0,
            ProductCode = productCode,
            DocumentNumber = "DOC001"
        };
    }

    private ProductPriceErp CreateErpPrice(decimal purchasePrice)
    {
        return new ProductPriceErp
        {
            ProductCode = "TEST001",
            PurchasePrice = purchasePrice,
            PriceWithVat = purchasePrice * 1.21m,
            PriceWithoutVat = purchasePrice,
            PurchasePriceWithVat = purchasePrice * 1.21m
        };
    }

    #endregion

    #region Basic Functionality Tests

    [Fact]
    public async Task GetCostsAsync_WithNoProducts_ReturnsEmptyDictionary()
    {
        // Arrange
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        // Act
        var result = await _repository.GetCostsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _mockCatalogRepository.Verify(x => x.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCostsAsync_WithDefaultDateRange_UsesLast13Months()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(10.50m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        // Act
        var result = await _repository.GetCostsAsync();

        // Assert
        result.Should().ContainKey("TEST001");
        result["TEST001"].Should().HaveCount(13); // Last 12 months + current month

        // Verify date range - should be June 2023 to June 2024 (13 months)
        var monthlyCosts = result["TEST001"].OrderBy(x => x.Month).ToList();
        monthlyCosts.First().Month.Should().Be(new DateTime(2023, 6, 1));
        monthlyCosts.Last().Month.Should().Be(new DateTime(2024, 6, 1));
    }

    [Fact]
    public async Task GetCostsAsync_WithCustomDateRange_UsesSpecifiedRange()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(15.75m);

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 3, 31);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        result["TEST001"].Should().HaveCount(3); // Jan, Feb, Mar

        result["TEST001"].Should().AllSatisfy(cost => cost.Cost.Should().Be(15.75m));
    }

    [Fact]
    public async Task GetCostsAsync_WithProductCodesFilter_ReturnsOnlySpecifiedProducts()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateCatalogProduct("TEST001"),
            CreateCatalogProduct("TEST002"),
            CreateCatalogProduct("TEST003")
        };

        products.ForEach(p => p.ErpPrice = CreateErpPrice(10m));

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var productCodesToFilter = new List<string> { "TEST001", "TEST003" };

        // Act
        var result = await _repository.GetCostsAsync(productCodes: productCodesToFilter);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey("TEST001");
        result.Should().ContainKey("TEST003");
        result.Should().NotContainKey("TEST002");
    }

    #endregion

    #region Manufacture History Cost Calculation Tests

    [Fact]
    public async Task GetCostsAsync_WithManufactureHistory_CalculatesWeightedAveragePerMonth()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(5.00m); // Fallback price

        // January 2024: 2 manufacture records
        var jan2024_1 = CreateManufactureRecord(new DateTime(2024, 1, 10), 100, 1000m); // 10.00 per piece
        var jan2024_2 = CreateManufactureRecord(new DateTime(2024, 1, 20), 50, 600m);   // 12.00 per piece

        // February 2024: 1 manufacture record
        var feb2024_1 = CreateManufactureRecord(new DateTime(2024, 2, 5), 200, 3000m);  // 15.00 per piece

        product.ManufactureHistory = new List<ManufactureHistoryRecord> { jan2024_1, jan2024_2, feb2024_1 };

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 2, 28);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var costs = result["TEST001"].OrderBy(c => c.Month).ToList();

        costs.Should().HaveCount(2);

        // January: weighted average = (100*1000 + 50*600) / (100+50) = 130,000/150 = 10.67
        var januaryCost = costs.FirstOrDefault(c => c.Month.Month == 1);
        januaryCost.Should().NotBeNull();
        januaryCost!.Cost.Should().BeApproximately(10.67m, 0.01m);

        // February: 15.00 per piece
        var februaryCost = costs.FirstOrDefault(c => c.Month.Month == 2);
        februaryCost.Should().NotBeNull();
        februaryCost!.Cost.Should().Be(15.00m);
    }

    [Fact]
    public async Task GetCostsAsync_WithZeroAmountInManufactureHistory_UsesFallbackPrice()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(8.50m);

        // Manufacture record with zero amount
        var manufactureRecord = CreateManufactureRecord(new DateTime(2024, 1, 15), 0, 1000m);
        product.ManufactureHistory = new List<ManufactureHistoryRecord> { manufactureRecord };

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var januaryCost = result["TEST001"].First();
        januaryCost.Cost.Should().Be(0m); // Zero total amount results in zero cost per weighted average calculation
    }

    #endregion

    #region ERP Price Fallback Tests

    [Fact]
    public async Task GetCostsAsync_WithoutManufactureHistory_UsesErpPurchasePrice()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(12.34m);
        product.ManufactureHistory = new List<ManufactureHistoryRecord>(); // Empty history

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var januaryCost = result["TEST001"].First();
        januaryCost.Cost.Should().Be(12.34m);
    }

    [Fact]
    public async Task GetCostsAsync_WithoutErpPrice_UsesZeroCost()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = null; // No ERP price
        product.ManufactureHistory = new List<ManufactureHistoryRecord>(); // Empty history

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var januaryCost = result["TEST001"].First();
        januaryCost.Cost.Should().Be(0m);
    }

    [Fact]
    public async Task GetCostsAsync_WithErpPriceZeroPurchasePrice_UsesZeroCost()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(0m); // Zero purchase price
        product.ManufactureHistory = new List<ManufactureHistoryRecord>(); // Empty history

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var januaryCost = result["TEST001"].First();
        januaryCost.Cost.Should().Be(0m);
    }

    #endregion

    #region Mixed Scenarios Tests

    [Fact]
    public async Task GetCostsAsync_MixedManufactureHistoryAndFallback_UsesCorrectCostPerMonth()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(7.50m); // Fallback price

        // Only February has manufacture history
        var feb2024 = CreateManufactureRecord(new DateTime(2024, 2, 10), 150, 2250m); // 15.00 per piece
        product.ManufactureHistory = new List<ManufactureHistoryRecord> { feb2024 };

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 3, 31);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var costs = result["TEST001"].OrderBy(c => c.Month).ToList();

        costs.Should().HaveCount(3);

        // January: use fallback ERP price
        var januaryCost = costs.FirstOrDefault(c => c.Month.Month == 1);
        januaryCost!.Cost.Should().Be(7.50m);

        // February: use manufacture cost
        var februaryCost = costs.FirstOrDefault(c => c.Month.Month == 2);
        februaryCost!.Cost.Should().Be(15.00m);

        // March: use fallback ERP price
        var marchCost = costs.FirstOrDefault(c => c.Month.Month == 3);
        marchCost!.Cost.Should().Be(7.50m);
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public async Task GetCostsAsync_WithNullProductCode_SkipsProduct()
    {
        // Arrange
        var product = CreateCatalogProduct("");
        product.ProductCode = null!; // Null product code
        product.ErpPrice = CreateErpPrice(10m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        // Act
        var result = await _repository.GetCostsAsync();

        // Assert
        result.Should().BeEmpty(); // Product with null code should be skipped
    }

    [Fact]
    public async Task GetCostsAsync_WithEmptyProductCode_SkipsProduct()
    {
        // Arrange
        var product = CreateCatalogProduct("");
        product.ErpPrice = CreateErpPrice(10m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        // Act
        var result = await _repository.GetCostsAsync();

        // Assert
        result.Should().BeEmpty(); // Product with empty code should be skipped
    }

    [Fact]
    public async Task GetCostsAsync_WithProductCodesFilterNotMatching_ReturnsEmpty()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(10m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var productCodesToFilter = new List<string> { "TEST999", "OTHER001" };

        // Act
        var result = await _repository.GetCostsAsync(productCodes: productCodesToFilter);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCostsAsync_WithEmptyProductCodesFilter_ReturnsAllProducts()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            CreateCatalogProduct("TEST001"),
            CreateCatalogProduct("TEST002")
        };

        products.ForEach(p => p.ErpPrice = CreateErpPrice(10m));

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        var emptyProductCodes = new List<string>();

        // Act
        var result = await _repository.GetCostsAsync(productCodes: emptyProductCodes);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey("TEST001");
        result.Should().ContainKey("TEST002");
    }

    [Fact]
    public async Task GetCostsAsync_WhenCatalogRepositoryThrows_BubbleUpException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Database connection failed");
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.GetCostsAsync());

        exception.Should().Be(expectedException);

        // Verify error logging occurred
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error calculating material costs")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCostsAsync_WithCancellationToken_PassesToRepository()
    {
        // Arrange
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        // Act
        await _repository.GetCostsAsync(cancellationToken: cancellationToken);

        // Assert
        _mockCatalogRepository.Verify(x => x.GetAllAsync(cancellationToken), Times.Once);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task GetCostsAsync_LogsDebugInformation()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(10m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Calculating material costs from")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCostsAsync_LogsInformationSummary()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(10m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        // Act
        await _repository.GetCostsAsync();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Calculated material costs for")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCostsAsync_WithManufactureHistory_LogsTraceInformation()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        var manufactureRecord = CreateManufactureRecord(new DateTime(2024, 1, 15), 100, 1500m);
        product.ManufactureHistory = new List<ManufactureHistoryRecord> { manufactureRecord };

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using manufacture history for")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCostsAsync_WithErpFallback_LogsTraceInformation()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(12.50m);
        product.ManufactureHistory = new List<ManufactureHistoryRecord>(); // Empty

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 1, 1);
        var dateTo = new DateOnly(2024, 1, 31);

        // Act
        await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using ERP price fallback for")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region MonthlyCost Result Validation Tests

    [Fact]
    public async Task GetCostsAsync_MonthlyCostsHaveCorrectStructure()
    {
        // Arrange
        var product = CreateCatalogProduct("TEST001");
        product.ErpPrice = CreateErpPrice(25.75m);

        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });

        var dateFrom = new DateOnly(2024, 3, 1);
        var dateTo = new DateOnly(2024, 5, 31);

        // Act
        var result = await _repository.GetCostsAsync(dateFrom: dateFrom, dateTo: dateTo);

        // Assert
        result.Should().ContainKey("TEST001");
        var monthlyCosts = result["TEST001"];

        monthlyCosts.Should().HaveCount(3);
        monthlyCosts.Should().AllSatisfy(cost =>
        {
            cost.Month.Day.Should().Be(1); // Should always be first day of month
            cost.Cost.Should().Be(25.75m);
        });

        // Verify months are March, April, May
        var months = monthlyCosts.Select(c => c.Month.Month).OrderBy(x => x).ToList();
        months.Should().BeEquivalentTo(new[] { 3, 4, 5 });
    }

    #endregion
}