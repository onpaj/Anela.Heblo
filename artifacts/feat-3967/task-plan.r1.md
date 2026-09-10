# DataQuality-Catalog Module Boundary Decoupling for ProductPairingDqtComparer Implementation Plan

**Goal:** Remove `ProductPairingDqtComparer`'s direct dependency on Catalog-owned types (`IEshopStockClient`, `IErpStockClient`, `EshopStock`, `ErpStock`, `ProductType`) by introducing two DataQuality-owned contracts backed by Catalog-side adapters, and retire the now-resolved `DataQualityCatalogAllowlist` entries in `ModuleBoundariesTests.cs`.

**Architecture:** This is the third application of an already-proven pattern in this codebase (see `IStockOperationQuery`/`DataQualityStockOperationQueryAdapter`). DataQuality declares two narrow, consumption-shaped interfaces (`IDqtEshopStockSource`, `IDqtErpStockSource`) plus its own snapshot DTOs (`DqtEshopStockItem`, `DqtErpStockItem`) in its `Contracts/` folder. Catalog implements both via `internal sealed` adapters in its `Infrastructure/` folder that wrap the existing `IEshopStockClient`/`IErpStockClient` and map onto the DataQuality DTOs — the `ProductType` enum comparison (`IsSellable`) moves from the comparer into the ERP adapter, since it is Catalog domain knowledge. `ProductPairingDqtComparer` is rewired to depend only on the two new contracts; the boundary test allowlist entries for this violation are removed.

**Tech Stack:** .NET 8, C#, MediatR-free plain services, xUnit, Moq, FluentAssertions, Microsoft.Extensions.DependencyInjection.

---

## File Structure

| File | Kind | Responsibility |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs` | new | DataQuality-owned contract for eshop stock snapshots |
| `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs` | new | DataQuality-owned eshop snapshot DTO (class, not record) |
| `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs` | new | DataQuality-owned contract for ERP stock snapshots |
| `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs` | new | DataQuality-owned ERP snapshot DTO (class, not record) |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs` | new | Catalog-side adapter: `IEshopStockClient` → `IDqtEshopStockSource` |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs` | new | Catalog-side adapter: `IErpStockClient` → `IDqtErpStockSource`, computes `IsSellable` from `ProductType` |
| `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` | modify | Register the two new adapter bindings (provider owns DI wiring) |
| `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` | modify | Rewire constructor/locals to new contracts, delete `IsSellable` helper |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapterTests.cs` | new | Unit tests for the eshop adapter's field projection |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapterTests.cs` | new | Unit tests for the ERP adapter's field projection and `IsSellable` mapping |
| `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` | modify | Rebind mocks from `IEshopStockClient`/`IErpStockClient` to the new contracts |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | modify | Empty out `DataQualityCatalogAllowlist`, update its comment |

---

### task: dqt-eshop-stock-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs`

- [ ] **Step 1: Write the DataQuality-owned eshop snapshot DTO**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtEshopStockItem
{
    public string Code { get; set; }
    public string PairCode { get; set; }
    public string Name { get; set; }
}
```

- [ ] **Step 2: Write the DataQuality-owned eshop contract**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtEshopStockSource
{
    Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` — these two files add no forbidden-namespace references (no `using Anela.Heblo.Domain.Features.Catalog*`).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs
git commit -m "Add DataQuality-owned IDqtEshopStockSource contract and snapshot DTO"
```

---

### task: dqt-erp-stock-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs`

- [ ] **Step 1: Write the DataQuality-owned ERP snapshot DTO**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtErpStockItem
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public bool IsSellable { get; set; }
}
```

Note: `IsSellable` replaces the raw `ProductTypeId` — the `ProductType` enum comparison is Catalog domain knowledge and lives only in `DataQualityErpStockSourceAdapter` (see task `catalog-erp-stock-source-adapter`).

- [ ] **Step 2: Write the DataQuality-owned ERP contract**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtErpStockSource
{
    Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs
git commit -m "Add DataQuality-owned IDqtErpStockSource contract and snapshot DTO"
```

---

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

### task: catalog-module-di-registration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:61-66`

- [ ] **Step 1: Register the two new adapter bindings**

The file already has `using Anela.Heblo.Application.Features.DataQuality.Contracts;` (line 21) and `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` (line 8), so no new `using` is needed. Find this existing block (lines 61-66):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
        services.AddScoped<IMaterialLotStockQuery, DataQualityMaterialLotStockQueryAdapter>();
        // DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
        services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

Replace it with (appending the two new registrations to the same "DataQuality owns the query contracts" group, after the resilience registration):

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
        services.AddScoped<IMaterialLotStockQuery, DataQualityMaterialLotStockQueryAdapter>();
        // DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
        services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
        services.AddScoped<IDqtEshopStockSource, DataQualityEshopStockSourceAdapter>();
        services.AddScoped<IDqtErpStockSource, DataQualityErpStockSourceAdapter>();
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "Register IDqtEshopStockSource/IDqtErpStockSource adapters in CatalogModule"
```

---

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

### task: retire-dataquality-catalog-allowlist

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:128-144`

- [ ] **Step 1: Empty the resolved allowlist**

Find this block (the comment plus the `DataQualityCatalogAllowlist` declaration):

```csharp
    // Allowlist for DataQuality -> Catalog. Pre-existing ProductPairingDqtComparer references
    // are out of scope for the 2026-06-03 StockWriteBackDqtComparer decoupling.
    // Track follow-up: introduce DataQuality-owned IProductPairingQuery contract and Catalog-side
    // adapter that surfaces eshop/erp product snapshots without leaking Catalog types.
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",

        // Compiler-generated async state machines and lambdas for CompareAsync capture EshopStock.
        // The declaring-type check covers nested types (<CompareAsync>d__6, <<CompareAsync>b__6_1>d)
        // via this single parent entry.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock",
    };
```

Replace it with (mirroring the "Empty — ..." comment style already used for `LeafletAllowlist`/`ArticleAllowlist`):

```csharp
    // Allowlist for DataQuality -> Catalog. Empty — ProductPairingDqtComparer now consumes
    // the DataQuality-owned IDqtEshopStockSource/IDqtErpStockSource contracts; the Catalog
    // adapters (DataQualityEshopStockSourceAdapter, DataQualityErpStockSourceAdapter) live in
    // Catalog.Infrastructure and implement them there, so no DataQuality type needs to
    // reference Catalog directly.
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Run the architecture test to verify it still passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: `Passed!` — all `ModuleBoundaryRule` theory cases pass, including `"DataQuality -> Catalog"` with the now-empty allowlist (confirms `ProductPairingDqtComparer` no longer references any Catalog-namespaced type).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "Retire resolved DataQuality -> Catalog allowlist entries for ProductPairingDqtComparer"
```

---

### task: full-verification

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors — confirms `CatalogModule`, `ProductPairingDqtComparer`, and both new adapters compile together and no other file still references the old `ProductPairingDqtComparer` constructor signature.

- [ ] **Step 2: Run `dotnet format` verification**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: exits 0 — no formatting violations in any of the new/modified files.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Passed!` with 0 failed — this includes `ModuleBoundariesTests`, `ProductPairingDqtComparerTests`, `DataQualityEshopStockSourceAdapterTests`, `DataQualityErpStockSourceAdapterTests`, and every other pre-existing test (confirms no other test file references the old `IEshopStockClient`/`IErpStockClient`-based constructor of `ProductPairingDqtComparer`).

- [ ] **Step 4: Grep sanity check for leftover references**

Run: `grep -rn "IEshopStockClient\|IErpStockClient\|Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`
Expected: no output (empty) — confirms FR-4/FR-5 acceptance criteria that neither file references `EshopStock`, `ErpStock`, `IEshopStockClient`, `IErpStockClient`, `ProductType`, or any `Anela.Heblo.Domain.Features.Catalog*`/`Anela.Heblo.Application.Features.Catalog*` namespace.

No commit for this task — it is verification-only. If any step fails, return to the task whose file caused the failure, fix it, and re-run this task's steps from the top.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (DataQuality-owned contracts and DTOs) → `dqt-eshop-stock-contract`, `dqt-erp-stock-contract`.
- FR-2 (Catalog-side adapters, `IsSellable` mapping moved into the ERP adapter) → `catalog-eshop-stock-source-adapter`, `catalog-erp-stock-source-adapter`.
- FR-3 (DI registration in `CatalogModule`, `Scoped` lifetime, same comment block) → `catalog-module-di-registration`.
- FR-4 (rewire `ProductPairingDqtComparer`, remove Catalog `using`s and the `IsSellable` helper) → `rewire-product-pairing-comparer` Step 3.
- FR-5 (update allowlist and existing unit tests) → `rewire-product-pairing-comparer` Step 1 (tests) and `retire-dataquality-catalog-allowlist` (allowlist).
- NFR-1/NFR-2 (no perf/security impact) — no code changes required; the adapters do a single in-memory `Select().ToList()`, verified structurally in the adapter task implementations.
- NFR-3 (module isolation, enforced by `ModuleBoundariesTests`) → `retire-dataquality-catalog-allowlist` Step 2 confirms the rule still passes with an empty allowlist.
- Data Model / API Design sections describe no persisted or public-API changes — no task needed beyond the in-memory types already covered above.
- Out of Scope items (behavior changes, other comparers, combined `IProductPairingQuery`, changes to `IEshopStockClient`/`IErpStockClient`/`EshopStock`/`ErpStock`/`ProductType` themselves, performance optimization) — confirmed untouched by every task above; no task modifies those files.

**2. Placeholder scan:** No "TBD"/"implement later"/"add appropriate error handling" phrases anywhere above; every step carries the exact code or exact shell command with expected output. No task says "similar to Task N" — each task's code is fully spelled out even though the comparer and its test are logically paired with the two adapter tasks.

**3. Type consistency:** `DqtEshopStockItem` (`Code`, `PairCode`, `Name`) and `DqtErpStockItem` (`ProductCode`, `ProductName`, `IsSellable`) are defined once in `dqt-eshop-stock-contract`/`dqt-erp-stock-contract` and used with identical property names in `catalog-eshop-stock-source-adapter`, `catalog-erp-stock-source-adapter`, `rewire-product-pairing-comparer` (production code and test file). `IDqtEshopStockSource.ListAsync`/`IDqtErpStockSource.ListAsync` both return `Task<IReadOnlyList<T>>` everywhere they appear (contracts, adapters, comparer locals, test mocks) — the source asymmetry between `IEshopStockClient.ListAsync` (`List<EshopStock>`) and `IErpStockClient.ListAsync` (`IReadOnlyList<ErpStock>`) is normalized away at the adapter boundary in both `catalog-eshop-stock-source-adapter` and `catalog-erp-stock-source-adapter`, per the arch-review's explicit amendment. Constructor parameter order (`eshopStockSource, erpStockSource, resilienceService, logger`) matches between the production constructor in `rewire-product-pairing-comparer` Step 3 and the test's `CreateSut()` in Step 1.
