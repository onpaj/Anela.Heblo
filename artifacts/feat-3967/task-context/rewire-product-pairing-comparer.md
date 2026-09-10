### task: rewire-product-pairing-comparer

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`

- [ ] **Step 1: Rewrite the test file to mock the new contracts (this will not compile until Step 3 lands)**

Replace the entire contents of `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class ProductPairingDqtComparerTests
{
    private readonly Mock<IDqtEshopStockSource> _eshopMock = new();
    private readonly Mock<IDqtErpStockSource> _erpMock = new();
    private readonly Mock<IDqtResilienceService> _resilienceMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    public ProductPairingDqtComparerTests()
    {
        // Pass-through resilience: invoke the inner operation directly.
        _resilienceMock
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<DqtEshopStockItem>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IReadOnlyList<DqtEshopStockItem>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        _resilienceMock
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<DqtErpStockItem>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IReadOnlyList<DqtErpStockItem>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));
    }

    private ProductPairingDqtComparer CreateSut() =>
        new(_eshopMock.Object, _erpMock.Object, _resilienceMock.Object, NullLogger<ProductPairingDqtComparer>.Instance);

    private void SetupEshop(params DqtEshopStockItem[] products) =>
        _eshopMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DqtEshopStockItem>)products.ToList());

    private void SetupErp(params DqtErpStockItem[] products) =>
        _erpMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DqtErpStockItem>)products.ToList());

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllProductsPaired()
    {
        // Arrange
        SetupEshop(new DqtEshopStockItem { Code = "P001", PairCode = "", Name = "Product 1" });
        SetupErp(new DqtErpStockItem { ProductCode = "P001", ProductName = "Product 1", IsSellable = true }); // Goods

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp()
    {
        // Arrange
        SetupEshop(new DqtEshopStockItem { Code = "ESHOP_ONLY", PairCode = "", Name = "Eshop Only" });
        SetupErp(); // Empty ERP

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("ESHOP_ONLY");
        ((ProductPairingMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(ProductPairingMismatch.MissingInErp);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp()
    {
        // Arrange
        SetupEshop(new DqtEshopStockItem { Code = "ESHOP001", PairCode = "ERP001", Name = "Pair Code Product" });
        SetupErp(); // ERP001 not in ERP

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        var mismatch = (ProductPairingMismatch)result.Mismatches.Single().MismatchCode;
        mismatch.Should().HaveFlag(ProductPairingMismatch.MissingInErp);
        mismatch.Should().HaveFlag(ProductPairingMismatch.PairCodeUnresolved);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts()
    {
        // Arrange
        SetupEshop(); // Empty Shoptet
        SetupErp(
            new DqtErpStockItem { ProductCode = "PROD001", ProductName = "Sellable", IsSellable = true },  // Product
            new DqtErpStockItem { ProductCode = "MAT001", ProductName = "Material", IsSellable = false }   // Material, not sellable
        );

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert — only PROD001 flagged; MAT001 is non-sellable and must be ignored
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("PROD001");
        ((ProductPairingMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(ProductPairingMismatch.MissingInShoptet);
    }

    [Fact]
    public async Task CompareAsync_WrapsBothListCalls_WithResilience()
    {
        // Arrange
        SetupEshop(new DqtEshopStockItem { Code = "P001", PairCode = "", Name = "Product 1" });
        SetupErp(new DqtErpStockItem { ProductCode = "P001", ProductName = "Product 1", IsSellable = true });

        // Act
        _ = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        _resilienceMock.Verify(r => r.ExecuteWithResilienceAsync(
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<DqtEshopStockItem>>>>(),
            "ProductPairingDqtComparer.EshopList",
            It.IsAny<CancellationToken>()), Times.Once);

        _resilienceMock.Verify(r => r.ExecuteWithResilienceAsync(
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<DqtErpStockItem>>>>(),
            "ProductPairingDqtComparer.ErpList",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to build**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductPairingDqtComparerTests"`
Expected: FAIL — build error (CS1503/CS7036): `ProductPairingDqtComparer`'s constructor does not accept `IDqtEshopStockSource`/`IDqtErpStockSource` yet.

- [ ] **Step 3: Rewrite the comparer to depend on the new contracts**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IDqtEshopStockSource _eshopStockSource;
    private readonly IDqtErpStockSource _erpStockSource;
    private readonly IDqtResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IDqtEshopStockSource eshopStockSource,
        IDqtErpStockSource erpStockSource,
        IDqtResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockSource = eshopStockSource;
        _erpStockSource = erpStockSource;
        _resilienceService = resilienceService;
        _logger = logger;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Date range is intentionally unused — product pairing is a current-state snapshot
        IReadOnlyList<DqtEshopStockItem> eshopProducts;
        try
        {
            eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
                async cancellationToken => await _eshopStockSource.ListAsync(cancellationToken),
                "ProductPairingDqtComparer.EshopList",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProductPairingDqtComparer failed to fetch eshop products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
                "ProductPairingDqtComparer.EshopList",
                ex.GetType().Name);
            throw;
        }

        IReadOnlyList<DqtErpStockItem> erpProducts;
        try
        {
            erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
                async cancellationToken => await _erpStockSource.ListAsync(cancellationToken),
                "ProductPairingDqtComparer.ErpList",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProductPairingDqtComparer failed to fetch ERP products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
                "ProductPairingDqtComparer.ErpList",
                ex.GetType().Name);
            throw;
        }

        var sellableErpProducts = erpProducts.Where(p => p.IsSellable).ToList();

        var erpCodeSet = sellableErpProducts
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All Shoptet identifiers (Code + PairCode) used when checking ERP → Shoptet direction
        var shoptetIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in eshopProducts)
        {
            shoptetIdentifiers.Add(p.Code);
            if (!string.IsNullOrWhiteSpace(p.PairCode))
                shoptetIdentifiers.Add(p.PairCode);
        }

        var mismatches = new List<DriftMismatch>();

        // Check A: each Shoptet product must resolve to an ERP code
        foreach (var eshopProduct in eshopProducts)
        {
            var hasPairCode = !string.IsNullOrWhiteSpace(eshopProduct.PairCode);
            var resolvedCode = hasPairCode ? eshopProduct.PairCode : eshopProduct.Code;

            if (erpCodeSet.Contains(resolvedCode))
                continue;

            var mismatch = ProductPairingMismatch.MissingInErp;
            if (hasPairCode)
                mismatch |= ProductPairingMismatch.PairCodeUnresolved;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = eshopProduct.Code,
                MismatchCode = (int)mismatch,
                ShoptetValue = eshopProduct.Name,
                HebloValue = null,
                Details = hasPairCode
                    ? $"Shoptet product '{eshopProduct.Code}' PairCode '{eshopProduct.PairCode}' not found in ERP"
                    : $"Shoptet product '{eshopProduct.Code}' not found in ERP"
            });
        }

        // Check B: each sellable ERP product must appear in Shoptet
        foreach (var erpProduct in sellableErpProducts)
        {
            if (shoptetIdentifiers.Contains(erpProduct.ProductCode))
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = erpProduct.ProductCode,
                MismatchCode = (int)ProductPairingMismatch.MissingInShoptet,
                HebloValue = erpProduct.ProductName,
                ShoptetValue = null,
                Details = $"Sellable ERP product '{erpProduct.ProductCode}' not in Shoptet catalog"
            });
        }

        var totalChecked = shoptetIdentifiers
            .Union(erpCodeSet, StringComparer.OrdinalIgnoreCase)
            .Count();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = totalChecked };
    }
}
```

Note what was removed relative to the original file: `using Anela.Heblo.Domain.Features.Catalog;`, `using Anela.Heblo.Domain.Features.Catalog.Stock;`, and the private `static bool IsSellable(ErpStock product)` helper (its logic now lives in `DataQualityErpStockSourceAdapter`, computed into `DqtErpStockItem.IsSellable`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductPairingDqtComparerTests"`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs
git commit -m "Rewire ProductPairingDqtComparer onto IDqtEshopStockSource/IDqtErpStockSource"
```

---

