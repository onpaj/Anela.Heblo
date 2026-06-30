### task: write-product-filter-service-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/ProductFilterServiceTests.cs`

**Context:**

The service under test is at:
`backend/src/Anela.Heblo.Application/Features/Analytics/Services/ProductFilterService.cs`

```csharp
public class ProductFilterService : IProductFilterService
{
    public async Task<List<AnalyticsProduct>> FilterProductsAsync(
        IAsyncEnumerable<AnalyticsProduct> productsStream,
        string? productFilter,
        string? categoryFilter,
        int maxProducts,
        CancellationToken cancellationToken = default)
    {
        var products = new List<AnalyticsProduct>();
        await foreach (var product in productsStream.WithCancellation(cancellationToken))
        {
            if (!PassesFilters(product, productFilter, categoryFilter))
                continue;
            products.Add(product);
            if (products.Count >= maxProducts)
                break;
        }
        return products;
    }

    public bool PassesFilters(AnalyticsProduct product, string? productFilter, string? categoryFilter)
    {
        if (!string.IsNullOrWhiteSpace(productFilter) &&
            !product.ProductName.Contains(productFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(categoryFilter) &&
            !string.Equals(product.ProductCategory, categoryFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
```

`AnalyticsProduct` required properties: `ProductCode` (string), `ProductName` (string), `Type` (AnalyticsProductType), `MarginAmount` (decimal), `SalesHistory` (List<SalesDataPoint>). `ProductCategory` is `string?` (nullable).

Existing test pattern from `MarginCalculatorTests.cs` — instantiate concrete class as a readonly field, use a static `MakeProduct` factory helper, FluentAssertions, `[Fact]` attributes.

- [ ] **Step 1: Write the complete test file**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/ProductFilterServiceTests.cs` with this content:

```csharp
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
        foreach (var p in products)
            yield return p;
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
        var product = MakeProduct("Night Serum");
        _service.PassesFilters(product, null, null).Should().BeTrue();
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
        // Filter is "Skin", category is "Skincare" — Equals not Contains, so this should fail
        var product = MakeProduct("Cream A", "Skincare");
        _service.PassesFilters(product, null, "Skin").Should().BeFalse();
    }

    [Fact]
    public void PassesFilters_NullCategoryFilter_SkipsCategoryCheck()
    {
        var product = MakeProduct("Cream A", "Bodycare");
        _service.PassesFilters(product, null, null).Should().BeTrue();
    }

    [Fact]
    public void PassesFilters_WhitespaceCategoryFilter_SkipsCategoryCheck()
    {
        var product = MakeProduct("Cream A", "Bodycare");
        _service.PassesFilters(product, null, "  ").Should().BeTrue();
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
        var stream = MakeStreamAsync(MakeProduct("Day Cream", "Skincare"));

        await _service.Awaiting(s => s.FilterProductsAsync(stream, null, null, 10, cancelledToken))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Build the backend to verify no compilation errors**

```bash
cd /home/user/worktrees/feature-3283-Coverage-Gap-Analytics-Productfilterservice-Passes
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore 2>&1 | tail -20
```

Expected: `Build succeeded.`

- [ ] **Step 3: Run only the new tests**

```bash
cd /home/user/worktrees/feature-3283-Coverage-Gap-Analytics-Productfilterservice-Passes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductFilterServiceTests" \
  --no-build 2>&1 | tail -30
```

Expected: All tests pass. Count: ~21 tests.

- [ ] **Step 4: Commit**

```bash
cd /home/user/worktrees/feature-3283-Coverage-Gap-Analytics-Productfilterservice-Passes
git add backend/test/Anela.Heblo.Tests/Features/Analytics/ProductFilterServiceTests.cs
git commit -m "test: add unit tests for ProductFilterService (coverage gap #3283)"
```

**Acceptance criteria:**
- `dotnet build` succeeds with no errors or warnings
- All ~21 new test methods pass
- `PassesFilters` branches covered: null name filter, whitespace name filter, name match same/different case, name no match, null category filter, whitespace category filter, category match same/different case, category no match, category substring mismatch, both filters null, both match, one match one not
- `FilterProductsAsync` branches covered: all-match stream, filtered stream, maxProducts cap (more than max, exactly max, fewer than max), empty stream, pre-cancelled token
