# Persist BoM Custom Order to Abra Flexi — Refactor Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the local `ProductIngredientOrders` table and write ingredient ordering directly to Abra Flexi via the new `IBoMClient.SetItemsOrderAsync` SDK method, making Flexi the single source of truth for BoM order.

**Architecture:** Bump `Rem.FlexiBeeSDK.Client` to 0.1.138 to get `SetItemsOrderAsync` and `BoMItemFlexiDto.Order`. Add `Ingredient.Order` to the domain model, mapped from the Flexi BoM. Rewrite `UpdateProductCompositionOrderHandler` to call Flexi instead of the local DB, and simplify `GetProductCompositionHandler` to sort by `Ingredient.Order` directly.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions, `Rem.FlexiBeeSDK.Client` 0.1.138, EF Core (migration deletion only).

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IManufactureTemplateCache.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCache.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureTemplateService.cs` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` |
| Rewrite | `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` |
| **Delete** | `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductIngredientOrder.cs` |
| **Delete** | `backend/src/Anela.Heblo.Domain/Features/Catalog/IProductIngredientOrderRepository.cs` |
| **Delete** | `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderRepository.cs` |
| **Delete** | `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderConfiguration.cs` |
| **Delete** | `backend/src/Anela.Heblo.Persistence/Migrations/20260527073152_AddProductIngredientOrder.cs` |
| **Delete** | `backend/src/Anela.Heblo.Persistence/Migrations/20260527073152_AddProductIngredientOrder.Designer.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` |
| Rewrite | `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs` |
| Create | `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs` |
| Modify | `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs` |
| Modify | `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs` |
| Modify | `docs/superpowers/plans/2026-05-27-catalog-composition-custom-order.md` |

---

## Task 1: Bump Rem.FlexiBeeSDK.Client to 0.1.138

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`

> **BLOCKER CHECK FIRST:** Run `nuget search "Rem.FlexiBeeSDK.Client"` and confirm 0.1.138 (or higher) is listed. If the published version is still 0.1.135, stop — this plan cannot be merged until the SDK is published.

- [ ] **Step 1: Update the package reference**

Replace the current version and comment in the `.csproj`:

```xml
<!-- Before -->
<!-- SDK 0.1.135 required: ensure Rem.FlexiBeeSDK.Client@0.1.135 is published to NuGet before merging -->
<PackageReference Include="Rem.FlexiBeeSDK.Client" Version="0.1.135" />

<!-- After -->
<!-- SDK 0.1.138: adds IBoMClient.SetItemsOrderAsync and BoMItemFlexiDto.Order -->
<PackageReference Include="Rem.FlexiBeeSDK.Client" Version="0.1.138" />
```

- [ ] **Step 2: Restore and build to verify the bump compiles cleanly**

```bash
cd backend
dotnet restore
dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` — no missing member errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
git commit -m "chore: bump Rem.FlexiBeeSDK.Client to 0.1.138"
```

---

## Task 2: Add `Order` to `Ingredient` and map it from the Flexi BoM

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `FlexiManufactureTemplateServiceTests.cs` after the last existing `[Fact]`, before the private helper classes:

```csharp
[Fact]
public async Task GetManufactureTemplateAsync_MapsIngredientOrder_FromBoMItemFlexiDto()
{
    // Arrange
    var header = ManufactureTestData.CreateBoMItem(1, 1, 10, ingredient: null,
        parent: ManufactureTestData.SemiProducts.SilkBar);
    header.IngredientCode = $"code:{ManufactureTestData.SemiProducts.SilkBar.Code}";
    header.IngredientFullName = ManufactureTestData.SemiProducts.SilkBar.Name;

    var ing1 = ManufactureTestData.CreateBoMItem(10, 2, 5, ManufactureTestData.Materials.Bisabolol);
    ing1.Order = 2;
    var ing2 = ManufactureTestData.CreateBoMItem(20, 2, 3, ManufactureTestData.Materials.Glycerol);
    ing2.Order = 1;

    _mockBomClient
        .Setup(x => x.GetAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<BoMItemFlexiDto> { header, ing1, ing2 });

    SetupEmptyStock();

    // Act
    var template = await _service.GetManufactureTemplateAsync(
        ManufactureTestData.SemiProducts.SilkBar.Code, CancellationToken.None);

    // Assert
    template.Should().NotBeNull();
    var bisabolol = template!.Ingredients.Single(i => i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code);
    var glycerol = template.Ingredients.Single(i => i.ProductCode == ManufactureTestData.Materials.Glycerol.Code);
    bisabolol.Order.Should().Be(2);
    glycerol.Order.Should().Be(1);
}
```

Add the helper `SetupEmptyStock()` to the private helper section at the bottom if it doesn't already exist:

```csharp
private void SetupEmptyStock()
{
    _mockStockClient
        .Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((IReadOnlyList<ErpStock>)new List<ErpStock>());
}
```

- [ ] **Step 2: Run the test — it must fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "GetManufactureTemplateAsync_MapsIngredientOrder_FromBoMItemFlexiDto" -v n
```

Expected: **FAILED** — `Ingredient does not contain a definition for 'Order'` (or similar compile error because `Ingredient.Order` doesn't exist yet).

- [ ] **Step 3: Add `Order` to `Ingredient`**

Full file after change (`backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs`):

```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Manufacture;

public class Ingredient
{
    public int TemplateId { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public double OriginalAmount { get; set; }
    public decimal Price { get; set; }
    public ProductType ProductType { get; set; }
    public bool HasLots { get; set; }
    public bool HasExpiration { get; set; }
    /// <summary>
    /// Display order from Abra Flexi BoM (poradi). 0 means unordered.
    /// </summary>
    public int Order { get; set; }
}
```

- [ ] **Step 4: Map `Order` from `BoMItemFlexiDto` in `FlexiManufactureTemplateService`**

In `FlexiManufactureTemplateService.cs`, in the `FetchAsync` method, find the `Ingredient` object initializer inside the `ingredients.Select(...)` lambda (currently around line 113). Add `Order = s.Order` after `HasLots`:

```csharp
return new Ingredient()
{
    TemplateId = s.Id,
    ProductCode = code,
    ProductName = s.IngredientFullName,
    Amount = s.Amount,
    ProductType = ResolveProductType(s),
    HasLots = hasLotsByProductCode.TryGetValue(code, out var hasLots) && hasLots,
    HasExpiration = false,
    Order = s.Order,
};
```

- [ ] **Step 5: Run the test — it must pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "GetManufactureTemplateAsync_MapsIngredientOrder_FromBoMItemFlexiDto" -v n
```

Expected: **PASSED**.

- [ ] **Step 6: Run the full test suite to confirm no regressions**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests -v n
dotnet test test/Anela.Heblo.Tests -v n
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs
git commit -m "feat: add Order to Ingredient and map from BoMItemFlexiDto"
```

---

## Task 3: Add `Invalidate` to `IManufactureTemplateCache` and implement it

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IManufactureTemplateCache.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCache.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs` (update `PassthroughTemplateCache` and `HitOnlyCache`)

- [ ] **Step 1: Write the failing cache test**

Add at the end of the `[Fact]` list in `ManufactureTemplateCacheTests.cs` (before the closing `}`):

```csharp
[Fact]
public async Task Invalidate_AfterCacheHit_NextGetOrFetchInvokesFetcherAgain()
{
    // Arrange
    var sut = CreateSut();
    var fetcherCalls = 0;

    Func<CancellationToken, Task<ManufactureTemplate?>> fetch = _ =>
    {
        fetcherCalls++;
        return Task.FromResult<ManufactureTemplate?>(BuildTemplate());
    };

    // First call: populates cache
    await sut.GetOrFetchAsync("MAS001001M", fetch, CancellationToken.None);
    fetcherCalls.Should().Be(1);

    // Act
    sut.Invalidate("MAS001001M");

    // Second call: cache was invalidated, must invoke fetcher again
    await sut.GetOrFetchAsync("MAS001001M", fetch, CancellationToken.None);

    // Assert
    fetcherCalls.Should().Be(2, "cache was invalidated between calls");
}
```

- [ ] **Step 2: Run the test — it must fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "Invalidate_AfterCacheHit_NextGetOrFetchInvokesFetcherAgain" -v n
```

Expected: **FAILED** — compile error because `IManufactureTemplateCache` has no `Invalidate` method.

- [ ] **Step 3: Add `Invalidate` to `IManufactureTemplateCache`**

Full file after change:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IManufactureTemplateCache
{
    Task<ManufactureTemplate?> GetOrFetchAsync(
        string productCode,
        Func<CancellationToken, Task<ManufactureTemplate?>> fetch,
        CancellationToken cancellationToken);

    void Invalidate(string productCode);
}
```

- [ ] **Step 4: Implement `Invalidate` in `ManufactureTemplateCache`**

Add the following method to `ManufactureTemplateCache.cs`, after `TrySet`:

```csharp
public void Invalidate(string productCode)
{
    var key = CacheKeyPrefix + productCode;
    try
    {
        _cache.Remove(key);
        _logger.LogDebug("Manufacture template cache INVALIDATED for {ProductCode}", productCode);
    }
    catch (ObjectDisposedException)
    {
        // Cache disposed during shutdown — ignore.
    }
}
```

- [ ] **Step 5: Update `PassthroughTemplateCache` and `HitOnlyCache` in `FlexiManufactureTemplateServiceTests.cs`**

These private test-only classes implement `IManufactureTemplateCache` and need the new method. Add `Invalidate` to each:

```csharp
// Inside PassthroughTemplateCache:
public void Invalidate(string productCode) { /* no-op for pass-through */ }

// Inside HitOnlyCache:
public void Invalidate(string productCode) { /* no-op for hit-only test cache */ }
```

- [ ] **Step 6: Run the failing test — it must now pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "Invalidate_AfterCacheHit_NextGetOrFetchInvokesFetcherAgain" -v n
```

Expected: **PASSED**.

- [ ] **Step 7: Run all Flexi adapter tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests -v n
```

Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IManufactureTemplateCache.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/ManufactureTemplateCache.cs
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/ManufactureTemplateCacheTests.cs
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs
git commit -m "feat: add Invalidate to IManufactureTemplateCache"
```

---

## Task 4: Add `InvalidateTemplate` to `IFlexiManufactureTemplateService` and implement it

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureTemplateService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiManufactureTemplateServiceTests.cs` (verify compile — no new test needed; the behaviour is tested end-to-end in Task 5's adapter test)

> Note: `FlexiManufactureClientTests` uses `Mock<IFlexiManufactureTemplateService>` — Moq auto-mocks the new method, so no changes needed there.

- [ ] **Step 1: Add `InvalidateTemplate` to the interface**

Full file after change:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiManufactureTemplateService
{
    Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default);
    void InvalidateTemplate(string productCode);
}
```

- [ ] **Step 2: Implement `InvalidateTemplate` in `FlexiManufactureTemplateService`**

Add the following method to `FlexiManufactureTemplateService.cs`, after `GetManufactureTemplateAsync`:

```csharp
public void InvalidateTemplate(string productCode)
{
    _cache.Invalidate(productCode);
    _logger.LogDebug("Manufacture template invalidated via service for {ProductCode}", productCode);
}
```

- [ ] **Step 3: Verify build**

```bash
cd backend
dotnet build --no-incremental
```

Expected: `Build succeeded.`

- [ ] **Step 4: Run all tests to confirm no regressions**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests -v n
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureTemplateService.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs
git commit -m "feat: add InvalidateTemplate to IFlexiManufactureTemplateService"
```

---

## Task 5: Add `SetBomItemsOrderAsync` to `IManufactureClient` and implement in `FlexiManufactureClient`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`
- Create: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs`

- [ ] **Step 1: Write the failing tests in a new test class**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureClientOrderTests
{
    private readonly Mock<IBoMClient> _bomClientMock = new();
    private readonly Mock<IFlexiManufactureTemplateService> _templateServiceMock = new();
    private readonly Mock<ILogger<FlexiManufactureClient>> _loggerMock = new();
    private readonly FlexiManufactureClient _client;

    public FlexiManufactureClientOrderTests()
    {
        var stockMovementClient = new Mock<IStockItemsMovementClient>();
        var movementService = new FlexiManufactureDocumentService(
            new Mock<Anela.Heblo.Adapters.Flexi.Stock.IErpStockClient>().Object,
            stockMovementClient.Object);

        _client = new FlexiManufactureClient(
            _bomClientMock.Object,
            new Mock<IProductSetsClient>().Object,
            _loggerMock.Object,
            _templateServiceMock.Object,
            new FefoConsumptionAllocator(),
            new FlexiIngredientRequirementAggregator(_templateServiceMock.Object),
            new FlexiIngredientStockValidator(new Mock<Anela.Heblo.Adapters.Flexi.Stock.IErpStockClient>().Object, TimeProvider.System),
            new FlexiLotLoader(new Mock<Anela.Heblo.Adapters.Flexi.Manufacture.Internal.ILotsClient>().Object),
            movementService,
            stockMovementClient.Object,
            TimeProvider.System);
    }

    [Fact]
    public async Task SetBomItemsOrderAsync_DelegatesToBomClientAndInvalidatesCache()
    {
        // Arrange
        var items = new List<(int BoMItemId, int Order)>
        {
            (100, 1),
            (200, 2),
        };

        _bomClientMock
            .Setup(x => x.SetItemsOrderAsync(
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _client.SetBomItemsOrderAsync("PRD1", items, CancellationToken.None);

        // Assert: SDK called with the right items
        _bomClientMock.Verify(
            x => x.SetItemsOrderAsync(
                It.Is<IEnumerable<(int, int)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item2 == 1) &&
                    seq.Any(t => t.Item1 == 200 && t.Item2 == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: cache invalidated
        _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
    }

    [Fact]
    public async Task SetBomItemsOrderAsync_EmptyList_StillCallsBomClientAndInvalidatesCache()
    {
        // Arrange
        _bomClientMock
            .Setup(x => x.SetItemsOrderAsync(
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _client.SetBomItemsOrderAsync("PRD1", Array.Empty<(int, int)>(), CancellationToken.None);

        // Assert
        _bomClientMock.Verify(
            x => x.SetItemsOrderAsync(It.IsAny<IEnumerable<(int, int)>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
    }
}
```

- [ ] **Step 2: Run — must fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FlexiManufactureClientOrderTests" -v n
```

Expected: **FAILED** — compile error: `IManufactureClient` has no `SetBomItemsOrderAsync`, `IBoMClient` has no `SetItemsOrderAsync`.

- [ ] **Step 3: Add `SetBomItemsOrderAsync` to `IManufactureClient`**

Full file after change:

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureClient
{
    Task<SubmitManufactureClientResponse> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default);

    Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken cancellationToken = default);

    Task<ManufactureTemplate?> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken = default);
    Task<List<ProductPart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);

    Task<List<ManufactureErpDocumentItem>> GetErpDocumentItemsAsync(string documentCode, int? documentTypeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the display order for BoM line items to Abra Flexi and invalidates the template cache.
    /// </summary>
    /// <param name="productCode">Product code whose BoM is being reordered (used for cache invalidation).</param>
    /// <param name="items">Pairs of (BoMItemId, Order) to set. BoMItemId is <see cref="Ingredient.TemplateId"/>.</param>
    Task SetBomItemsOrderAsync(
        string productCode,
        IEnumerable<(int BoMItemId, int Order)> items,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement `SetBomItemsOrderAsync` in `FlexiManufactureClient`**

Add the following method to `FlexiManufactureClient.cs`, after `GetManufactureTemplateAsync`:

```csharp
public async Task SetBomItemsOrderAsync(
    string productCode,
    IEnumerable<(int BoMItemId, int Order)> items,
    CancellationToken cancellationToken = default)
{
    await _bomClient.SetItemsOrderAsync(items, cancellationToken);
    _templateService.InvalidateTemplate(productCode);
}
```

- [ ] **Step 5: Run the failing tests — they must pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FlexiManufactureClientOrderTests" -v n
```

Expected: **PASSED**.

- [ ] **Step 6: Run all tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests -v n
dotnet test test/Anela.Heblo.Tests -v n
```

Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs
git commit -m "feat: add SetBomItemsOrderAsync to IManufactureClient and FlexiManufactureClient"
```

---

## Task 6: Rewrite `UpdateProductCompositionOrderHandler`

**Files:**
- Rewrite: `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs`
- Rewrite: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs`

- [ ] **Step 1: Rewrite the handler tests first**

Replace the entire content of `UpdateProductCompositionOrderHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class UpdateProductCompositionOrderHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly Mock<ILogger<UpdateProductCompositionOrderHandler>> _loggerMock = new();
    private readonly UpdateProductCompositionOrderHandler _handler;

    public UpdateProductCompositionOrderHandlerTests()
    {
        _handler = new UpdateProductCompositionOrderHandler(
            _manufactureClientMock.Object,
            _loggerMock.Object);
    }

    private static ManufactureTemplate BuildTemplate(params (int TemplateId, string Code)[] ingredients)
    {
        return new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = ingredients.Select(i => new Ingredient
            {
                TemplateId = i.TemplateId,
                ProductCode = i.Code,
                ProductName = i.Code
            }).ToList()
        };
    }

    [Fact]
    public async Task Handle_ValidOrder_CallsSetBomItemsOrderAsync_WithCorrectPairs()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-B", SortOrder = 2 },
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(2);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item2 == 1) &&
                    seq.Any(t => t.Item1 == 200 && t.Item2 == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsZeroAndDoesNotCallSetOrder()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(0);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(It.IsAny<string>(), It.IsAny<IEnumerable<(int, int)>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RequestItemCodeNotInTemplate_IsSkippedWithWarning()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A")); // Only MAT-A in BoM
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-GHOST", SortOrder = 2 }, // not in BoM
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.UpdatedCount.Should().Be(1); // only MAT-A was mapped
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int)>>(seq =>
                    seq.Count() == 1 && seq.Single().Item1 == 100),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyOrder_CallsSetBomItemsOrderWithEmptySequence()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>()
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.UpdatedCount.Should().Be(0);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int)>>(seq => !seq.Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run tests — they must fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests \
  --filter "UpdateProductCompositionOrderHandlerTests" -v n
```

Expected: **FAILED** — `UpdateProductCompositionOrderHandler` does not accept `IManufactureClient`.

- [ ] **Step 3: Rewrite `UpdateProductCompositionOrderHandler`**

Replace the entire content of `UpdateProductCompositionOrderHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderHandler
    : IRequestHandler<UpdateProductCompositionOrderRequest, UpdateProductCompositionOrderResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly ILogger<UpdateProductCompositionOrderHandler> _logger;

    public UpdateProductCompositionOrderHandler(
        IManufactureClient manufactureClient,
        ILogger<UpdateProductCompositionOrderHandler> logger)
    {
        _manufactureClient = manufactureClient ?? throw new ArgumentNullException(nameof(manufactureClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateProductCompositionOrderResponse> Handle(
        UpdateProductCompositionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);
        if (template is null)
        {
            _logger.LogWarning(
                "Cannot set BoM order for {ProductCode}: manufacture template not found in Flexi",
                request.ProductCode);
            return new UpdateProductCompositionOrderResponse { UpdatedCount = 0 };
        }

        var codeToBomItemId = template.Ingredients.ToDictionary(
            i => i.ProductCode,
            i => i.TemplateId,
            StringComparer.Ordinal);

        var tuples = new List<(int BoMItemId, int Order)>();
        foreach (var item in request.Order)
        {
            if (!codeToBomItemId.TryGetValue(item.IngredientProductCode, out var bomItemId))
            {
                _logger.LogWarning(
                    "Ingredient {IngredientCode} not found in Flexi BoM for {ProductCode} — skipping",
                    item.IngredientProductCode, request.ProductCode);
                continue;
            }
            tuples.Add((bomItemId, item.SortOrder));
        }

        await _manufactureClient.SetBomItemsOrderAsync(request.ProductCode, tuples, cancellationToken);

        return new UpdateProductCompositionOrderResponse { UpdatedCount = tuples.Count };
    }
}
```

- [ ] **Step 4: Run tests — they must pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests \
  --filter "UpdateProductCompositionOrderHandlerTests" -v n
```

Expected: **PASSED**.

- [ ] **Step 5: Verify the whole test suite**

```bash
cd backend
dotnet test -v n
```

Expected: all tests pass (the old handler tests referencing `IProductIngredientOrderRepository` are now replaced).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs
git commit -m "feat: rewrite UpdateProductCompositionOrderHandler to write order to Flexi"
```

---

## Task 7: Rewrite `GetProductCompositionHandler` to sort by `Ingredient.Order`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs`

- [ ] **Step 1: Rewrite the composition handler tests**

Replace the entire content of `GetProductCompositionHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductCompositionHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly GetProductCompositionHandler _handler;

    public GetProductCompositionHandlerTests()
    {
        _handler = new GetProductCompositionHandler(_manufactureClientMock.Object);
    }

    private static ManufactureTemplate BuildTemplate(params (string Code, string Name, int Order)[] ingredients)
    {
        return new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = ingredients.Select(i => new Ingredient
            {
                ProductCode = i.Code,
                ProductName = i.Name,
                Amount = 10,
                Order = i.Order
            }).ToList()
        };
    }

    [Fact]
    public async Task Handle_SortsByIngredientOrder_Ascending()
    {
        // Arrange
        var template = BuildTemplate(
            ("A", "Alpha", 2),
            ("B", "Beta",  1),
            ("C", "Gamma", 3));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Select(i => i.ProductCode).Should().Equal("B", "A", "C");
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_ZeroOrderItems_AppearLastSortedByName()
    {
        // Arrange — Flexi returns 0 for unordered items
        var template = BuildTemplate(
            ("A", "Alpha",   2),
            ("B", "Unnamed", 0),
            ("C", "Zebra",   0),
            ("D", "Delta",   1));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert: ordered items first (1, 2), then unordered by name
        response.Ingredients.Select(i => i.ProductCode).Should().Equal("D", "A", "B", "C");
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsEmptyList()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AssignsContiguous1BasedDisplayOrder()
    {
        // Arrange
        var template = BuildTemplate(
            ("A", "Alpha", 1),
            ("B", "Beta",  2));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2);
    }
}
```

- [ ] **Step 2: Run tests — they must fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests \
  --filter "GetProductCompositionHandlerTests" -v n
```

Expected: **FAILED** — handler still has `IProductIngredientOrderRepository` dependency (compile error after we touch it), or logic doesn't sort by `Ingredient.Order`.

- [ ] **Step 3: Rewrite `GetProductCompositionHandler`**

Replace the entire content of `GetProductCompositionHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler
    : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureClient _manufactureClient;

    public GetProductCompositionHandler(IManufactureClient manufactureClient)
    {
        _manufactureClient = manufactureClient ?? throw new ArgumentNullException(nameof(manufactureClient));
    }

    public async Task<GetProductCompositionResponse> Handle(
        GetProductCompositionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(
            request.ProductCode,
            cancellationToken);

        if (template is null)
        {
            return new GetProductCompositionResponse { Ingredients = new List<IngredientDto>() };
        }

        // Sort by Flexi BoM order (poradi). Zero means unordered — push those last, then alphabetical.
        var sorted = template.Ingredients
            .OrderBy(i => i.Order == 0 ? int.MaxValue : i.Order)
            .ThenBy(i => i.ProductName)
            .Select((i, index) => new IngredientDto
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                Amount = i.Amount,
                Unit = "g",
                Order = index + 1
            })
            .ToList();

        return new GetProductCompositionResponse { Ingredients = sorted };
    }
}
```

- [ ] **Step 4: Run tests — they must pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests \
  --filter "GetProductCompositionHandlerTests" -v n
```

Expected: **PASSED**.

- [ ] **Step 5: Run all tests**

```bash
cd backend
dotnet test -v n
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs
git commit -m "refactor: GetProductCompositionHandler sorts by Ingredient.Order from Flexi"
```

---

## Task 8: Delete dead local-persistence code

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductIngredientOrder.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Catalog/IProductIngredientOrderRepository.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderRepository.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderConfiguration.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Migrations/20260527073152_AddProductIngredientOrder.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Migrations/20260527073152_AddProductIngredientOrder.Designer.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

> **Developer DB note:** If you have applied the `20260527073152_AddProductIngredientOrder` migration to your local Postgres, drop the table first:
>
> ```bash
> cd backend
> dotnet ef database update 20241230204610_AddStockUpOperation \  # replace with the migration that precedes AddProductIngredientOrder
>   --project src/Anela.Heblo.Persistence \
>   --startup-project src/Anela.Heblo.Api
> ```
>
> Get the preceding migration name via:
> ```bash
> dotnet ef migrations list --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.Api
> ```

- [ ] **Step 1: Delete the domain entity and interface**

```bash
rm backend/src/Anela.Heblo.Domain/Features/Catalog/ProductIngredientOrder.cs
rm backend/src/Anela.Heblo.Domain/Features/Catalog/IProductIngredientOrderRepository.cs
```

- [ ] **Step 2: Delete the persistence implementation files**

```bash
rm backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderRepository.cs
rm backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderConfiguration.cs
# If the directory is now empty, remove it:
rmdir backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder 2>/dev/null || true
```

- [ ] **Step 3: Delete the migration files**

```bash
rm backend/src/Anela.Heblo.Persistence/Migrations/20260527073152_AddProductIngredientOrder.cs
rm backend/src/Anela.Heblo.Persistence/Migrations/20260527073152_AddProductIngredientOrder.Designer.cs
```

- [ ] **Step 4: Remove `ProductIngredientOrders` DbSet from `ApplicationDbContext`**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, remove:

```csharp
// Remove this line (line 60):
public DbSet<ProductIngredientOrder> ProductIngredientOrders { get; set; } = null!;
```

Also remove the corresponding `using` for `Anela.Heblo.Domain.Features.Catalog` if it becomes unused after the removal. (Check — other types from that namespace may still be referenced; remove only if unused.)

- [ ] **Step 5: Remove `IProductIngredientOrderRepository` DI registration from `CatalogModule`**

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, remove:

```csharp
// Remove these two lines:
services.AddTransient<IProductIngredientOrderRepository, ProductIngredientOrderRepository>();
```

Also remove the now-unused `using` directives:

```csharp
// Remove:
using Anela.Heblo.Domain.Features.Catalog;          // if no longer used
using Anela.Heblo.Persistence.Catalog.ProductIngredientOrder;  // remove entirely
```

- [ ] **Step 6: Update `ApplicationDbContextModelSnapshot.cs`**

Open `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` and remove the entire `modelBuilder.Entity("Anela.Heblo.Domain.Features.Catalog.ProductIngredientOrder", ...)` block. The block starts with:

```csharp
modelBuilder.Entity("Anela.Heblo.Domain.Features.Catalog.ProductIngredientOrder", b =>
```

and ends with the matching closing `});`. Remove it entirely.

- [ ] **Step 7: Build the solution**

```bash
cd backend
dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` — all references to `ProductIngredientOrder` and `IProductIngredientOrderRepository` are gone.

- [ ] **Step 8: Run all tests**

```bash
cd backend
dotnet test -v n
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: delete ProductIngredientOrder persistence — Flexi is now source of truth"
```

---

## Task 9: `dotnet format` and final validation

- [ ] **Step 1: Format all C# code**

```bash
cd backend
dotnet format
```

Expected: exits 0. If any files are changed, stage them.

- [ ] **Step 2: Final build**

```bash
cd backend
dotnet build --no-incremental
```

Expected: `Build succeeded.`

- [ ] **Step 3: Full test run**

```bash
cd backend
dotnet test -v n
```

Expected: all tests pass.

- [ ] **Step 4: Frontend build and lint**

```bash
cd frontend
npm run build
npm run lint
```

Expected: both exit 0. The generated TypeScript client should be unchanged (API route/shape did not change).

- [ ] **Step 5: Commit if dotnet format changed files**

```bash
# Only if dotnet format changed files:
git add -A
git commit -m "chore: dotnet format"
```

---

## Task 10: Update documentation

**Files:**
- Modify: `docs/superpowers/plans/2026-05-27-catalog-composition-custom-order.md`

- [ ] **Step 1: Add an addendum to the existing feature plan**

Open `docs/superpowers/plans/2026-05-27-catalog-composition-custom-order.md` and append the following section at the end:

```markdown
---

## Update — 2026-05-27: Persistence moved to Abra Flexi

**Supersedes:** the "Persistence" section of this plan (§ Task 4 / local DB).

The `ProductIngredientOrders` Postgres table has been removed. Order is now persisted directly
to Abra Flexi via `IBoMClient.SetItemsOrderAsync` (added in `Rem.FlexiBeeSDK.Client` 0.1.138),
which sets the `poradi` field on each BoM line item.

`GET /composition` now reads order from `BoMItemFlexiDto.Order` — no overlay join needed.

Relevant files:
- `IManufactureClient.SetBomItemsOrderAsync` — domain entry point
- `FlexiManufactureClient.SetBomItemsOrderAsync` — adapter implementation
- `UpdateProductCompositionOrderHandler` — rewrites order to Flexi
- `GetProductCompositionHandler` — sorts by `Ingredient.Order` directly
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/plans/2026-05-27-catalog-composition-custom-order.md
git commit -m "docs: update composition order plan — persistence moved to Flexi"
```

---

## Self-Review

### Spec coverage

| Spec section | Covered by |
|---|---|
| Bump SDK | Task 1 |
| Add `Ingredient.Order`, map from Flexi | Task 2 |
| Add `SetBomItemsOrderAsync` to domain client | Task 5 |
| Cache invalidation after order write | Task 3, 4, 5 |
| Rewrite `UpdateProductCompositionOrderHandler` | Task 6 |
| Rewrite `GetProductCompositionHandler` | Task 7 |
| Delete dead persistence code | Task 8 |
| Tests: handler + adapter + cache | Tasks 2, 3, 5, 6, 7 |
| Frontend — no change needed | Verified in Task 9 |
| Docs update | Task 10 |

All spec requirements are covered.

### Placeholder scan

No TBD, TODO, "implement later", "add appropriate error handling", or vague instructions — all steps contain complete code.

### Type consistency

- `Ingredient.Order` (int) introduced in Task 2, mapped in `FlexiManufactureTemplateService`, used in `GetProductCompositionHandler` (Task 7) — consistent.
- `SetBomItemsOrderAsync(string productCode, IEnumerable<(int BoMItemId, int Order)> items, CancellationToken)` — same signature in `IManufactureClient` (Task 5), `FlexiManufactureClient` (Task 5), and called in `UpdateProductCompositionOrderHandler` (Task 6) — consistent.
- `IManufactureTemplateCache.Invalidate(string productCode)` — defined in Task 3, implemented in `ManufactureTemplateCache` (Task 3), called via `IFlexiManufactureTemplateService.InvalidateTemplate` (Task 4), which is called in `FlexiManufactureClient.SetBomItemsOrderAsync` (Task 5) — consistent call chain.
- `PassthroughTemplateCache` and `HitOnlyCache` updated in Task 3 to implement new interface method — consistent.
