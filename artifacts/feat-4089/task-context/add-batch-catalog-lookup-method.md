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

