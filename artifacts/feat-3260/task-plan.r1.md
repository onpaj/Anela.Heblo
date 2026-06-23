# Decouple ShoptetApiPackingOrderClient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Remove direct cross-module repository dependencies from ShoptetApiPackingOrderClient by introducing consumer-owned contracts and provider-side adapters.

**Architecture:** ShoptetOrders module owns IPackingProductSource and IPackingCarrierCoolingSource contracts. Catalog module implements the product source adapter. CarrierCooling module implements the carrier cooling adapter. The adapter client is updated to use the new contracts.

**Tech Stack:** .NET 8, C#, xUnit, Moq, FluentAssertions, Clean Architecture

---

### task: define-contracts

- [ ] Create `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs`
- [ ] Create `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs`
- [ ] Build Application project to confirm no errors

**File 1: `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs`**
```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes, CancellationToken ct = default);
}

public class PackingProductInfo
{
    public Cooling Cooling { get; init; }
    public int? WeightGrams { get; init; }
    public string? ImageUrl { get; init; }
}
```

**File 2: `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs`**
```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default);
}

public class PackingCarrierCoolingSetting
{
    public string CarrierName { get; init; } = string.Empty;
    public string DeliveryHandlingName { get; init; } = string.Empty;
    public Cooling Cooling { get; init; }
}
```

Build check:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

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

### task: implement-carrier-cooling-adapter

- [ ] Create `CarrierCooling/Infrastructure/` directory and adapter file
- [ ] Add `using` and DI registration to `CarrierCoolingModule.cs`
- [ ] Create unit test file
- [ ] Run tests to confirm green

**File to create: `src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs`**

Note: `CarrierCooling/Infrastructure/` directory does not exist yet — create it.

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Infrastructure;

internal sealed class CarrierCoolingPackingCarrierCoolingAdapter : IPackingCarrierCoolingSource
{
    private readonly ICarrierCoolingRepository _repository;

    public CarrierCoolingPackingCarrierCoolingAdapter(ICarrierCoolingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default)
    {
        var settings = await _repository.GetAllAsync(ct);
        return settings.Select(s => new PackingCarrierCoolingSetting
        {
            CarrierName = s.Carrier.ToString(),
            DeliveryHandlingName = s.DeliveryHandling.ToString(),
            Cooling = s.Cooling,
        }).ToList();
    }
}
```

**Modify `CarrierCoolingModule.cs`** — add `using` at top and registration:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
```
```csharp
// Cross-module contract: CarrierCooling implements ShoptetOrders' IPackingCarrierCoolingSource via adapter.
// DI registration is owned by the provider (CarrierCooling), not the consumer (ShoptetOrders).
services.AddTransient<IPackingCarrierCoolingSource, CarrierCoolingPackingCarrierCoolingAdapter>();
```

**New test file: `test/Anela.Heblo.Tests/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapterTests.cs`**
```csharp
using Anela.Heblo.Application.Features.CarrierCooling.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.CarrierCooling.Infrastructure;

public class CarrierCoolingPackingCarrierCoolingAdapterTests
{
    [Fact]
    public async Task GetAllAsync_MapsCarrierNameAsEnumString()
    {
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CarrierCoolingSetting(Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "test") });
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle();
        result[0].CarrierName.Should().Be("PPL");
        result[0].DeliveryHandlingName.Should().Be("NaRuky");
        result[0].Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenRepositoryEmpty()
    {
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CarrierCoolingSetting>());
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().BeEmpty();
    }
}
```

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "CarrierCoolingPackingCarrierCoolingAdapterTests"
```

---

### task: update-packing-order-client

- [ ] Add new `ResolveCarrierCooling` overload to `ShoptetApiExpeditionListSource.cs`
- [ ] Replace `ShoptetApiPackingOrderClient.cs` entirely with updated implementation
- [ ] Update `ShoptetApiPackingOrderClientTests.cs` to use new contract mocks
- [ ] Run existing client tests to confirm green

**1. Add new ResolveCarrierCooling overload to `ShoptetApiExpeditionListSource.cs`**

Add this alongside the existing `ResolveCarrierCooling` method:
```csharp
internal static Cooling ResolveCarrierCooling(
    string shippingGuid,
    IReadOnlyDictionary<(string CarrierName, string DeliveryHandlingName), Cooling> matrix)
{
    if (!ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
        return Cooling.None;

    var handling = ShippingMethodRegistry.ResolveDeliveryHandling(method);
    if (!handling.HasValue)
        return Cooling.None;

    return matrix.TryGetValue((method.Carrier.ToString(), handling.Value.ToString()), out var cooling)
        ? cooling
        : Cooling.None;
}
```

**2. Replace `ShoptetApiPackingOrderClient.cs` entirely:**

Full file content:
```csharp
using System.Net;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetApiPackingOrderClient : IPackingOrderClient
{
    private readonly IShoptetExpeditionOrderSource _orderClient;
    private readonly IPackingProductSource _productSource;
    private readonly IPackingCarrierCoolingSource _carrierCoolingSource;
    private readonly ILogger<ShoptetApiPackingOrderClient> _logger;
    private readonly int _defaultItemWeightGrams;
    private readonly ShoptetOrdersSettings _orderSettings;

    public ShoptetApiPackingOrderClient(
        IShoptetExpeditionOrderSource orderClient,
        IPackingProductSource productSource,
        IPackingCarrierCoolingSource carrierCoolingSource,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings,
        IOptions<ShoptetOrdersSettings> orderSettings)
    {
        _orderClient = orderClient;
        _productSource = productSource;
        _carrierCoolingSource = carrierCoolingSource;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
        _orderSettings = orderSettings.Value;
    }

    public async Task<int> GetOrdersBeingPackedCountAsync(CancellationToken ct = default)
    {
        var response = await _orderClient.GetOrdersByStatusAsync(_orderSettings.PackingStateId, page: 1, ct);
        return response.Data.Paginator.TotalCount;
    }

    public async Task<int> GetOrdersBeingProcessedCountAsync(CancellationToken ct = default)
    {
        var response = await _orderClient.GetOrdersByStatusAsync(_orderSettings.ProcessingStateId, page: 1, ct);
        return response.Data.Paginator.TotalCount;
    }

    public async Task<PackingOrder?> GetPackingOrderAsync(string code, CancellationToken ct = default)
    {
        ExpeditionOrderDetail detail;
        try
        {
            detail = await _orderClient.GetExpeditionOrderDetailAsync(code, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var statusId = await _orderClient.GetOrderStatusIdAsync(code, ct);
        var order = ShoptetApiExpeditionListSource.MapToExpeditionOrder(detail);

        var coolingSettings = await _carrierCoolingSource.GetAllAsync(ct);
        var coolingMatrix = coolingSettings.ToDictionary(
            s => (s.CarrierName, s.DeliveryHandlingName), s => s.Cooling);
        order.CarrierCooling = ShoptetApiExpeditionListSource.ResolveCarrierCooling(
            detail.Shipping?.Guid ?? string.Empty, coolingMatrix);

        var productCodes = order.Items.Select(i => i.ProductCode).Distinct().ToList();
        var catalogItems = await _productSource.GetByCodesAsync(productCodes, ct);
        var coolingByCode = catalogItems.ToDictionary(kv => kv.Key, kv => kv.Value.Cooling);

        ShoptetApiExpeditionListSource.ApplyEnrichment(
            order.Items,
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            coolingByCode);

        var items = order.Items.Select(i =>
        {
            catalogItems.TryGetValue(i.ProductCode, out var info);
            var w = info?.WeightGrams;
            if (w is null)
            {
                _logger.LogWarning(
                    "Product {ProductCode} has no weight in catalog; using default {Default}g",
                    i.ProductCode, _defaultItemWeightGrams);
            }

            return new PackingOrderItem
            {
                Name = i.Name,
                Quantity = i.Quantity,
                ImageUrl = info?.ImageUrl,
                SetName = i.IsFromSet ? i.SetName : null,
                WeightGrams = w ?? _defaultItemWeightGrams,
            };
        }).ToList();

        var deliveryAddress = detail.DeliveryAddress ?? detail.BillingAddress;
        var shippingStreet = deliveryAddress is null
            ? null
            : CombineStreetAndHouseNumber(deliveryAddress.Street, deliveryAddress.HouseNumber);

        return new PackingOrder
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = detail.Shipping?.Name ?? string.Empty,
            Cooling = order.CarrierCooling,
            IsCooled = order.IsCooled,
            StatusId = statusId,
            IsEligibleForPacking = statusId == _orderSettings.PackingStateId,
            CustomerNote = string.IsNullOrWhiteSpace(order.CustomerRemark) ? null : order.CustomerRemark,
            EshopNote = string.IsNullOrWhiteSpace(order.EshopRemark) ? null : order.EshopRemark,
            ShippingStreet = shippingStreet,
            ShippingCity = NormalizeAddressField(deliveryAddress?.City),
            ShippingZip = NormalizeAddressField(deliveryAddress?.Zip),
            Items = items,
        };
    }

    private static string? NormalizeAddressField(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CombineStreetAndHouseNumber(string? street, string? houseNumber)
    {
        var hasStreet = !string.IsNullOrWhiteSpace(street);
        var hasHouseNumber = !string.IsNullOrWhiteSpace(houseNumber);

        if (hasStreet && hasHouseNumber)
            return $"{street} {houseNumber}".Trim();
        if (hasStreet)
            return street!.Trim();
        if (hasHouseNumber)
            return houseNumber!.Trim();
        return null;
    }
}
```

**3. Update `ShoptetApiPackingOrderClientTests.cs`**

Replace the `ICatalogRepository`/`ICarrierCoolingRepository` mock helpers with:
```csharp
private static IPackingProductSource ProductSourceWith(params (string code, PackingProductInfo info)[] items)
{
    var mock = new Mock<IPackingProductSource>();
    mock.Setup(s => s.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(items.ToDictionary(i => i.code, i => i.info));
    return mock.Object;
}

private static IPackingCarrierCoolingSource CoolingSourceWith(params PackingCarrierCoolingSetting[] settings)
{
    var mock = new Mock<IPackingCarrierCoolingSource>();
    mock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(settings);
    return mock.Object;
}

private static ShoptetApiPackingOrderClient BuildSut(
    ShoptetOrderClient orderClient,
    IPackingProductSource productSource,
    IPackingCarrierCoolingSource coolingSource,
    int defaultWeightGrams = 500)
{
    var settings = Options.Create(new ShoptetApiSettings { DefaultItemWeightGrams = defaultWeightGrams });
    var orderSettings = Options.Create(new ShoptetOrdersSettings());
    var logger = NullLogger<ShoptetApiPackingOrderClient>.Instance;
    return new ShoptetApiPackingOrderClient(orderClient, productSource, coolingSource, logger, settings, orderSettings);
}
```

Update each test to use the new helpers. Example for `GetPackingOrderAsync_MapsHeaderAndItems`:
```csharp
var productSource = ProductSourceWith(
    ("P001", new PackingProductInfo { ImageUrl = "https://img/p001.jpg", Cooling = Cooling.None }));
var sut = BuildSut(orderClient, productSource, CoolingSourceWith());
```

For the cooling test `GetPackingOrderAsync_ComputesCooling_FromCarrierMatrixAndCatalog`:
```csharp
var productSource = ProductSourceWith(
    ("P001", new PackingProductInfo { Cooling = Cooling.L1 }));
var coolingSource = CoolingSourceWith(
    new PackingCarrierCoolingSetting { CarrierName = "PPL", DeliveryHandlingName = "NaRuky", Cooling = Cooling.L1 });
var sut = BuildSut(orderClient, productSource, coolingSource);
```

For weight tests, use:
```csharp
var productSource = ProductSourceWith(
    ("P001", new PackingProductInfo { WeightGrams = 350, Cooling = Cooling.None }));
```

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "ShoptetApiPackingOrderClientTests"
```

---

### task: add-boundary-tests

- [ ] Locate `ModuleBoundariesTests.cs` and add allowlists for ShoptetApi-to-Catalog and ShoptetApi-to-Logistics cross-module references
- [ ] Add two new `ModuleBoundaryRule` entries to `Rules()`
- [ ] Run the boundary tests; if unexpected violations surface, add them to the appropriate allowlist and re-run

In `ModuleBoundariesTests.cs`, after the existing allowlist declarations, add:

```csharp
// Allowlist for ShoptetApi Adapters -> Catalog.
// ShoptetApiExpeditionListSource retains ICatalogRepository injection — out of scope.
// Track as follow-up; remove when ShoptetApiExpeditionListSource is decoupled.
private static readonly HashSet<string> ShoptetApiAdaptersCatalogAllowlist =
    new(StringComparer.Ordinal)
    {
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.ICatalogRepository",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.CatalogAggregate",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Catalog.CatalogProperties",
    };

// Allowlist for ShoptetApi Adapters -> Logistics.
// ShoptetApiExpeditionListSource retains ICarrierCoolingRepository — out of scope.
// ShippingMethodRegistry/ShippingMethod reference Carriers/DeliveryHandling by design.
private static readonly HashSet<string> ShoptetApiAdaptersLogisticsAllowlist =
    new(StringComparer.Ordinal)
    {
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.ICarrierCoolingRepository",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.CarrierCoolingSetting",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.Carriers",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShoptetApiExpeditionListSource -> Anela.Heblo.Domain.Features.Logistics.DeliveryHandling",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShippingMethodRegistry -> Anela.Heblo.Domain.Features.Logistics.Carriers",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShippingMethodRegistry -> Anela.Heblo.Domain.Features.Logistics.DeliveryHandling",
        "Anela.Heblo.Adapters.ShoptetApi.Expedition.ShippingMethod -> Anela.Heblo.Domain.Features.Logistics.Carriers",
    };
```

Add two new rules to `Rules()`:
```csharp
new ModuleBoundaryRule(
    Name: "ShoptetApi Adapters -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Adapters.ShoptetApi",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: ShoptetApiAdaptersCatalogAllowlist,
    InspectedAssembly: "Anela.Heblo.Adapters.ShoptetApi"),

new ModuleBoundaryRule(
    Name: "ShoptetApi Adapters -> Logistics",
    InspectedNamespacePrefix: "Anela.Heblo.Adapters.ShoptetApi",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Logistics",
        "Anela.Heblo.Application.Features.Logistics",
        "Anela.Heblo.Persistence.Logistics",
    },
    Allowlist: ShoptetApiAdaptersLogisticsAllowlist,
    InspectedAssembly: "Anela.Heblo.Adapters.ShoptetApi"),
```

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "ModuleBoundariesTests"
```

If the test fails with unexpected violations, add those types to the appropriate allowlist and re-run.

---

### task: verify-build

- [ ] Full solution build succeeds
- [ ] Format check passes with no changes
- [ ] All tests pass

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

All three commands must succeed with no errors.
