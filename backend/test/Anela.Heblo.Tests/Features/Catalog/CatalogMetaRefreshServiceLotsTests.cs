using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogMetaRefreshServiceLotsTests
{
    private readonly Mock<ILotsClient> _lotsClient = new();
    private readonly Mock<ILogger<CatalogMetaRefreshService>> _logger = new();
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMetaRefreshService _sut;

    public CatalogMetaRefreshServiceLotsTests()
    {
        var mergeScheduler = new Mock<ICatalogMergeScheduler>();
        _cacheStore = new CatalogCacheStore(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = false }),
            mergeScheduler.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        _sut = new CatalogMetaRefreshService(
            Mock.Of<ICatalogAttributesClient>(),
            _lotsClient.Object,
            Mock.Of<IProductPriceEshopClient>(),
            Mock.Of<IProductPriceErpClient>(),
            Mock.Of<IProductEshopUrlClient>(),
            Mock.Of<ICatalogResilienceService>(),
            _cacheStore,
            _logger.Object);
    }

    [Fact]
    public async Task RefreshLotsData_LogsCompletedEventOnceWithRowCount()
    {
        _lotsClient
            .Setup(x => x.GetAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogLot> { new(), new(), new() });

        await _sut.RefreshLotsData(CancellationToken.None);

        Assert.Equal(3, _cacheStore.GetLotsData().Count);
        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Catalog.LotsRefreshCompleted") && v.ToString()!.Contains("rows=3")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshLotsData_WhenClientThrows_PropagatesAndDoesNotLogCompleted()
    {
        _lotsClient
            .Setup(x => x.GetAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RefreshLotsData(CancellationToken.None));

        _logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Catalog.LotsRefreshCompleted")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
