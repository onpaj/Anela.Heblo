using System.Globalization;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")]

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShoptetApiExpeditionListSource : IPickingListSource
{
    // ShoptetOrderClient is the only implementation of IEshopOrderClient — safe to cast
    // within this adapter assembly to access expedition-specific methods not on the interface.
    private readonly ShoptetOrderClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ICatalogRepository _catalog;

    // GUIDs discovered via: GET /api/eshop?include=shippingMethods (production store 269953/anela.cz)
    private static readonly IReadOnlyList<ShippingMethod> ShippingList = new List<ShippingMethod>
    {
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY",              Id = 21,  Guids = ["f6610d4d-578d-11e9-beb1-002590dad85e"] }, // Zásilkovna (do ruky)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT",               Id = 15,  Guids = ["7878c138-578d-11e9-beb1-002590dad85e", "389cea0b-40f1-11ea-beb1-002590dad85e"] }, // Zásilkovna Z-Point (retail + VO)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK",           Id = 385, Guids = ["a6d9a6ce-0ede-11ee-b534-2a01067a25a9"] }, // Zásilkovna (do ruky) SK
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_CHLAZENY",     Id = 370, Guids = ["34d3f7d4-166f-11ee-b534-2a01067a25a9"] }, // Zásilkovna chlazený balík (do ruky)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY",      Id = 373, Guids = ["bac58d34-166f-11ee-b534-2a01067a25a9"] }, // Zásilkovna Z-Point chlazený balík - ZDARMA od 1500,-
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK_CHLAZENY",  Id = 388, Guids = ["75123baa-1671-11ee-b534-2a01067a25a9"] }, // Zásilkovna SK chlazený balík (do ruky)
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_ZDARMA",        Id = 487, Guids = ["79b9ef95-5e46-11f0-ae6d-9237d29d7242"] }, // Zásilkovna Z-Point - DOPRAVA ZDARMA
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA", Id = 481, Guids = ["db9bf927-5e44-11f0-ae6d-9237d29d7242"] }, // Zásilkovna Z-Point - PLATÍTE POUZE CHLADÍTKO
        new() { Carrier = Carriers.PPL,        Name = "PPL_DO_RUKY",                     Id = 6,   Guids = ["2ec88ea7-3fb0-11e2-a723-705ab6a2ba75", "389ce5b4-40f1-11ea-beb1-002590dad85e"] }, // PPL (do ruky) (retail + VO)
        new() { Carrier = Carriers.PPL,        Name = "PPL_PARCELSHOP",                  Id = 80,  Guids = ["c4e6c287-9a85-11ea-beb1-002590dad85e", "83372e07-9a86-11ea-beb1-002590dad85e"] }, // PPL ParcelShop (retail + VO)
        new() { Carrier = Carriers.PPL,        Name = "PPL_EXPORT",                      Id = 86,  Guids = ["f17a0a12-0ebe-11eb-933a-002590dad85e", "2fd96b91-1508-11eb-933a-002590dad85e"] }, // PPL Export (retail + VO)
        new() { Carrier = Carriers.PPL,        Name = "PPL_DO_RUKY_CHLAZENY",            Id = 358, Guids = ["05ea842d-166a-11ee-b534-2a01067a25a9"] }, // PPL chlazený balík (do ruky) - ZDARMA od 3000,-
        new() { Carrier = Carriers.PPL,        Name = "PPL_PARCELSHOP_CHLAZENY",         Id = 361, Guids = ["0d10802f-166c-11ee-b534-2a01067a25a9"] }, // PPL ParcelShop chlazený balík - ZDARMA od 1500,-
        new() { Carrier = Carriers.PPL,        Name = "PPL_EXPORT_CHLAZENY",             Id = 379, Guids = ["de70f0e4-1670-11ee-b534-2a01067a25a9"] }, // PPL Export chlazený balík (zahraničí)
        new() { Carrier = Carriers.GLS,        Name = "GLS_DO_RUKY",                     Id = 97,  Guids = ["138ec07f-0119-11ec-a39f-002590dc5efc", "b7e787c5-011d-11ec-a39f-002590dc5efc"] }, // GLS (do ruky) (retail + VO)
        new() { Carrier = Carriers.GLS,        Name = "GLS_EXPORT",                      Id = 109, Guids = ["c06835e6-165e-11ec-a39f-002590dc5efc", "bbbe7223-4ea8-11ec-a39f-002590dc5efc"] }, // GLS Export (retail + VO)
        new() { Carrier = Carriers.GLS,        Name = "GLS_PARCELSHOP",                  Id = 489, Guids = ["49b79aec-0118-11ec-a39f-002590dc5efc"] }, // GLS ParcelShop
        new() { Carrier = Carriers.Osobak,     Name = "OSOBAK",                          Id = 4,   Guids = ["8fdb2c89-3fae-11e2-a723-705ab6a2ba75", "389ce19e-40f1-11ea-beb1-002590dad85e"], MaxOrders = 1, MaxItems = int.MaxValue }, // Osobní odběr (retail + VO)
    };

    private static readonly Dictionary<string, ShippingMethod> ShippingByGuid =
        ShippingList
            .SelectMany(s => s.Guids.Select(g => (Guid: g, Method: s)))
            .ToDictionary(x => x.Guid, x => x.Method);

    public ShoptetApiExpeditionListSource(IEshopOrderClient client, TimeProvider timeProvider, ICatalogRepository catalog)
    {
        _client = (ShoptetOrderClient)client;
        _timeProvider = timeProvider;
        _catalog = catalog;
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

        var ordersByCarrier = new Dictionary<Carriers, List<(string Code, string ShippingGuid)>>();
        foreach (var order in allOrders)
        {
            var shippingGuid = order.Shipping?.Guid;
            if (string.IsNullOrEmpty(shippingGuid) || !ShippingByGuid.TryGetValue(shippingGuid, out var method))
                continue;
            if (carrierFilter != null && !carrierFilter.Contains(method.Carrier))
                continue;

            if (!ordersByCarrier.TryGetValue(method.Carrier, out var list))
            {
                list = new List<(string, string)>();
                ordersByCarrier[method.Carrier] = list;
            }

            list.Add((order.Code, shippingGuid));
        }

        var exportedFiles = new List<string>();
        var processedCodes = new List<string>();
        var timestamp = _timeProvider.GetFilenameTimestamp();

        foreach (var (carrier, orders) in ordersByCarrier)
        {
            // Sort by shippingGuid so same method types are together
            var sorted = orders.OrderBy(o => o.ShippingGuid).ToList();

            // Determine batch limits from the first shipping method for this carrier
            var maxItems = ShippingByGuid.TryGetValue(sorted[0].ShippingGuid, out var sm) ? sm.MaxItems : 20;
            var maxOrders = sm?.MaxOrders ?? int.MaxValue;
            var carrierDisplayName = carrier.ToString();

            // 3. Fetch all order details for this carrier upfront, then batch greedily by item count.
            //    This ensures batches are split based on how much content fits on a printed page,
            //    rather than by an arbitrary order count.
            var allExpeditionOrders = new List<ExpeditionOrder>();
            foreach (var (code, _) in sorted)
            {
                var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);
                allExpeditionOrders.Add(MapToExpeditionOrder(detail));
                processedCodes.Add(code);
            }

            // Greedy batching: accumulate orders until adding the next would exceed maxItems.
            // A single order with more items than maxItems always becomes its own batch.
            var currentBatch = new List<ExpeditionOrder>();
            var currentItemCount = 0;
            var batchIndex = 0;

            async Task FlushBatchAsync(List<ExpeditionOrder> batch)
            {
                // Enrich with stock counts from catalog
                var productCodes = batch.SelectMany(o => o.Items).Select(i => i.ProductCode).Distinct();
                var stockByCode = new Dictionary<string, decimal>();
                foreach (var productCode in productCodes)
                {
                    var entry = await _catalog.GetByIdAsync(productCode, cancellationToken);
                    if (entry != null)
                        stockByCode[productCode] = entry.Stock.Available;
                }
                foreach (var item in batch.SelectMany(o => o.Items))
                {
                    if (stockByCode.TryGetValue(item.ProductCode, out var stock))
                        item.StockCount = stock;
                }

                var data = new ExpeditionProtocolData
                {
                    CarrierDisplayName = carrierDisplayName,
                    Orders = batch,
                };

                var pdfBytes = ExpeditionProtocolDocument.Generate(data);
                var fileName = $"{timestamp}_{carrier}_{batchIndex}.pdf";
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

    private static ExpeditionOrder MapToExpeditionOrder(Model.ExpeditionOrderDetail detail)
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
            Items = MapOrderItems(detail),
        };
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
            if (string.Equals(item.ItemType, "product", StringComparison.OrdinalIgnoreCase))
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
                        WarehousePosition = string.Empty,
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
