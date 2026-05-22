using System.Globalization;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Tests")]

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShoptetApiExpeditionListSource : IPickingListSource
{
    // ShoptetOrderClient is the only implementation of IEshopOrderClient — safe to cast
    // within this adapter assembly to access expedition-specific methods not on the interface.
    private readonly ShoptetOrderClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;
    private readonly IGiftSettingRepository _giftSettings;
    private readonly Func<ExpeditionProtocolData, byte[]> _generateDocument;

    public ShoptetApiExpeditionListSource(
        IEshopOrderClient client,
        TimeProvider timeProvider,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        IGiftSettingRepository giftSettings,
        Func<ExpeditionProtocolData, byte[]>? generateDocument = null)
    {
        _client = (ShoptetOrderClient)client;
        _timeProvider = timeProvider;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _giftSettings = giftSettings;
        _generateDocument = generateDocument ?? ExpeditionProtocolDocument.Generate;
    }

    public async Task<PrintPickingListResult> CreatePickingList(
        PrintPickingListRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch all orders with the requested source state (paginate)
        var allOrders = await FetchAllOrdersAsync(request.SourceStateId, cancellationToken);

        // 2. Filter to carriers requested; group by carrier
        var carrierFilter = request.Carriers.Any()
            ? new HashSet<Carriers>(request.Carriers)
            : null;

        var ordersByMethod = new Dictionary<ShippingMethod, List<(string Code, string ShippingGuid, decimal? TotalWithVat, string? CurrencyCode)>>();
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

        var exportedFiles = new List<string>();
        var processedCodes = new List<string>();
        var timestamp = _timeProvider.GetFilenameTimestamp();

        // Load carrier cooling matrix once for the entire run
        var allSettings = await _carrierCooling.GetAllAsync(cancellationToken);
        var coolingMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.Cooling);

        // Load gift setting once for the entire run
        var giftSetting = await _giftSettings.GetAsync(cancellationToken);

        foreach (var (method, orders) in ordersByMethod)
        {
            var sorted = orders;
            var maxItems = method.MaxItems;
            var maxOrders = method.MaxOrders;
            var carrierDisplayName = method.DisplayName;

            // 3. Fetch all order details for this carrier upfront, then batch greedily by item count.
            //    This ensures batches are split based on how much content fits on a printed page,
            //    rather than by an arbitrary order count.
            var allExpeditionOrders = new List<ExpeditionOrder>();
            foreach (var (code, shippingGuid, totalWithVat, currencyCode) in sorted)
            {
                var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);
                var expeditionOrder = MapToExpeditionOrder(detail);
                expeditionOrder.CarrierCooling = ResolveCarrierCooling(shippingGuid, coolingMatrix);
                expeditionOrder.GiftBadgeText = ResolveGiftBadge(totalWithVat, currencyCode, giftSetting);
                allExpeditionOrders.Add(expeditionOrder);
                processedCodes.Add(code);
            }

            // Greedy batching: accumulate orders until adding the next would exceed maxItems.
            // A single order with more items than maxItems always becomes its own batch.
            var currentBatch = new List<ExpeditionOrder>();
            var currentItemCount = 0;
            var batchIndex = 0;

            async Task FlushBatchAsync(List<ExpeditionOrder> batch)
            {
                // Enrich with stock counts, warehouse positions, and cooling from catalog.
                // Positions are only applied where the Shoptet API left them blank (set components).
                var productCodes = batch.SelectMany(o => o.Items).Select(i => i.ProductCode).Distinct();
                var stockByCode = new Dictionary<string, decimal>();
                var locationByCode = new Dictionary<string, string>();
                var coolingByCode = new Dictionary<string, Cooling>();
                var priceByCode = new Dictionary<string, decimal>();
                foreach (var productCode in productCodes)
                {
                    var entry = await _catalog.GetByIdAsync(productCode, cancellationToken);
                    if (entry != null)
                    {
                        stockByCode[productCode] = entry.Stock.Eshop;
                        if (!string.IsNullOrEmpty(entry.Location))
                            locationByCode[productCode] = entry.Location;
                        coolingByCode[productCode] = entry.Properties.Cooling;
                        if (entry.PriceWithVat is > 0)
                            priceByCode[productCode] = entry.PriceWithVat.Value;
                    }
                }
                ApplyEnrichment(
                    batch.SelectMany(o => o.Items),
                    stockByCode,
                    locationByCode,
                    coolingByCode,
                    priceByCode);

                var fileName = $"{timestamp}_{method.Name}_{batchIndex}.pdf";
                var listId = Path.GetFileNameWithoutExtension(fileName);

                var data = new ExpeditionProtocolData
                {
                    CarrierDisplayName = carrierDisplayName,
                    ListId = listId,
                    Orders = batch,
                };

                var pdfBytes = _generateDocument(data);
                var filePath = Path.Combine(Path.GetTempPath(), fileName);
                await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);
                exportedFiles.Add(filePath);

                if (onBatchFilesReady != null)
                    await onBatchFilesReady(new List<string> { filePath });
            }

            foreach (var order in allExpeditionOrders)
            {
                var orderItemCount = order.Items.Count;

                if ((currentItemCount + orderItemCount > maxItems || currentBatch.Count >= maxOrders) && currentBatch.Count > 0)
                {
                    // Flush current batch before starting a new one
                    await FlushBatchAsync(currentBatch);
                    batchIndex++;
                    currentBatch = new List<ExpeditionOrder>();
                    currentItemCount = 0;
                }

                currentBatch.Add(order);
                currentItemCount += orderItemCount;
            }

            // Flush any remaining orders
            if (currentBatch.Count > 0)
            {
                await FlushBatchAsync(currentBatch);
            }
        }

        // 5. Update order states if requested
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
