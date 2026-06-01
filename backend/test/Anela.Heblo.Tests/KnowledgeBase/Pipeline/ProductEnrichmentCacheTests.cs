using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class ProductEnrichmentCacheTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IProductCatalogQueryService> _queryService = new();

    public ProductEnrichmentCacheTests()
    {
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider
            .Setup(sp => sp.GetService(typeof(IProductCatalogQueryService)))
            .Returns(_queryService.Object);
    }

    private ProductEnrichmentCache Create(int ttlMinutes = 60) =>
        new(_scopeFactory.Object, Options.Create(new KnowledgeBaseOptions
        {
            ProductEnrichmentCacheTtlMinutes = ttlMinutes
        }));

    private void SetupQueryService(params ProductCatalogEntry[] entries)
    {
        _queryService
            .Setup(s => s.GetActiveProductsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
    }

    [Fact]
    public async Task GetProductLookupAsync_MapsEntriesIntoLookup()
    {
        SetupQueryService(
            new ProductCatalogEntry { ProductCode = "PRD001", ProductName = "Sérum ABC", Url = null },
            new ProductCatalogEntry { ProductCode = "GDS001", ProductName = "Krém XYZ", Url = "https://example.com/gds001" });

        var cache = Create();
        var lookup = await cache.GetProductLookupAsync();

        Assert.Equal(2, lookup.Count);
        Assert.True(lookup.ContainsKey("PRD001"));
        Assert.Equal("Sérum ABC", lookup["PRD001"].ProductName);
        Assert.Null(lookup["PRD001"].Url);
        Assert.Equal("https://example.com/gds001", lookup["GDS001"].Url);
    }

    [Fact]
    public async Task GetProductLookupAsync_WithinTtl_ReturnsCachedResult_QueryServiceCalledOnce()
    {
        SetupQueryService(new ProductCatalogEntry { ProductCode = "PRD001", ProductName = "Sérum ABC" });
        var cache = Create(ttlMinutes: 60);

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _queryService.Verify(
            s => s.GetActiveProductsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductLookupAsync_AfterTtlExpiry_RefreshesFromQueryService()
    {
        SetupQueryService(new ProductCatalogEntry { ProductCode = "PRD001", ProductName = "Sérum ABC" });
        var cache = Create(ttlMinutes: 0); // TTL = 0 → always expired

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _queryService.Verify(
            s => s.GetActiveProductsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
