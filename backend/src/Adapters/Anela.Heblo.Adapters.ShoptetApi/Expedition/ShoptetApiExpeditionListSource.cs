using System.Globalization;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShoptetApiExpeditionListSource : IPickingListSource
{
    private readonly ShoptetApiExpeditionClient _client;

    private static readonly IReadOnlyList<ShippingMethod> ShippingList = new List<ShippingMethod>
    {
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY", Id = 21 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT", Id = 15 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK", Id = 385 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_CHLAZENY", Id = 370 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY", Id = 373 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK_CHLAZENY", Id = 388 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_ZDARMA", Id = 487 },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA", Id = 481 },
        new() { Carrier = Carriers.PPL, Name = "PPL_DO_RUKY", Id = 6 },
        new() { Carrier = Carriers.PPL, Name = "PPL_PARCELSHOP", Id = 80 },
        new() { Carrier = Carriers.PPL, Name = "PPL_EXPORT", Id = 86 },
        new() { Carrier = Carriers.PPL, Name = "PPL_DO_RUKY_CHLAZENY", Id = 358 },
        new() { Carrier = Carriers.PPL, Name = "PPL_PARCELSHOP_CHLAZENY", Id = 361 },
        new() { Carrier = Carriers.PPL, Name = "PPL_EXPORT_CHLAZENY", Id = 379 },
        new() { Carrier = Carriers.GLS, Name = "GLS_DO_RUKY", Id = 97 },
        new() { Carrier = Carriers.GLS, Name = "GLS_EXPORT", Id = 109 },
        new() { Carrier = Carriers.GLS, Name = "GLS_PARCELSHOP", Id = 489 },
        new() { Carrier = Carriers.Osobak, Name = "OSOBAK", Id = 4, PageSize = 1 },
    };

    private static readonly Dictionary<int, ShippingMethod> ShippingById =
        ShippingList.ToDictionary(s => s.Id, s => s);

    public ShoptetApiExpeditionListSource(ShoptetApiExpeditionClient client)
    {
        _client = client;
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

        var ordersByCarrier = new Dictionary<Carriers, List<(string Code, int ShippingId)>>();
        foreach (var order in allOrders)
        {
            var shippingId = order.Shipping?.Id ?? 0;
            if (!ShippingById.TryGetValue(shippingId, out var method))
                continue;
            if (carrierFilter != null && !carrierFilter.Contains(method.Carrier))
                continue;

            if (!ordersByCarrier.TryGetValue(method.Carrier, out var list))
            {
                list = new List<(string, int)>();
                ordersByCarrier[method.Carrier] = list;
            }

            list.Add((order.Code, shippingId));
        }

        var exportedFiles = new List<string>();
        var processedCodes = new List<string>();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        foreach (var (carrier, orders) in ordersByCarrier)
        {
            // Sort by shippingId so same method types are together
            var sorted = orders.OrderBy(o => o.ShippingId).ToList();

            // Determine page size from the first shipping method encountered for this carrier
            var pageSize = ShippingById.TryGetValue(sorted[0].ShippingId, out var sm) ? sm.PageSize : 8;
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
                    var detail = await _client.GetOrderDetailAsync(code, cancellationToken);
                    expeditionOrders.Add(MapToExpeditionOrder(detail));
                    processedCodes.Add(code);
                }

                // 4b-c. Generate PDF
                var data = new ExpeditionProtocolData
                {
                    CarrierDisplayName = carrierDisplayName,
                    Orders = expeditionOrders,
                };

                var pdfBytes = ExpeditionProtocolDocument.Generate(data);

                // 4d. Write to temp path
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
                await _client.UpdateOrderStatusAsync(code, request.DesiredStateId, cancellationToken);
        }

        return new PrintPickingListResult
        {
            ExportedFiles = exportedFiles,
            TotalCount = processedCodes.Count,
        };
    }

    private async Task<List<Model.ExpeditionOrderSummary>> FetchAllOrdersAsync(int statusId, CancellationToken ct)
    {
        var all = new List<Model.ExpeditionOrderSummary>();
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
                    Quantity = int.TryParse(i.Amount, out var qty) ? qty : 0,
                    StockCount = i.StockStatus?.StockCount ?? 0,
                    UnitPrice = decimal.TryParse(i.ItemPriceWithVat, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m,
                })
                .ToList(),
        };
    }
}
