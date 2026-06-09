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

    public Task<string> FlushAsync(
        IReadOnlyList<ExpeditionOrder> batch,
        ShippingMethod method,
        int batchIndex,
        string timestamp,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
