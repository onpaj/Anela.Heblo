### task: implement-catalog-adapter

- [ ] Create `src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs`
- [ ] Add `using` and DI registration to `CatalogModule.cs`
- [ ] Create unit test file
- [ ] Run tests to confirm green

**File to create: `src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs`**
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogPackingProductSourceAdapter : IPackingProductSource
{
    private readonly ICatalogRepository _repository;

    public CatalogPackingProductSourceAdapter(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes, CancellationToken ct = default)
    {
        var items = await _repository.GetByIdsAsync(productCodes, ct);
        return items.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var a = kv.Value;
                return new PackingProductInfo
                {
                    Cooling = a.Properties.Cooling,
                    WeightGrams = a.GrossWeight.HasValue ? (int?)((int)a.GrossWeight.Value)
                                : a.NetWeight.HasValue  ? (int?)((int)a.NetWeight.Value)
                                : null,
                    ImageUrl = a.Image,
                };
            });
    }
}
```

**Modify `CatalogModule.cs`** — add `using` at top and registration after existing cross-module adapter registrations:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
```
```csharp
// Cross-module contract: Catalog implements ShoptetOrders' IPackingProductSource via adapter.
// DI registration is owned by the provider (Catalog), not the consumer (ShoptetOrders).
services.AddTransient<IPackingProductSource, CatalogPackingProductSourceAdapter>();
```

**New test file: `test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapterTests.cs`**
```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogPackingProductSourceAdapterTests
{
    private static Mock<ICatalogRepository> CatalogWith(params CatalogAggregate[] items)
    {
        var mock = new Mock<ICatalogRepository>();
        mock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToDictionary(i => i.ProductCode, i => i));
        return mock;
    }

    [Fact]
    public async Task GetByCodesAsync_MapsCoolingFromProperties()
    {
        var catalog = CatalogWith(new CatalogAggregate { ProductCode = "P1", Properties = new CatalogProperties { Cooling = Cooling.L2 } });
        var sut = new CatalogPackingProductSourceAdapter(catalog.Object);

        var result = await sut.GetByCodesAsync(["P1"]);

        result["P1"].Cooling.Should().Be(Cooling.L2);
    }

    [Fact]
    public async Task GetByCodesAsync_UsesGrossWeightWhenSet()
    {
        var catalog = CatalogWith(new CatalogAggregate { ProductCode = "P1", GrossWeight = 400.5, NetWeight = 300.0, Properties = new CatalogProperties() });
        var sut = new CatalogPackingProductSourceAdapter(catalog.Object);

        var result = await sut.GetByCodesAsync(["P1"]);

        result["P1"].WeightGrams.Should().Be(400);
    }

    [Fact]
    public async Task GetByCodesAsync_FallsBackToNetWeightWhenGrossWeightNull()
    {
        var catalog = CatalogWith(new CatalogAggregate { ProductCode = "P1", GrossWeight = null, NetWeight = 250.0, Properties = new CatalogProperties() });
        var sut = new CatalogPackingProductSourceAdapter(catalog.Object);

        var result = await sut.GetByCodesAsync(["P1"]);

        result["P1"].WeightGrams.Should().Be(250);
    }

    [Fact]
    public async Task GetByCodesAsync_ReturnsNullWeightWhenBothAbsent()
    {
        var catalog = CatalogWith(new CatalogAggregate { ProductCode = "P1", GrossWeight = null, NetWeight = null, Properties = new CatalogProperties() });
        var sut = new CatalogPackingProductSourceAdapter(catalog.Object);

        var result = await sut.GetByCodesAsync(["P1"]);

        result["P1"].WeightGrams.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodesAsync_MapsImageUrl()
    {
        var catalog = CatalogWith(new CatalogAggregate { ProductCode = "P1", Image = "https://img/p1.jpg", Properties = new CatalogProperties() });
        var sut = new CatalogPackingProductSourceAdapter(catalog.Object);

        var result = await sut.GetByCodesAsync(["P1"]);

        result["P1"].ImageUrl.Should().Be("https://img/p1.jpg");
    }
}
```

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "CatalogPackingProductSourceAdapterTests"
```

---

