using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

public class ProductCatalogQueryServiceTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private ProductCatalogQueryService Create() => new(_repository.Object);

    [Fact]
    public async Task GetActiveProductsAsync_PredicateFiltersToProductAndGoodsOnly()
    {
        Expression<Func<CatalogAggregate, bool>>? capturedPredicate = null;
        _repository
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<CatalogAggregate, bool>>, CancellationToken>((pred, _) => capturedPredicate = pred)
            .ReturnsAsync(Array.Empty<CatalogAggregate>());

        var service = Create();
        await service.GetActiveProductsAsync();

        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(new CatalogAggregate { Type = ProductType.Product }));
        Assert.True(compiled(new CatalogAggregate { Type = ProductType.Goods }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.Material }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.SemiProduct }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.Set }));
        Assert.False(compiled(new CatalogAggregate { Type = ProductType.UNDEFINED }));
    }

    [Fact]
    public async Task GetActiveProductsAsync_MapsProductCodeNameAndUrl()
    {
        _repository
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product, Url = "https://example.com/prd001" },
                new CatalogAggregate { ProductCode = "GDS001", ProductName = "Krém XYZ", Type = ProductType.Goods, Url = null }
            });

        var service = Create();
        var result = await service.GetActiveProductsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("PRD001", result[0].ProductCode);
        Assert.Equal("Sérum ABC", result[0].ProductName);
        Assert.Equal("https://example.com/prd001", result[0].Url);
        Assert.Equal("GDS001", result[1].ProductCode);
        Assert.Equal("Krém XYZ", result[1].ProductName);
        Assert.Null(result[1].Url);
    }

    [Fact]
    public async Task GetActiveProductsAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var capturedToken = CancellationToken.None;
        _repository
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<CatalogAggregate, bool>>, CancellationToken>((_, t) => capturedToken = t)
            .ReturnsAsync(Array.Empty<CatalogAggregate>());

        var service = Create();
        await service.GetActiveProductsAsync(cts.Token);

        Assert.Equal(cts.Token, capturedToken);
    }
}
