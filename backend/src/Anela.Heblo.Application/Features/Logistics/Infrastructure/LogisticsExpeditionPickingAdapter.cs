using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.Logistics.Picking;

namespace Anela.Heblo.Application.Features.Logistics.Infrastructure;

// Bridge adapter: implements ExpeditionList's consumer-owned IExpeditionPickingSource
// and delegates to the Logistics-namespaced IPickingListSource (bound to
// ShoptetApiExpeditionListSource by AddShoptetApiAdapter).
internal sealed class LogisticsExpeditionPickingAdapter : IExpeditionPickingSource
{
    private readonly IPickingListSource _inner;

    public LogisticsExpeditionPickingAdapter(IPickingListSource inner) =>
        _inner = inner;

    public async Task<ExpeditionPickingResult> CreatePickingListAsync(
        ExpeditionPickingRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default)
    {
        var innerRequest = new PrintPickingListRequest
        {
            Carriers = request.Carriers,
            SourceStateId = request.SourceStateId,
            DesiredStateId = request.DesiredStateId,
            NoteStateId = request.NoteStateId,
            ChangeOrderState = request.ChangeOrderState,
            SendToPrinter = request.SendToPrinter,
        };

        var inner = await _inner.CreatePickingList(innerRequest, onBatchFilesReady, cancellationToken);

        return new ExpeditionPickingResult
        {
            ExportedFiles = inner.ExportedFiles,
            TotalCount = inner.TotalCount,
            SkippedCount = inner.SkippedCount,
        };
    }
}
