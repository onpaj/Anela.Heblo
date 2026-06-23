using System.Globalization;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Application.Features.Logistics.Picking;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Xcc;
using Microsoft.Extensions.Logging;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Tests")]

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShoptetApiExpeditionListSource : IPickingListSource
{
    private readonly IShoptetExpeditionOrderSource _client;
    private readonly TimeProvider _timeProvider;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;
    private readonly IGiftSettingRepository _giftSettings;
    private readonly ILogger<ShoptetApiExpeditionListSource> _logger;
    private readonly Func<ExpeditionProtocolData, byte[]> _generateDocument;

    public ShoptetApiExpeditionListSource(
        IShoptetExpeditionOrderSource client,
        TimeProvider timeProvider,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        IGiftSettingRepository giftSettings,
        ILogger<ShoptetApiExpeditionListSource> logger,
        Func<ExpeditionProtocolData, byte[]>? generateDocument = null)
    {
        _client = client;
        _timeProvider = timeProvider;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _giftSettings = giftSettings;
        _logger = logger;
        _generateDocument = generateDocument ?? ExpeditionProtocolDocument.Generate;
    }

    public async Task<PrintPickingListResult> CreatePickingList(
        PrintPickingListRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default)
    {
        var allOrders = await FetchAllOrdersAsync(request.SourceStateId, cancellationToken);
        var ordersByMethod = BuildOrdersByMethod(allOrders, request.Carriers);

        var exportedFiles = new List<string>();
        var processedCodes = new List<string>();
        var timestamp = _timeProvider.GetFilenameTimestamp();

        var allSettings = await _carrierCooling.GetAllAsync(cancellationToken);
        var coolingMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.Cooling);
        var coolingTextMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.CoolingText);

        var giftSetting = await _giftSettings.GetAsync(cancellationToken);
        var processor = new PickingListBatchProcessor(_catalog, _client, _generateDocument, _logger);

        foreach (var (method, orders) in ordersByMethod)
            await BatchAndFlushAsync(method, orders, coolingMatrix, coolingTextMatrix,
                giftSetting, processor, timestamp, exportedFiles, processedCodes, onBatchFilesReady, cancellationToken);

        if (request.ChangeOrderState)
        {
            foreach (var code in processedCodes)
                await _client.UpdateStatusAsync(code, request.DesiredStateId, cancellationToken);
        }

        return new PrintPickingListResult
        {
            ExportedFiles = exportedFiles,
            TotalCount = processedCodes.Count,
        };
    }

    private async Task<List<OrderSummary>> FetchAllOrdersAsync(int statusId, CancellationToken ct)
    {
        var all = new List<OrderSummary>();
        var page = 1;
        while (true)
        {
            var response = await _client.GetOrdersByStatusAsync(statusId, page, ct);
            all.AddRange(response.Data.Orders);

            if (page >= response.Data.Paginator.PageCount)
                break;
            page++;
        }
        return all;
    }

    private static Dictionary<ShippingMethod, List<(string Code, string ShippingGuid, decimal? TotalWithVat, string? CurrencyCode)>>
        BuildOrdersByMethod(
            List<OrderSummary> allOrders,
            IList<Carriers> requestedCarriers)
    {
        var carrierFilter = requestedCarriers.Any()
            ? new HashSet<Carriers>(requestedCarriers)
            : null;

        var ordersByMethod = new Dictionary<ShippingMethod, List<(string, string, decimal?, string?)>>();
        foreach (var order in allOrders)
        {
            var shippingGuid = order.Shipping?.Guid;
            if (string.IsNullOrEmpty(shippingGuid) || !ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
                continue;
            if (carrierFilter != null && !carrierFilter.Contains(method.Carrier))
                continue;

            if (!ordersByMethod.TryGetValue(method, out var list))
            {
                list = new List<(string, string, decimal?, string?)>();
                ordersByMethod[method] = list;
            }

            list.Add((order.Code, shippingGuid, order.Price?.WithVat, order.Price?.CurrencyCode));
        }
        return ordersByMethod;
    }

    private async Task BatchAndFlushAsync(
        ShippingMethod method,
        List<(string Code, string ShippingGuid, decimal? TotalWithVat, string? CurrencyCode)> orders,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling> coolingMatrix,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), string?> coolingTextMatrix,
        GiftSetting giftSetting,
        PickingListBatchProcessor processor,
        string timestamp,
        List<string> exportedFiles,
        List<string> processedCodes,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        var allExpeditionOrders = new List<ExpeditionOrder>();
        foreach (var (code, shippingGuid, totalWithVat, currencyCode) in orders)
        {
            var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);
            var expeditionOrder = MapToExpeditionOrder(detail);
            expeditionOrder.CarrierCooling = ResolveCarrierCooling(shippingGuid, coolingMatrix);
            expeditionOrder.CoolingText = ResolveCarrierCoolingText(shippingGuid, coolingTextMatrix);
            expeditionOrder.GiftBadgeText = ResolveGiftBadge(totalWithVat, currencyCode, giftSetting);
            allExpeditionOrders.Add(expeditionOrder);
            processedCodes.Add(code);
        }

        var currentBatch = new List<ExpeditionOrder>();
        var currentItemCount = 0;
        var batchIndex = 0;

        foreach (var order in allExpeditionOrders)
        {
            var orderItemCount = order.Items.Count;

            if ((currentItemCount + orderItemCount > method.MaxItems || currentBatch.Count >= method.MaxOrders) && currentBatch.Count > 0)
            {
                var overflowPath = await processor.FlushAsync(
                    currentBatch, method, batchIndex, timestamp, onBatchFilesReady, cancellationToken);
                exportedFiles.Add(overflowPath);
                batchIndex++;
                currentBatch = new List<ExpeditionOrder>();
                currentItemCount = 0;
            }

            currentBatch.Add(order);
            currentItemCount += orderItemCount;
        }

        if (currentBatch.Count > 0)
        {
            var finalPath = await processor.FlushAsync(
                currentBatch, method, batchIndex, timestamp, onBatchFilesReady, cancellationToken);
            exportedFiles.Add(finalPath);
        }
    }

    internal static ExpeditionOrder MapToExpeditionOrder(Model.ExpeditionOrderDetail detail)
    {
        var addr = detail.DeliveryAddress ?? detail.BillingAddress;
        var address = addr != null
            ? $"{addr.Street} {addr.HouseNumber}, {addr.Zip} {addr.City}".Trim()
            : string.Empty;

        var shipAddr = detail.DeliveryAddress ?? detail.BillingAddress;
        var customerName = !string.IsNullOrWhiteSpace(shipAddr?.FullName)
            ? shipAddr.FullName
            : !string.IsNullOrWhiteSpace(shipAddr?.Company)
                ? shipAddr.Company
                : !string.IsNullOrWhiteSpace(detail.FullName)
                    ? detail.FullName
                    : detail.Company ?? string.Empty;

        return new ExpeditionOrder
        {
            Code = detail.Code,
            CustomerName = customerName,
            Address = address,
            Phone = detail.Phone ?? string.Empty,
            CustomerRemark = detail.Notes?.CustomerRemark,
            EshopRemark = detail.Notes?.EshopRemark,
            Items = MapOrderItems(detail),
        };
    }

    internal static void ApplyEnrichment(
        IEnumerable<ExpeditionOrderItem> items,
        Dictionary<string, decimal> stockByCode,
        Dictionary<string, string> locationByCode,
        Dictionary<string, Cooling> coolingByCode,
        Dictionary<string, decimal>? priceByCode = null)
    {
        foreach (var item in items)
        {
            if (stockByCode.TryGetValue(item.ProductCode, out var stock))
                item.StockCount = stock;
            if (string.IsNullOrEmpty(item.WarehousePosition) && locationByCode.TryGetValue(item.ProductCode, out var location))
                item.WarehousePosition = location;
            if (coolingByCode.TryGetValue(item.ProductCode, out var cooling))
                item.Cooling = cooling;
            if (item.UnitPrice == 0m && priceByCode != null && priceByCode.TryGetValue(item.ProductCode, out var price))
                item.UnitPrice = price;
        }
    }

    internal static Cooling ResolveCarrierCooling(
        string shippingGuid,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling> matrix)
    {
        if (!ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
            return Cooling.None;

        var handling = ShippingMethodRegistry.ResolveDeliveryHandling(method);
        if (!handling.HasValue)
            return Cooling.None;

        return matrix.TryGetValue((method.Carrier, handling.Value), out var cooling)
            ? cooling
            : Cooling.None;
    }

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

    internal static string? ResolveCarrierCoolingText(
        string shippingGuid,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), string?> matrix)
    {
        if (!ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
            return null;

        var handling = ShippingMethodRegistry.ResolveDeliveryHandling(method);
        if (!handling.HasValue)
            return null;

        return matrix.TryGetValue((method.Carrier, handling.Value), out var text)
            ? text
            : null;
    }

    internal static string? ResolveGiftBadge(
        decimal? totalWithVat,
        string? currencyCode,
        GiftSetting setting)
    {
        if (!setting.IsEnabled) return null;
        if (!string.Equals(currencyCode, "CZK", StringComparison.OrdinalIgnoreCase)) return null;
        if (totalWithVat is null || totalWithVat < setting.ThresholdCzk) return null;
        return setting.Text;
    }

    internal static List<ExpeditionOrderItem> MapOrderItems(Model.ExpeditionOrderDetail detail)
    {
        var result = new List<ExpeditionOrderItem>();

        var setItemsByParentId = detail.Completion
            .Where(c => string.Equals(c.ItemType, "product-set-item", StringComparison.OrdinalIgnoreCase)
                     && c.ParentProductSetItemId.HasValue)
            .GroupBy(c => c.ParentProductSetItemId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in detail.Items)
        {
            if (string.Equals(item.ItemType, "product", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ItemType, "gift", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ExpeditionOrderItem
                {
                    ProductCode = item.Code ?? string.Empty,
                    Name = item.Name ?? string.Empty,
                    Variant = item.VariantName ?? string.Empty,
                    WarehousePosition = item.WarehousePosition ?? string.Empty,
                    Quantity = (int)(item.Amount ?? 0),
                    StockDemand = item.StockStatus?.AllDemand ?? 0,
                    Unit = item.Unit ?? string.Empty,
                    UnitPrice = decimal.TryParse(item.ItemPriceWithVat, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m,
                });
            }
            else if (string.Equals(item.ItemType, "product-set", StringComparison.OrdinalIgnoreCase))
            {
                var setQuantity = (int)(item.Amount ?? 1);
                if (!setItemsByParentId.TryGetValue(item.ItemId, out var setComponents))
                    continue;

                foreach (var component in setComponents)
                {
                    result.Add(new ExpeditionOrderItem
                    {
                        ProductCode = component.Code ?? string.Empty,
                        Name = component.Name ?? string.Empty,
                        Variant = component.VariantName ?? string.Empty,
                        WarehousePosition = string.Empty, // Shoptet completion API does not return stock locations for set components
                        Quantity = (int)(component.Amount ?? 0) * setQuantity,
                        Unit = component.Unit ?? string.Empty,
                        UnitPrice = 0m,
                        IsFromSet = true,
                        SetName = item.Name,
                    });
                }
            }
        }

        return result;
    }
}
