using System.Net;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Loads a single Shoptet order for the Balení packing screen. Reuses the expedition
/// mapper to expand product sets and compute carrier-aware cooling status.
/// </summary>
public class ShoptetApiPackingOrderClient : IPackingOrderClient
{
    private readonly ShoptetOrderClient _orderClient;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;
    private readonly ILogger<ShoptetApiPackingOrderClient> _logger;
    private readonly int _defaultItemWeightGrams;
    private readonly ShoptetOrdersSettings _orderSettings;

    public ShoptetApiPackingOrderClient(
        ShoptetOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings,
        IOptions<ShoptetOrdersSettings> orderSettings)
    {
        _orderClient = orderClient;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
        _orderSettings = orderSettings.Value;
    }

    public async Task<int> GetOrdersBeingPackedCountAsync(CancellationToken ct = default)
    {
        var response = await _orderClient.GetOrdersByStatusAsync(_orderSettings.PackingStateId, page: 1, ct);
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

        // Read the order status via the base detail endpoint. The status object is not
        // reliably present on the ?include= expedition response, so reuse the proven
        // path used by the order-blocking feature.
        var statusId = await _orderClient.GetOrderStatusIdAsync(code, ct);

        var order = ShoptetApiExpeditionListSource.MapToExpeditionOrder(detail);

        // Carrier cooling — resolve from the (carrier, delivery handling) matrix.
        var settings = await _carrierCooling.GetAllAsync(ct);
        var matrix = settings.ToDictionary(s => (s.Carrier, s.DeliveryHandling), s => s.Cooling);
        order.CarrierCooling = ShoptetApiExpeditionListSource.ResolveCarrierCooling(
            detail.Shipping?.Guid ?? string.Empty, matrix);

        // Per-product cooling and images from the catalog.
        var productCodes = order.Items.Select(i => i.ProductCode).Distinct().ToList();
        var catalogItems = await _catalog.GetByIdsAsync(productCodes, ct);
        var coolingByCode = catalogItems.ToDictionary(kv => kv.Key, kv => kv.Value.Properties.Cooling);

        var weightByCode = catalogItems.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.GrossWeight.HasValue ? (int?)((int)kv.Value.GrossWeight.Value)
                : kv.Value.NetWeight.HasValue ? (int)kv.Value.NetWeight.Value
                : (int?)null);

        ShoptetApiExpeditionListSource.ApplyEnrichment(
            order.Items,
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            coolingByCode);

        var items = order.Items.Select(i =>
        {
            if (!weightByCode.TryGetValue(i.ProductCode, out var w) || w is null)
            {
                _logger.LogWarning(
                    "Product {ProductCode} has no weight in catalog; using default {Default}g",
                    i.ProductCode, _defaultItemWeightGrams);
            }

            return new PackingOrderItem
            {
                Name = i.Name,
                Quantity = i.Quantity,
                ImageUrl = catalogItems.TryGetValue(i.ProductCode, out var c) ? c.Image : null,
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
