using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Cache;

public class MaterialCostCacheTests
{
    [Fact]
    public async Task GetCachedDataAsync_BeforeHydration_ReturnsEmptyData()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogRepoMock = new Mock<ICatalogRepository>();
        var loggerMock = new Mock<ILogger<MaterialCostCache>>();
        var options = Options.Create(new CostCacheOptions());

        var cache = new MaterialCostCache(memoryCache, catalogRepoMock.Object, loggerMock.Object, options);

        // Act
        var result = await cache.GetCachedDataAsync();

        // Assert
        Assert.False(result.IsHydrated);
        Assert.Empty(result.ProductCosts);
    }

    [Fact]
    public async Task RefreshAsync_StoresDataInCache()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogRepoMock = new Mock<ICatalogRepository>();
        var loggerMock = new Mock<ILogger<MaterialCostCache>>();
        var options = Options.Create(new CostCacheOptions());

        catalogRepoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        catalogRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        var cache = new MaterialCostCache(memoryCache, catalogRepoMock.Object, loggerMock.Object, options);

        // Act
        await cache.RefreshAsync();
        var result = await cache.GetCachedDataAsync();

        // Assert
        Assert.True(result.IsHydrated);
        Assert.NotNull(result.ProductCosts);
    }
}
