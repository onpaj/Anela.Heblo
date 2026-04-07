using System.Globalization;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc;

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
        new() { Carrier = Carriers.Osobak,     Name = "OSOBAK",                          Id = 4,   Guids = ["8fdb2c89-3fae-11e2-a723-705ab6a2ba75", "389ce19e-40f1-11ea-beb1-002590dad85e"], PageSize = 1 }, // Osobní odběr (retail + VO)
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

            // Determine page size from the first shipping method encountered for this carrier
            var pageSize = ShippingByGuid.TryGetValue(sorted[0].ShippingGuid, out var sm) ? sm.PageSize : 8;
            var carrierDisplayName = carrier.ToString();

            // 3. Batch
            var batches = sorted
                .Select((o, i) => (o, i))
                .GroupBy(x => x.i / pageSize)
                .Select(g => g.Select(x => x.o).ToList())
                .ToList();

            var batchIndex = 0;
            foreach (var batch in batches)
            {
                // 4a. Fetch detail for each order in the batch
                var expeditionOrders = new List<ExpeditionOrder>();
                foreach (var (code, _) in batch)
                {
                    var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);
                    expeditionOrders.Add(MapToExpeditionOrder(detail));
                    processedCodes.Add(code);
                }

                // 4b. Enrich with stock counts from catalog (in-memory, no extra I/O)
                var allCodes = expeditionOrders.SelectMany(o => o.Items).Select(i => i.ProductCode).Distinct();
                var stockByCode = new Dictionary<string, decimal>();
                foreach (var productCode in allCodes)
                {
                    var entry = await _catalog.GetByIdAsync(productCode, cancellationToken);
                    if (entry != null)
                        stockByCode[productCode] = entry.Stock.Available;
                }
                foreach (var item in expeditionOrders.SelectMany(o => o.Items))
                {
                    if (stockByCode.TryGetValue(item.ProductCode, out var stock))
                        item.StockCount = stock;
                }

                // 4b-c. Generate PDF
                var data = new ExpeditionProtocolData
                {
                    CarrierDisplayName = carrierDisplayName,
                    Orders = expeditionOrders,
                };

                var pdfBytes = ExpeditionProtocolDocument.Generate(data);

                // 4d. Write to temp; delivery is the caller's responsibility via onBatchFilesReady
                var fileName = $"{timestamp}_{carrier}_{batchIndex}.pdf";
                var filePath = Path.Combine(Path.GetTempPath(), fileName);
                await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);
                exportedFiles.Add(filePath);

                // 4e. Invoke callback per batch
                if (onBatchFilesReady != null)
                    await onBatchFilesReady(new List<string> { filePath });

                batchIndex++;
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
        var addr = detail.BillingAddress;
        var address = addr != null
            ? $"{addr.Street} {addr.HouseNumber}, {addr.Zip} {addr.City}".Trim()
            : string.Empty;

        return new ExpeditionOrder
        {
            Code = detail.Code,
            CustomerName = detail.FullName ?? string.Empty,
            Address = address,
            Phone = detail.Phone ?? string.Empty,
            Items = detail.Items
                .Where(i => string.Equals(i.ItemType, "product", StringComparison.OrdinalIgnoreCase))
                .Select(i => new ExpeditionOrderItem
                {
                    ProductCode = i.Code ?? string.Empty,
                    Name = i.Name ?? string.Empty,
                    Variant = i.VariantName ?? string.Empty,
                    WarehousePosition = i.WarehousePosition ?? string.Empty,
                    Quantity = (int)(i.Amount ?? 0),
                    Unit = i.Unit ?? string.Empty,
                    UnitPrice = decimal.TryParse(i.ItemPriceWithVat, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m,
                })
                .ToList(),
        };
    }
}
