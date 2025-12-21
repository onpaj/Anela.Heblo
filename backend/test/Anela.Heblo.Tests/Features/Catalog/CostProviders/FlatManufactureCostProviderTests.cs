using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

public class FlatManufactureCostProviderTests
{
    private const int DefaultDifficultyValue = 1;

    [Fact]
    public async Task GetHistoricalDifficulty_WithExistingSetting_ReturnsDifficultyValue()
    {
        // Arrange
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        var productCode = "TEST001";
        var referenceDate = new DateTime(2025, 6, 15);
        var expectedDifficulty = 5;

        difficultyRepoMock.Setup(r => r.FindAsync(productCode, referenceDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting
            {
                ProductCode = productCode,
                DifficultyValue = expectedDifficulty,
                ValidFrom = new DateTime(2025, 1, 1),
                ValidTo = null
            });

        var provider = CreateProvider(difficultyRepository: difficultyRepoMock.Object);

        // Act
        var result = await provider.GetHistoricalDifficultyAsync(productCode, referenceDate);

        // Assert
        Assert.Equal(expectedDifficulty, result);
    }

    [Fact]
    public async Task GetHistoricalDifficulty_WithNoSetting_ReturnsDefaultValue()
    {
        // Arrange
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        var productCode = "TEST002";
        var referenceDate = new DateTime(2025, 6, 15);

        difficultyRepoMock.Setup(r => r.FindAsync(productCode, referenceDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        var provider = CreateProvider(difficultyRepository: difficultyRepoMock.Object);

        // Act
        var result = await provider.GetHistoricalDifficultyAsync(productCode, referenceDate);

        // Assert
        Assert.Equal(DefaultDifficultyValue, result);
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithSingleProduct_DistributesCostsCorrectly()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 3, 31);

        // Manufacturing costs: 1000 CZK in Jan, 2000 CZK in Feb, 1500 CZK in Mar
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime from, DateTime to, string dept, CancellationToken ct) =>
            {
                var costs = new List<CostStatistics>();
                var currentMonth = new DateTime(from.Year, from.Month, 1);
                var endMonth = new DateTime(to.Year, to.Month, 1);

                while (currentMonth <= endMonth)
                {
                    decimal cost = currentMonth.Month switch
                    {
                        1 => 1000m,
                        2 => 2000m,
                        3 => 1500m,
                        _ => 0m
                    };
                    costs.Add(new CostStatistics
                    {
                        Date = currentMonth,
                        Cost = cost,
                        Department = "VYROBA"
                    });
                    currentMonth = currentMonth.AddMonths(1);
                }
                return costs;
            });

        // Manufacture history: 10 pieces in Jan, 20 pieces in Feb, 15 pieces in Mar
        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime from, DateTime to, string? code, CancellationToken ct) =>
            {
                return new List<ManufactureHistoryRecord>
                {
                    new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = productCode, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 },
                    new() { Date = new DateTime(2025, 2, 15), Amount = 20, ProductCode = productCode, DocumentNumber = "DOC002", PricePerPiece = 0, PriceTotal = 0 },
                    new() { Date = new DateTime(2025, 3, 15), Amount = 15, ProductCode = productCode, DocumentNumber = "DOC003", PricePerPiece = 0, PriceTotal = 0 }
                };
            });

        // Difficulty: constant value of 2 for all periods
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        difficultyRepoMock.Setup(r => r.FindAsync(productCode, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting
            {
                ProductCode = productCode,
                DifficultyValue = 2,
                ValidFrom = new DateTime(2024, 1, 1),
                ValidTo = null
            });

        var product = new CatalogAggregate
        {
            ProductCode = productCode
        };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object,
            difficultyRepository: difficultyRepoMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        // Total weighted points = (10 * 2) + (20 * 2) + (15 * 2) = 90
        // Total costs = 1000 + 2000 + 1500 = 4500
        // Cost per point = 4500 / 90 = 50
        // Cost per piece = 50 * 2 = 100 (difficulty is 2)
        Assert.Equal(3, result.Count);
        Assert.All(result, cost => Assert.Equal(100m, cost.Cost));
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithMultipleProducts_DistributesCostsProportionally()
    {
        // Arrange
        var product1Code = "PROD001";
        var product2Code = "PROD002";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        // Total cost: 9000 CZK in January
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 9000m, Department = "VYROBA" }
            });

        // Manufacture history:
        // PROD001: 10 pieces with difficulty 2 = 20 weighted points
        // PROD002: 20 pieces with difficulty 4 = 80 weighted points
        // Total: 100 weighted points
        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = product1Code, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 },
                new() { Date = new DateTime(2025, 1, 15), Amount = 20, ProductCode = product2Code, DocumentNumber = "DOC002", PricePerPiece = 0, PriceTotal = 0 }
            });

        // Difficulty settings
        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        difficultyRepoMock.Setup(r => r.FindAsync(product1Code, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting { ProductCode = product1Code, DifficultyValue = 2 });
        difficultyRepoMock.Setup(r => r.FindAsync(product2Code, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting { ProductCode = product2Code, DifficultyValue = 4 });

        var product1 = new CatalogAggregate { ProductCode = product1Code };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object,
            difficultyRepository: difficultyRepoMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product1, dateFrom, dateTo);

        // Assert
        // Cost per point = 9000 / 100 = 90
        // PROD001 cost = 90 * 2 = 180
        Assert.Single(result);
        Assert.Equal(180m, result[0].Cost);
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithNoManufactureHistory_ReturnsEmptyList()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 1000m, Department = "VYROBA" }
            });

        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());

        var product = new CatalogAggregate { ProductCode = productCode };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithNoManufacturingCosts_ReturnsZeroCosts()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = productCode, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 }
            });

        var product = new CatalogAggregate { ProductCode = productCode };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        Assert.Single(result);
        Assert.Equal(0m, result[0].Cost);
    }

    [Fact]
    public async Task CalculateFlatManufacturingCosts_WithProductNotManufacturedInPeriod_StillGetsCostBasedOnDifficulty()
    {
        // Arrange
        var productCode = "PROD001";
        var otherProductCode = "PROD002";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 1, 31);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 1000m, Department = "VYROBA" }
            });

        // Only other product was manufactured (10 pieces with difficulty 1 = 10 weighted points)
        var manufactureHistoryMock = new Mock<IManufactureHistoryClient>();
        manufactureHistoryMock.Setup(c => c.GetHistoryAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = otherProductCode, DocumentNumber = "DOC001", PricePerPiece = 0, PriceTotal = 0 }
            });

        var difficultyRepoMock = new Mock<IManufactureDifficultyRepository>();
        difficultyRepoMock.Setup(r => r.FindAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting { DifficultyValue = 1 });

        var product = new CatalogAggregate { ProductCode = productCode };

        var provider = CreateProvider(
            ledgerService: ledgerServiceMock.Object,
            manufactureHistoryClient: manufactureHistoryMock.Object,
            difficultyRepository: difficultyRepoMock.Object
        );

        // Act
        var result = await provider.CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo);

        // Assert
        // Cost per point = 1000 / 10 = 100
        // PROD001 gets cost based on its difficulty (1) even though not manufactured = 100 * 1 = 100
        Assert.Single(result);
        Assert.Equal(100m, result[0].Cost);
    }

    private FlatManufactureCostProvider CreateProvider(
        IFlatManufactureCostCache? cache = null,
        ICatalogRepository? catalogRepository = null,
        ILedgerService? ledgerService = null,
        IManufactureHistoryClient? manufactureHistoryClient = null,
        IManufactureDifficultyRepository? difficultyRepository = null,
        ILogger<FlatManufactureCostProvider>? logger = null,
        CostCacheOptions? options = null)
    {
        return new FlatManufactureCostProvider(
            cache ?? new FlatManufactureCostCache(new MemoryCache(new MemoryCacheOptions())),
            catalogRepository ?? Mock.Of<ICatalogRepository>(),
            ledgerService ?? Mock.Of<ILedgerService>(),
            manufactureHistoryClient ?? Mock.Of<IManufactureHistoryClient>(),
            difficultyRepository ?? Mock.Of<IManufactureDifficultyRepository>(),
            logger ?? Mock.Of<ILogger<FlatManufactureCostProvider>>(),
            Options.Create(options ?? new CostCacheOptions())
        );
    }
}
