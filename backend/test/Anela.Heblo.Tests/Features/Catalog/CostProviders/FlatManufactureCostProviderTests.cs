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
    internal async Task ComputeAllCosts_WithSingleProduct_DistributesCostsCorrectly()
    {
        // Arrange
        var productCode = "PROD001";
        var dateFrom = new DateOnly(2025, 1, 1);
        var dateTo = new DateOnly(2025, 3, 31);

        // Manufacturing costs: Total 4500 CZK (1000 + 2000 + 1500)
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = new DateTime(2025, 1, 1), Cost = 1000m, Department = "VYROBA" },
                new() { Date = new DateTime(2025, 2, 1), Cost = 2000m, Department = "VYROBA" },
                new() { Date = new DateTime(2025, 3, 1), Cost = 1500m, Department = "VYROBA" }
            });

        // Product with manufacture history and difficulty = 2
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ManufactureHistory = new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = productCode },
                new() { Date = new DateTime(2025, 2, 15), Amount = 20, ProductCode = productCode },
                new() { Date = new DateTime(2025, 3, 15), Amount = 15, ProductCode = productCode }
            }
        };

        // Set difficulty = 2 for all dates
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 2, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
            },
            new DateTime(2025, 1, 1)
        );

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync(new List<string> { productCode });

        // Assert
        // Total weighted points = (10 * 2) + (20 * 2) + (15 * 2) = 90
        // Cost per point = 4500 / 90 = 50
        // Product cost = 2 * 50 = 100 (same for all months)
        Assert.True(result.ContainsKey(productCode));
        var costs = result[productCode];
        Assert.True(costs.Count >= 3); // At least 3 months
        Assert.All(costs.Take(3), cost => Assert.Equal(100m, cost.Cost));
    }

    [Fact]
    internal async Task ComputeAllCosts_WithMultipleProducts_DistributesCostsProportionally()
    {
        // Arrange
        var product1Code = "PROD001";
        var product2Code = "PROD002";

        // Total cost: 9000 CZK
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

        // PROD001: 10 pieces with difficulty 2 = 20 weighted points
        var product1 = new CatalogAggregate
        {
            ProductCode = product1Code,
            ManufactureHistory = new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = product1Code }
            }
        };
        product1.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = product1Code, DifficultyValue = 2, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
            },
            new DateTime(2025, 1, 1)
        );

        // PROD002: 20 pieces with difficulty 4 = 80 weighted points
        var product2 = new CatalogAggregate
        {
            ProductCode = product2Code,
            ManufactureHistory = new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 20, ProductCode = product2Code }
            }
        };
        product2.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = product2Code, DifficultyValue = 4, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
            },
            new DateTime(2025, 1, 1)
        );

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product1, product2 });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync(new List<string> { product1Code });

        // Assert
        // Total weighted points = 20 + 80 = 100
        // Cost per point = 9000 / 100 = 90
        // PROD001 cost = 20 * 90 = 1800
        Assert.True(result.ContainsKey(product1Code));
        var costs = result[product1Code];
        Assert.True(costs.Count >= 1);
        Assert.Equal(1800m, costs[0].Cost);
    }

    [Fact]
    internal async Task ComputeAllCosts_WithNoManufactureHistory_ReturnsZeroCosts()
    {
        // Arrange
        var productCode = "PROD001";

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

        // Product with NO manufacture history
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ManufactureHistory = new List<ManufactureHistoryRecord>()
        };

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync(new List<string> { productCode });

        // Assert
        // With no manufacture history, all months should have zero cost
        Assert.True(result.ContainsKey(productCode));
        var costs = result[productCode];
        Assert.All(costs, cost => Assert.Equal(0m, cost.Cost));
    }

    [Fact]
    internal async Task ComputeAllCosts_WithNoManufacturingCosts_ReturnsZeroCosts()
    {
        // Arrange
        var productCode = "PROD001";

        // NO manufacturing costs from ledger
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>());

        // Product with manufacture history
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            ManufactureHistory = new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = productCode }
            }
        };
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 1, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
            },
            new DateTime(2025, 1, 1)
        );

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync(new List<string> { productCode });

        // Assert
        // With no costs from ledger, cost should be zero
        Assert.True(result.ContainsKey(productCode));
        var costs = result[productCode];
        Assert.All(costs, cost => Assert.Equal(0m, cost.Cost));
    }

    [Fact]
    internal async Task ComputeAllCosts_WithProductNotManufacturedInPeriod_UsesDefaultDifficulty()
    {
        // Arrange
        var productCode = "PROD001";
        var otherProductCode = "PROD002";

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

        // Only other product was manufactured
        var product1 = new CatalogAggregate
        {
            ProductCode = productCode,
            ManufactureHistory = new List<ManufactureHistoryRecord>() // No history
        };
        product1.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 2, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
            },
            new DateTime(2025, 1, 1)
        );

        var product2 = new CatalogAggregate
        {
            ProductCode = otherProductCode,
            ManufactureHistory = new List<ManufactureHistoryRecord>
            {
                new() { Date = new DateTime(2025, 1, 15), Amount = 10, ProductCode = otherProductCode }
            }
        };
        product2.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = otherProductCode, DifficultyValue = 1, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
            },
            new DateTime(2025, 1, 1)
        );

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product1, product2 });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync(new List<string> { productCode });

        // Assert
        // PROD001 was not manufactured, so cost = 0
        Assert.True(result.ContainsKey(productCode));
        var costs = result[productCode];
        Assert.All(costs, cost => Assert.Equal(0m, cost.Cost));
    }

    private FlatManufactureCostProvider CreateProvider(
        IFlatManufactureCostCache? cache = null,
        ICatalogRepository? catalogRepository = null,
        ILedgerService? ledgerService = null,
        ILogger<FlatManufactureCostProvider>? logger = null,
        CostCacheOptions? options = null)
    {
        return new FlatManufactureCostProvider(
            cache ?? new FlatManufactureCostCache(new MemoryCache(new MemoryCacheOptions())),
            catalogRepository ?? Mock.Of<ICatalogRepository>(),
            ledgerService ?? Mock.Of<ILedgerService>(),
            logger ?? Mock.Of<ILogger<FlatManufactureCostProvider>>(),
            Options.Create(options ?? new CostCacheOptions())
        );
    }
}
