### task: catalog-eshop-stock-source-adapter

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityEshopStockSourceAdapterTests
{
    private readonly Mock<IEshopStockClient> _inner = new();

    private DataQualityEshopStockSourceAdapter CreateAdapter() => new(_inner.Object);

    [Fact]
    public async Task ListAsync_ProjectsCodePairCodeAndName()
    {
        // Arrange
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>
            {
                new EshopStock { Code = "P001", PairCode = "ERP001", Name = "Product 1" },
            });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Code.Should().Be("P001");
        result[0].PairCode.Should().Be("ERP001");
        result[0].Name.Should().Be("Product 1");
    }

    [Fact]
    public async Task ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList()
    {
        // Arrange
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>());

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ProjectsMultipleProductsInOrder()
    {
        // Arrange
        _inner.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>
            {
                new EshopStock { Code = "A", PairCode = "", Name = "Alpha" },
                new EshopStock { Code = "B", PairCode = "B-ERP", Name = "Beta" },
            });

        // Act
        var result = await CreateAdapter().ListAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Code.Should().Be("A");
        result[1].Code.Should().Be("B");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityEshopStockSourceAdapterTests"`
Expected: FAIL — build error, `DataQualityEshopStockSourceAdapter` does not exist yet (CS0246).

- [ ] **Step 3: Write minimal implementation**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityEshopStockSourceAdapter : IDqtEshopStockSource
{
    private readonly IEshopStockClient _inner;

    public DataQualityEshopStockSourceAdapter(IEshopStockClient inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken)
    {
        var products = await _inner.ListAsync(cancellationToken);
        return products
            .Select(p => new DqtEshopStockItem { Code = p.Code, PairCode = p.PairCode, Name = p.Name })
            .ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityEshopStockSourceAdapterTests"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapterTests.cs backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs
git commit -m "Add Catalog-side DataQualityEshopStockSourceAdapter implementing IDqtEshopStockSource"
```

---

