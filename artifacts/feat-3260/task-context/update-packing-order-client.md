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

