using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Eshop;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityEshopStockSourceAdapterTests
{
    private readonly Mock<IEshopStockClient> _inner = new();
    private readonly CatalogCacheStore _store;

    public DataQualityEshopStockSourceAdapterTests()
    {
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        _store = new CatalogCacheStore(
            new MemoryCache(new MemoryCacheOptions()),
            timeProvider.Object,
            Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = true }),
            Mock.Of<ICatalogMergeScheduler>(),
            Mock.Of<ILogger<CatalogCacheStore>>());
    }

    private DataQualityEshopStockSourceAdapter CreateAdapter() => new(_inner.Object, _store);

    private void SetupSupplies(params string[] codes) =>
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.Select(c => new EshopStock { Code = c }).ToList());

    [Fact]
    public async Task ListAsync_ProjectsCodeFromSuppliesAndNameFromProductCache()
    {
        // Arrange
        SetupSupplies("P001");
        _store.SetEshopProductData(new List<EshopProduct> { new() { Code = "P001", Name = "Product 1" } });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Code.Should().Be("P001");
        result[0].Name.Should().Be("Product 1");
    }

    [Fact]
    public async Task ListAsync_VariantNamePresent_AppendsItToName()
    {
        // Arrange
        SetupSupplies("P001");
        _store.SetEshopProductData(new List<EshopProduct>
        {
            new() { Code = "P001", Name = "Product 1", VariantName = "50 ml" },
        });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result[0].Name.Should().Be("Product 1 50 ml");
    }

    [Fact]
    public async Task ListAsync_ProductCacheEmpty_StillReturnsCodesWithEmptyNames()
    {
        // Arrange
        SetupSupplies("A", "B");

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Select(r => r.Code).Should().Equal("A", "B");
        result.Should().OnlyContain(r => r.Name == string.Empty);
    }

    [Fact]
    public async Task ListAsync_DuplicateProductCodesInCache_DoesNotThrow()
    {
        // Arrange
        SetupSupplies("P001");
        _store.SetEshopProductData(new List<EshopProduct>
        {
            new() { Code = "P001", Name = "First" },
            new() { Code = "P001", Name = "Second" },
        });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Single().Name.Should().Be("First");
    }

    [Fact]
    public async Task ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList()
    {
        // Arrange
        SetupSupplies();

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
