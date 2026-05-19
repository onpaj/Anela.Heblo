using System.Net;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;

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

    public ShoptetApiPackingOrderClient(
        IEshopOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling)
    {
        _orderClient = orderClient as ShoptetOrderClient
            ?? throw new InvalidOperationException(
                $"{nameof(IEshopOrderClient)} must be {nameof(ShoptetOrderClient)} " +
                $"but got {orderClient.GetType().Name}.");
        _catalog = catalog;
        _carrierCooling = carrierCooling;
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

        ShoptetApiExpeditionListSource.ApplyEnrichment(
            order.Items,
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            coolingByCode);

        var items = order.Items.Select(i => new PackingOrderItem
        {
            Name = i.Name,
            Quantity = i.Quantity,
            ImageUrl = catalogItems.TryGetValue(i.ProductCode, out var c) ? c.Image : null,
            SetName = i.IsFromSet ? i.SetName : null,
        }).ToList();

        return new PackingOrder
        {
            Code = order.Code,
            CustomerName = order.CustomerName,
            ShippingMethodName = detail.Shipping?.Name ?? string.Empty,
            Cooling = order.CarrierCooling,
            IsCooled = order.IsCooled,
            StatusId = statusId,
            CustomerNote = string.IsNullOrWhiteSpace(order.CustomerRemark) ? null : order.CustomerRemark,
            EshopNote = string.IsNullOrWhiteSpace(order.EshopRemark) ? null : order.EshopRemark,
            Items = items,
        };
    }
}
