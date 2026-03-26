namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(
        PrintPickingListRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default);
}
