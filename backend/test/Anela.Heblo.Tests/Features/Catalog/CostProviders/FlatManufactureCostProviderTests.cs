using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Tests for FlatManufactureCostProvider.
/// Uses Collection attribute to ensure sequential execution due to static RefreshLock in the provider.
/// </summary>
[Collection("FlatManufactureCostProviderTests")]
public class FlatManufactureCostProviderTests
{
    private const int DefaultDifficultyValue = 1;

    [Fact]
    internal async Task ComputeAllCosts_WithSingleProduct_DistributesCostsCorrectly()
    {
        // Arrange
        var productCode = "PROD001";
        // Use relative dates to ensure test data falls within the dynamic date range
        var now = DateTime.UtcNow;
        var month1 = new DateTime(now.Year, now.Month, 1).AddMonths(-2);
        var month2 = month1.AddMonths(1);
        var month3 = month2.AddMonths(1);

        // Manufacturing costs: Total 4500 CZK (1000 + 2000 + 1500)
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month1, Cost = 1000m, Department = "VYROBA" },
                new() { Date = month2, Cost = 2000m, Department = "VYROBA" },
                new() { Date = month3, Cost = 1500m, Department = "VYROBA" }
            });

        // Product with manufacture history and difficulty = 2
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month1.AddDays(14), Amount = 10, ProductCode = productCode },
                new() { Date = month2.AddDays(14), Amount = 20, ProductCode = productCode },
                new() { Date = month3.AddDays(14), Amount = 15, ProductCode = productCode }
            }
        };

        // Set difficulty = 2 for all dates
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 2, ValidFrom = month1.AddYears(-1), ValidTo = null }
            },
            month1
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
        // Use relative dates to ensure test data falls within the dynamic date range
        var now = DateTime.UtcNow;
        var month1 = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        // Total cost: 9000 CZK
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month1, Cost = 9000m, Department = "VYROBA" }
            });

        // PROD001: 10 pieces with difficulty 2 = 20 weighted points
        var product1 = new CatalogAggregate
        {
            ProductCode = product1Code,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month1.AddDays(14), Amount = 10, ProductCode = product1Code }
            }
        };
        product1.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = product1Code, DifficultyValue = 2, ValidFrom = month1.AddYears(-1), ValidTo = null }
            },
            month1
        );

        // PROD002: 20 pieces with difficulty 4 = 80 weighted points
        var product2 = new CatalogAggregate
        {
            ProductCode = product2Code,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month1.AddDays(14), Amount = 20, ProductCode = product2Code }
            }
        };
        product2.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = product2Code, DifficultyValue = 4, ValidFrom = month1.AddYears(-1), ValidTo = null }
            },
            month1
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
        // PROD001 cost per piece = (20 * 90) / 10 = 180
        Assert.True(result.ContainsKey(product1Code));
        var costs = result[product1Code];
        Assert.True(costs.Count >= 1);
        Assert.Equal(180m, costs[0].Cost);
    }

    [Fact]
    internal async Task ComputeAllCosts_WithNoManufactureHistory_ReturnsZeroCosts()
    {
        // Arrange
        var productCode = "PROD001";
        // Use relative dates to ensure test data falls within the dynamic date range
        var now = DateTime.UtcNow;
        var month1 = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month1, Cost = 1000m, Department = "VYROBA" }
            });

        // Product with NO manufacture history
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>()
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
        // Use relative dates to ensure test data falls within the dynamic date range
        var now = DateTime.UtcNow;
        var month1 = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

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
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month1.AddDays(14), Amount = 10, ProductCode = productCode }
            }
        };
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 1, ValidFrom = month1.AddYears(-1), ValidTo = null }
            },
            month1
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
        // Use relative dates to ensure test data falls within the dynamic date range
        var now = DateTime.UtcNow;
        var month1 = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month1, Cost = 1000m, Department = "VYROBA" }
            });

        // Only other product was manufactured
        var product1 = new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>() // No history
        };
        product1.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 2, ValidFrom = month1.AddYears(-1), ValidTo = null }
            },
            month1
        );

        var product2 = new CatalogAggregate
        {
            ProductCode = otherProductCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month1.AddDays(14), Amount = 10, ProductCode = otherProductCode }
            }
        };
        product2.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = otherProductCode, DifficultyValue = 1, ValidFrom = month1.AddYears(-1), ValidTo = null }
            },
            month1
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

    [Fact]
    internal async Task ComputeAllCosts_WithSemiProductInCatalog_ExcludesSemiProductFromCostPool()
    {
        // Arrange
        var productCode = "PROD001";
        var semiProductCode = "SEMI001";
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month, Cost = 10000m, Department = "VYROBA" }
            });

        // Finished product: 100 pieces at difficulty 35 = 3500 weighted points
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month.AddDays(14), Amount = 100, ProductCode = productCode }
            }
        };
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 35, ValidFrom = month.AddYears(-1), ValidTo = null }
            },
            month
        );

        // Semi-product: 100 000 grams of bulk at the default difficulty of 1.
        // Counting it would swamp the denominator (100 000 vs 3500 points).
        var semiProduct = new CatalogAggregate
        {
            ProductCode = semiProductCode,
            Type = ProductType.SemiProduct,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month.AddDays(14), Amount = 100000, ProductCode = semiProductCode }
            }
        };

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product, semiProduct });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync();

        // Assert
        // Denominator = 3500 points (semi-product excluded) -> cost per point = 10000 / 3500
        // Product cost per piece = 35 * (10000 / 3500) = 100
        Assert.All(result[productCode], cost => Assert.Equal(100m, cost.Cost, 4));

        // The whole VYROBA pool lands on what is sold, so the semi-product itself carries no flat cost
        Assert.All(result[semiProductCode], cost => Assert.Equal(0m, cost.Cost));
    }

    [Fact]
    internal async Task ComputeAllCosts_WithPurchasedGoodsInCatalog_ExcludesGoodsFromCostPool()
    {
        // Arrange
        var productCode = "PROD001";
        var goodsCode = "GOODS001";
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month, Cost = 10000m, Department = "VYROBA" }
            });

        // Manufactured product: 100 pieces at difficulty 35 = 3500 weighted points
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month.AddDays(14), Amount = 100, ProductCode = productCode }
            }
        };
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 35, ValidFrom = month.AddYears(-1), ValidTo = null }
            },
            month
        );

        // Purchased goods are not made in house, so a stray receipt must not take a share of VYROBA
        var goods = new CatalogAggregate
        {
            ProductCode = goodsCode,
            Type = ProductType.Goods,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month.AddDays(14), Amount = 3500, ProductCode = goodsCode }
            }
        };

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product, goods });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync();

        // Assert
        // Denominator = 3500 points (goods excluded) -> 35 * (10000 / 3500) = 100 per piece
        Assert.All(result[productCode], cost => Assert.Equal(100m, cost.Cost, 4));
        Assert.All(result[goodsCode], cost => Assert.Equal(0m, cost.Cost));
    }

    [Fact]
    internal async Task ComputeAllCosts_WithSetProduct_ExcludesSetFromCostPool()
    {
        // Arrange
        var productCode = "PROD001";
        var setCode = "SET001";
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock.Setup(s => s.GetDirectCosts(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                "VYROBA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = month, Cost = 36000m, Department = "VYROBA" }
            });

        // 100 pieces at difficulty 35 = 3500 weighted points
        var product = new CatalogAggregate
        {
            ProductCode = productCode,
            Type = ProductType.Product,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month.AddDays(14), Amount = 100, ProductCode = productCode }
            }
        };
        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = productCode, DifficultyValue = 35, ValidFrom = month.AddYears(-1), ValidTo = null }
            },
            month
        );

        // A set is assembled from finished products rather than manufactured, so it takes no
        // share of the VYROBA pool - that labour is already carried by the products it is built
        // from. 50 pieces at difficulty 2 = 100 weighted points that must stay OUT of the denominator
        var set = new CatalogAggregate
        {
            ProductCode = setCode,
            Type = ProductType.Set,
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = month.AddDays(14), Amount = 50, ProductCode = setCode }
            }
        };
        set.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = setCode, DifficultyValue = 2, ValidFrom = month.AddYears(-1), ValidTo = null }
            },
            month
        );

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product, set });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = CreateProvider(
            catalogRepository: catalogRepositoryMock.Object,
            ledgerService: ledgerServiceMock.Object);

        // Act
        await provider.RefreshAsync();
        var result = await provider.GetCostsAsync();

        // Assert
        // Denominator = 3500 points (the set's 100 excluded) -> 35 * (36000 / 3500) = 360 per piece
        Assert.All(result[productCode], cost => Assert.Equal(360m, cost.Cost, 4));
        Assert.All(result[setCode], cost => Assert.Equal(0m, cost.Cost));
    }

    private FlatManufactureCostProvider CreateProvider(
        IFlatManufactureCostCache? cache = null,
        ICatalogRepository? catalogRepository = null,
        ILedgerService? ledgerService = null,
        ILogger<FlatManufactureCostProvider>? logger = null,
        DataSourceOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ICatalogRepository)))
            .Returns(catalogRepository ?? Mock.Of<ICatalogRepository>());

        return new FlatManufactureCostProvider(
            cache ?? new FlatManufactureCostCache(new MemoryCache(new MemoryCacheOptions())),
            serviceProviderMock.Object,
            ledgerService ?? Mock.Of<ILedgerService>(),
            logger ?? Mock.Of<ILogger<FlatManufactureCostProvider>>(),
            Options.Create(options ?? new DataSourceOptions()),
            timeProvider ?? TimeProvider.System
        );
    }
}
