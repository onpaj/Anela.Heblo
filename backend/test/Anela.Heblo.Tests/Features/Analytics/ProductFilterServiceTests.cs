using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class ProductFilterServiceTests
{
    private readonly ProductFilterService _service = new();

    private static AnalyticsProduct MakeProduct(string name, string? category = null) =>
        new()
        {
            ProductCode = "TEST",
            ProductName = name,
            Type = AnalyticsProductType.Product,
            ProductCategory = category,
            MarginAmount = 0m,
            SalesHistory = []
        };

    private static async IAsyncEnumerable<AnalyticsProduct> MakeStreamAsync(
        params AnalyticsProduct[] products)
    {
        await Task.CompletedTask;
        foreach (var p in products)
            yield return p;
    }

    private static async IAsyncEnumerable<AnalyticsProduct> MakeCancellableStreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        params AnalyticsProduct[] products)
    {
        await Task.CompletedTask;
        foreach (var p in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return p;
        }
    }

    // --- PassesFilters: name filter ---

    [Fact]
    public void PassesFilters_NameMatchSameCase_ReturnsTrue()
    {
        var product = MakeProduct("Day Cream SPF30");
        _service.PassesFilters(product, "Cream", null).Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_NameMatchDifferentCase_ReturnsTrue()
    {
        var product = MakeProduct("Day Cream SPF30");
        _service.PassesFilters(product, "cream", null).Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_NameNoMatch_ReturnsFalse()
    {
        var product = MakeProduct("Night Serum");
        _service.PassesFilters(product, "Cream", null).Should().BeFalse();
    }

    [Fact]
    public void PassesFilters_NullProductFilter_SkipsNameCheck()
    {
        var product = MakeProduct("Night Serum", "Skincare");
        _service.PassesFilters(product, null, "Skincare").Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_WhitespaceProductFilter_SkipsNameCheck()
    {
        var product = MakeProduct("Night Serum");
        _service.PassesFilters(product, "   ", null).Should().BeTrue();
    }

    // --- PassesFilters: category filter ---

    [Fact]
    public void PassesFilters_CategoryMatchSameCase_ReturnsTrue()
    {
        var product = MakeProduct("Cream A", "Skincare");
        _service.PassesFilters(product, null, "Skincare").Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_CategoryMatchDifferentCase_ReturnsTrue()
    {
        var product = MakeProduct("Cream A", "Skincare");
        _service.PassesFilters(product, null, "skincare").Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_CategoryNoMatch_ReturnsFalse()
    {
        var product = MakeProduct("Cream A", "Bodycare");
        _service.PassesFilters(product, null, "Skincare").Should().BeFalse();
    }

    [Fact]
    public void PassesFilters_CategoryFilterIsSubstringOfCategory_ReturnsFalse()
    {
        var product = MakeProduct("Cream A", "Skincare");
        _service.PassesFilters(product, null, "Skin").Should().BeFalse();
    }

    [Fact]
    public void PassesFilters_NullCategoryFilter_SkipsCategoryCheck()
    {
        var product = MakeProduct("Cream A", "Bodycare");
        _service.PassesFilters(product, "cream", null).Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_WhitespaceCategoryFilter_SkipsCategoryCheck()
    {
        var product = MakeProduct("Cream A", "Bodycare");
        _service.PassesFilters(product, null, "  ").Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_NullProductCategory_WithCategoryFilter_ReturnsFalse()
    {
        var product = MakeProduct("Cream A", category: null);
        _service.PassesFilters(product, null, "Skincare").Should().BeFalse();
    }

    // --- PassesFilters: combined filters ---

    [Fact]
    public void PassesFilters_BothFiltersMatch_ReturnsTrue()
    {
        var product = MakeProduct("Day Cream", "Skincare");
        _service.PassesFilters(product, "cream", "Skincare").Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_NameMatchCategoryMismatch_ReturnsFalse()
    {
        var product = MakeProduct("Day Cream", "Bodycare");
        _service.PassesFilters(product, "cream", "Skincare").Should().BeFalse();
    }

    [Fact]
    public void PassesFilters_CategoryMatchNameMismatch_ReturnsFalse()
    {
        var product = MakeProduct("Night Serum", "Skincare");
        _service.PassesFilters(product, "cream", "Skincare").Should().BeFalse();
    }

    [Fact]
    public void PassesFilters_BothFiltersNull_ReturnsTrue()
    {
        var product = MakeProduct("Night Serum", "Bodycare");
        _service.PassesFilters(product, null, null).Should().BeTrue();
    }

    // --- FilterProductsAsync: filtering ---

    [Fact]
    public async Task FilterProductsAsync_FiltersOutNonMatchingProducts()
    {
        var stream = MakeStreamAsync(
            MakeProduct("Day Cream", "Skincare"),
            MakeProduct("Night Serum", "Skincare"),
            MakeProduct("Body Lotion", "Bodycare"),
            MakeProduct("Eye Cream", "Skincare"),
            MakeProduct("Toner", "Bodycare"));

        var result = await _service.FilterProductsAsync(stream, "cream", null, 100);

        result.Should().HaveCount(2);
        result[0].ProductName.Should().Be("Day Cream");
        result[1].ProductName.Should().Be("Eye Cream");
    }

    [Fact]
    public async Task FilterProductsAsync_NoFilters_ReturnsAllUpToMax()
    {
        var products = Enumerable.Range(1, 5).Select(i => MakeProduct($"Product {i}")).ToArray();
        var stream = MakeStreamAsync(products);

        var result = await _service.FilterProductsAsync(stream, null, null, 100);

        result.Should().HaveCount(5);
    }

    // --- FilterProductsAsync: maxProducts cap ---

    [Fact]
    public async Task FilterProductsAsync_MoreItemsThanMax_CapsAtMaxProducts()
    {
        var products = Enumerable.Range(1, 10).Select(i => MakeProduct($"Product {i}")).ToArray();
        var stream = MakeStreamAsync(products);

        var result = await _service.FilterProductsAsync(stream, null, null, 3);

        result.Should().HaveCount(3);
        result[0].ProductName.Should().Be("Product 1");
        result[1].ProductName.Should().Be("Product 2");
        result[2].ProductName.Should().Be("Product 3");
    }

    [Fact]
    public async Task FilterProductsAsync_ExactlyMaxItems_ReturnsAllItems()
    {
        var products = Enumerable.Range(1, 3).Select(i => MakeProduct($"Product {i}")).ToArray();
        var stream = MakeStreamAsync(products);

        var result = await _service.FilterProductsAsync(stream, null, null, 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task FilterProductsAsync_FewerItemsThanMax_ReturnsAllItems()
    {
        var products = Enumerable.Range(1, 2).Select(i => MakeProduct($"Product {i}")).ToArray();
        var stream = MakeStreamAsync(products);

        var result = await _service.FilterProductsAsync(stream, null, null, 3);

        result.Should().HaveCount(2);
    }

    // --- FilterProductsAsync: empty stream ---

    [Fact]
    public async Task FilterProductsAsync_EmptyStream_ReturnsEmptyList()
    {
        var stream = MakeStreamAsync();

        var result = await _service.FilterProductsAsync(stream, "cream", "Skincare", 10);

        result.Should().BeEmpty();
    }

    // --- FilterProductsAsync: cancellation ---

    [Fact]
    public async Task FilterProductsAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var cancelledToken = new CancellationToken(canceled: true);
        var stream = MakeCancellableStreamAsync(products: [MakeProduct("Day Cream", "Skincare")]);

        await _service.Awaiting(s => s.FilterProductsAsync(stream, null, null, 10, cancelledToken))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
