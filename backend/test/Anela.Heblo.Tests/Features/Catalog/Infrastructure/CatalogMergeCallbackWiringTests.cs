using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public sealed class CatalogMergeCallbackWiringTests
{
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock;
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogMergeCallbackWiring _sut;

    public CatalogMergeCallbackWiringTests()
    {
        _schedulerMock = new Mock<ICatalogMergeScheduler>();

        _cacheStore = new CatalogCacheStore(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new CatalogCacheOptions()),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        _mergeService = new CatalogMergeService(
            _cacheStore,
            TimeProvider.System,
            Mock.Of<ILogger<CatalogMergeService>>());

        _sut = new CatalogMergeCallbackWiring(_schedulerMock.Object, _mergeService);
    }

    [Fact]
    public async Task StartAsync_RegistersMergeCallback_ExactlyOnce()
    {
        await _sut.StartAsync(CancellationToken.None);

        _schedulerMock.Verify(
            x => x.SetMergeCallback(It.IsAny<Func<CancellationToken, Task>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WiredCallback_ExecutesMergeAndPopulatesCache()
    {
        Func<CancellationToken, Task>? capturedCallback = null;
        _schedulerMock
            .Setup(x => x.SetMergeCallback(It.IsAny<Func<CancellationToken, Task>>()))
            .Callback<Func<CancellationToken, Task>>(cb => capturedCallback = cb);

        _cacheStore.SetErpStockData(new List<ErpStock>
        {
            new ErpStock { ProductCode = "WIRE001", Stock = 10 }
        });

        await _sut.StartAsync(CancellationToken.None);

        capturedCallback.Should().NotBeNull("StartAsync must register the callback");

        await capturedCallback!(CancellationToken.None);

        var current = _cacheStore.TryGetCurrent();
        current.Should().NotBeNull("merge callback must populate the cache");
        current!.Should().ContainSingle(p => p.ProductCode == "WIRE001");
    }
}
