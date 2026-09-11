# Implementation Plan: Batch Catalog Lookup for Gift Package Manufacture Ingredients

## Feature Name
Batch Catalog Lookup for Gift Package Manufacture Ingredients

## Goal
Eliminate the N+1 `ILogisticsCatalogSource.GetCatalogItemAsync` loop in `GiftPackageManufactureService.GetGiftPackageDetailAsync` by adding a batch lookup method to `ILogisticsCatalogSource`, implementing it in `LogisticsCatalogSourceAdapter` on top of the existing `ICatalogRepository.GetByIdsAsync` bulk lookup, and swapping the call site to use it. `CreateManufactureAsync` and `DisassembleGiftPackageAsync` inherit the fix automatically since both call `GetGiftPackageDetailAsync` internally. Behavior is unchanged: a missing ingredient still resolves to `AvailableStock = 0` / `Image = null`.

## Architecture
No new components, no module-boundary changes. This is a two-layer, additive change entirely inside two existing modules:

```
GiftPackageManufactureService.GetGiftPackageDetailAsync   (Application/Features/Logistics/UseCases/GiftPackageManufacture/Services)
        │  calls ILogisticsCatalogSource.GetCatalogItemsAsync(codes, ct)   [NEW]
        ▼
ILogisticsCatalogSource                                    (Application/Features/Logistics/Contracts)   [+1 method]
        │  implemented by
        ▼
LogisticsCatalogSourceAdapter                               (Application/Features/Catalog/Infrastructure)  [+1 method]
        │  delegates to (pre-existing, unchanged)
        ▼
ICatalogRepository.GetByIdsAsync                             (Domain/Features/Catalog)
```

`ILogisticsCatalogSource` is Logistics's outbound port; `LogisticsCatalogSourceAdapter` is Catalog's implementation of that port (already registered as `services.AddTransient<ILogisticsCatalogSource, LogisticsCatalogSourceAdapter>()` in `CatalogModule.cs` — no DI change needed). The existing single-item `GetCatalogItemAsync` is kept unchanged and continues to serve `GetTransportBoxByCodeHandler`, which is explicitly out of scope for this change and must not be touched.

## Tech Stack
- .NET 8, C# (nullable reference types enabled, `ImplicitUsings` enabled — no `using System.Linq;` / `using System.Collections.Generic;` needed in new code, matching existing files in this codebase)
- xUnit + Moq + FluentAssertions for backend unit tests
- MediatR / Clean Architecture / Vertical Slice organization (unaffected by this change — no handler, controller, or DTO changes)

## File Structure (created / modified)

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs` | Add `GetCatalogItemsAsync` member to the interface |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs` | Implement `GetCatalogItemsAsync`, reusing the existing private `ToCatalogItem` helper |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapterTests.cs` | Add unit tests for the new adapter method |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` | Replace the per-code `foreach` loop (current lines 111–118) with a single call to `GetCatalogItemsAsync` |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` | Update mocks/assertions from `GetCatalogItemAsync` to `GetCatalogItemsAsync` for ingredient-resolution tests |

No files are created; no files are deleted. No DI registration changes. No changes to `GetTransportBoxByCodeHandler`, `ICatalogRepository`, `CatalogAggregate`, or any HTTP-facing DTO/contract.

---

### task: add-batch-catalog-lookup-method

**Scope:** Add `GetCatalogItemsAsync` to `ILogisticsCatalogSource` and implement it in `LogisticsCatalogSourceAdapter`, with unit test coverage on the adapter. This task is self-contained and does not touch `GiftPackageManufactureService` or its tests — that is the next task.

#### Current state of the files this task touches

`backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs` (full current content):

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;

namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsCatalogSource
{
    Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code, CancellationToken cancellationToken);
}
```

`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs` (full current content):

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsCatalogSourceAdapter : ILogisticsCatalogSource
{
    private readonly ICatalogRepository _catalogRepository;

    public LogisticsCatalogSourceAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(item => item.Type == ProductType.Set)
            .Select(item => ToGiftPackageItem(item, fromUtc, toUtc))
            .ToList();
    }

    public async Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(code, cancellationToken);

        if (aggregate is null || aggregate.Type != ProductType.Set)
            return null;

        return ToGiftPackageItem(aggregate, fromUtc, toUtc);
    }

    public async Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(code, cancellationToken);
        return aggregate is null ? null : ToCatalogItem(aggregate);
    }

    private static LogisticsGiftPackageItem ToGiftPackageItem(
        CatalogAggregate aggregate,
        DateTime fromUtc,
        DateTime toUtc) => new()
        {
            ProductCode = aggregate.ProductCode,
            ProductName = aggregate.ProductName,
            Image = aggregate.Image,
            AvailableStock = aggregate.Stock.Available,
            TotalSoldInPeriod = aggregate.GetTotalSold(fromUtc, toUtc),
            StockMinSetup = (int)aggregate.Properties.StockMinSetup,
            OptimalStockDaysSetup = aggregate.Properties.OptimalStockDaysSetup,
        };

    private static LogisticsCatalogItem ToCatalogItem(CatalogAggregate aggregate) => new()
    {
        ProductCode = aggregate.ProductCode,
        Image = aggregate.Image,
        EshopStock = aggregate.Stock.Eshop,
        AvailableStock = aggregate.Stock.Available,
    };
}
```

`ICatalogRepository.GetByIdsAsync` (pre-existing, unchanged, in `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`), signature for reference:

```csharp
Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
```

The existing adapter test file `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapterTests.cs` (full current content):

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class LogisticsCatalogSourceAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private LogisticsCatalogSourceAdapter CreateAdapter() => new(_repository.Object);

    private static CatalogAggregate MakeAggregate(
        string code,
        ProductType type = ProductType.Set,
        string productName = "Product",
        string? image = null,
        decimal eshopStock = 0m,
        decimal erpStock = 0m,
        decimal transport = 0m,
        StockSource primaryStockSource = StockSource.Erp,
        decimal stockMinSetup = 0m,
        int optimalStockDaysSetup = 0)
    {
        return new CatalogAggregate
        {
            ProductCode = code,
            ProductName = productName,
            Type = type,
            Image = image,
            Stock = new StockData
            {
                Eshop = eshopStock,
                Erp = erpStock,
                Transport = transport,
                PrimaryStockSource = primaryStockSource,
            },
            Properties = new CatalogProperties
            {
                StockMinSetup = stockMinSetup,
                OptimalStockDaysSetup = optimalStockDaysSetup,
            },
        };
    }

    private static readonly DateTime From = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetGiftPackageSetsAsync_ReturnsOnlySetTypeProducts()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeAggregate("SET-1", ProductType.Set),
                MakeAggregate("MAT-1", ProductType.Material),
                MakeAggregate("GOODS-1", ProductType.Goods),
            });

        var result = await CreateAdapter().GetGiftPackageSetsAsync(From, To, ct);

        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("SET-1");
    }

    [Fact]
    public async Task GetGiftPackageSetsAsync_ProjectsAllFields()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(
            code: "SET-1",
            type: ProductType.Set,
            productName: "Gift Set",
            image: "image.jpg",
            erpStock: 10m,
            transport: 5m,
            primaryStockSource: StockSource.Erp,
            stockMinSetup: 3m,
            optimalStockDaysSetup: 14);

        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 1), AmountB2B = 4, AmountB2C = 2 },
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { aggregate });

        var result = (await CreateAdapter().GetGiftPackageSetsAsync(From, To, ct)).Single();

        result.ProductCode.Should().Be("SET-1");
        result.ProductName.Should().Be("Gift Set");
        result.Image.Should().Be("image.jpg");
        result.AvailableStock.Should().Be(aggregate.Stock.Available);
        result.TotalSoldInPeriod.Should().Be(aggregate.GetTotalSold(From, To));
        result.StockMinSetup.Should().Be((int)aggregate.Properties.StockMinSetup);
        result.OptimalStockDaysSetup.Should().Be(14);
    }

    [Fact]
    public async Task GetGiftPackageAsync_ReturnsNullForNonSetProduct()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MAT-1", ct))
            .ReturnsAsync(MakeAggregate("MAT-1", ProductType.Material));

        var result = await CreateAdapter().GetGiftPackageAsync("MAT-1", From, To, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGiftPackageAsync_ReturnsNullWhenProductNotFound()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", ct))
            .ReturnsAsync((CatalogAggregate?)null);

        var result = await CreateAdapter().GetGiftPackageAsync("MISSING", From, To, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGiftPackageAsync_ProjectsFieldsCorrectly()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(
            code: "SET-2",
            type: ProductType.Set,
            productName: "Premium Set",
            image: "premium.jpg",
            erpStock: 20m,
            transport: 3m,
            primaryStockSource: StockSource.Erp,
            stockMinSetup: 5m,
            optimalStockDaysSetup: 30);

        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2025, 3, 1), AmountB2B = 10, AmountB2C = 5 },
        };

        _repository.Setup(r => r.GetByIdAsync("SET-2", ct)).ReturnsAsync(aggregate);

        var result = await CreateAdapter().GetGiftPackageAsync("SET-2", From, To, ct);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("SET-2");
        result.ProductName.Should().Be("Premium Set");
        result.Image.Should().Be("premium.jpg");
        result.AvailableStock.Should().Be(aggregate.Stock.Available);
        result.TotalSoldInPeriod.Should().Be(aggregate.GetTotalSold(From, To));
        result.StockMinSetup.Should().Be((int)aggregate.Properties.StockMinSetup);
        result.OptimalStockDaysSetup.Should().Be(30);
    }

    [Fact]
    public async Task GetCatalogItemAsync_ReturnsNullWhenNotFound()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", ct))
            .ReturnsAsync((CatalogAggregate?)null);

        var result = await CreateAdapter().GetCatalogItemAsync("MISSING", ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCatalogItemAsync_ProjectsImageAndEshopStock()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(
            code: "PROD-1",
            type: ProductType.Product,
            image: "product.png",
            eshopStock: 7m,
            erpStock: 12m,
            transport: 2m,
            primaryStockSource: StockSource.Erp);

        _repository.Setup(r => r.GetByIdAsync("PROD-1", ct)).ReturnsAsync(aggregate);

        var result = await CreateAdapter().GetCatalogItemAsync("PROD-1", ct);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("PROD-1");
        result.Image.Should().Be("product.png");
        result.EshopStock.Should().Be(7m);
        result.AvailableStock.Should().Be(aggregate.Stock.Available);
    }
}
```

`LogisticsCatalogItem` (unchanged, for reference, `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsCatalogItem.cs`):

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public sealed class LogisticsCatalogItem
{
    public required string ProductCode { get; init; }
    public string? Image { get; init; }
    public decimal EshopStock { get; init; }
    public decimal AvailableStock { get; init; }
}
```

#### Step 1 — Write failing tests for the new adapter method

Edit `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapterTests.cs`. Insert the following three `[Fact]` methods immediately after `GetCatalogItemAsync_ProjectsImageAndEshopStock` (i.e. right before the final closing `}` of the class):

```csharp

    [Fact]
    public async Task GetCatalogItemsAsync_ReturnsDictionaryKeyedByProductCode()
    {
        var ct = CancellationToken.None;
        var aggregate1 = MakeAggregate(
            code: "PROD-1",
            type: ProductType.Product,
            image: "product1.png",
            eshopStock: 7m,
            erpStock: 12m);
        var aggregate2 = MakeAggregate(
            code: "PROD-2",
            type: ProductType.Product,
            image: "product2.png",
            eshopStock: 3m,
            erpStock: 5m);

        _repository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), ct))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>
            {
                ["PROD-1"] = aggregate1,
                ["PROD-2"] = aggregate2,
            });

        var result = await CreateAdapter().GetCatalogItemsAsync(new List<string> { "PROD-1", "PROD-2" }, ct);

        result.Should().HaveCount(2);
        result["PROD-1"].ProductCode.Should().Be("PROD-1");
        result["PROD-1"].Image.Should().Be("product1.png");
        result["PROD-1"].EshopStock.Should().Be(7m);
        result["PROD-1"].AvailableStock.Should().Be(aggregate1.Stock.Available);
        result["PROD-2"].ProductCode.Should().Be("PROD-2");
        result["PROD-2"].Image.Should().Be("product2.png");
    }

    [Fact]
    public async Task GetCatalogItemsAsync_OmitsCodesNotFoundInRepository()
    {
        var ct = CancellationToken.None;
        var aggregate = MakeAggregate(code: "PROD-1", type: ProductType.Product);

        _repository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), ct))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate> { ["PROD-1"] = aggregate });

        var result = await CreateAdapter().GetCatalogItemsAsync(new List<string> { "PROD-1", "MISSING" }, ct);

        result.Should().ContainSingle();
        result.Should().ContainKey("PROD-1");
        result.Should().NotContainKey("MISSING");
    }

    [Fact]
    public async Task GetCatalogItemsAsync_WithEmptyCodes_ReturnsEmptyDictionary()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), ct))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>());

        var result = await CreateAdapter().GetCatalogItemsAsync(new List<string>(), ct);

        result.Should().BeEmpty();
    }
```

Run the tests to confirm they fail to compile (the `GetCatalogItemsAsync` member does not exist yet on `LogisticsCatalogSourceAdapter`):

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsCatalogSourceAdapterTests"
```

Expected: build error `CS1061` (or similar) — `'LogisticsCatalogSourceAdapter' does not contain a definition for 'GetCatalogItemsAsync'`. This confirms the test is exercising code that doesn't exist yet (red).

#### Step 2 — Add the interface member

Edit `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs`. Replace the full file content with:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;

namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsCatalogSource
{
    Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
        IReadOnlyList<string> codes, CancellationToken cancellationToken);
}
```

(Only change: the new `GetCatalogItemsAsync` member added at the end of the interface.)

Run the build to confirm it now fails differently — `LogisticsCatalogSourceAdapter` no longer satisfies `ILogisticsCatalogSource` because it doesn't implement the new member:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `CS0535` — `'LogisticsCatalogSourceAdapter' does not implement interface member 'ILogisticsCatalogSource.GetCatalogItemsAsync(...)'`.

#### Step 3 — Implement the adapter method

Edit `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs`. Add the new method immediately after the existing `GetCatalogItemAsync` method (i.e. right before the `private static LogisticsGiftPackageItem ToGiftPackageItem(...)` helper). The method to insert:

```csharp
    public async Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
        return aggregates.ToDictionary(kv => kv.Key, kv => ToCatalogItem(kv.Value));
    }
```

The full file after this edit reads:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsCatalogSourceAdapter : ILogisticsCatalogSource
{
    private readonly ICatalogRepository _catalogRepository;

    public LogisticsCatalogSourceAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(item => item.Type == ProductType.Set)
            .Select(item => ToGiftPackageItem(item, fromUtc, toUtc))
            .ToList();
    }

    public async Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(code, cancellationToken);

        if (aggregate is null || aggregate.Type != ProductType.Set)
            return null;

        return ToGiftPackageItem(aggregate, fromUtc, toUtc);
    }

    public async Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(code, cancellationToken);
        return aggregate is null ? null : ToCatalogItem(aggregate);
    }

    public async Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
        return aggregates.ToDictionary(kv => kv.Key, kv => ToCatalogItem(kv.Value));
    }

    private static LogisticsGiftPackageItem ToGiftPackageItem(
        CatalogAggregate aggregate,
        DateTime fromUtc,
        DateTime toUtc) => new()
        {
            ProductCode = aggregate.ProductCode,
            ProductName = aggregate.ProductName,
            Image = aggregate.Image,
            AvailableStock = aggregate.Stock.Available,
            TotalSoldInPeriod = aggregate.GetTotalSold(fromUtc, toUtc),
            StockMinSetup = (int)aggregate.Properties.StockMinSetup,
            OptimalStockDaysSetup = aggregate.Properties.OptimalStockDaysSetup,
        };

    private static LogisticsCatalogItem ToCatalogItem(CatalogAggregate aggregate) => new()
    {
        ProductCode = aggregate.ProductCode,
        Image = aggregate.Image,
        EshopStock = aggregate.Stock.Eshop,
        AvailableStock = aggregate.Stock.Available,
    };
}
```

Run the build and the adapter test suite to confirm everything now passes:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsCatalogSourceAdapterTests"
```

Expected: build succeeds, all tests in `LogisticsCatalogSourceAdapterTests` pass (including the 3 new ones).

Note: at this point the wider solution will **not** yet build cleanly end-to-end from a fresh `dotnet build` at solution level only in the trivial sense that nothing else references the new member yet — that's fine, this task only adds capability, it does not require any other call site to change. `GiftPackageManufactureService` and `GetTransportBoxByCodeHandler` continue to compile unchanged since they only use the pre-existing `GetCatalogItemAsync`, which is untouched.

#### Step 4 — Commit

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs \
  src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs \
  test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapterTests.cs
git commit -m "Add batch GetCatalogItemsAsync to ILogisticsCatalogSource

Adds a batch lookup method to ILogisticsCatalogSource, implemented in
LogisticsCatalogSourceAdapter by delegating to the existing
ICatalogRepository.GetByIdsAsync bulk lookup, matching the pattern
already used by CatalogPackingProductSourceAdapter and
PurchaseMaterialCatalogAdapter. No call sites are changed yet.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S92jEwnTs5hzRKfucYBmHw"
```

---

### task: swap-giftpackage-service-to-batch-lookup

**Scope:** Replace the per-ingredient `GetCatalogItemAsync` loop in `GiftPackageManufactureService.GetGiftPackageDetailAsync` with a single call to `GetCatalogItemsAsync` (added by the previous task, already merged into `ILogisticsCatalogSource` / `LogisticsCatalogSourceAdapter`), and update `GiftPackageManufactureServiceTests.cs` accordingly. This task assumes `ILogisticsCatalogSource.GetCatalogItemsAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken)` already exists and returns `Task<IReadOnlyDictionary<string, LogisticsCatalogItem>>` (keyed by product code, missing codes simply absent from the dictionary).

#### Current state of the files this task touches

`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — the relevant excerpt (current lines 77–140, `GetGiftPackageDetailAsync`, shown in full for context; only lines 111–118 change):

```csharp
    public async Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var (actualFromDate, actualToDate, daysDiff) = ResolveDateRange(fromDate, toDate);

        // Get the basic product info from catalog
        var product = await _catalogSource.GetGiftPackageAsync(giftPackageCode, actualFromDate, actualToDate, cancellationToken);

        if (product == null)
        {
            throw new ArgumentException($"Gift package '{giftPackageCode}' not found or is not a set product");
        }

        var (dailySales, suggestedQuantity, severity, stockCoveragePercent) =
            ComputePackageMetrics(product, salesCoefficient, daysDiff);

        // Create the detailed gift package with ingredients
        var giftPackage = new GiftPackageDto
        {
            Code = product.ProductCode,
            Name = product.ProductName,
            AvailableStock = (int)product.AvailableStock,
            DailySales = dailySales,
            OverstockMinimal = product.StockMinSetup,
            OverstockOptimal = product.OptimalStockDaysSetup,
            SuggestedQuantity = suggestedQuantity,
            Severity = severity,
            StockCoveragePercent = stockCoveragePercent,
            Ingredients = new List<GiftPackageIngredientDto>()
        };

        // Load BOM (Bill of Materials) from manufacture repository
        var productParts = await _manufactureClient.GetSetPartsAsync(giftPackageCode, cancellationToken);

        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
        var ingredientCatalog = new Dictionary<string, LogisticsCatalogItem>(StringComparer.Ordinal);
        foreach (var code in ingredientCodes)
        {
            var item = await _catalogSource.GetCatalogItemAsync(code, cancellationToken);
            if (item != null)
                ingredientCatalog[code] = item;
        }

        var ingredients = new List<GiftPackageIngredientDto>();
        foreach (var part in productParts)
        {
            ingredientCatalog.TryGetValue(part.ProductCode, out var ingredientItem);

            var ingredient = new GiftPackageIngredientDto
            {
                ProductCode = part.ProductCode,
                ProductName = part.ProductName,
                RequiredQuantity = part.Amount,
                AvailableStock = (double)(ingredientItem?.AvailableStock ?? 0),
                Image = ingredientItem?.Image
            };

            ingredients.Add(ingredient);
        }

        giftPackage.Ingredients = ingredients;

        return giftPackage;
    }
```

`backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` currently has 5 tests that set up or verify `_catalogSourceMock` against the single-item `GetCatalogItemAsync` for ingredient resolution (line numbers as currently in the file):
- `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients` (lines 101–139) — two per-code `Setup(x => x.GetCatalogItemAsync("ING001", ...))` / `("ING002", ...)` calls.
- `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` (lines 156–219) — same two per-code setups.
- `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates` (lines 272–300) — one `Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), ...))` with a callback returning a synthesized item per code.
- `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient` (lines 302–327) — sets up the callback-based single-item mock and asserts `Times.Exactly(2)`. This test specifically encodes the N+1 behavior being removed and must be renamed/rewritten to assert `Times.Once` on the new batch method.
- `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage` (lines 329–353) — sets up `GetCatalogItemAsync(It.IsAny<string>(), ...)` to always return `null`.

#### Step 1 — Update the call site (write the change, this is the "red→green" step since existing tests currently mock the old method)

First, run the current test suite to confirm the baseline (all currently green before this task's edits, using the old single-item mocks):

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: all tests pass (baseline, pre-change).

Now edit `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`. Replace this block (current lines 110–118):

```csharp
        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
        var ingredientCatalog = new Dictionary<string, LogisticsCatalogItem>(StringComparer.Ordinal);
        foreach (var code in ingredientCodes)
        {
            var item = await _catalogSource.GetCatalogItemAsync(code, cancellationToken);
            if (item != null)
                ingredientCatalog[code] = item;
        }
```

with:

```csharp
        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
        var ingredientCatalog = await _catalogSource.GetCatalogItemsAsync(ingredientCodes, cancellationToken);
```

Nothing else in the file changes — the `using` list, class declaration, constructor, and every other method (`GetAvailableGiftPackagesAsync`, `CreateManufactureAsync`, `DisassembleGiftPackageAsync`, `ResolveDateRange`, `ComputePackageMetrics`, `CalculateSeverity`, `CalculateStockCoveragePercent`) are untouched. The downstream `foreach (var part in productParts)` loop that builds `GiftPackageIngredientDto` via `ingredientCatalog.TryGetValue(...)` is untouched — it works identically against the dictionary returned by `GetCatalogItemsAsync`.

Build to confirm compile success:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds (the type of `ingredientCatalog` is now inferred as `IReadOnlyDictionary<string, LogisticsCatalogItem>`, which supports `TryGetValue` identically to `Dictionary<string, LogisticsCatalogItem>`).

Run the existing test suite again — this is expected to now **fail**, because the tests still mock the old single-item method which is no longer called:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected failures: `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients`, `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems`, `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates`, `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient` (assertion `Times.Exactly(2)` now fails — the mocked `GetCatalogItemAsync` is never called), and `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage` (ingredients resolve to `AvailableStock = 0` regardless of the mock, since the unmocked `GetCatalogItemsAsync` on the `Mock<ILogisticsCatalogSource>` returns `null`/default by default rather than the intended dictionary, which will surface as either a `NullReferenceException` inside `TryGetValue` or an assertion mismatch depending on Moq's default-value behavior for the interface method). This confirms the tests are red because of the mock mismatch introduced by this task's edit, as expected before the next step fixes them.

#### Step 2 — Update the test mocks and assertions to match the new call pattern

Edit `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs`.

**2a.** In `GetGiftPackageDetailAsync_ShouldReturnGiftPackageWithIngredients` (current lines 101–139), replace:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m });
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m });
```

with:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m },
            });
```

**2b.** In `CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` (current lines 156–219), replace the identical block:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m });
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync("ING002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m });
```

with the same replacement:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LogisticsCatalogItem>
            {
                ["ING001"] = new LogisticsCatalogItem { ProductCode = "ING001", AvailableStock = 100m },
                ["ING002"] = new LogisticsCatalogItem { ProductCode = "ING002", AvailableStock = 75m },
            });
```

**2c.** In `GetGiftPackageDetailAsync_WithCustomDateRange_ShouldUseSpecifiedDates` (current lines 272–300), replace:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) =>
                new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m });
```

with:

```csharp
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> codes, CancellationToken _) =>
                (IReadOnlyDictionary<string, LogisticsCatalogItem>)codes.ToDictionary(
                    code => code,
                    code => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m }));
```

**2d.** Replace the whole `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient` test (current lines 302–327):

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) =>
                new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m });

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        _catalogSourceMock.Verify(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
```

with:

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_CallsGetCatalogItemsAsyncOncePerInvocation()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> codes, CancellationToken _) =>
                (IReadOnlyDictionary<string, LogisticsCatalogItem>)codes.ToDictionary(
                    code => code,
                    code => new LogisticsCatalogItem { ProductCode = code, AvailableStock = 50m }));

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        _catalogSourceMock.Verify(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _catalogSourceMock.Verify(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

(Note the added `Times.Never` check on the old single-item method, directly encoding FR-4's acceptance criterion that ingredient resolution no longer calls it.)

**2e.** Replace the `GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage` test (current lines 329–353):

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LogisticsCatalogItem?)null);

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert — missing ingredients get zero stock and null image
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients.Should().OnlyContain(i => i.AvailableStock == 0.0 && i.Image == null);
    }
```

with:

```csharp
    [Fact]
    public async Task GetGiftPackageDetailAsync_MissingIngredientInCatalog_ReturnsZeroStockAndNullImage()
    {
        // Arrange
        var giftPackageCode = "SET001";
        var product = CreateGiftPackageItem(giftPackageCode, "Test Gift Set 1", 100, 50);

        _catalogSourceMock
            .Setup(x => x.GetGiftPackageAsync(giftPackageCode, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _manufactureClientMock
            .Setup(x => x.GetSetPartsAsync(giftPackageCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestProductParts());
        _catalogSourceMock
            .Setup(x => x.GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, LogisticsCatalogItem>)new Dictionary<string, LogisticsCatalogItem>());

        // Act
        var result = await _service.GetGiftPackageDetailAsync(giftPackageCode);

        // Assert — missing ingredients get zero stock and null image
        result.Should().NotBeNull();
        result.Ingredients.Should().HaveCount(2);
        result.Ingredients.Should().OnlyContain(i => i.AvailableStock == 0.0 && i.Image == null);
    }
```

(`CreateTestProductParts()` returns two ingredients, `ING001` and `ING002` — see the existing helper at the bottom of the file, unchanged. An empty dictionary from `GetCatalogItemsAsync` means both are absent, matching the previous "single-item mock always returns null" behavior via `TryGetValue`'s false path.)

No other tests in this file reference `GetCatalogItemAsync`/`GetCatalogItemsAsync` for ingredient resolution — `GetAvailableGiftPackagesAsync_*` tests only mock `GetGiftPackageSetsAsync` and are unaffected; `GetGiftPackageDetailAsync_WithNonExistentProduct_ShouldThrowArgumentException` never reaches the ingredient-resolution code path (it throws before `GetSetPartsAsync` is called) and is unaffected.

Run the test suite again:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```

Expected: all tests pass (green).

#### Step 3 — Full solution verification

```bash
cd backend && dotnet build
cd backend && dotnet format --verify-no-changes
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: solution builds with no errors, `dotnet format` reports no changes needed, and the entire `Anela.Heblo.Tests` suite passes (this also re-confirms the `LogisticsCatalogSourceAdapterTests` additions from the previous task still pass alongside this task's changes, and confirms `GetTransportBoxByCodeHandler` — untouched, still calling the single-item `GetCatalogItemAsync` — continues to compile and its own tests, if any, are unaffected).

If `dotnet format` reports changes, apply them:

```bash
cd backend && dotnet format
```

then re-run `dotnet build` and the test suite to confirm nothing broke.

#### Step 4 — Commit

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs \
  test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs
git commit -m "Replace per-ingredient catalog lookup with batch call in GiftPackageManufactureService

GetGiftPackageDetailAsync now resolves ingredient catalog data with a
single GetCatalogItemsAsync call instead of one GetCatalogItemAsync
call per distinct ingredient code, closing the N+1 pattern that also
affected CreateManufactureAsync and DisassembleGiftPackageAsync (both
call GetGiftPackageDetailAsync internally). Behavior for callers is
unchanged, including the existing 'missing ingredient -> zero stock,
null image' fallback.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S92jEwnTs5hzRKfucYBmHw"
```

---

## Self-Review

Checked against the spec (`spec.r1.md`), arch review, and design documents:

- **FR-1** (add batch method to `ILogisticsCatalogSource`) — covered by `add-batch-catalog-lookup-method`, Step 2. Signature matches exactly: `Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken)`.
- **FR-2** (implement in `LogisticsCatalogSourceAdapter`) — covered by `add-batch-catalog-lookup-method`, Step 3. Delegates to `_catalogRepository.GetByIdsAsync` exactly once per call, reuses the existing private `ToCatalogItem`, mirrors `CatalogPackingProductSourceAdapter.GetByCodesAsync`'s `.ToDictionary` style as the arch review permits. Empty-input and missing-code behavior verified by the three new adapter tests in Step 1/3.
- **FR-3** (replace the loop in `GetGiftPackageDetailAsync`) — covered by `swap-giftpackage-service-to-batch-lookup`, Step 1. The exact before/after diff matches the spec's proposed replacement verbatim. The downstream `foreach` loop is explicitly left untouched.
- **FR-4** (update existing unit tests) — covered by `swap-giftpackage-service-to-batch-lookup`, Step 2, sub-steps 2a–2e. All five call sites that referenced `GetCatalogItemAsync` for ingredient resolution are updated; the renamed `GetGiftPackageDetailAsync_CallsGetCatalogItemsAsyncOncePerInvocation` test asserts `Times.Once` on the batch method and `Times.Never` on the single-item method; the "missing ingredient" test is preserved with an empty-dictionary mock.
- **NFR-1** (O(N) → O(1) round-trips) — structurally guaranteed by the Step 1 replacement: exactly one `GetCatalogItemsAsync` call replaces the `foreach` loop, and this is asserted by the `Times.Once` check in the renamed test.
- **NFR-2** (security — no change) — no new inputs, auth surface, or data exposure introduced by either task; not applicable to add further test coverage for.
- **Out of scope items** (`GetTransportBoxByCodeHandler`, `GetCatalogItemAsync` removal, `ICatalogRepository`/`CatalogAggregate` changes, benchmarking, `IManufactureClient` batching) — none of these files are touched by either task; explicitly called out in both tasks' scope notes.

Placeholder / hand-waving scan: every code block in both tasks is complete, compilable C# with no `TODO`, `...`, or "similar to above" references — each task restates the full current file content it edits and the full new content, so a developer reading only one task's section has everything needed. Type and method names are consistent across both tasks and match the interface signature exactly (`GetCatalogItemsAsync`, `IReadOnlyList<string> codes`, `CancellationToken cancellationToken`, `IReadOnlyDictionary<string, LogisticsCatalogItem>`).

Cross-task consistency check: Task 1 introduces `GetCatalogItemsAsync` on `ILogisticsCatalogSource`/`LogisticsCatalogSourceAdapter`; Task 2 consumes that exact signature at the call site and in test mocks (`It.IsAny<IReadOnlyList<string>>()`, `IReadOnlyDictionary<string, LogisticsCatalogItem>` return type) — no drift between the two tasks' assumed API shape.

No issues found requiring further correction.
