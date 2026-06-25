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

        // status is a base field on GET /api/orders/{code} and is returned on the
        // ?include=stockLocation,notes expedition response, so no extra call is needed.
        var statusId = detail.Status?.Id ?? 0;
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
