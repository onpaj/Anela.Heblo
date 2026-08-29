### task: catalog-erp-stock-source-adapter

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityErpStockSourceAdapterTests
{
    private readonly Mock<IErpStockClient> _inner = new();

    private DataQualityErpStockSourceAdapter CreateAdapter() => new(_inner.Object);

    private void SetupErp(params ErpStock[] products) =>
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)products.ToList());

    [Fact]
    public async Task ListAsync_ProjectsProductCodeAndProductName()
    {
        // Arrange
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = 1 });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("P001");
        result[0].ProductName.Should().Be("Product 1");
    }

    [Theory]
    [InlineData(1, true)]   // Goods
    [InlineData(8, true)]   // Product
    [InlineData(3, false)]  // Material
    [InlineData(7, false)]  // SemiProduct
    [InlineData(99, false)] // Set
    [InlineData(0, false)]  // UNDEFINED
    public async Task ListAsync_MapsIsSellable_FromProductTypeId(int productTypeId, bool expectedSellable)
    {
        // Arrange
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = productTypeId });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.IsSellable.Should().Be(expectedSellable);
    }

    [Fact]
    public async Task ListAsync_WhenProductTypeIdIsNull_IsSellableIsFalse()
    {
        // Arrange
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = null });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.IsSellable.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList()
    {
        // Arrange
        SetupErp();

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityErpStockSourceAdapterTests"`
Expected: FAIL — build error, `DataQualityErpStockSourceAdapter` does not exist yet (CS0246).

- [ ] **Step 3: Write minimal implementation**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityErpStockSourceAdapter : IDqtErpStockSource
{
    private readonly IErpStockClient _inner;

    public DataQualityErpStockSourceAdapter(IErpStockClient inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken)
    {
        var products = await _inner.ListAsync(cancellationToken);
        return products
            .Select(p => new DqtErpStockItem
            {
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                IsSellable = p.ProductTypeId == (int)ProductType.Goods || p.ProductTypeId == (int)ProductType.Product,
            })
            .ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityErpStockSourceAdapterTests"`
Expected: `Passed! - Failed: 0, Passed: 9, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapterTests.cs backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs
git commit -m "Add Catalog-side DataQualityErpStockSourceAdapter implementing IDqtErpStockSource"
```

---

