using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Domain.Features.Catalog;
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
    private readonly Mock<ICatalogRepository> _repository = new();

    public ProductEnrichmentCacheTests()
    {
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider
            .Setup(sp => sp.GetService(typeof(ICatalogRepository)))
            .Returns(_repository.Object);
    }

    private ProductEnrichmentCache Create(int ttlMinutes = 60) =>
        new(_scopeFactory.Object, Options.Create(new KnowledgeBaseOptions
        {
            ProductEnrichmentCacheTtlMinutes = ttlMinutes
        }));

    private void SetupRepository(params CatalogAggregate[] products)
    {
        _repository
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);
    }

    [Fact]
    public async Task GetProductLookupAsync_ReturnsOnlyProductAndGoodsTypes()
    {
        var product = new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product };
        var goods = new CatalogAggregate { ProductCode = "GDS001", ProductName = "Krém XYZ", Type = ProductType.Goods };
        // FindAsync is called with a filter — we simulate it already filtered
        SetupRepository(product, goods);

        var cache = Create();
        var lookup = await cache.GetProductLookupAsync();

        Assert.Equal(2, lookup.Count);
        Assert.True(lookup.ContainsKey("PRD001"));
        Assert.True(lookup.ContainsKey("GDS001"));
        Assert.Equal("Sérum ABC", lookup["PRD001"].ProductName);
        Assert.Null(lookup["PRD001"].Url);
    }

    [Fact]
    public async Task GetProductLookupAsync_WithinTtl_ReturnsCachedResult_RepositoryCalledOnce()
    {
        SetupRepository(new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product });
        var cache = Create(ttlMinutes: 60);

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _repository.Verify(
            r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductLookupAsync_AfterTtlExpiry_RefreshesFromRepository()
    {
        SetupRepository(new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product });
        var cache = Create(ttlMinutes: 0); // TTL = 0 → always expired

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _repository.Verify(
            r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
