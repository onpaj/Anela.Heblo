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
    public async Task GetProductLookupAsync_PredicateFiltersToProductAndGoodsOnly()
    {
        System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>? capturedPredicate = null;
        _repository
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>, CancellationToken>(
                (pred, _) => capturedPredicate = pred)
            .ReturnsAsync(new[]
            {
                new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product },
                new CatalogAggregate { ProductCode = "GDS001", ProductName = "Krém XYZ", Type = ProductType.Goods }
            });

        var cache = Create();
        var lookup = await cache.GetProductLookupAsync();

        // Verify data mapping
        Assert.Equal(2, lookup.Count);
        Assert.True(lookup.ContainsKey("PRD001"));
        Assert.Equal("Sérum ABC", lookup["PRD001"].ProductName);
        Assert.Null(lookup["PRD001"].Url);

        // Verify the predicate actually filters correctly
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(new CatalogAggregate { Type = ProductType.Product }));
        Assert.True(compiled(new CatalogAggregate { Type = ProductType.Goods }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.Material }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.SemiProduct }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.Set }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.UNDEFINED }));
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
