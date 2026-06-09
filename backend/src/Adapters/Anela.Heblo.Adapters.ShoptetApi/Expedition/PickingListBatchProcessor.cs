using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal sealed class PickingListBatchProcessor
{
    internal const string CoolingMarkerValue = "CHLAZENE";
    internal const int CoolingAdditionalFieldIndex = 6;

    private readonly ICatalogRepository _catalog;
    private readonly ShoptetOrderClient _client;
    private readonly Func<ExpeditionProtocolData, byte[]> _generateDocument;
    // Logger parameter is intentionally typed as the base ILogger (not ILogger<PickingListBatchProcessor>)
    // so the log category remains "ShoptetApiExpeditionListSource". Ops alerting filters on that category;
    // changing it would silently break dashboards. Do not "clean up" to ILogger<T>.
    private readonly ILogger _logger;

    public PickingListBatchProcessor(
        ICatalogRepository catalog,
        ShoptetOrderClient client,
        Func<ExpeditionProtocolData, byte[]> generateDocument,
        ILogger logger)
    {
        _catalog = catalog;
        _client = client;
        _generateDocument = generateDocument;
        _logger = logger;
    }

    public async Task<string> FlushAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        ShippingMethod method,
        int batchIndex,
        string timestamp,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        await EnrichBatchAsync(batch, cancellationToken);

        var fileName = $"{timestamp}_{method.Name}_{batchIndex}.pdf";
        var listId = Path.GetFileNameWithoutExtension(fileName);

        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = method.DisplayName,
            ListId = listId,
            Orders = batch.ToList(),
        };

        var pdfBytes = _generateDocument(data);
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);

        await WriteCoolingMarkersAsync(batch, cancellationToken);

        if (onBatchFilesReady != null)
            await onBatchFilesReady(new List<string> { filePath });

        return filePath;
    }

    private async Task EnrichBatchAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        CancellationToken cancellationToken)
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
        ShoptetApiExpeditionListSource.ApplyEnrichment(
            batch.SelectMany(o => o.Items),
            stockByCode,
            locationByCode,
            coolingByCode,
            priceByCode);
    }

    private async Task WriteCoolingMarkersAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        CancellationToken cancellationToken)
    {
        foreach (var order in batch)
        {
            if (!order.IsCooled)
                continue;

            try
            {
                await _client.SetAdditionalFieldAsync(
                    order.Code,
                    CoolingAdditionalFieldIndex,
                    CoolingMarkerValue,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to set Shoptet additionalField[{Index}]={Value} for order {OrderCode}; PDF print continues.",
                    CoolingAdditionalFieldIndex,
                    CoolingMarkerValue,
                    order.Code);
            }
        }
    }
}
